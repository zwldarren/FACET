# FACET

A 2D top-down factory-defence prototype where **an enemy's weakness is an exact shape and your damage is a
function of the shape your factory runs**. Every item on the map is four quadrants of two minerals - cut,
turned and stacked by three operator machines into one of eighty items - and a turret fires only the round a
belt physically delivered to it. A round matching the wave's demand does full damage; anything else a quarter.

Unity 6 (`6000.3.23f1`), 2D, built for WebGL. A run walks the **Map Set**
(`Assets/Data/Campaign.asset`): clear a map and the next one is built in place, under the same views,
carrying over only where the player is.

## How to play

Mine a mineral, cut it up, build the shape the briefing asks for, and shoot what comes through the door. The
run opens with a stockpile of circles and one rule that explains everything else: **a machine's ports are
read off the belts around it** - a belt pointing in is an input, a belt pointing away is an output - so a line
is wired by laying belts at it, not by configuring it.

| Input | Action |
|---|---|
| `WASD` / arrows, mouse wheel | pan and zoom the camera |
| click the build bar, or `1`–`9`, `0` | select a building (`0` is the turret); the same digit or tile again drops the selection |
| `Q` / `E` | turn the placement ghost - a cutter's cut line and a rotator's outlet are chosen this way |
| `Esc` | drop the selection: the cursor then only **reads** what is under it, which is the state a run opens in |
| left mouse | place one belt, or land a machine; **drag** to lay a belt run or to aim a machine. With nothing selected, the same click pins whatever is under it and the info card describes *it* |
| right mouse | delete for a full refund; on a jammed segment it **clears the jam** instead |
| `Space` / `P` | pause / resume |
| `N` | start the next wave early — every wave also starts itself when its countdown runs out |
| `C` | put the Core back in the middle of the view, from anywhere on the map |
| `H` | open or close the controls card — the keyboard and the restart button, over the map |
| `R` | restart the run |

A sorter's filter is set from the info card: pin a sorter and the card grows a **quadrant canvas** - four
cells you cycle through hole / circle / square, plus the choice between "contains a mineral" and "exactly this
item". The machine's badge is the shape (or the mineral) it routes for.

The HUD is four clickable regions: a status card top-left (map and wave, the run's **seed**, the Core's health
and the wave clock as two bars, what is coming as a row of chips - the count, the enemy, and the exact shape it
is weak to, drawn as a quadrant figure, for the next wave *and* the one after - and two diagnostic lines that
speak only when something needs them: a jam or a starved gun, what is out of view - with the card's edge strip
warning-coloured whenever any of those need you), a controls cluster top-right (pause, and a help button that
opens the keyboard and the restart button over the map; `H` opens it too), the build bar along the bottom
(the operators this map has not unlocked are greyed out and refuse to be selected, and tiles are tinted when
the stockpile cannot cover them), and an info card above it. Beside the status card floats the **stockpile**:
the shape the run banks, in that shape's own colour. A refused placement says why in a **toast**, and whatever
is under the pointer says what it is and what it is doing in a **tooltip** beside the cursor — the map is
readable at the pointer whether or not a building is selected.

A map is bigger than a screen, so the edge of the screen answers *where*: the **locator** points at
everything the player can neither see nor click. A wave off-view is one chevron in the colour of its nearest
walker, dotted once per enemy; a jammed segment off-view is a red mark, because that is a tile a right-click
has to reach; the Core is a pale mark, because it is the way back. Out of view means what it says — a walker
under the build bar or the status card is as unfindable as one off the display, so the locator works off the
HUD's own panel boxes, and an arrow is stepped back along its sight line until it clears the card it would
otherwise sit under. While the Core is being chewed on out of view the screen wears a warning frame. The
status card says the same thing in numbers, off the same answer, so the arrows and the sentence cannot
disagree.

## The shape algebra

**An item is four quadrants** - NW, NE, SE, SW - each one a hole, a circle or a square. Two bits a quadrant,
so an item is a `uint` and equality is exact: a top half-circle and a bottom half-circle are two different
items, and so are circle-left/square-right and square-left/circle-right. There are `3⁴ - 1 = 80` of them, and
the whole set is enumerable at build time.

