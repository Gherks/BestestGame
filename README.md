# BestestGame

BestestGame supports a live application and a separate development application:

| | Address | Database | Application files |
|---|---|---|---|
| Live | http://localhost:5231 | `../BestestGameLive/data/data.json` | Published copy in `../BestestGameLive/current` |
| Development | http://localhost:5232 | `.dev-data/data.json` | Source checkout and Debug build |

Requires the .NET 10 SDK, Python 3, `curl`, `flock` (util-linux), and systemd for
the live service. All scripts run as your normal user.

The live deployment is a separate sibling folder named `BestestGameLive`, outside
the Git checkout. On this machine it is
`/home/gherks/Repos/BestestGame/BestestGameLive`. Published app files, live data,
previous releases, and backups are stored there. The installed service runs
directly from this folder, so development builds and source edits do not affect it.

To set up the live application and start it automatically when you sign in:

```bash
./install-startup.sh
```

To also start at boot before you sign in and keep running after logout:

```bash
./install-startup.sh --boot
```

Only `--boot` may request your sudo password, to enable systemd lingering for your
account. Lingering also applies to other enabled systemd user services.

If you already installed the earlier service that runs directly from the source
checkout, run `./update-live.sh` once to migrate it to `BestestGameLive`. The first
deployment copies the existing database into `BestestGameLive/data/data.json`
after stopping the old service. Existing data and boot startup settings are
preserved; the original database is also retained. Later deployments never replace
the live database. Close any terminal running
the old manual launcher first; an existing background service is handled automatically.

To develop with hot reload while the live app keeps running:

```bash
./develop.sh
```

Use `./develop.sh --no-browser` to skip opening the browser. You can also run
`dotnet watch --project BestestGame/BestestGame.csproj run --launch-profile http`
or `dotnet run --project BestestGame/BestestGame.csproj --launch-profile http`.
The `http` and `https` launch profiles both use a separate development database;
the HTTPS profile uses port 7215. `develop.sh` copies the current live database
into `.dev-data/data.json` before starting, replacing previous development data.
Direct `dotnet` commands keep the existing development copy; run
`python3 refresh-dev-data.py` first if you want to refresh it. Development data
is never copied over the live database.

For breakpoints, open `BestestGame/BestestGame.code-workspace` in VS Code with the
Microsoft C# extension, select **BestestGame (development)** in Run and Debug,
and press F5. Each new debug session builds the app, then copies
`BestestGameLive/data/data.json` into `.dev-data/data.json` before
starting on port 5232. This replaces changes made during previous debug sessions;
the live database stays untouched. If the live database cannot be read or contains
invalid JSON, preparation fails and the existing development copy is preserved.
Stop `develop.sh` before using F5, since both are development instances using that port.

When your changes are ready, update the live application with:

```bash
./update-live.sh
```

Or double-click `Deploy-Live.desktop` and allow it to launch if KDE asks. Its
terminal shows progress and the result; press Enter to close it. The shortcut
runs `./update-live.sh --wait` and deploys the current source checkout.

The script runs the regression checks and publishes a Release build using
separate build artifacts while the old app stays running. It then briefly stops
the service, backs up the live database, switches to the published release, and
starts the service. If startup fails, it restores the previous release and
service configuration. A failed build leaves the running version untouched.
Database backups and previous releases remain in `BestestGameLive`; code rollback
does not replace the live database. The service runs with `Production` settings
and no longer rebuilds or reads application assets from your source checkout.

To open the live application, double-click `BestestGame.desktop` and allow it to
launch if KDE asks. After installation, its launcher starts the published service
if needed and opens your browser. It does not deploy your development changes.

You can also open it from a terminal:

```bash
./launch.sh
```

To start or stop only the live service, double-click `Start-Live.desktop` or
`Stop-Live.desktop`. Allow the shortcut to launch if KDE asks. Each opens a
terminal with the result; press Enter to close it. Starting waits for the live
application to respond at http://localhost:5231. These shortcuts control the
installed service without building or deploying changes, and leave development
instances and automatic startup settings alone.

The underlying scripts can also be run from a terminal:

```bash
./start-live.sh
./stop-live.sh
```

