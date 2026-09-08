#!/usr/bin/env bash
set -euo pipefail

# Creates an unsigned local .app bundle. Distribution signing/notarization is a
# release concern and intentionally is not attempted here.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-osx-arm64}"
output="${2:-$root/artifacts/macos/$rid}"
publish="$output/publish"
bundle="$output/G915 Fix.app"
iconset="$(mktemp -d)/G915Fix.iconset"
trap 'rm -rf "$(dirname "$iconset")"' EXIT

if [[ "$rid" != "osx-arm64" && "$rid" != "osx-x64" ]]; then
  echo "RID must be osx-arm64 or osx-x64." >&2
  exit 2
fi

rm -rf "$output"
mkdir -p "$publish" "$bundle/Contents/MacOS" "$bundle/Contents/Resources" "$iconset"
dotnet publish "$root/src/G915Fix.MacOS/G915Fix.MacOS.csproj" \
  --configuration Release --runtime "$rid" --self-contained false --output "$publish"
cp -R "$publish/." "$bundle/Contents/MacOS/"

# app-icon.icon is the source of truth. Its vector asset is rasterized into the
# legacy ICNS representation that LaunchServices currently consumes for .app bundles.
source_icon="$root/res/app-icon.icon/Assets/app-icon-gradient.svg"
source_png="$(dirname "$iconset")/source.png"
sips -s format png "$source_icon" --out "$source_png" >/dev/null
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$source_png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  doubled=$((size * 2))
  sips -z "$doubled" "$doubled" "$source_png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$bundle/Contents/Resources/app-icon.icns"
cp -R "$root/res/app-icon.icon" "$bundle/Contents/Resources/app-icon.icon"

cat > "$bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleDevelopmentRegion</key><string>en</string>
  <key>CFBundleDisplayName</key><string>G915 Fix</string>
  <key>CFBundleExecutable</key><string>G915Fix.MacOS</string>
  <key>CFBundleIdentifier</key><string>com.twcrews.g915fix</string>
  <key>CFBundleIconFile</key><string>app-icon</string>
  <key>CFBundleName</key><string>G915 Fix</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

echo "Created unsigned bundle: $bundle"
