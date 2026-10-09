"""Refresh the isolated development database before a VS Code debug session."""

from contextlib import closing
import json
import os
from pathlib import Path
import shutil
import sqlite3
import sys
import tempfile
import time


def read_live_snapshot(database):
    # A live release from before SQLite writes the whole JSON file; retry if a save is in progress.
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


def write_database_copy(live_database, temporary_path):
    # SQLite's own backup reads a consistent copy even while the live application is writing,
    # including what is still in the write-ahead log beside the file.
    with closing(sqlite3.connect(f"{live_database.as_uri()}?mode=ro", uri=True)) as live, \
            closing(sqlite3.connect(temporary_path)) as copy:
        live.backup(copy)
        # One plain file, with no write-ahead log of its own to keep beside it.
        copy.execute("PRAGMA journal_mode = DELETE")
        if copy.execute("PRAGMA quick_check").fetchone() != ("ok",):
            raise ValueError("The copy of the live database is damaged.")


def find_live_database(checkout):
    live_directory = checkout.parent / "BestestGameLive"
    live_database = live_directory / "data/data.db"
    earlier_json = live_database.with_suffix(".json")
    if live_database.is_file():
        return live_database.resolve(strict=True)
    if earlier_json.is_file():
        # The live release is from before SQLite; the development application moves the copy in itself.
        return earlier_json.resolve(strict=True)
    if (live_directory / "current").is_symlink():
        # Once deployed, never fall back to old data if the live database is missing.
        return live_database.resolve(strict=True)
    # Support the existing source-based service until its first deployment.
    project = checkout / "BestestGame"
    settings = json.loads((project / "appsettings.json").read_text(encoding="utf-8-sig"))
    configured_path = settings.get("DatabasePath")
    if not isinstance(configured_path, str) or not configured_path.strip():
        configured_path = "data.db"
    configured_database = (project / configured_path).with_suffix(".db")
    if configured_database.is_file():
        return configured_database.resolve(strict=True)
    return configured_database.with_suffix(".json").resolve(strict=True)


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
    live_database = find_live_database(checkout)
    development_database = checkout / ".dev-data" / f"data{live_database.suffix}"
    if live_database == development_database.resolve():
        raise ValueError("The live and development database paths must be different.")

    snapshot = read_live_snapshot(live_database) if live_database.suffix == ".json" else None
    development_database.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = None
    try:
        with tempfile.NamedTemporaryFile(dir=development_database.parent,
                                         prefix=".data-", suffix=".tmp", delete=False) as output:
            temporary_path = Path(output.name)
            if snapshot is not None:
                output.write(snapshot)
                output.flush()
                os.fsync(output.fileno())
        if snapshot is None:
            write_database_copy(live_database, temporary_path)
        # Replace only after reading and validating the complete live snapshot.
        os.replace(temporary_path, development_database)
    finally:
        if temporary_path is not None:
            for suffix in ("", "-wal", "-shm"):
                Path(f"{temporary_path}{suffix}").unlink(missing_ok=True)

    # What the previous debug session left must not be read as part of the new copy: its write-ahead
    # log, and whichever of data.json and data.db the live application does not use.
    stale_database = development_database.with_suffix(".db")
    leftovers = [Path(f"{stale_database}-wal"), Path(f"{stale_database}-shm")]
    leftovers.append(development_database.with_suffix(".json" if snapshot is None else ".db"))
    for leftover in leftovers:
        leftover.unlink(missing_ok=True)

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
    except (OSError, ValueError, sqlite3.Error) as error:
        print(f"Could not refresh the development database: {error}", file=sys.stderr)
        sys.exit(1)
