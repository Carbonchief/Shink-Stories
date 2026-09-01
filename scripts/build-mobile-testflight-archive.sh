#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_PATH="$ROOT_DIR/Shink.Mobile/Shink.Mobile.csproj"
FRAMEWORK="net10.0-ios"
RUNTIME_IDENTIFIER="ios-arm64"
CONFIGURATION="Release"
VERIFY_SCRIPT="$ROOT_DIR/scripts/verify-mobile-app-icons.sh"
IOS_ARTWORK="$ROOT_DIR/Shink.Mobile/obj/$CONFIGURATION/$FRAMEWORK/$RUNTIME_IDENTIFIER/resizetizer/r/Assets.xcassets/schink_appicon.appiconset/schink_appiconItunesArtwork.png"

/bin/bash "$VERIFY_SCRIPT" source

dotnet restore "$PROJECT_PATH" \
  -p:TargetFramework="$FRAMEWORK" \
  -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
  --nologo

dotnet clean "$PROJECT_PATH" \
  --framework "$FRAMEWORK" \
  --configuration "$CONFIGURATION" \
  -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
  -p:ValidateXcodeVersion=false \
  --nologo

set +e
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
publish_exit_code=$?
set -e

/bin/bash "$VERIFY_SCRIPT" ios-artwork "$IOS_ARTWORK"

if [[ "$publish_exit_code" -ne 0 ]]; then
  echo "The iOS icon is correct, but the TestFlight archive build failed. Apply the documented FinderInfo fallback only when that is the reported cause." >&2
  exit "$publish_exit_code"
fi

echo "TestFlight archive build completed with the approved teal app icon."
