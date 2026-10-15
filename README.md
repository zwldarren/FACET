# FACET

A 2D top-down tower-defense prototype where **circles are money and half-circles are ammunition** —
and both are physical items riding conveyor belts. There is no abstract currency: a circle mined at
the edge of the map is not spendable until a belt delivers it into the Core, and a cannon fires only
the half-circles that physically reach it. A wrong-shape delivery jams the belt segment it lands on
and silences everything downstream, so the answer to pressure is re-plumbing the line.

Unity 6 (`6000.3.23f1`), 2D, built for WebGL. A run walks the **Map Set**
(`Assets/Data/Campaign.asset`): clear a map and the next one is built in place, under the same views,
carrying over only where the player is.

## How to play

Mine a circle, belt it home, split it into ammunition, and shoot what comes through the door. The
run opens with a stockpile of circles and one rule that explains everything else: **a machine's ports
are read off the belts around it** — a belt pointing in is an input, a belt pointing away is an
output — so a line is wired by laying belts at it, not by configuring it.

| Input | Action |
|---|---|
| `WASD` / arrows, mouse wheel | pan and zoom the camera |
| click the build bar, or `1`–`9`, `0` | select a building (`0` is the assembler); the same digit or tile again drops the selection |
| `Q` / `E` | turn the placement ghost (splitter, converters and the wall have no facing) |
| `Esc` | drop the selection: the cursor then only **reads** what is under it, which is the state a run opens in |
| left mouse | place one belt, or land a machine; **drag** to lay a belt run or to aim a machine. With nothing selected, the same click pins whatever is under it and the info card describes *it* |
| right mouse | delete for a full refund; on a jammed segment it **clears the jam** instead |
| `Space` / `P` | pause / resume |
| `N` | start the next wave early — every wave also starts itself when its countdown runs out |
| `C` | put the Core back in the middle of the view, from anywhere on the map |
| `H` | open or close the controls card — the keyboard and the restart button, over the map |
| `R` | restart the run |

