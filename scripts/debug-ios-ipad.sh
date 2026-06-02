#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# Optional override: IOS_IPAD_SIM="iPad Pro (13-inch)"
IOS_IPAD_SIM="${IOS_IPAD_SIM:-}"

# shellcheck source=./mobile-debug-lib.sh
source "$SCRIPT_DIR/mobile-debug-lib.sh"

run_ios_target ipad "$IOS_IPAD_SIM"
