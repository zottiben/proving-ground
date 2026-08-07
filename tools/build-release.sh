#!/usr/bin/env bash
# Builds the release tarball that install.sh downloads.
#
#   tools/build-release.sh v0.1.0 [outdir]
#
# Produces <outdir>/proving-ground-<version>.tar.gz and checksums.txt.
set -euo pipefail

VERSION="${1:-}"
OUT="${2:-dist}"

if [[ -z "$VERSION" ]]; then
  echo "Usage: tools/build-release.sh <version> [outdir]" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="$(mktemp -d)"
NAME="proving-ground-${VERSION}"
trap 'rm -rf "$STAGE"' EXIT

# The Editor's update notice compares package.json against the release tag, so a drift
# between them shows every user a permanent phantom update. Catch it here.
PACKAGE_VERSION="$(python3 -c "import json;print(json.load(open('$ROOT/packages/com.zottiben.provingground/package.json'))['version'])")"
if [[ "${VERSION#v}" != "$PACKAGE_VERSION" ]]; then
  echo "Version mismatch: tag is ${VERSION} but package.json says ${PACKAGE_VERSION}." >&2
  echo "Update the package.json version to ${VERSION#v} and try again." >&2
  exit 1
fi

mkdir -p "$STAGE/$NAME"

# The layout the installed CLI expects: package/, skills/, mcp/, VERSION.
cp -R "$ROOT/packages/com.zottiben.provingground" "$STAGE/$NAME/package"
cp -R "$ROOT/skills" "$STAGE/$NAME/skills"
cp -R "$ROOT/mcp" "$STAGE/$NAME/mcp"
cp "$ROOT/install.sh" "$STAGE/$NAME/install.sh"
cp "$ROOT/README.md" "$STAGE/$NAME/README.md"
cp "$ROOT/LICENSE" "$STAGE/$NAME/LICENSE"
printf '%s\n' "$VERSION" > "$STAGE/$NAME/VERSION"

# Development leftovers must not ship: a baked virtualenv would carry absolute paths
# from the build machine and break on someone else's.
rm -rf "$STAGE/$NAME/mcp/.venv" "$STAGE/$NAME/mcp/uv.lock"
find "$STAGE/$NAME" -name '__pycache__' -type d -prune -exec rm -rf {} + 2>/dev/null || true
find "$STAGE/$NAME" -name '.DS_Store' -delete 2>/dev/null || true

mkdir -p "$ROOT/$OUT"
TARBALL="$ROOT/$OUT/${NAME}.tar.gz"
tar -czf "$TARBALL" -C "$STAGE" "$NAME"

(cd "$ROOT/$OUT" && shasum -a 256 "${NAME}.tar.gz" > checksums.txt)

echo "Built $TARBALL"
echo "  $(du -h "$TARBALL" | cut -f1)"
cat "$ROOT/$OUT/checksums.txt"
