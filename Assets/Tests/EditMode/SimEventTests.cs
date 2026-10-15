using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the event stream - the seam every reaction to the game hangs off (a flash, a sound, a
    /// HUD ticker). Two properties matter: the stream says what actually happened (which shape jammed what,
    /// which enemy was hit, which wave ended), and it is <b>derived</b> - nothing in the simulation reads it
    /// back - so it must be a pure function of the run, which the determinism test below pins tick for tick. The
    /// buffer is bounded, so a test reading a specific tick's events clears first (the simulation appends, the
    /// reader clears).
    /// </summary>
    public class SimEventTests
    {
        [Test]
        public void APlacement_ReportsItsCost_AndARemoval_TheRefund()
        {
            SimWorld world = Sim.NewWorld(20, 12);
            world.Events.Clear();

            Sim.Place(world, BuildKind.Splitter, new Int2(2, 2), Dir.East);

            Assert.AreEqual(1, world.Events.Count);
            Assert.AreEqual(SimEventKind.Built, world.Events[0].Kind);
            Assert.AreEqual(new Int2(2, 2), world.Events[0].Cell);
            Assert.AreEqual(world.Economy.CostOf(BuildKind.Splitter), world.Events[0].Amount, Sim.Tol,
                "the splitter's cost, reported");

            world.Events.Clear();
            Assert.IsTrue(world.TryRemoveBuilding(new Int2(2, 2)));

            Assert.AreEqual(1, world.Events.Count);
            Assert.AreEqual(SimEventKind.Removed, world.Events[0].Kind);
            Assert.AreEqual(world.Economy.CostOf(BuildKind.Splitter), world.Events[0].Amount, Sim.Tol,
                "the full refund, reported");
        }

        [Test]
        public void AWrongShapeDelivery_ReportsTheJam_AndTheShapeThatCausedIt()
        {
            // The shipped table jams nothing - a machine's diet is content, and every shipped eating machine
            // eats anything - so the jam rule is proved on a table that narrows one, which is the case it
            // still exists for. Either way the event must name the shape that arrived wrong, or a log line
            // cannot say what went wrong.
            SimWorld world = new SimWorld(Sim.TestMap(20, 12), new SimConfig(),
                CustomContentTests.Rewrite(machine: CustomContentTests.CutterOnADiet(ShapeFilter.Is(Sim.Circle))));

            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.Square));

            world.Events.Clear();
            Sim.Tick(world, 60);

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Jammed));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Jammed, out SimEvent jammed));
            Assert.AreEqual(new Int2(3, 2), jammed.Cell);
            Assert.AreEqual(Sim.Square, jammed.Shape, "the shape that arrived wrong");

            world.Events.Clear();
            Assert.IsTrue(world.Belts.TryClearJam(new Int2(3, 2)));
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.JamCleared));
            Assert.AreEqual(new Int2(3, 2), world.Events[0].Cell);

            // The control, on the shipped table: the same delivery to the same machine is not a jam at all,
            // because the chamber and the cutter both eat anything. The rule did not change - the data did.
            SimWorld shipped = Sim.NewWorld(20, 12);
            Sim.LayRun(shipped, new Int2(3, 2), Dir.East, 1);
            Sim.Place(shipped, BuildKind.Cutter, new Int2(4, 2), Dir.East);
            Assert.IsTrue(shipped.Belts.TrySpawnItem(new Int2(3, 2), Sim.Square));

            shipped.Events.Clear();
            Sim.Tick(shipped, 60);

            Assert.AreEqual(0, shipped.Events.CountOf(SimEventKind.Jammed),
                "nothing in the shipped game is a wrong-shape delivery any more");
        }

        [Test]
        public void ACircleReachingTheCore_ReportsTheBank_AtTheBeltThatDeliveredIt()
        {
            // The Core's footprint in the 20x12 test map is (8,4)-(11,7), so (7,4) pointing east is a delivery:
            // the tick banks it and the stream says so the same tick.
            SimWorld world = Sim.NewWorld(20, 12);
            Sim.LayRun(world, new Int2(7, 4), Dir.East, 1);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(7, 4), Sim.Circle));
            int before = world.Economy.Circles;   // read after the belt was paid for

            world.Events.Clear();
            Sim.Tick(world, 60);

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Banked));
            Assert.IsTrue(world.Events.TryLast(SimEventKind.Banked, out SimEvent banked));
            Assert.AreEqual(new Int2(7, 4), banked.Cell);
            Assert.AreEqual(Sim.Circle, banked.Shape);
            Assert.AreEqual(before + 1, world.Economy.Circles, "and the stockpile moved by one");
        }

        [Test]
        public void AFiredShot_ReportsItsAmmo_AndItsHit_ReportsTheEnemy()
        {
            // One turret, two top half-circles, one 6-HP Runner demanding exactly that item: the first
            // matched shot damages, the second kills - so the stream has to carry a fired shot, a damage and
            // a kill, in that order.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Turret, new Int2(4, 2), Dir.East);

            int id = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(6.5f, 2.5f), Sim.TopHalfCircle);
            Assert.Greater(id, 0);

            world.Events.Clear();

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.TopHalfCircle));
            Sim.Tick(world, 60);

            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(3, 2), Sim.TopHalfCircle));
            Sim.Tick(world, 60);

            Assert.AreEqual(2, world.Events.CountOf(SimEventKind.ShotFired), "two shots, two reports");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.ShotFired, out SimEvent shot));
            Assert.AreEqual(new Int2(4, 2), shot.Cell, "reported at the turret that fired");
            Assert.AreEqual(Sim.TopHalfCircle, shot.Shape, "and with the ammunition it spent");
            Assert.AreEqual(world.Content.Turret(BuildKind.Turret).Damage, shot.Amount, Sim.Tol,
                "the report carries the damage the round is worth, read off the table");

            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyDamaged), "the first shot hurt it");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.EnemyKilled), "the second killed it");

            Assert.IsTrue(world.Events.TryLast(SimEventKind.EnemyKilled, out SimEvent killed));
            Assert.AreEqual(id, killed.EnemyId, "named by id, so a view can flash that one enemy");
            Assert.AreEqual(EnemyKind.Runner, killed.Enemy);
            Assert.AreEqual(0, world.Enemies.AliveCount);
        }

        [Test]
        public void AnEnemyAtTheCore_ReportsTheDamageItDid()
        {
            // The Core's attack ring is two tiles from its centre, so an enemy dropped half a tile outside it
            // arrives and hits within a few ticks.
            SimWorld world = Sim.NewEnduringWorld(20, 12);
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(10f, 8.5f), Sim.Circle);

            world.Events.Clear();
            Sim.Tick(world, 120);

            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0, "it hit the Core");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.CoreDamaged, out SimEvent hit));
            Assert.AreEqual(6f, hit.Amount, Sim.Tol, "the Runner's damage");
        }

        [Test]
        public void AWave_ReportsItsStart_ItsClear_AndTheRunEnding_ExactlyOnceEach()
        {
            // The one-wave rig, with the stream drained every tick so the buffer's bound cannot confuse the
            // count. Every wave that started was cleared, and a won run reports its win once, not per tick.
            SimWorld world = Sim.OneWaveWorld();

            int started = 0, cleared = 0, won = 0, lost = 0;
            for (int i = 0; i < 60 * SimConfig.TickRate && world.Status == GameStatus.Playing; i++)
            {
                world.Events.Clear();
                if (world.Waves.HasWaveToStart) world.Waves.StartNextWave();
                Sim.FeedOneWaveGun(world);
                world.Tick(InputCommand.None);

                started += world.Events.CountOf(SimEventKind.WaveStarted);
                cleared += world.Events.CountOf(SimEventKind.WaveCleared);
                won += world.Events.CountOf(SimEventKind.RunWon);
                lost += world.Events.CountOf(SimEventKind.RunLost);
            }

            Assert.AreEqual(GameStatus.Won, world.Status);
            Assert.Greater(started, 0, "the run fought at least one wave");
            Assert.AreEqual(started, cleared, "every wave that started was cleared");
            Assert.AreEqual(1, won, "the win is reported exactly once");
            Assert.AreEqual(0, lost);
        }

        [Test]
        public void TheRingKeepsTheMostRecentEvents_AndCountsWhatItOverwrote()
        {
            // A reader that never clears still has to see the present: the buffer is a ring, so a stream that
            // outgrows it loses its oldest events and says how many.
            var events = new SimEventBuffer(capacity: 4);
            Assert.AreEqual(-1, events.LastDropTick, "a fresh ring has never dropped anything");

            for (int i = 0; i < 10; i++)
            {
                events.Tick = i;
                events.Banked(new Int2(i, 0));
            }

            Assert.AreEqual(4, events.Count, "the ring holds its capacity");
            Assert.AreEqual(6, events.Dropped, "and reports the six that fell off the oldest end");
            Assert.AreEqual(9, events.LastDropTick,
                "and says which tick it happened on - the number a window reader checks against");
            Assert.AreEqual(new Int2(6, 0), events[0].Cell, "index 0 is the oldest still held");
            Assert.AreEqual(new Int2(9, 0), events[3].Cell, "and the last is the newest");
            Assert.IsTrue(events.TryLast(SimEventKind.Banked, out SimEvent last));
            Assert.AreEqual(new Int2(9, 0), last.Cell, "so asking for the last one answers with the present");

            events.Clear();
            Assert.AreEqual(0, events.Count);
            Assert.AreEqual(0, events.Dropped);
            Assert.AreEqual(-1, events.LastDropTick, "and a restart starts the honesty clock fresh");
        }

        [Test]
        public void TheWindow_CountsAndReads_OnlyWhatIsInsideIt()
        {
            // The HUD's questions ("banked in the last second", "a refusal still on screen") are the buffer's
            // window queries, so the boundary is the buffer's to get right: an event exactly on sinceTick is
            // inside, one tick older is outside, and a drop only shames the window it fell into.
            var events = new SimEventBuffer(capacity: 8);

            events.Tick = 0;
            events.Banked(new Int2(0, 0));
            events.Tick = 10;
            events.Banked(new Int2(10, 0));
            events.Jammed(new Int2(11, 0), Sim.Square);
            events.Tick = 20;
            events.Banked(new Int2(20, 0));

            Assert.AreEqual(3, events.CountSince(SimEventKind.Banked, 0), "the whole stream");
            Assert.AreEqual(2, events.CountSince(SimEventKind.Banked, 10), "sinceTick itself is inside");
            Assert.AreEqual(1, events.CountSince(SimEventKind.Banked, 11), "one tick older is outside");
            Assert.AreEqual(0, events.CountSince(SimEventKind.Jammed, 12), "and the kind is respected");

            Assert.IsTrue(events.TryLastWithin(SimEventKind.Banked, 15, out SimEvent fresh));
            Assert.AreEqual(20, fresh.Tick, "the last of the kind within the window");
            Assert.IsTrue(events.TryLastWithin(SimEventKind.Banked, 10, out SimEvent wider));
            Assert.AreEqual(20, wider.Tick, "widening the window does not change the answer");
            Assert.IsFalse(events.TryLastWithin(SimEventKind.Banked, 21, out _), "an empty window is empty");
            Assert.IsTrue(events.TryLast(SimEventKind.Banked, out _), "while TryLast sees the ring as it always did");
            Assert.IsFalse(events.TryLastWithin(SimEventKind.RunWon, 0, out _), "a kind never emitted is never found");
        }

        [Test]
        public void ADrop_Shames_OnlyTheWindowItFellInto()
        {
            // Dropped is a run-long counter; DroppedSince is what a window reader actually asks. An old
            // overflow must not make a fresh, comfortably-sized window confess to counts it can still prove -
            // and a fresh one must not be reported as quiet.
            var events = new SimEventBuffer(capacity: 4);

            for (int i = 0; i < 6; i++)
            {
                events.Tick = i;          // ticks 0..5: the ring wraps, the two oldest are overwritten
                events.Banked(new Int2(i, 0));
            }

            Assert.AreEqual(2, events.Dropped);
            Assert.AreEqual(5, events.LastDropTick);
            Assert.IsTrue(events.DroppedSince(5), "the drop landed on tick 5, inside a window starting at 5");
            Assert.IsTrue(events.DroppedSince(3), "and inside any window the drop tick reaches");
            Assert.IsFalse(events.DroppedSince(6), "a window opened after the last drop has all its events");

            events.Tick = 9;
            events.Banked(new Int2(9, 0));
            // A full ring never catches up: every new event evicts an old one, so this push is itself a drop.
            // What a reader can prove is the narrower fact it needs - a window opening after the last eviction
            // lost nothing it should have seen.
            Assert.AreEqual(3, events.Dropped, "and it is counted, not swallowed");
            Assert.AreEqual(9, events.LastDropTick);
            Assert.IsTrue(events.DroppedSince(9), "inside a window that reaches the eviction, honesty says so");
            Assert.IsFalse(events.DroppedSince(10), "a window opened after the last eviction is provably whole");
            Assert.AreEqual(0, events.CountSince(SimEventKind.Banked, 10), "and its count - empty, here - cannot be quietly low");
        }

        /// <summary>
        /// The same honesty, one second earlier: a window opened in a run's first second reaches back before
        /// tick 0, and the ring's "never dropped anything" sentinel is -1 - so a comparison that forgets which is
        /// which reports a full stream and zero drops on a run that wrote three events. That is the HUD's
        /// "last 1s" line on the first frame, the one claim to quietness the player cannot check.
        /// </summary>
        [Test]
        public void AFreshRing_IsWhole_EvenForAWindowReachingBeforeTheFirstTick()
        {
            var events = new SimEventBuffer(capacity: 4);

            Assert.AreEqual(-1, events.LastDropTick, "nothing has been overwritten");
            Assert.IsFalse(events.DroppedSince(-30), "and a window opened before the run began is not shamed");
            Assert.IsFalse(events.DroppedSince(0), "nor the one the first second of the HUD asks");

            events.Tick = 0;
            events.Banked(new Int2(1, 0));
            Assert.IsFalse(events.DroppedSince(-30), "still whole after the first event");
        }

        [Test]
        public void ARefusedPlacement_ReportsWhy_TheClickThatBuiltNothing()
        {
            // The click the player complains about: the stockpile cannot cover a belt, so nothing appears on
            // the map. Silence there reads as a broken button, so the tick reports the refusal with reason and
            // price - which lets the HUD say "not enough circles" instead of nothing at all.
            SimWorld world = BrokeWorld();
            Click(world, new Int2(2, 2), BuildKind.Belt);

            Assert.IsFalse(world.Belts.Has(new Int2(2, 2)), "nothing was built");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal),
                "and the click said why");
            Assert.AreEqual(RejectionReason.NoFunds, refusal.Reason);
            Assert.AreEqual(new Int2(2, 2), refusal.Cell);
            Assert.AreEqual(BuildKind.Belt, refusal.Building);
            Assert.AreEqual(world.Economy.CostOf(BuildKind.Belt), refusal.Amount, Sim.Tol,
                "the price it could not pay");
        }

        [Test]
        public void ARefusedPlacement_NamesTheTileOrTheGround_NotJustNo()
        {
            // Funded, so every refusal here is about the map rather than the money - and "blocked" and
            // "wrong ground" are different problems with different fixes.
            SimWorld world = Sim.NewWorld(16, 10);
            var ore = new Int2(4, 4);
            world.Terrain.SetOre(ore, Quad.Circle);

            Click(world, ore, BuildKind.Turret);                       // a machine on a vein
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent onOre));
            Assert.AreEqual(RejectionReason.BadGround, onOre.Reason);
            Assert.AreEqual(ore, onOre.Cell);
            Assert.AreEqual(BuildKind.Turret, onOre.Building);

            Click(world, world.Core.Cell, BuildKind.Belt);             // the Core is occupied ground
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent onCore));
            Assert.AreEqual(RejectionReason.Occupied, onCore.Reason);

            Click(world, new Int2(-2, 5), BuildKind.Belt);             // the background past the edge
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent offMap));
            Assert.AreEqual(RejectionReason.OffMap, offMap.Reason);

            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.Built), "and none of them built anything");
        }

        [Test]
        public void ARefusedCutter_NamesTheLane_NotJustThePressCell()
        {
            // A machine on two tiles is refused for the tile that refused it, and the player is told about the
            // cell they pressed on: "the lane this cutter needs is taken" is what that has to read as, because
            // the belt in the way is not under the cursor. Aimed a quarter turn round, the same click builds.
            SimWorld world = Sim.NewWorld(16, 10);
            var press = new Int2(3, 6);
            Sim.LayRun(world, press + MachineFootprint.LaneOf(Dir.East).Offset(), Dir.East, 1);

            Click(world, press, BuildKind.Cutter);

            Assert.IsFalse(world.Machines.Has(press), "nothing is built where the lane is a belt");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent onLane),
                "and the click said why");
            Assert.AreEqual(RejectionReason.Occupied, onLane.Reason, "the lane tile is taken");
            Assert.AreEqual(press, onLane.Cell, "reported at the cell the player pressed on");

            world.Tick(Gesture.Aim(press, BuildKind.Cutter));
            world.Tick(Gesture.Aim(press + Dir.North.Offset(), BuildKind.Cutter));
            world.Tick(Gesture.Release(press + Dir.North.Offset(), BuildKind.Cutter));

            Assert.AreEqual(Dir.North, Sim.MachineAt(world, press).Direction,
                "the same click aimed north puts the lane on bare ground");
            Assert.IsTrue(world.Machines.Has(press + Dir.East.Offset()),
                "so the machine stands on both of its tiles");
        }

        [Test]
        public void ADragThatRunsDry_StopsQuietly_BecauseTheRunShowsWhereItStopped()
        {
            // The boundary of the reporting above: the cells a belt *drag* sweeps are skipped like blocked
            // ones - a run that stops at a dry stockpile is visible on the map, and a drag over an existing
            // build must not shout once per cell.
            SimWorld world = BrokeWorld(circles: 2);
            Sim.Drag(world, new Int2(1, 1), new Int2(6, 1));

            Assert.IsTrue(world.Belts.Has(new Int2(1, 1)), "the run got its two cells");
            Assert.IsTrue(world.Belts.Has(new Int2(2, 1)));
            Assert.IsFalse(world.Belts.Has(new Int2(3, 1)), "and stopped when the stockpile did");
            Assert.AreEqual(0, world.Economy.Circles);
            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.PlacementRejected),
                "the cells the drag swept are skipped, not reported");
        }

        [Test]
        public void ASuccessfulPlacement_ReportsOnlyTheBuild()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            world.Events.Clear();

            Click(world, new Int2(2, 8), BuildKind.Belt);

            Assert.IsTrue(world.Belts.Has(new Int2(2, 8)), "the click placed its belt");
            Assert.AreEqual(1, world.Events.CountOf(SimEventKind.Built));
            Assert.AreEqual(0, world.Events.CountOf(SimEventKind.PlacementRejected), "no refusal to report");
        }

        /// <summary>A press and release on one cell - the gesture spelled the way the input adapter spells it,
        /// as far as the tick is concerned.</summary>
        private static void Click(SimWorld world, Int2 cell, BuildKind kind)
        {
            world.Tick(Gesture.Aim(cell, kind));
            world.Tick(Gesture.Release(cell, kind));
        }

        /// <summary>A world with next to nothing to spend - the state "not enough circles" is about.</summary>
        private static SimWorld BrokeWorld(int circles = 0)
            => new SimWorld(Sim.TestMap(16, 10, circles), new SimConfig());

        [Test]
        public void TwoIdenticalRuns_ReportIdenticalStreams()
        {
            // Derived or not, the stream has to be a function of the run: a replay that drives sound and
            // flashes must react identically, or the feedback lies about what happened.
            SimWorld a = Sim.NewWorld(24, 12);
            SimWorld b = Sim.NewWorld(24, 12);

            for (int i = 0; i < 300; i++)
            {
                a.Events.Clear();
                b.Events.Clear();

                if (i % 30 == 0)
                {
                    a.Belts.TrySpawnItem(new Int2(2, 2), Sim.Circle);
                    b.Belts.TrySpawnItem(new Int2(2, 2), Sim.Circle);
                }

                var cmd = Gesture.Aim(new Int2(2 + (i % 9), 1 + (i % 5)), BuildKind.Belt);
                a.Tick(cmd);
                b.Tick(cmd);

                Assert.AreEqual(a.Events.Count, b.Events.Count, "stream lengths diverged on tick " + i);
                for (int e = 0; e < a.Events.Count; e++)
                {
                    Assert.AreEqual(a.Events[e].Kind, b.Events[e].Kind, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Cell, b.Events[e].Cell, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Tick, b.Events[e].Tick, "tick " + i + " event " + e);
                    Assert.AreEqual(a.Events[e].Amount, b.Events[e].Amount, "tick " + i + " event " + e);
                }
            }

            Assert.AreEqual(a.Economy.Circles, b.Economy.Circles);
        }
    }
}
