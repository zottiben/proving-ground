#!/usr/bin/env bash
# Runs the package test suites in the test project and prints a summary.
# Usage: tools/test.sh [editmode|playmode|python|all]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="$ROOT/testproject"
MODE="${1:-all}"
STATUS=0

run_python() {
  local python="$ROOT/mcp/.venv/bin/python"
  if [[ ! -x "$python" ]]; then
    echo "python: no venv at $python. Run install.sh, or: uv venv mcp/.venv"
    return 1
  fi
  if ! "$python" -c "import pytest" 2>/dev/null; then
    echo "python: pytest missing. Run: uv pip install --python $python -e '$ROOT/mcp[dev]'"
    return 1
  fi
  (cd "$ROOT/mcp" && "$python" -m pytest tests -q)
}

if [[ "$MODE" == "python" ]]; then
  run_python || STATUS=1
  exit $STATUS
fi

if [[ ! -x "$UNITY" ]]; then
  echo "Unity not found at $UNITY. Set UNITY_PATH." >&2
  exit 2
fi

run_platform() {
  local platform="$1"
  local log="$PROJECT/$platform.log"
  local results="$PROJECT/$platform-results.xml"

  rm -f "$log" "$results"
  "$UNITY" -batchmode -nographics -projectPath "$PROJECT" -logFile "$log" \
    -runTests -testPlatform "$platform" -testResults "$results" >/dev/null 2>&1

  local errors
  errors=$(grep -E "error CS[0-9]+" "$log" | sort -u)
  if [[ -n "$errors" ]]; then
    echo "$platform: COMPILE ERRORS"
    echo "$errors"
    return 1
  fi

  python3 - "$results" "$platform" <<'PY'
import sys, os, xml.etree.ElementTree as ET
path, platform = sys.argv[1], sys.argv[2]
if not os.path.exists(path):
    print(f"{platform}: no results file produced"); sys.exit(1)
root = ET.parse(path).getroot()
failed = int(root.get('failed') or 0)
print(f"{platform}: {root.get('passed')}/{root.get('total')} passed, {failed} failed")
for tc in root.iter('test-case'):
    if tc.get('result') != 'Passed':
        print(f"  FAIL {tc.get('name')}")
        m = tc.find('.//message')
        if m is not None and m.text:
            print("    " + m.text.strip()[:400].replace("\n", "\n    "))
sys.exit(1 if failed else 0)
PY
}

[[ "$MODE" == "all" || "$MODE" == "editmode" ]] && { run_platform EditMode || STATUS=1; }
[[ "$MODE" == "all" || "$MODE" == "playmode" ]] && { run_platform PlayMode || STATUS=1; }
[[ "$MODE" == "all" ]] && { run_python || STATUS=1; }

exit $STATUS
