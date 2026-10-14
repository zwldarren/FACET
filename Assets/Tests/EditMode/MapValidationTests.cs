using System.Collections.Generic;
using System.Text;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The rules a map has to satisfy, exercised on maps that deliberately break them. A map is data,
    /// and data mistakes do not throw - they produce a wave standing at its door forever, which reads as
    /// a hung game. Each test is a map wrong in exactly one way, and the validator must name that way
    /// and nothing else. The shipped maps are held to the same rules by the editor window; nothing here
    /// reads a shipped map.
    /// </summary>
    public class MapValidationTests
    {
        [Test]
        public void Validate_ARockWallAcrossALane_IsADoorThatCanNeverArrive()
        {
            // The mistake a canyon map makes: a ridge drawn across the only lane its door has. Nothing
            // throws - the wave simply has nowhere to walk, and this is what says so.
            var door = new Int2(0, 2);
            MapDefinition map = Map(16, 12,
                doors: new[] { door },
                terrain: TerrainLayout.Rects(16, 12,
                    new TerrainRect(new Int2(4, 0), 1, 12, TerrainKind.Rock)));

            List<MapProblem> problems = MapValidation.Validate(map);

            AssertProblem(problems, MapProblemKind.SpawnUnreachable, door);
            Assert.IsEmpty(ProblemsOtherThan(problems, MapProblemKind.SpawnUnreachable),
                "the wall is the only thing wrong with it:\n" + Describe(problems));
        }

        [Test]
        public void Validate_ADoorOnRock_IsAWaveSpawnedInsideTheGround()
        {
            var door = new Int2(2, 2);
            MapDefinition map = Map(16, 12,
                doors: new[] { door },
                terrain: TerrainLayout.Rects(16, 12, new TerrainRect(door, 1, 1, TerrainKind.Rock)));

            AssertProblem(MapValidation.Validate(map), MapProblemKind.SpawnOnTerrain, door);
        }

        [Test]
        public void Validate_OreNoBeltCanReach_IsCutOff()
        {
            // A vein walled in behind a closed ridge: the drill that would mine it can never be fed, and a
            // player would only find that out after buying the drill.
            var ore = new Int2(2, 2);
            MapDefinition map = Map(16, 12,
                doors: new[] { new Int2(0, 10) },
                patches: new[] { new ShapePatch(ore, 1, 1, ShapeType.Circle) },
                terrain: TerrainLayout.Rects(16, 12,
                    new TerrainRect(new Int2(4, 0), 1, 12, TerrainKind.Rock)));

            AssertProblem(MapValidation.Validate(map), MapProblemKind.OreCutOff, ore);
        }

        [Test]
        public void Validate_ATerrainArrayThatDoesNotFitTheMap_IsReported()
        {
            var map = new MapDefinition("short", 8, 4, 10, new ShapePatch[0], new Int2[0],
                new WaveDefinition[0], terrain: new TerrainKind[10]);

            List<MapProblem> problems = MapValidation.Validate(map);
            AssertProblem(problems, MapProblemKind.TerrainSizeMismatch, new Int2(-1, -1));
        }

        [Test]
        public void Validate_ACoreThatDoesNotFit_IsReported_AndStopsTheFloods()
        {
            // The Core is placed at the tile the map names, so a footprint hanging off the grid is a map
            // that cannot be built at all. The floods are seeded from the Core, so without this the same
            // map would also report every door and vein as unreachable - noise around one real problem.
            var map = new MapDefinition("off the edge", 8, 4, 10, new ShapePatch[0],
                new[] { new Int2(0, 0) }, new[] { Wave(1) }, coreCell: new Int2(6, 3));

            List<MapProblem> problems = MapValidation.Validate(map);

            AssertProblem(problems, MapProblemKind.CoreOutOfBounds, new Int2(6, 3));
            Assert.IsEmpty(ProblemsOtherThan(problems, MapProblemKind.CoreOutOfBounds),
                "and nothing else, because nothing else can be known:\n" + Describe(problems));
        }

        [Test]
        public void Validate_AMapWithNoDoorsOrWaves_IsReported()
        {
            // The degenerate map every test helper starts from: fine to run machines on, not a map.
            List<MapProblem> problems = MapValidation.Validate(Sim.TestMap(12, 10));

            AssertProblem(problems, MapProblemKind.NoSpawnPoints, new Int2(-1, -1));
            AssertProblem(problems, MapProblemKind.NoWaves, new Int2(-1, -1));
        }

        [Test]
        public void Validate_AWaveAtAMissingDoor_AndAnEmptyWave_AreBothReported()
        {
            var map = new MapDefinition("bad waves", 16, 12, 10, new ShapePatch[0],
                new[] { new Int2(0, 10) },
                new[]
                {
                    new WaveDefinition(0f, new SpawnGroup(EnemyKind.Spike, 4, 7, 0f, 1f)),
                    new WaveDefinition(30f),
                });

            List<MapProblem> problems = MapValidation.Validate(map);

            AssertProblem(problems, MapProblemKind.GroupAtMissingDoor, new Int2(-1, -1));
            AssertProblem(problems, MapProblemKind.WaveWithoutEnemies, new Int2(-1, -1));
        }

        [Test]
        public void Validate_APatchHangingOffTheMap_IsReported()
        {
            var map = new MapDefinition("overhanging", 16, 12, 10,
                new[] { new ShapePatch(new Int2(15, 11), 3, 3, ShapeType.Circle) },
                new[] { new Int2(0, 10) }, new[] { Wave(1) });

            AssertProblem(MapValidation.Validate(map), MapProblemKind.PatchOffMap, new Int2(17, 13));
        }

        [Test]
        public void Validate_OreUnderTheCore_IsReportedAsTheCoresDataDisagreeing()
        {
            // The Core keeps its tiles and stamps before the ground, so a vein authored over it is lost
            // silently - exactly the kind of silence this validator exists to break.
            var map = new MapDefinition("vein at the Core", 16, 12, 10,
                new[] { new ShapePatch(new Int2(6, 4), 2, 2, ShapeType.Circle) },
                new[] { new Int2(0, 10) }, new[] { Wave(1) },
                terrain: TerrainLayout.Rects(16, 12, new TerrainRect(new Int2(6, 4), 4, 4, TerrainKind.Rock)));

            AssertProblem(MapValidation.Validate(map), MapProblemKind.CoreUnderTerrain, new Int2(6, 4));
        }

        [Test]
        public void Validate_OreInTheTerrainArray_IsReported_WithOrWithoutAVeinBesideIt()
        {
            // A vein is the ore list's entry, and the ground under one is plain - so an array that says
            // ore is a map no writer here produces, and both halves of the mistake get named. With no
            // entry at the cell the world still gets an ore tile that yields nothing; with an entry
            // beside it, the byte is the half-write left behind the moment the entry is removed by hand.
            var ore = new Int2(2, 2);
            MapDefinition orphan = Map(16, 12,
                doors: new[] { new Int2(0, 10) },
                terrain: TerrainLayout.Rects(16, 12, new TerrainRect(ore, 2, 2, TerrainKind.Ore)));

            List<MapProblem> problems = MapValidation.Validate(orphan);

            AssertProblem(problems, MapProblemKind.OreInTerrain, ore);
            Assert.IsEmpty(ProblemsOtherThan(problems, MapProblemKind.OreInTerrain),
                "and nothing else is wrong with it:\n" + Describe(problems));

            MapDefinition paired = Map(16, 12,
                doors: new[] { new Int2(0, 10) },
                patches: new[] { new ShapePatch(ore, 2, 2, ShapeType.Circle) },
                terrain: TerrainLayout.Rects(16, 12, new TerrainRect(ore, 2, 2, TerrainKind.Ore)));

            AssertProblem(MapValidation.Validate(paired), MapProblemKind.OreInTerrain, ore);
        }

        /// <summary>A map with a real door and a real wave, so a test can break exactly one other thing
        /// about it and see one problem come back.</summary>
        private static MapDefinition Map(int width, int height, Int2[] doors,
            ShapePatch[] patches = null, TerrainKind[] terrain = null)
            => new MapDefinition("case", width, height, Sim.TestCircles, patches, doors,
                new[] { Wave(2) }, terrain);

        /// <summary>A wave that walks in at the last door and is otherwise unremarkable.</summary>
        private static WaveDefinition Wave(int count)
            => new WaveDefinition(0f, new SpawnGroup(EnemyKind.Spike, count, 0, 0f, 1f));

        private static void AssertProblem(List<MapProblem> problems, MapProblemKind kind, Int2 cell)
        {
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == kind && problems[i].Cell == cell) return;

            Assert.Fail("expected a " + kind + " problem at " + cell + ", got:\n" + Describe(problems));
        }

        private static List<MapProblem> ProblemsOtherThan(List<MapProblem> problems, MapProblemKind kind)
        {
            var rest = new List<MapProblem>();
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind != kind) rest.Add(problems[i]);

            return rest;
        }

        private static string Describe(List<MapProblem> problems)
        {
            var text = new StringBuilder();
            if (problems.Count == 0) text.Append("  (no problems)");

            for (int i = 0; i < problems.Count; i++)
                text.Append("  ").Append(problems[i].Kind).Append(": ").Append(problems[i].Message).Append('\n');

            return text.ToString();
        }
    }
}
