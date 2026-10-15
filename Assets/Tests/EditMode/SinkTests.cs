using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the drain: the one machine that takes and destroys, and the ending every fork
    /// needs. The two that matter most are
    /// <see cref="AForkWhoseSurplusHasNowhereToGo_StallsTheCutter_AndTheBeltSaysSo"/> and
    /// <see cref="TheSameFork_WithASinkOnTheSurplusLane_KeepsCutting"/>: one rig, one machine added between
    /// them, and between them the whole reason the sink exists. If they go red, the first layout the
    /// campaign teaches has no ending again.
    /// </summary>
    public class SinkTests
    {
        /// <summary>
        /// The rig both headline tests run on, on a 20x12 map (Core at (8,4)-(11,7)).
        ///
        /// A drill at (3,6) pushes onto the belt at (4,6), which feeds the <em>back</em> of a cutter flowing
        /// east at (5,6) - so the machine cuts top from bottom, the top half is the piece on its left and
        /// keeps the fed lane, and the bottom half leaves a lane over to (5,5)'s front. The top half then has
        /// the Core as its customer, two belts away, and the Core eats anything (it banks the complete shapes
        /// and destroys the rest), which makes it the one drain that can never back up. The surplus lane is the
        /// one under test: a belt at (6,5) that runs into bare ground, or into a sink.
        /// </summary>
        private static void BuildFork(SimWorld world, bool sink)
        {
            world.Terrain.SetOre(new Int2(3, 6), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(3, 6), Dir.East);

            Sim.LayRun(world, new Int2(4, 6), Dir.East, 1);                    // into the cutter's back
            Sim.Place(world, BuildKind.Cutter, new Int2(5, 6), Dir.East);     // lane tile (5,5)

            Sim.LayRun(world, new Int2(6, 6), Dir.East, 2);                   // (6,6) (7,6), into the Core
            Sim.LayRun(world, new Int2(6, 5), Dir.East, 1);                   // the surplus lane, and its end
            if (sink) Sim.Place(world, BuildKind.Sink, new Int2(7, 5), Dir.East);
        }

        /// <summary>
        /// How many times the drill's own belt handed its item on in <paramref name="ticks"/> - the honest
        /// measure of whether a line is still running, taken where the factory's first machine can see it.
        /// An occupied cell that never empties is a line that has stopped, whatever any machine's card says.
        /// </summary>
        private static int HandoffsOver(SimWorld world, Int2 cell, int ticks)
        {
            int handoffs = 0;
            bool occupied = world.Belts.HasItemAt(cell);

            for (int i = 0; i < ticks; i++)
            {
                Sim.Tick(world, 1);
                bool now = world.Belts.HasItemAt(cell);
                if (occupied && !now) handoffs++;
                occupied = now;
            }

            return handoffs;
        }

        /// <summary>
        /// The failure the drain was built for, and the one a player meets before they have ever heard of a
        /// sink: the fork's second lane ends in the air, so its four-piece buffer fills and the machine that
        /// made them stops - which stops the lane that <em>was</em> being used, three tiles upstream. The
        /// belt says where: the one cell of the line that is parked at its exit edge with nothing in front
        /// of it.
        /// </summary>
        [Test]
        public void AForkWhoseSurplusHasNowhereToGo_StallsTheCutter_AndTheBeltSaysSo()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            BuildFork(world, sink: false);

            Sim.Tick(world, 300);   // ten seconds: the drill makes ten, and the cutter would cut six pairs

            Assert.IsTrue(world.Belts.IsBlocked(new Int2(6, 5)),
                "the surplus lane ends in the air, and the segment that ends it is the one marked");
            Assert.AreEqual(1, world.Belts.BlockedCount, "one dead end, one marker");

            // And the fork has stopped: its second lane is holding pieces nobody takes.
            Assert.Greater(Sim.MachineAt(world, new Int2(5, 6)).OutputCount2, 0,
                "the cutter is holding the half nobody wants, and cannot make another pair");
            Assert.AreEqual(0, HandoffsOver(world, new Int2(4, 6), 150),
                "so the line is dead: five seconds pass and the drill's belt never empties once");
        }

        /// <summary>
        /// The same rig with one machine added. Nothing else moves: the surplus is destroyed as it is made,
        /// the fork keeps cutting, and the line the player cares about keeps running.
        /// </summary>
        [Test]
        public void TheSameFork_WithASinkOnTheSurplusLane_KeepsCutting()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            BuildFork(world, sink: true);

            Sim.Tick(world, 300);

            MachineState sink = Sim.MachineAt(world, new Int2(7, 5));
            Assert.GreaterOrEqual(sink.Swallowed, 4,
                "the drain ate the surplus as it arrived, one a second of drilling");
            Assert.AreEqual(0, world.Belts.BlockedCount, "nothing on this map has nowhere to go");
            Assert.AreEqual(0, world.Belts.JamCount);

            Assert.GreaterOrEqual(HandoffsOver(world, new Int2(4, 6), 150), 3,
                "and five seconds later the drill is still handing its items on, one every second and a half");
        }

        /// <summary>
        /// A sink eats from every side at once - the one machine with no facing, no intake of its own and no
        /// outlet, so a player may drop it anywhere a belt points at it and never aim it first.
        /// </summary>
        [Test]
        public void ASink_EatsFromEverySide()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            var centre = new Int2(4, 2);                 // clear of the Core at (6,3)-(9,6)
            Sim.Place(world, BuildKind.Sink, centre, Dir.East);

            foreach (Dir side in new[] { Dir.North, Dir.East, Dir.South, Dir.West })
            {
                Int2 belt = centre - side.Offset();      // the cell on that side...
                Sim.LayRun(world, belt, side, 1);        // ...facing into the sink
                Assert.IsTrue(world.Belts.TrySpawnItem(belt, Sim.Circle));
            }

            // A sink eats one item a tick, so the four arrive together and leave one after another.
            Sim.Tick(world, 35);

            Assert.AreEqual(4, Sim.MachineAt(world, centre).Swallowed, "one item in from each of the four sides");
            foreach (Dir side in new[] { Dir.North, Dir.East, Dir.South, Dir.West })
                Assert.IsFalse(world.Belts.HasItemAt(centre - side.Offset()), side + " was taken");
        }

        /// <summary>
        /// A jammed segment is eaten like any other, and the jam goes with the item - which is what makes a
        /// sink laid at the end of a stuck line unstick it without a right-click.
        ///
        /// This is the one place the drain's intake differs from every other machine's: a machine that
        /// carries what it takes must leave a jammed item alone, or it would move the mistake the player is
        /// meant to clear. The splitter is that machine, and the contrast is asserted here so the rule has
        /// a witness rather than a comment.
        /// </summary>
        [Test]
        public void ASink_TakesAJammedItemAndTheJamWithIt_WhereACarryingMachineMayNot()
        {
            SimWorld world = Sim.NewWorld(20, 12);

            // A stuck segment in front of a sink: the sink eats it and the segment is clear. The item has to
            // have reached the exit edge first - a jam freezes an item where it stands, and a delivery a
            // machine can actually refuse is always one that arrived.
            Sim.LayRun(world, new Int2(2, 2), Dir.East, 1);
            world.Belts.TrySpawnItem(new Int2(2, 2), Sim.Circle);
            Sim.Tick(world, 31);
            Assert.IsTrue(world.Belts.IsBlocked(new Int2(2, 2)), "nothing is there yet to take it");

            world.Belts.TryJam(new Int2(2, 2));
            Assert.AreEqual(1, world.Belts.JamCount);

            Sim.Place(world, BuildKind.Sink, new Int2(3, 2), Dir.East);
            Sim.Tick(world, 2);

            Assert.AreEqual(0, world.Belts.JamCount, "the drain takes the jam with the item");
            Assert.IsFalse(world.Belts.IsJammed(new Int2(2, 2)));
            Assert.IsFalse(world.Belts.HasItemAt(new Int2(2, 2)));
            Assert.IsFalse(world.Belts.IsBlocked(new Int2(2, 2)), "and the dead end is a destination now");
            Assert.AreEqual(1, Sim.MachineAt(world, new Int2(3, 2)).Swallowed);

            // The same stuck segment in front of a hub that carries items elsewhere: it stays jammed, and
            // stays for the right-click.
            Sim.LayRun(world, new Int2(2, 8), Dir.East, 1);
            world.Belts.TrySpawnItem(new Int2(2, 8), Sim.Circle);
            Sim.Tick(world, 31);
            world.Belts.TryJam(new Int2(2, 8));
            Sim.Place(world, BuildKind.Splitter, new Int2(3, 8), Dir.East);

            Sim.Tick(world, 2);

            Assert.AreEqual(1, world.Belts.JamCount, "a carrying machine may not move the mistake past the fix");
            Assert.IsTrue(world.Belts.HasItemAt(new Int2(2, 8)));
            Assert.IsTrue(Sim.MachineAt(world, new Int2(3, 8)).Carried.IsEmpty);
        }

        /// <summary>
        /// The row itself: a 1x1 building that eats anything, costs less than the machine it rescues, needs
        /// no unlock - a fix the campaign has not handed over is the same as no fix, and the cutter's first
        /// lesson is also the first way to get stuck - and is filed under the catalogue's own word for it.
        /// </summary>
        [Test]
        public void TheDrain_NeedsNoUnlock_EatsAnything_AndIsFiledUnderWaste()
        {
            ContentDatabase content = ContentDatabase.Default;
            MachineDef sink = content.Machine(BuildKind.Sink);

            Assert.AreEqual(BuildKind.Sink, sink.Build);
            Assert.AreEqual(TileKind.Sink, sink.Tile);
            Assert.AreEqual(BehaviorKind.Sink, sink.Behavior);
            Assert.AreEqual(Operators.None, sink.Requires, "no operator gates the drain");
            Assert.Less(sink.Cost, content.Machine(BuildKind.Cutter).Cost,
                "it has to be cheaper than the machine it rescues");
            Assert.AreEqual(FilterMode.Any, sink.Diet.Mode, "it eats what a carrying machine would refuse");
            Assert.IsTrue(sink.Diet.Matches(Sim.TopHalfCircle));
            Assert.IsTrue(sink.Diet.Matches(Sim.Composite));
            Assert.IsFalse(sink.UsesFacing, "it eats from every side, so there is no facing for an arrow to name");

            Assert.AreEqual(MachineRole.Waste, MachineReading.Role(sink));
            Assert.AreEqual("WASTE", MachineReading.RoleLabel(MachineRole.Waste));
            Assert.DoesNotThrow(() => MachineReading.Blurb(sink, content));
            Assert.DoesNotThrow(() => MachineReading.Stats(sink, content, new SimConfig()));

            var place = new SimWorld(TestBareMap(), new SimConfig());
            place.TryPlace(BuildKind.Sink, new Int2(1, 1), Dir.East);
            Assert.AreEqual(Shape.None, MachineReading.Badge(Sim.MachineAt(place, new Int2(1, 1)), sink),
                "a sink holds nothing, so it can wear no badge");
        }

        /// <summary>The smallest world a machine can stand in, for the tests that need a placed one.</summary>
        private static MapDefinition TestBareMap()
            => new MapDefinition("bare", 16, 10, 40, new ShapePatch[0], new Int2[0], new WaveDefinition[0]);

        /// <summary>
        /// And the one claim the ladder makes about it: a map that has handed over no operator at all still
        /// lets a player build the drain. Map 1 hands over the cutter and nothing else, which is exactly the
        /// map on which the surplus problem is first met.
        /// </summary>
        [Test]
        public void OnAMapThatHasHandedOverNoOperators_TheDrainIsStillBuildable()
        {
            var map = new MapDefinition("bare", 16, 10, 40, new ShapePatch[0], new Int2[0],
                new WaveDefinition[0], unlocked: Operators.None);
            var world = new SimWorld(map, new SimConfig());

            Assert.IsFalse(world.IsUnlocked(BuildKind.Rotator), "this map has handed over nothing");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Cutter));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Sink));
            Assert.IsTrue(world.TryPlace(BuildKind.Sink, new Int2(4, 4), Dir.East));
        }

        /// <summary>
        /// The reading a player gets from the drain: nothing to say when it is unfed - a drain with nothing
        /// to drain is not a fault, and a note that warned there would be a note that cried wolf - and the
        /// one number that tells a working sink from an idle one, which is otherwise impossible to see on a
        /// map, since both look exactly like a hole in the ground.
        /// </summary>
        [Test]
        public void TheCard_SaysHowMuchItHasEaten_AndNeverWarns()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Sim.LayRun(world, new Int2(2, 5), Dir.East, 1);
            Sim.Place(world, BuildKind.Sink, new Int2(3, 5), Dir.East);
            var cell = new Int2(3, 5);

            Assert.AreEqual(NoteSeverity.Neutral, LiveNote(world, cell).Severity, "an empty drain is not a problem");
            StringAssert.Contains("nothing is delivering", LiveNote(world, cell).Text);

            world.Belts.TrySpawnItem(new Int2(2, 5), Sim.Circle);
            Sim.Tick(world, 31);

            Assert.AreEqual(1, Sim.MachineAt(world, cell).Swallowed);
            StringAssert.Contains("1", LiveNote(world, cell).Text,
                "what it has eaten is the whole of its state");
            Assert.AreEqual(NoteSeverity.Neutral, LiveNote(world, cell).Severity);
        }

        private static ReadingNote LiveNote(SimWorld world, Int2 cell)
        {
            Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot machine));
            return MachineReading.LiveNote(world, cell, in machine);
        }
    }
}
