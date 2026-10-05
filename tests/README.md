Run the persistence and tournament regression checks with:

```bash
dotnet run --project tests/BestestGame.Checks
```

Checks use an isolated temporary database and delete it afterward.

Focused voting checks start with 113 fully ranked entries and verify adaptive
splitting down to individual duels, batch wins and losses, complete batch undo,
duplicate and concurrent input, tournament isolation, and finishing all 113 new
matchups with preferences that do not follow standing order.

After adding an entry, use **Finish matchups** to open `/vote?focus=<entry-id>`.
The arena also has a **Focus on entry** selector, with entries ordered by remaining
duels from most to fewest and alphabetically when counts are tied. Adaptive voting starts with
groups of up to ten opponents in standing order; **Mixed or unsure?** halves the
current group without recording results. Either winning button records all of
that group's individual duels. Turn off **Adaptive groups** for rapid 1v1 voting.
Shortcuts are **1 / Left arrow** for the focused entry, **2 / Right arrow** for its
opponents, **M** to split, **U** to undo, and **S** to skip. Skipped matches remain
pending, arena bans are respected, and undo reverses the entire last batch.
Votes persist in the database; the current split queue and mode also survive a
reload in the same browser tab. Undo history lasts for the current visit.

Run the Linux startup and deployment transaction checks with:

```bash
python3 tests/startup_checks.py
```

These checks mock systemd, .NET, and HTTP commands and use temporary checkouts.
They cover migration to the sibling `BestestGameLive` folder, separate publish
artifacts, preservation of the live database across deployments, backups, concurrent
updates, rollback after build, startup, or HTTP failures, and refreshing the
development database before debugging. They do not change your installed service
or live database.

Games have an optional `ReleaseYear` and an `IncludedTitles` array of objects:

```json
{
  "Title": "A collection",
  "ReleaseYear": null,
  "IncludedTitles": [
    { "Title": "An independently named game", "ReleaseYear": 2007 }
  ]
}
```

Unknown years are `null`; assigned years must be integers from 1 to 9999.
Older databases with string-based included titles or missing years still load.
Saving writes included titles as objects with optional years.

On the import page, use **Add a game** and **Add included title** to assign years
independently. Use **Edit details** to change or clear years on existing entries.
The group remains one participant with shared points and duels. Plain bulk import
remains available; its entries can be edited afterward to assign years.

The GOTY Time Machine (`/goty?year=2007`) ranks releases by their current full
tournament score, then completed head-to-head wins among tied entries in that year.
Unresolved ties retain shared ranks. Collections contribute included titles
with their shared score and a source label; unknown years stay outside the archive.
The Hall of Dank also supports filtering (`/vote?year=2007`), keeping grouped
participants intact and showing only included titles matching the chosen year.
These views do not change scores or generate separate year-specific duels.
