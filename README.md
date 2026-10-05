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
