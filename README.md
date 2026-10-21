# FACET

A 2D top-down factory-defence prototype: an enemy's weakness is a shape, and what your factory
builds is what your guns fire. Unity 6 (`6000.3.23f1`), 2D, built for WebGL.

**Playable build:** <https://zwldarren.github.io/FACET/>

The names this codebase uses for its own concepts are in [GLOSSARY.md](GLOSSARY.md).

## Getting started

Open the project in Unity `6000.3.23f1` and press Play with `Assets/Scenes/Main.unity` open. The
scene opens on the title screen - Play or Enter starts the campaign at its frontier map, and the run
behind the title is held until then, so no time passes or keys reach it. In a run, **Esc or M** (or the
**MENU** button in the HUD's top-right cluster) opens the pause menu: resume, restart this run, the map
list, how to play, and main menu. The two doors that throw the run away - restart and main menu - each
take a second press on that button: the first arms it and says so, because the run is not saved;
cleared maps are kept either way.

The scene holds a `Main Camera` and one object with `SimulationDriver`; the driver builds every view at
runtime (terrain, machines, belts, items, enemies, shots, cursor, HUD), so there are no prefabs to wire.
`SimulationDriver`'s **Start Map Index** boots straight into any map for testing and skips the title.

## Development

```bash
bash Tools/test.sh                 # EditMode + PlayMode, headless (Tools/test.sh EditMode is the fast loop)
bash Tools/check.sh                # compile Core, Game and Editor without the Editor at all
python Tools/check-maps.py         # the map assets' own format, without opening the Editor
bash Tools/publish-pages.sh        # build the WebGL player and mirror it into docs/, for GitHub Pages
```

Results land in `Logs/editmode.xml` and `Logs/playmode.xml`; each run deletes its own file first, so
a suite that never ran reports "no results" instead of reading as a pass. The EditMode suite draws
its own maps and reads no shipped map data, so repainting a map cannot turn it red — the files in
`Assets/Data/Maps` are held to their format by `Tools/check-maps.py` instead.

### Where things live

```
Assets/Scripts/Core        the engine-free simulation (no UnityEngine reference), deterministic at 30 Hz
Assets/Scripts/Unity       the view layer: one renderer per concern, one mesh each, Palette for colour
Assets/Scripts/Editor      the WebGL build tool, the content-asset tools, the map painting window
                           (FACET ▸ Map Editor) and the Scene view's own map editor
Assets/Tests/EditMode      the simulation, the generator, the content table, the maps
Assets/Tests/PlayMode      the shipped scene booting for real, through the driver and the views
Assets/Data                the assets the scene is wired to: Palette, ContentDatabase, Campaign, Maps
Tools                      the test, check, build and publish scripts
```

`Assets/Scripts/Core` is plain C# with `noEngineReferences`, so a run is reproducible from its own
state and testable without a scene; the Editor-facing assets are carriers that convert to those
plain definitions (`ContentDatabaseAsset.ToCore()`, `MapAsset.ToCore()`). Costs, stats, the look and
the maps are data rather than code — edited as assets in the Editor, with no recompile.

### Publishing the playable build

The player ships in FACET's own page, `Assets/WebGLTemplates/FACET` (a fixed 960×600 frame, the game's
loading and failure screens, a letterboxing fullscreen button, no Unity branding). The build tool selects it,
so edit the template there - never the generated `docs/index.html`.

`Tools/publish-pages.sh` builds the player, mirrors `Builds/WebGL` into `docs/` and prints the
publish steps (`--no-build` mirrors what is already on disk). `docs/` is generated, not source:
every run replaces it wholesale, so nothing in it should be edited by hand. Pages requires
**Settings ▸ Pages ▸ Source: Deploy from a branch ▸ `main` / `/docs`**.
