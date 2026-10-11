#!/usr/bin/env bash
#
# Publishes the playable WebGL build to GitHub Pages, so the assignment is a link and not a zip.
#
# Pages serves one folder out of the repository, and the folder it serves has to be committed:
# Builds/ is git-ignored (it is an artefact and it is 13 MB), so this script mirrors the build into
# docs/ and lets the repository carry that instead. docs/ is a generated mirror - never edit it,
# every run replaces it wholesale so a file the build stopped emitting cannot linger and be served.
#
# Usage:  bash Tools/publish-pages.sh [--no-build]
#
#   --no-build   publish the Builds/WebGL already on disk instead of rebuilding first
#
# The build itself is Tools/build-webgl.sh; this script only adds the mirror and the commit. Run
# Tools/build-webgl.sh directly when you want the player without publishing it.
#
# One setting the build fixes and this script depends on: Brotli with Unity's decompression fallback.
# Pages sends no Content-Encoding header and rejects a repository trying to set one, so a compressed
# payload has to be decompressed by the loader in JavaScript. Without the fallback the player
# downloads and then fails to start, which looks like a broken game rather than a misconfigured host.
#
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

# ---------------------------------------------------------------- mirror
# Replaced wholesale: a stale file is the one failure mode of a mirror that costs nothing to avoid,
# and `cp -R` alone would leave behind whatever a renamed build output used to be called.
rm -rf "$PUBLISH_DIR"
mkdir -p "$PUBLISH_DIR"
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
echo "  https://zwldarren.github.io/CS526-demo/"
echo
echo "Then, every publish:"
echo "  git add docs && git commit -m 'build(pages): publish the WebGL player' && git push"
echo
echo "A private repository needs GitHub Pro for this - on Free, Pages is public-repository only."
