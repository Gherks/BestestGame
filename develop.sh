#!/usr/bin/env bash
set -euo pipefail

case "${1:-}" in
    '') [[ $# -eq 0 ]] || exit 1 ;;
    --no-browser) [[ $# -eq 1 ]] || exit 1; export DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1 ;;
    --help|-h) printf 'Usage: ./develop.sh [--no-browser]\n\nDevelop with hot reload on port 5232 and a separate development database.\n'; exit 0 ;;
    *) printf 'Usage: ./develop.sh [--no-browser]\n' >&2; exit 1 ;;
esac
root_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
command -v dotnet >/dev/null || { printf 'The .NET 10 SDK is required.\n' >&2; exit 1; }
python3 "$root_dir/refresh-dev-data.py"
printf 'Development: http://localhost:5232\nDatabase: %s/.dev-data/data.db\n' "$root_dir"
cd "$root_dir"
exec dotnet watch --project "$root_dir/BestestGame/BestestGame.csproj" run --launch-profile http
