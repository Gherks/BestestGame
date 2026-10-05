#!/usr/bin/env bash
set -euo pipefail

pause_on_exit=false
case "${1:-}" in
    '') ;;
    --wait) pause_on_exit=true ;;
    --help|-h) printf 'Usage: ./start-live.sh [--wait]\n\nStart the installed live service. --wait keeps a desktop terminal open for feedback.\n'; exit 0 ;;
    *) printf 'Usage: ./start-live.sh [--wait]\n' >&2; exit 1 ;;
esac
[[ $# -le 1 ]] || { printf 'Usage: ./start-live.sh [--wait]\n' >&2; exit 1; }

finish() {
    local result=$?
    if [[ "$pause_on_exit" == true && -t 0 ]]; then
        read -r -p 'Press Enter to close...' || true
    fi
    exit "$result"
}
trap finish EXIT

fail() { printf 'Error: %s\n' "$1" >&2; exit 1; }
for dependency in systemctl curl; do
    command -v "$dependency" >/dev/null || fail "Required command missing: $dependency"
done

service_name='bestestgame.service'
url='http://localhost:5231'
printf 'Starting live BestestGame...\n'
systemctl --user start "$service_name" ||
    fail 'Could not start the live service. Set it up with ./install-startup.sh, or check: journalctl --user -u bestestgame.service'

deadline=$((SECONDS + 60))
while ((SECONDS < deadline)); do
    systemctl --user is-active --quiet "$service_name" ||
        fail 'The live service stopped during startup. Check: journalctl --user -u bestestgame.service'
    if curl --noproxy '*' --fail --silent --output /dev/null --max-time 2 "$url/tournaments"; then
        printf 'Live BestestGame is running at %s\n' "$url"
        exit 0
    fi
    sleep 0.5
done
fail 'The live application did not respond within 60 seconds. Check: journalctl --user -u bestestgame.service'
