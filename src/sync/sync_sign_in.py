"""The ingest credential a scheduled run uses when nobody is at the machine: one
interactive WorkOS sign-in, then a refresh token the run exchanges for as many
short-lived access tokens as it needs. Why this rather than a machine-to-machine
client or a shared secret is ADR 0013."""

from __future__ import annotations

import base64
import hashlib
import json
import secrets
import webbrowser
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlencode, urlparse
from urllib.request import Request, urlopen

from atomic_file import write_replacing


# A token is replaced while this much of its life is left, so one never expires
# between being handed out and the request that carries it reaching Azure.
EXPIRY_MARGIN = timedelta(seconds=60)


class TokenEndpointError(RuntimeError):
    """The sign-in service refused or could not answer a token request."""

    def __init__(self, status: int, error_code: str | None = None, detail: str = "") -> None:
        super().__init__(f"Sign-in service returned HTTP {status}"
                         f"{f' ({error_code})' if error_code else ''}"
                         f"{f': {detail}' if detail else ''}")
        self.status = status
        self.error_code = error_code


class SyncSignInRequired(Exception):
    """WorkOS no longer accepts the saved refresh token, so the interactive sign-in
    is due. Named apart from a fault because only this one waits on the athlete."""


def token_endpoint(authority: str) -> str:
    return f"{authority.rstrip('/')}/oauth2/token"


def authorize_endpoint(authority: str) -> str:
    return f"{authority.rstrip('/')}/oauth2/authorize"


def naming_resource(params: dict[str, str], resource: str | None) -> dict[str, str]:
    """WorkOS puts its registered default Resource Indicator in `aud`, which is what
    the ingest endpoint validates against. Only a machine whose default is some other
    resource names one itself."""
    return params if not resource else {**params, "resource": resource}


def post_form(url: str, form: dict[str, str]) -> dict[str, Any]:
    """The live token endpoint. Errors arrive as TokenEndpointError so that the one
    answer meaning "sign in again" can be told from the ones meaning "try later"."""
    request = Request(url, data=urlencode(form).encode("utf-8"), method="POST",
                      headers={"Content-Type": "application/x-www-form-urlencoded"})
    try:
        with urlopen(request, timeout=30) as response:
            body = response.read().decode("utf-8", "replace")
        try:
            return json.loads(body)
        except ValueError:
            # The answer may have carried a rotated refresh token that is now
            # unreadable and unsaved. WorkOS honours the spent one for another thirty
            # seconds, so saying so is worth more than a parser's complaint.
            raise TokenEndpointError(
                response.status, None,
                "the response could not be read; if the sync stops working, sign in"
                " again") from None
    except HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:2000]
        try:
            code = json.loads(detail).get("error")
        except ValueError:
            code = None
        # The body can quote the refresh token back; only the error code is kept.
        raise TokenEndpointError(error.code, code, "" if code else "request refused") from None


class SyncSignIn:
    """Issues access tokens for ingest from one saved, rotating refresh token."""

    def __init__(
        self,
        token_file: Path,
        authority: str,
        client_id: str,
        resource: str | None = None,
        exchange: Callable[[str, dict[str, str]], dict[str, Any]] = post_form,
        now: Callable[[], datetime] = lambda: datetime.now(timezone.utc),
    ) -> None:
        self._token_file = token_file
        self._authority = authority
        self._client_id = client_id
        self._resource = resource
        self._exchange = exchange
        self._now = now
        self._issued: tuple[str, datetime] | None = None

    def _saved_refresh_token(self) -> str:
        try:
            recorded = json.loads(self._token_file.read_text(encoding="utf-8"))
            return recorded["refreshToken"]
        except (OSError, ValueError, KeyError, TypeError):
            raise SyncSignInRequired(
                f"No usable Coach sign in is saved at {self._token_file};"
                " sign in once on this machine") from None

    def _save_refresh_token(self, refresh_token: str) -> None:
        write_replacing(self._token_file, json.dumps({"refreshToken": refresh_token}))

    def access_token(self) -> str:
        """The current access token, obtained or renewed as the run needs it. A long
        catch-up outlives any one token, so this is called per request, not per run."""
        if self._issued is not None:
            token, expires_at = self._issued
            if self._now() + EXPIRY_MARGIN < expires_at:
                return token
        form = {
            "grant_type": "refresh_token",
            "client_id": self._client_id,
            "refresh_token": self._saved_refresh_token(),
        }
        try:
            issued = self._exchange(token_endpoint(self._authority),
                                    naming_resource(form, self._resource))
        except TokenEndpointError as refused:
            # Only invalid_grant means the saved token itself is spent; every other
            # answer is a fault, which retries and reads as a failure.
            if refused.error_code != "invalid_grant":
                raise
            raise SyncSignInRequired(
                f"WorkOS no longer accepts the saved sign-in at {self._token_file},"
                " so the interactive sign in is due") from None
        # WorkOS retires the token just used, so the replacement is saved before the
        # access token is handed out: losing it costs an interactive sign-in.
        # Saved first, and only when one came back: WorkOS retires the token just used,
        # so anything that could fail between its arrival and its write risks locking
        # the machine out. A response carrying no replacement means the saved one still
        # stands, which is not an error.
        if issued.get("refresh_token"):
            self._save_refresh_token(issued["refresh_token"])
        access_token = issued.get("access_token")
        if not access_token:
            raise TokenEndpointError(200, None, "the response carried no access token")
        # An unstated lifetime is treated as already spent, so the next request renews
        # rather than carrying a token of unknown age.
        self._issued = (access_token,
                        self._now() + timedelta(seconds=int(issued.get("expires_in") or 0)))
        return access_token


