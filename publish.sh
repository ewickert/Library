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
  ZIP_ABS="$PROJ_DIR/$OUT_ROOT/${APP_NAME}-${RID}.zip"
  echo ""
  echo "==> Publishing $RID"

  if [[ "$RID" == osx-* ]]; then
    # Publish into a temp staging dir so we can wrap into a .app bundle
    STAGE_DIR="$OUT_ROOT/.stage-$RID"
    rm -rf "$STAGE_DIR"
    PUBLISH_OUT="$STAGE_DIR"
  else
    rm -rf "$OUT_DIR"
    PUBLISH_OUT="$OUT_DIR"
  fi

  dotnet publish "$PROJ" \
    -c Release \
    -r "$RID" \
    -f net10.0 \
    --self-contained true \
    -p:UseMonoRuntime=false \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:PublishReadyToRun=false \
    -o "$PUBLISH_OUT"

  if [[ "$RID" == osx-* ]]; then
    # ── Build .app bundle ─────────────────────────────────────────────────────
    APP_BUNDLE="$OUT_DIR/${APP_NAME}.app"
    rm -rf "$APP_BUNDLE"
    mkdir -p "$APP_BUNDLE/Contents/MacOS"
    mkdir -p "$APP_BUNDLE/Contents/Resources"

    # Copy published files into Contents/MacOS
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
    rm -rf "$STAGE_DIR"
  else
    # ── Windows: plain zip ────────────────────────────────────────────────────
    echo "   Zipping -> $ZIP_ABS"
    rm -f "$ZIP_ABS"
    (cd "$OUT_ROOT" && zip -r "$(basename "$ZIP_ABS")" "$RID")
    echo "   Done -> ${ZIP_ABS##*/}"
  fi
done

echo ""
echo "All done. Output in: $OUT_ROOT/"
