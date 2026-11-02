#!/usr/bin/env bash
#
# Publishes the playable WebGL build to GitHub Pages, so the assignment is a link and not a zip.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

BUILD_DIR="$PROJECT_ROOT/Builds/WebGL"
PUBLISH_DIR="$PROJECT_ROOT/docs"

# ---------------------------------------------------------------- build
if [ "${1:-}" != "--no-build" ]; then
    bash "$PROJECT_ROOT/Tools/build-webgl.sh"
else
    echo "== FACET: publishing the existing build (--no-build) =="
fi

if [ ! -f "$BUILD_DIR/index.html" ]; then
    echo "error: no player at $BUILD_DIR/index.html - run without --no-build." >&2
    exit 1
fi

snapshot_names="$(git -C "$PROJECT_ROOT" ls-tree --name-only HEAD:docs 2>/dev/null || true)"
preserved=""
for name in $snapshot_names; do
    if git -C "$PROJECT_ROOT" cat-file -e "HEAD:docs/$name/.snapshot" 2>/dev/null; then
        preserved="$preserved $name"
    fi
done
for name in $preserved; do
    if [ -e "$BUILD_DIR/$name" ]; then
        echo "error: snapshot '$name' collides with a directory the build itself emits - rename it." >&2
        exit 1
    fi
done

rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"
# git archive of a tree streams the tree's contents, not the directory itself, so each snapshot is
# extracted into a directory of its own name.
for name in $preserved; do
    mkdir -p "$PUBLISH_DIR/$name"
    git -C "$PROJECT_ROOT" archive "HEAD:docs/$name" | tar -x -C "$PUBLISH_DIR/$name"
done
cp -R "$BUILD_DIR/." "$PUBLISH_DIR/"

# Pages runs Jekyll over what it serves, and Jekyll drops files it reads as its own - anything under
# an underscore, and the .nojekyll marker itself is what turns that pass off. The build writes this
# file; re-asserted here because a publish without it ships a player missing exactly the files Jekyll
# decided were templating.
touch "$PUBLISH_DIR/.nojekyll"

if [ ! -f "$PUBLISH_DIR/index.html" ] || [ ! -f "$PUBLISH_DIR/Build/WebGL.loader.js" ]; then
    echo "error: the mirror at $PUBLISH_DIR is incomplete." >&2
    exit 1
fi

echo
echo "OK - docs/ holds the player ($(du -sh "$PUBLISH_DIR" | cut -f1))."
echo
echo "Next, once per repository:"
echo "  GitHub -> Settings -> Pages -> Source: Deploy from a branch -> main / docs -> Save."
echo "  The site lands at https://<owner>.github.io/<repository>/ - for this checkout,"
echo "  https://zwldarren.github.io/FACET/"
echo
echo "Then, every publish:"
echo "  git add docs && git commit -m 'build(pages): publish the WebGL player' && git push"
echo
echo "A private repository needs GitHub Pro for this - on Free, Pages is public-repository only."
