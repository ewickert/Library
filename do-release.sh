#!/usr/bin/env bash
# Creates a GitHub release and uploads publish artifacts.
# Usage: ./do-release.sh <tag>              e.g. ./do-release.sh v1.2.0
#        ./do-release.sh <tag> --skip-build (use existing publish/ output)
#        ./do-release.sh <tag> --draft      (create as draft)
#        ./do-release.sh <tag> --prerelease
set -eo pipefail

# ── Args ───────────────────────────────────────────────────────────────────────
if [[ $# -lt 1 ]]; then
  echo "Usage: ./do-release.sh <tag> [--skip-build] [--draft] [--prerelease]"
  echo "  e.g. ./do-release.sh v1.2.0"
  exit 1
fi

TAG="$1"; shift
SKIP_BUILD=false
GH_FLAGS=()
ARTIFACTS=()

for arg in "$@"; do
  case "$arg" in
    --skip-build) SKIP_BUILD=true ;;
    --draft)      GH_FLAGS+=(--draft) ;;
    --prerelease) GH_FLAGS+=(--prerelease) ;;
    *) echo "Unknown flag: $arg"; exit 1 ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUT_ROOT="$SCRIPT_DIR/publish"
APP_NAME="MtgLibrary"

# ── Prerequisite check ─────────────────────────────────────────────────────────
if ! command -v gh &>/dev/null; then
  echo "ERROR: GitHub CLI (gh) not found. Install from https://cli.github.com/"
  exit 1
fi

# ── Build ──────────────────────────────────────────────────────────────────────
if [[ "$SKIP_BUILD" == false ]]; then
  echo "==> Building all targets…"
  "$SCRIPT_DIR/publish.sh"
fi

# ── Collect artifacts ──────────────────────────────────────────────────────────
ARTIFACTS=()
for RID in "win-x64" "osx-x64" "osx-arm64"; do
  ZIP="$OUT_ROOT/${APP_NAME}-${RID}.zip"
  if [[ -f "$ZIP" ]]; then
    ARTIFACTS+=("$ZIP")
    echo "   Found: ${ZIP##*/}"
  else
    echo "   WARNING: missing artifact $ZIP — skipping"
  fi
done

if [[ ${#ARTIFACTS[@]} -eq 0 ]]; then
  echo "ERROR: no artifacts found in $OUT_ROOT/. Run publish.sh first or remove --skip-build."
  exit 1
fi

# ── Tag ────────────────────────────────────────────────────────────────────────
# Create and push the tag if it doesn't exist locally/remotely
cd "$SCRIPT_DIR"
if git rev-parse "$TAG" &>/dev/null; then
  echo "==> Tag $TAG already exists locally, pushing…"
  git push origin "$TAG" || true
else
  echo "==> Creating git tag $TAG on current commit…"
  git tag "$TAG"
  git push origin "$TAG"
fi

# ── GitHub release ────────────────────────────────────────────────────────────
echo ""
echo "==> Creating GitHub release $TAG…"
GH_CMD=(gh release create "$TAG" --title "$APP_NAME $TAG" --generate-notes)
for f in "${GH_FLAGS[@]}"; do GH_CMD+=("$f"); done
for a in "${ARTIFACTS[@]}"; do GH_CMD+=("$a"); done
"${GH_CMD[@]}"

echo ""
echo "Release $TAG published!"
echo "https://github.com/ewickert/Library/releases/tag/$TAG"
