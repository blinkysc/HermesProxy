#!/usr/bin/env bash
# Runs HermesProxy from source under `dotnet watch`, so a saved .cs edit is patched into the
# running proxy while the game stays connected (see docs/hot-reload.md).
#
#   scripts/hot-reload.sh <install dir>
#
# <install dir> is the folder holding an installed HermesProxy and its appsettings.json. The proxy
# reads that config and uses that folder's CSV, AccountData, item cache, Logs and PacketsLog, so it
# behaves like the installed binary. dotnet watch's own output is copied to
# <install dir>/Logs/dotnet-watch.log.
#
# Needs the ASP.NET Core runtime (dotnet watch runs on it), e.g. `pacman -S aspnet-runtime`.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
install="${1:-${HERMES_INSTALL_DIR:-}}"
if [[ -z "$install" || ! -f "$install/appsettings.json" ]]; then
    echo "usage: $0 <directory holding the installed HermesProxy and its appsettings.json>" >&2
    exit 1
fi
install="$(cd "$install" && pwd)"

if pgrep -x HermesProxy >/dev/null; then
    echo "HermesProxy is already running; close the game (or that proxy) first." >&2
    exit 1
fi

mkdir -p "$install/Logs"
cd "$install"
export HERMES_WORKING_DIRECTORY="$install"
dotnet watch --project "$repo/HermesProxy" run --no-launch-profile -- --config "$install/appsettings.json" \
    2>&1 | tee "$install/Logs/dotnet-watch.log"