Three operators act on that space:

* **The cutter** splits one item along the cut line its facing selects - the blade runs across the facing
  arrow, not along it. Facing north or south cuts across (top
  piece and bottom piece); facing east or west cuts down the middle (left and right). Both pieces leave at
  once, one per outlet - the facing side takes the piece on that side, the side opposite takes the other - and
  each outlet keeps its own buffer, so one blocked side never costs the other piece. Cutting a quarter yields
  the quarter plus nothing, and that is why "an item can be cut at most twice" is a corollary rather than a
  rule. The cut line itself is drawn - across the placement ghost and across every placed cutter - so which
  two pieces a machine is aimed at is on the screen, not in this manual.
* **The rotator** turns an item a quarter clockwise, always clockwise: counter-clockwise is three of them in
  series, which is a logistics puzzle rather than a second machine. A half turn is *not* the identity - it
  gives the complement piece on the same cut line - so the rotator is also the orientation fix: the briefing
  asked for a top half-circle and the line is running bottom halves, and one turn repairs it.
* **The stacker** unions two items whose filled quadrants do not overlap: circle-left beside square-right is
  one round, and an overlapping second piece simply waits on its belt. That is a stall, not a jam - the item
  it cannot merge is one the player spent a chain building.

One worked example, which is also a test: cut a circle and a square, both across. The circle's bottom half
turned once is the left half-circle; the square's top half turned once is the right half-square; stack them
and you have circle-left / square-right. The leftovers - top half-circle under bottom half-square - stack
directly into circle-top / square-bottom. One pair of cutters, two turns and two stacks, both mixed rounds
done.

## The rules a run is played against

* **Everything costs circles, and circles are only spendable once belted into the Core.** A drill mines one
  full shape per second onto the belt it faces; the line home is a purchase (1 ○ per belt cell), so the map's
  distances are the economy. Removal refunds in full. A full circle delivered to the Core is one currency;
  anything else delivered is destroyed — which is the standing tension: **every circle ore is money or
  ammunition**, and a mixed round's material spans both mineral chains.
* **The briefing names an exact item per group, orientation included**, and the wave generator draws those
  demands only from what this map can actually build - the closure of its minerals under its unlocked
  operators. Deep demands come with the tougher, slower archetype, so progression lives in the shapes asked
  for rather than in a stat table.
* **Matched rounds do full damage, anything else a quarter.** There is one gun: a **universal chamber** that
  eats any shape, one round per shot, no diet table and no splash. It does not consume while it has no target
  or is cooling down, so ammunition queues visibly on the belt. It prefers the enemy its chambered round
  actually hurts. Its real fire rate is `min(1/s, what its lines deliver)` — damage is a throughput property
  of the layout *and* a shape property of the round.
* **A run's only randomness is its seed**, which the HUD prints: the wave generator is a seeded stream, so a
  run is reproducible from the number on the screen. The seed is drawn fresh every run — two runs of one map
  do not play out identically — and a map may pin a non-zero seed of its own to replay exactly.
* **A belt is transport, not a machine:** it may cross a vein or a wave's doorway, and it never jams on a
  wrong *destination* — a belt pointed at rock just stalls at full progress.
* **The pipe jumps one tile** (two cells ahead), which is how two belts cross. The **sorter** routes by
  shape: what its filter matches leaves by the side it faces, everything else by the other outlets.
* **Machines are solid and destructible; belts are walkable and are not.** Enemies follow a flow field
  to the Core *around* machines, attack what they walk into, and **charge a turret they notice** from a
  little further out — so a gun belongs three tiles off the lane, and a bare wall beside the road is
  bait. A walker only charges or hits what it can see, so rock between the two is walked round rather
  than leaned on. Walkers keep a body's width apart. Destruction pays no refund.
* **A sealed Core is breached at the cheapest hole.** With no way round, a wave follows the route that
  prices the player's buildings and walks to the shortest crossing, rather than pressing at whichever
  boulder happens to lie between it and the Core. Rock is still a wall to that route, and so is a block
  nothing may attack: sealing a lane with walls buys time, never immunity.
