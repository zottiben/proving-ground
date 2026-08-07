#!/usr/bin/env bash
# Compiles the package inside the test project and reports only what matters.
# Usage: tools/compile.sh [--tests]
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.3.16f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="$ROOT/testproject"
LOG="$PROJECT/compile.log"

if [[ ! -x "$UNITY" ]]; then
  echo "Unity not found at $UNITY. Set UNITY_PATH." >&2
  exit 2
fi

rm -f "$LOG"

if [[ "${1:-}" == "--tests" ]]; then
  "$UNITY" -batchmode -nographics -projectPath "$PROJECT" -logFile "$LOG" \
    -runTests -testPlatform EditMode \
    -testResults "$PROJECT/test-results.xml" >/dev/null 2>&1
  STATUS=$?
else
  "$UNITY" -batchmode -quit -nographics -projectPath "$PROJECT" -logFile "$LOG" >/dev/null 2>&1
  STATUS=$?
fi

ERRORS=$(grep -E "error CS[0-9]+" "$LOG" | sort -u)

if [[ -n "$ERRORS" ]]; then
  echo "COMPILE ERRORS:"
  echo "$ERRORS"
  exit 1
fi

if grep -q "Compilation failed" "$LOG"; then
  echo "COMPILATION FAILED (no CS errors captured; see $LOG)"
  grep -E "Compilation failed|Error building" "$LOG" | head -20
  exit 1
fi

echo "compiled clean (unity exit $STATUS)"
ls "$PROJECT/Library/ScriptAssemblies/" 2>/dev/null | grep -i proving || echo "WARNING: no ProvingGround assemblies produced"
exit 0
