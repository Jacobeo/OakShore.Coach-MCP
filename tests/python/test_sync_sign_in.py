"""The unattended ingest credential, asserted at the Python seam: the form of the
token request, the Authorization header the batches carry, and what the athlete is
told when the saved sign-in stops working."""

import base64
import hashlib
import json
import sys
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import parse_qs, urlparse


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from sync_sign_in import (  # noqa: E402
    SyncSignIn, SyncSignInRequired, TokenEndpointError, pkce_pair, sign_in)


AUTHORITY = "https://proficient-steam-61-staging.authkit.app"
CLIENT_ID = "client_01ABCDEF"
NOW = datetime(2026, 10, 27, 19, 0, tzinfo=timezone.utc)


class Exchange:
    """Stands in for the token endpoint, recording what was asked of it."""

    def __init__(self, *responses):
        self.responses = list(responses)
        self.calls = []

    def __call__(self, url, form):
        self.calls.append((url, form))
        answer = self.responses.pop(0)
        if isinstance(answer, Exception):
            raise answer
        return answer


def issued(access_token="at_first", refresh_token="rt_second", expires_in=300):
    return {"access_token": access_token, "refresh_token": refresh_token,
            "expires_in": expires_in}


class CredentialTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.token_file = Path(self.directory.name) / "workos.json"
        self.token_file.write_text(json.dumps({"refreshToken": "rt_first"}), encoding="utf-8")

    def credential(self, exchange, now=NOW):
        return SyncSignIn(self.token_file, AUTHORITY, CLIENT_ID,
                                exchange=exchange, now=lambda: now)

    def test_exchanges_the_saved_refresh_token_for_an_access_token(self):
        exchange = Exchange(issued())

        token = self.credential(exchange).access_token()

        self.assertEqual("at_first", token)
        url, form = exchange.calls[0]
        self.assertEqual(f"{AUTHORITY}/oauth2/token", url)
        self.assertEqual("refresh_token", form["grant_type"])
        self.assertEqual("rt_first", form["refresh_token"])
        self.assertEqual(CLIENT_ID, form["client_id"])

    def test_saves_the_rotated_refresh_token_so_the_next_run_can_sign_in(self):
        exchange = Exchange(issued(refresh_token="rt_second"))

        self.credential(exchange).access_token()

        saved = json.loads(self.token_file.read_text(encoding="utf-8"))
        self.assertEqual("rt_second", saved["refreshToken"])

    def test_reuses_an_access_token_that_is_still_good(self):
        exchange = Exchange(issued(expires_in=300))
        credential = self.credential(exchange)

        self.assertEqual("at_first", credential.access_token())
        self.assertEqual("at_first", credential.access_token())

        self.assertEqual(1, len(exchange.calls))

    def test_renews_a_token_about_to_expire_part_way_through_a_catch_up(self):
        # A catch-up of many days outlives one access token; the run must not stop for it.
        clock = [NOW]
        exchange = Exchange(issued(access_token="at_first", refresh_token="rt_second"),
                            issued(access_token="at_third", refresh_token="rt_fourth"))
        credential = SyncSignIn(self.token_file, AUTHORITY, CLIENT_ID,
                                      exchange=exchange, now=lambda: clock[0])

        self.assertEqual("at_first", credential.access_token())
        clock[0] = NOW + timedelta(seconds=280)

        self.assertEqual("at_third", credential.access_token())
        self.assertEqual("rt_second", exchange.calls[1][1]["refresh_token"])

    def test_a_response_without_a_replacement_keeps_the_saved_refresh_token(self):
        # The saved token still stands when none came back; discarding or failing over
        # it would cost an interactive sign-in for nothing.
        answer = issued()
        del answer["refresh_token"]

        self.assertEqual("at_first", self.credential(Exchange(answer)).access_token())

        saved = json.loads(self.token_file.read_text(encoding="utf-8"))
        self.assertEqual("rt_first", saved["refreshToken"])

    def test_a_token_of_unstated_life_is_renewed_rather_than_trusted(self):
        answer = issued(access_token="at_first")
        del answer["expires_in"]
        exchange = Exchange(answer, issued(access_token="at_third"))

        credential = self.credential(exchange)
        self.assertEqual("at_first", credential.access_token())
        self.assertEqual("at_third", credential.access_token())

    def test_a_refused_refresh_token_asks_for_the_interactive_sign_in(self):
        exchange = Exchange(TokenEndpointError(400, "invalid_grant"))

        with self.assertRaises(SyncSignInRequired) as refused:
            self.credential(exchange).access_token()

        self.assertIn("sign in", str(refused.exception).lower())

    def test_a_token_endpoint_fault_is_a_failure_rather_than_a_sign_in_prompt(self):
        exchange = Exchange(TokenEndpointError(503, None, "service unavailable"))

        with self.assertRaises(TokenEndpointError):
            self.credential(exchange).access_token()

    def test_a_machine_that_has_never_signed_in_asks_for_the_interactive_sign_in(self):
        self.token_file.unlink()

        with self.assertRaises(SyncSignInRequired):
            self.credential(Exchange(issued())).access_token()


