# GLOSSARY

The names this codebase uses for its concepts, so a suggestion, a test name and a comment say the
same thing. Architecture *vocabulary* (module, interface, seam, depth, leverage, locality) is the
shared design language and lives outside this file; this file is the project's own domain.
Mechanics in full are in [README.md](README.md); this is only the naming.

## The economy of things

- **Content table** (`ContentDatabase`) - the one table of what exists: shapes, recipes, machines,
  turrets, enemies, costs. Enum ordinals into it are the save format; rows are keyed by `Id`.
- **Row / slot** - one machine's entry, indexed by its `BuildKind`. A `MachineRow` in the ScriptableObject
  carrier overrides the whole definition except its `Id`, its slot, and the additive `MaxHp`.
- **Behaviour** (`BehaviorKind`) - what a machine *does*: drill, converter, pipe, splitter, sorter,
  turret, or none. The tick, the badge, the draw and the narration all dispatch on it - and every
  dispatch that dispatches on it names every kind it has an answer for, and throws on the rest.
- **Belt, hub, run** - the transport vocabulary: a **belt** carries one item per cell, a **hub**
  (splitter/sorter) reads its outlets off the belts wired around it, a **run** is a laid line of belts.

## Laying belts

- **Staircase** - the Manhattan path a belt drag lays: dominant axis first, X wins a tie, one
  decision per walk (not per step), so a diagonal reads as two straight legs.
- **`BeltWalk`** - the one statement of the staircase rule. `BuildController.LayPath` walks it
  incrementally; `BeltWalk.Predict` answers "what would a drag from A to B lay" for ghosts, tests
  and anything else asking. Geometry only - whether a cell *accepts* a belt is the BuildController's
  question.

## The wave's walk

- **Flow field** (`PathField`) - the way to the Core from every walkable cell, stored as one step
  per cell rather than a path per enemy. It is kept three times: the **direct field** (the plain
  breadth-first flood the game has always simulated), the **flank field** (a Dijkstra where a cell
  under a standing gun's range costs `Balance.PathThreatWeight` extra) and the **breach field** (a
  Dijkstra where a cell carrying a building costs `Balance.PathBreachWeight`). All three rebuild only
  when a building comes or goes; terrain is fixed and cannot invalidate them.
- **Breach** (`TryBreach`) - the way in when there is no way round: the route that walks *through*
  what the player built, priced per building cell so the wave takes the shortest crossing rather than
  the nearest one. Read only by a walker whose chosen field has no direction for its cell, so an
  unsealed run walks exactly as it always did - and rock stays a wall to it, as does a building with no
  health (`MachineDef.MaxHp` 0), which nothing may chew.
- **Sight** (`EnemyField.Sees`) - whether the straight line from a walker to a machine's cell centre
  crosses ground it cannot cross. The one thing that keeps the wave's aggression honest: a machine it
  notices but cannot see is walked round rather than pressed at, and since a line in sight is a line
  `Advance` can walk, a charge never grinds. It settles what may be hit as well as what may be charged:
  the reach is asked about a machine the walker can see, not merely one that is near.
- **Threat** - 1 for a cell whose centre is inside a standing turret's range, 0 elsewhere. Asked of
  the machines, not of the shots: a starved gun still covers its ground, because a wave reads the
  gun on the ground and not the magazine behind it. Belts and walls project none - a wall is there
  to be chewed.
- **Flank decision** (`FlankAt`, `EnemyState.AvoidsFire`) - the one judgement a walker makes, once,
  at the door it came in: follow the flank field only when the open way undercuts what the direct
  way would itself pay under fire (`_directWeighted`, the chain sum of the direct field's own
  steps) by `Balance.PathDetourFactor` plus `PathDetourSlack` steps of slop. The choice is kept for
  the walker's life, so a field rebuilt under a walking wave never dithers it between the two; the
  wave's next spawn weighs the new ground instead. With no turret on the map the decision always
  reads "direct", which is why a threat-free world plays bit-for-bit as it always did.

## Being out of view

- **Locator** (`ThreatRadar`) - the game's one answer to "where is it?", for everything the player can
  neither see nor click. Arithmetic on a rectangle, not a renderer: two readers ask it -
  `ThreatRadarView` draws the arrows, `HudView` prints the same numbers - so the marks on the edge of the
  map and the sentence in the status card cannot disagree about what is out of view.
