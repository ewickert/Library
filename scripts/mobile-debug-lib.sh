#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "Missing required command: $cmd" >&2
    exit 1
  fi
}

list_ios_device_names() {
  xcrun simctl list devices available | awk '
    /^[[:space:]]+[A-Za-z0-9].*\([0-9A-F-]+\) \(.*\)[[:space:]]*$/ {
      line = $0
      sub(/^[[:space:]]+/, "", line)
      name = line
      sub(/ \([0-9A-F-]+\).*/, "", name)
      print name
    }
  '
}

pick_ios_device() {
  local mode="$1"
  local exact_name="${2:-}"
  local pattern

  if [[ -n "$exact_name" ]]; then
    pattern=""
  elif [[ "$mode" == "iphone" ]]; then
    pattern="iPhone"
  elif [[ "$mode" == "ipad" ]]; then
    pattern="iPad"
  else
    pattern=".*"
  fi

  xcrun simctl list devices available | awk -v exact="$exact_name" -v pat="$pattern" '
    /^[[:space:]]+[A-Za-z0-9].*\([0-9A-F-]+\) \(.*\)[[:space:]]*$/ {
      line = $0
      sub(/^[[:space:]]+/, "", line)
      name = line
      sub(/ \([0-9A-F-]+\).*/, "", name)
      if (exact != "") {
        if (name != exact) {
          next
        }
      } else {
        if (name !~ pat) {
          next
        }
      }

      udid = line
      sub(/[[:space:]]+\([^)]*\)[[:space:]]*$/, "", udid)
      sub(/^.*\(/, "", udid)
      sub(/\)[[:space:]]*$/, "", udid)
      if (udid ~ /^[0-9A-F-]+$/) {
        print name "|" udid
        exit
      }
    }
  '
}

ensure_ios_booted() {
  local udid="$1"
  xcrun simctl boot "$udid" >/dev/null 2>&1 || true
  open -a Simulator --args -CurrentDeviceUDID "$udid" >/dev/null 2>&1 || true
  xcrun simctl bootstatus "$udid" -b
}

run_ios_target() {
  local mode="$1"
  local exact_name="${2:-}"
  local allow_fallback="${IOS_FALLBACK_ANY_SIM:-1}"

  require_cmd xcrun
  require_cmd dotnet

  local selected
  selected="$(pick_ios_device "$mode" "$exact_name")"

  if [[ -z "$selected" && -z "$exact_name" && "$allow_fallback" == "1" ]]; then
    echo "No $mode simulator found. Falling back to first available iOS simulator."
    selected="$(pick_ios_device any "")"
  fi

  if [[ -z "$selected" ]]; then
    echo "No matching iOS simulator found (mode=$mode, name=${exact_name:-auto})." >&2
    echo "Available iOS simulators:" >&2
    local names
    names="$(list_ios_device_names)"
    if [[ -z "$names" ]]; then
      echo "  (none)" >&2
      echo "Install/download simulator runtimes in Xcode and create at least one iPhone or iPad simulator." >&2
      echo "Tip: open Xcode -> Settings -> Components, then use Xcode -> Open Developer Tool -> Simulator." >&2
    else
      echo "$names" | sed 's/^/  - /' >&2
    fi
    exit 1
  fi

  local name="${selected%%|*}"
  local udid="${selected##*|}"

  echo "Using iOS simulator: $name ($udid)"
  ensure_ios_booted "$udid"

  cd "$ROOT_DIR"
  dotnet run -f net10.0-ios -p:_DeviceName=":v2:udid=$udid"
}

pick_android_avd() {
  local mode="$1"
  local exact_name="${2:-}"

  require_cmd emulator

  if [[ -n "$exact_name" ]]; then
    emulator -list-avds | awk -v exact="$exact_name" '$0 == exact { print; exit }'
    return
  fi

  if [[ "$mode" == "phone" ]]; then
    emulator -list-avds | awk 'tolower($0) !~ /tablet/ { print; exit }'
  else
    emulator -list-avds | awk 'tolower($0) ~ /tablet/ { print; exit }'
  fi
}

android_serial_for_avd() {
  local avd="$1"

  require_cmd adb

  local serial current
  while IFS= read -r serial; do
    current="$(adb -s "$serial" emu avd name 2>/dev/null | tr -d '\r')"
    if [[ "$current" == "$avd" ]]; then
      echo "$serial"
      return
    fi
  done < <(adb devices | awk 'NR > 1 && $2 == "device" { print $1 }')
}

ensure_android_booted() {
  local avd="$1"

  local serial
  serial="$(android_serial_for_avd "$avd" || true)"

  if [[ -z "$serial" ]]; then
    echo "Starting Android emulator AVD: $avd"
    emulator -avd "$avd" -no-snapshot-load >/tmp/library-"$avd".log 2>&1 &

    local i
    for i in $(seq 1 90); do
      serial="$(android_serial_for_avd "$avd" || true)"
      if [[ -n "$serial" ]]; then
        break
      fi
      sleep 2
    done
  fi

  if [[ -z "$serial" ]]; then
    echo "Timed out waiting for emulator for AVD '$avd'." >&2
    exit 1
  fi

  adb -s "$serial" wait-for-device
  until [[ "$(adb -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; do
    sleep 2
  done

  echo "$serial"
}

run_android_target() {
  local mode="$1"
  local exact_name="${2:-}"

  require_cmd dotnet
  require_cmd adb

  local avd
  avd="$(pick_android_avd "$mode" "$exact_name")"
  if [[ -z "$avd" ]]; then
    echo "No matching Android AVD found (mode=$mode, name=${exact_name:-auto})." >&2
    exit 1
  fi

  echo "Using Android AVD: $avd"
  local serial
  serial="$(ensure_android_booted "$avd")"
  echo "Using Android serial: $serial"

  cd "$ROOT_DIR"
  dotnet run -f net10.0-android -p:_DeviceName="$serial"
}
