#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# Optional override: IOS_IPHONE_SIM="iPhone 16 Pro"
IOS_IPHONE_SIM="${IOS_IPHONE_SIM:-}"

# shellcheck source=./mobile-debug-lib.sh
source "$SCRIPT_DIR/mobile-debug-lib.sh"

run_ios_target iphone "$IOS_IPHONE_SIM"