The HUD is four clickable regions: a status card top-left (map and wave, the Core's health and the wave
clock as two bars, what is coming as a row of chips — the count, the enemy, and the shape it is weak to —
and two diagnostic lines that speak only when something needs them: a jam or a starved gun, what is out of
view — with the card's edge strip warning-coloured whenever any of those need you), a controls cluster
top-right (pause, and a help button that opens the keyboard and the restart button over the map; `H` opens
it too), the build bar along the bottom (tiles tinted when the stockpile cannot cover them, with a line
showing how far from covering them), and an info card above it. Beside the status card floats the
**stockpile**: the shape the run banks, in that shape's own colour. A refused placement says why in a
**toast**, and whatever is under the pointer says what it is and what it is doing in a **tooltip** beside
the cursor — the map is readable at the pointer whether or not a building is selected.

A map is bigger than a screen, so the edge of the screen answers *where*: the **locator** points at
everything the player can neither see nor click. A wave off-view is one chevron in the colour of its nearest
walker, dotted once per enemy; a jammed segment off-view is a red mark, because that is a tile a right-click
has to reach; the Core is a pale mark, because it is the way back. Out of view means what it says — a walker
under the build bar or the status card is as unfindable as one off the display, so the locator works off the
HUD's own panel boxes, and an arrow is stepped back along its sight line until it clears the card it would
otherwise sit under. While the Core is being chewed on out of view the screen wears a warning frame. The
status card says the same thing in numbers, off the same answer, so the arrows and the sentence cannot
disagree.

The rules a run is played against:

* **Everything costs circles, and circles are only spendable once belted into the Core.** A drill mines
  one circle per second onto the belt it faces; the line home is a purchase (1 ○ per belt cell), so the
  map's distances are the economy. Removal refunds in full.
* **The decomposer splits either mineral — ○ → ◠◠ or □ → ▭▭ — in 1.5 s**, the recipe chosen by what is
  fed to it, and jams on anything already split. One outlet caps it at 1/s, which is exactly a cannon's
  rate; a second outlet lets it run faster.
* **The assembler fuses ◠ + ▭ → ◈** in 2 s — the deep end of the ammunition ladder, where the two
  mineral chains meet. A composite round **matches every weakness**: its structure contains both
  minerals, so no enemy resists it.
* **Square veins open a second chain:** the same decomposer runs □ → ▭▭, and a **mortar** eats ▭ and ◈.
  The mortar cycles half again as long and its shell **bursts over an area** — it answers a pack where
  the cannon answers a trickle, so it is a choice rather than an upgrade.
* **The cannon eats ◠ and ◈, the mortar ▭ and ◈** — which ammunition a shot is made of is which belt
  delivered it, so what a gun fires is a routing decision. Each fires at most one shot per interval;
  their real fire rates are `min(1/interval, what their lines deliver)`. Damage is a throughput
  property of the layout, not a purchase.
* **A wrong-shape delivery jams that segment** and it stays jammed. Right-click clears it, destroying
  that item and keeping the belt.
* **A belt is transport, not a machine:** it may cross a vein or a wave's doorway, and it never jams
  on a wrong *destination* — a belt pointed at rock just stalls at full progress.
* **The pipe jumps one tile** (two cells ahead), which is how two belts cross. The **sorter** routes by
  shape: the shape it filters for leaves by the side it faces, everything else by the other outlets.
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
Assets/Tests/EditMode      the simulation, the content table, the painting and the campaign machinery
Assets/Tests/PlayMode      the shipped scene booting for real, through the driver and the views
Assets/Data                the assets the scene is wired to: Palette, ContentDatabase, Campaign, Maps
```

`Assets/Scripts/Core` is plain C# with `noEngineReferences`, so a run is reproducible from its own
state and testable without a scene; the Editor-facing assets are carriers that convert to plain
definitions (`ContentDatabaseAsset.ToCore()`, `MapAsset.ToCore()`).

### Tuning and adding content without code

The scene wires `Assets/Data/Palette.asset` (the whole look: material, colours, line widths, geometry,
and a per-content Sprite or procedural override block) and `Assets/Data/ContentDatabase.asset` (every
cost, turret and enemy stat, shape word and conversion recipe). Every row of both is an **override**,
so a kind with no row keeps the shipped value and an asset is purely additive; edit a value and it
takes effect on the next Play, with no recompile.

* **Retune** — a cost, a fire interval, a drill's rate, a building's or an enemy's HP, how far an enemy
  notices machines. The HUD prints its numbers from the same definitions, so what it claims and what
  the tick does cannot disagree.
* **Add a machine that reuses a behaviour** — an enum value in `BuildKind` plus a row naming its `Id`,
  `Tile`, `Behavior` and `Cost` (and, for a converter, a `Recipe Id`). No switch has to learn about it.
* **Add an enemy or a shape** — the same: an enum value plus a row, a colour, and a shipped look. Leave
  one out and the game throws with the kind's name; `ContentFormatTests` goes red before you press Play.
* `FACET ▸ Refresh Content Database from shipped table` rebuilds the committed asset from the shipped
  numbers in place, keeping its GUID; `FACET ▸ Create Content Database` writes a fresh one.

### Maps are assets

A map is a `MapAsset` in `Assets/Data/Maps`. **`FACET ▸ Map Editor`** paints one as a grid of tiles,
**`FACET ▸ Edit Map in Scene`** paints the same map where it is drawn, and `Assets ▸ Create ▸ FACET ▸
Map` makes an empty one to start from. The brushes are Ground, Ore (with the mineral it yields), Rock,
Water, Void, the Core and the wave entry points; right-drag erases, one stroke is one undo entry, and
the window shows the map's size, starting stockpile, wave table and **every problem the map has**, from
`MapValidation` — a door with no walkable route to the Core, a wave spawning inside rock, a vein no
belt can reach, a patch hanging off the edge. None of those throws at runtime; they produce a wave
that stands at its door forever. **Preview** (and the Scene view editor) draws the map with the game's
own views, with no Play mode.

A vein is written in one place: the `Ore:` list names its cell and its mineral, and the cell's
`Terrain:` byte stays plain. The conversion lets a patch claim its cells as ore whatever the array says,
so the array is not where a vein is added or taken away - editing that half alone is what goes wrong
quietly. An array that says Ore with no entry beside it is a tile only a drill may stand on and that
yields nothing when it does; a second entry for one cell is ignored, because the first one counts.
`MapValidation` names both and the window shows them, and `python Tools/check-maps.py` checks them in the
file itself, where a hand edit lands before any Editor is open.

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
