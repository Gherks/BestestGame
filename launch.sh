#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
project="$root_dir/BestestGame/BestestGame.csproj"
url="http://localhost:5231"
server_pid=""
service_mode=false

fail() {
    printf '\n%s\n' "$1" >&2
    if [[ -t 0 ]]; then
        read -r -p "Press Enter to close..." || true
    fi
    exit 1
}

cleanup() {
    if [[ -n "$server_pid" ]]; then
        kill "$server_pid" 2>/dev/null || true
        wait "$server_pid" 2>/dev/null || true
    fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP

open_browser() {
    if [[ "${1:-}" == "--no-browser" ]]; then
        return
    fi
    if command -v xdg-open >/dev/null; then
        xdg-open "$url" 9>&- >/dev/null 2>&1 &
    else
        printf 'Open %s in your browser.\n' "$url"
    fi
}

if [[ $# -gt 1 || ( $# -eq 1 && "$1" != "--no-browser" ) ]]; then
    fail "Usage: $0 [--no-browser]"
fi

for dependency in dotnet curl flock; do
    command -v "$dependency" >/dev/null || fail "Required command missing: $dependency"
done

cd "$root_dir"
if [[ -f "$root_dir/.runtime/live/current/BestestGame.dll" ]]; then
    command -v systemctl >/dev/null || fail 'Required command missing: systemctl'
    systemctl --user start bestestgame.service || fail 'Could not start the live service. Check: journalctl --user -u bestestgame.service'
    service_mode=true
else
    mkdir -p "$root_dir/BestestGame/obj"
    exec 9>"$root_dir/BestestGame/obj/launcher.lock"
    if flock -n 9; then
        printf 'Building BestestGame...\n'
        dotnet build "$project" || fail "Build failed. Check the output above; this app requires the .NET 10 SDK."
        printf 'Starting BestestGame at %s\n' "$url"
        ASPNETCORE_ENVIRONMENT=Development dotnet run --project "$project" \
            --no-build --no-launch-profile --urls "$url" 9>&- &
        server_pid=$!
    else
        printf 'BestestGame is already starting or running.\n'
    fi
fi

ready=false
for ((attempt = 0; attempt < 120; attempt++)); do
    if [[ "$service_mode" == true ]] && ! systemctl --user is-active --quiet bestestgame.service; then
        fail 'The live service stopped. Check: journalctl --user -u bestestgame.service'
    fi
    if [[ -n "$server_pid" ]] && ! kill -0 "$server_pid" 2>/dev/null; then
        fail "BestestGame stopped during startup. Check the output above."
    fi
    if curl --noproxy '*' --fail --silent --output /dev/null --max-time 1 "$url"; then
        ready=true
        break
    fi
    sleep 0.5
done
[[ "$ready" == true ]] || fail "BestestGame did not become ready at $url."
open_browser "${1:-}"

if [[ -n "$server_pid" ]]; then
    printf '\nBestestGame is running. Keep this terminal open; press Ctrl+C to stop.\n'
    wait "$server_pid" || fail "BestestGame stopped with an error."
fi
