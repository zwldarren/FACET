#!/usr/bin/env bash
#
# Compiles the FACET assemblies outside the Unity Editor, so a change can be
# checked without switching windows. Three things it proves:
#
#   FACET.Core  - compiles against netstandard2.1 / C# 9 with NO engine reference,
#                 which is the contract the Core asmdef enforces.
#   FACET.Game  - compiles against the real UnityEngine assemblies and the real
#                 package assemblies (Unity.InputSystem, ...) from Library/ScriptAssemblies.
#   FACET.Editor - compiles against the real UnityEditor assemblies, because the
#                 Editor tooling (the build tool, the asset creators, the map
#                 painting window) is code too and a typo in it is a broken menu.
#   the two test assemblies - FACET.Core.Tests and FACET.PlayTests, the same way:
#                 a suite that does not compile is a suite that does not run, and
#                 a test's use of an API a change just moved is a red test.
#
# It is a fast syntax + API-surface check, not a substitute for opening the Editor:
# it cannot catch scene wiring, serialization or rendering mistakes.
#
# Runs on Windows (Git Bash) and macOS: Tools/locate-unity.sh resolves the editor and hands back the
# path form the native compiler takes on that machine.
#
# Usage:  bash Tools/check.sh
#
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Unity reaps Temp/ while the Editor is open, which can take the response files out from under a check
# that is already running - so the scratch directory can be pointed elsewhere, e.g. at a path outside
# the project. Nothing about the check depends on where it is.
OUT_DIR="${FACET_CHECK_OUT:-$PROJECT_ROOT/Temp/CompileCheck}"

# ---------------------------------------------------------------- locate toolchain
if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: 'dotnet' is not on PATH (install the .NET SDK, it ships the Roslyn compiler)." >&2
    exit 2
fi

# `command -v dotnet` is a symlink on macOS (usually into /usr/local/share/dotnet), so the SDK lives
# beside the resolved binary, not beside the link.
DOTNET_BIN="$(command -v dotnet)"
DOTNET_BIN="$(readlink -f "$DOTNET_BIN" 2>/dev/null || printf '%s' "$DOTNET_BIN")"
DOTNET_HOME="$(dirname "$DOTNET_BIN")"
# ---------------------------------------------------------------- locate the Unity editor
# Also leaves newest(), the version sort the SDK lookup below uses.
. "$PROJECT_ROOT/Tools/locate-unity.sh"

SDK_VERSION="$(ls "$DOTNET_HOME/sdk" | newest)"
CSC="$DOTNET_HOME/sdk/$SDK_VERSION/Roslyn/bincore/csc.dll"

if [ ! -f "$CSC" ]; then
    echo "error: no Roslyn compiler at $CSC" >&2
    exit 2
fi

NETSTANDARD="$UNITY_DATA/NetStandard/ref/2.1.0/netstandard.dll"
if [ ! -f "$NETSTANDARD" ]; then
    echo "error: no netstandard2.1 reference assemblies at $NETSTANDARD" >&2
    exit 2
fi

mkdir -p "$OUT_DIR"

