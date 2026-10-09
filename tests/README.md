Run the persistence and tournament regression checks with:

```bash
dotnet run --project tests/BestestGame.Checks
```

Checks use an isolated temporary database and delete it afterward.

Voting saves immediately and changes duels with a sequential slide-and-fade.
Browser checks cover both directions/phases, ordinary/adaptive/rapid results,
queued pointer/keyboard input, full Undo, completion, reduced motion, API fallback,
interruption, navigation cleanup and mobile/enlarged text.

The reports these browser checks were recorded in are not kept in this repository; the
numbered steps below describe what each round covered at the time. The earlier rounds ran
against the previous theme and fonts. The current theme (Libre Baskerville and Barlow
Condensed, painted voting canvases, cover art) was checked in Firefox at desktop, 390px and
320px widths, with hover, keyboard and reduced-motion runs. Bundled font licenses, source
commit and asset hashes are in [font provenance](../BestestGame/wwwroot/fonts/README.md).

Focused voting checks start with 113 fully ranked entries and verify adaptive
splitting down to individual duels, batch wins and losses, complete batch undo,
duplicate and concurrent input, tournament isolation, and finishing all 113 new
matchups with preferences that do not follow standing order. They also cover
selecting another group without reordering the rest of the queue or saving
results, and the opponent bar's layout: fixed standing order whichever group is
current, a split dividing its segment in place, finished and banned neighbours
merging, and a group drawn in pieces when its opponents are no longer adjacent.
The bar's hover/focus popovers, split preview and animation, and rapid 1v1 marker
need a browser.

After adding an entry, use **Finish matchups** to open `/vote?focus=<entry-id>`.
**Voting options** contains the **Focus on entry** selector, listing only entries with more than one remaining
duel, ordered from most to fewest and by tournament placement when counts are tied. Adaptive voting starts with
groups of up to ten opponents in standing order; **Split group** halves the
current group without recording results. Either winning button records all of
that group's individual duels. Select a segment of the bar under the choices to
vote on that group next. Turn off **Adaptive groups** for rapid 1v1 voting.
Shortcuts are **1 / Left arrow** for the focused entry, **2 / Right arrow** for its
opponents, **M** to split, **U** to undo, and **S** to skip. Skipped matches remain
pending, arena bans are respected, and undo reverses the entire last batch.
Votes persist in the database; the current split queue and mode also survive a
reload in the same browser tab. So does Undo for the last 20 vote batches; undoing a
split, skip or jump is only possible until the page is reloaded.

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

`CoverArtChecks` adds 20 assertions against a stand-in for Twitch and IGDB: nothing requested
without credentials, rejected credentials, token reuse and renewal, safe quoting of titles,
which candidates are certain (name, year within one, accents and punctuation set aside),
collections and legacy titles, downloading beside the database, uncertain and unknown
titles, replacing, restoring, clearing and removing pictures, uploaded pictures (stored by
their real kind, replaced, refused when not a picture or too large, and left alone by a
fetch), and unchanged storage for entries without a cover. The Games page controls, the candidate chooser and the painted
canvases need a browser; the real IGDB service needs credentials. See the Cover art section
of [the main README](../README.md).

On Games (`/import`), search the library by parent or included title. **Add game**
and **Actions → Edit details** open a shared native editor; **Import list** opens a
separate native dialog. Title/year appear before the optional included-title
section, which assigns independent optional years. Editing focuses the release
year (**Actions → Rename** focuses the title) and retains the collection as one
participant with shared points and duels. Validation retains unsaved values;
Cancel/Escape discards drafts. Success refreshes the library and shows focused
voting links beside feedback only for entries with stored pending duels. Closing
returns focus to the initiator, its visible row Actions summary, or the library
heading if the row is hidden/removed. Remove retains its native confirmation.

Game of the year (`/goty?year=2007`) ranks releases by their current full
tournament score, then completed head-to-head wins among tied entries in that year.
Unresolved ties retain shared ranks. Collections contribute included titles
with their shared score and a source label; unknown years stay outside the archive.
Rankings (`/rankings`, or `/rankings?year=2007`) supports filtering, keeping grouped
participants intact and showing only included titles matching the chosen year.
These views do not change scores or generate separate year-specific duels.

