# BestestGame

On Linux, double-click `BestestGame.desktop` and allow it to launch if KDE asks.
The launcher builds the application, starts it at http://localhost:5231, and
opens your default browser. Keep its terminal open while using the app; close
the terminal or press Ctrl+C to stop the server. Launching the shortcut again
opens the running app without starting another server.

You can also start it from any terminal:

```bash
./launch.sh
```

Requires the .NET 10 SDK, `curl`, and `flock` (from util-linux). `xdg-open` opens
the browser automatically. Use `./launch.sh --no-browser` to start only the server.
Application data is read from and written to
`/home/gherks/Repos/BestestGame/Database/data.json`, as configured by `DatabasePath`
in `BestestGame/appsettings.json`.

To add the shortcut to your application menu:

```bash
mkdir -p ~/.local/share/applications
cp BestestGame.desktop ~/.local/share/applications/
```

The desktop shortcut points to this checkout's absolute path. Update its `Exec`,
`Path`, and `Icon` entries if you move the repository.
