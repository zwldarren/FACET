#!/usr/bin/env bash
#
# Freezes the player currently mirrored in docs/ as a version snapshot under docs/<name>/, so the
# published site keeps that build playable at its own URL while docs/ root goes on holding only the
# latest one - publish-pages.sh replaces the root wholesale every run and re-materializes snapshots
# around it, so a snapshot is only safe once it is committed, which is why this script commits.
#
# Usage:  bash Tools/snapshot-pages.sh <name> [-m "<message>"]
#
#   name       the URL segment to serve the snapshot under, e.g. 'alpha' - the player then lives at
#              https://<owner>.github.io/<repository>/<name>/
#   -m msg     the commit message; defaults to "build(pages): freeze the <name> snapshot"
#
# The snapshot is taken from what docs/ holds right now, not from Builds/ - what is published is
# what gets frozen, and a stale mirror cannot sneak in under a name that promises otherwise. Run
# publish-pages.sh first when the build to freeze is newer than the last publish.
#
# Snapshots pile up as full copies (about 13 MB each here), so retire the ones nobody links to
# any more: delete docs/<name>/ and commit.
#
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="$PROJECT_ROOT/docs"

NAME=""
MESSAGE=""
while [ $# -gt 0 ]; do
    case "$1" in
        -m) MESSAGE="$2"; shift 2 ;;
        -*) echo "error: unknown option '$1'." >&2; exit 1 ;;
        *)
            if [ -n "$NAME" ]; then echo "error: one name only (got '$NAME' and '$1')." >&2; exit 1; fi
            NAME="$1"; shift ;;
    esac
done

if [ -z "$NAME" ]; then
    echo "usage: bash Tools/snapshot-pages.sh <name> [-m \"<message>\"]" >&2
    exit 1
fi
if ! printf '%s' "$NAME" | grep -Eq '^[A-Za-z0-9][A-Za-z0-9_-]*$'; then
    echo "error: '$NAME' is not a URL-safe folder name (letters, digits, dash, underscore)." >&2
    exit 1
fi
for reserved in Build TemplateData; do
    if [ "$NAME" = "$reserved" ]; then
        echo "error: '$NAME' is a name the build itself emits - pick another." >&2
        exit 1
    fi
done

if [ ! -f "$PUBLISH_DIR/index.html" ] || [ ! -f "$PUBLISH_DIR/Build/WebGL.loader.js" ]; then
    echo "error: nothing to snapshot at $PUBLISH_DIR - run Tools/publish-pages.sh first." >&2
    exit 1
fi

SNAP_DIR="$PUBLISH_DIR/$NAME"

# Replaced wholesale like the root is, so a re-snapshot of an old name leaves no stale file behind.
rm -rf "$SNAP_DIR"
mkdir -p "$SNAP_DIR"
# The tree docs/ holds right now, one level deep: every entry copied into the snapshot, except
# .nojekyll - the root's own business, it only means anything at the top of the served tree - and
# the snapshot directory itself, which the glob would otherwise try to copy into itself.
find "$PUBLISH_DIR" -mindepth 1 -maxdepth 1 ! -name '.nojekyll' ! -path "$SNAP_DIR" \
    -exec cp -R {} "$SNAP_DIR/" \;
# The marker publish-pages.sh looks for: a snapshot is a directory it clears and restores from the
# last commit, never one it lets the build overwrite.
touch "$SNAP_DIR/.snapshot"

git -C "$PROJECT_ROOT" add "docs/$NAME"
git -C "$PROJECT_ROOT" commit -m "${MESSAGE:-build(pages): freeze the $NAME snapshot}"

echo
echo "OK - docs/$NAME/ holds the frozen player ($(du -sh "$SNAP_DIR" | cut -f1)), committed."
echo "  It is served at https://<owner>.github.io/<repository>/$NAME/ once pushed:"
echo "  https://zwldarren.github.io/FACET/$NAME/"
echo
echo "Next:"
echo "  git push"