`TournamentStandingsChecks` covers competition ranks from full-tournament
points, head-to-head order among entries level on points (repeated among those
still level, with circles and unplayed pairs sharing a rank), stable
alphabetical presentation within a shared rank, year filtering before
search, preservation of ranks while searching, collection/included-title and
Unicode matching without duplicate participants, blank/no-result/unknown-year
cases, zero-point ties and read-only game data. It also covers when a year's
first place is still open: contenders by points within reach, level leaders with
matchups left, head-to-head decisions and collections as one contender. `ManagementChecks` covers
renaming entries and tournaments and deleting a tournament with its cover pictures.
Rankings exposes completed/pending counts and all matchup/exclusion actions
through its native entry Details dialog, which opens with a read-only opponent bar.
`FocusedVotingChecks` covers the bar's standing order and won/lost/banned/pending
status for an entry. Browser checks are needed for responsive
rows, native search/filter input, dialog keyboard/focus behavior and those actions.

The same .NET check command also covers Rankings route compatibility, shared
legacy/year display formatting, and the page-owned exclusion session helper.
Browser checks are still required for actual session storage, tab isolation,
navigation history, modal focus and rendered behavior.

`/vote?year=2007` redirects to filtered Rankings. `/vote?focus=<entry-id>&year=2007`
stays in focused Voting and links back to that Rankings year. Exclusions are set
on Rankings and retained per tournament in each tab's session storage; they apply
to both voting modes and survive reload. Unavailable IDs are pruned on restore.
No exclusion state is written to the game database. With browser storage blocked,
settings remain in memory only during navigation in the current tab.


`TournamentNavigationChecks` covers switch destinations: clearing old focus,
retaining a release year only on Rankings/GOTY, ordinary Voting, stable Games and
Home/Tournaments routes, path bases, casing, repeated queries and unrelated
parameters/fragments. Selection checks verify persisted application-wide selection,
unchanged games/scores/duels, tournament-scoped reads and ignored unavailable IDs.
Browser validation covers the interactive shell in the static layout, full page
refresh, Tournaments creation/selection notifications, menu/active-page/focus,
retained-year empty states and exclusions after switching/reloading.

`TournamentInsightsChecks` adds 12 assertions for the Insights section on Rankings: upsets
(a win from at least a twentieth of the field behind, and never from one point) ordered by
gap, each loop of three found once from its highest entry and listed only if it shares no
result with a wider one, neighbouring rivalries with pending ones first among equals, ignored
stray results, list limits beside full counts, empty tournaments and read-only data.
Entry names in the section open the existing Details dialog; that needs a browser.

`TournamentOverviewChecks` adds 17 assertions for Home's five-state action
policy, setup precedence for zero/single-parent collections, pending/completed
stored duels, and no automatic matchup generation. It checks bounded tied-leader
summaries, stable ordering, zero-point/empty states, parent-only collections,
unknown years and read-only IDs/scores/years/duel data. Home and Tournaments share
this presentation policy; GOTY rules remain separate.

Step 7 browser evidence covers all Home states, actual continuation destinations,
existing-list/creation ordering, native creation jumps/input focus, first and
additional creation, whitespace guards, selection/creation focus and selector
notifications, adding into a new tournament, responsive/long/grouped titles,
native instructions before hydration and text contrast. A shared skip-link limitation
found during these checks was resolved in Step 13.

`GameLibraryChecks` adds 15 assertions for trimmed/case-insensitive title search,
included-title matching without duplicate parents, Unicode and legacy included
titles, blank/reset/no-result/empty cases, stored pending counts, completed/single
entries and read-only IDs/scores/years/duel state. It runs with the same .NET check
command above.

Step 8 browser validation covers the 121-entry library, filtered counts/reset,
native included-title/action disclosures, pending-only focused links, distant
form reveal and focus, edit cancellation/saving, unchanged validation/duplicate
rules, real add/import/removal flows and preserved collection semantics. It also
checks removal keyboard containment/focus return, expanded 16-title collections,
responsive/enlarged text, reduced motion and exact isolated-data preservation.

