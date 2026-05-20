#!/usr/bin/env bash
# Publishes self-contained single-file builds for all three targets.
# Usage: ./publish.sh              (all targets)
#        ./publish.sh win-x64      (single target)
set -euo pipefail

PROJ="Library.csproj"
OUT_ROOT="publish"
TARGETS=("win-x64" "osx-x64" "osx-arm64")

# If an argument was supplied, only build that target
if [[ $# -gt 0 ]]; then
  TARGETS=("$1")
fi

PROJ_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="MtgLibrary"
EXEC_NAME="Library"   # binary name produced by dotnet publish

for RID in "${TARGETS[@]}"; do
  OUT_DIR="$OUT_ROOT/$RID"
  STAGE_DIR="$OUT_DIR/.stage"
  ZIP_ABS="$PROJ_DIR/$OUT_ROOT/${APP_NAME}-${RID}.zip"
  echo ""
  echo "==> Publishing $RID"

  dotnet publish "$PROJ" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishReadyToRun=false \
    -o "$STAGE_DIR"

  if [[ "$RID" == osx-* ]]; then
    # ── Build .app bundle ─────────────────────────────────────────────────────
    APP_BUNDLE="$OUT_DIR/${APP_NAME}.app"
    rm -rf "$APP_BUNDLE"
    mkdir -p "$APP_BUNDLE/Contents/MacOS"
    mkdir -p "$APP_BUNDLE/Contents/Resources"

    # Move published files into Contents/MacOS
    cp -a "$STAGE_DIR/." "$APP_BUNDLE/Contents/MacOS/"
    chmod +x "$APP_BUNDLE/Contents/MacOS/$EXEC_NAME"

    # Write Info.plist
    cat > "$APP_BUNDLE/Contents/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>               <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>        <string>${APP_NAME}</string>
  <key>CFBundleIdentifier</key>         <string>com.mtglibrary.app</string>
  <key>CFBundleVersion</key>            <string>1.0.0</string>
  <key>CFBundleShortVersionString</key> <string>1.0.0</string>
  <key>CFBundlePackageType</key>        <string>APPL</string>
  <key>CFBundleExecutable</key>         <string>${EXEC_NAME}</string>
  <key>NSHighResolutionCapable</key>    <true/>
  <key>NSPrincipalClass</key>           <string>NSApplication</string>
  <key>LSMinimumSystemVersion</key>     <string>12.0</string>
</dict>
</plist>
PLIST

    echo "   Packaging -> $ZIP_ABS"
    rm -f "$ZIP_ABS"
    (cd "$OUT_DIR" && zip -r "$ZIP_ABS" "${APP_NAME}.app")
    echo "   Done -> ${ZIP_ABS##*/}"
  else
    # ── Windows: plain zip ────────────────────────────────────────────────────
    rm -rf "$OUT_DIR"
    mv "$STAGE_DIR" "$OUT_DIR"
    echo "   Zipping -> $ZIP_ABS"
    rm -f "$ZIP_ABS"
    (cd "$OUT_ROOT" && zip -r "$(basename "$ZIP_ABS")" "$RID")
    echo "   Done -> ${ZIP_ABS##*/}"
  fi

  rm -rf "$STAGE_DIR"
done

echo ""
echo "All done. Output in: $OUT_ROOT/"
