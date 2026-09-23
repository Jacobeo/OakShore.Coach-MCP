# Issue tracker: Local Markdown

Issues and specs live under `.scratch/`.

## Conventions

- Preserve existing spec locations; link each ticket to its source spec.
- Store tickets at `.scratch/<feature-slug>/issues/<NN>-<slug>.md`.
- Write one file per ticket, numbered from `01` in dependency order.
- Record triage state near the top as `**Status:** <role>`,
  using [triage-labels.md](triage-labels.md).
- Record blocking tickets near the top as `**Blocked by:**`,
  listing their numbers and titles, or `None (can start immediately)`.
- Append comments under `## Comments`.

## Publishing and fetching

Publishing means writing ticket files in the feature's issues directory.
Fetching means reading the referenced ticket and its comments.
Resolve ticket numbers within the named feature; ask if the feature is unclear.

## Working order

Work on tickets whose blockers are complete. Triage readiness alone does
not mean a ticket's blockers are complete.

Follow the solution inclusion rule in the root AGENTS.md for new files.
