using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// Issue #19: the stacker is handed over with words. What the build bar says it is for, what a map that hands
    /// it over says, what a mixed demand says beside its figure, what a placement would join, and why a stalled
    /// stacker is stalled - all asserted through <see cref="MachineReading"/>, the interface the HUD formats.
    /// </summary>
    public class StackerExplanationTests
    {
        private static readonly Int2 StackerCell = new Int2(5, 9);
        private static readonly Shape MixedHalf = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

        /// <summary>Belts pointing into (5,9) from the west, north and south, and an outlet east - the same rig the
        /// operator tests use. <paramref name="placeStacker"/> false leaves the cell empty, for the ghost's case.</summary>
        private static SimWorld StackerRig(bool placeStacker, out Int2 west, out Int2 north, out Int2 south)
        {
            SimWorld world = Sim.NewWorld(20, 12);
            west = new Int2(4, 9);
            north = new Int2(5, 10);
            south = new Int2(5, 8);

            Sim.LayRun(world, west, Dir.East, 1);
            Sim.LayRun(world, north, Dir.South, 1);
            Sim.LayRun(world, south, Dir.North, 1);
            if (placeStacker) Sim.Place(world, BuildKind.Stacker, StackerCell, Dir.East);
            Sim.LayRun(world, new Int2(6, 9), Dir.East, 1);
            return world;
        }

        private static ReadingNote CardOf(SimWorld world)
        {
            Assert.IsTrue(world.Machines.TryGetSnapshot(StackerCell, out MachineSnapshot snapshot));
            return MachineReading.LiveNote(world, StackerCell, in snapshot);
        }

        // ---------------------------------------------------------------- the words

        [Test]
        public void TheBlurb_NamesAConcreteExample_NotJustTheRule()
        {
            SimWorld world = Sim.NewWorld(10, 10);
            string blurb = MachineReading.Blurb(world.Content.Machine(BuildKind.Stacker), world.Content);

            StringAssert.Contains("circle", blurb);
            StringAssert.Contains("square", blurb);
            StringAssert.Contains("one", blurb);
            StringAssert.Contains("overlap", blurb, "and it still states the rule");
        }

        [Test]
        public void EveryShippedSentence_IsAsciiPlusTheMiddleDot()
        {
            // The built player's font has no other glyph, and a missing one is a gap on screen.
            SimWorld world = Sim.NewWorld(10, 10);
            string[] said =
            {
                MachineReading.Blurb(world.Content.Machine(BuildKind.Stacker), world.Content),
                MachineReading.Introduction(Operators.Cutter),
                MachineReading.Introduction(Operators.Rotator),
                MachineReading.Introduction(Operators.Stacker),
                MachineReading.DemandNote(MixedHalf),
            };

            foreach (string line in said)
                foreach (char c in line)
                    Assert.IsTrue(c < 128 || c == '·', "'" + c + "' in: " + line);
        }

        [Test]
        public void AMapThatAddsTheStacker_IntroducesExactlyTheStacker()
        {
            MapDefinition quarry = MapWith(Operators.Cutter | Operators.Rotator);
            MapDefinition brickyard = MapWith(Operators.All);

            Assert.AreEqual(Operators.Stacker, MachineReading.NewOperators(brickyard, quarry));
            Assert.AreEqual(Operators.None, MachineReading.NewOperators(quarry, quarry), "nothing new, nothing said");
            Assert.AreEqual(Operators.Cutter | Operators.Rotator, MachineReading.NewOperators(quarry, null),
                "the first map hands over everything it has");

            StringAssert.Contains("Stacker", MachineReading.Introduction(Operators.Stacker));
            StringAssert.Contains("circle half and a square half", MachineReading.Introduction(Operators.Stacker));

            // The HUD card prints these unwrapped, one line each.
            foreach (Operators op in new[] { Operators.Cutter, Operators.Rotator, Operators.Stacker })
                Assert.LessOrEqual(MachineReading.Introduction(op).Length, 58, op + " runs off the card");
        }

        [Test]
        public void OnlyAMixedDemand_CarriesTheStackerLine()
        {
            Assert.IsTrue(MachineReading.IsMixed(MixedHalf));
            StringAssert.Contains("Stacker", MachineReading.DemandNote(MixedHalf));

            Assert.IsFalse(MachineReading.IsMixed(Sim.TopHalfCircle));
            Assert.IsFalse(MachineReading.IsMixed(Sim.Square));
            Assert.IsNull(MachineReading.DemandNote(Sim.TopHalfCircle), "a one-mineral round needs no extra word");
            Assert.IsNull(MachineReading.DemandNote(Shape.None));
        }

        // ---------------------------------------------------------------- the preview

        [Test]
        public void TheGhost_PreviewsTheRoundTwoIncomingPiecesWouldMake()
        {
            SimWorld world = StackerRig(placeStacker: false, out Int2 west, out Int2 north, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.BottomHalfSquare));
            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.TopHalfCircle));

            Assert.IsTrue(MachineReading.TryPreviewStack(world, StackerCell, out Shape a, out Shape b, out Shape union));
            Assert.AreEqual(MixedHalf, union, "a circle top half and a square bottom half make the mixed half");
            Assert.IsTrue((a == Sim.TopHalfCircle && b == Sim.BottomHalfSquare) ||
                          (a == Sim.BottomHalfSquare && b == Sim.TopHalfCircle), "and it names the two pieces");
        }

        [Test]
        public void TheGhost_PreviewsNothing_ForAnOverlappingPairOrASinglePiece()
        {
            SimWorld world = StackerRig(placeStacker: false, out Int2 west, out Int2 north, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.TopHalfSquare));
            Assert.IsFalse(MachineReading.TryPreviewStack(world, StackerCell, out _, out _, out _),
                "one piece is not a pair");

            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.TopHalfCircle));
            Assert.IsFalse(MachineReading.TryPreviewStack(world, StackerCell, out _, out _, out _),
                "two tops cover the same side - the machine would wait, so the ghost promises nothing");
        }

        [Test]
        public void TheGhostsPreview_IsWhatTheMachineActuallyMakes()
        {
            SimWorld world = StackerRig(placeStacker: false, out Int2 west, out Int2 north, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.BottomHalfSquare));
            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.TopHalfCircle));
            Assert.IsTrue(MachineReading.TryPreviewStack(world, StackerCell, out _, out _, out Shape promised));

            Sim.Place(world, BuildKind.Stacker, StackerCell, Dir.East);
            for (int t = 0; t < 30 * 6 && Sim.ShapeAt(world, new Int2(6, 9)).IsEmpty; t++) world.Tick(InputCommand.None);

            Assert.AreEqual(promised, Sim.ShapeAt(world, new Int2(6, 9)), "the preview and the tick agree");
        }

        // ---------------------------------------------------------------- the card

        [Test]
        public void AStalledStacker_SaysWhy_AndWhatToFeedIt()
        {
            SimWorld world = StackerRig(placeStacker: true, out Int2 west, out Int2 north, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.TopHalfCircle));
            for (int t = 0; t < 30 * 3 && Sim.MachineAt(world, StackerCell).Carried.IsEmpty; t++) world.Tick(InputCommand.None);

            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.TopHalfSquare));
            Sim.Tick(world, 30 * 2);

            ReadingNote note = CardOf(world);
            Assert.AreEqual(NoteSeverity.Warn, note.Severity, "a stall is a problem the player must see: " + note.Text);
            StringAssert.Contains("stalled", note.Text);
            StringAssert.Contains("overlaps", note.Text);
            StringAssert.Contains("other half", note.Text, "and it says the fix");
        }

        [Test]
        public void AStackerHoldingOnePiece_WithNothingWaiting_IsNotCalledStalled()
        {
            SimWorld world = StackerRig(placeStacker: true, out Int2 west, out _, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.TopHalfCircle));
            for (int t = 0; t < 30 * 3 && Sim.MachineAt(world, StackerCell).Carried.IsEmpty; t++) world.Tick(InputCommand.None);

            ReadingNote note = CardOf(world);
            Assert.AreEqual(NoteSeverity.Neutral, note.Severity, note.Text);
            StringAssert.Contains("other half", note.Text);
        }

        [Test]
        public void AJoiningStacker_NamesWhatComesOut()
        {
            SimWorld world = StackerRig(placeStacker: true, out Int2 west, out Int2 north, out _);
            Assert.IsTrue(world.Belts.TrySpawnItem(west, Sim.BottomHalfSquare));
            Assert.IsTrue(world.Belts.TrySpawnItem(north, Sim.TopHalfCircle));

            for (int t = 0; t < 30 * 4; t++)
            {
                MachineState m = Sim.MachineAt(world, StackerCell);
                if (!m.Carried.IsEmpty && !m.Carried2.IsEmpty) break;
                world.Tick(InputCommand.None);
            }

            ReadingNote note = CardOf(world);
            StringAssert.Contains("into " + MixedHalf.Describe(), note.Text);
        }

        private static MapDefinition MapWith(Operators unlocked)
            => new MapDefinition("t", 10, 10, 0, new ShapePatch[0], new Int2[0], new WaveDefinition[0],
                unlocked: unlocked);
    }
}
