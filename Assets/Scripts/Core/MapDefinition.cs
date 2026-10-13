using System;

namespace Facet.Core
{
    /// <summary>One rectangular shape patch in the ground.</summary>
    public readonly struct ShapePatch
    {
        public readonly Int2 Origin;
        public readonly int Width;
        public readonly int Height;
        public readonly ShapeType Shape;

        public ShapePatch(Int2 origin, int width, int height, ShapeType shape)
        {
            Origin = origin;
            Width = width;
            Height = height;
            Shape = shape;
        }

        public bool Contains(Int2 cell)
            => cell.X >= Origin.X && cell.Y >= Origin.Y &&
               cell.X < Origin.X + Width && cell.Y < Origin.Y + Height;
    }

    /// <summary>
    /// One map: its size, the stockpile the run starts with, the ground itself, where minerals come out
    /// of it, where the Core stands, where waves walk in from, and the wave table. All fixed data, so a
    /// run is reproducible from the world's own state.
    ///
    /// This is the engine-free form the simulation runs on - plain arrays in, nothing else. The Editor's
    /// authoring surface is <c>MapAsset</c>, which converts itself to one of these with <c>ToCore()</c>,
    /// exactly as the content table does; so a map painted by hand and a map baked from one arrive here as
    /// the same data and the simulation cannot tell them apart.
    /// </summary>
    public sealed class MapDefinition
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Circles in the stockpile when the run starts. The bootstrap budget: the first
        /// drill, its line home, and the first defence all come out of this.</summary>
        public readonly int StartCircles;

        public readonly ShapePatch[] Patches;
        public readonly Int2[] SpawnPoints;
        public readonly WaveDefinition[] Waves;

        /// <summary>
        /// The ground of every tile, row-major (<c>y * Width + x</c>), or <see langword="null"/> for a map
        /// that is plain, buildable ground everywhere - which is what every map used to be, so a map with
        /// no terrain is not a special case anywhere. <see cref="TerrainLayout.Rects"/> is the authoring
        /// form of it - "rock from here to there" instead of an 80x48 literal - and the asset the Editor
        /// paints carries the array itself, cell by cell.
        /// </summary>
        public readonly TerrainKind[] Terrain;

        /// <summary>
        /// Lower-left tile of the Core's footprint. Authored rather than derived: the middle of a
        /// rectangle is a fine place for a tutorial's Core and a poor one for a map that is not a
        /// rectangle, so the map says where its Core is and this is the one answer every reader uses.
        /// Left unset, it is centred - the value every map had before the field existed.
        /// </summary>
        public readonly Int2 CoreCell;

        public MapDefinition(string name, int width, int height, int startCircles,
            ShapePatch[] patches, Int2[] spawnPoints, WaveDefinition[] waves,
            TerrainKind[] terrain = null, Int2? coreCell = null)
        {
            Name = name;
            Width = width;
            Height = height;
            StartCircles = startCircles;
            Patches = patches ?? new ShapePatch[0];
            SpawnPoints = spawnPoints ?? new Int2[0];
            Waves = waves ?? new WaveDefinition[0];
            Terrain = terrain;
            CoreCell = coreCell ?? CentredCore(width, height);
        }

        /// <summary>Centre of a spawn point in world units, where enemies are created. The index is the
        /// wave table's, resolved by <see cref="ResolveSpawn"/> like every other reader of it, so the
        /// tile the enemies enter at is the tile the marker lights and the info card names.</summary>
        public Vec2 SpawnCenter(int index)
        {
            Int2 cell = SpawnPoints[ResolveSpawn(index)];
            return new Vec2(cell.X + 0.5f, cell.Y + 0.5f);
        }

        /// <summary>
        /// Where this cell sits in <see cref="SpawnPoints"/>, or -1 for a tile no wave enters at. The one
        /// answer to "is this an entry point", so the cursor that marks the tile and the map data that
        /// names the door cannot disagree.
        /// </summary>
        public int SpawnIndexOf(Int2 cell)
        {
            for (int i = 0; i < SpawnPoints.Length; i++)
                if (SpawnPoints[i] == cell) return i;

            return -1;
        }

        /// <summary>
        /// The entry point a wave group's <see cref="SpawnGroup.SpawnPoint"/> names, or -1 when this map
        /// has no entry point to name - or the table named a negative position, which is not a door.
        ///
        /// The index wraps, because a wave table names its door by *position*: a table authored against
        /// one map's list stays loadable by a map with fewer doors instead of indexing off the end.
        /// </summary>
        public int ResolveSpawn(int spawnPoint)
            => spawnPoint < 0 || SpawnPoints.Length == 0 ? -1 : spawnPoint % SpawnPoints.Length;

        /// <summary>The ground a cell carries, with a map that has no terrain array reading as plain
        /// ground everywhere. The one answer to "what does this map say the ground is", so nothing that
        /// reads map data has to special-case a map written before terrain existed. A short array reads
        /// as ground past its end rather than throwing, because this is asked about bad map data by the
        /// validator - which is the thing that has to survive reading it in order to report it.
        ///
        /// This is the map's terrain <em>array</em> as authored, and deliberately not the ground a world
        /// ends up with: a vein is stated as a patch, so a cell inside one is ore in the world whether or
        /// not this says so. Every writer in the game leaves the array plain under a vein - a vein is the
        /// ore list's entry and nothing else - so an array that says ore is the one map error
        /// <see cref="MapValidation"/> names rather than papers over. <see cref="StampTerrain"/> is the
        /// whole rule.</summary>
        public TerrainKind TerrainAt(Int2 cell)
        {
            if (Terrain == null || cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height)
                return TerrainKind.Ground;

            int i = cell.Y * Width + cell.X;
            return i < Terrain.Length ? Terrain[i] : TerrainKind.Ground;
        }

