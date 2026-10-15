using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>Shared scaffolding for the EditMode tests. Deliberately tiny - the simulation needs no
    /// scene, Play mode or fixtures, so these are just conveniences.
    ///
    /// The item model is the shape algebra now, so the old <c>ShapeType</c> names live here as the exact
    /// items they always meant: a test reads "a half-circle" instead of spelling four quadrants, and a
    /// test that means the <em>top</em> half says so. Every one of them is a plain <see cref="Shape"/>, so
    /// nothing here is a second vocabulary - <see cref="Shape.Describe"/> remains the only prose.</summary>
    internal static class Sim
    {
        public const float Tol = 1e-4f;

        /// <summary>Tolerance for comparing a belt item's progress against a computed value.</summary>
        public const float ProgressTol = 1e-3f;

        /// <summary>The stockpile a unit-test world starts with: large enough that only economy tests
        /// ever have to think about the economy.</summary>
        public const int TestCircles = 100000;

        // ---------------------------------------------------------------- the design's named items
        //
        // The five names the retired ShapeType had, spelled as the quadrants they always were. A half is
        // an orientation now, not a value, so the top and bottom halves are two different items and the
        // rotator's whole job is moving between them - which is exactly why these are spelled out rather
        // than derived by a helper that could quietly change which way up it means.

        public static readonly Shape Circle = Shape.Full(Quad.Circle);
        public static readonly Shape Square = Shape.Full(Quad.Square);

        /// <summary>The top half of a circle: the piece a horizontal cut of a full circle puts above the
        /// line, and what a drill's circle becomes after one cut.</summary>
        public static readonly Shape TopHalfCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);

        public static readonly Shape BottomHalfCircle = Shape.Of(Quad.Empty, Quad.Empty, Quad.Circle, Quad.Circle);

        public static readonly Shape LeftHalfCircle = Shape.Of(Quad.Circle, Quad.Empty, Quad.Empty, Quad.Circle);

        public static readonly Shape RightHalfCircle = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);

        public static readonly Shape TopHalfSquare = Shape.Of(Quad.Square, Quad.Square, Quad.Empty, Quad.Empty);

        public static readonly Shape BottomHalfSquare = Shape.Of(Quad.Empty, Quad.Empty, Quad.Square, Quad.Square);

        public static readonly Shape LeftHalfSquare = Shape.Of(Quad.Square, Quad.Empty, Quad.Empty, Quad.Square);

        public static readonly Shape RightHalfSquare = Shape.Of(Quad.Empty, Quad.Square, Quad.Square, Quad.Empty);

        /// <summary>The old composite, as the quadrant content it was: a circle above the line and a
        /// square below it. It is one of the eighty, not a wildcard - see
        /// <see cref="Shape.Overlaps"/> for why "contains both minerals" stopped meaning "hits everything".</summary>
        public static readonly Shape Composite = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

        /// <summary>A featureless map of the given size: no patches, spawn points or waves, and
        /// <paramref name="startCircles"/> banked. Unit tests draw their own terrain on it.</summary>
        public static MapDefinition TestMap(int width, int height, int startCircles = TestCircles)
            => new MapDefinition("test", width, height, startCircles,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0]);

        /// <summary>
        /// The rig every test that needs a real map fights on, written here rather than read from a shipped
        /// one: the game's maps are designer-repainted data, so asserting *their* ground, doors or waves would
        /// test today's map file rather than the simulation. A corridor down the north side (rock at (4,10)
        /// and (6,10), each eleven tiles tall) makes a wave walk a straight lane into the Core's north face.
        /// Two circle veins: one beside the Core for the economy line, one feeding the operator chain a
        /// player actually builds - drill &gt; cutter &gt; turret - whose gun sits four tiles off the lane,
        /// inside its 7-tile range and outside the 2.5 tiles a Runner notices a machine from. Two Runners a
        /// wave, two waves a table, and a budget that pays for the opening: the whole opening costs 64
        /// (14 for the economy line, 50 for the defence), so 80 leaves a little to spare.
        ///
        /// The wave's demand is the <em>top</em> half-circle, which is the piece the rig's cutter sends out of
        /// the lane tile's front, a lane over from the tile the belt fed: a group's weakness is one exact item
        /// now, so the rig has to name the item its own line produces or the gun would be firing resisted
        /// rounds all run.
        /// </summary>
        public static MapDefinition RigMap()
            => new MapDefinition("rig", 16, 22, 80,
                new[]
                {
                    new ShapePatch(new Int2(12, 6), 2, 1, Quad.Circle),
                    new ShapePatch(new Int2(12, 9), 2, 1, Quad.Circle),
                },
                new[] { new Int2(5, 20) },
                new[]
                {
                    new WaveDefinition(60f, new SpawnGroup(EnemyKind.Runner, 2, 0, 0f, 2f, TopHalfCircle)),
                    new WaveDefinition(30f, new SpawnGroup(EnemyKind.Runner, 2, 0, 0f, 2f, TopHalfCircle)),
                },
                TerrainLayout.Rects(16, 22,
                    new TerrainRect(new Int2(4, 10), 1, 11, TerrainKind.Rock),
                    new TerrainRect(new Int2(6, 10), 1, 11, TerrainKind.Rock)),
                coreCell: new Int2(4, 6));

        /// <summary>A world on <see cref="RigMap"/> - what the older tests took from the shipped campaign.</summary>
        public static SimWorld Rig() => new SimWorld(RigMap(), new SimConfig());

        /// <summary>
        /// The rig's economy line: a drill on the Core-side vein at (12,6) pushing west onto four belts that
        /// run into the Core's east side - fourteen circles, four of them the line home.
        /// </summary>
        public static void BuildRigEconomy(SimWorld world)
        {
            Place(world, BuildKind.Drill, new Int2(12, 6), Dir.West);
            LayRun(world, new Int2(11, 6), Dir.West, 4);          // (11,6) .. (8,6), into the Core
        }

        /// <summary>
        /// The rig's defence: a drill on the second vein at (12,9) pushing west onto one belt into the back of
        /// a <b>cutter</b> at (10,9) that flows west, so the cut runs top from bottom and both pieces leave by
        /// the front of their own tile. The displaced top piece turns up the map and then east into the
        /// <b>turret</b> at (9,11) - four tiles from the lane's column - and the piece that keeps the lane
        /// leaves onto a dead-ended belt, a visible queue rather than a lost half. <paramref name="trunk"/> is
        /// the input run's first cell, where a jam goes in.
        /// </summary>
        public static void BuildRigDefence(SimWorld world, out Int2 trunk)
        {
            Place(world, BuildKind.Drill, new Int2(12, 9), Dir.West);
            Int2 runStart = new Int2(11, 9);
            LayRun(world, runStart, Dir.West, 1);                 // (11,9), into the cutter's back
            trunk = runStart;

            Place(world, BuildKind.Cutter, new Int2(10, 9), Dir.West);
            LayRun(world, new Int2(9, 10), Dir.West, 1);          // the top half out of the lane tile's front
            LayRun(world, new Int2(8, 10), Dir.North, 1);         // one belt on, turning up the map
            LayRun(world, new Int2(8, 11), Dir.East, 1);          // and into the gun
            LayRun(world, new Int2(9, 9), Dir.West, 1);           // the bottom half's own dead end
            Place(world, BuildKind.Turret, new Int2(9, 11), Dir.West);
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

        /// <summary>A world whose Core cannot die, for tests about turrets and supply rather than the run
        /// ending: an enemy reaching the Core would otherwise end the run and stop the simulation
        /// mid-measurement.</summary>
        public static SimWorld NewEnduringWorld(int width = 20, int height = 12)
            => new SimWorld(TestMap(width, height), new SimConfig { CoreMaxHp = 1000000f });

        /// <summary>
        /// The minimal rig that can actually end: a 20x12 map (Core at (8,4)-(11,7)) whose one wave sends a
        /// single Runner in from (13,10), answered by the one universal turret at (10,10) fed by a belt at
        /// (9,10) that a test stocks each tick. The Runner dies, the wave clears, the table is exhausted,
        /// and the run ends - what the win and the clock's-last-tick tests need without the campaign's
        /// longer maps. It has already built by the time any tick runs (so the clock is armed) and its
        /// opening countdown is 0, so the wave starts itself on the first tick; a press of N is harmless
        /// but redundant. A test of the Held-to-press transition needs a rig that builds nothing at all.
        ///
        /// The group's demand is a top half-circle, which is what <see cref="FeedOneWaveGun"/> loads: six
        /// health against four matched damage is the two shots this rig has always taken, where a resisted
        /// round would take eight.
        /// </summary>
        public static SimWorld OneWaveWorld()
        {
            var map = new MapDefinition("one wave", 20, 12, TestCircles,
                new ShapePatch[0],
                new[] { new Int2(13, 10) },
                new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, 1, 0, 1f, 1f, TopHalfCircle)) });
            var world = new SimWorld(map, new SimConfig());

            LayRun(world, new Int2(9, 10), Dir.East, 1);
            Place(world, BuildKind.Turret, new Int2(10, 10), Dir.East);
            return world;
        }

        /// <summary>Keep <see cref="OneWaveWorld"/>'s turret fed: one top half-circle on the belt's single
        /// cell each tick - the exact item the wave is weak to. At <see cref="SimConfig.BeltSpeed"/> of 1
        /// the cell delivers one per second - exactly the gun's ceiling - though a shot still waits out the
        /// cell's first second of fill.</summary>
        public static void FeedOneWaveGun(SimWorld world)
            => world.Belts.TrySpawnItem(new Int2(9, 10), TopHalfCircle);

        public static void Tick(SimWorld world, InputCommand cmd, int count)
        {
            for (int i = 0; i < count; i++) world.Tick(cmd);
        }

        public static void Tick(SimWorld world, int count) => Tick(world, InputCommand.None, count);

        /// <summary>
        /// Press on a cell, drag to another, then lift. Driven through <see cref="Gesture.Drag"/> - the three
        /// ticks the shipped input adapter spells, release edge included - with the selection explicit, since a
        /// command with no selection is the inspect cursor and places nothing.
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
        /// Drag a route, then prove every cell of the Manhattan walk is a belt facing the next cell. The walk
        /// takes the drag's dominant axis first, exactly as the drag does. A route crossing the Core is silently
        /// skipped by the drag (with the direction link dropped across the gap), leaving a line that looks fine
        /// and starves, so tests refuse one. A shape patch is *not* skipped: transport crosses ore.
        /// </summary>
        public static void Route(SimWorld world, Int2 from, Int2 to)
        {
            Drag(world, from, to);

            // Assert against the one statement of the walk rule, not a copy: the drag lays what
            // BeltWalk.Predict predicts, cell by cell and direction by direction.
            List<BeltWalk.Step> steps = BeltWalk.Predict(from, to);
            foreach (BeltWalk.Step step in steps)
            {
                Assert.IsTrue(world.Belts.Has(step.Cell), "route cell " + step.Cell + " was not laid");
                Assert.AreEqual(step.Direction, DirAt(world, step.Cell),
                    "route cell " + step.Cell + " faces the wrong way");
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

        /// <summary>The shape riding a cell, or nothing.</summary>
        public static Shape ShapeAt(SimWorld world, Int2 cell)
            => world.Belts.TryGet(cell, out BeltState state) ? state.Item : Shape.None;

        /// <summary>How many items on the map are exactly this shape - the question a routing or cutting
        /// test asks far more often than "what is on cell X".</summary>
        public static int CountOf(SimWorld world, Shape shape)
        {
            int found = 0;
            foreach (ItemSnapshot item in ItemsIn(world))
                if (item.Shape == shape) found++;

            return found;
        }
    }
}
