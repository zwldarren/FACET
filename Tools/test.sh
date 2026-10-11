#!/usr/bin/env bash
#
# Runs the test suites headless, so a change can be verified without opening the Editor - and without
# the two dev machines running different commands.
#
#   EditMode   the simulation and the content table, no scene: the fast loop (seconds).
#   PlayMode   the shipped scene booting for real, through the driver and the views (seconds too, but
#              it has to build a player loop and a scene first).
#
# Runs on Windows (Git Bash) and macOS: Tools/locate-unity.sh resolves the editor and hands back the
# path form the native Unity binary takes on that machine.
#
# Usage:  bash Tools/test.sh [EditMode|PlayMode]     (default: both, in that order)
#
# Results land in Logs/editmode.xml and Logs/playmode.xml, logs beside them. Exits non-zero when a
# suite fails, when it wrote no results at all, or when the Editor already has the project open.
#
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

. "$PROJECT_ROOT/Tools/locate-unity.sh"

if [ ! -f "$UNITY_BIN" ]; then
    echo "error: no Unity editor binary at $UNITY_BIN." >&2
    exit 2
fi

case "${1:-all}" in
    EditMode) PLATFORMS="EditMode" ;;
    PlayMode) PLATFORMS="PlayMode" ;;
    all)      PLATFORMS="EditMode PlayMode" ;;
    *)        echo "usage: bash Tools/test.sh [EditMode|PlayMode]" >&2; exit 2 ;;
esac

fail=0
for platform in $PLATFORMS; do
    name="$(printf '%s' "$platform" | tr 'A-Z' 'a-z')"
    results="$PROJECT_ROOT/Logs/$name.xml"
    log="$PROJECT_ROOT/Logs/$name.log"
    mkdir -p "$PROJECT_ROOT/Logs"

    echo "== FACET: $platform =="
    # Drop last run's result file first: a batchmode that aborts before the suite ever runs -
    # compiler errors, a stray Editor lock - would otherwise leave yesterday's green XML where
    # the parse below will read it as today's pass. A result that never ran must read as missing.
    rm -f "$results"
    # Unity's exit code is not read here: it also reports 2 for a failed suite, and its only reliable
    # answer is the result file, which this script parses below.
    "$UNITY_BIN" -batchmode -nographics \
        -projectPath "$(to_native "$PROJECT_ROOT")" \
        -runTests -testPlatform "$platform" \
        -testResults "$(to_native "$results")" \
        -logFile "$(to_native "$log")" || true

    # A suite that never ran must never read as a suite that passed, so the missing file is its own
    # failure - with the one cause worth naming up front, because it is the one a dev hits by hand.
    if [ ! -f "$results" ]; then
        echo "FAILED: $platform wrote no results - see $log" >&2
        if grep -q "another Unity instance" "$log" 2>/dev/null; then
            echo "        The Editor has this project open: close it, or run the suite from the Editor." >&2
        fi
        fail=1
        continue
    fi

    summary="$(grep -o '<test-run[^>]*>' "$results" | head -1)"
    if printf '%s' "$summary" | grep -q 'result="Passed"'; then
        counts="$(printf '%s' "$summary" | sed 's/.*testcasecount="\([0-9]*\)".*passed="\([0-9]*\)".*/\1 of \2/')"
        echo "OK - $platform: $counts passed"
    else
        echo "FAILED: $platform - see $results and $log" >&2
        fail=1
    fi
done

exit "$fail"