- **Open screen** (`ThreatRadar.Viewport`) - the part of the window no HUD panel covers, and therefore the
  part a click can reach. The rule the whole feature hangs on, and the reason it reads
  `HudLayout.Panels` - the cards' boxes - rather than `HudLayout.Insets`: an inset is one number per side,
  which is right for a camera bound and wrong for a question about a tile, because the ground below the
  status card is free and a walker there needs no arrow.
- **Arrow / mark** - what the locator puts on the edge of that open screen. A chevron is something that
  moves and is coming (an enemy cluster); a mark is a place to go (a jammed segment, the Core). Sizes are
  screen pixels rather than tiles, because an arrow is a mark on the window and the window is what zooms.
- **Step back** (`EdgeTowards`) - an arrow that would land under a card is slid in along its own sight line
  until it clears the card. The sight line is what the arrow means, so it is the one thing the arrow never
  leaves: it never slides sideways to somewhere it would point at the wrong tile.
- **Fold** (`Marker.Fold`, `RadarLook.MergePixels`) - the judgement that makes a wave in one door one arrow
  counting sixteen rather than sixteen arrows: clustered by where the *arrow* would land, along the edge,
  not through the map. A full marker array folds rather than dropping, so the count stays true.
- **Danger frame** - the band the open screen wears while the Core is being hit out of view. It says "look
  back", not "look here" - the arrows are what say where - and it is timed off the same event window the
  health bar's flash fades over (`ThreatRadar.CoreHitAge`), so the card and the frame pulse together.

- **Shape word / shape mark** (`ShapeDef.Glyph`, `HudIcons`) - the two ways a shape is shown, and the rule
  for which is which. In a sentence the shape's *word* is spelled from the content table ("starved - no
  half-circle waiting on its lines"); where a shape is a *mark* - beside the stockpile, on a build tile -
  the HUD draws the silhouette the map draws, from the same Palette. It used to spell both with one
  character ("○", "◠"), and in a built player every one of those came out as a gap: the font the IMGUI text
  is drawn with has no such character. Same reasoning kept the HUD's shipped strings inside ASCII plus the
  middle dot - no en dash, no triangle - for the same reason.

## Reading machines

- **MachineReading** - the one module that says what a machine *means* to the player: the
  catalogue's words and numbers, the live card sentence, the facing note, the ports line, the
  badge shape and the work fraction. Strings and severity, never colour or layout - colour is the
  `Palette`'s question and layout is the HUD's. The HUD formats what this returns; the EditMode
  tests cross the same interface, so "what the card claims" and "what the table says" cannot drift.
- **Catalogue line / stats** (`Blurb`, `Stats`) - the build bar and inspect-card sentences about a
  kind in general, quoted straight from its content-table row (a turret's line quotes *its* shots
  per second, from the table, not a copy).
- **Live note** (`LiveNote`, `ReadingNote`) - the one line about *this* building right now, with a
  **severity** (`Neutral` / `Good` / `Warn`) instead of a colour; `Warn` means "the player should
  look, and a fix at the cursor exists" (starved turret, no outlet, nothing to push onto).
- **Role** (`MachineRole`) - what a building is filed under: transport, wall, mining, processing,
  routing, defence. The build bar's category colour and the card's header word hang off this one enum.
- **Landing** (`DrillLanding`, `PipeLanding`) - the cell a drill pushes its mined shape onto (the
  tile it faces), and the cell a pipe drops its carried shape onto (two tiles along its facing,
  jumping exactly one). One statement each: the tick delivers through them and the card describes
  them, so the picture cannot drift from the simulation - the same deal the port masks make.
- **Badge / work** (`Badge`, `Work`) - what a building draws on itself (its diet, its filter, the
  shape in hand) and the 0..1 fraction the view animates. Both dispatched on behaviour; both throw
  on one they have no answer for.

## The clock and the stream

- **Wave phase** (`WavePhase`) - what the wave clock is doing right now, in one word: `Running`,
  `Intermission` (a countdown is running), `Held` (the opening, before the player's first building arms
  its clock - no time passes there), `Over`. Readers branch on the phase instead of recomposing it from
  the `CurrentWave == 0` / `NextWave == 0` sentinels; `HasWaveToStart` is the one question the N key, the
  HUD's button and a test's run-to-end all ask.
- **Window** (in time) - a slice of the event stream at or after a given tick: `CountSince`,
  `TryLastWithin`, `DroppedSince`. The HUD's "last 1s" line and the refusal toast read these instead
  of hand-filtering the ring; drop honesty (`DroppedSince`, next to `Dropped` and `LastDropTick`)
  is stated once, where the ring's bound is.

