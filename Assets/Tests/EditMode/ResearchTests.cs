using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// Research: a map's schedule puts buildings on the bar wave by wave, locked, and the player buys them with
    /// banked shapes. A building the schedule names is unplaceable until bought; one it does not name is left as
    /// the map and the lesson leave it, so a map with no schedule plays exactly as before.
    /// </summary>
    public class ResearchTests
    {
        private const int Rich = 500;

        /// <summary>A 20x12 map with empty waves - each clears the tick it starts - and the given schedule.</summary>
        private static SimWorld World(int startShapes, params ResearchOffer[] offers)
        {
            var waves = new WaveDefinition[6];
            for (int i = 0; i < waves.Length; i++) waves[i] = new WaveDefinition(0f);

            var map = new MapDefinition("research", 20, 12, startShapes, new ShapePatch[0],
                new[] { new Int2(1, 1) }, waves, research: new ResearchSchedule(offers));
            return new SimWorld(map, new SimConfig());
        }

        /// <summary>Start and fight out the next wave. The waves here have no enemies, so it clears at once.</summary>
        private static void ClearWave(SimWorld world)
        {
            int target = world.Waves.Cleared + 1;
            world.Waves.StartNextWave();
            for (int i = 0; i < 10 && world.Waves.Cleared < target; i++) world.Tick(InputCommand.None);
            Assert.AreEqual(target, world.Waves.Cleared, "the empty wave clears");
        }

        private static ResearchOffer Splitter(int afterWave, int cost)
            => new ResearchOffer(afterWave, new[] { BuildKind.Splitter }, cost);

        [Test]
        public void AMapWithoutASchedule_LocksNothing()
        {
            SimWorld world = Sim.NewWorld(20, 12);

            Assert.IsFalse(world.Research.Active);
            Assert.IsTrue(world.IsUnlocked(BuildKind.Splitter));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter));
            Assert.IsFalse(world.Researchable(BuildKind.Splitter));
        }

        [Test]
        public void AScheduledBuilding_IsLockedAndOffNothing_BeforeItsWave()
        {
            SimWorld world = World(Rich, Splitter(1, 20));

            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter), "named by the schedule, so locked");
            Assert.IsFalse(world.Researchable(BuildKind.Splitter), "and not on offer before its wave");
            Assert.IsFalse(world.TryPlace(BuildKind.Splitter, new Int2(4, 4), Dir.East), "so it cannot be placed");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt), "a building the schedule does not name is untouched");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Turret));
        }

        [Test]
        public void AfterItsWave_TheBuildingIsOffered_ButStillLocked()
        {
            SimWorld world = World(Rich, Splitter(1, 20));

            ClearWave(world);

            Assert.IsTrue(world.Researchable(BuildKind.Splitter), "on the bar with a price");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter), "but not placeable until bought");
            Assert.AreEqual(1, world.Research.OfferedCount);
            Assert.AreSame(world.Map.Research.Offers[0], world.Research.OfferedAt(0));
        }

        [Test]
        public void Research_SpendsTheCost_AndUnlocks()
        {
            SimWorld world = World(Rich, Splitter(1, 20));
            ClearWave(world);

            int before = world.Economy.Shapes;
            world.RequestResearch(BuildKind.Splitter);
            world.Tick(InputCommand.None);

            Assert.AreEqual(before - 20, world.Economy.Shapes, "paid out of the Core");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Splitter));
            Assert.IsFalse(world.Researchable(BuildKind.Splitter), "no longer on offer: it is bought");
            Assert.IsTrue(world.TryPlace(BuildKind.Splitter, new Int2(4, 4), Dir.East), "and placeable");
        }

        [Test]
        public void Research_TheStockpileCannotCover_IsRefused_AndSpendsNothing()
        {
            SimWorld world = World(10, Splitter(1, 20));
            ClearWave(world);

            world.RequestResearch(BuildKind.Splitter);
            world.Tick(InputCommand.None);

            Assert.AreEqual(10, world.Economy.Shapes);
            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter));
            Assert.IsTrue(world.Researchable(BuildKind.Splitter), "still on offer for later");
        }

        [Test]
        public void Research_BeforeTheOffer_IsDropped()
        {
            SimWorld world = World(Rich, Splitter(1, 20));

            world.RequestResearch(BuildKind.Splitter);
            world.Tick(InputCommand.None);

            Assert.AreEqual(Rich, world.Economy.Shapes);
            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter));
        }

        [Test]
        public void AFreeOffer_IsHandedOverAsItIsMade()
        {
            SimWorld world = World(Rich, new ResearchOffer(1, new[] { BuildKind.Cutter, BuildKind.Sink }, 30),
                new ResearchOffer(1, new[] { BuildKind.Pipe }, 0, "Pipe available"));

            ClearWave(world);

            Assert.IsTrue(world.IsUnlocked(BuildKind.Pipe), "cost 0: no purchase needed");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Cutter));

            world.RequestResearch(BuildKind.Sink);
            world.Tick(InputCommand.None);

            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter), "one purchase buys every building in its row");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Sink));
            Assert.AreEqual(Rich - 30, world.Economy.Shapes, "and is paid once");
        }

        [Test]
        public void AnOfferAtWaveZero_StandsFromTheFirstFrame()
        {
            SimWorld world = World(Rich, Splitter(0, 20));

            Assert.IsTrue(world.Researchable(BuildKind.Splitter));
            Assert.AreEqual(1, world.Research.OfferedCount);
        }

        [Test]
        public void Offers_AreMadeOnceEach_InScheduleOrder_AsTheirWavesClear()
        {
            SimWorld world = World(Rich, Splitter(1, 20),
                new ResearchOffer(3, new[] { BuildKind.Cutter }, 40),
                new ResearchOffer(2, new[] { BuildKind.Rotator }, 40));

            ClearWave(world);
            Assert.AreEqual(1, world.Research.OfferedCount);

            ClearWave(world);
            Assert.AreEqual(2, world.Research.OfferedCount);
            Assert.IsTrue(world.Researchable(BuildKind.Rotator));
            Assert.IsFalse(world.Researchable(BuildKind.Cutter));

            ClearWave(world);
            Assert.AreEqual(3, world.Research.OfferedCount);
            Assert.IsTrue(world.Researchable(BuildKind.Cutter));
            Assert.AreSame(world.Map.Research.Offers[1], world.Research.OfferedAt(2),
                "the offers read in the order they were made");

            Sim.Tick(world, 5);
            Assert.AreEqual(3, world.Research.OfferedCount, "and none is made twice");
        }

        [Test]
        public void Restart_ForgetsResearch()
        {
            SimWorld world = World(Rich, Splitter(1, 20));
            ClearWave(world);
            world.RequestResearch(BuildKind.Splitter);
            world.Tick(InputCommand.None);
            Assert.IsTrue(world.IsUnlocked(BuildKind.Splitter));

            world.Restart();

            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter), "a new run researches from scratch");
            Assert.IsFalse(world.Researchable(BuildKind.Splitter));
            Assert.AreEqual(0, world.Research.OfferedCount);
            Assert.AreEqual(0, world.Waves.Cleared);
        }

        [Test]
        public void Cleared_CountsTheWavesSurvived()
        {
            SimWorld world = World(Rich);

            Assert.AreEqual(0, world.Waves.Cleared);
            ClearWave(world);
            ClearWave(world);
            Assert.AreEqual(2, world.Waves.Cleared);
        }

        // ------------------------------------------------------------------ the lessons

        private static TutorialTask Line(TutorialStep step, BuildKind build)
            => new TutorialTask(step, step.ToString(), null, new[] { build }, false);

        /// <summary>Waves of 10 seconds' wait, the first offer's lesson three lines: buy, pick, place.</summary>
        private static SimWorld LessonWorld()
        {
            var lesson = new TutorialScript(null, new[]
            {
                Line(TutorialStep.ResearchBuilding, BuildKind.Splitter),
                Line(TutorialStep.SelectBuilding, BuildKind.Splitter),
                Line(TutorialStep.PlaceBuilding, BuildKind.Splitter),
            }, coached: true);

            var waves = new WaveDefinition[4];
            for (int i = 0; i < waves.Length; i++) waves[i] = new WaveDefinition(10f);

            var map = new MapDefinition("lesson", 20, 12, Rich, new ShapePatch[0], new[] { new Int2(1, 1) }, waves,
                research: new ResearchSchedule(new[]
                {
                    new ResearchOffer(1, new[] { BuildKind.Splitter }, 20, null, lesson),
                }));
            return new SimWorld(map, new SimConfig());
        }

        [Test]
        public void NoLesson_IsTaught_BeforeItsOffer()
        {
            SimWorld world = LessonWorld();

            Assert.IsNull(world.Research.Lesson);
            Assert.IsFalse(world.Research.Teaching);
        }

        [Test]
        public void TheLesson_StartsWithTheOffer_AndHoldsTheCountdown_NotTheRun()
        {
            SimWorld world = LessonWorld();
            ClearWave(world);

            Assert.IsNotNull(world.Research.Lesson, "offered, so taught");
            Assert.AreEqual(TutorialStep.ResearchBuilding, world.Research.Lesson.Current.Step);
            Assert.IsFalse(world.Paused, "the player has to be able to do what it says");

            float before = world.Waves.IntermissionRemaining;
            int tick = world.TickCount;
            Sim.Tick(world, 60);
            Assert.AreEqual(tick + 60, world.TickCount, "the run goes on");
            Assert.AreEqual(before, world.Waves.IntermissionRemaining, "and the countdown waits");
        }

        [Test]
        public void EachLine_TicksOnlyWhenItIsReallyDone()
        {
            SimWorld world = LessonWorld();
            ClearWave(world);
            TutorialState lesson = world.Research.Lesson;

            Sim.Tick(world, 5);
            Assert.AreEqual(0, lesson.StepsCompleted, "not bought yet");

            world.RequestResearch(BuildKind.Splitter);
            Sim.Tick(world, 3);
            Assert.AreEqual(1, lesson.StepsCompleted, "bought");

            Sim.Tick(world, 5);
            Assert.AreEqual(1, lesson.StepsCompleted, "not picked up yet");

            Assert.IsTrue(world.TryPlace(BuildKind.Splitter, new Int2(4, 4), Dir.East));
            Sim.Tick(world, 3);

            Assert.IsNull(world.Research.Lesson, "picking and placing done: the lesson is over");
            Assert.IsFalse(world.Research.Teaching);
        }

        [Test]
        public void AFinishedLesson_LetsTheCountdownRun()
        {
            SimWorld world = LessonWorld();
            ClearWave(world);
            world.RequestResearch(BuildKind.Splitter);
            Sim.Tick(world, 3);
            Assert.IsTrue(world.TryPlace(BuildKind.Splitter, new Int2(4, 4), Dir.East));
            Sim.Tick(world, 3);

            float before = world.Waves.IntermissionRemaining;
            Sim.Tick(world, 30);

            Assert.Less(world.Waves.IntermissionRemaining, before, "the wave is on its way again");
        }

        [Test]
        public void N_SkipsTheLesson_AndLeavesTheResearchOnTheBar()
        {
            SimWorld world = LessonWorld();
            ClearWave(world);
            Assert.IsTrue(world.Research.Teaching);

            world.Waves.StartNextWave();
            Sim.Tick(world, 2);

            Assert.IsFalse(world.Research.Teaching, "the player called the wave: they know this already");
            Assert.IsTrue(world.Researchable(BuildKind.Splitter), "and can still buy it");
        }

        [Test]
        public void Restart_TeachesTheLessonAgain()
        {
            SimWorld world = LessonWorld();
            ClearWave(world);
            world.RequestResearch(BuildKind.Splitter);
            Sim.Tick(world, 3);
            Assert.AreEqual(1, world.Research.Lesson.StepsCompleted);

            world.Restart();
            Assert.IsNull(world.Research.Lesson);

            ClearWave(world);
            Assert.AreEqual(0, world.Research.Lesson.StepsCompleted, "from its first line");
        }

        [Test]
        public void TheNewSteps_NameABuilding_OrSayTheLineDoes()
        {
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.ResearchBuilding));
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.SelectBuilding));
            Assert.IsTrue(TutorialScript.NeedsNoBuilding(TutorialStep.PlaceBuilding));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.TwoTurretsArmed));
            Assert.AreEqual(BuildKind.Turret, TutorialScript.RequiredBuilding(TutorialStep.AnyTurretHoldsWaveShape));
        }

        [Test]
        public void TheEndlessCopy_KeepsTheSchedule()
        {
            SimWorld world = World(Rich, Splitter(1, 20));

            MapDefinition endless = world.Map.WithEndless();

            Assert.AreSame(world.Map.Research, endless.Research);
        }
    }
}
