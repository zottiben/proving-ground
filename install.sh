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
# PG_DATA_DIR and PG_BIN_DIR let an existing install update itself in place. Without
# them an update run from a non-default location reinstalls to the default one, leaving
# the launcher pointing at the old version forever.
DATA_DIR="${PG_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/proving-ground}"
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

# macOS still ships Python 3.9 at /usr/bin/python3, so finding "a python3" is not
# enough. Check the usual places a newer one lives before giving up, since a working
# interpreter is almost always present just not first on PATH.
usable_python() {
  [ -x "$1" ] || command -v "$1" >/dev/null 2>&1 || return 1
  "$1" -c 'import sys; sys.exit(0 if sys.version_info >= (3,10) else 1)' 2>/dev/null
}

PYTHON=""
for candidate in \
  python3 python3.13 python3.12 python3.11 python3.10 python \
  /opt/homebrew/bin/python3 /usr/local/bin/python3
do
  if usable_python "$candidate"; then
    PYTHON="$candidate"
    break
  fi
done

if [ -z "$PYTHON" ] && command -v uv >/dev/null 2>&1; then
  UV_PYTHON="$(uv python find 2>/dev/null || true)"
  usable_python "$UV_PYTHON" && PYTHON="$UV_PYTHON"
fi

if [ -z "$PYTHON" ]; then
  cat >&2 <<'MSG'
Proving Ground needs Python 3.10 or newer, and could not find one.

macOS ships 3.9, which is too old. Install a newer one with either:

    brew install python
    curl -LsSf https://astral.sh/uv/install.sh | sh   # then: uv python install 3.12

Then run the installer again.
MSG
  exit 1
fi

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

# GitHub returns pretty-printed JSON, so fields of the same object land on different
# lines. Correlating them with grep and sed silently picks the wrong value; python is
# already a hard requirement, so use it.
json_field() {
  "$PYTHON" -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
print(data.get(sys.argv[1], "") or "")
' "$1" 2>/dev/null || true
}

asset_id() {
  "$PYTHON" -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)
for asset in data.get("assets", []):
    if asset.get("name") == sys.argv[1]:
        print(asset.get("id", ""))
        break
' "$1" 2>/dev/null || true
}

if [ -z "$VERSION" ]; then
  VERSION="$(api "https://api.github.com/repos/${REPO}/releases/latest" 2>/dev/null | json_field tag_name)"

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
  ASSET_ID="$(api "https://api.github.com/repos/${REPO}/releases/tags/${VERSION}" 2>/dev/null | asset_id "$TARBALL")"

  if [ -n "$ASSET_ID" ]; then
    curl -fsSL -H "Authorization: Bearer $TOKEN" -H "Accept: application/octet-stream" \
      "https://api.github.com/repos/${REPO}/releases/assets/${ASSET_ID}" -o "$TMP/$TARBALL" \
      && DOWNLOADED=1

    CHECKSUM_ID="$(api "https://api.github.com/repos/${REPO}/releases/tags/${VERSION}" 2>/dev/null | asset_id checksums.txt)"
    if [ -n "$CHECKSUM_ID" ]; then
      curl -fsSL -H "Authorization: Bearer $TOKEN" -H "Accept: application/octet-stream" \
        "https://api.github.com/repos/${REPO}/releases/assets/${CHECKSUM_ID}" \
        -o "$TMP/checksums.txt" 2>/dev/null || true
    fi
  fi
fi

if [ "$DOWNLOADED" = "0" ]; then
  curl -fsSL "https://github.com/${REPO}/releases/download/${VERSION}/${TARBALL}" \
    -o "$TMP/$TARBALL" || {
      echo "Could not download $TARBALL." >&2
      if [ -z "$TOKEN" ]; then
        echo "If the repository is private, run 'gh auth login' or set GITHUB_TOKEN." >&2
      else
        echo "The release exists but the asset could not be fetched. Check your token's scope." >&2
      fi
      exit 1
    }
  curl -fsSL "https://github.com/${REPO}/releases/download/${VERSION}/checksums.txt" \
    -o "$TMP/checksums.txt" 2>/dev/null || true
fi

if [ -f "$TMP/checksums.txt" ]; then
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

# Record where this install put itself, so `proving-ground update` can reinstall to the
# same place rather than guessing from the environment it happens to run in.
cat > "$TARGET/install-manifest" <<EOF
PG_DATA_DIR=$DATA_DIR
PG_BIN_DIR=$BIN_DIR
EOF

# `current` is what Unity project manifests point at, so an update swings one symlink
# instead of rewriting every project that has ever been set up.
ln -sfn "$TARGET" "$DATA_DIR/current"

mkdir -p "$BIN_DIR"
for name in proving-ground pg; do
  # PG_HOME is baked in rather than recomputed from XDG_DATA_HOME at run time, so the
  # command keeps working whatever environment it is later invoked from.
  cat > "$BIN_DIR/$name" <<EOF
#!/bin/sh
PG_HOME="\${PG_HOME:-$DATA_DIR/current}"
export PG_HOME
exec "\$PG_HOME/.venv/bin/proving-ground" "\$@"
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
