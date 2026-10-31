using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The training map's one-action-per-line steps: picking a building up, standing a turret, and getting
    /// through the first wave. Each is still a fact about the run - the selection is world state set by a
    /// command on a tick, a turret stands or it does not, the wave clock is between waves or it is not - so a
    /// line ticks on what the player actually did, not on a key that happened to be pressed.
    ///
    /// Like <see cref="TutorialTests"/>, the runs fight on the rig's ground with a lesson of their own rather
    /// than reading a shipped map.
    /// </summary>
    public class TutorialStepByStepTests
    {
        private static readonly Int2 Vein = new Int2(12, 6);

        private static TutorialTask Line(TutorialStep step, BuildKind[] opens = null, bool startsWave = false)
            => new TutorialTask(step, "line " + step, null, opens ?? new BuildKind[0], startsWave);

        private static SimWorld World(BuildKind[] starting, params TutorialTask[] lines)
            => World(starting, null, lines);

        private static SimWorld World(BuildKind[] starting, WaveDefinition[] waves, params TutorialTask[] lines)
        {
            MapDefinition rig = Sim.RigMap();
            var map = new MapDefinition("lesson", rig.Width, rig.Height, rig.StartShapes, rig.Patches,
                rig.SpawnPoints, waves ?? new[] { new WaveDefinition(10f), new WaveDefinition(10f) },
                rig.Terrain, rig.CoreCell, Operators.All, rig.Seed, 0, new TutorialScript(starting, lines));
            return new SimWorld(map, new SimConfig());
        }

        private static void Holding(SimWorld world, BuildKind? kind, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++) world.Tick(new InputCommand(cursorCell: new Int2(1, 1), selected: kind));
        }

        [Test]
        public void ASelectLine_TicksWhenThatBuildingIsInHand_AndNotForAnother()
        {
            SimWorld world = World(new[] { BuildKind.Drill, BuildKind.Belt },
                Line(TutorialStep.SelectDrill),
                Line(TutorialStep.SelectBelt));

            Holding(world, null, 3);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "nothing in hand ticks nothing");

            Holding(world, BuildKind.Belt, 3);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "the belt is not the drill the line asks for");

            Holding(world, BuildKind.Drill, 2);
            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "picking the drill up ticks the line");

            Holding(world, BuildKind.Belt, 2);
            Assert.AreEqual(2, world.Tutorial.StepsCompleted, "and the next line is its own building");
        }

        [Test]
        public void ASelectLine_CannotBeTickedByABuildingTheLessonHasNotHandedOver()
        {
            // A building still locked by the lesson cannot be placed, so holding it is not the step done.
            SimWorld world = World(new[] { BuildKind.Drill }, Line(TutorialStep.SelectTurret));

            Holding(world, BuildKind.Turret, 5);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "the turret has not been handed over yet");
        }

        [Test]
        public void PlaceTurret_TicksForAStandingGun_LoadedOrNot()
        {
            SimWorld world = World(new[] { BuildKind.Turret }, Line(TutorialStep.PlaceTurret));

            world.Tick(InputCommand.None);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted);

            Sim.Place(world, BuildKind.Turret, new Int2(10, 10), Dir.East);
            world.Tick(InputCommand.None);

            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "an empty chamber still counts: feeding it is the next line");
            Assert.IsFalse(Sim.ArmedAt(world, new Int2(10, 10)));
        }

        [Test]
        public void ClearFirstWave_WaitsForTheWaveTheLessonCalled_ThenTicks()
        {
            // The first line calls wave one; the second waits for it to be over. Wave one is empty here, so it
            // ends on its own the tick after it starts - the rule under test is the clock's, not the fight's.
            var waves = new[]
            {
                new WaveDefinition(0f),
                new WaveDefinition(10f, new SpawnGroup(EnemyKind.Runner, 1, 0, 0.5f, 1f, Sim.Circle)),
            };
            SimWorld world = World(new[] { BuildKind.Drill }, waves,
                Line(TutorialStep.ClearFirstWave, new[] { BuildKind.Belt }),
                Line(TutorialStep.PlaceDrill));

            Sim.Tick(world, 30);
            Assert.AreEqual(0, world.Tutorial.StepsCompleted, "a held first wave has not been fought");

            world.Waves.ReleaseWait();
            for (int i = 0; i < 30 * 10 && world.Tutorial.StepsCompleted < 1; i++) world.Tick(InputCommand.None);

            Assert.AreEqual(1, world.Tutorial.StepsCompleted, "the line ticks once wave one is over");
            Assert.AreNotEqual(WavePhase.Running, world.Waves.Phase);
            Assert.AreEqual(2, world.Waves.NextWave, "with wave two next, held for the lesson");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt), "and hands over what it opens");
        }

        [Test]
        public void EveryNewStep_NamesTheBuildingItNeeds()
        {
            // The map validator refuses a lesson that can never pass a line, by asking which building a line is
            // about; a step that named none would throw there instead of being checked.
            Assert.AreEqual(BuildKind.Drill, TutorialScript.RequiredBuilding(TutorialStep.SelectDrill));
            Assert.AreEqual(BuildKind.Belt, TutorialScript.RequiredBuilding(TutorialStep.SelectBelt));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.SelectTurret));
            Assert.AreEqual(BuildKind.Cutter, TutorialScript.RequiredBuilding(TutorialStep.SelectCutter));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.PlaceTurret));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.ClearFirstWave));
        }
    }
}