## Input

- **Gesture** (`Gesture.Click/Aim/Release/Removal/Drag`) - the game's named input gestures, spelled
  once as `InputCommand`s: a click is the press-and-hold that latches inspect, a drag is press,
  carry, lift - the lift carrying the release edge, as the mouse's does. The shipped adapter
  (`BuildInput`) and the tests quote the same words, so "what a click is" has one spelling.
- **Camera keys** - the pan, the wheel and `C`, which puts the Core back in the middle of the view. They
  stay outside the gesture vocabulary on purpose: `CameraRig` reads its devices itself, because nothing
  replays a pan and routing it through the tick would make the camera step at 30 Hz for no reason. `C` is
  the locator's own companion - it says *that* something is out of view and where; the key says *go there*.

## Editing maps

- **Session** (`MapEdit.Session`) - one map-edit transaction: it records the asset for Undo once,
  before the first write, dirties it once, and closes the edit; a stroke surface (the painting
  window's grid, the Scene view's brush) holds one from press to release, so a drag is one undo
  entry. What differs between the surfaces - which button means "erase" - stays theirs; the
  record/dirty/array-identity protocol does not.
- **Structural edit** - an operation on the asset's own lists (add or remove a door or wave, add a
  group, move the Core, resize) that answers *whether anything changed*. The public arrays are
  serialization, not the interface: the copy-append-reassign dance and the door dedupe live here,
  with a test crossing each instead of a hand clicking it.
- **Vein** - a mineral in the ground, written in one place: an `Ore:` entry naming its cell, with that
  cell's `Terrain:` byte left plain. The conversion claims cells from the entry (`PatchClaims`,
  `StampTerrain`), so the array is not where a vein is added or taken away. Both halves of getting it
  wrong are named rather than silent: an array that says Ore with no entry beside it
  (`OreInTerrain` - a tile only a drill may stand on, which yields nothing when it does) and two
  entries for one cell (`DuplicateOre` - only the first one counts). `Tools/check-maps.py` asks the
  same questions of the file itself, without an Editor.
- **Canonical walk / drawn view** (`DrawableView`, `DrawnMap`) - the one enumeration of what a map
  draws: every in-map cell's ground and mineral (first-entry-wins, the conversion's own rule), the
  in-bounds doors each with the table position a wave group names them by, and the Core footprint.
  The painting fills, the preview's rebuild-test and `ContentHash` are all this one walk - the
  picture and the simulation's conversion cannot disagree about a cell because there is one walk,
  read twice.
- **Content hash** (`ContentHash`) - the checksum of exactly the canonical walk: the name and the
  starting stockpile are not pixels and are not counted. The preview's old hand-written digest -
  a fifth walk of the same data - is this one line now.
- **Grid frame** (`GridFrame`) - the map editor's tile↔pixel arithmetic (fit, zoom, clamp, the
  y-flip, hit-test, scroll-to-cell) as pure data, tested from EditMode off the IMGUI that hosts it;
  the window is a thin adapter over the frame it laid out this pass.

## The simulation's own doorway

- **The seam** (`SimWorld`) - chosen and stated: the world's public **fields** are the interface to
  its modules (`world.Belts.TrySpawnItem(...)`, `world.Waves.StartNextWave()`), and a **method** on
  the world exists only for what has no field to cross to - state behind the façade (`TryPlace`,
  `ShotsFired`) or a request that must land on a tick boundary (`RequestNextWave`,
  `RequestRestart`). The pass-throughs that only re-exposed a public field are gone.
- **Read that reads** - `ToCore`, `Validate`, `DrawableView` and `ContentHash` answer about the
  asset as authored and write nothing; repair lives where repair belongs (`OnValidate` on import,
  and each write being the whole of what it writes: a stroke lays down the ground it names and takes
  any mineral with it, and `PaintOre` writes the one place a vein lives). A query that resized the map
  under a repaint, with no Undo and no dirty, was the bug this rule retired.

## The run

- **Tick** - one step of the fixed-timestep simulation; the eight-step order inside it is
  `SimWorld`'s own affair and no caller sequences it.
- **Event stream** (`SimEvents`) - what the simulation reports having done; a fixed ring that
  overwrites its oldest, with `Dropped` and `LastDropTick` so a reader can tell when "nothing
  happened" is actually "I stopped seeing".
- **Twist** - the game's one designed tension: a turret is only *armed* when the right shape is
  waiting on its input belts, so the defence line and the production line are the same line.
