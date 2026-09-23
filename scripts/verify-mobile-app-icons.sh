#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon.png"
PLAY_STORE_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_appicon_playstore.png"
ANDROID_LAUNCHER_ICON="$APP_ICON"
ANDROID_ROUND_LAUNCHER_ICON="$ROOT_DIR/Shink.Mobile/Resources/AppIcon/schink_android_round_icon.png"
EXPECTED_SOURCE_SHA256="8a5d7a16984ad1343d2c7263f0712272b582bb19ba5a9339933abc2fe2bc7f22"
# AAPT2 strips the fully opaque alpha channel from generated PNGs when packaging the AAB.
EXPECTED_ANDROID_LAUNCHER_SOURCE_SHA256="$EXPECTED_SOURCE_SHA256"
EXPECTED_ANDROID_ROUND_LAUNCHER_SOURCE_SHA256="f2a9cd3eab2572c096809a8b92cd6d8ef5f77ef2f7f8f6b658891c1c5fd04d2c"
EXPECTED_ANDROID_XXXHDPI_SHA256="dbe36cbe16d7e80afa143c98e50a30765107eec13ce9cbaf73c9e7c6e54f6ef8"
EXPECTED_ANDROID_ROUND_XXXHDPI_SHA256="bc57aa1e40e861d775499e277ba4f921af9edfef68be1fbaad2c25733690d473"
EXPECTED_ANDROID_FOREGROUND_XXXHDPI_SHA256="2781ce62aad7a993225d9cde5b0a2faf7c544d74ff828095d6b27c19c2c9e34d"
EXPECTED_ANDROID_ROUND_FOREGROUND_XXXHDPI_SHA256="1afd6edb14cf2b6cb99adf069663272c318e3105c9b29917dabe932f791017e8"
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