Step 9 browser validation covers native add/edit/import dialogs, optional included
controls, pointer/keyboard opening, containment, cancellation, Escape and visible
focus restoration/fallbacks. It checks invalid/duplicate/empty drafts, independent
years, unchanged IDs/points/duels on detail saves, parent-only generation on actual
add/import, nearby focused-voting destinations, duplicate year/legacy formatting,
and removal/Rankings dialog regressions with the shared helper, along with responsive,
text zoom, reduced-motion and isolated-data checks.

`OrdinaryDuelChecks` adds 35 isolated assertions for preferred/empty/excluded
queues, choosing a different pending duel on Skip, safe single-matchup Skip,
stale/wrong/non-participant input, one parent point per vote, retained saved winner,
Vote → Skip → Undo, repeated input/Undo, later corrections, externally removed
results, another session's vote, tournament isolation and visit-only reset, and for
closest-first order: level matchups first, the next closest on Skip, exclusions,
requested matchups and the random order when it is off. Six more cover the undo
history: several votes undone in turn, a new session taking over a saved history,
and dropping corrected, removed, unknown and surplus entries on restore. These run
with the same .NET check command.

**Closest matchups first** under Voting options (on by default, kept for the browser
tab) asks about entries that are level on points before lopsided pairs; turned off,
matchups come up in random order. It applies from the next matchup.

Ordinary Voting has 1/Left, 2/Right, U and S shortcuts, using the shared scoped
keyboard helper with a separate action map. The focused defaults and session
behavior remain. Browser checks cover actual pointer/keyboard saves, progress,
feedback, skip/guarded undo, queued bursts, native repeats/composition/modifiers,
controlled typing/native/equivalent modal fixtures, focus-mode transitions,
navigation disposal/reconnection, exclusions and stale tournament reload recovery.
Responsive/long/collection, 125% text, reduced-motion, completion/setup and protected
data were checked as well.

Step 11 browser validation covers ten compact opponent rows and native included
context, both explicitly scoped batch actions, mobile sticky controls with a
measured scroll reserve, keyboard focus, 320px enlarged text and short-viewport
fallback. It checks actual split/skip queue changes, all winning shortcuts,
full-batch Undo, rapid 1v1, queued/stale input and later corrections, reload mode/
queue restoration, tournament-specific tab exclusions, completion/recovery,
mode/navigation cleanup, save motion, reduced motion and ordinary compatibility.
Services/models and the adaptive queue algorithm stay unchanged.

Step 12 browser validation covers the sole native year selector, immediate warm
leader result, compact Rank/Title/Points nominees and initially collapsed scoring/
archive disclosures, including secondary Random year. It checks native keyboard
selection/disclosures/focus, year links/reload/Back/Forward, continued ordinary and
focused Voting, legacy year-only redirects, tournament switching, independent
included years, one-parent head-to-head counting, shared competition ranks,
unknown-year exclusion and all missing/empty/no-points/completed states.
`RankingsNavigationChecks` adds five assertions restricting the legacy redirect
to actual Voting paths, preventing a departing Voting component from redirecting
GOTY's year query during Back navigation. Responsive, 125% text, reduced-motion,
pre-hydration, contrast and exact data-preservation were checked too. The existing
GOTY ranking algorithm and persistence behavior remain unchanged.

Step 13 repeats the baseline screen/state matrix on all main pages at desktop,
tablet, 390px and 320px, plus 125% text, reduced motion and short-height focused
controls. Integrated browser workflows cover create → add/import → vote →
Rankings/correction → edit years → GOTY → switch, and deep included-title search
in a 121-entry library → edit/cancel → focus → split → batch vote/full Undo →
reload. It checks native dialogs/focus/containment, typing/modal/modifier/repeat
shortcut suppression, exclusions, saved queue/mode, legacy and focused routes,
Back/Forward, recovery appearance and exact original-data preservation.

The shared skip link now uses the actual current URL, preserving page/year/focus
before and after hydration; this resolves the limitation recorded in Steps 7–12.
Focused dock lifecycle checks cover missing/detached DOM references, repeated
enhanced navigation and exact mode/save/batch/queue Undo, preventing a reproduced
navigation cleanup exception. Obsolete score-button/table-wrapper styles are removed. Recovery
banner captures used a controlled reveal; no production exception was induced.
