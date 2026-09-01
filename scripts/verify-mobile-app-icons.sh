#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon.png"
PLAY_STORE_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon_playstore.png"
EXPECTED_SOURCE_SHA256="30faba4a58e01bf90b4fdd3580308312aca40a5e93e9298dcbf34fd1f9e8eba8"
# AAPT2 strips the fully opaque alpha channel from the generated PNG when packaging the AAB.
EXPECTED_ANDROID_XXXHDPI_SHA256="4db84166ede18ca827e03c6bc7b7f4bfba1b4dd67178f5bc7a32a10007e57212"
EXPECTED_IOS_MARKETING_SHA256="22b6480f4b77ef22dc674f72f1aff208dcbd7c636de05f9e4c40618458d58bce"

sha256_file() {
  shasum -a 256 "$1" | awk '{ print $1 }'
}

require_file_hash() {
  local file_path="$1"
  local expected_hash="$2"
  local label="$3"

  if [[ ! -f "$file_path" ]]; then
    echo "Missing $label: $file_path" >&2
    exit 1
  fi

  local actual_hash
  actual_hash="$(sha256_file "$file_path")"
  if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "$label does not match the approved teal Schink icon." >&2
    echo "Expected SHA-256: $expected_hash" >&2
    echo "Actual SHA-256:   $actual_hash" >&2
    exit 1
  fi
}

verify_sources() {
  require_file_hash "$APP_ICON" "$EXPECTED_SOURCE_SHA256" "MAUI app icon"
  require_file_hash "$PLAY_STORE_ICON" "$EXPECTED_SOURCE_SHA256" "Google Play listing icon"
}

mode="${1:-source}"
verify_sources

case "$mode" in
  source)
    echo "Approved teal app and Google Play listing icons verified."
    ;;
  android-aab)
    bundle_path="${2:-}"
    icon_entry="base/res/mipmap-xxxhdpi-v4/schink_appicon.png"
    if [[ -z "$bundle_path" || ! -f "$bundle_path" ]]; then
      echo "Usage: $0 android-aab <bundle.aab>" >&2
      exit 64
    fi
    if ! unzip -Z1 "$bundle_path" | grep -Fx "$icon_entry" >/dev/null; then
      echo "Google Play bundle is missing $icon_entry: $bundle_path" >&2
      exit 1
    fi
    embedded_hash="$(unzip -p "$bundle_path" "$icon_entry" | shasum -a 256 | awk '{ print $1 }')"
    if [[ "$embedded_hash" != "$EXPECTED_ANDROID_XXXHDPI_SHA256" ]]; then
      echo "Google Play bundle contains a stale or unexpected launcher icon." >&2
      echo "Expected SHA-256: $EXPECTED_ANDROID_XXXHDPI_SHA256" >&2
      echo "Actual SHA-256:   $embedded_hash" >&2
      exit 1
    fi
    echo "Google Play bundle contains the approved teal launcher icon."
    ;;
  ios-artwork)
    artwork_path="${2:-}"
    if [[ -z "$artwork_path" ]]; then
      echo "Usage: $0 ios-artwork <schink_appiconItunesArtwork.png>" >&2
      exit 64
    fi
    require_file_hash "$artwork_path" "$EXPECTED_IOS_MARKETING_SHA256" "generated iOS marketing icon"
    echo "Generated iOS asset catalog contains the approved teal app icon."
    ;;
  *)
    echo "Unknown verification mode: $mode" >&2
    exit 64
    ;;
esac
