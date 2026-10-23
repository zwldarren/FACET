using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The lesson's two gates and the clock it holds. A map that teaches opens the run with one building, holds
    /// <em>every</em> wave until a line of the lesson lets it go, and hands the rest of the bar over as each line
    /// is ticked.
    ///
    /// The runs here fight on the rig's ground with a wave table of their own: the shipped maps are painted data
    /// (their veins, doors and lesson would be what a test asserted), while the rig is a corridor a wave really
    /// walks down and a vein a drill really stands on. The lines themselves are the smallest ones that can be
    /// satisfied by a real fact - a drill placed, a shape banked - so the tests never pretend a click ticked a
    /// lesson.
    /// </summary>
    public class TutorialTests
    {
        // ---------------------------------------------------------------- the lesson, in one place

        /// <summary>One line with the parts these tests vary; the text is never what is under test here.</summary>
        private static TutorialTask Line(TutorialStep step, BuildKind[] opens = null, bool startsWave = false)
            => new TutorialTask(step, "line " + step, null, opens ?? new BuildKind[0], startsWave);

        /// <summary>A lesson that opens with the drill - the one building every line in this file needs.</summary>
        private static TutorialScript Script(params TutorialTask[] lines)
            => new TutorialScript(new[] { BuildKind.Drill }, lines);

        /// <summary>Two waits that never start a wave by themselves: enough to watch a clock, not a fight.</summary>
        private static readonly WaveDefinition[] TwoWaves =
        {
            new WaveDefinition(10f),
            new WaveDefinition(10f),
        };

        /// <summary>The rig's ground, doors and Core with this file's wave table and lesson.</summary>
        private static MapDefinition LessonMap(TutorialScript tutorial, WaveDefinition[] waves = null,
            Operators unlocked = Operators.All)
        {
            MapDefinition rig = Sim.RigMap();
            return new MapDefinition("lesson", rig.Width, rig.Height, rig.StartShapes,
                rig.Patches, rig.SpawnPoints, waves ?? TwoWaves,
                rig.Terrain, rig.CoreCell, unlocked, rig.Seed, 0, tutorial);
        }

        /// <summary>The vein every test's drill stands on, on the rig's own ground.</summary>
        private static readonly Int2 Vein = new Int2(12, 6);

        // ---------------------------------------------------------------- the clock

        [Test]
        public void ATeachingMap_HoldsEveryWait_AndABuildingDoesNotArmIt()
        {
            // The whole point of gating waves on a lesson: a countdown cannot arrive before the player has been
            // told what the wave will ask for. So the opening is not the first building's, and N is not an
            // answer either - a gate a key can bypass is a gate the second wave does not have.
            SimWorld world = new SimWorld(LessonMap(Script(
                Line(TutorialStep.PlaceDrill, new[] { BuildKind.Belt }),
                Line(TutorialStep.BeltFork))), new SimConfig());

            Assert.IsTrue(world.Tutorial.Active);
            Assert.IsTrue(world.Waves.HoldEveryWait, "the clock is the lesson's from the first frame");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase);
            Assert.IsFalse(world.Waves.HasWaveToStart, "and no reader is offered a wave it cannot start");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Belt), "the run opens with the drill alone");

            // A building is not a lesson: laying one does not start the clock, and no tick of it is spent.
            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Sim.Tick(world, (int)(5 * 60 * SimConfig.TickRate));

            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "a standing drill ticks the first line");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt), "and the line handed the belt over");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the lesson's second line has not been ticked");
            Assert.IsFalse(world.Waves.HasWaveToStart, "N cannot cut a teaching hold short");
            Assert.AreEqual(0, world.Waves.CurrentWave);
        }

        [Test]
        public void ACueLine_ReleasesTheHeldWait_SoItsOwnCountdownRuns()
        {
            // A cue does not start the wave on the tick it is read: the line's own intermission is the pause
            // between being taught and being attacked, which is what puts the briefing on screen for a moment.
            SimWorld world = new SimWorld(LessonMap(
                Script(Line(TutorialStep.PlaceDrill, startsWave: true)),
                new[] { new WaveDefinition(10f) }), new SimConfig());

            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the opening is the lesson's to release");

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);

            Assert.IsTrue(world.Tutorial.Complete, "the only line is ticked");
            Assert.IsFalse(world.Waves.HoldEveryWait, "and the run is nobody's lesson any more");
            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "the cue starts the announced pause");
            Assert.Greater(world.Waves.IntermissionRemaining, 0f);
            Assert.Less(world.Waves.IntermissionRemaining, 10f, "a countdown, not the whole wait");

            int ticks = (int)MathF.Ceiling(10f * SimConfig.TickRate);
            Sim.Tick(world, ticks - 2);
            Assert.AreEqual(0, world.Waves.CurrentWave, "the pause is not over yet");

            Sim.Tick(world, 1);
            Assert.AreEqual(1, world.Waves.CurrentWave, "and then the wave the line called arrives");
        }

        [Test]
        public void ACueTickedDuringAWave_ReleasesTheWaitThatFollows()
        {
            // A line is often satisfied in the middle of a wave - the cut the next wave is weak to is made
            // during the one before. There is no wait to release then, so the cue has to be banked for the wait
            // the clearing arms; otherwise the wave it was about stays held and N is refused with it.
            SimWorld world = new SimWorld(LessonMap(Script(
                    Line(TutorialStep.PlaceDrill, startsWave: true),
                    Line(TutorialStep.BankShape, startsWave: true),
                    Line(TutorialStep.BeltFork)),
                new[]
                {
                    new WaveDefinition(0.05f,
                        new SpawnGroup(EnemyKind.Runner, 1, 0, 0f, 1f, Sim.TopHalfCircle)),
                    new WaveDefinition(10f),
                }), new SimConfig());

            // The first line calls wave 1 with a countdown shorter than a blink, so the wave is in flight long
            // before the second line can be ticked.
            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Sim.Tick(world, 5);
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase, "wave 1 is out");

            // The banked shape is the second line's fact, and it happens mid-wave.
            world.Economy.Bank(1);
            world.Tick(InputCommand.None);

            Assert.AreEqual(2, world.Tutorial.StepsCompleted, "the cue line is ticked");
            Assert.IsTrue(world.Waves.HoldEveryWait, "the lesson still holds the clock");
            Assert.AreEqual(WavePhase.Running, world.Waves.Phase,
                "releasing a wait nobody holds cannot change the wave in flight");

            // Kill the wave the way a turret would: what this test measures is the wait after it, not the
            // fight. (An enemy left alive would keep hammering the Core until the run was lost, and a lost
            // run's clock never moves again, so the wave would read as running forever.)
            Sim.Tick(world, 2);
            var enemies = new List<EnemySnapshot>();
            world.Enemies.GetEnemies(enemies);
            Assert.AreEqual(1, enemies.Count, "wave 1 has its runner in it");

            for (int i = 0; i < enemies.Count; i++)
                world.Enemies.ApplyDamage(enemies[i].Id, 1000f, enemies[i].Weakness);

            world.Tick(InputCommand.None);

            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase,
                "the wait after the wave counts down instead of being held again");
            Assert.AreEqual(2, world.Waves.NextWave);
            Assert.Greater(world.Waves.IntermissionRemaining, 0f);
            Assert.IsTrue(world.Waves.HasWaveToStart, "and the player may cut its countdown short");
        }

        [Test]
        public void ACueTickedWhileACountdownRuns_IsSpent()
        {
            // A cue names the wave that is waiting. If one is already counting down, an earlier cue is calling
            // it - so a second cue must not be banked for the wait after it, or one line would call two waves.
            SimWorld world = new SimWorld(LessonMap(Script(
                Line(TutorialStep.PlaceDrill, startsWave: true),
                Line(TutorialStep.BeltFork))), new SimConfig());

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "the line's cue called this wave");

            world.Waves.ReleaseWait();

            int ticks = (int)MathF.Ceiling(10f * SimConfig.TickRate);
            Sim.Tick(world, ticks - 1);
            Assert.AreEqual(1, world.Waves.CurrentWave, "wave 1 arrives on its own countdown");

            Sim.Tick(world, 1);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase,
                "and the wait after it belongs to the lesson again - the extra cue bought nothing");
            Assert.IsFalse(world.Waves.HasWaveToStart);
        }

        [Test]
        public void TheLastLine_ReleasesTheHold_EvenWithNoCue()
        {
            // A script whose final line is not the one that calls a wave would otherwise leave the run waiting
            // forever for a clock nobody is left to arm.
            SimWorld world = new SimWorld(LessonMap(Script(
                    Line(TutorialStep.PlaceDrill),
                    Line(TutorialStep.BankShape)),
                new[] { new WaveDefinition(10f) }), new SimConfig());

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the first line does not call a wave");

            world.Economy.Bank(1);
            world.Tick(InputCommand.None);

            Assert.IsTrue(world.Tutorial.Complete);
            Assert.IsFalse(world.Waves.HoldEveryWait, "the lesson holds nothing once it is done");
            Assert.AreEqual(WavePhase.Intermission, world.Waves.Phase, "a finished lesson leaves nothing held");
            Assert.Greater(world.Waves.IntermissionRemaining, 0f);
        }

        // ---------------------------------------------------------------- the two rules the first map teaches

        [Test]
        public void AForkLine_TicksOnlyWithBothFrontsBelted_AndCallsNoWave()
        {
            // The lesson's one line about a machine's *shape*. A cutter queues four pieces per lane and then
            // stops dead, so a lane with no belt on it is the first thing a player's first cut stalls on - and
            // the line exists to have them wire it before the machine has to say so. One front is half a fork.
            var script = new TutorialScript(new[] { BuildKind.Drill, BuildKind.Belt, BuildKind.Cutter },
                new[] { Line(TutorialStep.BeltFork), Line(TutorialStep.MakeHalf) });
            SimWorld world = new SimWorld(LessonMap(script), new SimConfig());

            Sim.Place(world, BuildKind.Drill, new Int2(12, 9), Dir.West);
            Sim.LayRun(world, new Int2(11, 9), Dir.West, 1);          // into the cutter's back
            Sim.Place(world, BuildKind.Cutter, new Int2(10, 9), Dir.West);
            Sim.LayRun(world, new Int2(9, 9), Dir.West, 1);           // the front of the tile the belt fed

            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "one front is half a fork");

            // The second front is the lane over: the tile the displaced piece leaves by.
            Sim.LayRun(world, new Int2(9, 10), Dir.West, 1);
            world.Tick(InputCommand.None);

            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "both fronts is the fork");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase,
                "and a fork line is not a cue - the clock is still the lesson's");
        }

        [Test]
        public void ADiagonalPair_IsNotTheHalfACutMakes()
        {
            // Two quadrants filled is not the same fact as "a cut was made": a diagonal pair fills two and no
            // cut produces one (Shape.IsHalf says which is which, and Shape.Name has always worded the
            // difference). The line reads the shape's own answer, so a stacker's diagonal cannot tick a lesson
            // about the cutter.
            var script = new TutorialScript(new[] { BuildKind.Belt },
                new[] { Line(TutorialStep.MakeHalf), Line(TutorialStep.BeltFork) });
            SimWorld world = new SimWorld(LessonMap(script), new SimConfig());

            Sim.LayRun(world, new Int2(2, 2), Dir.East, 2);

            var diagonal = Shape.Of(Quad.Circle, Quad.Empty, Quad.Circle, Quad.Empty);   // NW and SE
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(2, 2), diagonal), "the belt took the piece");

            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a diagonal pair is not a half");

            Sim.LayRun(world, new Int2(2, 4), Dir.East, 1);
            Assert.IsTrue(world.Belts.TrySpawnItem(new Int2(2, 4), Sim.TopHalfCircle));
            world.Tick(InputCommand.None);

            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "a half that a cut makes is");
        }

        // ---------------------------------------------------------------- the last lesson: halves are ammunition

        [Test]
        public void ATurretWithAHalf_TicksTheLine()
        {
            // The opening's last line is about what the gun is chambered with, not that it is chambered:
            // wave 2 is weak to halves, and a cut that never reaches the gun is produce, not defence. The
            // chain is the rig's own - drill, cutter, and the lane-over half carried round to the turret.
            var script = new TutorialScript(
                new[] { BuildKind.Drill, BuildKind.Belt, BuildKind.Cutter, BuildKind.Turret },
                new[] { Line(TutorialStep.ArmTurretWithHalf) });
            SimWorld world = new SimWorld(LessonMap(script), new SimConfig());

            Sim.BuildRigDefence(world, out _);
            Sim.Tick(world, (int)(20 * SimConfig.TickRate));

            world.Machines.CountTurrets(out _, out int armed);
            Assert.AreEqual(1, armed, "the gun really chambered what the line handed it");
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "a chambered half ticks the line");
        }

        [Test]
        public void ATurretWithAFullRound_DoesNotTickTheHalfLine()
        {
            // Same gun, same drill line, no cutter: the round is real, and the answer is still no.
            var script = new TutorialScript(
                new[] { BuildKind.Drill, BuildKind.Belt, BuildKind.Turret },
                new[] { Line(TutorialStep.ArmTurretWithHalf) });
            SimWorld world = new SimWorld(LessonMap(script), new SimConfig());

            Sim.Place(world, BuildKind.Drill, new Int2(12, 9), Dir.West);
            Sim.LayRun(world, new Int2(11, 9), Dir.West, 3);          // (11,9) .. (9,9), into the gun
            Sim.Place(world, BuildKind.Turret, new Int2(8, 9), Dir.West);   // (7,9) is the Core's own corner

            Sim.Tick(world, (int)(20 * SimConfig.TickRate));

            world.Machines.CountTurrets(out _, out int armed);
            Assert.AreEqual(1, armed, "the gun is loaded - the rule refuses the round's shape, not its absence");
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a full circle is not the half wave 2 is weak to");
        }

        [Test]
        public void TwoTurretsHoldingHalves_TickThePairLine_AndOneIsNotEnough()
        {
            // The fork's two lanes end in two guns. A single armed gun is the older line's fact; the pair is
            // its own because a player who wired one lane has left half the cutter's output on a dead end.
            var script = new TutorialScript(
                new[] { BuildKind.Belt, BuildKind.Turret },
                new[] { Line(TutorialStep.TwoTurretsLoaded) });

            SimWorld one = new SimWorld(LessonMap(script), new SimConfig());
            Sim.LayRun(one, new Int2(1, 2), Dir.East, 1);
            Sim.Place(one, BuildKind.Turret, new Int2(2, 2), Dir.East);
            Assert.IsTrue(one.Belts.TrySpawnItem(new Int2(1, 2), Sim.TopHalfCircle));
            Sim.Tick(one, (int)(5 * SimConfig.TickRate));
            Assert.AreEqual(0, one.Tutorial.StepsCompleted, "one loaded gun is not the fork's pair");

            SimWorld two = new SimWorld(LessonMap(script), new SimConfig());
            Sim.LayRun(two, new Int2(1, 2), Dir.East, 1);
            Sim.Place(two, BuildKind.Turret, new Int2(2, 2), Dir.East);
            Sim.LayRun(two, new Int2(3, 2), Dir.East, 1);
            Sim.Place(two, BuildKind.Turret, new Int2(4, 2), Dir.East);
            Assert.IsTrue(two.Belts.TrySpawnItem(new Int2(1, 2), Sim.TopHalfCircle));
            Assert.IsTrue(two.Belts.TrySpawnItem(new Int2(3, 2), Sim.TopHalfCircle));
            Sim.Tick(two, (int)(5 * SimConfig.TickRate));
            Assert.AreEqual(1, two.Tutorial.StepsCompleted, "both guns chambered: the pair is armed");
        }

        [Test]
        public void TwoTurretsHoldingAcrossHalves_TickTheTurnedCutterLine_AndLeftRightDoNot()
        {
            // A north/south cutter makes left/right halves; the demand asks for top/bottom. The line is about
            // the cut that reaches the chamber, so it is judged by the halves the guns hold - a player who
            // never turned the cutter before placing it fails here, and no rotator has to stand anywhere.
            var script = new TutorialScript(
                new[] { BuildKind.Belt, BuildKind.Turret },
                new[] { Line(TutorialStep.TwoTurretsLoadedAcross) });

            SimWorld across = new SimWorld(LessonMap(script), new SimConfig());
            Sim.LayRun(across, new Int2(1, 2), Dir.East, 1);
            Sim.Place(across, BuildKind.Turret, new Int2(2, 2), Dir.East);
            Sim.LayRun(across, new Int2(3, 2), Dir.East, 1);
            Sim.Place(across, BuildKind.Turret, new Int2(4, 2), Dir.East);
            Assert.IsTrue(across.Belts.TrySpawnItem(new Int2(1, 2), Sim.TopHalfCircle));
            Assert.IsTrue(across.Belts.TrySpawnItem(new Int2(3, 2), Sim.BottomHalfCircle));
            Sim.Tick(across, (int)(5 * SimConfig.TickRate));
            Assert.AreEqual(1, across.Tutorial.StepsCompleted, "a top and a bottom half are across the lane");

            SimWorld sideways = new SimWorld(LessonMap(script), new SimConfig());
            Sim.LayRun(sideways, new Int2(1, 2), Dir.East, 1);
            Sim.Place(sideways, BuildKind.Turret, new Int2(2, 2), Dir.East);
            Sim.LayRun(sideways, new Int2(3, 2), Dir.East, 1);
            Sim.Place(sideways, BuildKind.Turret, new Int2(4, 2), Dir.East);
            Assert.IsTrue(sideways.Belts.TrySpawnItem(new Int2(1, 2), Sim.TopHalfCircle));
            Assert.IsTrue(sideways.Belts.TrySpawnItem(new Int2(3, 2), Sim.LeftHalfCircle));
            Sim.Tick(sideways, (int)(5 * SimConfig.TickRate));
            Assert.AreEqual(0, sideways.Tutorial.StepsCompleted,
                "a left half is the cut the north flow already made, not the turn");
        }

        // ---------------------------------------------------------------- the caller's skip

        [Test]
        public void ASkippedLesson_TeachesAndHoldsNothing_EvenAcrossARestart()
        {
            // A caller may set a lesson aside before the world's first tick - a scripted scene, or a future
            // door that offers a lessonless replay. The world then reads as a map without one - the bar is
            // the map's own, the opening waits for a building rather than for a line - and a restart does
            // not bring the lesson back, because the skip is a call about the map, not a run's state.
            SimWorld world = new SimWorld(LessonMap(Script(
                Line(TutorialStep.PlaceDrill, new[] { BuildKind.Belt }, startsWave: true),
                Line(TutorialStep.BeltFork))), new SimConfig());

            world.Tutorial.Skip();
            world.Tick(InputCommand.None);

            Assert.IsFalse(world.Tutorial.Active, "a skipped lesson is not there");
            Assert.IsTrue(world.Tutorial.Complete);
            Assert.IsFalse(world.Tutorial.GatesWaves);
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt), "the bar is the map's own from the first frame");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter));
            Assert.IsFalse(world.Waves.HoldEveryWait);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase,
                "the opening still waits for the first building - the map's own rule, not the lesson's");
            Assert.IsTrue(world.Waves.HasWaveToStart, "and N is the player's again");

            world.RequestRestart();
            world.Tick(InputCommand.None);

            Assert.IsFalse(world.Tutorial.Active, "a restart is not a reason to teach again");
            Assert.IsFalse(world.Waves.HoldEveryWait);
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));
        }

        [Test]
        public void ALockedKind_IsTheLessons_OnlyWhenTheLessonIsTheOneGate()
        {
            // A dead digit gets a word only when the lesson is the gate the kind fails: a kind the map's own
            // operator set refuses is not this run's to explain, and an unlocked kind has no refusal to
            // explain.
            SimWorld world = new SimWorld(LessonMap(
                Script(Line(TutorialStep.PlaceDrill, new[] { BuildKind.Belt })),
                unlocked: Operators.Cutter), new SimConfig());

            Assert.IsTrue(world.LockedByTutorial(BuildKind.Belt), "the lesson is the only gate the belt fails");
            Assert.IsFalse(world.LockedByTutorial(BuildKind.Drill), "an unlocked kind is not locked");
            Assert.IsFalse(world.LockedByTutorial(BuildKind.Stacker),
                "the map's operator set refuses the stacker - that is not the lesson's doing");

            world.Tutorial.Skip();
            Assert.IsFalse(world.LockedByTutorial(BuildKind.Belt), "no lesson, no lesson's refusal");
        }

        [Test]
        public void ARestart_PutsTheLessonBackAtItsFirstLine_AndHoldsTheClockAgain()
        {
            // A restart is a new run: a bar that stayed wide and a clock that stayed armed would be the
            // half-restarted state a restart exists to avoid.
            SimWorld world = new SimWorld(LessonMap(Script(
                Line(TutorialStep.PlaceDrill, new[] { BuildKind.Belt }, startsWave: true),
                Line(TutorialStep.BeltFork))), new SimConfig());

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted);
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));

            world.RequestRestart();
            world.Tick(InputCommand.None);

            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a restart is a new run");
            Assert.IsFalse(world.Tutorial.Complete);
            Assert.IsTrue(world.Waves.HoldEveryWait);
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the clock is the lesson's again");
            Assert.AreEqual(1, world.Waves.NextWave);
            Assert.IsFalse(world.Waves.HasWaveToStart);
            Assert.IsFalse(world.IsUnlocked(BuildKind.Belt), "and the bar is back to the lesson's first key");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Drill));
        }

        // ---------------------------------------------------------------- the two gates

        [Test]
        public void AMapWithoutAScript_KeepsTheOpeningItAlwaysHad()
        {
            SimWorld world = new SimWorld(Sim.RigMap(), new SimConfig());

            Assert.IsFalse(world.Tutorial.Active);
            Assert.IsFalse(world.Tutorial.GatesWaves);
            Assert.IsTrue(world.Tutorial.Complete, "nothing to teach is nothing left to learn");
            Assert.IsFalse(world.Waves.HoldEveryWait, "the clock is the map's own again");
            Assert.AreEqual(WavePhase.Held, world.Waves.Phase, "the opening still waits for the first building");
            Assert.IsTrue(world.Waves.HasWaveToStart, "and N is the player's to press");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter));
        }

        [Test]
        public void BothGates_HaveToAgree_TheMapAndTheLesson()
        {
            // The lesson hands the cutter over with its first key, and the map's operator set still refuses it:
            // a tutorial is a lesson laid over a level, not a permission to overrule the level's ladder.
            var taught = new TutorialScript(
                new[] { BuildKind.Drill, BuildKind.Cutter },
                new[] { Line(TutorialStep.PlaceDrill) });

            SimWorld refused = new SimWorld(LessonMap(taught, unlocked: Operators.Rotator), new SimConfig());
            Assert.IsFalse(refused.IsUnlocked(BuildKind.Cutter),
                "the map never unlocked the cutter, so the bar cannot carry one");
            Assert.IsTrue(refused.IsUnlocked(BuildKind.Drill));
            Assert.IsFalse(refused.TryPlace(BuildKind.Cutter, new Int2(9, 9), Dir.West),
                "and asking for one anyway is refused");

            SimWorld admitted = new SimWorld(LessonMap(taught, unlocked: Operators.Cutter), new SimConfig());
            Assert.IsTrue(admitted.IsUnlocked(BuildKind.Cutter));
        }

        [Test]
        public void PastTheEndOfTheLesson_TheLineIsEmptyAndCannotTick()
        {
            // Readers walking the list they were told the length of must not be handed a default struct: a
            // default is PlaceDrill, which ticks the moment any drill stands, and null text.
            var script = new TutorialScript(new[] { BuildKind.Drill },
                new[] { Line(TutorialStep.PlaceDrill) });

            Assert.AreEqual(TutorialScript.NoStep, script[-1].Step);
            Assert.AreEqual(TutorialScript.NoStep, script[script.Count].Step);
            Assert.AreEqual(string.Empty, script[script.Count].Text);
            Assert.AreEqual(0, script[script.Count].Opens.Length, "normalized, not null");
            Assert.AreEqual(0, script[script.Count].Ghost.Length, "the empty line has no blueprint");

            SimWorld world = new SimWorld(LessonMap(script, new[] { new WaveDefinition(10f) }), new SimConfig());
            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);

            Assert.IsTrue(world.Tutorial.Complete);
            Assert.AreEqual(TutorialScript.NoStep, world.Tutorial.Current.Step,
                "the current line past the end cannot be read as a step that ticks");
            Assert.AreEqual(string.Empty, world.Tutorial.Current.Text);
        }
    }
}
