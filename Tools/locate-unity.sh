#!/usr/bin/env bash
#
# The Tools' environment: where the Unity editor is, and how this shell hands a path to a native
# binary. Sourced, never run:
#
#     PROJECT_ROOT=... . "$PROJECT_ROOT/Tools/locate-unity.sh"
#
# Sets UNITY_DATA (the editor's Data folder, the one holding Managed/UnityEngine), UNITY_BIN (the
# editor binary beside it) and to_native() (a path in the form csc and Unity take on this machine),
# and leaves the newest() version sort for the scripts that need one.
#
# The editor is looked for in three places, in order: what the Editor itself recorded
# (Library/EditorInstance.json), UNITY_DATA_OVERRIDE, and the Unity Hub's install folders. The first
# is authoritative while the Editor is open and is deleted when it exits cleanly, the Hub folders are
# what is left when it is closed - and a secondary install path, which is where a Hub on a roomier
# drive puts its editors, is read from the Hub's own config. Exits 2 when none of them answers.

# macOS's BSD sort has no -V, so the newest of a list of dotted versions is picked by comparing each
# field's leading number on either machine.
newest() { sort -t. -k1,1n -k2,2n -k3,3n | tail -1; }

UNITY_DATA=""

if [ -f "$PROJECT_ROOT/Library/EditorInstance.json" ]; then
    # `|| true`: a json without the key is "not found", not a reason to abort under `set -e`.
    UNITY_DATA="$(grep -o '"app_contents_path"[^,]*' "$PROJECT_ROOT/Library/EditorInstance.json" \
        | sed 's/.*: *"//; s/"$//' || true)"
fi

if [ -z "$UNITY_DATA" ] && [ -n "${UNITY_DATA_OVERRIDE:-}" ]; then
    UNITY_DATA="$UNITY_DATA_OVERRIDE"
fi

if [ -z "$UNITY_DATA" ]; then
    # What is inside a Hub install: the Data folder on Windows, the bundle's Contents on macOS.
    # UNITY_HUB_DIR covers a Hub that was told to install somewhere else entirely.
    case "$(uname -s)" in
        Darwin)
            HUB_ROOT="${UNITY_HUB_DIR:-/Applications/Unity/Hub/Editor}"
            HUB_CONFIG="$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json"
            HUB_CONTENT="Unity.app/Contents"
            ;;
        *)
            HUB_ROOT="${UNITY_HUB_DIR:-${PROGRAMFILES:-C:/Program Files}/Unity/Hub/Editor}"
            HUB_CONFIG="${APPDATA:-$HOME/AppData/Roaming}/UnityHub/secondaryInstallPath.json"
            HUB_CONTENT="Editor/Data"
            ;;
    esac

    # The secondary path is a JSON string with escaped backslashes on Windows; unescaping it to
    # forward slashes leaves a macOS value (which has none) exactly as it was.
    HUB_SECONDARY=""
    if [ -f "$HUB_CONFIG" ]; then
        HUB_SECONDARY="$(tr -d '"' < "$HUB_CONFIG" | sed 's/\\\\/\//g' | tr -d '\r')"
    fi

    for hub_root in "$HUB_ROOT" "$HUB_SECONDARY"; do
        [ -n "$hub_root" ] && [ -d "$hub_root" ] || continue
        version="$(ls "$hub_root" 2>/dev/null | newest)"
        if [ -n "$version" ] && [ -d "$hub_root/$version/$HUB_CONTENT/Managed/UnityEngine" ]; then
            UNITY_DATA="$hub_root/$version/$HUB_CONTENT"
            break
        fi
    done
fi

if [ ! -d "$UNITY_DATA/Managed/UnityEngine" ]; then
    echo "error: cannot find the Unity editor install." >&2
    echo "       Open the project in the Editor once (it writes Library/EditorInstance.json)," >&2
    echo "       or run with UNITY_DATA_OVERRIDE set to its Data folder - on Windows" >&2
    echo "       'D:/Unity/Editor/Data', on macOS '/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents'." >&2
    exit 2
fi

# The editor binary sits beside the Data folder on Windows and inside the bundle on macOS. Both are
# still found from the one install, so a dev machine of either kind is one lookup.
case "$(uname -s)" in
    Darwin) UNITY_BIN="$UNITY_DATA/MacOS/Unity" ;;
    *)      UNITY_BIN="$UNITY_DATA/../Unity.exe" ;;
esac

# csc and Unity are native processes: Git Bash hands them a path they cannot read, so on Windows every
# path is converted with cygpath; on macOS a POSIX path is already the native form, and the conversion
# is the identity. One helper, so a script never has to know which machine it is on.
case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) to_native() { cygpath -m "$1"; } ;;
    *)                    to_native() { printf '%s' "$1"; } ;;
esac