        /// <summary>True for a tile of the Core's footprint, the block that keeps its tiles whatever the
        /// map's ground says - a rock drawn over the thing the player defends would be a map that cannot
        /// be played.</summary>
        public bool IsCoreTile(Int2 cell)
            => cell.X >= CoreCell.X && cell.Y >= CoreCell.Y &&
               cell.X < CoreCell.X + CoreState.Size && cell.Y < CoreCell.Y + CoreState.Size;

        /// <summary>True where a shape patch covers a cell, the Core's tiles included. The one test of
        /// "is there a vein here", so <see cref="AuthoredGroundAt"/> and <see cref="MapValidation"/>'s
        /// ore rule cannot come apart about it.</summary>
        public bool PatchClaims(Int2 cell)
        {
            for (int p = 0; p < Patches.Length; p++)
                if (Patches[p].Shape.IsShape() && Patches[p].Contains(cell)) return true;

            return false;
        }

        /// <summary>
        /// The ground this map's <em>data</em> states at one cell, the Core included: ore where a shape
        /// patch covers it, otherwise whatever its terrain array says.
        ///
        /// This is <see cref="StampTerrain"/>'s precedence asked about a single tile - a patch outbids
        /// the array, because a vein is the more specific authoring intent - and the one statement of
        /// that ordering: <see cref="StampTerrain"/> applies it in bulk and <see cref="MapValidation"/>
        /// asks it per cell, so when one changes, the other is the same rule it is changing. The Core's
        /// own tiles are included, which the stamp exempts: the world zeroes them after patching so the
        /// Core always keeps plain ground under it, and that is the ground a world gets - the validator
        /// judges the map the author wrote, not the ground that was built.
        /// </summary>
        public TerrainKind AuthoredGroundAt(Int2 cell)
            => PatchClaims(cell) ? TerrainKind.Ore : TerrainAt(cell);

        /// <summary>
        /// Write this map's ground into a cell array of <c>Width * Height</c>, row-major: what the world
        /// will actually have.
        ///
        /// This is the one statement of what a map's ground is, and everything that needs to know reads
        /// it from here - <see cref="TerrainField.Stamp"/> to build a world, and <see cref="MapValidation"/>
        /// to walk the map a designer drew. Three rules, in this order:
        ///
        ///   * the Core's own tiles stay plain ground, whatever the map says about them;
        ///   * a shape patch claims its cells as ore, because a vein is the more specific authoring
        ///     intent - a whole chain is built around where it is - so a rock drawn across one cannot
        ///     quietly delete the mineral;
        ///   * the terrain array paints what nobody claimed, so a rock is likewise never quietly turned
        ///     into ore.
        ///
        /// An array that states ore at all is a map no writer in the game produces, and
        /// <see cref="MapValidation"/> is what reports it - with or without a patch on the same cell, so
        /// neither half of a vein can be written alone without being named.
        /// The ordering itself is not re-stated by its readers: <see cref="PatchClaims"/> is this
        /// precedence's one test, and <see cref="AuthoredGroundAt"/> asks it, as does the validator.
        /// <paramref name="shape"/> is optional and receives the mineral of every ore cell, for the
        /// caller that also needs what a vein yields.
        /// </summary>
        public void StampTerrain(TerrainKind[] kind, ShapeType[] shape = null)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));

            int cells = Width * Height;
            for (int p = 0; p < Patches.Length; p++)
            {
                ShapePatch patch = Patches[p];
                for (int y = 0; y < patch.Height; y++)
                {
                    for (int x = 0; x < patch.Width; x++)
                    {
                        var cell = new Int2(patch.Origin.X + x, patch.Origin.Y + y);
                        if (cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height) continue;
                        if (IsCoreTile(cell)) continue;

                        int i = cell.Y * Width + cell.X;
                        if (i >= kind.Length) continue;

                        kind[i] = TerrainKind.Ore;
                        if (shape != null && i < shape.Length) shape[i] = patch.Shape;
                    }
                }
            }

            if (Terrain == null) return;

            for (int i = 0; i < Terrain.Length && i < cells && i < kind.Length; i++)
            {
                if (Terrain[i] == TerrainKind.Ground) continue;

                var cell = new Int2(i % Width, i / Width);
                if (IsCoreTile(cell)) continue;
                if (kind[i] != TerrainKind.Ground) continue;   // already claimed by a patch
                kind[i] = Terrain[i];
            }
        }

        /// <summary>
        /// Lower-left tile of a Core centred on the map: the block's block, clamped so a map only just
        /// bigger than the Core still puts it in bounds.
        ///
        /// Public because it is the answer to a question the Editor asks too: a map that names no Core is
        /// drawn where the Core <em>will</em> stand, and that is before the conversion that decides it has
        /// run. An editor-side copy of this arithmetic would be a second answer that can drift from the
        /// one the simulation builds.
        /// </summary>
        public static Int2 CentredCore(int width, int height)
        {
            int x = Math.Clamp(width / 2 - CoreState.Size / 2, 0, Math.Max(0, width - CoreState.Size));
            int y = Math.Clamp(height / 2 - CoreState.Size / 2, 0, Math.Max(0, height - CoreState.Size));
            return new Int2(x, y);
        }
    }
}
