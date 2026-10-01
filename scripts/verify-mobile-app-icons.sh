#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon.png"
PLAY_STORE_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon_playstore.png"
ANDROID_LAUNCHER_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon.svg"
ANDROID_ROUND_LAUNCHER_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_android_round_icon.svg"
EXPECTED_SOURCE_SHA256="8a5d7a16984ad1343d2c7263f0712272b582bb19ba5a9339933abc2fe2bc7f22"
# Baselines are from AAPT2-compiled PNGs, pixel-checked against the generated assets.
EXPECTED_ANDROID_LAUNCHER_SOURCE_SHA256="891d3a7c086054fa2070470d62b7748e76f931d981bb1d0b6e85e88b56c8ca3b"
EXPECTED_ANDROID_ROUND_LAUNCHER_SOURCE_SHA256="891d3a7c086054fa2070470d62b7748e76f931d981bb1d0b6e85e88b56c8ca3b"
EXPECTED_ANDROID_XXXHDPI_SHA256="ff822861a76a00e74c5b7fd050b434ee4bc337717c1aa2e4117c413c012a6c36"
EXPECTED_ANDROID_ROUND_XXXHDPI_SHA256="a3b7e3ef51f8e5b95197e5deb56caf961c2d47448b03e657edb3e1d3df85574a"
EXPECTED_ANDROID_FOREGROUND_XXXHDPI_SHA256="c40f31ff3e9f93178a95f8b3005349fce4ae99be07a0b3b9d1e86a69c3b46b1f"
EXPECTED_ANDROID_ROUND_FOREGROUND_XXXHDPI_SHA256="c40f31ff3e9f93178a95f8b3005349fce4ae99be07a0b3b9d1e86a69c3b46b1f"
EXPECTED_ANDROID_BACKGROUND_XXXHDPI_SHA256="6339ac1cf568a1e99f9bd97c8e037c023b107597f5ade63d6472566e40060ee1"
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
  require_file_hash "$ANDROID_ROUND_LAUNCHER_ICON" "$EXPECTED_ANDROID_ROUND_LAUNCHER_SOURCE_SHA256" "Android round launcher icon"
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
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_android_round_icon_foreground.png" "$EXPECTED_ANDROID_ROUND_FOREGROUND_XXXHDPI_SHA256"
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_appicon_background.png" "$EXPECTED_ANDROID_BACKGROUND_XXXHDPI_SHA256"
    verify_bundle_icon "base/res/mipmap-xxxhdpi-v4/schink_android_round_icon_background.png" "$EXPECTED_ANDROID_BACKGROUND_XXXHDPI_SHA256"
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
