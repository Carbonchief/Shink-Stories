#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/Shink.Mobile/Shink.Mobile.csproj"
ANDROID_FRAMEWORK="${SCHINK_ANDROID_DEMO_FRAMEWORK:-net10.0-android}"
ARTIFACT_DIR="$ROOT_DIR/artifacts/mobile-demo"
KEYCHAIN_SERVICE="${SCHINK_ANDROID_DEMO_KEYCHAIN_SERVICE:-Schink Stories Android Demo Keystore}"
KEYCHAIN_ACCOUNT="${SCHINK_ANDROID_DEMO_KEYCHAIN_ACCOUNT:-schink-stories-demo}"

export SCHINK_ANDROID_DEMO_KEYSTORE="${SCHINK_ANDROID_DEMO_KEYSTORE:-$HOME/.android/schink-stories-demo.keystore}"
export SCHINK_ANDROID_DEMO_KEY_ALIAS="${SCHINK_ANDROID_DEMO_KEY_ALIAS:-schink-stories-demo}"

if [[ ! -f "$SCHINK_ANDROID_DEMO_KEYSTORE" ]]; then
  echo "Missing demo keystore: $SCHINK_ANDROID_DEMO_KEYSTORE" >&2
  exit 1
fi

if [[ -z "${SCHINK_ANDROID_DEMO_STORE_PASS:-}" ]]; then
  SCHINK_ANDROID_DEMO_STORE_PASS="$(security find-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$KEYCHAIN_SERVICE" -w)"
  export SCHINK_ANDROID_DEMO_STORE_PASS
fi

if [[ -z "${SCHINK_ANDROID_DEMO_KEY_PASS:-}" ]]; then
  export SCHINK_ANDROID_DEMO_KEY_PASS="$SCHINK_ANDROID_DEMO_STORE_PASS"
fi

dotnet publish "$PROJECT_PATH" \
  -f "$ANDROID_FRAMEWORK" \
  -c Release \
  -p:AndroidPackageFormat=apk \
  "$@"

APPLICATION_VERSION="$(sed -n 's:.*<ApplicationVersion>\(.*\)</ApplicationVersion>.*:\1:p' "$PROJECT_PATH" | head -1)"
SIGNED_APK="$ROOT_DIR/Shink.Mobile/bin/Release/$ANDROID_FRAMEWORK/publish/com.schink.stories.mobile-Signed.apk"
OUTPUT_APK="$ARTIFACT_DIR/schink-stories-mobile-demo-release-v${APPLICATION_VERSION}-huawei.apk"

if [[ -z "$APPLICATION_VERSION" ]]; then
  echo "Could not read ApplicationVersion from $PROJECT_PATH" >&2
  exit 1
fi

if [[ ! -f "$SIGNED_APK" ]]; then
  echo "Missing signed APK: $SIGNED_APK" >&2
  exit 1
fi

mkdir -p "$ARTIFACT_DIR"
cp "$SIGNED_APK" "$OUTPUT_APK"
echo "Demo APK: $OUTPUT_APK"