# ---------------------------------------------------------------- the response-file recipe
# All three assemblies are compiled the same way: a fixed four-flag header plus the netstandard
# reference, the caller's extra references (the engine assemblies, then the assemblies built
# before this one), the package assemblies filtered by the caller's exclude patterns, and the
# caller's sources. Only those arguments differ per assembly, so the recipe lives here once and
# each build below states what is its own.
build_rsp() {
    local name="$1" out="$2" src_dir="$3" exclude="$4"
    shift 4
    {
        echo "-target:library"
        echo "-langversion:9.0"
        echo "-nologo"
        echo "-nostdlib+"
        # Some assemblies only exist with a define set - the test assemblies carry
        # UNITY_INCLUDE_TESTS on their asmdef, so their sources are behind it.
        [ -z "$RSP_DEFINES" ] || echo "-define:$RSP_DEFINES"
        echo "-out:\"$(to_native "$OUT_DIR/$out")\""
        echo "-reference:\"$(to_native "$NETSTANDARD")\""
        local ref
        for ref in "$@"; do
            echo "-reference:\"$(to_native "$ref")\""
        done
        if [ -n "$exclude" ]; then
            local d base skip pat
            # Split on spaces only: read does not pathname-expand, so a pattern like
            # FACET.* stays a pattern instead of globbing against the caller's directory.
            local -a pats=()
            read -r -a pats <<< "$exclude" || true
            for d in "$PROJECT_ROOT/Library/ScriptAssemblies"/*.dll; do
                [ -e "$d" ] || continue
                base="$(basename "$d")"
                skip=
                for pat in "${pats[@]}"; do
                    case "$base" in
                        $pat) skip=1; break ;;
                    esac
                done
                [ -n "$skip" ] || echo "-reference:\"$(to_native "$d")\""
            done
        fi
        local f
        for f in "$src_dir"/*.cs; do echo "\"$(to_native "$f")\""; done
    } > "$OUT_DIR/$name.rsp"
}

# Invoke the compiler on the response file that build_rsp just wrote; a failure names the
# assembly and aborts the whole check.
run_csc() {
    local name="$1" label="$2"
    if ! dotnet "$(to_native "$CSC")" "@$(to_native "$OUT_DIR/$name.rsp")"; then
        echo "FAILED: $label" >&2
        exit 1
    fi
}

# ---------------------------------------------------------------- FACET.Core
RSP_DEFINES=
build_rsp core FACET.Core.dll "$PROJECT_ROOT/Assets/Scripts/Core" ""

echo "== FACET.Core (netstandard2.1, no engine) =="
run_csc core FACET.Core

# ---------------------------------------------------------------- FACET.Game
# Package assemblies, but not our own stale output and not Editor-only ones.
build_rsp game FACET.Game.dll "$PROJECT_ROOT/Assets/Scripts/Unity" "FACET.* *.Editor.dll" \
    "$UNITY_DATA/Managed/UnityEngine"/*.dll "$OUT_DIR/FACET.Core.dll"

echo "== FACET.Game (UnityEngine + package assemblies) =="
run_csc game FACET.Game

# ---------------------------------------------------------------- FACET.Editor
# UnityEditor.dll lives beside the UnityEngine assemblies on Windows and macOS alike, so the editor
# step needs no lookup of its own. The Editor assembly references both assemblies above, which is why
# this runs last.
# Package assemblies too, but not our own stale output.
build_rsp editor FACET.Editor.dll "$PROJECT_ROOT/Assets/Scripts/Editor" "FACET.*" \
    "$UNITY_DATA/Managed/UnityEngine"/*.dll "$OUT_DIR/FACET.Core.dll" "$OUT_DIR/FACET.Game.dll"

echo "== FACET.Editor (UnityEngine + UnityEditor) =="
run_csc editor FACET.Editor

# ---------------------------------------------------------------- the test assemblies
# The suite is code too, and a suite that does not compile is a suite that does not run: the two test
# assemblies are compiled here for the same reason the game is. They reference the three above by
# file, so a change that breaks a test's API use is caught in the same pass as the change itself.
# Both carry UNITY_INCLUDE_TESTS on their asmdef, which is why their sources are behind it.
#
# NUnit is a precompiled reference rather than a package assembly, so it is not in ScriptAssemblies
# at all: their asmdefs name it and Unity resolves it out of the package cache. NUnit's own build is a
# .NET Framework one, so the netfx shims come along with it - the same reference set Unity's own
# generated project file for this assembly carries.
NUNIT="$(ls -d "$PROJECT_ROOT"/Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll 2>/dev/null | head -n 1)"
if [ -z "$NUNIT" ]; then
    echo "error: no nunit.framework.dll under Library/PackageCache - open the project once, or run the suite in the Editor." >&2
    exit 2
fi

NETFX_SHIMS="$UNITY_DATA/NetStandard/compat/2.1.0/shims/netfx"
if [ ! -d "$NETFX_SHIMS" ]; then
    echo "error: no .NET Framework shims at $NETFX_SHIMS, which NUnit needs to compile against." >&2
    exit 2
fi

# The managed references these two need, with one omission: the monolithic UnityEditor.dll defines the
# same types as UnityEditor.CoreModule.dll, and a compile that references both reports every one of
# them as ambiguous. The Editor assembly above gets away with both because it touches none of them.
MANAGED_REFS=()
for d in "$UNITY_DATA/Managed/UnityEngine"/*.dll; do
    [ "$(basename "$d")" = "UnityEditor.dll" ] || MANAGED_REFS+=("$d")
done

RSP_DEFINES="UNITY_INCLUDE_TESTS"
build_rsp edittests FACET.Core.Tests.dll "$PROJECT_ROOT/Assets/Tests/EditMode" "FACET.*" \
    "${MANAGED_REFS[@]}" "$NUNIT" "$NETFX_SHIMS"/*.dll \
    "$OUT_DIR/FACET.Core.dll" "$OUT_DIR/FACET.Game.dll" "$OUT_DIR/FACET.Editor.dll"

echo "== FACET.Core.Tests (EditMode) =="
run_csc edittests FACET.Core.Tests

build_rsp playtests FACET.PlayTests.dll "$PROJECT_ROOT/Assets/Tests/PlayMode" "FACET.*" \
    "${MANAGED_REFS[@]}" "$NUNIT" "$NETFX_SHIMS"/*.dll \
    "$OUT_DIR/FACET.Core.dll" "$OUT_DIR/FACET.Game.dll"

echo "== FACET.PlayTests (PlayMode) =="
run_csc playtests FACET.PlayTests
RSP_DEFINES=

echo
echo "OK - FACET.Core, FACET.Game, FACET.Editor and both test assemblies compiled clean."
