#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# Optional override: ANDROID_PHONE_AVD="Pixel_8_API_35"
ANDROID_PHONE_AVD="${ANDROID_PHONE_AVD:-}"

# shellcheck source=./mobile-debug-lib.sh
source "$SCRIPT_DIR/mobile-debug-lib.sh"

run_android_target phone "$ANDROID_PHONE_AVD"
