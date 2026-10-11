#!/usr/bin/env bash
#
# Builds the WebGL player with the Unity editor in batchmode, so the playable build the
# assignment ships is one command and not a list of editor steps.
#
# Runs on Windows (Git Bash) and macOS: Tools/locate-unity.sh resolves the editor and hands back the
# path form the native Unity binary takes on that machine.
#
# Usage:  bash Tools/build-webgl.sh
#
# The output lands in Builds/WebGL (git-ignored, it is an artefact). Publish that folder as it
# stands - see README.md for the GitHub Pages steps.
#
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# ---------------------------------------------------------------- locate the Unity editor
. "$PROJECT_ROOT/Tools/locate-unity.sh"

if [ ! -f "$UNITY_BIN" ]; then
    echo "error: no Unity editor binary at $UNITY_BIN (the WebGL module has to be installed too)." >&2
    exit 2
fi

echo "== FACET: WebGL build =="
"$UNITY_BIN" -batchmode -nographics -quit \
    -projectPath "$(to_native "$PROJECT_ROOT")" \
    -buildTarget WebGL \
    -executeMethod Facet.EditorTools.BuildWebGL.Build \
    -logFile "$(to_native "$PROJECT_ROOT")/Logs/build-webgl.log"

# The build tool exits non-zero on failure, but a missing player is the failure worth naming:
# Unity can report success and still leave nothing behind if the build target is not installed.
if [ ! -f "$PROJECT_ROOT/Builds/WebGL/index.html" ]; then
    echo "error: no player at Builds/WebGL/index.html - see Logs/build-webgl.log" >&2
    exit 1
fi

echo "OK - Builds/WebGL/index.html is built; see Logs/build-webgl.log for the summary."
