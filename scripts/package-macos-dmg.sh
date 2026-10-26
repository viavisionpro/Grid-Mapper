#!/usr/bin/env bash
set -euo pipefail
GRID_MAPPER_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GRID_MAPPER_VERSION="$(sed -nE 's/.*<Version>([0-9]+\.[0-9]+\.[0-9]+)<\/Version>.*/\1/p' "$GRID_MAPPER_ROOT/Directory.Build.props")"
test -n "$GRID_MAPPER_VERSION"
GRID_MAPPER_PACKAGES="$GRID_MAPPER_ROOT/artifacts/packages"
GRID_MAPPER_APP="$GRID_MAPPER_PACKAGES/$GRID_MAPPER_VERSION/Grid Mapper.app"
GRID_MAPPER_DMG="$GRID_MAPPER_PACKAGES/GridMapper-$GRID_MAPPER_VERSION-osx-arm64.dmg"
if [[ -e "$GRID_MAPPER_DMG" || -L "$GRID_MAPPER_DMG" || -e "$GRID_MAPPER_DMG.sha256" || -L "$GRID_MAPPER_DMG.sha256" ]]; then
    echo "Existing versioned DMG preserved; increment the version before rebuilding: $GRID_MAPPER_DMG" >&2
    exit 1
fi
python3 "$GRID_MAPPER_ROOT/scripts/package-app.py" --verify "$GRID_MAPPER_APP"
GRID_MAPPER_BUILD="$(mktemp -d "$GRID_MAPPER_PACKAGES/.dmg-stage.XXXXXX")"
GRID_MAPPER_STAGE="$GRID_MAPPER_BUILD/contents"
mkdir "$GRID_MAPPER_STAGE"
GRID_MAPPER_PARTIAL="$GRID_MAPPER_BUILD/GridMapper-$GRID_MAPPER_VERSION-partial.dmg"
trap 'rm -rf "$GRID_MAPPER_BUILD"' EXIT
ditto "$GRID_MAPPER_APP" "$GRID_MAPPER_STAGE/$(basename "$GRID_MAPPER_APP")"
cp "$GRID_MAPPER_ROOT/docs/user/README.md" "$GRID_MAPPER_STAGE/README.md"
cp "$GRID_MAPPER_ROOT/docs/user/README.ru.md" "$GRID_MAPPER_STAGE/README.ru.md"
ln -s /Applications "$GRID_MAPPER_STAGE/Applications"
hdiutil create -volname "Grid Mapper" -srcfolder "$GRID_MAPPER_STAGE" -format UDZO "$GRID_MAPPER_PARTIAL"
hdiutil verify "$GRID_MAPPER_PARTIAL"
ln "$GRID_MAPPER_PARTIAL" "$GRID_MAPPER_DMG"
(cd "$GRID_MAPPER_PACKAGES" && set -C && shasum -a 256 "$(basename "$GRID_MAPPER_DMG")" > "$(basename "$GRID_MAPPER_DMG").sha256")
printf '%s\n' "$GRID_MAPPER_DMG"
