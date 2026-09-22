#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon.png"
PLAY_STORE_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon_playstore.png"
ANDROID_LAUNCHER_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_android_icon.png"
EXPECTED_SOURCE_SHA256="8a5d7a16984ad1343d2c7263f0712272b582bb19ba5a9339933abc2fe2bc7f22"
# AAPT2 strips the fully opaque alpha channel from generated PNGs when packaging the AAB.
EXPECTED_ANDROID_LAUNCHER_SOURCE_SHA256="35c9577a755f4057c199c8d2108015728438529a10a0cdc706dbddd534651191"
EXPECTED_ANDROID_XXXHDPI_SHA256="4b85b3890b2fa939b33433889ca5498710e9a2c8234bee8266e58ca3f92bce7f"
EXPECTED_ANDROID_ROUND_XXXHDPI_SHA256="8836dc5603e3236bf247867a9aad895afa64a921a95db843bb42b0efa1ad2c15"
EXPECTED_ANDROID_FOREGROUND_XXXHDPI_SHA256="de9ede8af9d0f6af81ef130271b567edf811802cc21c5ed38a8fb58079b48c91"
EXPECTED_IOS_MARKETING_SHA256="5fec1012c94a1f0421a57b0bcf5133d8d96679efa8e629c35c660b97ee1b9bf3"

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
    echo "$label does not match the configured Schink Stories app icon." >&2
    echo "Expected SHA-256: $expected_hash" >&2
    echo "Actual SHA-256:   $actual_hash" >&2
    exit 1
  fi
}

verify_sources() {
  require_file_hash "$APP_ICON" "$EXPECTED_SOURCE_SHA256" "MAUI app icon"
  require_file_hash "$PLAY_STORE_ICON" "$EXPECTED_SOURCE_SHA256" "Google Play listing icon"
  require_file_hash "$ANDROID_LAUNCHER_ICON" "$EXPECTED_ANDROID_LAUNCHER_SOURCE_SHA256" "Android launcher icon"
}

mode="${1:-source}"
verify_sources

case "$mode" in
  source)
    echo "Schink Stories app and Google Play listing icons verified."
    ;;
  android-aab)
    bundle_path="${2:-}"
    if [[ -z "$bundle_path" || ! -f "$bundle_path" ]]; then
      echo "Usage: $0 android-aab <bundle.aab>" >&2
      exit 64
    fi
    verify_bundle_icon() {
      local icon_entry="$1"
      local expected_hash="$2"
      if ! unzip -Z1 "$bundle_path" | grep -Fx "$icon_entry" >/dev/null; then
        echo "Google Play bundle is missing $icon_entry: $bundle_path" >&2
        exit 1
      fi
      embedded_hash="$(unzip -p "$bundle_path" "$icon_entry" | shasum -a 256 | awk '{ print $1 }')"
      if [[ "$embedded_hash" != "$expected_hash" ]]; then
        echo "Google Play bundle contains a stale or unexpected Android launcher icon: $icon_entry" >&2
        echo "Expected SHA-256: $expected_hash" >&2
        echo "Actual SHA-256:   $embedded_hash" >&2
        exit 1
      fi
    }
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_appicon.png" "$EXPECTED_ANDROID_XXXHDPI_SHA256"
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_appicon_round.png" "$EXPECTED_ANDROID_ROUND_XXXHDPI_SHA256"
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_appicon_foreground.png" "$EXPECTED_ANDROID_FOREGROUND_XXXHDPI_SHA256"
    echo "Google Play bundle contains the configured Schink Stories launcher icon."
    ;;
  ios-artwork)
    artwork_path="${2:-}"
    if [[ -z "$artwork_path" ]]; then
      echo "Usage: $0 ios-artwork <schink_appiconItunesArtwork.png>" >&2
      exit 64
    fi
    require_file_hash "$artwork_path" "$EXPECTED_IOS_MARKETING_SHA256" "generated iOS marketing icon"
    echo "Generated iOS asset catalog contains the configured Schink Stories app icon."
    ;;
  *)
    echo "Unknown verification mode: $mode" >&2
    exit 64
    ;;
esac
