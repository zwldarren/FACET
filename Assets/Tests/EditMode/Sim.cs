using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

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

        /// <summary>
        /// Where the shipping campaign lives. One path, named once: the tests read the same asset the game
        /// runs on, so a map that ships is a map the tests are held to, and the two cannot drift the way a
        /// fixture and a built game can.
        /// </summary>
        private const string CampaignPath = "Assets/Data/Campaign.asset";

        /// <summary>The shipping campaign, read the way the driver reads it.</summary>
        public static MapCatalog Campaign => MapCatalog.From(AssetDatabase.LoadAssetAtPath<MapSet>(CampaignPath));

        /// <summary>The maps of it, in the order they are played.</summary>
        public static IReadOnlyList<MapDefinition> Maps => Campaign.Maps;

        /// <summary>One map of it by position. Map 1 is the tutorial, so a test that names a map by index
        /// is naming the order the campaign is played in - see CampaignTests for what each one promises.</summary>
        public static MapDefinition Shipped(int index) => Campaign.At(index);

        /// <summary>A featureless map of the given size: no patches, no spawn points, no waves, and
        /// <paramref name="startCircles"/> banked - by default a stockpile that never runs dry. Unit
        /// tests draw their own terrain on it.</summary>
        public static MapDefinition TestMap(int width, int height, int startCircles = TestCircles)
            => new MapDefinition("test", width, height, startCircles,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]);

        /// <summary>A test map world. Unit tests draw their own patches and never see a wave.</summary>
        public static SimWorld NewWorld(int width, int height)
            => new SimWorld(TestMap(width, height), new SimConfig());

        /// <summary>The real map 1, with its real starting stockpile. Economy and scenario tests live here:
        /// what they build is exactly what a player could afford on the map the game opens with.</summary>
        public static SimWorld NewWorld() => new SimWorld(Shipped(0), new SimConfig());

        /// <summary>A world whose Core cannot die, for tests that are about turrets and supply rather
        /// than about the run ending: an enemy that reaches the Core would otherwise end the run and
        /// stop the simulation mid-measurement.</summary>
        public static SimWorld NewEnduringWorld(int width = 20, int height = 12)
            => new SimWorld(TestMap(width, height), new SimConfig { CoreMaxHp = 1000000f });

        public static void Tick(SimWorld world, InputCommand cmd, int count)
        {
            for (int i = 0; i < count; i++) world.Tick(cmd);
        }

        public static void Tick(SimWorld world, int count) => Tick(world, InputCommand.None, count);

        /// <summary>Advance until the stockpile reaches <paramref name="target"/> - the economy
        /// line's deliveries banking at the Core - or fail after <paramref name="maxSeconds"/>.</summary>
        public static void TickUntilFunded(SimWorld world, int target, float maxSeconds = 240f)
        {
            int cap = (int)(maxSeconds * SimConfig.TickRate);
            for (int i = 0; i < cap && world.Economy.Circles < target; i++)
                world.Tick(InputCommand.None);

            Assert.GreaterOrEqual(world.Economy.Circles, target,
                "the economy never banked " + target + " circles");
        }

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
        /// The reference map-1 defence, shared by every test that fights the wave: a drill on the
        /// east patch at (62,23) pushing west, fifteen belts to a decomposer at (46,23), two more
        /// belts to a pipe at (43,23) that jumps whatever sits at (43,22) - bare ground here, the
        /// economy line when both are built - then south and west to a cannon at (37,18) covering
        /// the Core's southern corner, inside range of both the southern approach and the western
        /// flank the wave ends with. The cannon sits three tiles off the south lane on purpose: a
        /// machine two tiles off is inside a Spike's notice range, and the wave would charge it
        /// instead of walking into its fire. The build costs 77 circles against the map's 80, so it
        /// is a build a player can actually afford.
        /// <paramref name="trunk"/> is a cell in the middle of the input run - the one a jam goes in.
        /// </summary>
        public static void BuildMapOneDefence(SimWorld world, out Int2 trunk, out Int2 cannon)
        {
            Place(world, BuildKind.Drill, new Int2(62, 23), Dir.West);
            LayRun(world, new Int2(61, 23), Dir.West, 15);      // (61,23) .. (47,23)

            trunk = new Int2(54, 23);

            Place(world, BuildKind.Decomposer, new Int2(46, 23), Dir.West);
            LayRun(world, new Int2(45, 23), Dir.West, 2);       // (45,23) (44,23)
            Place(world, BuildKind.Pipe, new Int2(43, 23), Dir.South);   // over (43,22), to (43,21)
            LayRun(world, new Int2(43, 21), Dir.South, 3);      // (43,21) .. (43,19)
            LayRun(world, new Int2(43, 18), Dir.West, 1);       // the corner
            LayRun(world, new Int2(42, 18), Dir.West, 5);       // (42,18) .. (38,18), pointing in

            cannon = new Int2(37, 18);
            Place(world, BuildKind.Cannon, cannon, Dir.West);
        }

        /// <summary>The economy line: a drill on the east patch at (62,22) and twenty belts straight
        /// west into the Core, so mined circles bank themselves. Thirty circles of the run's budget.</summary>
        public static void BuildMapOneEconomyLine(SimWorld world)
        {
            Place(world, BuildKind.Drill, new Int2(62, 22), Dir.West);
            LayRun(world, new Int2(61, 22), Dir.West, 20);      // (61,22) .. (42,22), into the Core
        }

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
        /// Run the whole game, starting each wave the moment its intermission begins so the test does
        /// not sit through the countdowns (the countdown has its own test).
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
