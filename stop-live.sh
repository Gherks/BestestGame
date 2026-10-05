#!/usr/bin/env bash
set -euo pipefail

pause_on_exit=false
case "${1:-}" in
    '') ;;
    --wait) pause_on_exit=true ;;
    --help|-h) printf 'Usage: ./stop-live.sh [--wait]\n\nStop the live service. --wait keeps a desktop terminal open for feedback.\n'; exit 0 ;;
    *) printf 'Usage: ./stop-live.sh [--wait]\n' >&2; exit 1 ;;
esac
[[ $# -le 1 ]] || { printf 'Usage: ./stop-live.sh [--wait]\n' >&2; exit 1; }

finish() {
    local result=$?
    if [[ "$pause_on_exit" == true && -t 0 ]]; then
        read -r -p 'Press Enter to close...' || true
    fi
    exit "$result"
}
trap finish EXIT

command -v systemctl >/dev/null || { printf 'Required command missing: systemctl\n' >&2; exit 1; }
printf 'Stopping live BestestGame...\n'
if ! systemctl --user stop bestestgame.service; then
    printf 'Could not stop the live service. Check: journalctl --user -u bestestgame.service\n' >&2
    exit 1
fi
printf 'Live BestestGame is stopped. Automatic startup settings are unchanged.\n'
