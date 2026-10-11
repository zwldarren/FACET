using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The Editor's carrier for a hand-authored map: a grid of ground, the veins in it, where the Core
    /// stands, where waves walk in and the wave table - painted in <c>FACET ▸ Map Editor</c> and stored
    /// as an asset.
    ///
    /// It is <em>not</em> what the simulation runs on. <see cref="ToCore"/> converts it to the plain-C#
    /// <see cref="MapDefinition"/> - the same conversion the content table makes, for the same reason:
    /// it is what lets <c>FACET.Core</c> keep its no-engine-references contract while maps are authored
    /// in the Editor, and what keeps a run reproducible from its own state rather than from an asset the
    /// simulation would have to load.
    ///
    /// The one thing this type owes the conversion is the shape of the data. The runtime form is a
    /// row-major cell array because a painted map already knows every cell, and ore is a list of
    /// rectangles because that is what the simulation's patch rule takes - so the two are compressed and
    /// expanded on the way across rather than either side being bent to the other's shape.
    ///
    /// Two rules say who may write this type and who may only read it.
    ///
    /// <b>Reads do not write.</b> <see cref="ToCore"/>, <see cref="Validate"/>, <see cref="DrawableView"/>
    /// and <see cref="ContentHash"/> answer about the asset as authored; none of them resizes it, lays
    /// out its terrain array or otherwise touches it. A validation run inside a repaint pass used to
    /// normalize the asset as a side effect - a mutation with no Undo entry and no dirty flag, which is
    /// an interface fact absent from its signature. Repair happens where repair belongs:
    /// <see cref="OnValidate"/> normalizes on import, and every write below keeps the arrays it writes in
    /// step as part of writing.
    ///
    /// <b>Edits answer "changed".</b> The lists - doors, waves, groups, the Core cell, the size - are
    /// edited through the operations at the bottom of this file, each of which checks, writes, and says
    /// whether anything moved. They are the only writers, because a list is an array and every caller
    /// that appended by hand had to remember the copy-append-reassign dance, the bounds and the door
    /// dedupe on its own. The editor surfaces reach these through the editor's <c>MapEdit.Session</c>,
    /// which owns the Undo and dirty bookkeeping around them; the arrays stay public only because Unity
    /// serializes them, and the Inspector is the one caller left handwriting them.
    /// </summary>
    [CreateAssetMenu(fileName = "Map", menuName = "FACET/Map", order = 2)]
    public sealed class MapAsset : ScriptableObject
    {
        /// <summary>One painted ore tile. Ore is stored per cell because that is how it is painted; the
        /// conversion joins a row's cells back into the rectangles the runtime form wants.</summary>
        [Serializable]
        public struct OreCell
        {
            public Vector2Int Cell;
            public ShapeType Shape;
        }

        /// <summary>One block of a wave, as the Inspector and the wave table edit it.</summary>
        [Serializable]
        public sealed class SpawnGroupRow
        {
            public EnemyKind Kind = EnemyKind.Spike;
            [Min(1)] public int Count = 6;
            [Tooltip("Position in this map's Spawn Points list, wrapped the way the map itself wraps it.")]
            public int SpawnPoint;
            public float FirstDelay;
            [Min(0.01f)] public float Interval = 2f;
        }

        [Serializable]
        public sealed class WaveRow
        {
            [Tooltip("Seconds before this wave. Unread for the first wave, which waits for the player.")]
            public float Intermission = 30f;
            public SpawnGroupRow[] Groups = new SpawnGroupRow[0];

            /// <summary>The engine-free wave this row describes: its groups converted, the same way the
            /// whole asset crosses in <see cref="MapAsset.ToCore"/>. An entry the Inspector can leave empty
            /// contributes nothing, exactly as the conversion has always skipped it.</summary>
            public WaveDefinition ToCore()
            {
                var groups = new List<SpawnGroup>();
                if (Groups != null)
                    for (int g = 0; g < Groups.Length; g++)
                    {
                        SpawnGroupRow group = Groups[g];
                        if (group == null) continue;

                        groups.Add(new SpawnGroup(group.Kind, group.Count, group.SpawnPoint,
                            group.FirstDelay, group.Interval));
                    }

                return new WaveDefinition(Intermission, groups.ToArray());
            }

            /// <summary>How many enemies the wave holds: the groups' counts added, with an entry the
            /// Inspector can leave empty contributing nothing. Asked, not re-derived - this converts
            /// the row and reads <see cref="WaveDefinition.Total"/> off it, so the count the wave table
            /// shows and the wave the simulation runs are one answer.</summary>
            public int Total => ToCore().Total;
        }

        [Header("Map")]
        public string Name = "New Map";
        [Min(1)] public int Width = 64;
        [Min(1)] public int Height = 40;
        [Tooltip("Circles in the stockpile when the run starts - the bootstrap budget.")]
        public int StartCircles = 80;

        [Header("Core")]
        [Tooltip("Lower-left tile of the Core's footprint. Leave at (-1,-1) to centre it on the map.")]
        public Vector2Int CoreCell = new Vector2Int(-1, -1);

        [Header("Ground")]
        [Tooltip("Row-major (y * Width + x), Width * Height long. Written by the paint operations; " +
            "a map with no obstacles at all may leave it all Ground.")]
        public TerrainKind[] Terrain = new TerrainKind[0];

        [Tooltip("Painted veins. A cell here is painted ground too - the conversion writes the ore into " +
            "the terrain array and the mineral into the patch list, so the two cannot disagree.")]
        public OreCell[] Ore = new OreCell[0];

        [Header("Doors and waves")]
        [Tooltip("Wave entry points, in the order a wave table names them by position.")]
        public Vector2Int[] SpawnPoints = new Vector2Int[0];
        public WaveRow[] Waves = new WaveRow[0];

        /// <summary>Whether a cell is one the asset holds. The one answer to "is this on the map": the
        /// ground half of it would otherwise be restated by every method here, by the painting surfaces,
        /// and by the Scene view's editor - and a surface that clips half a tile off one edge is a
        /// disagreement no test would think to look for.</summary>
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>The ground at a cell, with a cell outside the array reading as plain ground - so a
        /// half-edited asset is still paintable rather than throwing in the window's draw loop.</summary>
        public TerrainKind TerrainAt(int x, int y)
        {
            if (!InBounds(x, y) || Terrain == null) return TerrainKind.Ground;

            int i = y * Width + x;
            return i < Terrain.Length ? Terrain[i] : TerrainKind.Ground;
        }

        /// <summary>Paint one cell's ground. Kept in range rather than trusted, because the window
        /// paints with a dragged pointer that can leave the grid.</summary>
        public void Paint(int x, int y, TerrainKind kind)
        {
            if (!InBounds(x, y)) return;
            EnsureTerrainSize();

            Terrain[y * Width + x] = kind;

            // Ground that stops being ore stops yielding: a vein cell keeps its mineral only while it
            // is a vein, which is what stops a rock painted over a patch from leaving a mineral behind
            // that the map does not show.
            if (kind != TerrainKind.Ore) RemoveOreAt(new Vector2Int(x, y));
        }

        /// <summary>Paint one ore tile with its mineral: the ground becomes ore and the mineral is
        /// recorded in the same call, so the two halves of a vein cannot drift apart. Inside a batch
        /// (<see cref="BeginBatch"/>) the append lands on the batch's buffer instead of the array,
        /// which is written back once at <see cref="EndBatch"/>.</summary>
        public void PaintOre(int x, int y, ShapeType shape)
        {
            if (!InBounds(x, y) || !shape.IsShape()) return;

            Paint(x, y, TerrainKind.Ore);
            var cell = new Vector2Int(x, y);
            RemoveOreAt(cell);

            if (_batch != null)
            {
                _batch.Add(new OreCell { Cell = cell, Shape = shape });
                return;
            }

            var grown = new List<OreCell>(Ore) { new OreCell { Cell = cell, Shape = shape } };
            Ore = grown.ToArray();
        }

        /// <summary>Erase whatever is at a cell: the ground goes back to plain and any mineral with it.</summary>
        public void Erase(int x, int y)
        {
            if (!InBounds(x, y)) return;

            Paint(x, y, TerrainKind.Ground);
            RemoveOreAt(new Vector2Int(x, y));
        }

        /// <summary>
        /// Where the Core's footprint actually stands: the cell the map names, or the centred one when it
        /// names none. It is <see cref="MapDefinition.CentredCore"/> applied to this asset's size, so the
        /// Core an editor draws while a map is being painted is the Core the runtime will build - the same
        /// rule, read twice, rather than an editor-side copy that can drift from it.
        /// </summary>
        public Vector2Int CoreFootprint
        {
            get
            {
                if (CoreCell.x >= 0 && CoreCell.y >= 0) return CoreCell;

                Int2 centred = MapDefinition.CentredCore(Width, Height);
                return new Vector2Int(centred.X, centred.Y);
            }
        }

        /// <summary>
        /// Resize the map. The terrain array is re-laid row by row rather than copied flat, so a resize
        /// does not shear the map - and what the new edges cut is only the ground: every painted tile
        /// that still fits is carried over, and ore past an edge is dropped, with the ground it was
        /// painted on and the mineral under it.
        ///
        /// Doors and the Core survive whatever the new size is, even when they land off the map: they
        /// are facts about the map rather than painted tiles, and a resize is a change of canvas, not a
        /// change of placement, so moving or dropping them silently would say something the author did
        /// not. A door or a Core left outside the edges is not an error here - <see cref="Validate"/>
        /// is what reports it, under the map's own problem list, and the author moves it themselves.
        /// </summary>
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

        /// <summary>
        /// The engine-free map this asset describes. A read: it answers about the asset as authored and
        /// writes nothing. Sizes are floored and arrays treated as they are for this conversion alone
        /// (<see cref="Normalize"/> keeps the asset itself repaired at import), so a half-edited asset
        /// converts honestly rather than being quietly repaired on someone who only asked a question.
        ///
        /// Two conversions are worth naming. A map with no obstacle on it hands over a
        /// <see langword="null"/> terrain array rather than a grid of plain ground, because that is what
        /// "a map that is all ground" already means to the runtime and it keeps the two forms honest.
        /// And the painted ore cells are joined back into row-run rectangles, because the runtime form
        /// states ore as patches: a painted vein of any size crosses as one rectangle per row it spans.
        /// </summary>
        public MapDefinition ToCore()
        {
            int width = Mathf.Max(1, Width);
            int height = Mathf.Max(1, Height);

            return new MapDefinition(Name, width, height, StartCircles,
                patches: OrePatches(width, height),
                spawnPoints: SpawnPointArray(),
                waves: WaveDefinitions(),
                terrain: TerrainArray(width, height),
                coreCell: CoreCell.x < 0 || CoreCell.y < 0 ? (Int2?)null : new Int2(CoreCell.x, CoreCell.y));
        }

        /// <summary>Everything wrong with this map, as the simulation would read it. What the editor
        /// window shows and what the test suite asserts on: one rule, one answer - and a pure one,
        /// because this runs inside a repaint pass, and a question that resized the asset on its way
        /// through <see cref="ToCore"/> was a mutation with no Undo and no dirty.</summary>
        public List<MapProblem> Validate() => MapValidation.Validate(ToCore());

        /// <summary>
        /// The one canonical walk of what this map <em>draws</em>: the ground and the mineral of every
        /// in-map cell, the in-bounds doors with the table position each carries, and the Core's
        /// footprint - normalized exactly the way <see cref="ToCore"/> normalizes, so the picture an
        /// editor paints and the map the simulation runs are projections of one enumeration, not two
        /// hand-walks that can drift. Painting surfaces and <see cref="ContentHash"/> read it; the
        /// drift it prevents is the one it names in the digest's own words ("what the preview draws").
        /// A read: it copies into a fresh <see cref="DrawnMap"/> and writes nothing back.
        /// </summary>
        public DrawnMap DrawableView()
        {
            int width = Mathf.Max(1, Width);
            int height = Mathf.Max(1, Height);

            var ground = new TerrainKind[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    ground[y * width + x] = TerrainAt(x, y);

            // The per-cell mineral, claimed the way the conversion claims it: the FIRST entry for a
            // cell counts, whatever it says, and entries past the edges are not on the map. Painting
            // keeps at most one entry per cell anyway (PaintOre writes one); this rule is what makes
            // an Inspector-handwritten list read the same here as it does in OrePatches.
            var mineral = new ShapeType[width * height];
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
                    mineral[at] = cell.Shape;
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

        /// <summary>
        /// A digest of everything the map draws, so "has this map changed?" is one comparison. It is
        /// the <see cref="DrawableView"/> walk and nothing else: size, Core footprint, every cell's
        /// ground and mineral, every in-bounds door with the table position it carries, and the door
        /// each wave group names - because the entry points are drawn with the next wave's own doors
        /// marked. What it leaves out is what nothing draws: the map's name and its starting stockpile.
        ///
        /// It is a hash of a few thousand bytes for a map this size, and deliberately not a revision
        /// counter on the asset: the writers are the painting surfaces, the Inspector and undo, and a
        /// counter only catches the ones that remember to count.
        /// </summary>
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
        // to the arrays above that a caller outside this type drives: before them, every editor that
        // wanted to add a door or a wave held the whole protocol - copy to a List, append or remove,
        // reassign the array (in-place would not survive Undo), and remember the bounds and the door
        // dedupe. The editor's MapEdit.Session wraps these with the Undo record and the dirty flag;
        // the rules themselves are the asset's, so the painting surfaces cannot disagree by a door.

        /// <summary>Resize, floored to the asset's own one-tile minimum, answering whether the canvas
        /// actually moved. An apply that changes nothing is not an edit and says so.</summary>
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

        /// <summary>Put an entry point on a tile, unless the map has no such tile or the tile already
        /// has a door: two doors at one tile would be two names for one place, and a wave table naming
        /// either would mean the same thing.</summary>
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

        /// <summary>Take the tile's entry point away if it has one, and say so - an erase dragged over
        /// open ground answers <see langword="false"/>, which is what keeps it from being an edit.</summary>
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

        /// <summary>Append an empty wave: how many enemies it should hold is the designer's answer, and
        /// the validation panel says the wave has none until it is given some.</summary>
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

        /// <summary>Append a group with the row's own defaults to the wave at a table position, so a
        /// group dropped into a wave is one the runtime can already spawn rather than one the designer
        /// has to fill in from nothing. A position with no row - the Inspector can leave one - gets
        /// nothing: which row to fill in is not something this operation can guess.</summary>
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
        /// size fields. A stale length is the one mistake that would otherwise reach the simulation as
        /// silently missing ground. A write-side repair: the read path never calls it.</summary>
        private void EnsureTerrainSize()
        {
            if (Terrain != null && Terrain.Length == Width * Height) return;
            Resize(Width, Height);
        }

        /// <summary>Bring the asset back to a state a conversion can trust: a size of at least one tile
        /// and a terrain array that matches it. Runs where the asset arrives or the Inspector touches
        /// it - never inside a query, which answers about the asset as it is instead of repairing it.</summary>
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

        /// <summary>Per-cell writes to the ore list churn this instead of the array while a batch is
        /// open, and <see cref="EndBatch"/> writes it back once. The batch is the writer's bookkeeping,
        /// not a second answer to what a brush paints: a stroke's writes are the same writes inside it
        /// as outside, and it holds the same entries in the same order the array did.</summary>
        private List<OreCell> _batch;

        /// <summary>
        /// Open a batch of writes: per-cell ore mechanics run on a private buffer that holds the array's
        /// entries, and <see cref="EndBatch"/> hands it back as the array once, which is what keeps a
        /// whole-map fill from rebuilding the ore list once per cell it paints. Nested calls are ignored -
        /// the batch is a scope a bulk writer opens once per sweep, and nothing inside it may resize the
        /// map: the terrain array's shape is settled by the first call here, so
        /// <see cref="EnsureTerrainSize"/>'s own <see cref="Resize"/> cannot land between the snapshot
        /// and the writes it watches over.
        /// </summary>
        internal void BeginBatch()
        {
            if (_batch != null) return;

            EnsureTerrainSize();

            _batch = new List<OreCell>(Ore.Length);
            if (Ore != null) _batch.AddRange(Ore);
        }

        /// <summary>Hand the batch's buffer back as the ore array. A double close writes nothing; the
        /// array is only replaced when a batch actually opened.</summary>
        internal void EndBatch()
        {
            if (_batch == null) return;

            Ore = _batch.ToArray();
            _batch = null;
        }

        /// <summary>Drop every entry naming a cell from the ore list - or, while a batch is open, from
        /// the batch's buffer, which is the same list until <see cref="EndBatch"/> hands it back. The
        /// order of what is kept is the order it was in, exactly as the array version keeps.</summary>
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
        // Every helper below answers about the asset as authored, at the given (floored) size. None of
        // them writes a field: the conversion the runtime asks for and the validation the repaint asks
        // for used to normalize the asset on their way through, which is a side effect with a query's
        // signature.

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

            // A map that is all ground hands over nothing rather than a grid of plain, because that is
            // what "all ground" already means to the runtime.
            return plain ? null : laid;
        }

        /// <summary>Painted ore as row-run rectangles, in stable row-major order. One pass over the ore
        /// list into a cell-indexed lookup, then one pass over the grid: the conversion runs on every
        /// editor pass, so scanning the list per tile would be a scan inside a scan - the same reason
        /// <see cref="DrawableView"/> walks the list once. The first entry for a cell is the one that
        /// counts, here and there, which is what makes the picture and the patches one answer.</summary>
        private ShapePatch[] OrePatches(int width, int height)
        {
            var shapes = new ShapeType[width * height];
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
                    shapes[at] = ore.Shape;
                }

            var patches = new List<ShapePatch>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    ShapeType shape = shapes[y * width + x];
                    if (!shape.IsShape()) { x++; continue; }

                    int start = x;
                    while (x < width && shapes[y * width + x] == shape) x++;
                    patches.Add(new ShapePatch(new Int2(start, y), x - start, 1, shape));
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

        /// <summary>Each row over to the engine-free wave through the row's own conversion
        /// (<see cref="MapAsset.WaveRow.ToCore"/>), so there is one statement of it. A row set to null
        /// in code - the Inspector cannot author one - gets the empty wave it always has.</summary>
        private WaveDefinition[] WaveDefinitions()
        {
            WaveRow[] rows = Waves ?? new WaveRow[0];
            var waves = new WaveDefinition[rows.Length];
            for (int w = 0; w < rows.Length; w++)
                waves[w] = rows[w] == null ? new WaveDefinition(0f) : rows[w].ToCore();

            return waves;
        }
    }
}
