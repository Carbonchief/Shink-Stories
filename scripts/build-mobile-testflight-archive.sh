#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FRAMEWORK="net10.0-ios"
RUNTIME_IDENTIFIER="ios-arm64"
CONFIGURATION="Release"
VERIFY_SCRIPT="$ROOT_DIR/scripts/verify-mobile-app-icons.sh"
BUILD_ROOT="$(mktemp -d /private/tmp/schink-ios-build.XXXXXX)"
SOURCE_ROOT="$BUILD_ROOT/source"
PROJECT_PATH="$SOURCE_ROOT/Shink.Mobile/Shink.Mobile.csproj"
IOS_ARTWORK="$SOURCE_ROOT/Shink.Mobile/obj/$CONFIGURATION/$FRAMEWORK/$RUNTIME_IDENTIFIER/resizetizer/r/Assets.xcassets/schink_appicon.appiconset/schink_appiconItunesArtwork.png"
ARCHIVES_ROOT="$HOME/Library/Developer/Xcode/Archives"
ARCHIVE_STARTED_EPOCH="$(date +%s)"

/bin/bash "$VERIFY_SCRIPT" source

# Build from a metadata-clean source copy. This avoids the File Provider
# xattrs that can be reattached to generated bundles in the synced checkout.
mkdir -p "$SOURCE_ROOT"
rsync -a \
  --exclude '.git' \
  --exclude 'bin' \
  --exclude 'obj' \
  "$ROOT_DIR/" "$SOURCE_ROOT/"
/usr/bin/xattr -cr "$SOURCE_ROOT"

dotnet restore "$PROJECT_PATH" \
  -p:TargetFramework="$FRAMEWORK" \
  -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
  --nologo

publish_ios() {
  dotnet publish "$PROJECT_PATH" \
    --framework "$FRAMEWORK" \
    --configuration "$CONFIGURATION" \
    -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
    -p:ArchiveOnBuild=true \
    -p:ValidateXcodeVersion=false \
    -p:CodesignKey='Apple Distribution: SCHINK PTY. LTD. (6DP8F4CY29)' \
    -p:CodesignTeam=6DP8F4CY29 \
    -p:CodesignProvision='Schink Stories App Store Connect 2026' \
    --nologo
}

publish_ios

/bin/bash "$VERIFY_SCRIPT" ios-artwork "$IOS_ARTWORK"

archive_dir="$(find "$ARCHIVES_ROOT" -maxdepth 2 -type d -name '*.xcarchive' -exec stat -f '%m %N' {} + | sort -n | tail -1 | sed 's/^[0-9]* //')"
if [[ -z "$archive_dir" || ! -d "$archive_dir/Products/Applications/Shink.Mobile.app" ]]; then
  echo "The iOS publish completed, but no usable Xcode archive was produced." >&2
  exit 1
fi

archive_mtime="$(stat -f '%m' "$archive_dir")"
if [[ "$archive_mtime" -lt "$ARCHIVE_STARTED_EPOCH" ]]; then
  echo "The iOS publish completed, but it did not produce a new Xcode archive." >&2
  exit 1
fi

/usr/bin/xattr -cr "$archive_dir"
/usr/bin/codesign --verify --deep --strict --verbose=2 "$archive_dir/Products/Applications/Shink.Mobile.app"
if [[ ! -f "$archive_dir/Products/Applications/Shink.Mobile.app/PrivacyInfo.xcprivacy" ]]; then
  echo "The Xcode archive is missing PrivacyInfo.xcprivacy." >&2
  exit 1
fi

echo "TestFlight archive build completed with the approved teal app icon."
