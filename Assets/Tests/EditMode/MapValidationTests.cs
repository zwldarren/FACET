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
                patches: new[] { new ShapePatch(ore, 1, 1, Quad.Circle) },
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
        public void Validate_AMapThatHandsItsWavesToTheGenerator_IsNotWaveless()
        {
            // The rework's rule, and the reason a map has a `GeneratedWaves` field at all: a map may author
            // nothing and let the generator brief it, or author a scripted opening and hand the rest over.
            // What is still a mistake is a map with neither - it can never be cleared and never ends. Each
            // map has a vein because the generator needs one to demand from; that rule has its own test.
            var vein = new[] { new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle) };

            var scripted = new MapDefinition("generated", 16, 12, Sim.TestCircles, vein,
                new[] { new Int2(0, 10) }, new WaveDefinition[0], generatedWaves: 4);
            AssertNoProblem(MapValidation.Validate(scripted), MapProblemKind.NoWaves);

            var endless = new MapDefinition("endless", 16, 12, Sim.TestCircles, vein,
                new[] { new Int2(0, 10) }, new WaveDefinition[0], generatedWaves: -1);
            AssertNoProblem(MapValidation.Validate(endless), MapProblemKind.NoWaves);
        }

        [Test]
        public void Validate_AGeneratorWithNoMineralToDemandFrom_IsReported()
        {
            // The generator roots its legal demands at the map's own veins, so a map that mines nothing hands it
            // an empty pool and every generated wave arrives with nothing in it: a bounded run won without a
            // fight, and an endless one that advances through empty waves for ever. The empty-wave rule never
            // sees it, because that rule only ever reads the authored table.
            var noOre = new MapDefinition("no ore", 16, 12, Sim.TestCircles, new ShapePatch[0],
                new[] { new Int2(0, 10) }, new WaveDefinition[0], generatedWaves: 6);

            List<MapProblem> problems = MapValidation.Validate(noOre);

            AssertProblem(problems, MapProblemKind.GeneratorWithoutMineral, new Int2(-1, -1));
            AssertNoProblem(problems, MapProblemKind.NoWaves);

            // One vein is all it takes, and then the rule is quiet.
            var withOre = new MapDefinition("ore", 16, 12, Sim.TestCircles,
                new[] { new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle) },
                new[] { new Int2(0, 10) }, new WaveDefinition[0], generatedWaves: 6);

            AssertNoProblem(MapValidation.Validate(withOre), MapProblemKind.GeneratorWithoutMineral);
        }

        [Test]
        public void Validate_ADemandTheMapCannotBuild_IsTheSameUnwinnableWaveAsNamingNone()
        {
            // The other half of that rule, and the one the item picker leaves open: a group naming a real item
            // this map's minerals and unlocked operators cannot make - a square-bearing round on a map that
            // mines only circles. It is exactly as unwinnable as a group naming nothing, and just as invisible
            // at runtime, which is why the closure the generator keeps its own demands inside is asked about
            // the authored table too.
            var circleVein = new[] { new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle) };

            var unbuildable = new MapDefinition("unbuildable", 16, 12, Sim.TestCircles, circleVein,
                new[] { new Int2(0, 10) },
                new[]
                {
                    new WaveDefinition(0f,
                        new SpawnGroup(EnemyKind.Runner, 3, 0, 0f, 1f, Shape.Full(Quad.Square))),
                });

            List<MapProblem> problems = MapValidation.Validate(unbuildable);

            AssertProblem(problems, MapProblemKind.DemandNotBuildable, new Int2(-1, -1));
            AssertNoProblem(problems, MapProblemKind.GroupWithoutDemand);

            // The same map asking for something it can build is clean, so the rule is about the demand rather
            // than about the map being an unusual one.
            var buildable = new MapDefinition("buildable", 16, 12, Sim.TestCircles, circleVein,
                new[] { new Int2(0, 10) },
                new[]
                {
                    new WaveDefinition(0f,
                        new SpawnGroup(EnemyKind.Runner, 3, 0, 0f, 1f, Sim.TopHalfCircle)),
                });

            AssertNoProblem(MapValidation.Validate(buildable), MapProblemKind.DemandNotBuildable);
        }

        [Test]
        public void Validate_AGroupWithNoDemand_IsAWaveNobodyCanMatch()
        {
            // A group's weakness is one exact item now, and a group that names none can only ever be shot at
            // a quarter damage: a wave nobody can beat rather than a hard one, and invisible at runtime.
            var map = new MapDefinition("no demand", 16, 12, Sim.TestCircles, new ShapePatch[0],
                new[] { new Int2(0, 10) },
                new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, 3, 0, 0f, 1f, Shape.None)) });

            AssertProblem(MapValidation.Validate(map), MapProblemKind.GroupWithoutDemand, new Int2(-1, -1));
        }

        [Test]
        public void Validate_AWaveAtAMissingDoor_AndAnEmptyWave_AreBothReported()
        {
            var map = new MapDefinition("bad waves", 16, 12, 10, new ShapePatch[0],
                new[] { new Int2(0, 10) },
                new[]
                {
                    new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, 4, 7, 0f, 1f, Sim.TopHalfCircle)),
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
                new[] { new ShapePatch(new Int2(15, 11), 3, 3, Quad.Circle) },
                new[] { new Int2(0, 10) }, new[] { Wave(1) });

            AssertProblem(MapValidation.Validate(map), MapProblemKind.PatchOffMap, new Int2(17, 13));
        }

        [Test]
        public void Validate_OreUnderTheCore_IsReportedAsTheCoresDataDisagreeing()
        {
            // The Core keeps its tiles and stamps before the ground, so a vein authored over it is lost
            // silently - exactly the kind of silence this validator exists to break.
            var map = new MapDefinition("vein at the Core", 16, 12, 10,
                new[] { new ShapePatch(new Int2(6, 4), 2, 2, Quad.Circle) },
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
                patches: new[] { new ShapePatch(ore, 2, 2, Quad.Circle) },
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
            => new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, count, 0, 0f, 1f, Sim.TopHalfCircle));

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

        /// <summary>No problem of that kind anywhere: asked as an absence, because the point is that a rule
        /// the map satisfies cannot be broken by the rest of the map.</summary>
        private static void AssertNoProblem(List<MapProblem> problems, MapProblemKind kind)
        {
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == kind)
                    Assert.Fail("unexpected " + kind + " at " + problems[i].Cell + ": " + problems[i].Message);
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