`xdg-open` opens the browser automatically. Before installing the service, the
launcher retains its original behavior: build and run the source checkout on
port 5231, keeping the terminal open. Before the first deployment, the database is
`/home/gherks/Repos/BestestGame/Database/data.json`, as configured by `DatabasePath`
in `BestestGame/appsettings.json`. This is used only to seed the new live folder
on its first deployment. Published configuration and the service use an absolute
path to `BestestGameLive/data/data.json`, independent of development configuration
and release folders. Until migration, the debug-data refresh script supports the
old configured database location.

To add the shortcut to your application menu:

```bash
mkdir -p ~/.local/share/applications
cp BestestGame.desktop ~/.local/share/applications/
```

The desktop shortcuts point to this checkout's absolute path. Update their `Exec`,
`Path`, and `Icon` entries if you move the repository.

To inspect or disable the service:

```bash
systemctl --user status bestestgame.service
journalctl --user -u bestestgame.service
systemctl --user disable --now bestestgame.service
```

Voting (`/vote`) and Rankings (`/rankings`) are separate pages. Rankings retains
year filtering, wins/losses inspection, correction, pending-match voting and undo,
and links to focused voting. Legacy `/vote?year=2007` redirects to
`/rankings?year=2007`; a `focus` query always opens focused Voting, including when
a year is also present. Its Rankings link retains the year. Choosing **All
matchups** opens ordinary `/vote`.

Ordinary Voting puts the two choices, stored progress, **Undo** and **Skip for now**
first. **Voting options** contains entry selection, Rankings/exclusions and shortcut
guidance. Skip picks another available pending matchup when possible and keeps
Undo for the last saved vote; a sole available matchup stays pending. Undo only
reverses the exact saved winner, preserving a later correction. Shortcuts are
**1 / Left** for the first choice, **2 / Right** for the second, **U** for Undo and
**S** for Skip. Repeats, stale presentations, typing and open dialogs are ignored.
Completion links to Rankings. Undo is local to the visit; saved results persist.
If another tab changes the application-wide tournament, stale ordinary choices
are cleared and **Reload Voting** restores the current context.

Votes save immediately. The current duel fades and slides left over 180ms, then
the next duel fades in from the right over 220ms. This applies to ordinary,
adaptive and rapid voting. Further vote inputs wait until the new duel is visible;
Undo, Skip and Split can interrupt the transition. Reduced motion uses instant
updates. Voting surfaces and loading status stay static. See
[the vote-transition validation report](docs/ux-duel-transitions.md).

Native disclosures (Voting options, included titles, row Actions, the mobile menu
and the Home/GOTY explanations) unfold from their summary and fold back over
240ms. This is CSS only, in `app.css`, and needs a disclosure's content to be a
single child element. Reduced motion and browsers without `::details-content`
toggle instantly.

Headings and game titles use **Bricolage Grotesque**; controls and supporting
copy use **DM Sans**. Both variable fonts are bundled locally with their licenses,
preloaded and served by the application, with automatic optical sizing and system
fallbacks. Body text is 18px, controls are at least 16px, supporting text is 15px
and voting titles scale from 24px to 30px. Sizes use `rem` so browser text settings
still apply. See [font asset provenance](BestestGame/wwwroot/fonts/README.md) and
[the typography validation report](docs/ux-typography.md).

Focused Voting (`/vote?focus=<entry-id>`) shows a progress summary above two
equal-width tactile choices: the entry on the left and its compact opponent rows
on the right. Included titles appear as text inside each choice, retaining
collection, legacy-label and release-year context. Activating a choice records
that side as the winner of **every** listed matchup; the group size is stated
below the choices. A bar the width of both choices sits just under them and shows
every opponent in a fixed standing order, highest ranked on the left: hollow
segments are groups still to vote on, the taller filled one is current, and
finished opponents can be shown as won or lost segments. Hovering or focusing a segment
lists its titles; selecting one makes that group the current duel without saving
anything. **Split group** previews its cut on the bar and divides the segment in
place. Rapid 1v1 keeps the bar and marks the current opponent inside its group.
**Show won and lost** is off by default, so the remaining groups fill the bar;
turning it on adds the finished segments, and the choice lasts for the browser tab.
The same bar (`MatchupBar`) opens each entry's Details dialog on Rankings as a
read-only overview of its won, lost and pending opponents.
A mobile action bar keeps group context, **Split group**,
**Undo** and **Skip for now** reachable while reading;
very short viewports use normal flow. Splitting saves no results, and Undo reverses
the whole last vote or queue change. **Adaptive groups** can switch to rapid 1v1;
**Voting options** contains all shortcut guidance. The split queue and mode survive
a reload in the same tab; Undo history remains local to the visit. See
[the focused-voting validation report](docs/ux-step11.md).

