#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/Shink.Mobile/Shink.Mobile.csproj"
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
  -f net10.0-android \
  -c Release \
  -p:AndroidPackageFormat=apk \
  "$@"
