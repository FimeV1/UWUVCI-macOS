#!/bin/bash
# Builds UWUVCI.app and the zips to hand out. Run on a Mac with the .NET 8 SDK and the Xcode command line tools.
#   ./build-mac.sh            both Apple Silicon and Intel
#   ./build-mac.sh arm64      one architecture (arm64 or x64)
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

VERSION="1.0.0"
ARCHS=("${@:-arm64 x64}")
[ $# -eq 0 ] && ARCHS=(arm64 x64)
DIST="$PWD/dist"
rm -rf "$DIST"; mkdir -p "$DIST"

echo "== icon"
dotnet build src/UWUVCI.Cli -c Release -v q --nologo | grep -E "error|Warn.*[1-9]" || true
ICONSET="$DIST/UWUVCI.iconset"; mkdir -p "$ICONSET"
dotnet src/UWUVCI.Cli/bin/Release/net8.0/uwuvci-cli.dll icon --in "$PWD/src/UWUVCI.App/Assets/app.ico" --out "$DIST/icon-1024.png" --size 1024 --home "$DIST/.clihome" >/dev/null
for s in 16 32 128 256 512; do
  sips -z $s $s "$DIST/icon-1024.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$DIST/icon-1024.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$DIST/UWUVCI.icns"
rm -rf "$ICONSET" "$DIST/.clihome" "$DIST/icon-1024.png"

for ARCH in "${ARCHS[@]}"; do
  echo "== $ARCH"
  STAGE="$DIST/$ARCH"; APP="$STAGE/UWUVCI.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

  dotnet publish src/UWUVCI.App -c Release -r "osx-$ARCH" --self-contained true \
    -p:DebugType=none -p:DebugSymbols=false -o "$APP/Contents/MacOS" -v q --nologo | grep -E " error |rror\(s\)" || true
  [ -f "$APP/Contents/MacOS/UWUVCI" ] || { echo "publish failed for $ARCH"; exit 1; }

  cp "$DIST/UWUVCI.icns" "$APP/Contents/Resources/UWUVCI.icns"
  cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>UWUVCI</string>
  <key>CFBundleDisplayName</key><string>UWUVCI</string>
  <key>CFBundleIdentifier</key><string>org.uwuvci.mac</string>
  <key>CFBundleExecutable</key><string>UWUVCI</string>
  <key>CFBundleIconFile</key><string>UWUVCI</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>UWUVCI config</string>
      <key>CFBundleTypeExtensions</key><array><string>uwuvci</string></array>
      <key>CFBundleTypeRole</key><string>Viewer</string>
    </dict>
  </array>
</dict>
</plist>
PLIST

  chmod +x "$APP/Contents/MacOS/UWUVCI" "$APP/Contents/MacOS/native/"*
  xattr -cr "$APP"
  # Ad-hoc signature: required for anything to run on Apple Silicon. It is not a Developer ID, so Gatekeeper
  # still asks the user to approve the app the first time (see the read-me that goes into the zip).
  codesign --force --deep -s - "$APP" 2>&1 | grep -v "replacing existing signature" || true
  codesign --verify --deep "$APP"

  cp "$PWD/packaging/READ ME FIRST.txt" "$STAGE/"
  NAME="UWUVCI-macOS-$([ "$ARCH" = arm64 ] && echo AppleSilicon || echo Intel)"
  mv "$STAGE" "$DIST/$NAME"
  # ditto keeps the executable bits and the signature intact; plain zip tools on other systems do not.
  (cd "$DIST" && ditto -c -k --keepParent "$NAME" "$NAME.zip")
  echo "   $(du -sh "$DIST/$NAME.zip" | cut -f1)  $DIST/$NAME.zip"
done
rm -f "$DIST/UWUVCI.icns"
