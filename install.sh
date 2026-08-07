#!/bin/sh
# Install or update Proving Ground.
#   curl -fsSL https://zottiben.github.io/proving-ground/install.sh | sh
# Once installed, `proving-ground update` re-runs this same script for you.
#
# Options:
#   --check           report whether an update is available, install nothing
#   --force           reinstall even when the latest version is already installed
#   --version <tag>   install a specific release tag (e.g. v0.1.0)
#   -h, --help        show this help
#
# Installs to ~/.local/share/proving-ground and puts `proving-ground` (and the short
# alias `pg`) on your PATH. Needs python3 and curl.
set -eu

REPO="zottiben/proving-ground"
DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/proving-ground"
BIN_DIR="${PG_BIN_DIR:-$HOME/.local/bin}"

usage() {
  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
}

CHECK_ONLY=0
FORCE=0
VERSION=""
while [ $# -gt 0 ]; do
  case "$1" in
    --check) CHECK_ONLY=1 ;;
    --force) FORCE=1 ;;
    --version)
      shift
      [ $# -gt 0 ] || { echo "--version needs a release tag" >&2; exit 2; }
      VERSION="$1"
      ;;
    --version=*) VERSION="${1#--version=}" ;;
    -h | --help) usage; exit 0 ;;
    "") ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

need() {
  command -v "$1" >/dev/null 2>&1 || { echo "Proving Ground needs $1 on your PATH." >&2; exit 1; }
}
need curl
need tar

PYTHON=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1; then
    if "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3,10) else 1)' 2>/dev/null; then
      PYTHON="$candidate"
      break
    fi
  fi
done
[ -n "$PYTHON" ] || { echo "Proving Ground needs Python 3.10 or newer." >&2; exit 1; }

# The repository may be private, in which case the API and the asset download both need
# a token. An authenticated gh is the least effort for the person running this; an
# explicit token still wins if one is set.
TOKEN="${GITHUB_TOKEN:-${GH_TOKEN:-}}"
if [ -z "$TOKEN" ] && command -v gh >/dev/null 2>&1; then
  TOKEN="$(gh auth token 2>/dev/null || true)"
fi

api() {
  if [ -n "$TOKEN" ]; then
    curl -fsSL -H "Authorization: Bearer $TOKEN" -H "Accept: application/vnd.github+json" "$1"
  else
    curl -fsSL -H "Accept: application/vnd.github+json" "$1"
  fi
}

# --- what is installed, and what is available ---------------------------------------

CURRENT="${PG_CURRENT_VERSION:-}"
if [ -z "$CURRENT" ] && [ -f "$DATA_DIR/current/VERSION" ]; then
  CURRENT="$(cat "$DATA_DIR/current/VERSION")"
fi

if [ -z "$VERSION" ]; then
  RELEASE_JSON="$(api "https://api.github.com/repos/${REPO}/releases/latest" 2>/dev/null || true)"
  VERSION="$(printf '%s' "$RELEASE_JSON" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -1)"

  if [ -z "$VERSION" ]; then
    echo "Could not find a release for ${REPO}." >&2
    if [ -z "$TOKEN" ]; then
      echo "The repository may be private. Authenticate with 'gh auth login', or set GITHUB_TOKEN." >&2
    fi
    exit 1
  fi
fi

if [ "$CHECK_ONLY" = "1" ]; then
  if [ "$CURRENT" = "$VERSION" ]; then
    echo "Proving Ground $CURRENT is up to date."
  else
    echo "Proving Ground ${CURRENT:-not installed} -> $VERSION available."
  fi
  exit 0
fi

if [ "$CURRENT" = "$VERSION" ] && [ "$FORCE" = "0" ]; then
  echo "Proving Ground $VERSION is already installed. Use --force to reinstall."
  exit 0
fi

# --- download ------------------------------------------------------------------------

TARBALL="proving-ground-${VERSION}.tar.gz"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT TERM

echo "Downloading Proving Ground $VERSION"

DOWNLOADED=0
if [ -n "$TOKEN" ]; then
  # A private repo's assets are only reachable through the API, by asset id.
  ASSETS_JSON="$(api "https://api.github.com/repos/${REPO}/releases/tags/${VERSION}" || true)"
  ASSET_ID="$(printf '%s' "$ASSETS_JSON" \
    | tr '{' '\n' \
    | grep -F "\"name\": \"${TARBALL}\"" \
    | sed -n 's/.*"id": *\([0-9]*\).*/\1/p' \
    | head -1)"

  if [ -n "$ASSET_ID" ]; then
    curl -fsSL -H "Authorization: Bearer $TOKEN" -H "Accept: application/octet-stream" \
      "https://api.github.com/repos/${REPO}/releases/assets/${ASSET_ID}" -o "$TMP/$TARBALL" \
      && DOWNLOADED=1
  fi
fi

if [ "$DOWNLOADED" = "0" ]; then
  curl -fsSL "https://github.com/${REPO}/releases/download/${VERSION}/${TARBALL}" \
    -o "$TMP/$TARBALL" || {
      echo "Could not download $TARBALL." >&2
      [ -z "$TOKEN" ] && echo "If the repository is private, run 'gh auth login' or set GITHUB_TOKEN." >&2
      exit 1
    }
fi

if curl -fsSL "https://github.com/${REPO}/releases/download/${VERSION}/checksums.txt" \
     -o "$TMP/checksums.txt" 2>/dev/null; then
  if command -v shasum >/dev/null 2>&1; then
    (cd "$TMP" && grep -F "$TARBALL" checksums.txt | shasum -a 256 -c - >/dev/null 2>&1) \
      && echo "Checksum verified" \
      || { echo "Checksum mismatch for $TARBALL." >&2; exit 1; }
  fi
fi

# --- install -------------------------------------------------------------------------

TARGET="$DATA_DIR/versions/$VERSION"
rm -rf "$TARGET"
mkdir -p "$TARGET"
tar -xzf "$TMP/$TARBALL" -C "$TARGET" --strip-components=1

echo "Building the agent bridge"
"$PYTHON" -m venv "$TARGET/.venv" >/dev/null
"$TARGET/.venv/bin/pip" install --quiet --upgrade pip >/dev/null 2>&1 || true
"$TARGET/.venv/bin/pip" install --quiet "$TARGET/mcp" || {
  echo "Could not install the Python dependencies." >&2
  exit 1
}

# `current` is what Unity project manifests point at, so an update swings one symlink
# instead of rewriting every project that has ever been set up.
ln -sfn "$TARGET" "$DATA_DIR/current"

mkdir -p "$BIN_DIR"
for name in proving-ground pg; do
  cat > "$BIN_DIR/$name" <<EOF
#!/bin/sh
exec "$DATA_DIR/current/.venv/bin/proving-ground" "\$@"
EOF
  chmod +x "$BIN_DIR/$name"
done

echo
echo "Proving Ground $VERSION installed."

case ":$PATH:" in
  *":$BIN_DIR:"*) ;;
  *)
    echo
    echo "  $BIN_DIR is not on your PATH. Add it:"
    echo "    echo 'export PATH=\"$BIN_DIR:\$PATH\"' >> ~/.zshrc && exec zsh"
    ;;
esac

cat <<'EOF'

  Next: go to your game and run setup.

    cd ~/path/to/your-game
    proving-ground setup

EOF
