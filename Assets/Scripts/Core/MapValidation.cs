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
    /// The rules a map has to satisfy to be playable, as one engine-free function - so the Editor's
    /// painting surfaces, the test suite and a map baked out of the campaign are all held to the same
    /// answer.
    ///
    /// The expensive check is the only one worth having: a flood over the grid from the Core's footprint,
    /// asking whether each door can actually be walked from, and whether each vein can actually be
    /// reached by a belt. A map is a set of rectangles, a ground and a wave table, and the mistakes those
    /// make are exactly the ones nothing else catches - a ridge drawn all the way across a lane, a rock
    /// dropped on a door, a patch walled off behind water. None of them throws; they produce a wave that
    /// stands still, which reads as a hung game rather than as a bug.
    ///
    /// It walks the ground through <see cref="MapDefinition.StampTerrain"/> - the same rule
    /// <see cref="TerrainField.Stamp"/> builds a world with - so the map being judged and the map the
    /// simulation will run are the same map, patches and all. Buildings are deliberately not part of it:
    /// a map is validated as the player first sees it, with nothing built. Whether a defence can then
    /// hold is a balance question, not a data error.
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

        /// <summary>The caller-owned-list form. Private, because a validation runs once per map load or
        /// once per editor pass - nobody validates per frame - so no caller has ever needed to reuse its
        /// list, and the allocating overload is the whole public answer.</summary>
        private static void Validate(MapDefinition map, List<MapProblem> into)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (into == null) throw new ArgumentNullException(nameof(into));

            into.Clear();

            CheckPatches(map, into);
            CheckWaves(map, into);
            CheckTerrainSize(map, into);

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
        /// The ground the map really has, as the world would be built from it. Asked rather than
        /// re-derived: this is <see cref="MapDefinition.StampTerrain"/>, which is the same call
        /// <see cref="TerrainField.Stamp"/> makes, so a rule that changes there cannot leave this walking
        /// a map nobody will ever play.
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
        /// either way - but a map whose own data disagrees with where it puts the Core is a map whose
        /// author has lost track of one of the two, and that is worth saying.
        ///
        /// This asks the map's <em>data</em> - <see cref="MapDefinition.AuthoredGroundAt"/>, the map's
        /// own per-cell ground - not the ground the world would be built with: the world zeroes the
        /// Core's tiles after patching, so its ground keeps plain ground under the Core by construction
        /// and asking it here could never find anything. The disagreement is the finding.
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
                if (!patch.Shape.IsShape())
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

        private static void CheckWaves(MapDefinition map, List<MapProblem> into)
        {
            if (map.SpawnPoints.Length == 0)
            {
                into.Add(new MapProblem(MapProblemKind.NoSpawnPoints, NoCell,
                    map.Name + ": no entry points, so no wave can walk in"));
            }

            if (map.Waves.Length == 0)
            {
                into.Add(new MapProblem(MapProblemKind.NoWaves, NoCell,
                    map.Name + ": no waves, so the map can never be cleared"));
            }

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
