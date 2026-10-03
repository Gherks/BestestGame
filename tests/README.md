Run the persistence and tournament regression checks with:

```bash
dotnet run --project tests/BestestGame.Checks
```

Checks use an isolated temporary database and delete it afterward.

Game entries support an optional `IncludedTitles` string array. Old entries
without the property load with an empty list; saving writes it for every game.
On the import page, use the separate title and included-titles fields to add a
group, or choose **Edit included titles** on an existing game. Enter one included
title per line. Names are independent of the main title, and the group remains
one participant with shared points and duels. Plain bulk import remains available.
