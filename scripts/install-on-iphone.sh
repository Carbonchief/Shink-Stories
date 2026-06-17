#!/bin/zsh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_PATH="$REPO_ROOT/Shink.Mobile/Shink.Mobile.csproj"
BUILD_CONFIG="${BUILD_CONFIG:-Debug}"
DEVICE_NAME="${DEVICE_NAME:-Luan iPhone 15 Pro}"
DEVICE_UDID="${DEVICE_UDID:-}"
SIGNING_IDENTITY="${SIGNING_IDENTITY:-}"
APP_BUNDLE_PATH="$REPO_ROOT/Shink.Mobile/bin/$BUILD_CONFIG/net10.0-ios/ios-arm64/Shink.Mobile.app"
SIGNED_APP_PATH="/tmp/Shink.Mobile.app"

resolve_device_udid() {
  if [[ -n "$DEVICE_UDID" ]]; then
    echo "$DEVICE_UDID"
    return 0
  fi

  local line
  line="$(xcrun devicectl list devices | awk -v name="$DEVICE_NAME" '$0 ~ name { print; exit }')"
  if [[ -z "$line" ]]; then
    echo "Could not find paired device named '$DEVICE_NAME'." >&2
    exit 1
  fi

  echo "$line" | grep -Eo '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -n 1
}

resolve_signing_identity() {
  if [[ -n "$SIGNING_IDENTITY" ]]; then
    echo "$SIGNING_IDENTITY"
    return 0
  fi

  local line
  line="$(security find-identity -v -p codesigning | awk -F '"' '/Apple Development:/ { print $2; exit }')"
  if [[ -z "$line" ]]; then
    echo "Could not find an Apple Development signing identity." >&2
    exit 1
  fi

  echo "$line"
}

DEVICE_UDID="$(resolve_device_udid)"
SIGNING_IDENTITY="$(resolve_signing_identity)"

echo "Building for device '$DEVICE_NAME' ($DEVICE_UDID)..."
rm -rf "$REPO_ROOT/Shink.Mobile/bin/$BUILD_CONFIG/net10.0-ios" "$REPO_ROOT/Shink.Mobile/obj/$BUILD_CONFIG/net10.0-ios"

set +e
dotnet build "$PROJECT_PATH" \
  -f net10.0-ios \
  -c "$BUILD_CONFIG" \
  -p:ValidateXcodeVersion=false \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey="$SIGNING_IDENTITY" \
  -p:_DeviceName=":v2:udid=$DEVICE_UDID" \
  -v:minimal
build_exit_code=$?
set -e

if [[ ! -d "$APP_BUNDLE_PATH" ]]; then
  echo "The iPhone app bundle was not produced." >&2
  exit "${build_exit_code:-1}"
fi

echo "Preparing clean signed app bundle..."
rm -rf "$SIGNED_APP_PATH"
ditto --norsrc --noextattr "$APP_BUNDLE_PATH" "$SIGNED_APP_PATH"
/usr/bin/codesign \
  --force \
  --sign "$SIGNING_IDENTITY" \
  --entitlements "$SIGNED_APP_PATH/archived-expanded-entitlements.xcent" \
  "$SIGNED_APP_PATH"
/usr/bin/codesign --verify --deep --strict --verbose=2 "$SIGNED_APP_PATH"

echo "Installing on '$DEVICE_NAME'..."
xcrun devicectl device install app --device "$DEVICE_UDID" "$SIGNED_APP_PATH"

echo "Installed com.schink.stories.mobile on '$DEVICE_NAME'."