DEFAULT_REDIRECT_PORT = 8765
# Garmin's token lives in the user profile for the reasons in ADR 0006, and this one
# is the same kind of credential held for the same unattended run.
DEFAULT_TOKEN_FILE = "~/.coach-sync/workos_tokens.json"


def pkce_pair() -> tuple[str, str]:
    """A fresh verifier and its S256 challenge. WorkOS advertises S256 only."""
    verifier = base64.urlsafe_b64encode(secrets.token_bytes(64)).rstrip(b"=").decode("ascii")
    digest = hashlib.sha256(verifier.encode("ascii")).digest()
    return verifier, base64.urlsafe_b64encode(digest).rstrip(b"=").decode("ascii")


def catch_redirect(port: int) -> dict[str, list[str]]:
    """Serves exactly one request on the loopback address and returns its query."""
    caught: dict[str, list[str]] = {}

    class Callback(BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802 - the name BaseHTTPRequestHandler dispatches to
            caught.update(parse_qs(urlparse(self.path).query))
            answered = "error" not in caught
            self.send_response(200 if answered else 400)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.end_headers()
            self.wfile.write(("Coach sync is signed in. You can close this tab."
                              if answered else "Sign-in failed. Check the terminal.")
                             .encode("utf-8"))

        def log_message(self, *_: Any) -> None:
            # The request line carries the authorization code; it is logged nowhere.
            pass

    with HTTPServer(("127.0.0.1", port), Callback) as server:
        server.handle_request()
    return caught


def sign_in(
    token_file: Path,
    authority: str,
    client_id: str,
    resource: str | None = None,
    port: int = DEFAULT_REDIRECT_PORT,
    exchange: Callable[[str, dict[str, str]], dict[str, Any]] = post_form,
    open_browser: Callable[[str], Any] = webbrowser.open,
    await_redirect: Callable[[int], dict[str, list[str]]] = catch_redirect,
    announce: Callable[[str], None] = print,
) -> None:
    """The interactive sign-in, run by hand once per machine. It leaves behind a
    refresh token, which is all an unattended run needs from then on."""
    verifier, challenge = pkce_pair()
    state = secrets.token_urlsafe(24)
    redirect_uri = f"http://127.0.0.1:{port}/callback"
    query = {
        "response_type": "code",
        "client_id": client_id,
        "redirect_uri": redirect_uri,
        "code_challenge": challenge,
        "code_challenge_method": "S256",
        # offline_access is what makes the response carry a refresh token at all.
        "scope": "openid profile email offline_access",
        "state": state,
    }
    url = f"{authorize_endpoint(authority)}?{urlencode(naming_resource(query, resource))}"
    announce(f"Opening the sign-in page. If no browser opens, visit:\n{url}")
    open_browser(url)
    answer = await_redirect(port)
    if "error" in answer:
        raise RuntimeError(f"Sign-in was refused: {answer['error'][0]}")
    # A mismatched state means the redirect caught was not the one this run started.
    if answer.get("state", [None])[0] != state:
        raise RuntimeError("Sign-in redirect did not match this request")
    if "code" not in answer:
        raise RuntimeError("Sign-in redirect carried no authorization code")
    form = {
        "grant_type": "authorization_code",
        "client_id": client_id,
        "code": answer["code"][0],
        "redirect_uri": redirect_uri,
        "code_verifier": verifier,
    }
    issued = exchange(token_endpoint(authority), naming_resource(form, resource))
    if not issued.get("refresh_token"):
        raise RuntimeError("Sign-in returned no refresh token;"
                           " check that offline_access is allowed for this client")
    write_replacing(token_file, json.dumps({"refreshToken": issued["refresh_token"]}))
    announce(f"Signed in. The sync can now run unattended; its sign-in is at {token_file}.")
