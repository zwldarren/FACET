# FACET — Prototype

A 2D top-down tower-defense prototype where **circles are money and half-circles are ammunition** —
and both are physical items riding conveyor belts. There is no abstract currency: a circle mined at
the edge of the map is not spendable until a belt delivers it into the Core, and a cannon fires only
the half-circles that physically reach it. A wrong-shape delivery jams the belt segment it lands on
and silences everything downstream, so the answer to pressure is re-plumbing the line.

Unity 6 (`6000.3.23f1`), 2D, built for WebGL. Current scope: two shipped maps (Quarry · Foundry) ·
two chains — ○ → ◠ fired by the Cannon, □ → ▭ fired by the Mortar — through the decomposer
and the cutter · a splitter and a sorter · a pipe that jumps one belt across another · walls ·
two enemy kinds (Spike, Bulwark). A map can bring its own ground — rock, water and the void that
shape it — and the painted Canyon map in `Assets/Data/Maps` shows that layer; the campaign itself
plays the two plain-ground maps.
The campaign's **Map Set** (`Assets/Data/Campaign.asset`, a list of `MapAsset`s) is the expansion seam: a
second map — more waves, more minerals, a ground of its own — is a `MapAsset` painted with
`FACET ▸ Map Editor` and added to that list, not new systems. So is
`Assets/Data/ContentDatabase.asset`: a new machine, enemy, shape or recipe is a row plus the enum value
that names it, not a `switch` that has to learn about it.

## Controls

| Input | Action |
|---|---|
| `WASD` / arrows | pan the camera |
| mouse wheel | zoom |
| **click the build bar** | select a building — the bottom bar is a row of buttons, each showing its hotkey, name, role and cost |
| `1`–`9`, `0` | select the same buildings by hotkey — the tenth kind, the wall, is `0`; **press the same digit again to drop the selection**, exactly as clicking the same tile twice does |
| `Q` / `E`, or the info card's **turn** buttons | turn the placement ghost (a splitter, every converter and the wall have no facing to turn) |
| `Esc`, or the info card's **inspect** button | drop the selection: the cursor then only **reads** — the state a run opens in |
| left mouse | place the selected building — a click lays one belt facing the ghost, or lands the machine where it was aimed; **drag** to lay a belt run or to aim a machine. With nothing selected the same click pins whatever is under it and the info card describes *it*. A placement the run cannot pay for, or cannot make there, says so in a toast |
| right mouse | delete what is under the cursor for a full refund; on a jammed segment it **clears the jam** instead |
| `Space` / `P`, or the **pause** button | pause / resume |
| `N`, or the status card's **start wave** button | start the next wave now — the first wave only ever arrives this way. Once a map is cleared it carries on to the next map instead |
| `R`, or the **restart run** button | restart the run |