Arena exclusions are temporary settings in each browser tab's session storage,
keyed by tournament. They apply to both voting modes across navigation and reload;
unavailable IDs are pruned on restore. Exclusions never change the database, and
tournament selection remains application-wide. If browser storage is unavailable,
settings work in memory during navigation in that tab but cannot survive a full
reload. See [the regression-check documentation](tests/README.md) for game and
voting behavior.

Rankings shows rank, title and full-tournament points. Equal points share
competition ranks (for example, 1, 2, 2, 4); alphabetical order within a tie is
only for presentation. Release-year filtering determines the standings before
title search, so search preserves each entry's position. Search also matches
included titles and returns their collection once. Open **Details** for counts,
wins, losses/corrections, pending choices, focused completion and exclusions.
GOTY retains its separate head-to-head rules. Search is local to the page;
release-year links retain their existing query parameter.

Game of the year (`/goty?year=2007`) puts one **Release year** selector before the
current leader or shared leaders, followed by compact rank/title/points nominees.
Completed voting labels the result **Winner** or **Shared winners**. Points come
from the full tournament; collection-derived releases retain their source label.
**How yearly rankings work** explains inheritance and head-to-head ties, while
the initially collapsed **Release-year archive** contains year links and the
secondary **Random year** action. Missing-year and no-points states keep clear
assign-years/voting links. Year browsing, reload and Back preserve stored results;
the legacy Voting redirect applies only to actual Voting URLs. See
[the GOTY validation report](docs/ux-step12.md).


The shared **Active tournament** selector works on every page, including Home.
Switching uses the existing application-wide selection and reloads the current
page with that tournament's data. It clears the previous entry's `focus` query;
release years remain on Rankings and Game of the year, where unavailable years
have explicit empty states. Other pages discard the year query on a switch.
Existing per-tab exclusions stay keyed by tournament. Selecting or creating a
tournament on Tournaments also updates the shared selector without losing feedback.

The mobile **Menu** disclosure shows the current page and all six destinations:
Home, Voting, Rankings, Games, Game of the year and Tournaments. **Games** keeps
the `/import` route and all existing adding/importing/editing/removal behavior.

Games opens with a searchable library and entry count. Search matches game names
and included titles, returning a collection once; **Clear search** restores the
list. Compact rows show title/year and pending matchup context. Collections have
a native included-title disclosure. **Actions** contains **Edit details**, **Finish
matchups** when pending duels exist, and confirmed **Remove**. Escape closes Actions.

**Add game** and **Actions → Edit details** open the same native editor dialog;
**Import list** opens its own dialog. Title/year appear first, with included-title
controls in an optional disclosure. Editing focuses the release year and keeps
the saved title read-only. Failed validation retains the draft; Cancel or Escape
discards it and returns focus to the initiating control, with a library fallback
if that row disappears. Saves/imports refresh the collection and show pending
**Finish matchups** links beside their confirmation. Shared display formatting
avoids repeated year suffixes and preserves legacy collection labels without
rewriting stored titles. Search remains local to the page and resets on navigation
or tournament switching.

Home shows the active tournament, stored matchup progress and the next useful
action: create, choose, add games, continue voting or view rankings. Zero-duel
tournaments with fewer than two entries remain setup states. A small summary
shows up to three tied leaders with full tournament points; all results stay on
Rankings. **How it works** is an optional native disclosure.

Tournaments puts the active tournament first and the existing list before the
creation form. **Create a tournament** jumps to the form; creation becomes the
primary task when the list is empty. Creating focuses **Add games** beside the
confirmation. Selecting focuses the appropriate continuation action and updates
the shared selector using the existing application-wide selection.

**Skip to content** is the first keyboard destination and focuses the current
page's content, retaining its release-year/focused-entry query. It works before
interactive hydration. The redesign's integrated responsive, keyboard, dialog,
voting, correction, year-browsing and data-preservation checks are recorded in
[the final UX validation report](docs/ux-final.md). Focused action-bar cleanup
also tolerates a DOM reference disappearing during navigation. No deployment is included.
