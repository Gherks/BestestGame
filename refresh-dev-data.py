"""Refresh the isolated development database before a VS Code debug session."""

import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import time


def read_live_snapshot(database):
    # The live app writes the whole JSON file; retry if a save is in progress.
    for attempt in range(5):
        try:
            snapshot = database.read_bytes()
            if not isinstance(json.loads(snapshot), dict):
                raise ValueError("The live database must contain a JSON object.")
            return snapshot
        except (OSError, ValueError):
            if attempt == 4:
                raise
            time.sleep(0.1)


def mirror_covers(live_covers, development_covers):
    """Give the debugging copy the cover pictures its refreshed database refers to."""
    if not live_covers.is_dir():
        return
    development_covers.mkdir(parents=True, exist_ok=True)
    wanted = {path.name: path for path in live_covers.iterdir() if path.is_file()}
    # Pictures the live data does not have belong to debugging changes the refresh just discarded.
    for existing in development_covers.iterdir():
        if existing.is_file() and existing.name not in wanted:
            existing.unlink()
    # A picture's name changes whenever its content does, so a matching size means the same picture.
    for name, source in wanted.items():
        target = development_covers / name
        if not target.is_file() or target.stat().st_size != source.stat().st_size:
            shutil.copy2(source, target)


def refresh_development_data(checkout):
    checkout = checkout.resolve()
    live_directory = checkout.parent / "BestestGameLive"
    live_database = live_directory / "data/data.json"
    if live_database.is_file() or (live_directory / "current").is_symlink():
        # Once deployed, never fall back to old data if the live database is missing.
        live_database = live_database.resolve(strict=True)
    else:
        # Support the existing source-based service until its first deployment.
        project = checkout / "BestestGame"
        settings = json.loads((project / "appsettings.json").read_text(encoding="utf-8-sig"))
        configured_path = settings.get("DatabasePath")
        if not isinstance(configured_path, str) or not configured_path.strip():
            configured_path = "data.json"
        live_database = (project / configured_path).resolve(strict=True)
    development_database = checkout / ".dev-data/data.json"
    if live_database == development_database.resolve():
        raise ValueError("The live and development database paths must be different.")

    snapshot = read_live_snapshot(live_database)
    development_database.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = None
    try:
        with tempfile.NamedTemporaryFile(dir=development_database.parent,
                                         prefix=".data-", suffix=".tmp", delete=False) as output:
            temporary_path = Path(output.name)
            output.write(snapshot)
            output.flush()
            os.fsync(output.fileno())
        # Replace only after reading and validating the complete live snapshot.
        os.replace(temporary_path, development_database)
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)

    print(f"Development database refreshed from {live_database}")
    try:
        mirror_covers(live_database.parent / "covers", development_database.parent / "covers")
    except OSError as error:
        # Debugging works without pictures; the database itself is already in place.
        print(f"Could not copy the live cover pictures: {error}", file=sys.stderr)
    print(f"Debugging data: {development_database}")


if __name__ == "__main__":
    try:
        refresh_development_data(Path(__file__).resolve().parent)
    except (OSError, ValueError) as error:
        print(f"Could not refresh the development database: {error}", file=sys.stderr)
        sys.exit(1)
