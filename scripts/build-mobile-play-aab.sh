#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/Shink.Mobile/Shink.Mobile.csproj"
ANDROID_FRAMEWORK="${SCHINK_ANDROID_PLAY_FRAMEWORK:-net10.0-android}"
KEYSTORE="${SCHINK_ANDROID_PLAY_UPLOAD_KEYSTORE:-$HOME/.android/schink-stories-play-upload.keystore}"
KEY_ALIAS="${SCHINK_ANDROID_PLAY_UPLOAD_KEY_ALIAS:-schink-stories-play-upload}"
KEYCHAIN_SERVICE="${SCHINK_ANDROID_PLAY_UPLOAD_KEYCHAIN_SERVICE:-Schink Stories Google Play Upload Key}"
KEYCHAIN_ACCOUNT="${SCHINK_ANDROID_PLAY_UPLOAD_KEYCHAIN_ACCOUNT:-schink-stories-play-upload}"
ICON_VERIFY_SCRIPT="$ROOT_DIR/scripts/verify-mobile-app-icons.sh"

/bin/bash "$ICON_VERIFY_SCRIPT" source

if [[ ! -f "$KEYSTORE" ]]; then
  echo "Missing Google Play upload keystore: $KEYSTORE" >&2
  exit 1
fi

KEY_PASSWORD="$(security find-generic-password -a "$KEYCHAIN_ACCOUNT" -s "$KEYCHAIN_SERVICE" -w)"

export SCHINK_ANDROID_PLAY_KEY_PASSWORD="$KEY_PASSWORD"

cleanup() {
  unset KEY_PASSWORD SCHINK_ANDROID_PLAY_KEY_PASSWORD
}
trap cleanup EXIT

dotnet restore "$PROJECT_PATH" \
  -p:TargetFramework="$ANDROID_FRAMEWORK" \
  -p:SchinkGooglePlayBuild=true \
  --nologo

dotnet clean "$PROJECT_PATH" \
  --framework "$ANDROID_FRAMEWORK" \
  --configuration Release \
  -p:SchinkGooglePlayBuild=true \
  --nologo

dotnet publish "$PROJECT_PATH" \
  --framework "$ANDROID_FRAMEWORK" \
  --configuration Release \
  -p:AndroidPackageFormat=aab \
  -p:SchinkGooglePlayBuild=true \
  --nologo

UNSIGNED_BUNDLE="$ROOT_DIR/Shink.Mobile/bin/Release/$ANDROID_FRAMEWORK/publish/com.schink.stories.mobile.aab"
if [[ ! -f "$UNSIGNED_BUNDLE" ]]; then
  echo "Unsigned Google Play bundle was not produced: $UNSIGNED_BUNDLE" >&2
  exit 1
fi

/bin/bash "$ICON_VERIFY_SCRIPT" android-aab "$UNSIGNED_BUNDLE"

APPLICATION_VERSION="$(sed -n 's:.*<ApplicationVersion>\(.*\)</ApplicationVersion>.*:\1:p' "$PROJECT_PATH" | head -1)"
if [[ -z "$APPLICATION_VERSION" ]]; then
  echo "Could not read ApplicationVersion from $PROJECT_PATH" >&2
  exit 1
fi

ARTIFACT_DIRECTORY="$ROOT_DIR/artifacts/mobile-play"
ARTIFACT_PATH="$ARTIFACT_DIRECTORY/schink-stories-mobile-v${APPLICATION_VERSION}-Signed.aab"
mkdir -p "$ARTIFACT_DIRECTORY"
cp "$UNSIGNED_BUNDLE" "$ARTIFACT_PATH"

jarsigner \
  -keystore "$KEYSTORE" \
  -storetype PKCS12 \
  -storepass:env SCHINK_ANDROID_PLAY_KEY_PASSWORD \
  -keypass:env SCHINK_ANDROID_PLAY_KEY_PASSWORD \
  -sigalg SHA256withRSA \
  -digestalg SHA-256 \
  "$ARTIFACT_PATH" \
  "$KEY_ALIAS"

echo "Google Play bundle: $ARTIFACT_PATH"
