#!/usr/bin/env bash
set -euo pipefail

# Creates an unsigned local .app bundle. Distribution signing/notarization is a
# release concern and intentionally is not attempted here.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-osx-arm64}"
output="${2:-$root/artifacts/macos/$rid}"
publish="$output/publish"
bundle="$output/G915 Fix.app"
temporary_directory="$(mktemp -d)"
asset_catalog_info="$temporary_directory/asset-catalog-info.plist"
trap 'rm -rf "$temporary_directory"' EXIT

if [[ "$rid" != "osx-arm64" && "$rid" != "osx-x64" ]]; then
  echo "RID must be osx-arm64 or osx-x64." >&2
  exit 2
fi

if ! actool="$(xcrun --find actool 2>/dev/null)"; then
  echo "Xcode Command Line Tools with actool are required to package the macOS app." >&2
  exit 1
fi

rm -rf "$output"
mkdir -p "$publish" "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
dotnet publish "$root/src/G915Fix.MacOS/G915Fix.MacOS.csproj" \
  --configuration Release --runtime "$rid" --self-contained false --output "$publish"
cp -R "$publish/." "$bundle/Contents/MacOS/"

# Compile the authored Icon Composer asset directly. actool creates Assets.car
# and the legacy ICNS fallback consumed by older macOS releases.
"$actool" --compile "$bundle/Contents/Resources" \
  --platform macosx --minimum-deployment-target 13.0 --app-icon app-icon \
  --output-partial-info-plist "$asset_catalog_info" \
  "$root/res/app-icon.icon"

cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>G915 Fix</string>
  <key>CFBundleExecutable</key><string>G915Fix.MacOS</string>
  <key>CFBundleIdentifier</key><string>com.twcrews.g915fix</string>
  <key>CFBundleName</key><string>G915 Fix</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

# actool supplies CFBundleIconFile and CFBundleIconName for the compiled asset.
/usr/libexec/PlistBuddy -c "Merge $asset_catalog_info" "$bundle/Contents/Info.plist"

echo "Created unsigned bundle: $bundle"
