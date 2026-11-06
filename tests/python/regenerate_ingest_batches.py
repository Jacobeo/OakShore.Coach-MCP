"""Regenerate the committed ingest-batches.json from the export importer, for the fixture
archive and the ExportSetIndex beside it. Run it after a deliberate change to what the
importer posts, and review the diff."""

import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from import_export_archive import import_export_archive  # noqa: E402


FIXTURES = ROOT / "tests" / "python" / "fixtures" / "garmin-export"


def main() -> int:
    posted: list[dict] = []
    import_export_archive(FIXTURES / "archive", posted.append,
                          json.loads((FIXTURES / "exercise-sets.json").read_text(encoding="utf-8")))
    target = FIXTURES / "ingest-batches.json"
    target.write_text(json.dumps(posted, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(posted)} batches to {target}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