* **The wall (2 ○, 120 hp) is the maze tool**, and it may not stand on a wave's doorway.
* **The opening clock starts with your first building**, so reading the map and planning the first line
  cost no time at all. `N` calls the wave early either way.
* **A drill needs a shape patch, and a patch takes only a drill** — but a belt may be laid across ore,
  and the ore is still there under it when the belt is removed.
* **The ground is the map's, not the player's.** Rock, water and the void outside the map are the
  author's obstacles: nothing is built on them and the walkers' flow field reads them. A wall the player
  buys can be chewed through; a rock ridge cannot fall.

## The campaign

Progression is the **operator unlock ladder**, not the map's number: a map declares which operators it hands
over (`Unlocked`), and a machine whose operator is locked cannot be built at all - the build bar does not
offer it and a placement is refused.

| Map | Size | Teaches | Unlocks | Waves |
|---|---|---|---|---|
| Quarry | 48×72 | Cutting, and the money loop | Cutter | 6 scripted |
| Foundry | 64×40 | The second mineral, and orientation | Cutter, Rotator | 8 scripted |
| Canyon | 72×44 | Combination - mixed rounds | All three | 3 scripted, then 12 generated |

Canyon's authored table teaches the mixed round; the twelve waves after it are the generator's exam, drawn
from exactly what that map can build. **Endless mode** is the same mechanism with no bound: a map whose
`Generated Waves` is `-1` never runs out of demands.

## Development

Open the project in Unity `6000.3.23f1` and press Play with `Assets/Scenes/Main.unity` open. The scene
holds a `Main Camera` and one object with `SimulationDriver`; the driver builds every view at runtime
(terrain, machines, belts, items, enemies, shots, cursor, HUD), so there are no prefabs to wire.
`SimulationDriver`'s **Start Map Index** boots straight into any map for testing.

```bash
bash Tools/test.sh                 # EditMode + PlayMode, headless (Tools/test.sh EditMode is the fast loop)
bash Tools/check.sh                # compile Core, Game and Editor without the Editor at all
python Tools/check-maps.py         # the map assets' own format, without opening the Editor
bash Tools/publish-pages.sh        # build the WebGL player and mirror it into docs/, for GitHub Pages
```

Results land in `Logs/editmode.xml` and `Logs/playmode.xml`; each run deletes its own file first, so a
suite that never ran reports "no results" instead of reading as a pass. The EditMode suite draws its
own maps and reads no shipped map data, so repainting a map cannot turn it red - the files in
`Assets/Data/Maps` are held to their format by `Tools/check-maps.py` instead, which is what a hand edit
of one gets checked against.

### Where things live

```
Assets/Scripts/Core        the engine-free simulation (no UnityEngine reference), deterministic at 30 Hz
Assets/Scripts/Unity       the view layer: one renderer per concern, one mesh each, Palette for colour
Assets/Scripts/Editor      the WebGL build tool, the content-asset tools, the map painting window
                           (FACET ▸ Map Editor) and the Scene view's own map editor
Assets/Tests/EditMode      the simulation, the shape algebra, the generator, the content table, the maps
Assets/Tests/PlayMode      the shipped scene booting for real, through the driver and the views
Assets/Data                the assets the scene is wired to: Palette, ContentDatabase, Campaign, Maps
```

`Assets/Scripts/Core` is plain C# with `noEngineReferences`, so a run is reproducible from its own
state and testable without a scene; the Editor-facing assets are carriers that convert to plain
definitions (`ContentDatabaseAsset.ToCore()`, `MapAsset.ToCore()`).

### Tuning and adding content without code

The scene wires `Assets/Data/Palette.asset` (the whole look: material, colours, line widths, geometry,
and a per-content Sprite or procedural override block) and `Assets/Data/ContentDatabase.asset` (every
cost, gun and enemy stat). Every row of both is an **override**, so a kind with no row keeps the shipped
value and an asset is purely additive; edit a value and it takes effect on the next Play, with no
recompile.

