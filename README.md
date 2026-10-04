# BestestGame

BestestGame supports a live application and a separate development application:

| | Address | Database | Application files |
|---|---|---|---|
| Live | http://localhost:5231 | Existing database configured in `BestestGame/appsettings.json` | Published copy in `.runtime/live/current` |
| Development | http://localhost:5232 | `.dev-data/data.json` | Source checkout and Debug build |

Requires the .NET 10 SDK, Python 3, `curl`, `flock` (util-linux), and systemd for
the live service. All scripts run as your normal user.

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
checkout, run `./update-live.sh` once to migrate it to a published copy. Existing
boot startup settings and live data are preserved. Close any terminal running
the old manual launcher first; an existing background service is handled automatically.

To develop with hot reload while the live app keeps running:

```bash
./develop.sh
```

Use `./develop.sh --no-browser` to skip opening the browser. You can also run
`dotnet watch --project BestestGame/BestestGame.csproj run --launch-profile http`
or `dotnet run --project BestestGame/BestestGame.csproj --launch-profile http`.
The `http` and `https` launch profiles both use a separate development database;
the HTTPS profile uses port 7215. Development data starts empty and is created
when you first save. It is never copied over the live database.
To seed development with existing data, stop the development app and copy one of
the JSON backups from `.runtime/live/backups` to `.dev-data/data.json`.

For breakpoints, open `BestestGame/BestestGame.code-workspace` in VS Code with the
Microsoft C# extension, select **BestestGame (development)** in Run and Debug,
and press F5. This builds and debugs the development app on port 5232. Stop
`develop.sh` before using F5, since both are development instances using that port.

When your changes are ready, update the live application with:

```bash
./update-live.sh
```

The script runs the regression checks and publishes a Release build using
separate build artifacts while the old app stays running. It then briefly stops
the service, backs up the live database, switches to the published release, and
starts the service. If startup fails, it restores the previous release and
service configuration. A failed build leaves the running version untouched.
Database backups and previous releases remain in `.runtime/live`; code rollback
does not replace the live database. The service runs with `Production` settings
and no longer rebuilds or reads application assets from your source checkout.

To open the live application, double-click `BestestGame.desktop` and allow it to
launch if KDE asks. After installation, its launcher starts the published service
if needed and opens your browser. It does not deploy your development changes.

You can also open it from a terminal:

```bash
./launch.sh
```

`xdg-open` opens the browser automatically. Before installing the service, the
launcher retains its original behavior: build and run the source checkout on
port 5231, keeping the terminal open. The default live database is
`/home/gherks/Repos/BestestGame/Database/data.json`, as configured by `DatabasePath`
in `BestestGame/appsettings.json`. Relative database paths resolve against the
application content root. Deployment passes the live database path as an absolute
path so moving between release folders does not change it.

To add the shortcut to your application menu:

```bash
mkdir -p ~/.local/share/applications
cp BestestGame.desktop ~/.local/share/applications/
```

The desktop shortcut points to this checkout's absolute path. Update its `Exec`,
`Path`, and `Icon` entries if you move the repository.

To inspect or disable the service:

```bash
systemctl --user status bestestgame.service
journalctl --user -u bestestgame.service
systemctl --user disable --now bestestgame.service
```
