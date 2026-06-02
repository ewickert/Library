#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# Optional override: ANDROID_TABLET_AVD="Pixel_Tablet_API_35"
ANDROID_TABLET_AVD="${ANDROID_TABLET_AVD:-}"

# shellcheck source=./mobile-debug-lib.sh
source "$SCRIPT_DIR/mobile-debug-lib.sh"

run_android_target tablet "$ANDROID_TABLET_AVD"
