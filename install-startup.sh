#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: ./install-startup.sh [--boot]

Install and start BestestGame as a background systemd user service.
By default, it starts automatically when you sign in.
Use --boot to also start it before login and keep it running after logout.
Only --boot may request sudo, to enable lingering for your user.
EOF
}

fail() {
    printf 'Error: %s\n' "$1" >&2
    exit 1
}

start_at_boot=false
case "${1:-}" in
    '') ;;
    --boot) start_at_boot=true ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; exit 1 ;;
esac
[[ $# -le 1 ]] || { usage >&2; exit 1; }
[[ $EUID -ne 0 ]] || fail 'Run this script as your normal user, without sudo.'

if [[ "$start_at_boot" == true ]]; then
    command -v loginctl >/dev/null || fail 'Required command missing: loginctl'
fi
root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
service_name='bestestgame.service'
"$root_dir/update-live.sh"

if [[ "$start_at_boot" == true ]]; then
    user_name="$(id -un)"
    if [[ "$(loginctl show-user "$user_name" --property=Linger --value)" != yes ]]; then
        command -v sudo >/dev/null ||
            fail 'BestestGame starts at login, but enabling boot startup requires sudo.'
        printf 'Enabling startup before login for %s; sudo may request your password.\n' "$user_name"
        sudo loginctl enable-linger "$user_name" ||
            fail 'BestestGame starts at login, but enabling boot startup failed.'
    fi
    printf 'BestestGame will start at boot, including before you sign in.\n'
else
    printf 'BestestGame will start automatically when you sign in.\n'
fi

printf 'Status:  systemctl --user status %s\n' "$service_name"
printf 'Logs:    journalctl --user -u %s\n' "$service_name"
printf 'Disable: systemctl --user disable --now %s\n' "$service_name"
