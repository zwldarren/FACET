using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>Shared scaffolding for the EditMode tests. Deliberately tiny - the simulation
    /// needs no scene, no Play mode and no fixtures, so these are just conveniences.</summary>
    internal static class Sim
    {
        public const float Tol = 1e-4f;

        /// <summary>Tolerance for comparing a belt item's progress against a computed value.</summary>
        public const float ProgressTol = 1e-3f;

        /// <summary>The stockpile a unit-test world starts with: enough that only tests which are
        /// about the economy itself ever have to think about the economy.</summary>
        public const int TestCircles = 100000;

        /// <summary>A featureless map of the given size: no patches, no spawn points, no waves, and
        /// <paramref name="startCircles"/> banked - by default a stockpile that never runs dry. Unit
        /// tests draw their own terrain on it.</summary>
        public static MapDefinition TestMap(int width, int height, int startCircles = TestCircles)
            => new MapDefinition("test", width, height, startCircles,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]);

        /// <summary>
        /// The rig every test that needs a real map fights on, written here rather than read from a shipped
        /// one: the maps the game plays are data a designer repaints whenever they like, so a test that
        /// asserted *their* ground, doors or waves would be a test of today's map file rather than of the
        /// simulation. This map is the tests' own, it is small, and it is shaped so that a fight on it is
        /// the same fight every time.
        ///
        /// A corridor down the north side - rock at (4,10) and (6,10), each eleven tiles tall - so a wave
        /// walks a straight lane into the Core's north face instead of a diagonal across open ground. One
        /// vein beside the Core for the economy line and one at the mouth of a drill &gt; decomposer &gt;
        /// cannon line, whose gun sits four tiles off that lane: inside its 7-tile range, outside the 2.5
        /// tiles a Spike notices a machine from, which is why nothing charges it. Two Spikes a wave, two
        /// waves a table, and a budget that pays for exactly the opening both lines described below cost.
        /// </summary>
        public static MapDefinition RigMap()
            => new MapDefinition("rig", 16, 22, 62,
                new[]
                {
                    new ShapePatch(new Int2(12, 6), 2, 1, ShapeType.Circle),
                    new ShapePatch(new Int2(12, 9), 2, 1, ShapeType.Circle),
                },
                new[] { new Int2(5, 20) },
                new[]
                {
                    new WaveDefinition(60f, new SpawnGroup(EnemyKind.Spike, 2, 0, 0f, 2f)),
                    new WaveDefinition(30f, new SpawnGroup(EnemyKind.Spike, 2, 0, 0f, 2f)),
                },
                TerrainLayout.Rects(16, 22,
                    new TerrainRect(new Int2(4, 10), 1, 11, TerrainKind.Rock),
                    new TerrainRect(new Int2(6, 10), 1, 11, TerrainKind.Rock)),
                coreCell: new Int2(4, 6));

        /// <summary>A world on <see cref="RigMap"/> - what the older tests took from the shipped campaign.</summary>
        public static SimWorld Rig() => new SimWorld(RigMap(), new SimConfig());

        /// <summary>
        /// The rig's economy line: a drill on the vein beside the Core at (12,6), pushing west onto four
        /// belts that run into the Core's east side - fourteen circles, four of them the line home.
        /// </summary>
        public static void BuildRigEconomy(SimWorld world)
        {
            Place(world, BuildKind.Drill, new Int2(12, 6), Dir.West);
            LayRun(world, new Int2(11, 6), Dir.West, 4);          // (11,6) .. (8,6), into the Core
        }

        /// <summary>
        /// The rig's defence: a drill on the second vein at (12,9) pushing west onto two belts into a
        /// decomposer at (9,9), whose outlet runs north up into a cannon at (9,11) - three tiles from the
        /// lane's cell at (9,10)'s column, four from the lane itself. The cannon faces west so its barrel
        /// points at the Core. <paramref name="trunk"/> is the input run's first cell: the one a jam goes
        /// in. Forty-eight circles, the opening's other half.
        /// </summary>
        public static void BuildRigDefence(SimWorld world, out Int2 trunk)
        {
            Place(world, BuildKind.Drill, new Int2(12, 9), Dir.West);
            Int2 runStart = new Int2(11, 9);
            LayRun(world, runStart, Dir.West, 2);                 // (11,9) (10,9), into the decomposer
            trunk = runStart;

            Place(world, BuildKind.Decomposer, new Int2(9, 9), Dir.East);
            LayRun(world, new Int2(9, 10), Dir.North, 1);         // out of the decomposer, up into the gun
            Place(world, BuildKind.Cannon, new Int2(9, 11), Dir.West);
        }

        /// <summary>The whole opening, the way a player builds it: the economy first, then the defence.</summary>
        public static void BuildRig(SimWorld world, out Int2 trunk)
        {
            BuildRigEconomy(world);
            BuildRigDefence(world, out trunk);
        }

        /// <summary>A test map world. Unit tests draw their own patches and never see a wave.</summary>
        public static SimWorld NewWorld(int width, int height)
            => new SimWorld(TestMap(width, height), new SimConfig());

        /// <summary>A world whose Core cannot die, for tests that are about turrets and supply rather
        /// than about the run ending: an enemy that reaches the Core would otherwise end the run and
        /// stop the simulation mid-measurement.</summary>
        public static SimWorld NewEnduringWorld(int width = 20, int height = 12)
            => new SimWorld(TestMap(width, height), new SimConfig { CoreMaxHp = 1000000f });

        /// <summary>
        /// The minimal rig that can actually end: a 20x12 map (Core at (8,4)-(11,7)) whose one wave
        /// sends a single Spike in from (13,10), answered by a cannon at (10,10) fed by a belt at
        /// (9,10) - which a test stocks by hand each tick. The spike dies at the gun, the wave clears,
        /// and the table is exhausted: the run ends, which is what the tests that need a run to END
        /// (a win, the clock's last tick) need without borrowing the campaign's longer maps. Note the
        /// rig has already built by the time any tick runs - the belt and cannon go through TryPlace,
        /// which arms the clock - and its opening countdown is 0, so the wave starts itself on the
        /// very first tick; a press of N is harmless but redundant here. A test that means to
        /// exercise the Held-to-press transition needs a rig that builds nothing at all.
        /// </summary>
        public static SimWorld OneWaveWorld()
        {
            var map = new MapDefinition("one wave", 20, 12, TestCircles,
                new ShapePatch[0],
                new[] { new Int2(13, 10) },
                new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Spike, 1, 0, 1f, 1f)) });
            var world = new SimWorld(map, new SimConfig());

            LayRun(world, new Int2(9, 10), Dir.East, 1);
            Place(world, BuildKind.Cannon, new Int2(10, 10), Dir.East);
            return world;
        }

        /// <summary>Keep <see cref="OneWaveWorld"/>'s cannon fed: one half-circle dropped on the belt's
        /// single cell each tick. At <see cref="SimConfig.BeltSpeed"/> of 1 the cell delivers one per
        /// second - exactly the gun's ceiling, so the gun is never throughput-starved, though a shot
        /// still waits out the cell's first second of fill.</summary>
        public static void FeedOneWaveGun(SimWorld world)
            => world.Belts.TrySpawnItem(new Int2(9, 10), ShapeType.HalfCircle);

        public static void Tick(SimWorld world, InputCommand cmd, int count)
        {
            for (int i = 0; i < count; i++) world.Tick(cmd);
        }

        public static void Tick(SimWorld world, int count) => Tick(world, InputCommand.None, count);

        /// <summary>
        /// Press the left button on a cell, hold it and drag the cursor to another, then lift. Driven
        /// through <see cref="Gesture.Drag"/> - the same three ticks the shipped input adapter spells
        /// into commands, release edge included - with the selection explicit, because a command with
        /// no selection is the inspect cursor, which places nothing.
        /// </summary>
        public static void Drag(SimWorld world, Int2 from, Int2 to)
        {
            foreach (InputCommand cmd in Gesture.Drag(from, to)) world.Tick(cmd);
        }

        /// <summary>Lay a straight run of belts, asserting every cell was free.</summary>
        public static void LayRun(SimWorld world, Int2 start, Dir direction, int length)
        {
            Int2 cell = start;
            for (int i = 0; i < length; i++)
            {
                Assert.IsTrue(world.TryPlaceBelt(cell, direction), "expected " + cell + " to be free");
                cell = cell + direction.Offset();
            }
        }

        public static void Place(SimWorld world, BuildKind kind, Int2 cell, Dir direction)
            => Assert.IsTrue(world.TryPlace(kind, cell, direction),
                "expected to place " + kind + " at " + cell + " facing " + direction);

        /// <summary>
        /// Drag a route and then prove it landed: every cell of the Manhattan walk must be a belt
        /// facing the next cell of the walk. The walk takes the drag's dominant axis first, exactly as
        /// the drag itself does, so a route up the map is proved cell by cell up the map. A route that
        /// crosses the Core is silently skipped by the drag (and the direction link is dropped across the
        /// gap), which would leave a line that looks fine and starves - so the tests refuse to accept one.
        /// A shape patch is *not* skipped: transport crosses ore, so a route over a vein is one unbroken
        /// run.
        /// </summary>
        public static void Route(SimWorld world, Int2 from, Int2 to)
        {
            Drag(world, from, to);

            // Assert against the one statement of the walk rule, not a copy of it: the drag lays
            // what BeltWalk.Predict predicts, cell by cell and direction by direction.
            List<BeltWalk.Step> steps = BeltWalk.Predict(from, to);
            foreach (BeltWalk.Step step in steps)
            {
                Assert.IsTrue(world.Belts.Has(step.Cell), "route cell " + step.Cell + " was not laid");
                Assert.AreEqual(step.Direction, DirAt(world, step.Cell),
                    "route cell " + step.Cell + " faces the wrong way");
            }
        }

        /// <summary>
        /// Run the whole game, starting each wave the moment its wait begins so the test does not sit
        /// through the countdowns (the countdown has its own tests). The opening's wait counts: it is Held
        /// for a first building, and a press ends it just as a running countdown can be cut short.
        /// </summary>
        public static void RunToEnd(SimWorld world, int maxTicks = 60 * 30 * 12)
        {
            for (int i = 0; i < maxTicks && world.Status == GameStatus.Playing; i++)
            {
                if (world.Waves.HasWaveToStart) world.Waves.StartNextWave();
                world.Tick(InputCommand.None);
            }
        }

        public static Dir DirAt(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Belts.TryGet(cell, out BeltState state), "expected a belt at " + cell);
            return state.Direction;
        }

        public static float ProgressAt(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Belts.TryGet(cell, out BeltState state), "expected a belt at " + cell);
            return state.Progress;
        }

        public static MachineState MachineAt(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Machines.TryGet(cell, out MachineState state), "expected a machine at " + cell);
            return state;
        }

        public static bool ArmedAt(SimWorld world, Int2 cell) => MachineAt(world, cell).Armed;

        public static List<ItemSnapshot> ItemsIn(SimWorld world)
        {
            var items = new List<ItemSnapshot>();
            world.Belts.GetItems(items);
            return items;
        }

        public static int ItemCount(SimWorld world) => ItemsIn(world).Count;

        /// <summary>The shape riding a cell, or None.</summary>
        public static ShapeType ShapeAt(SimWorld world, Int2 cell)
            => world.Belts.TryGet(cell, out BeltState state) ? state.Item : ShapeType.None;
    }
}
