# AGENTS.md

Project instructions for AI coding assistants. This file is the single source of truth;
tool-specific files (`CLAUDE.md`, and `.github/copilot-instructions.md` if one is added)
are pointers to it and hold no content of their own.

## Status

No application code yet. The repository holds the domain language, the decisions already
fixed, and the specs. `src/`, `tests/` and the solution file do not exist; the first person
to create them follows the layout in [docs/build-order.md](docs/build-order.md), the
dependency rules below, and the solution rule below that.

## No guesswork — ask

Ambiguous requirement, two plausible readings, or a convention that does not fit the
situation: **ask instead of guessing**. A wrong assumption implemented confidently costs
more than a question. When you do make a judgment call, say so explicitly so it can be
corrected.

## Read on demand

Do not read these up front. Read the matching file when the task touches that area.

| File | Read when |
| --- | --- |
| [CONTEXT.md](CONTEXT.md) | Naming anything — a type, a table, a tool, a variable |
| [docs/adr/](docs/adr/) | Changing how sync, hosting, storage or Garmin writes behave |
| [docs/build-order.md](docs/build-order.md) | Starting a step, or deciding what comes next |
| [.scratch/](.scratch/) | Implementing a step — the spec carries the detail the build order omits |
| [docs/example-session.md](docs/example-session.md) | Designing a tool's shape or its response |

## Vocabulary is binding

[CONTEXT.md](CONTEXT.md) defines the domain terms and, for each, the words to avoid. Those
names carry into code unchanged: type names, table names, tool names, parameters, test
names. An `Activity` is never a `Session`; a `Program` is never a `TrainingPlan`. When a
concept has no term yet, add it to CONTEXT.md in the same change that introduces it.

## Architecture — dependency rules

Four components, arrows pointing inward at `Coach.Domain`:

| Project | References | Holds |
| --- | --- | --- |
| `Coach.Domain` | Nothing beyond the BCL | Domain types, and the interfaces the outer layers implement |
| `Coach.Infrastructure` | `Coach.Domain` | Persistence, FIT parsing, the ingest write path |
| `Coach.Api` | `Coach.Domain`, `Coach.Infrastructure` | MCP tools, the ingest endpoint, composition root |
| `src/sync/` (Python) | — | Garmin client; POSTs to ingest over HTTP |

Checkable consequences:

- **`Coach.Domain` has no `PackageReference`.** No EF Core or Dapper, no ASP.NET, no Azure
  SDK, no MCP SDK, no `Garmin.FIT.Sdk`. If a domain type needs one of those, the abstraction
  is in the wrong project.
- **Domain types stay free of infrastructure attributes** — no EF mapping attributes, no
  serialisation attributes. Mapping lives with the mapper.
- **Repository and gateway interfaces are declared in `Coach.Domain`** and implemented in
  `Coach.Infrastructure`.
- **SQL, connection strings and `DbContext` appear only in `Coach.Infrastructure`.** A
  data-access `using` in `Coach.Api` or `Coach.Domain` means a line was crossed.
- **Constructor injection only.** `new` on a service appears in `Program.cs` or in a test,
  nowhere else. `Program.cs` is the single composition root.
- **The Python sync never opens a database connection** ([ADR 0003](docs/adr/0003-python-for-garmin-dotnet-for-the-rest.md)).
  Its only route into the store is the ingest endpoint.
- **Garmin API calls live behind one module in `src/sync/`**, so the Python test seam can
  replay fixtures at that boundary.

These rules exist to be tested, not read. The change that creates `src/` also creates
`tests/Coach.ArchitectureTests`, asserting each dependency direction with NetArchTest, so a
violation is a red test rather than a review comment.

## Every tracked file belongs to the solution

Visual Studio is the git client for this repository, so a file that git tracks but the
solution does not know about is invisible where the work happens. **A change that adds a
tracked file adds it to `coach-mcp.sln` in the same change** — code, project files,
configuration, scripts, markdown, CI workflows, all of it. A file present on disk and absent
from the solution is an unfinished change.

How each kind gets in:

| Kind | Where it goes |
| --- | --- |
| .NET projects under `src/` and `tests/` | Added as projects |
| Root config — `.gitignore`, `.editorconfig`, `Dockerfile`, `README.md`, `AGENTS.md`, `CLAUDE.md` | A `Solution Items` solution folder |
| `docs/`, `CONTEXT.md` | A `Docs` solution folder, mirroring the folder structure |
| `.scratch/` | A `Specs` solution folder |
| `.github/workflows/` | A `CI` solution folder |
| `src/sync/` (Python) | A Python project (`.pyproj`) if the Python workload is installed, otherwise a `Sync` solution folder holding the files |
| `spikes/`, `gaudit/` | Solution folders — read in Visual Studio, never built |

Solution folders hold links, not copies: the file keeps its real path on disk, and the
solution entry is what makes it visible and openable. Adding one is an edit to the solution
file, so include that edit rather than leaving it for later.

(Visual Studio's Git Changes window does list every modified file in the working tree, so
nothing escapes a commit either way. The point of this rule is Solution Explorer: a file
you cannot see there is a file you do not open, review or remember.)

## MCP tools

- A tool method does three things: validate input, call one domain service, return
  `structuredContent`. Business logic in a tool method belongs in `Coach.Domain`.
- **Every response carries the time its data was last synced**
  ([ADR 0002](docs/adr/0002-own-datastore-is-the-system-of-record.md)). A response shape
  without a freshness field is incomplete.
- **Every table and every query carries `UserId`**, from the first table onward.
- Tool descriptions are load-bearing, not documentation: ChatGPT's safety scanner rejects
  connectors over wording, and health data is the trigger area. Return `structuredContent`
  alone — no duplicate serialised JSON block.
- Writes to Garmin are additive and reversible only
  ([ADR 0004](docs/adr/0004-garmin-writes-are-additive-only.md)). Recorded Activities are
  permanently read-only.

## Tests

Two seams, one per runtime, described in [.scratch/00-README.md](.scratch/00-README.md).
Prefer them over any new one — a test that mocks a repository is testing the mock. A new
seam needs a stated reason the existing two cannot cover the case.

## Comments describe the code as it is

A comment explains a non-obvious *why*, or a constraint invisible at that spot, in as few
words as possible. Keep out of comments:

- **Code history** — what it used to do, what replaced what, "now", "previously". Git carries that.
- **Future plans** — what could be done later belongs in [.scratch/](.scratch/).
- **Re-derivations of the design** — if it takes more than two or three lines, it is a
  decision: write an ADR under [docs/adr/](docs/adr/) and point at it in one line.

Same rules for test comments, SQL, log messages and exception messages. If your change makes
a nearby comment stale, fix or remove it.

## Working agreement

- **Propose before non-trivial edits.** Explain the trade-off, not just the change.
- **Commit only when asked.** Never push unprompted.
- **Push back** on a risky or wrong approach rather than implementing it quietly.
- A step is not finished until its **done when** in [docs/build-order.md](docs/build-order.md)
  is demonstrably true. Say which one you demonstrated and how.

## Two directories that are not project code

- **[gaudit/](gaudit/)** — a vendored copy of the third-party `taxuspt/garmin-mcp` server,
  kept to read for Garmin API behaviour. Never imported, never edited, never a model for our
  structure (110+ thin passthrough tools is the opposite of this project's curated surface).
- **[spikes/](spikes/)** — throwaway scripts, committed for the record, never imported.
