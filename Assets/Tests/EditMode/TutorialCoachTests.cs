using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The coached lesson's rules: the steps a tutorial level adds on top of the ones a lesson over a real level
    /// uses - a popup the player reads and dismisses, a building picked up, a wave the player calls - and the
    /// HUD it reveals a piece at a time. The view that draws the popup and the boxes reads only what is pinned
    /// here.
    /// </summary>
    public class TutorialCoachTests
    {
        private static TutorialTask Line(TutorialStep step, TutorialHud reveal = TutorialHud.None,
            BuildKind[] opens = null, bool startsWave = false)
            => new TutorialTask(step, "line " + step, null, opens, startsWave, reveal: reveal);

        private static SimWorld World(bool coached, params TutorialTask[] lines)
            => World(coached, new[] { new WaveDefinition(5f, new SpawnGroup(EnemyKind.Runner, 1, 0, 1f, 1f,
                Sim.Circle)) }, lines);

        private static SimWorld World(bool coached, WaveDefinition[] waves, params TutorialTask[] lines)
        {
            MapDefinition rig = Sim.RigMap();
            var map = new MapDefinition("coached", rig.Width, rig.Height, rig.StartShapes,
                rig.Patches, rig.SpawnPoints, waves, rig.Terrain, rig.CoreCell, Operators.All, rig.Seed, 0,
                new TutorialScript(new[] { BuildKind.Drill, BuildKind.Belt, BuildKind.Turret }, lines, coached));
            return new SimWorld(map, new SimConfig());
        }

        private static readonly Int2 Vein = new Int2(12, 6);

        [Test]
        public void APopup_PassesOnePressAtATime()
        {
            SimWorld world = World(true,
                Line(TutorialStep.Acknowledge), Line(TutorialStep.Acknowledge), Line(TutorialStep.PlaceDrill));

            Sim.Tick(world, 30);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "an explanation waits for the player, however long");
            Assert.IsTrue(world.Tutorial.WaitsForAcknowledge);

            world.RequestTutorialNext();
            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "two presses before a tick pass one line, not two");

            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "and the press is not carried to the next popup");

            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(2, world.Tutorial.StepsCompleted);
            Assert.IsFalse(world.Tutorial.WaitsForAcknowledge, "the third line is a building, not a popup");
        }

        [Test]
        public void APressOnALineThatIsNotAPopup_IsNotBanked()
        {
            SimWorld world = World(true, Line(TutorialStep.PlaceDrill), Line(TutorialStep.Acknowledge));

            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a press cannot pass a line about a building");

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted);

            Sim.Tick(world, 10);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted,
                "the early press was dropped, so the popup after it still waits to be read");
        }

        [Test]
        public void PickingUpTheDrill_TicksTheSelectLine()
        {
            SimWorld world = World(true, Line(TutorialStep.SelectDrill), Line(TutorialStep.PlaceDrill));

            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "nothing in hand yet");

            world.Tick(Gesture.Aim(new Int2(1, 1), BuildKind.Drill));
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "the drill in hand ticks the line");
        }

        [Test]
        public void ASelectLine_DoesNotHoldAPlayerWhoAlreadyBuilt()
        {
            SimWorld world = World(true, Line(TutorialStep.SelectDrill), Line(TutorialStep.PlaceDrill));

            Sim.Place(world, BuildKind.Drill, Vein, Dir.West);
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "a standing drill answers 'pick up the drill'");
        }

        [Test]
        public void PlacingATurret_TicksItsLine_LoadedOrNot()
        {
            SimWorld world = World(true, Line(TutorialStep.PlaceTurret), Line(TutorialStep.ArmTurret));

            Sim.Place(world, BuildKind.Turret, new Int2(10, 10), Dir.East);
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "an empty turret still stands");
            Assert.AreEqual(TutorialStep.ArmTurret, world.Tutorial.Current.Step, "loading it is the next line");
        }

        [Test]
        public void TheWave_WaitsForThePlayersCall_AndStartsOnIt()
        {
            SimWorld world = World(true,
                Line(TutorialStep.StartWave, startsWave: true), Line(TutorialStep.ClearWave));

            Sim.Tick(world, (int)(20 * SimConfig.TickRate));
            Assert.AreEqual(0, world.Waves.CurrentWave, "a coached map never starts a wave on a timer");
            Assert.IsTrue(world.Tutorial.WaitsForWaveCall, "the line is waiting for the call");

            world.Tick(new InputCommand(startWavePressed: true));
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "N answers the line");
            Assert.AreEqual(1, world.Waves.CurrentWave, "and the wave starts on that tick");
            Assert.IsFalse(world.Tutorial.WaitsForWaveCall);
        }

        [Test]
        public void TheWaveButton_CountsAsTheCall_Too()
        {
            SimWorld world = World(true,
                Line(TutorialStep.StartWave, startsWave: true), Line(TutorialStep.ClearWave));

            world.RequestNextWave();
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.Waves.CurrentWave, "the button and the key are one request");
        }

        [Test]
        public void ACoachedHud_IsRevealedLineByLine_AndHandedBackWhenTheLessonEnds()
        {
            SimWorld world = World(true,
                Line(TutorialStep.Acknowledge, TutorialHud.None),
                Line(TutorialStep.Acknowledge, TutorialHud.Bar),
                Line(TutorialStep.Acknowledge, TutorialHud.Stockpile | TutorialHud.Status));

            Assert.AreEqual(TutorialHud.None, world.Tutorial.Revealed, "a coached map opens on an empty HUD");

            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(TutorialHud.Bar, world.Tutorial.Revealed, "the line about the bar brings the bar");

            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(TutorialHud.Bar | TutorialHud.Stockpile | TutorialHud.Status,
                world.Tutorial.Revealed, "and a piece once shown stays shown");

            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.IsTrue(world.Tutorial.Complete);
            Assert.AreEqual(TutorialHud.All, world.Tutorial.Revealed, "a finished lesson gives the HUD back");
        }

        [Test]
        public void ALessonThatIsNotCoached_HidesNothing()
        {
            SimWorld world = World(false, Line(TutorialStep.PlaceDrill, TutorialHud.Bar));

            Assert.IsFalse(world.Tutorial.Coached);
            Assert.AreEqual(TutorialHud.All, world.Tutorial.Revealed,
                "Quarry's task card lesson keeps the whole HUD on screen");
        }

        [Test]
        public void ARestart_ForgetsAPressThatHadNotLandedYet()
        {
            SimWorld world = World(true, Line(TutorialStep.Acknowledge), Line(TutorialStep.Acknowledge));

            world.RequestTutorialNext();
            world.Restart();
            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a press made before a restart is not the new run's");
        }

        [Test]
        public void TheCoachedSteps_ThatAreNotAboutABuilding_AreSaidSo()
        {
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.Acknowledge));
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.StartWave));
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.ClearWave));
            Assert.IsFalse(TutorialScript.NeedsNoBuilding(TutorialStep.PlaceDrill));

            Assert.AreEqual(BuildKind.Drill, TutorialScript.RequiredBuilding(TutorialStep.SelectDrill));
            Assert.AreEqual(BuildKind.Belt, TutorialScript.RequiredBuilding(TutorialStep.SelectBelt));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.SelectTurret));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.PlaceTurret));
        }
    }
}
