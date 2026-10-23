using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>What is wrong with a map. One value per rule, so a caller can group or filter without
    /// parsing the sentence that comes with it.</summary>
    public enum MapProblemKind
    {
        /// <summary>The Core's footprint is not wholly inside the grid.</summary>
        CoreOutOfBounds = 0,

        /// <summary>A terrain array whose length is not Width * Height.</summary>
        TerrainSizeMismatch = 1,

        /// <summary>The map's ground is not plain under the Core it also places.</summary>
        CoreUnderTerrain = 2,

        /// <summary>No entry point at all: nothing can walk in.</summary>
        NoSpawnPoints = 3,

        /// <summary>No waves: a map that can never be cleared and never ends.</summary>
        NoWaves = 4,

        /// <summary>An entry point outside the grid.</summary>
        SpawnOffMap = 5,

        /// <summary>An entry point standing on rock, water or void - a wave spawning inside a wall.</summary>
        SpawnOnTerrain = 6,

        /// <summary>An entry point with no walkable route to the Core, so its wave can never arrive.</summary>
        SpawnUnreachable = 7,

        /// <summary>Ore no belt can reach, so nothing can be mined there.</summary>
        OreCutOff = 8,

        /// <summary>A shape patch hanging off the grid.</summary>
        PatchOffMap = 9,

        /// <summary>A shape patch naming no mineral.</summary>
        PatchWithoutMineral = 10,

        /// <summary>A wave group naming an entry point the map does not have.</summary>
        GroupAtMissingDoor = 11,

        /// <summary>A wave with no enemies in it: it can never be cleared.</summary>
        WaveWithoutEnemies = 12,

        /// <summary>The terrain array stating ore, which no writer in the game does: a vein is the ore
        /// list's entry, and the ground under one is plain.</summary>
        OreInTerrain = 13,

        /// <summary>Two ore entries naming one cell, of which only the first one counts.</summary>
        DuplicateOre = 14,

        /// <summary>A wave group naming no weakness shape, so no round can ever match it and the wave is
        /// only killable at a half damage.</summary>
        GroupWithoutDemand = 15,

        /// <summary>A wave group whose weakness shape this map's minerals and unlocked operators cannot
        /// build, which is the same unwinnable wave as naming none - and just as invisible at runtime.</summary>
        DemandNotBuildable = 16,

        /// <summary>The generator is asked for waves but no vein yields a mineral, so the closure it draws
        /// its demands from is empty and every generated wave arrives with nothing in it.</summary>
        GeneratorWithoutMineral = 17,

        /// <summary>A tutorial line with no text: the case at the end of the tutorial list, a row added by
        /// accident, prints a blank line the player is told to act on.</summary>
        TutorialLineWithoutText = 18,

        /// <summary>A tutorial line whose guide tile is outside the grid: the arrow points off the map at
        /// nothing.</summary>
        TutorialTargetOffMap = 19,

        /// <summary>A tutorial line no rule can ever satisfy, because the building it is about is never
        /// handed over - either no earlier line opens it, or the map's own operator set refuses it. The lesson
        /// then never completes, so every wave it gates waits forever: a hung run with nothing said.</summary>
        TutorialStepUnreachable = 20,

        /// <summary>A tutorial line naming a step this build has no rule for - a row serialized before the
        /// enum changed, or a hand-edited file. It is the same hung run as the entry above, and a validator has
        /// to be able to say so rather than throwing at a designer who cannot act on a stack trace.</summary>
        TutorialStepUnknown = 21,

        /// <summary>A tutorial line (or its hint) too long for the card that prints it: the task card's body
        /// is five micro lines in a fixed corner, so a sentence past the budget spills past the card's edge
        /// onto the map. The HUD shrinks type to cope, but the shrink has a floor - past it, the answer is
        /// shorter words, not smaller ones.</summary>
        TutorialLineTooLong = 22,
    }

    /// <summary>One problem found in a map, with the sentence a designer can act on.</summary>
    public readonly struct MapProblem
    {
        public readonly MapProblemKind Kind;

        /// <summary>The tile the problem is about, or (-1,-1) when it is about the map as a whole.</summary>
        public readonly Int2 Cell;

        public readonly string Message;

        public MapProblem(MapProblemKind kind, Int2 cell, string message)
        {
            Kind = kind;
            Cell = cell;
            Message = message;
        }

        public override string ToString() => Message;
    }

    /// <summary>
    /// The rules a map must satisfy to be playable, as one engine-free function - the Editor's painting
    /// surfaces, the test suite and a campaign-baked map are all held to the same answer.
    ///
    /// The expensive check is the flood over the grid from the Core's footprint: can each door be walked
    /// from, can each vein be reached by a belt. These are the mistakes nothing else catches - a ridge
    /// across a lane, a rock on a door, a patch walled off behind water - and none of them throws; they
    /// produce a wave that stands still, which reads as a hung game rather than a bug.
    ///
    /// It walks the ground through <see cref="MapDefinition.StampTerrain"/> - the same rule
    /// <see cref="TerrainField.Stamp"/> builds a world with - so the map being judged and the map the
    /// simulation runs are one, patches and all. Buildings are deliberately excluded: a map is validated
    /// as the player first sees it, with nothing built; whether a defence holds is balance, not data error.
    /// </summary>
    public static class MapValidation
    {
        /// <summary>Relaxation order, fixed so a report is the same list every run.</summary>
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>Every problem with a map, in a stable order. Empty means playable.</summary>
        public static List<MapProblem> Validate(MapDefinition map)
        {
            var problems = new List<MapProblem>();
            Validate(map, problems);
            return problems;
        }

        /// <summary>The caller-owned-list form. Private, because validation runs once per map load or
        /// editor pass - nobody validates per frame - so the allocating overload is the whole public
        /// answer.</summary>
        private static void Validate(MapDefinition map, List<MapProblem> into)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (into == null) throw new ArgumentNullException(nameof(into));

            into.Clear();

            CheckPatches(map, into);
            CheckWaves(map, into);
            CheckTerrainSize(map, into);

            // The array's own rule, asked with the cheap ones: it is about the data as written down, not
            // the ground a world would be built with, so a Core that does not fit still gets its veins named.
            CheckOreInTerrain(map, into);

            // Everything left is asked from the Core outwards, so a Core that does not fit is the one
            // problem worth reporting on its own: without seeds every cell reads as unreachable, and the
            // report would be a wall of noise around one real mistake.
            if (!CoreInBounds(map))
            {
                into.Add(new MapProblem(MapProblemKind.CoreOutOfBounds, map.CoreCell,
                    map.Name + ": the Core's " + CoreState.Size + "x" + CoreState.Size + " footprint at " +
                    map.CoreCell + " does not fit in " + map.Width + "x" + map.Height));
                return;
            }

            TerrainKind[] ground = GroundOf(map);
            CheckCoreGround(map, into);

            bool[] walkable = Flood(map, ground, forTransport: false);
            bool[] transport = Flood(map, ground, forTransport: true);

            CheckSpawns(map, ground, walkable, into);
            CheckOre(map, ground, transport, into);
        }

        /// <summary>
        /// The ground the map really has. This calls <see cref="MapDefinition.StampTerrain"/>, the same
        /// call <see cref="TerrainField.Stamp"/> makes, so a rule that changes there cannot leave this
        /// walking a map nobody will ever play.
        /// </summary>
        private static TerrainKind[] GroundOf(MapDefinition map)
        {
            var ground = new TerrainKind[map.Width * map.Height];
            map.StampTerrain(ground);
            return ground;
        }

        /// <summary>A terrain array that does not describe this many tiles.</summary>
        private static void CheckTerrainSize(MapDefinition map, List<MapProblem> into)
        {
            if (map.Terrain == null || map.Terrain.Length == map.Width * map.Height) return;

            into.Add(new MapProblem(MapProblemKind.TerrainSizeMismatch, NoCell,
                map.Name + ": the terrain array holds " + map.Terrain.Length + " tiles, and the map is " +
                map.Width * map.Height + " (" + map.Width + "x" + map.Height + ")"));
        }

        /// <summary>
        /// Ground under the Core that is not plain. The Core keeps its tiles, so the world is playable
        /// either way - but a map whose data disagrees with where it puts the Core has lost track of one
        /// of the two, which is worth saying.
        ///
        /// Asks the map's <em>data</em> (<see cref="MapDefinition.AuthoredGroundAt"/>), not the ground the
        /// world would be built with: the world zeroes the Core's tiles after patching, so that ground is
        /// plain there by construction and asking it could never find anything. The disagreement is the
        /// finding.
        /// </summary>
        private static void CheckCoreGround(MapDefinition map, List<MapProblem> into)
        {
            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    var cell = new Int2(map.CoreCell.X + x, map.CoreCell.Y + y);
                    TerrainKind kind = map.AuthoredGroundAt(cell);
                    if (kind == TerrainKind.Ground) continue;

                    into.Add(new MapProblem(MapProblemKind.CoreUnderTerrain, cell,
                        map.Name + ": the ground says " + kind + " under the Core at " + cell +
                        " - the Core keeps its tiles, so the map's data is what is wrong"));
                }
            }
        }

        private static void CheckPatches(MapDefinition map, List<MapProblem> into)
        {
            for (int p = 0; p < map.Patches.Length; p++)
            {
                ShapePatch patch = map.Patches[p];
                if (patch.Mineral == Quad.Empty)
                {
                    into.Add(new MapProblem(MapProblemKind.PatchWithoutMineral, patch.Origin,
                        map.Name + ": a shape patch at " + patch.Origin + " yields no mineral"));
                }

                foreach (Int2 corner in new[]
                         {
                             patch.Origin,
                             new Int2(patch.Origin.X + patch.Width - 1, patch.Origin.Y + patch.Height - 1),
                         })
                {
                    if (corner.X >= 0 && corner.Y >= 0 && corner.X < map.Width && corner.Y < map.Height)
                        continue;

                    into.Add(new MapProblem(MapProblemKind.PatchOffMap, corner,
                        map.Name + ": a shape patch hangs off the map at " + corner));
                }
            }
        }

        /// <summary>
        /// The terrain array stating ore, which no writer in the game produces - painting writes a vein
        /// as an ore-list entry and leaves the ground under it plain. Two ways to get here: with <em>no</em>
        /// vein listed the world still gets an ore tile, one only a drill may stand on and that yields
        /// nothing; with a vein already listed, the byte duplicates the entry and leaves an ore tile behind
        /// the moment that entry is removed by hand.
        /// </summary>
        private static void CheckOreInTerrain(MapDefinition map, List<MapProblem> into)
        {
            if (map.Terrain == null) return;

            int cells = map.Width * map.Height;
            for (int i = 0; i < map.Terrain.Length && i < cells; i++)
            {
                if (map.Terrain[i] != TerrainKind.Ore) continue;

                var cell = new Int2(i % map.Width, i / map.Width);
                string why = map.PatchClaims(cell)
                    ? " as well as the vein listed there - a vein is the ore list's entry, and the ground " +
                      "under one is plain"
                    : " and no vein is listed there - a drill would stand on it and mine nothing";

                into.Add(new MapProblem(MapProblemKind.OreInTerrain, cell,
                    map.Name + ": the terrain array says Ore at " + cell + why));
            }
        }

        private static void CheckWaves(MapDefinition map, List<MapProblem> into)
        {
            if (map.SpawnPoints.Length == 0)
            {
                into.Add(new MapProblem(MapProblemKind.NoSpawnPoints, NoCell,
                    map.Name + ": no entry points, so no wave can walk in"));
            }

            // A map with no authored waves is legitimate now: the wave generator replaces the hand-written
            // table, and a map that hands the whole run to it (or to it after a scripted opening) says so
            // with <see cref="MapDefinition.GeneratedWaves"/>. What is still a mistake is a map with
            // neither - it can never be cleared and never ends.
            if (map.Waves.Length == 0 && !map.GeneratesWaves)
            {
                into.Add(new MapProblem(MapProblemKind.NoWaves, NoCell,
                    map.Name + ": no waves and no generator, so the map can never be cleared"));
            }
            else if (map.GeneratesWaves && map.MineralList().Length == 0)
            {
                // The generator roots its pool at the map's veins, so a map that mines nothing has an empty
                // closure: every generated wave would be built out of nothing and arrive empty, which ends a
                // bounded run without a fight and never ends an endless one.
                into.Add(new MapProblem(MapProblemKind.GeneratorWithoutMineral, NoCell,
                    map.Name + ": the generator runs but no vein names a mineral, so it has nothing to " +
                    "demand and every generated wave arrives empty"));
            }

            // The hand-authored table is the one entrance the closure does not guard: ShapePool keeps the
            // generator honest, but a designer naming an item the map cannot build ships a wave nothing can
            // answer. Built once, and only when some group actually names a demand.
            ShapePool pool = null;

            for (int w = 0; w < map.Waves.Length; w++)
            {
                WaveDefinition wave = map.Waves[w];
                if (wave.Groups.Length == 0 || wave.Total <= 0)
                {
                    into.Add(new MapProblem(MapProblemKind.WaveWithoutEnemies, NoCell,
                        map.Name + ": wave " + (w + 1) + " has no enemies, so it can never be cleared"));
                }

                for (int g = 0; g < wave.Groups.Length; g++)
                {
                    SpawnGroup group = wave.Groups[g];
                    if (group.Weakness.IsEmpty)
                    {
                        into.Add(new MapProblem(MapProblemKind.GroupWithoutDemand, NoCell,
                            map.Name + ": wave " + (w + 1) + "'s group of " + group.Count + " " +
                            group.Kind + " names no weakness shape, so nothing can ever match it"));
                    }
                    else
                    {
                        // Asked of a map that mines something: a map with no vein at all can build nothing, so
                        // every demand in it is unbuildable and the report would be one copy of the same
                        // sentence per group - about the map, not about the wave. What this rule is for is a
                        // designer naming an item the map's own minerals and operators do not reach, which is
                        // exactly as unwinnable and much harder to see.
                        pool ??= map.Pool();
                        if (pool.Minerals.Count > 0 && !pool.CanProduce(group.Weakness))
                        {
                            into.Add(new MapProblem(MapProblemKind.DemandNotBuildable, NoCell,
                                map.Name + ": wave " + (w + 1) + "'s group of " + group.Count + " " +
                                group.Kind + " is weak to " + group.Weakness.Describe() + ", which this " +
                                "map's minerals and unlocked operators cannot build"));
                        }
                    }

                    if (group.SpawnPoint >= 0 && group.SpawnPoint < map.SpawnPoints.Length) continue;

                    into.Add(new MapProblem(MapProblemKind.GroupAtMissingDoor, NoCell,
                        map.Name + ": wave " + (w + 1) + " sends " + group.Count + " " + group.Kind +
                        " to entry point " + group.SpawnPoint + ", and the map has " +
                        map.SpawnPoints.Length));
                }
            }
        }

        private static void CheckSpawns(MapDefinition map, TerrainKind[] ground, bool[] walkable,
            List<MapProblem> into)
        {
            for (int i = 0; i < map.SpawnPoints.Length; i++)
            {
                Int2 spawn = map.SpawnPoints[i];
                if (!InBounds(map, spawn))
                {
                    into.Add(new MapProblem(MapProblemKind.SpawnOffMap, spawn,
                        map.Name + ": entry point " + i + " is off the map at " + spawn));
                    continue;
                }

                TerrainKind kind = ground[Index(map, spawn)];
                if (!kind.Walkable())
                {
                    into.Add(new MapProblem(MapProblemKind.SpawnOnTerrain, spawn,
                        map.Name + ": entry point " + i + " at " + spawn + " stands on " + kind +
                        ", so its wave spawns inside the ground"));
                    continue;
                }

                if (!walkable[Index(map, spawn)])
                {
                    into.Add(new MapProblem(MapProblemKind.SpawnUnreachable, spawn,
                        map.Name + ": entry point " + i + " at " + spawn +
                        " has no walkable route to the Core, so its wave would stand there forever"));
                }
            }
        }

        /// <summary>Ore no belt can reach: a vein behind rock or water is a drill nobody can feed, and a
        /// player only finds that out after buying the drill.</summary>
        private static void CheckOre(MapDefinition map, TerrainKind[] ground, bool[] transport,
            List<MapProblem> into)
        {
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    int i = y * map.Width + x;
                    if (ground[i] != TerrainKind.Ore || transport[i]) continue;

                    into.Add(new MapProblem(MapProblemKind.OreCutOff, new Int2(x, y),
                        map.Name + ": the ore at (" + x + "," + y + ") has no route a belt can take to " +
                        "the Core, so nothing can be mined there"));
                }
            }
        }

        /// <summary>
        /// Which cells are reachable from the Core, over the ground the map draws. Seeded with the Core's
        /// whole footprint, because arriving anywhere on it is arriving - the same seed the walkers' flow
        /// field uses, so the two cannot disagree about whether a door is connected.
        /// </summary>
        private static bool[] Flood(MapDefinition map, TerrainKind[] ground, bool forTransport)
        {
            int cells = map.Width * map.Height;
            var seen = new bool[cells];
            var queue = new int[cells];
            int head = 0;
            int tail = 0;

            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    var seed = new Int2(map.CoreCell.X + x, map.CoreCell.Y + y);
                    if (!InBounds(map, seed)) continue;

                    int i = Index(map, seed);
                    if (seen[i]) continue;

                    seen[i] = true;
                    queue[tail++] = i;
                }
            }

            while (head < tail)
            {
                int current = queue[head++];
                var cell = new Int2(current % map.Width, current / map.Width);

                for (int k = 0; k < Sides.Length; k++)
                {
                    Int2 neighbour = cell + Sides[k].Offset();
                    if (!InBounds(map, neighbour)) continue;

                    TerrainKind kind = ground[Index(map, neighbour)];
                    bool open = forTransport ? kind.AllowsBelt() : kind.Walkable();
                    if (!open) continue;

                    int i = Index(map, neighbour);
                    if (seen[i]) continue;

                    seen[i] = true;
                    queue[tail++] = i;
                }
            }

            return seen;
        }

        private static bool CoreInBounds(MapDefinition map)
            => map.CoreCell.X >= 0 && map.CoreCell.Y >= 0 &&
               map.CoreCell.X + CoreState.Size <= map.Width &&
               map.CoreCell.Y + CoreState.Size <= map.Height;

        private static bool InBounds(MapDefinition map, Int2 cell)
            => cell.X >= 0 && cell.Y >= 0 && cell.X < map.Width && cell.Y < map.Height;

        private static int Index(MapDefinition map, Int2 cell) => cell.Y * map.Width + cell.X;

        /// <summary>No cell: a problem about the map as a whole rather than about one tile.</summary>
        private static Int2 NoCell => new Int2(-1, -1);
    }
}