The HUD is four fixed regions, all clickable: a status card top-left (map and wave, the Core's
health, turret and jam diagnostics, the last second's events), a controls card top-right, the build
bar along the bottom, and an info card above it that describes whichever building is hovered or
selected - its cost, what it does, its numbers, and the facing its ghost will be built with. Beside
the status card floats the **stockpile**: the shape the run banks, in that shape's own colour, and how
many of it there are, in the largest type on screen and with no panel of its own - the one number the
whole economy is played against, kept where the eye already is. The bar's tiles are tinted when the
stockpile cannot cover them, their cost turning amber and bold, so the palette doubles as an
affordability readout and does not require the number keys; the stockpile's own number turns amber
with them. The status card's edge stripe turns from the accent colour to the warning colour whenever
something in it needs the player: a jam, a starved turret, a leaking Core.

A refused placement turns up as a **toast**. The tick reports the reason on the event stream
(`PlacementRejected`: no funds, the tile is occupied, the ground is wrong, the tile is a wave's
doorway, off the map), and a dark panel sized to that sentence floats under the top cards for three
seconds of simulation time, fading
out as it goes, with the stockpile flashing warning-coloured beside it. "Not enough circles" is only
useful next to how many circles there are, and a click that quietly did nothing is the one thing that
reads as a broken game.

Nothing has to be selected, though, and the run opens that way: with no building picked the cursor
does not preview a placement, it reads. A left click pins whatever is under it and the info card
describes *that* instead of a ghost - its name and coordinates, the sides its belts are actually
wired to, what it is doing this second (splitting, carrying, holding, starved, jammed, nothing to
push onto), its numbers, its health where it has any, and the facing that means something to it - while
the pinned tile stays
outlined on the map beside a turret's range ring, so the card and the map agree about which building
"it" is. Reading is not only for what the player built: a vein in the ground reports the mineral it
yields and how big it is, and a **wave's doorway** reports which waves walk in at it - the two things
a layout is chosen around, and neither of them a building. Hovering the bar always describes the
catalogue entry instead, because that is the question a pointer over the palette is asking. `Esc`,
the **inspect** button in the card, or pressing the selected digit again all drop the selection and
go back to reading.

The panels are also the camera's pan bound: the map has to cover the screen minus the strips they
occupy, and may then be pushed a further 5% of the screen past its own edge, so an edge tile comes to
rest in the open with background behind it instead of against the card that was hiding it. The last
5% is what makes that visible at the bottom, where the build bar is as wide as the screen and as tall
as its whole strip and would otherwise leave the map's last row pressed flat against it. `HudLayout`
holds the geometry for both the HUD and `PanLimits` - the stockpile readout counts as part of the top
strip, beside the card that strip is measured from - and `CameraRig`'s **Edge Slack Screens** is that
5%: one table, drawn on and obeyed by two views, and pinned by a test that no piece of the HUD overlaps
another.

## The rules the prototype is built on

* **Everything costs circles, and circles are only spendable once belted into the Core.** A run opens
  with 80 circles already banked — the bootstrap budget, the one delivery the player does not make
  themselves, and the only one that is not belted home. A drill
  mines one circle per second onto the belt it faces; the line home is a purchase (1 ○ per belt
  cell), so the map's distances are the economy. Removal refunds in full.
* **The decomposer splits ○ → ◠◠ in 1.5 s.** It eats only circles — feeding a half-circle back into
  it jams the feeding belt — and pushes its halves out through the belts pointing away from it, round
  robin, exactly as a splitter's ports work. One outlet caps it at that belt's 1/s; a second outlet
  lets it run at its own 1.33 halves/s, so two cannons at full rate still need two decomposers (or
  one, split and shared, at half rate each).
* **The second map opens a second mineral, and the second chain is the first one's shape.** Square
  patches yield □; a **cutter** runs □ → ▭▭ on its own recipe, and a **mortar** eats only ▭. The cutter
  is the decomposer's behaviour on another recipe and the mortar is the cannon's behaviour on another
  diet, so what the second chain buys is a choice rather than a bigger number: the mortar's cycle is
  half again as long - two-thirds the shot rate - for twice the damage, which is one item per kill
  against the cannon's two, so a thinner line can feed it and it pays for that with a slower reaction
  and a longer reach. Two guns also means two
  diets, so a line of the wrong ammunition jams at the wrong gun exactly as it does at the right one.
* **The cannon eats only ◠ and the mortar only ▭**, each at most one shot per interval. Their real fire
  rates are `min(1/interval, what their lines deliver)` — damage is a throughput property of the
  layout, not a purchase.
* **A wrong-shape delivery jams that segment** and it stays jammed, with the offending item on it.
  Right-clicking the jammed segment clears the jam and destroys that item, keeping the belt — so the
  line keeps its shape and is one click from running again. Turning the belt with `Q`/`E` does not
  clear a jam, and a jammed belt cannot be deleted until it has been cleared.
* **The pipe jumps one tile** — it swallows whatever is delivered and drops it on the belt two cells
  ahead, which is how two belts cross. It is transport, not a machine with a diet: it never jams.
* **A machine's ports are read off the belts around it**: a belt pointing in is an input, a belt
  pointing away is an output. That one rule gives the splitter 1-in-3-out, 3-in-1-out and 2-in-2-out
  as the same building on different streets, and gives the decomposer a second outlet the moment a
  player lays a second belt away from it. Splitters deal items round-robin, skip blocked outputs, and
  backpressure when full.
* **The sorter routes by shape** — the same hub as a splitter, with one setting: the shape its
  definition filters for leaves by the side it faces, and every other shape leaves by the other
  outlets. It ships filtering for the circle — the money shape — so one sorter facing the Core banks
  the money while the half-circles carry on down the line, which turns "keep money and ammo apart"
  from two separate lines into one split. Routing is strict: if the side a shape needs has no room, the
  sorter holds the item and the line backs up behind it. It never pushes a shape somewhere that will
  never want it, so a mis-wired outlet is a queue you can see rather than a jam four cells later.
* **The first wave waits for the player.** A run opens with as much time to build as they want: the
  first wave has no countdown and never arrives on its own — `N`, or the HUD's button, calls it, and
  only the defence that is actually finished is tested. Waves after it count down on the clock and
  walk in on their own, so the schedule is pressure the player meets later, not noise at the start.
* **Machines are solid and destructible; belts are walkable and not.** Every machine has health
  (`MaxHp` in the content table), and enemies treat it as an obstacle: they follow a flow field to the
  Core *around* machines, stop and attack any attackable machine within reach, and **charge one they
  notice** a little further out — a Spike hits what it walks into (1.6 tiles) and heads for what it
  sees from 2.5, which is why a gun belongs three tiles off the lane and why a bare wall beside the
  road is bait rather than scenery. An enemy only resumes the walk once the machine is gone. When the
  Core is sealed in they walk straight at it and chew through whatever stands in the way. The
  Core's own rules are unchanged — reach its ring and it takes the hits. A **belt** is a flat conveyor,
  so it stays walkable and unattackable: the 1-cost cell must not become the cheapest wall. Destruction
  pays no refund — a chewed building is lost work, not a cash-out — and a definition left at `MaxHp` 0
  is indestructible and untargetable, which is how belts and any custom row that predates HP opt out.
* **The wall is the maze tool** — 2 ○, 120 hp, no behaviour, hotkey `0`. It is what makes the flow
  field a decision rather than a detour: it decides which way a wave walks, and it is what a sealed-in
  enemy eats. A machine chews red as it takes hits, so how close a wall is to falling is visible; a
  belt pointed at a wall simply stalls at full progress, like any building that never consumes.
  **It may not stand on a wave's doorway**: the map's door has to stay walkable, and a wall there is
  refused as a doorway — plug it for two circles and the wave would spawn inside the wall. Only a
  *machine* would seal a door: a belt is a flat conveyor and may cross one, exactly as it may cross a
  vein.
* **Walkers keep a body's width between them.** The flow field sends a whole wave down one line, so
  each tick any pair closer than a body's width (0.84 tiles — the drawn diameter) pushes apart by half
  the overlap each, never moving a walker faster than it moves itself and never into a solid cell. A
  wave may still *arrive* as a knot — a door is a single tile — but it never stays one: the rule is
  what pulls the fresh arrival out of the queue over the next few ticks.
* **Every drill needs a shape patch in the ground**, and every shape patch only accepts a drill — so a
  patch is what a chain is built around. **A belt, though, can be laid across ore**: transport crosses
  a vein, only machines are kept off it. That is what makes a wide vein workable at all, because a
  drill pushes onto the tile *beside* it and inside a 3x3 patch every neighbour is ore — so the middle
  cell costs you the belt lane crossing it (and the drill that would have stood on that lane's cell),
  rather than being dead ground. The ore is not consumed by the belt: it is recorded under it and comes
  back when the belt is removed, and the ore bed stays visible on both sides of the lane.
* **The ground is the map's, and it is not the player's.** Rock, water and the void outside the map are
  the map's own obstacles: no belt crosses them, no machine stands on them, and the walkers' flow field
  reads them — so a ridge is a route the *author* draws rather than a purchase the *player* makes. A wall
  the player buys is a building with health an enemy can chew through; a rock ridge cannot fall, and that
  difference is what the two are for. Terrain is stamped once from the map's data and never moves — a
  restart re-stamps the identical layout — so it is the one thing a player action can never touch. What
  the ground is and what is standing on it are two separate layers (`TerrainField` and `TileGrid`), which
  is why a third kind of obstacle is a row in an enum rather than a case in every rule that reads a tile.
  A map that states no terrain is plain buildable ground everywhere, which is what every map used to be.
* **Clearing a map loads the next one without restarting.** The first map mines one mineral and holds
  one approach; the second is smaller, mines both minerals, and sends three times as many enemies in
  three waves, opening a new door on each one; the third is not a rectangle at all — void corners, two
  rock ridges that cut the flanking lanes in half, and a pond in the lane below the Core — so its waves
  have to be met at gates the map chose rather than head-on. `N` on a cleared map builds the next map in
  place — same views, same session — and what carries over between maps is only where the player is.
  Authored maps are appended to that progression, so a painted map is a map of the campaign like any
  other.

## Running it

Open the project in Unity `6000.3.23f1` and press Play with `Assets/Scenes/Main.unity` open. The scene
contains a `Main Camera` and one object with `SimulationDriver`; the driver builds every view at
runtime (terrain, machines, belts, items, enemies, shots, cursor, HUD), so there are no prefabs to
wire. The whole look lives in `Assets/Data/Palette.asset` — the shared material, the colours, the
screen-space line widths, and every view's geometry and layer depth — so a re-skin is a duplicate of
that asset, not a code change. The material itself is `Assets/Art/Materials/UnlitVertexColor.mat`.

The simulation itself (`Assets/Scripts/Core`) is engine-free and deterministic: it advances at exactly
30 Hz and reproduces identically from the same command stream, which is what the EditMode tests rely
on and what makes a balance run reproducible outside the Editor.

### Re-skinning without code

Every content's appearance can be overridden in the Inspector, in the asset the scene already wires.
Open `Assets/Data/Palette.asset` and unfold **Per-content visual overrides**. Each entry is off until
ticked, so an untouched palette draws exactly the shipped look - an override is purely additive.

* **Core** - tick *Override*, set *Source* to *Sprite*, drop in an image. The Core draws that image and
  still tints toward the damage colour as it takes hits, so the health readout survives the re-skin.
* **Items / Patches** - one entry per shape you want to re-skin; a shape with no entry, and every
  shape of a mineral added later, draws the built-in look. Give a shape a Sprite, or a different
  procedural silhouette, colour and size. The
  weakness icon on an enemy, the icon a machine draws for the shape it handles, and shots in flight
  all follow the same shape override, so a shape reads the same everywhere it appears.
* **Enemies / Machines** - one entry per building you want to re-skin, picked by kind. A Sprite
  replaces the body. A machine keeps its functional overlays (a turret's barrel and ammo icon, a
  decomposer's and splitter's port stubs), so its state stays legible. A building with no entry - and
  the mortar and the cutter are the shipped examples - draws the silhouette its behaviour already
  chooses, so it is the cannon's body a shade darker rather than a row in this list.

The shared colours, geometry and line widths above the override block still apply, and the material is
still `Assets/Art/Materials/UnlitVertexColor.mat`.

### Tuning and adding content without code

The scene also wires `Assets/Data/ContentDatabase.asset`: every cost, turret and enemy stat, shape
glyph and conversion recipe the simulation reads. Edit a value and it takes effect on the next Play -
no recompile. Every row is an **override**, so a kind with no row keeps the shipped value and the
asset is purely additive.

* **Retune** - change a cost, a fire interval, a drill's rate, a building's or an enemy's HP (a
  machine's `Max Hp` row; 0 keeps the shipped value, so an old asset is unchanged), or how far an enemy
  notices machines (its `Detection Range`; at or below the attack reach it only hits what it walks
  into). The HUD prints its
  numbers from the same definitions, so what it claims and what the tick does cannot disagree. Each
  row also carries the content's stable `Id`, and a row that names one lands by that id rather than by
  the enum's ordinal - so inserting a value into `BuildKind` cannot silently retune a different
  building. Leave the `Id` empty on a hand-added row and it is keyed by the dropdown, as before.
The ordinals themselves are nailed by `ContentFormatTests`: every value's exact slot (including
`TileKind`'s retired 3) is a failing test if it moves, because the stored asset numbers read them.
* **Add a machine that reuses a behaviour** - add the enum value in `BuildKind`, add a row naming its
  `Id`, `Tile`, `Behavior` and `Cost`, and (for a converter) point its `Recipe Id` at a recipe row. No
  switch anywhere has to learn about it: the tick dispatches on the definition, the card's sentence,
  numbers, badge and spin dispatch on the behaviour through `MachineReading` (the one narration module -
  what the HUD prints *is* what the tests cross), and the appearance block needs no entry either -
  a building with no override draws the silhouette its behaviour chooses.
* **Add an enemy or a shape** - the same: an enum value plus a row. The views now name every kind:
  an enemy needs a body colour (`Palette.EnemyColor`) and a shipped look (`EnemyVisuals.For`), and a
  shape a colour and a silhouette (`Palette.ShapeColor`/`ProcShapeOf`) - leave one out and the game
  throws at the missing switch with the kind's name, and `ContentFormatTests` goes red before you
  ever press Play. That is the whole cost; there is nothing else to remember.
* **Refresh the asset** - `FACET > Refresh Content Database from shipped table` rebuilds every row from
  the shipped numbers in place, which is how the committed asset learns about content added since it
  was made (and it keeps its GUID, so the scene's reference to it survives).
* Missing the asset? `FACET > Create Content Database` writes one filled with the shipped values, and
  `ContentDatabase.Default` is what the game runs on if the asset is empty or unwired.

The `FACET.Core` assembly still has `noEngineReferences`, so the ScriptableObject is only a carrier:
`ContentDatabaseAsset.ToCore()` converts it to the plain-C# `ContentDatabase` the simulation is handed,
which is what keeps a run deterministic and headless-testable.

### Maps are assets

A map is a `MapAsset` in `Assets/Data/Maps`. **`FACET ▸ Edit Map in Scene`** paints one where it is drawn,
**`FACET ▸ Map Editor`** paints one as a grid of tiles, and `Assets ▸ Create ▸ FACET ▸ Map` makes an empty one
to start from. The maps the game ships are assets of that same kind — Quarry and Foundry in
`Assets/Data/Maps`, with Canyon alongside them as painted editor data the campaign does not list —
so the maps a programmer edits and the maps a designer paints are one shape of data,
converted to the engine-free form by the same `ToCore()`.

The **campaign** - which of the maps the game plays, and in what order - is one more asset:
`Assets/Data/Campaign.asset`, a **`MapSet`** holding a list of maps. It is a file rather than an array on the
driver so that "what does this game ship" is one thing to read, diff and review instead of a list buried in a
scene file; `SimulationDriver` is wired to it in `Main.unity`, and a driver with no campaign reports it and
stands down rather than starting a run with nothing in it.

The window paints a `MapAsset` cell by cell: **Ground**, **Ore** (with the mineral it yields), **Rock**,
**Water**, **Void** (outside the map - not drawn, so the background shows through), the Core's position and
the wave entry points. Right-drag erases. It shows the map's size and starting stockpile, the wave table,
and - under the painting - **every problem the map has**, from `MapValidation`: a door with no walkable route
to the Core, a wave spawning inside a rock, a vein no belt can reach, a patch hanging off the edge, a terrain
grid that does not match the map's size. Those are the mistakes map data makes, and none of them throws at
runtime; they produce a wave that stands at its door forever, which reads as a hung game rather than as a bad
map. The same rules are asserted over every map the campaign ships in the test suite, so a map painted today
is held to the list the shipped ones were held to.

**Preview** draws the map in the Scene view while it is being painted, and it draws it the way the game will:
the ground, the rock and water with their rims, the veins in their mineral colours, the Core and the wave
entry points, at the map's real size, in the palette the scene's driver is wired to, with no Play mode and no
simulation behind it. It is `MapPreview` in the Editor assembly, and it does not re-implement the look: it
builds what `SimulationDriver` builds for a map - a `SimWorld` from `MapAsset.ToCore()`, and the same
`MeshView`s - and hands each view one frame per Scene view repaint through `MeshView.Draw`, which is the
gate's second caller and the reason a view can be drawn with no game running. It rebuilds whenever the map's
data changes, whoever changed it (the window, the Inspector, undo), and it stands down for the whole of Play
mode, where the driver's own views are drawing the same map. **Focus** frames the Scene view on it, which is
where the preview draws.

**`FACET ▸ Edit Map in Scene`** is the same map painted where it is drawn rather than in a grid of tones. It
frames the Scene view on the map, draws it with those same game views, and puts the brush in an overlay along
the top: the same brushes, the same mineral popup, and the tile under the pointer marked where a stroke would
land. Left-drag paints and **Shift**+left-drag erases, both through one `MapPainting.Stroke` - the call the
window's grid makes too - so a rock painted in the Scene view and a rock painted in the window are not two
rules that have to be kept in step. A stroke is one undo entry: the press opens a `MapEdit.Session` and the
release closes it, so the record-once-per-stroke and the dirty-once bookkeeping have one owner instead of
a ladder each surface climbed by memory - Ctrl+Z takes back a drag, not one tile of it. The grid's tile↔pixel
arithmetic is likewise its own module, `GridFrame` - fit, clamp, zoom, the y-flip and the hit-test, tested
from EditMode rather than only by a hand on the mouse. The brush takes the mouse only over the map, only while Alt is up
and no view tool is in hand, so the camera still moves the way it always did; the pointer is read by meeting the
view's ray with the plane the map is drawn in, so it lands on the tile it looks like it is on at any zoom; and
**Done** or **Escape** gives the Scene view back. The overlay's **Window** button opens the painting window on
the same map, which is where the wave table and the validator are - and the map is framed once per map rather
than once per repaint, because the camera is the designer's.

The asset is only a carrier, like the content table: `MapAsset.ToCore()` converts it to the plain-C#
`MapDefinition` the simulation runs on, so `FACET.Core` keeps its `noEngineReferences` contract and a run
stays reproducible from its own state. The conversion is a **read** - it answers about the asset as authored
and repairs nothing on the way through, so a validation during a repaint can no longer resize the map without
an undo entry. What the map *draws* has one enumeration, `MapAsset.DrawableView()` - the ground and mineral
of every in-map cell, the doors each carrying the table position a wave group names it by - and
`ContentHash()` is that walk's checksum, so the painting surface and the preview's rebuild-test agree by
being the same walk twice rather than by luck. Edits to the lists (doors, waves, groups, the Core cell, the
size) are operations on the asset that answer *whether anything changed*; `MapEdit.Session` is the editor's
transaction around them, owning the Undo record and the dirty flag that every caller used to restate.
Wiring a map into the game is one step: add it to the **Map Set** the
driver is wired to, in the position it should be played. The driver logs a warning at load if the map it just
built has any problem in it, so a bad map says so in the console rather than by playing strangely.

The ground itself is two layers, and the split is the point. `TileGrid` answers "what is standing on this
tile" (`Empty`, `Belt`, the Core, a machine); `TerrainField` answers "what is the ground" (`Ground`, `Ore`,
`Rock`, `Water`, `Void`) and owns the three rules that read both - may a walker stand here, may a belt be
laid here, may a machine be built here. Terrain is deliberately not a `TileKind`, so a new kind of ground is
a row in an enum plus the look it draws as - never a case in every rule that reads a tile. The ore's mineral
lives in the same layer, so removing whatever stands on a vein hands the vein back without anyone having to
remember what was underneath.

### Maps and the campaign

The campaign is the **Map Set** `SimulationDriver` is wired to: a list of `MapAsset`s in the order they are
played, converted once into the engine-free `MapDefinition`s every reader uses, behind `MapCatalog`. One list,
because "which map is next" is asked by the driver, by the campaign, by the HUD's *carry on* button and by the
tests - and a second source of maps is exactly what would have made a painted one invisible: the driver would
load it and the campaign would not know it existed, or the HUD would promise a map that never came. Each map
carries its own size, starting stockpile, ground, mineral patches, where its Core stands, its entry points and
its wave table.

Clearing a map is not the end of a run: `SimulationDriver` builds the next map as a **new
`SimWorld`** and points every view at it (`MeshView.Rebind`), so the ground, the entry markers and the
camera bounds follow it without reloading the scene. The world is replaced rather than reset because a
world is one run: its fields are sized by its map, and it must stay a thing you can replay from its own
state. What carries over is one number, in `CampaignState` - which map, and how many are cleared.

That split is what makes "map 4" cheap to add: a new map is an entry in the table or a painted asset, and a
new chain for it is rows in the content table. `SimulationDriver`'s **Start Map Index** boots straight into
any map for testing.

### What just happened: the event stream

The simulation reports what it does on one stream (`SimWorld.Events`): a building went up or came
down, an enemy hit a building or finished one off, a segment jammed (and with which shape), a shot was
fired, an enemy was hit or killed, a circle banked, the Core took damage, a wave started or was
cleared, a placement was refused (and why), the run ended. Nothing in the
simulation reads it back, so it cannot change the fight it describes - which is why every reaction to
the game hangs off it instead of off the simulation.

Three things use it today. **A kill bursts**: the enemy goes white-hot when a shot lands, and leaves an
expanding ring where it died - the first visual feedback in the project, and the reason to have a
stream at all. **The HUD reports the last second**: circles banked, shots, kills, buildings lost, and
where the last jam is, all read off the same stream, so the readout cannot disagree with the flash
about what just happened. **The HUD answers a refused click**: the stockpile is dry, or the tile is
taken, or the ground is wrong, and a toast prints the reason - which the view could not work out for
itself without re-running the placement rules and guessing which one bit. A sound or a screen shake
would be a fourth reader, not a fourth mechanism.

The buffer is a ring, so it always holds the most recent events and a reader needs no bookkeeping:
it reads the window it cares about and the present is guaranteed to be in there. `Dropped` counts
anything a very busy tick pushed out of the ring, so a reader can tell "nothing happened" from "I
fell behind" rather than guessing.

## Publishing the playable build

The player is one command, because a build that only exists on the machine that made it is not a
deliverable:

```bash
bash Tools/publish-pages.sh              # build, mirror the result into docs/, print the publish steps
bash Tools/publish-pages.sh --no-build   # mirror the Builds/WebGL already on disk
```

`Tools/build-webgl.sh` — or **FACET ▸ Build WebGL** in the Editor — writes the player to
`Builds/WebGL`, which is git-ignored: it is a 13 MB artefact. Pages serves one folder out of the
repository, so `publish-pages.sh` mirrors that folder into `docs/` and the repository carries that
instead. `docs/` is generated, not source: every run replaces it wholesale, so a file the build
stopped emitting cannot linger and be served, and nothing in it should be edited by hand.

Three things are what let the player survive a plain static host, and all three are in the build
rather than in a checklist someone has to remember:

* **Brotli with Unity's decompression fallback.** Pages sends no `Content-Encoding` header and a
  repository cannot set one, so a compressed payload has to be unpacked by the loader in JavaScript.
  Compressing *without* the fallback downloads fine and then fails to start, which reads as a broken
  game rather than as a misconfigured host.
* **`.nojekyll`.** Pages runs Jekyll over what it serves, and Jekyll drops anything it reads as its own
  templating. The marker is what turns that pass off.
* **Relative URLs.** `index.html` addresses the player as `Build/...` and not `/Build/...`, so the site
  works under the `/CS526-demo/` sub-path that a project page is served from.

Once per repository: **Settings ▸ Pages ▸ Source: Deploy from a branch ▸ `main` / `/docs` ▸ Save**,
which serves the site at <https://zwldarren.github.io/CS526-demo/>. After that, a publish is an
ordinary commit:

```bash
git add docs && git commit -m "build(pages): publish the WebGL player" && git push
```

One thing this repository does not currently satisfy: **Pages on the Free plan is public repositories
only**, so a private `zwldarren/CS526-demo` blocks the Pages setting outright. GitHub Pro lifts that
(it is free for students through the GitHub Student Developer Pack), and so does making the repository
public — but a Pages site cannot be access-controlled below GitHub Enterprise Cloud, so a site
published from a private repository is still readable by anyone with the link. Switching a repository
from public to private unpublishes its site.

## Tests

| Suite | Where | What it covers |
|---|---|---|
| EditMode | `Assets/Tests/EditMode` | belts (27), the content table and its Editor carrier (19), **the painting the two surfaces share (14)**, the event stream and its windows in time (14), **the maps and the campaign (12)**, production, drills and ore (11), turrets/jams (11), the tick, pause/restart and determinism (10), **map validation (10)**, waves and restart (9), the inspect cursor (9), economy and the Core bank (8), **the map asset - conversion, structural edits, the canonical walk and its hash (16)**, **the grid geometry the editor window hosts (8)**, **the ground, obstacles and the maps that use them (8)**, siege, health and destruction (7), the sorter (7), the second chain (7), visual overrides (7), splitters (6), the camera's pan bound (6), pipes (5), the machine registry and port masks (5), a custom table driving the game (5), enemies and doorways (4), end-to-end balance (3), **the content format - enum ordinals, hotkey reach, fallback switches, and what a card may claim (15)**, **the staircase the drag and its tests share (5)**, build settings (2), **the MeshView draw gate driven by hand (4)** - 274 total |
| PlayMode smoke | `Assets/Tests/PlayMode` | the shipped scene boots with its Palette and content asset wired, the driver builds every view, advances the world across frames, and draws real geometry for what is on the map; a map whose ground is rock, water and void draws it and leaves the void out; a Sprite override reaches the view; a kill leaves a burst that then fades; clearing a map loads the next one under the same views; a refused click reaches the HUD's band - 12 total |

```bash
# EditMode + PlayMode, headless, on either dev machine (Tools/locate-unity.sh finds the Editor, from
# its own EditorInstance.json or from the Unity Hub's install folders):
bash Tools/test.sh                 # both suites; `bash Tools/test.sh EditMode` is the fast loop

# Compile Core, Game and Editor without the Editor at all (~20s) - the check before a full run:
bash Tools/check.sh
```

Each run clears its own `Logs/<suite>.xml` before asking the Editor, so a batchmode that never compiled -
the one failure mode whose only outward sign is yesterday's green file still sitting there - reports
"no results" rather than reading as a pass. Trust the script's exit code, and when in doubt the
`<test-run …>` line at the top of the XML says the count and the verdict in one tag.

The balance tests are the ones to read first: `ScenarioTests.MapOne_HoldsItsWave_OnARealEconomy`
builds the reference defence on the real map inside the real budget — the starting stockpile plus
what an economy line physically belts home — and plays the wave to a win. The second test runs the
same layout with no decomposer: raw circles reach the cannon, the line jams, and the Core falls.

## Code map

```
Assets/Scripts/Core        engine-free simulation (no UnityEngine reference)
  TileGrid, BeltField      the map and the belt layer: one item per cell, hand-offs as a fixpoint
  TerrainField,            the ground itself: ore, rock, water, and the void outside the map, plus the
  TerrainKind              three rules that read both layers (walkable, belt, machine) - the layer that
                           makes a map something other than a rectangle
  MapDefinition,           the map definition: size, budget, ground, patches, Core, spawn points, waves;
  TerrainRect              a map's terrain is a cell array, authored as rectangles
  MapValidation            the rules a map has to satisfy to be playable, as one engine-free function:
                           doors that cannot reach the Core, ore no belt can reach, a Core off the grid -
                           what the painting window shows and what the tests assert
  ContentDatabase          the one table of what exists: shapes, recipes, machines, turrets, enemies, costs
  MachineField             the built things: where they are, what they hold, their health, and the
                           occupancy registry; enemy damage and destruction (no refund) land here, and
                           the map's doorways are the one tile it refuses to build on
  MachineDelivery          the belt-to-machine rule: which cells deliver in, which sides are outlets
   MachineReading           what a machine means to the player, said once: role, catalogue line and
                            numbers, the live card sentence with its severity, facing, ports, badge
                            and work - behaviour-dispatched, throwing on a behaviour it has no answer
                            for. The HUD formats and colours what this returns, and the tests cross
                            the same interface, so the card cannot drift from the table; it also owns
                            the drill's and the pipe's landing cells, which the tick delivers through
  MachineSystem            one pass over the machines, dispatching each to its definition's behaviour
  DrillBehavior …          the behaviours: drill, converter (recipe-driven), pipe, splitter, sorter, turret
  SimTelemetry,            the tick's counters, and the context and interface a behaviour is handed - the
  MachineTickContext       seam that makes a new machine a row in the table rather than a new case
  SimEvents                the tick's event stream: what the simulation reports having done, and the
                           windows in time a reader asks of it ("counted since", "the last one within",
                           "did a drop land inside this window")
  CampaignState            where the player is across maps: which map, and how many are cleared
  CoreSinkSystem           deliveries into the Core bank as spendable circles
  PathField                the walkers' way to the Core: a BFS direction field over the cells a walker may
                           enter (no machine, and ground the map drew that is open), rebuilt only when a
                           building comes or goes - terrain is fixed, so it cannot invalidate the field
  EnemyField               walks the flow field, stops to hit machines in reach, hits the Core, and
                           keeps walkers a body apart
  ProjectileField          shots in flight, so the shape that killed something is visible
  WaveDirector             the map's waves: the first held for the player, the countdowns after it, and what
                           counts as a cleared wave - and WavePhase, the one word for what the clock is doing
                           now (Running/Intermission/Held/Over) so readers stop recomposing it from the
                           CurrentWave and NextWave sentinels
  BuildController          selection, the read pin, drag-to-lay, machine aiming, jam-clearing delete, and the bill
   BeltWalk                 the one statement of the staircase a belt drag lays - dominant axis first,
                            X wins a tie: the drag walks it, Predict answers it whole for ghosts and
                            tests, and no test keeps a private copy of it any more
  EconomyState             the stockpile: spend, refund, bank (costs come from the content table)
  Balance                  the global rules only (core reach, projectile radius/lifetime, enemy
                           spacing, weakness multipliers) - the old facade over the shipped table is
                           gone; call sites name their table (`world.Content`, or `ContentDatabase.Default`
                           when they mean the shipped one) instead of a second spelling of the global
  SimWorld                 the tick order and the run's state. The seam is chosen: the public fields are
                           the interface to the modules (a jam, an item, a turret count is written on
                           Belts/Machines/Waves directly), and a method here forwards only to state that
                           is deliberately private (the build gestures, the shot telemetry) or lands on
                           a tick boundary (RequestNextWave, RequestRestart). No pass-throughs left
                           re-exposing a field the caller could have crossed to anyway
   InputCommand             everything an input source may ask one tick to do; the one-shot latch the
                           driver and the tests both honour
   Gesture                  the game's own named gestures - Click, Aim, Release, Removal, Drag - built on
                           InputCommand so "what a click is" is spelled once, reused by the tests and by
                           anything that drives the simulation through the tick
Assets/Scripts/Unity       view layer (one renderer per concern, one mesh each, Palette for colour)
  Palette                  the whole look, including per-content Sprite/procedural overrides (VisualCatalog)
  VisualCatalog            the override types: VisualStyle, the kind-keyed per-content catalogs, and the shape helpers
  GridRenderer             the ground: runs of drawn tiles filled, boundaries touching ground lined, so a
                           map with holes in it is not a rectangle with a grid over it
  TerrainRenderer          the map's own obstacles, filled per run of a kind and rimmed where the kind changes
  OreRenderer              the ore in the ground: bed quads plus mineral silhouettes, read off TerrainField.GetOre
  MapAsset                 the carrier for a painted map: ToCore() converts it to the engine-free one as a
                            **read** (never repairing the asset on the way), DrawableView()/ContentHash() are
                            one canonical walk of what the map draws and its checksum, and the lists are
                            edited through operations that answer "changed" - the paint calls a brush makes
                            are MapPainting's (one answer for both surfaces), the Undo/dirty bookkeeping
                            around them is the editor's MapEdit.Session (one transaction for both)
  MapPainting              the one place a brush writes to a MapAsset: stroke, fill, doors and erasing, shared
                           by the painting window and the Scene view's editor
  MapSet                   the campaign: the maps, in the order they are played, for the driver to walk
  MapCatalog               the progression as one list, converted once, with the clamping every reader shares
  ContentDatabaseAsset     the Editor carrier for the content table; ToCore() converts it to the engine-free one
  HudLayout, PanLimits     where the HUD's panels sit, and the pan bound that implies: the map has to cover
                           the screen minus the strips they own, so no tile is stuck behind a panel
Assets/Scripts/Editor      the WebGL build tool, the content-asset creator, the map painting window, the
                           Scene view's own map editor, and MapPreview - which draws a map with the game's own
                           views, without Play mode, for both of them. Two modules the painting surfaces share:
                            GridFrame          the grid's tile↔pixel geometry as pure arithmetic (fit, clamp,
                                              the y-flip, hit-test, scroll-to-cell) so it is EditMode-testable
                                              off the IMGUI that hosts it; the window is a thin adapter over it
                            MapEdit.Session    one map-edit transaction - records for Undo once before the first
                                              write, dirties once after, wraps the strokes and structural edits -
                                              so the two stroke surfaces and the wave table cannot drift on the
                                              record/dirty/array-identity protocol they used to each restate
```
