using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One rectangular shape patch in the ground: a vein of one mineral.</summary>
    public readonly struct ShapePatch
    {
        public readonly Int2 Origin;
        public readonly int Width;
        public readonly int Height;

        /// <summary>The mineral the vein yields. A drill mines it as the full shape of that mineral, which
        /// is the root of every chain the map can build - and the reason the wave generator's demand pool
        /// is a map property: no square ore, no square-family shapes.</summary>
        public readonly Quad Mineral;

        public ShapePatch(Int2 origin, int width, int height, Quad mineral)
        {
            Origin = origin;
            Width = width;
            Height = height;
            Mineral = mineral;
        }

        public bool Contains(Int2 cell)
            => cell.X >= Origin.X && cell.Y >= Origin.Y &&
               cell.X < Origin.X + Width && cell.Y < Origin.Y + Height;
    }

    /// <summary>
    /// One map: size, starting stockpile, ground, mineral patches, the Core, wave doorways and the wave
    /// table - plus the two things the rework added to it: which operators the map has unlocked, and how
    /// its waves are sourced. All fixed data, so a run is reproducible from the world's state. Engine-free
    /// - the Editor's authoring surface is <c>MapAsset</c>, which converts with <c>ToCore()</c> to this
    /// same data.
    /// </summary>
    public sealed class MapDefinition
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Shapes the run starts with: the bootstrap budget for the first drill, its line home,
        /// and the first defence. The unit is a complete shape whatever mineral it is made of - see
        /// <see cref="CurrencyMineral"/> for the one this map's money is drawn as.</summary>
        public readonly int StartShapes;

        public readonly ShapePatch[] Patches;
        public readonly Int2[] SpawnPoints;
        public readonly WaveDefinition[] Waves;

        /// <summary>
        /// The mineral this map's money is <em>drawn</em> as: the first vein the map lists, or a circle for
        /// a map with no veins at all. The rule is not restricted to it - the Core banks <em>any</em>
        /// complete shape (<see cref="Shape.IsFull"/>) - but a mark needs a mineral and a sentence needs a
        /// noun, and the map's own ore is the coin the player will actually see arriving. Derived from the
        /// patches rather than authored, so a repainted vein cannot leave the HUD naming a mineral the map
        /// does not have.
        /// </summary>
        public readonly Quad CurrencyMineral;

        /// <summary>
        /// The operator machines this map has unlocked. Progression is this set rather than the map's
        /// ordinal (§7): a map teaches the operators it hands over, so a locked rotator is not merely
        /// unaffordable, it cannot be built at all.
        /// </summary>
        public readonly Operators Unlocked;

        /// <summary>The seed a run defaults to: the only source of randomness in Core, and what the wave
        /// generator draws from. Zero - the authored default - means the host draws a fresh seed per run
        /// instead (§5: one seed per run, shown in the HUD); a non-zero value pins every run of this map
        /// to it, for a demo that must replay exactly. Shown in the HUD either way.</summary>
        public readonly int Seed;

        /// <summary>
        /// How many generated waves follow the authored table, or <b>-1 for endless</b>. 0 - the authored
        /// default - means the map is finished when its table is cleared, which is what every scripted
        /// tutorial map wants; -1 is how a map becomes endless mode, since the generator then never runs
        /// out of demands.
        /// </summary>
        public readonly int GeneratedWaves;

        /// <summary>Ground of every tile, row-major (<c>y * Width + x</c>), or <see langword="null"/> for
        /// plain, buildable ground everywhere - not a special case anywhere. <see cref="TerrainLayout.Rects"/>
        /// is the authoring form; the painted asset carries the array itself, cell by cell.</summary>
        public readonly TerrainKind[] Terrain;

        /// <summary>Lower-left tile of the Core's footprint. Authored rather than derived: the middle of
        /// a rectangle fits a tutorial but not a non-rectangular map. Left unset, it is centred - the value
        /// every map had before the field existed.</summary>
        public readonly Int2 CoreCell;

        public MapDefinition(string name, int width, int height, int startShapes,
            ShapePatch[] patches, Int2[] spawnPoints, WaveDefinition[] waves,
            TerrainKind[] terrain = null, Int2? coreCell = null,
            Operators unlocked = Operators.All, int seed = 0, int generatedWaves = 0)
        {
            Name = name;
            Width = width;
            Height = height;
            StartShapes = startShapes;
            Patches = patches ?? new ShapePatch[0];
            SpawnPoints = spawnPoints ?? new Int2[0];
            Waves = waves ?? new WaveDefinition[0];
            Terrain = terrain;
            CoreCell = coreCell ?? CentredCore(width, height);
            Unlocked = unlocked;
            Seed = seed;
            GeneratedWaves = generatedWaves;

            // The money, named off the map's own veins rather than authored beside them: the first mineral
            // the patch list yields, which is the one a drill on this map will first put on a belt.
            Quad[] minerals = MineralList();
            CurrencyMineral = minerals.Length > 0 ? minerals[0] : Quad.Circle;
        }

        /// <summary>
        /// The closure of this map's minerals under its unlocked operators: every item a player on this map
        /// can actually build, and therefore the only pool the wave generator may demand from. Built on
        /// demand and cheap (a breadth-first walk over at most 80 items) - the map is fixed, so the answer
        /// is too.
        /// </summary>
        public ShapePool Pool() => new ShapePool(MineralList(), Unlocked);

        /// <summary>The distinct minerals this map's veins yield, in the order the patches list them. What
        /// the generator roots its pool at.</summary>
        public Quad[] MineralList()
        {
            var minerals = new List<Quad>();
            for (int i = 0; i < Patches.Length; i++)
            {
                Quad mineral = Patches[i].Mineral;
                if (mineral == Quad.Empty || minerals.Contains(mineral)) continue;
                minerals.Add(mineral);
            }

            return minerals.ToArray();
        }

        /// <summary>Does this map source waves procedurally? A generated map may author a scripted opening
        /// and then hand over to the generator, or author nothing at all and be generated from wave 1.</summary>
        public bool GeneratesWaves => GeneratedWaves != 0;

        /// <summary>The same map, with its wave count unbounded: the campaign's entry into endless mode
        /// (§7) is the last map played again with the generator answering forever, and the driver - not
        /// the map data - is what flips it, so an authored map stays what the editor drew. Everything else
        /// is carried over as-is; the run's seed remains the host's per-run decision.</summary>
        public MapDefinition WithEndless()
            => new MapDefinition(Name, Width, Height, StartShapes, Patches, SpawnPoints, Waves,
                Terrain, CoreCell, Unlocked, Seed, -1);

        /// <summary>Centre of a spawn point in world units, where enemies are created. The index is
        /// resolved by <see cref="ResolveSpawn"/> like every other reader, so the entry tile is the one the
        /// marker lights and the info card names.
        ///
        /// Bad data - a table that named a negative door, or a map with no doors at all - reads as the
        /// first door rather than an out-of-range crash: <see cref="MapValidation"/> already names the map
        /// that wrote it, and a wave walking in from the wrong door is visible, where a thrown index on a
        /// spawn tick is a dead run.</summary>
        public Vec2 SpawnCenter(int index)
        {
            if (SpawnPoints.Length == 0) return new Vec2(0.5f, 0.5f);

            int resolved = ResolveSpawn(index);
            Int2 cell = SpawnPoints[resolved < 0 ? 0 : resolved];
            return new Vec2(cell.X + 0.5f, cell.Y + 0.5f);
        }

        /// <summary>Where this cell sits in <see cref="SpawnPoints"/>, or -1 for a tile no wave enters
        /// at. The one answer to "is this an entry point", so the cursor and the map data cannot
        /// disagree.</summary>
        public int SpawnIndexOf(Int2 cell)
        {
            for (int i = 0; i < SpawnPoints.Length; i++)
                if (SpawnPoints[i] == cell) return i;

            return -1;
        }

        /// <summary>The entry point a wave group's <see cref="SpawnGroup.SpawnPoint"/> names, or -1 when
        /// the map has no entry point or the table named a negative position. The index wraps, because a
        /// table names its door by position: one authored against a longer list stays loadable by a map
        /// with fewer doors.</summary>
        public int ResolveSpawn(int spawnPoint)
            => spawnPoint < 0 || SpawnPoints.Length == 0 ? -1 : spawnPoint % SpawnPoints.Length;

        /// <summary>The ground a cell carries, with a map that has no terrain array reading as plain
        /// ground everywhere; a short array reads as ground past its end, because the validator asks this
        /// about bad data. This is the authored terrain <em>array</em>, not the world's ground: a vein is a
        /// patch, so an array that states ore is the one map error <see cref="MapValidation"/> names.
        /// <see cref="StampTerrain"/> is the whole rule.</summary>
        public TerrainKind TerrainAt(Int2 cell)
        {
            if (Terrain == null || cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height)
                return TerrainKind.Ground;

            int i = cell.Y * Width + cell.X;
            return i < Terrain.Length ? Terrain[i] : TerrainKind.Ground;
        }

        /// <summary>True for a tile of the Core's footprint: the block keeps its tiles whatever the
        /// map's ground says, since a rock drawn over the Core would be unplayable.</summary>
        public bool IsCoreTile(Int2 cell)
            => cell.X >= CoreCell.X && cell.Y >= CoreCell.Y &&
               cell.X < CoreCell.X + CoreState.Size && cell.Y < CoreCell.Y + CoreState.Size;

        /// <summary>True where a shape patch covers a cell, the Core's tiles included. The one test of
        /// "is there a vein here", so <see cref="AuthoredGroundAt"/> and <see cref="MapValidation"/>'s
        /// ore rule cannot come apart.</summary>
        public bool PatchClaims(Int2 cell)
        {
            for (int p = 0; p < Patches.Length; p++)
                if (Patches[p].Mineral != Quad.Empty && Patches[p].Contains(cell)) return true;

            return false;
        }

        /// <summary>The ground this map's <em>data</em> states at one cell, the Core included: ore where
        /// a patch covers it, otherwise its terrain array. This is <see cref="StampTerrain"/>'s precedence
        /// asked per cell - a patch outbids the array, because a vein is the more specific intent - and the
        /// one statement of that ordering. The Core's own tiles are included, which the stamp exempts, so
        /// the validator judges the map the author wrote rather than the ground that was built.</summary>
        public TerrainKind AuthoredGroundAt(Int2 cell)
            => PatchClaims(cell) ? TerrainKind.Ore : TerrainAt(cell);

        /// <summary>Write this map's ground into a cell array of <c>Width * Height</c>, row-major: what
        /// the world will actually have. The one statement of a map's ground; <see cref="TerrainField.Stamp"/>
        /// and <see cref="MapValidation"/> read it from here. Three rules, in order: the Core's tiles stay
        /// plain ground; a patch claims its cells as ore (a vein is the more specific intent, so a rock
        /// drawn across one cannot delete the mineral); the terrain array paints what nobody claimed.
        /// An array that states ore is a map no writer produces, and <see cref="MapValidation"/> names it.
        /// <paramref name="mineral"/> optionally receives the mineral of every ore cell.</summary>
        public void StampTerrain(TerrainKind[] kind, Quad[] mineral = null)
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
                        if (mineral != null && i < mineral.Length) mineral[i] = patch.Mineral;
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

        /// <summary>Lower-left tile of a Core centred on the map, clamped so a map only just bigger than
        /// the Core still puts it in bounds. Public because the Editor asks it too: a map that names no Core
        /// is drawn where the Core <em>will</em> stand, before the conversion that decides it, and an
        /// editor-side copy would be a second answer that can drift from the simulation's.</summary>
        public static Int2 CentredCore(int width, int height)
        {
            int x = Math.Clamp(width / 2 - CoreState.Size / 2, 0, Math.Max(0, width - CoreState.Size));
            int y = Math.Clamp(height / 2 - CoreState.Size / 2, 0, Math.Max(0, height - CoreState.Size));
            return new Int2(x, y);
        }
    }
}
