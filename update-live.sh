#!/usr/bin/env bash
set -euo pipefail

if [[ ${1:-} == --help || ${1:-} == -h ]]; then
    printf 'Usage: ./update-live.sh\n\nCheck, publish, and restart the live BestestGame service on port 5231.\n'
    exit 0
fi
[[ $# -eq 0 ]] || { printf 'Usage: ./update-live.sh\n' >&2; exit 1; }

fail() { printf 'Error: %s\n' "$1" >&2; exit 1; }
[[ $EUID -ne 0 ]] || fail 'Run this script as your normal user, without sudo.'
for dependency in dotnet systemctl curl flock python3; do
    command -v "$dependency" >/dev/null || fail "Required command missing: $dependency"
done
systemctl --user show-environment >/dev/null 2>&1 ||
    fail 'Cannot connect to your systemd user session. Run this after signing in.'

root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
[[ "$root_dir" != *$'\n'* && "$root_dir" != *$'\r'* ]] || fail 'The checkout path cannot contain line breaks.'
project_dir="$root_dir/BestestGame"
runtime_dir="$root_dir/.runtime/live"
service_name='bestestgame.service'
config_dir="${XDG_CONFIG_HOME:-$HOME/.config}"
[[ "$config_dir" == /* ]] || fail 'XDG_CONFIG_HOME must be an absolute path.'
unit_dir="$config_dir/systemd/user"
unit_path="$unit_dir/$service_name"
url='http://localhost:5231'
launcher_lock="$project_dir/obj/launcher.lock"
if [[ -f "$launcher_lock" ]] && ! flock -n "$launcher_lock" true &&
    ! systemctl --user is-active --quiet "$service_name"; then
    fail 'Close the terminal running the manual launcher, then run this script again.'
fi

# Keep the existing live database at its configured location, independent of releases.
live_database="$(python3 - "$project_dir" <<'PY'
import json
from pathlib import Path
import sys
project = Path(sys.argv[1])
value = json.loads((project / 'appsettings.json').read_text()).get('DatabasePath')
if not isinstance(value, str) or not value.strip():
    value = 'data.json'
print((project / value).resolve())
PY
)"

mkdir -p -- "$runtime_dir/releases" "$runtime_dir/backups" "$unit_dir"
exec 9>"$runtime_dir/update.lock"
flock -n 9 || fail 'Another live update is already running.'
[[ ! -e "$runtime_dir/current" || -L "$runtime_dir/current" ]] ||
    fail 'The live current path must be a symlink, not a directory.'

build_dir="$(mktemp -d "$runtime_dir/build.XXXXXXXX")"
release_dir=''
previous_release=''
previous_unit=false
previous_running=false
previous_enabled=false
changed=false
success=false

switch_release() {
    rm -f -- "$runtime_dir/current.next"
    ln -s -- "$1" "$runtime_dir/current.next"
    mv -Tf -- "$runtime_dir/current.next" "$runtime_dir/current"
}

cleanup() {
    local result=$?
    trap - EXIT
    trap '' INT TERM HUP
    set +e
    if [[ "$changed" == true && "$success" != true ]]; then
        printf 'Update failed; restoring the previous service and release...\n' >&2
        local restored=true
        systemctl --user stop "$service_name" || restored=false
        if [[ -n "$previous_release" ]]; then
            switch_release "$previous_release" || restored=false
        else
            rm -f -- "$runtime_dir/current" || restored=false
        fi
        if [[ "$previous_enabled" != true ]]; then
            systemctl --user disable "$service_name" || restored=false
        fi
        if [[ "$previous_unit" == true ]]; then
            cp -p -- "$build_dir/previous.service" "$unit_path" || restored=false
        else
            rm -f -- "$unit_path" || restored=false
        fi
        systemctl --user daemon-reload || restored=false
        if [[ "$previous_running" == true ]]; then
            systemctl --user start "$service_name" || restored=false
        fi
        if [[ "$restored" == true ]]; then
            printf 'Previous service configuration restored. Live data was not replaced.\n' >&2
        else
            printf 'Rollback needs attention. Check: systemctl --user status %s\n' "$service_name" >&2
        fi
    fi
    rm -rf -- "$build_dir"
    if [[ "$success" != true && "$changed" != true && -n "$release_dir" ]]; then
        rm -rf -- "$release_dir"
    fi
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP

printf 'Running regression checks; the live service stays running during the build...\n'
dotnet run --project "$root_dir/tests/BestestGame.Checks/BestestGame.Checks.csproj" \
    --configuration Release --artifacts-path "$build_dir/artifacts" --no-launch-profile \
    --disable-build-servers 9>&-
release_dir="$(mktemp -d "$runtime_dir/releases/release.XXXXXXXX")"
printf 'Publishing a separate Release build...\n'
dotnet publish "$project_dir/BestestGame.csproj" --configuration Release \
    --artifacts-path "$build_dir/artifacts" --output "$release_dir" \
    --disable-build-servers 9>&-
[[ -f "$release_dir/BestestGame.dll" ]] || fail 'Publish did not produce BestestGame.dll.'
release_id="$(basename -- "$release_dir")"

unit_quote() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    value="${value//%/%%}"
    value="${value//$'\n'/\\n}"
    value="${value//$'\r'/\\r}"
    value="${value//$'\t'/\\t}"
    printf '"%s"' "$value"
}
dotnet_path="$(command -v dotnet)"
dotnet_path="${dotnet_path//\$/\$\$}"
cat > "$build_dir/$service_name" <<EOF
[Unit]
Description=BestestGame (live)

[Service]
Type=exec
WorkingDirectory=${runtime_dir//%/%%}/current/
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="DOTNET_ENVIRONMENT=Production"
Environment=$(unit_quote "DatabasePath=$live_database")
Environment=$(unit_quote "DeploymentId=$release_id")
ExecStart=$(unit_quote "$dotnet_path") ./BestestGame.dll --urls $url
Restart=on-failure
RestartSec=5
TimeoutStopSec=30

[Install]
WantedBy=default.target
EOF

if [[ -L "$runtime_dir/current" ]]; then
    previous_release="$(readlink -- "$runtime_dir/current")"
fi
if [[ -f "$unit_path" ]]; then
    cp -p -- "$unit_path" "$build_dir/previous.service"
    previous_unit=true
fi
if systemctl --user is-active --quiet "$service_name"; then previous_running=true; fi
if systemctl --user is-enabled --quiet "$service_name"; then previous_enabled=true; fi

printf 'Switching the live service to the published release...\n'
changed=true
if [[ "$previous_unit" == true || "$previous_running" == true ]]; then
    systemctl --user stop "$service_name"
fi
if [[ -f "$live_database" ]]; then
    backup_path="$runtime_dir/backups/data-$release_id.json"
    cp -p -- "$live_database" "$backup_path"
    printf 'Saved live database backup: %s\n' "$backup_path"
fi
switch_release "$release_dir"
cp -- "$build_dir/$service_name" "$unit_path.next"
chmod 644 "$unit_path.next"
mv -f -- "$unit_path.next" "$unit_path"
systemctl --user daemon-reload
systemctl --user enable "$service_name"
systemctl --user start "$service_name"

ready=false
deadline=$((SECONDS + 60))
while ((SECONDS < deadline)); do
    systemctl --user is-active --quiet "$service_name" || break
    if [[ "$(curl --noproxy '*' --fail --silent --max-time 2 "$url/healthz")" == "$release_id" ]] &&
        curl --noproxy '*' --fail --silent --output /dev/null --max-time 2 "$url/tournaments"; then
        ready=true
        break
    fi
    sleep 1
done
if [[ "$ready" != true ]]; then
    if command -v journalctl >/dev/null; then
        journalctl --user --unit "$service_name" --lines 25 --no-pager >&2 || true
    fi
    fail "The new release did not become ready at $url."
fi
success=true
printf '\nLive BestestGame is ready at %s\nRelease: %s\n' "$url" "$release_dir"
printf 'Development runs separately at http://localhost:5232 using .dev-data/data.json.\n'
