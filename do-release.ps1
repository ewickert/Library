# Creates a GitHub release and uploads publish artifacts.
# Usage: .\do-release.ps1 <tag>              e.g. .\do-release.ps1 v1.2.0
#        .\do-release.ps1 <tag> -SkipBuild   (use existing publish\ output)
#        .\do-release.ps1 <tag> -Draft
#        .\do-release.ps1 <tag> -Prerelease
param(
    [Parameter(Mandatory)][string]$Tag,
    [switch]$SkipBuild,
    [switch]$Draft,
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$outRoot   = Join-Path $scriptDir "publish"
$appName   = "MtgLibrary"

# ── Prerequisite check ─────────────────────────────────────────────────────────
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) not found. Install from https://cli.github.com/"
    exit 1
}

# ── Build ──────────────────────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "==> Building all targets…" -ForegroundColor Cyan
    & "$scriptDir\publish.ps1"
}

# ── Collect artifacts ──────────────────────────────────────────────────────────
$artifacts = @()
foreach ($rid in @("win-x64", "osx-x64", "osx-arm64")) {
    $zip = Join-Path $outRoot "${appName}-${rid}.zip"
    if (Test-Path $zip) {
        $artifacts += $zip
        Write-Host "   Found: $(Split-Path $zip -Leaf)"
    } else {
        Write-Warning "Missing artifact $zip — skipping"
    }
}

if ($artifacts.Count -eq 0) {
    Write-Error "No artifacts found in $outRoot\. Run publish.ps1 first or remove -SkipBuild."
    exit 1
}

# ── Tag ────────────────────────────────────────────────────────────────────────
Push-Location $scriptDir
try {
    $tagExists = git rev-parse $Tag 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "==> Tag $Tag already exists locally, reusing."
    } else {
        Write-Host "==> Creating git tag $Tag on current commit…" -ForegroundColor Cyan
        git tag $Tag
        git push origin $Tag
    }
} finally { Pop-Location }

# ── GitHub release ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==> Creating GitHub release $Tag…" -ForegroundColor Cyan

$ghArgs = @("release", "create", $Tag, "--title", "$appName $Tag", "--generate-notes")
if ($Draft)      { $ghArgs += "--draft" }
if ($Prerelease) { $ghArgs += "--prerelease" }
$ghArgs += $artifacts

& gh @ghArgs

Write-Host ""
Write-Host "Release $Tag published!" -ForegroundColor Green
Write-Host "https://github.com/ewickert/Library/releases/tag/$Tag"