* **Retune** — a cost, a cutter's cycle, a gun's damage, a building's or an enemy's HP, how far an enemy
  notices machines. The HUD prints its numbers from the same definitions, so what it claims and what
  the tick does cannot disagree.
* **Add a machine that reuses a behaviour** — an enum value in `BuildKind` plus a row naming its `Id`,
  `Tile`, `Behavior` and `Cost`. No switch has to learn about it. A new *behaviour* is bigger on purpose:
  it must be given a role, a catalogue line, a badge and a port rule by name, and every one of those
  dispatches throws rather than guessing.
* **Narrow a machine's diet** — set the row's `Override Diet`. Every shipped machine eats any shape, so
  nothing in the shipped game jams; a machine that refuses something brings the old routing tension back,
  and the belt it landed on jams until a right-click clears it.
* **Add an enemy or a mineral** — a colour, a shipped look, and (for an enemy) an enum value plus a row.
  Shapes are *not* content: the item space is the algebra's 80, so a new mineral would be a third value in
  `Quad` and everything else follows - which is why the retune block has no shape rows to edit.
* `FACET ▸ Refresh Content Database from shipped table` rebuilds the committed asset from the shipped
  numbers in place, keeping its GUID; `FACET ▸ Create Content Database` writes a fresh one.

### Maps are assets

A map is a `MapAsset` in `Assets/Data/Maps`. **`FACET ▸ Map Editor`** paints one as a grid of tiles,
**`FACET ▸ Edit Map in Scene`** paints the same map where it is drawn, and `Assets ▸ Create ▸ FACET ▸
Map` makes an empty one to start from. The brushes are Ground, Ore (with the mineral it yields), Rock,
Water, Void, the Core and the wave entry points; right-drag erases, one stroke is one undo entry, and
the window shows the map's size, starting stockpile, **unlocked operators, seed and generated waves**,
the wave table - where each group also names the exact item it is weak to - and **every problem the map
has**, from `MapValidation`: a door with no walkable route to the Core, a wave spawning inside rock, a
vein no belt can reach, a patch hanging off the edge, a group with no demand. None of those throws at
runtime; they produce a wave that stands at its door forever, or one that can never be matched.
**Preview** (and the Scene view editor) draws the map with the game's own views, with no Play mode.

A vein is written in one place: the `Ore:` list names its cell and its mineral, and the cell's
`Terrain:` byte stays plain. The conversion lets a patch claim its cells as ore whatever the array says,
so the array is not where a vein is added or taken away - editing that half alone is what goes wrong
quietly. An array that says Ore with no entry beside it is a tile only a drill may stand on and that
yields nothing when it does; a second entry for one cell is ignored, because the first one counts.
`MapValidation` names both and the window shows them, and `python Tools/check-maps.py` checks them in the
file itself - together with the two wave rules - where a hand edit lands before any Editor is open.

Wiring a map into the game is one step: add it to the **Map Set** (`Assets/Data/Campaign.asset`) in the
position it should be played.

### The event stream

The simulation reports what it does on one stream (`SimWorld.Events`): a building up or down, a machine
hit or destroyed, a segment jammed, a shot fired, an enemy hit or killed, a circle banked, the Core
damaged, a wave started or cleared, a placement refused and why, the run ended. Nothing in the
simulation reads it back, so it cannot change the fight it describes — the kill burst, the HUD's
"last second" readout and its toasts are all readers of it, and one more reader (a sound, a shake) is
not a fourth mechanism. The buffer is a ring, so a reader needs no bookkeeping; `Dropped` says when a
busy tick pushed something out.

### Publishing the playable build

`Tools/publish-pages.sh` builds the player, mirrors `Builds/WebGL` into `docs/` and prints the publish
steps (`--no-build` mirrors what is already on disk). `docs/` is generated, not source: every run
replaces it wholesale, so nothing in it should be edited by hand. The build carries the three things a
plain static host needs — Brotli with Unity's decompression fallback, a `.nojekyll` marker, and
relative URLs — and is served at <https://zwldarren.github.io/CS526-demo/>. Pages requires **Settings ▸
Pages ▸ Source: Deploy from a branch ▸ `main` / `/docs`**, and on the Free plan a public repository.
