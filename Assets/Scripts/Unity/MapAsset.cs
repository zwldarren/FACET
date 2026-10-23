using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The Editor's carrier for a hand-authored map - a grid of ground, the veins in it, where the Core
    /// stands, where waves walk in and the wave table - painted in <c>FACET ▸ Map Editor</c> and stored
    /// as an asset.
    ///
    /// It is <em>not</em> what the simulation runs on: <see cref="ToCore"/> converts it to the plain-C#
    /// <see cref="MapDefinition"/>, the same conversion the content table makes, which is what lets
    /// <c>FACET.Core</c> keep its no-engine-references contract while maps are authored in the Editor.
    /// The runtime form is a row-major cell array and ore is a list of rectangles, so the two are
    /// compressed and expanded on the way across.
    ///
    /// <b>Reads do not write.</b> <see cref="ToCore"/>, <see cref="Validate"/>, <see cref="DrawableView"/>
    /// and <see cref="ContentHash"/> answer about the asset as authored and touch nothing; a validation
    /// run inside a repaint pass used to normalize the asset as a side effect - a mutation with no Undo
    /// entry or dirty flag. Repair belongs to <see cref="OnValidate"/> on import and to the writes
    /// below, and <see cref="PaintOre"/> writes the one place a vein lives.
    ///
    /// <b>Edits answer "changed".</b> Doors, waves, groups, the Core cell and the size are edited
    /// through the operations at the bottom of this file, each of which checks, writes and reports
    /// whether anything moved. They are the only writers, and the editor surfaces reach them through
    /// the editor's <c>MapEdit.Session</c>, which owns the Undo and dirty bookkeeping.
    /// </summary>
    [CreateAssetMenu(fileName = "Map", menuName = "FACET/Map", order = 2)]
    public sealed class MapAsset : ScriptableObject
    {
        /// <summary>One painted ore tile; the conversion joins a row's cells back into runtime rectangles.</summary>
        [Serializable]
        public struct OreCell
        {
            public Vector2Int Cell;
            [Tooltip("The mineral this tile yields. A drill mines it as the full shape of that mineral.")]
            public Quad Mineral;
        }

        /// <summary>One block of a wave, as the Inspector and the wave table edit it.</summary>
        [Serializable]
        public sealed class SpawnGroupRow
        {
            public EnemyKind Kind = EnemyKind.Runner;
            [Min(1)] public int Count = 6;
            [Tooltip("Position in this map's Spawn Points list, wrapped the way the map itself wraps it.")]
            public int SpawnPoint;
            public float FirstDelay;
            [Min(0.01f)] public float Interval = 2f;
            [Tooltip("The exact item this group is weak to, orientation included: the demand the player has " +
                "to build a line for. A group with no shape here can never be matched, and the map validator " +
                "says so.")]
            public ShapeField Weakness;
        }

        [Serializable]
        public sealed class WaveRow
        {
            [Tooltip("Seconds before this wave. It starts itself when it runs out; N starts it early. " +
                "The first wave wants the longest countdown on the table, and its clock starts with the " +
                "player's first building rather than with the run.")]
            public float Intermission = 30f;
            public SpawnGroupRow[] Groups = new SpawnGroupRow[0];

            /// <summary>The engine-free wave this row describes: its groups converted as in
            /// <see cref="MapAsset.ToCore"/>; an empty group entry contributes nothing.</summary>
            public WaveDefinition ToCore()
            {
                var groups = new List<SpawnGroup>();
                if (Groups != null)
                    for (int g = 0; g < Groups.Length; g++)
                    {
                        SpawnGroupRow group = Groups[g];
                        if (group == null) continue;

                        groups.Add(new SpawnGroup(group.Kind, group.Count, group.SpawnPoint,
                            group.FirstDelay, group.Interval, group.Weakness.ToCore()));
                    }

                return new WaveDefinition(Intermission, groups.ToArray());
            }

            /// <summary>Enemies in the wave: asked via <see cref="WaveDefinition.Total"/> rather than
            /// re-derived, so the table's count and the simulation's wave are one answer.</summary>
            public int Total => ToCore().Total;
        }

        [Header("Map")]
        public string Name = "New Map";
        [Min(1)] public int Width = 64;
        [Min(1)] public int Height = 40;
        [Tooltip("Complete shapes in the stockpile when the run starts - the bootstrap budget. One full " +
            "circle and one full square are worth the same: the currency is a complete shape.")]
        public int StartShapes = 80;

        [Header("Core")]
        [Tooltip("Lower-left tile of the Core's footprint. Leave at (-1,-1) to centre it on the map.")]
        public Vector2Int CoreCell = new Vector2Int(-1, -1);

        [Header("Ground")]
        [Tooltip("Row-major (y * Width + x), Width * Height long. Written by the paint operations; " +
            "a map with no obstacles at all may leave it all Ground. Never Ore: a vein is the Ore " +
            "list's entry, and the ground under one is plain.")]
        public TerrainKind[] Terrain = new TerrainKind[0];

        [Tooltip("Painted veins, one entry per cell: the only place a vein is written down. The first " +
            "entry for a cell is the one that counts, and the terrain array under it stays plain.")]
        public OreCell[] Ore = new OreCell[0];

        [Header("Doors and waves")]
        [Tooltip("Wave entry points, in the order a wave table names them by position.")]
        public Vector2Int[] SpawnPoints = new Vector2Int[0];
        public WaveRow[] Waves = new WaveRow[0];

        [Header("Progression")]
        [Tooltip("The operator machines this map hands the player. Progression is this set rather than a map " +
            "ordinal: the campaign's first map hands over the cutter and the rotator, and the map that " +
            "teaches combination hands over the stacker as well. " +
            "A locked machine cannot be built at all, and the wave generator never demands a shape this map's " +
            "minerals and unlocked operators cannot build.")]
        public Operators Unlocked = Operators.All;

        [Tooltip("The map's default seed. 0 - the shipped default - draws a FRESH seed every run, so two " +
            "runs of one map play out differently (the run's seed is shown in the HUD either way). A " +
            "non-zero value pins every run of this map to that seed, for a demo or a lesson that must " +
            "replay exactly.")]
        public int Seed = 0;

        [Tooltip("How many generated waves follow the authored table: 0 finishes the map when the table is " +
            "cleared (what every scripted map wants), a positive count hands over to the generator for that " +
            "many exam waves, and -1 is ENDLESS - the generator never runs out of demands.")]
        public int GeneratedWaves = 0;

        // ------------------------------------------------------------------ the tutorial

        /// <summary>One piece of a tutorial line's blueprint: the building, the tile and the facing the
        /// guide draws as a ghost. A cutter's second tile is not written here - the footprint rule derives
        /// it, the same rule the real placement uses.</summary>
        [Serializable]
        public struct TutorialGhostRow
        {
            [Tooltip("What stands on this tile. Belts are drawn as belt beds; machines as their bodies.")]
            public BuildKind Build;

            [Tooltip("The tile the ghost stands on. A two-tile machine's second tile follows its facing.")]
            public Vector2Int Cell;

            [Tooltip("The way the ghost faces: a belt's arrow, a machine's body and barrel.")]
            public Dir Facing;
        }

        /// <summary>One line of the map's tutorial, as the Inspector edits it.</summary>
        [Serializable]
        public sealed class TutorialTaskRow
        {
            [Tooltip("What has to be true of the run for this line to tick: a drill standing, a complete shape " +
                "really banked at the Core, a turret with a round in its chamber, a half anywhere, a turret " +
                "chambering a half.")]
            public TutorialStep Step = TutorialStep.PlaceDrill;

            [Tooltip("The line as the HUD prints it, one or two short sentences. The HUD's font has no en or " +
                "em dash - write a hyphen.")]
            [TextArea(2, 3)]
            public string Text = string.Empty;

            [Tooltip("The second sentence, shown once the line has been current for half a minute without " +
                "being ticked: what the rule actually asks for, said the long way round. Leave it empty for a " +
                "line nobody gets stuck on.")]
            [TextArea(2, 3)]
            public string Hint = string.Empty;

            [Tooltip("The one tile the guide points at, or leave it negative for a line with no one " +
                "right place to be.")]
            public Vector2Int Target = new Vector2Int(-1, -1);

            [Tooltip("The standard layout drawn as transparent ghosts on the map: every building and belt " +
                "the line wants, with its facing. Empty draws only the target marker.")]
            public TutorialGhostRow[] Ghost = new TutorialGhostRow[0];

            [Tooltip("The buildings this line hands over when it ticks. The last line hands over everything " +
                "else whether or not it lists them.")]
            public BuildKind[] Opens = new BuildKind[0];

            [Tooltip("Does ticking this line call the next wave? A map with a tutorial holds every wave until a " +
                "line releases it, so a wave arrives when the player has been taught what it asks for. A line " +
                "ticked while the previous wave is still being fought calls the wave after it - which is what " +
                "lets the cut a wave is weak to be taught during the wave before.")]
            public bool StartsWave;
        }

        [Header("Tutorial")]
        [Tooltip("The buildings the run opens with, before any line is ticked. Everything else waits for a line " +
            "that opens it, so a one-tile bar is the lesson's first sentence. Ignored when the task list below " +
            "is empty - a map with no lines has no tutorial.")]
        public BuildKind[] TutorialStarting = new BuildKind[0];

        [Tooltip("The lines this map teaches, in order. One at a time: each has to be really done before the " +
            "next is shown. Empty - the shipped default - is a map without a tutorial. Validate() refuses a " +
            "line with no text, a guide tile off the map, or a line about a building no earlier line hands " +
            "over - the last one is a run that waits forever for a wave nothing can release.")]
        public TutorialTaskRow[] TutorialTasks = new TutorialTaskRow[0];

        /// <summary>The tile a problem is about when it is about the map as a whole, spelled as
        /// <see cref="MapValidation"/> spells it.</summary>
        private static readonly Int2 NoCell = new Int2(-1, -1);

        /// <summary>Whether a cell is one the asset holds - the one answer to "is this on the map",
        /// shared by every method here and the painting surfaces.</summary>
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>The ground the array states at a cell, with an out-of-array cell reading as plain
        /// ground. A vein is not read here - it is <see cref="Ore"/>'s entry, and an ore value in the
        /// array is a <see cref="MapValidation"/> problem.</summary>
        public TerrainKind TerrainAt(int x, int y)
        {
            if (!InBounds(x, y) || Terrain == null) return TerrainKind.Ground;

            int i = y * Width + x;
            return i < Terrain.Length ? Terrain[i] : TerrainKind.Ground;
        }

        /// <summary>Paint one cell's ground, kept in range because a dragged pointer can leave the grid.</summary>
        public void Paint(int x, int y, TerrainKind kind)
        {
            if (!InBounds(x, y)) return;
            EnsureTerrainSize();

            Terrain[y * Width + x] = kind;

            // Non-ore ground takes the mineral with it, so a rock over a patch leaves nothing behind.
            // Ore is not painted through here: PaintOre is the one writer of a vein.
            if (kind != TerrainKind.Ore) RemoveOreAt(new Vector2Int(x, y));
        }

        /// <summary>Paint one ore tile: the vein is the <see cref="Ore"/> entry and the ground under it
        /// is plain, so there is no second half to leave behind. Inside a batch
        /// (<see cref="BeginBatch"/>) the append lands on the buffer, written back once at
        /// <see cref="EndBatch"/>.</summary>
        public void PaintOre(int x, int y, Quad mineral)
        {
            if (!InBounds(x, y) || mineral == Quad.Empty) return;

            var cell = new Vector2Int(x, y);

            // Plain ground also clears any earlier mineral here: repainting changes it, never stacks one.
            Paint(x, y, TerrainKind.Ground);

            if (_batch != null)
            {
                _batch.Add(new OreCell { Cell = cell, Mineral = mineral });
                return;
            }

            var grown = new List<OreCell>(Ore) { new OreCell { Cell = cell, Mineral = mineral } };
            Ore = grown.ToArray();
        }

        /// <summary>Erase whatever is at a cell: the ground goes back to plain and any mineral with it.</summary>
        public void Erase(int x, int y)
        {
            if (!InBounds(x, y)) return;

            Paint(x, y, TerrainKind.Ground);
            RemoveOreAt(new Vector2Int(x, y));
        }

        /// <summary>The cell the Core stands on, or the centred one when the map names none - it is
        /// <see cref="MapDefinition.CentredCore"/> applied here, so the editor's Core and the runtime's
        /// cannot drift.</summary>
        public Vector2Int CoreFootprint
        {
            get
            {
                if (CoreCell.x >= 0 && CoreCell.y >= 0) return CoreCell;

                Int2 centred = MapDefinition.CentredCore(Width, Height);
                return new Vector2Int(centred.X, centred.Y);
            }
        }

        /// <summary>Resize the map. The terrain is re-laid row by row so the map does not shear; tiles
        /// that still fit are carried over, and ore past an edge is dropped.
        ///
        /// Doors and the Core survive even off-map: they are placement, not painted tiles, and a resize
        /// is a change of canvas, not of placement. <see cref="Validate"/> reports an off-edge door or
        /// Core rather than this silently moving it.</summary>
        public void Resize(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);

            var moved = new TerrainKind[width * height];
            for (int y = 0; y < Mathf.Min(height, Height); y++)
            {
                for (int x = 0; x < Mathf.Min(width, Width); x++)
                    moved[y * width + x] = TerrainAt(x, y);
            }

            var keptOre = new List<OreCell>();
            if (Ore != null)
                for (int i = 0; i < Ore.Length; i++)
                {
                    Vector2Int cell = Ore[i].Cell;
                    if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) continue;

                    keptOre.Add(Ore[i]);
                }

            Width = width;
            Height = height;
            Terrain = moved;
            Ore = keptOre.ToArray();
        }

        /// <summary>The engine-free map this asset describes; a read that writes nothing. Sizes are
        /// floored and a half-edited asset converts honestly rather than being repaired.
        ///
        /// An all-ground map hands over a <see langword="null"/> terrain array rather than plain cells,
        /// which is what "all ground" already means. Painted ore cells join back into row-run
        /// rectangles, the runtime's patch form.</summary>
        public MapDefinition ToCore()
        {
            int width = Mathf.Max(1, Width);
            int height = Mathf.Max(1, Height);

            return new MapDefinition(Name, width, height, StartShapes,
                patches: OrePatches(width, height),
                spawnPoints: SpawnPointArray(),
                waves: WaveDefinitions(),
                terrain: TerrainArray(width, height),
                coreCell: CoreCell.x < 0 || CoreCell.y < 0 ? (Int2?)null : new Int2(CoreCell.x, CoreCell.y),
                unlocked: Unlocked,
                seed: Seed,
                generatedWaves: GeneratedWaves,
                tutorial: TutorialPlan());
        }

        /// <summary>Everything wrong with this map as the simulation would read it; pure, because it runs
        /// in a repaint pass and must not mutate the asset.
        ///
        /// The duplicate-ore rule lives here, not in <see cref="MapValidation"/>: the conversion keeps a
        /// cell's first entry and drops the rest, so a duplicate is invisible once the runtime form
        /// exists - it is a fact about the list as written down.</summary>
        public List<MapProblem> Validate()
        {
            List<MapProblem> problems = MapValidation.Validate(ToCore());
            var seen = new HashSet<Vector2Int>();

            if (Ore != null)
                for (int i = 0; i < Ore.Length; i++)
                {
                    Vector2Int cell = Ore[i].Cell;
                    if (seen.Add(cell)) continue;

                    problems.Add(new MapProblem(MapProblemKind.DuplicateOre, new Int2(cell.x, cell.y),
                        Name + ": the ore list names (" + cell.x + "," + cell.y + ") more than once, and " +
                        "only the first entry counts - the mineral named after it is ignored"));
                }

            CheckTutorial(problems);
            return problems;
        }

        /// <summary>
        /// The tutorial's own authoring rules: what a lesson must say, and what it must be able to reach.
        /// They live here beside the duplicate-ore rule for the same reason - once the runtime form exists, a
        /// row is a line in a list and nothing remembers which fields were left blank. One of them is worse
        /// than invisible: a line whose building no earlier line hands over can never tick, so the lesson never
        /// completes and every wave it gates waits forever - a run that hangs with nothing said.
        /// </summary>
        private void CheckTutorial(List<MapProblem> into)
        {
            TutorialTaskRow[] rows = TutorialTasks ?? new TutorialTaskRow[0];
            int width = Mathf.Max(1, Width);
            int height = Mathf.Max(1, Height);

            // What the run holds while each row is current: the starting set plus every earlier row's Opens.
            // A row's own Opens pay out when it ticks, so they are added after the row has been judged -
            // otherwise a line could open the building its own rule is about, which is a lesson that starts
            // finished.
            var opened = new List<BuildKind>(TutorialStarting ?? new BuildKind[0]);

            for (int i = 0; i < rows.Length; i++)
            {
                TutorialTaskRow row = rows[i];
                if (row == null)
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialLineWithoutText, NoCell,
                        Name + ": tutorial line " + (i + 1) + " is a missing row, so the HUD prints a line " +
                        "the player is told to act on"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.Text))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialLineWithoutText, NoCell,
                        Name + ": tutorial line " + (i + 1) + " has no text, so the HUD prints a blank line " +
                        "the player is told to act on"));
                }

                // The card that prints a line is a fixed corner of micro type: the HUD shrinks to its floor
                // and past the floor the sentence spills out of the card onto the map. Same rule for the hint,
                // which the card pins under the line once the player has been stuck a while.
                if (LongerThanCard(row.Text))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialLineTooLong, NoCell,
                        Name + ": tutorial line " + (i + 1) + " runs " + row.Text.Length + " characters - " +
                        "the task card holds about " + TutorialTextBudget + " at its smallest type, so the " +
                        "line would spill past the card onto the map"));
                }

                if (LongerThanCard(row.Hint))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialLineTooLong, NoCell,
                        Name + ": tutorial line " + (i + 1) + "'s hint runs " + row.Hint.Length +
                        " characters - the task card holds about " + TutorialTextBudget + " at its smallest " +
                        "type, so the hint would spill past the card onto the map"));
                }

                // A negative target is the authored "no place to point" - the Inspector shows (-1,-1) - so
                // only the top of the grid is a fault.
                if (row.Target.x >= width || row.Target.y >= height)
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialTargetOffMap,
                        new Int2(row.Target.x, row.Target.y),
                        Name + ": tutorial line " + (i + 1) + " points at (" + row.Target.x + "," +
                        row.Target.y + "), which is outside the " + width + "x" + height + " map"));
                }

                TutorialGhostRow[] ghost = row.Ghost ?? new TutorialGhostRow[0];
                for (int g = 0; g < ghost.Length; g++)
                {
                    Vector2Int cell = ghost[g].Cell;
                    if (cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height) continue;

                    into.Add(new MapProblem(MapProblemKind.TutorialTargetOffMap,
                        new Int2(cell.x, cell.y),
                        Name + ": tutorial line " + (i + 1) + " draws a " + ghost[g].Build + " ghost at (" +
                        cell.x + "," + cell.y + "), which is outside the " + width + "x" + height + " map"));
                }

                BuildKind need;

                if (!TutorialScript.TryRequiredBuilding(row.Step, out need))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialStepUnknown, NoCell,
                        Name + ": tutorial line " + (i + 1) + " asks for step " + (int)row.Step +
                        ", which is not a lesson this build knows - the line can never tick, so every wave " +
                        "it gates waits forever"));
                }
                else if (!MapAllows(need))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialStepUnreachable, NoCell,
                        Name + ": tutorial line " + (i + 1) + " is about a " + need + ", and this map's " +
                        "operator set never unlocks one - the line can never tick, so every wave it gates " +
                        "waits forever"));
                }
                else if (!opened.Contains(need))
                {
                    into.Add(new MapProblem(MapProblemKind.TutorialStepUnreachable, NoCell,
                        Name + ": tutorial line " + (i + 1) + " is about a " + need + ", and no earlier " +
                        "line hands one over - the line can never tick, so every wave it gates waits forever"));
                }

                if (row.Opens != null) opened.AddRange(row.Opens);
            }
        }

        /// <summary>The characters a task line or its hint may run to. The task card's column is 332 design
        /// units of micro type whose shrink floor is 8 pt (HudView AutoFit) - about 88 average characters -
        /// and past the floor the text overflows the card. Tools/check-maps.py holds the same number, because
        /// a hand edit of the asset is judged by the same rules as the painting window.</summary>
        private const int TutorialTextBudget = 88;

        private static bool LongerThanCard(string line) => line != null && line.Length > TutorialTextBudget;

        /// <summary>Does the map's own operator set admit a building? Asked of the shipped table, which is
        /// what an asset validated in the Editor is played with; a kind the table does not define reads as
        /// buildable, because a partial table is legal and this rule is about the tutorial, not the table.</summary>
        private bool MapAllows(BuildKind kind)
            => !ContentDatabase.Default.TryMachine(kind, out MachineDef machine) ||
               (machine.Requires & ~Unlocked) == 0;

        /// <summary>The one canonical walk of what this map <em>draws</em> - ground, mineral, in-bounds
        /// doors with their table position, and the Core footprint - normalized as <see cref="ToCore"/>
        /// does, so the preview and the run are projections of one enumeration. A read that copies into
        /// a fresh <see cref="DrawnMap"/> and writes nothing back.</summary>
        public DrawnMap DrawableView()
        {
            int width = Mathf.Max(1, Width);
            int height = Mathf.Max(1, Height);

            var ground = new TerrainKind[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    ground[y * width + x] = TerrainAt(x, y);

            // The per-cell mineral, claimed as the conversion claims it: the FIRST entry per in-map
            // cell counts, so an Inspector-handwritten list reads as it does in OrePatches.
            var mineral = new Quad[width * height];
            var claimed = new bool[width * height];
            if (Ore != null)
                for (int i = 0; i < Ore.Length; i++)
                {
                    OreCell cell = Ore[i];
                    if (cell.Cell.x < 0 || cell.Cell.y < 0 || cell.Cell.x >= width || cell.Cell.y >= height)
                        continue;

                    int at = cell.Cell.y * width + cell.Cell.x;
                    if (claimed[at]) continue;

                    claimed[at] = true;
                    mineral[at] = cell.Mineral;
                }

            var doorCells = new List<Vector2Int>();
            var doorPositions = new List<int>();
            if (SpawnPoints != null)
                for (int i = 0; i < SpawnPoints.Length; i++)
                {
                    Vector2Int door = SpawnPoints[i];
                    if (door.x < 0 || door.y < 0 || door.x >= width || door.y >= height) continue;

                    doorCells.Add(door);
                    doorPositions.Add(i);
                }

            return new DrawnMap(width, height, ground, mineral,
                doorCells.ToArray(), doorPositions.ToArray(), CoreFootprint);
        }

        /// <summary>A digest of everything the map draws, so "has this map changed?" is one comparison.
        /// It is the <see cref="DrawableView"/> walk plus the door each wave group names; it leaves out
        /// what nothing draws - the name and the starting stockpile.
        ///
        /// Deliberately not a revision counter: the writers are many, and a counter only catches the
        /// ones that remember to count.</summary>
        public int ContentHash()
        {
            DrawnMap drawn = DrawableView();

            unchecked
            {
                int h = 17;
                h = h * 31 + drawn.Width;
                h = h * 31 + drawn.Height;
                h = h * 31 + drawn.Core.x;
                h = h * 31 + drawn.Core.y;

                for (int i = 0; i < drawn.Ground.Length; i++) h = h * 31 + (int)drawn.Ground[i];
                for (int i = 0; i < drawn.Mineral.Length; i++) h = h * 31 + (int)drawn.Mineral[i];

                for (int i = 0; i < drawn.DoorCells.Length; i++)
                {
                    h = h * 31 + drawn.DoorCells[i].x;
                    h = h * 31 + drawn.DoorCells[i].y;
                    h = h * 31 + drawn.DoorPositions[i];
                }

                if (Waves != null)
                    for (int w = 0; w < Waves.Length; w++)
                    {
                        WaveRow row = Waves[w];
                        if (row == null || row.Groups == null) { h = h * 31; continue; }

                        for (int g = 0; g < row.Groups.Length; g++)
                            h = h * 31 + (row.Groups[g] != null ? row.Groups[g].SpawnPoint : -1);
                    }

                return h;
            }
        }

        // ------------------------------------------------------------- the structural edits
        //
        // One operation per edit, each answering whether anything changed. These are the only writers
        // to the arrays above that outside callers drive: each holds the copy-append-reassign dance and
        // the bounds/dedupe rules so callers cannot disagree. The editor's MapEdit.Session wraps them
        // with the Undo record and the dirty flag.

        /// <summary>Resize, floored to one tile, answering whether the canvas actually moved.</summary>
        public bool SetSize(int width, int height)
        {
            int w = Mathf.Max(1, width);
            int h = Mathf.Max(1, height);
            if (w == Width && h == Height) return false;

            Resize(w, h);
            return true;
        }

        /// <summary>Move the Core's footprint, answering whether it moved.</summary>
        public bool SetCore(int x, int y)
        {
            if (CoreCell.x == x && CoreCell.y == y) return false;

            CoreCell = new Vector2Int(x, y);
            return true;
        }

        /// <summary>The Core back on its centred tile (<see cref="CoreFootprint"/>), answering whether
        /// it had left it.</summary>
        public bool CentreCore() => SetCore(-1, -1);

        /// <summary>Put an entry point on a tile unless it is off-map or already has a door, since two
        /// doors at one tile are two names for one place.</summary>
        public bool TryAddDoor(int x, int y)
        {
            if (!InBounds(x, y)) return false;

            var cell = new Vector2Int(x, y);
            Vector2Int[] doors = SpawnPoints ?? new Vector2Int[0];
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] == cell) return false;

            var points = new List<Vector2Int>(doors) { cell };
            SpawnPoints = points.ToArray();
            return true;
        }

        /// <summary>Remove the tile's entry point if it has one; an erase over open ground answers
        /// <see langword="false"/> and is not an edit.</summary>
        public bool TryRemoveDoor(int x, int y)
        {
            var cell = new Vector2Int(x, y);
            Vector2Int[] doors = SpawnPoints ?? new Vector2Int[0];

            var kept = new List<Vector2Int>(doors.Length);
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] != cell) kept.Add(doors[i]);

            if (kept.Count == doors.Length) return false;

            SpawnPoints = kept.ToArray();
            return true;
        }

        /// <summary>Append an empty wave; validation reports it until the designer fills it.</summary>
        public bool AddWave()
        {
            WaveRow[] rows = Waves ?? new WaveRow[0];
            var grown = new List<WaveRow>(rows) { new WaveRow() };
            Waves = grown.ToArray();
            return true;
        }

        /// <summary>Remove the wave at a table position, answering whether there was one.</summary>
        public bool RemoveWave(int index)
        {
            WaveRow[] rows = Waves;
            if (rows == null || index < 0 || index >= rows.Length) return false;

            var kept = new List<WaveRow>(rows);
            kept.RemoveAt(index);
            Waves = kept.ToArray();
            return true;
        }

        /// <summary>Append a group with the row's own defaults to the wave at a table position, so the
        /// runtime can already spawn it. A position with no row gets nothing.</summary>
        public bool AddWaveGroup(int waveIndex)
        {
            if (waveIndex < 0 || Waves == null || waveIndex >= Waves.Length) return false;

            WaveRow row = Waves[waveIndex];
            if (row == null) return false;

            SpawnGroupRow[] groups = row.Groups ?? new SpawnGroupRow[0];
            var grown = new List<SpawnGroupRow>(groups) { new SpawnGroupRow() };
            row.Groups = grown.ToArray();
            return true;
        }

        /// <summary>Remove the group at a position in a wave's table, answering whether there was one.</summary>
        public bool RemoveWaveGroup(int waveIndex, int groupIndex)
        {
            if (waveIndex < 0 || Waves == null || waveIndex >= Waves.Length) return false;

            WaveRow row = Waves[waveIndex];
            if (row == null || row.Groups == null || groupIndex < 0 || groupIndex >= row.Groups.Length)
                return false;

            var kept = new List<SpawnGroupRow>(row.Groups);
            kept.RemoveAt(groupIndex);
            row.Groups = kept.ToArray();
            return true;
        }

        /// <summary>Keep the terrain array exactly Width * Height, whatever the Inspector did to the
        /// size fields - a stale length would reach the simulation as silently missing ground. Write-side
        /// only; the read path never calls it.</summary>
        private void EnsureTerrainSize()
        {
            if (Terrain != null && Terrain.Length == Width * Height) return;
            Resize(Width, Height);
        }

        /// <summary>Bring the asset back to a state a conversion can trust: size at least one tile and a
        /// matching terrain array. Never runs inside a query, which answers as the asset is.</summary>
        private void Normalize()
        {
            Width = Mathf.Max(1, Width);
            Height = Mathf.Max(1, Height);
            if (Terrain == null) Terrain = new TerrainKind[0];
            if (Ore == null) Ore = new OreCell[0];
            if (SpawnPoints == null) SpawnPoints = new Vector2Int[0];
            if (Waves == null) Waves = new WaveRow[0];
            EnsureTerrainSize();
        }

        private void OnValidate() => Normalize();

        /// <summary>Per-cell ore writes churn this buffer while a batch is open, and
        /// <see cref="EndBatch"/> writes it back once; it holds the same entries in the same order the
        /// array did.</summary>
        private List<OreCell> _batch;

        /// <summary>Open a batch: ore writes run on a private buffer that <see cref="EndBatch"/> hands
        /// back once, so a whole-map fill does not rebuild the list per cell. Nested calls are ignored -
        /// nothing inside may resize the map, so <see cref="EnsureTerrainSize"/>'s <see cref="Resize"/>
        /// cannot land between the snapshot and the writes.</summary>
        internal void BeginBatch()
        {
            if (_batch != null) return;

            EnsureTerrainSize();

            _batch = new List<OreCell>(Ore.Length);
            if (Ore != null) _batch.AddRange(Ore);
        }

        /// <summary>Hand the batch's buffer back as the ore array; a double close writes nothing.</summary>
        internal void EndBatch()
        {
            if (_batch == null) return;

            Ore = _batch.ToArray();
            _batch = null;
        }

        /// <summary>Drop every entry naming a cell from the ore list, or from the batch buffer while one
        /// is open, keeping the remaining entries in order.</summary>
        private void RemoveOreAt(Vector2Int cell)
        {
            if (_batch != null)
            {
                int keep = 0;
                for (int i = 0; i < _batch.Count; i++)
                    if (_batch[i].Cell != cell) _batch[keep++] = _batch[i];

                _batch.RemoveRange(keep, _batch.Count - keep);
                return;
            }

            var kept = new List<OreCell>(Ore.Length);
            for (int i = 0; i < Ore.Length; i++)
                if (Ore[i].Cell != cell) kept.Add(Ore[i]);

            Ore = kept.ToArray();
        }

        // ------------------------------------------------------------------ the conversion, read-only
        //
        // Every helper below answers about the asset as authored, at the given (floored) size, and
        // writes no field: none of them normalizes the asset on the way through.

        private TerrainKind[] TerrainArray(int width, int height)
        {
            var laid = new TerrainKind[width * height];
            bool plain = true;

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    TerrainKind kind = TerrainAt(x, y);
                    laid[y * width + x] = kind;
                    if (kind != TerrainKind.Ground) plain = false;
                }

            // All-ground hands over nothing rather than a grid of plain: that is what "all ground" means.
            return plain ? null : laid;
        }

        /// <summary>Painted ore as row-run rectangles in row-major order: one pass into a cell-indexed
        /// lookup, then one pass over the grid, since scanning the list per tile would be a scan inside
        /// a scan. The first entry for a cell counts, as in <see cref="DrawableView"/>.</summary>
        private ShapePatch[] OrePatches(int width, int height)
        {
            var minerals = new Quad[width * height];
            var claimed = new bool[width * height];

            if (Ore != null)
                for (int i = 0; i < Ore.Length; i++)
                {
                    OreCell ore = Ore[i];
                    if (ore.Cell.x < 0 || ore.Cell.y < 0 || ore.Cell.x >= width || ore.Cell.y >= height)
                        continue;

                    int at = ore.Cell.y * width + ore.Cell.x;
                    if (claimed[at]) continue;

                    claimed[at] = true;
                    minerals[at] = ore.Mineral;
                }

            var patches = new List<ShapePatch>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    Quad mineral = minerals[y * width + x];
                    if (mineral == Quad.Empty) { x++; continue; }

                    int start = x;
                    while (x < width && minerals[y * width + x] == mineral) x++;
                    patches.Add(new ShapePatch(new Int2(start, y), x - start, 1, mineral));
                }
            }

            return patches.ToArray();
        }

        private Int2[] SpawnPointArray()
        {
            Vector2Int[] doors = SpawnPoints ?? new Vector2Int[0];
            var points = new Int2[doors.Length];
            for (int i = 0; i < doors.Length; i++)
                points[i] = new Int2(doors[i].x, doors[i].y);

            return points;
        }

        /// <summary>Each row converted through its own <see cref="MapAsset.WaveRow.ToCore"/>, so there
        /// is one statement of it; a code-null row gets the empty wave it always has.</summary>
        private WaveDefinition[] WaveDefinitions()
        {
            WaveRow[] rows = Waves ?? new WaveRow[0];
            var waves = new WaveDefinition[rows.Length];
            for (int w = 0; w < rows.Length; w++)
                waves[w] = rows[w] == null ? new WaveDefinition(0f) : rows[w].ToCore();

            return waves;
        }

        /// <summary>The tutorial this map teaches, or <see langword="null"/> for the map that teaches
        /// nothing - an empty task list is the whole answer, so there is no second "has a tutorial" flag to
        /// fall out of step with it. A code-null row becomes a line that can never tick rather than a hole in
        /// the list, since the list's length is what the HUD counts against.
        ///
        /// The guide target is negative-or-not rather than a sentinel: the Inspector shows (-1,-1) for "no
        /// place to point", and a map is never authored at a negative tile.</summary>
        private TutorialScript TutorialPlan()
        {
            TutorialTaskRow[] rows = TutorialTasks ?? new TutorialTaskRow[0];
            if (rows.Length == 0) return null;

            var tasks = new TutorialTask[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                TutorialTaskRow row = rows[i];
                if (row == null)
                {
                    // A hole in the list is not a line: the step no rule answers can never tick, so a null
                    // row stops the lesson on that line instead of ticking itself - which is what
                    // PlaceDrill would do the moment any drill stands. The text says so rather than printing a
                    // blank the player is told to act on; Validate() reports it as well.
                    tasks[i] = new TutorialTask(TutorialScript.NoStep, "(task line missing)", null, null,
                        false);
                    continue;
                }

                bool placed = row.Target.x >= 0 && row.Target.y >= 0;
                tasks[i] = new TutorialTask(row.Step, row.Text,
                    placed ? new Int2(row.Target.x, row.Target.y) : (Int2?)null,
                    row.Opens, row.StartsWave, row.Hint, GhostOf(row.Ghost));
            }

            return new TutorialScript(TutorialStarting, tasks);
        }

        /// <summary>Convert a row's authored ghosts to the engine-free blueprint, preserving order: the
        /// first entry is the line's primary building, which is what the bar highlights first. A null list
        /// is no blueprint rather than a null array, the same normalization every other row field gets.</summary>
        private static TutorialGhost[] GhostOf(TutorialGhostRow[] rows)
        {
            if (rows == null || rows.Length == 0) return new TutorialGhost[0];

            var ghost = new TutorialGhost[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                ghost[i] = new TutorialGhost(rows[i].Build,
                    new Int2(rows[i].Cell.x, rows[i].Cell.y), rows[i].Facing);

            return ghost;
        }
    }
}