class SignInTests(unittest.TestCase):
    """The one interactive sign-in a machine ever needs."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.token_file = Path(self.directory.name) / "workos.json"
        self.opened = []

    def sign_in(self, exchange, redirect):
        sign_in(self.token_file, AUTHORITY, CLIENT_ID, exchange=exchange,
                open_browser=self.opened.append,
                await_redirect=lambda port: redirect(self.opened[0]),
                announce=lambda text: None)

    def test_leaves_behind_the_refresh_token_an_unattended_run_needs(self):
        exchange = Exchange(issued(refresh_token="rt_from_login"))

        self.sign_in(exchange, lambda url: {
            "code": ["the_code"],
            "state": [parse_qs(urlparse(url).query)["state"][0]]})

        saved = json.loads(self.token_file.read_text(encoding="utf-8"))
        self.assertEqual("rt_from_login", saved["refreshToken"])
        _, form = exchange.calls[0]
        self.assertEqual("authorization_code", form["grant_type"])
        self.assertEqual("the_code", form["code"])

    def test_asks_for_a_refresh_token_rather_than_an_access_token_alone(self):
        exchange = Exchange(issued())

        self.sign_in(exchange, lambda url: {
            "code": ["the_code"],
            "state": [parse_qs(urlparse(url).query)["state"][0]]})

        self.assertIn("offline_access", parse_qs(urlparse(self.opened[0]).query)["scope"][0])

    def test_a_redirect_from_some_other_request_is_refused(self):
        # Without the state check a redirect the athlete did not start could plant
        # somebody else's authorization code on this machine.
        with self.assertRaises(RuntimeError):
            self.sign_in(Exchange(issued()),
                         lambda url: {"code": ["the_code"], "state": ["not_this_request"]})

        self.assertFalse(self.token_file.exists())

    def test_sends_the_verifier_for_the_challenge_it_showed(self):
        exchange = Exchange(issued())

        self.sign_in(exchange, lambda url: {
            "code": ["the_code"],
            "state": [parse_qs(urlparse(url).query)["state"][0]]})

        shown = parse_qs(urlparse(self.opened[0]).query)
        sent = exchange.calls[0][1]["code_verifier"]
        digest = hashlib.sha256(sent.encode("ascii")).digest()
        self.assertEqual(base64.urlsafe_b64encode(digest).rstrip(b"=").decode("ascii"),
                         shown["code_challenge"][0])
        self.assertEqual("S256", shown["code_challenge_method"][0])

    def test_each_sign_in_uses_a_fresh_verifier(self):
        self.assertNotEqual(pkce_pair()[0], pkce_pair()[0])


if __name__ == "__main__":
    unittest.main()
