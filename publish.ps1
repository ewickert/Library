# Publishes self-contained single-file builds for all three targets.
# Usage: .\publish.ps1              (all targets)
#        .\publish.ps1 win-x64      (single target)
param(
    [string]$Target = ""
)

$ErrorActionPreference = "Stop"

$proj      = "Library.csproj"
$outRoot   = "publish"
$targets   = if ($Target) { @($Target) } else { @("win-x64", "osx-x64", "osx-arm64") }

$appName  = "MtgLibrary"
$execName = "Library"   # binary name produced by dotnet publish

foreach ($rid in $targets) {
    $outDir   = Join-Path $outRoot $rid
    $stageDir = Join-Path $outDir  ".stage"
    $zipFile  = Join-Path $outRoot "${appName}-${rid}.zip"
    Write-Host ""
    Write-Host "==> Publishing $rid" -ForegroundColor Cyan

    dotnet publish $proj `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishReadyToRun=false `
        -o $stageDir

    if ($rid -like "osx-*") {
        # ── Build .app bundle ─────────────────────────────────────────────────
        $appBundle  = Join-Path $outDir "${appName}.app"
        $macOSDir   = Join-Path $appBundle "Contents\MacOS"
        $resDir     = Join-Path $appBundle "Contents\Resources"

        if (Test-Path $appBundle) { Remove-Item $appBundle -Recurse -Force }
        New-Item -ItemType Directory -Path $macOSDir  | Out-Null
        New-Item -ItemType Directory -Path $resDir    | Out-Null

        # Copy published files into Contents/MacOS
        Copy-Item "$stageDir\*" $macOSDir -Recurse

        # Write Info.plist
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>               <string>$appName</string>
  <key>CFBundleDisplayName</key>        <string>$appName</string>
  <key>CFBundleIdentifier</key>         <string>com.mtglibrary.app</string>
  <key>CFBundleVersion</key>            <string>1.0.0</string>
  <key>CFBundleShortVersionString</key> <string>1.0.0</string>
  <key>CFBundlePackageType</key>        <string>APPL</string>
  <key>CFBundleExecutable</key>         <string>$execName</string>
  <key>NSHighResolutionCapable</key>    <true/>
  <key>NSPrincipalClass</key>           <string>NSApplication</string>
  <key>LSMinimumSystemVersion</key>     <string>12.0</string>
</dict>
</plist>
"@
        $plist | Set-Content (Join-Path $appBundle "Contents\Info.plist") -Encoding UTF8

        Write-Host "   Packaging -> $zipFile" -ForegroundColor Cyan
        if (Test-Path $zipFile) { Remove-Item $zipFile }
        Compress-Archive -Path $appBundle -DestinationPath $zipFile
        Write-Host "   Done -> $zipFile" -ForegroundColor Green
    } else {
        # ── Windows: plain zip ────────────────────────────────────────────────
        if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
        Move-Item $stageDir $outDir
        Write-Host "   Zipping -> $zipFile" -ForegroundColor Cyan
        if (Test-Path $zipFile) { Remove-Item $zipFile }
        Compress-Archive -Path "$outDir\*" -DestinationPath $zipFile
        Write-Host "   Done -> $zipFile" -ForegroundColor Green
    }

    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
}

Write-Host ""
Write-Host "All done. Output in: $outRoot/" -ForegroundColor Green
