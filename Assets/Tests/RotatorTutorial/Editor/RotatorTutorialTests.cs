using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    public sealed class RotatorTutorialTests
    {
        private static readonly Int2 Gap = new Int2(14, 6);
        private static readonly Int2 Gun = new Int2(14, 8);
        private static readonly Int2 CutterCell = new Int2(10, 6);
        private static readonly Int2 CutterSecondTile = new Int2(10, 5);
        private static readonly Int2 LaneBelt = new Int2(12, 6);
        private SimWorld _world;

        private static MapAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/RotatorTutorial.asset");
            Assert.IsNotNull(asset);
            return asset;
        }

        [SetUp]
        public void SetUp() => _world = new SimWorld(Load().ToCore(), new SimConfig());

        [Test]
        public void RotatorLessonIsAValidShortCoachedMapWithOneWave()
        {
            MapAsset asset = Load();
            var problems = asset.Validate();
            Assert.AreEqual(0, problems.Count, problems.Count > 0 ? problems[0].ToString() : "");
            Assert.IsTrue(asset.TutorialCoached);
            Assert.AreEqual(1, asset.Waves.Length);
            Assert.LessOrEqual(asset.TutorialTasks.Length, 8, "the lesson stays short");
        }

        [Test]
        public void CampaignTeachesTheRotatorAfterTheCutterAndBeforeQuarry()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>("Assets/Data/Campaign.asset");
            Assert.AreEqual("Cutter Tutorial", campaign.Maps[1].Name);
            Assert.AreSame(Load(), campaign.Maps[2]);
            Assert.AreEqual("Quarry", campaign.Maps[3].Name);
        }

        [Test]
        public void ExistingSerializedStepNumbersArePreserved()
        {
            Assert.AreEqual(20, (int)TutorialStep.PlaceCutterTurrets);
            Assert.AreEqual(21, (int)TutorialStep.SelectRotator);
            Assert.AreEqual(22, (int)TutorialStep.PlaceRotator);
            Assert.AreEqual(23, (int)TutorialStep.LoadWaveShape);
        }

        [Test]
        public void TheRunOpensWithTheLineStandingAndTheGapOpen()
        {
            Assert.IsTrue(_world.Machines.TryGetSnapshot(CutterCell, out MachineSnapshot cutter));
            Assert.AreEqual(BehaviorKind.Cutter, cutter.Behavior);
            Assert.IsTrue(_world.Machines.Has(Gun));
            Assert.IsTrue(_world.Belts.Has(LaneBelt));
            Assert.IsFalse(_world.Machines.Has(Gap));
            Assert.IsFalse(_world.Belts.Has(Gap));
            Assert.AreEqual(30, _world.Economy.Shapes, "the line is part of the map, not a purchase");
        }

        [Test]
        public void TheGunStaysEmptyUntilTheGapIsFilled()
        {
            Ticks(1800);
            Assert.IsTrue(_world.Machines.TryGetSnapshot(Gun, out MachineSnapshot gun));
            Assert.IsTrue(gun.Shape.IsEmpty, "no round can reach the gun without the rotator");
        }

        [Test]
        public void TheCutterLineMakesTopHalvesAndOneTurnMakesTheWavesRightHalf()
        {
            WaveDefinition wave = _world.Waves.NextWaveDefinition;
            Shape top = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
            Assert.AreEqual(top.RotatedCW(), wave.Groups[0].Weakness);
            Assert.AreNotEqual(top, wave.Groups[0].Weakness);

            PlayToThePlacement();
            Assert.IsTrue(_world.Tutorial.Current.Step == TutorialStep.PlaceRotator);
            _world.Tick(new InputCommand(cursorCell: Gap, selected: BuildKind.Rotator, rotateSteps: -1));
            Press(Gap);
            Assert.AreEqual(TutorialStep.LoadWaveShape, _world.Tutorial.Current.Step);

            for (int i = 0; i < 1800 && _world.Tutorial.Current.Step == TutorialStep.LoadWaveShape; i++)
                _world.Tick(InputCommand.None);
            Assert.AreEqual(TutorialStep.StartWave, _world.Tutorial.Current.Step);
            Assert.IsTrue(_world.Machines.TryGetSnapshot(Gun, out MachineSnapshot gun));
            Assert.AreEqual(wave.Groups[0].Weakness, gun.Shape, "the real line delivers the turned shape");
        }

        [Test]
        public void TheDefaultFacingCannotCompleteThePlacementStep()
        {
            PlayToThePlacement();
            Assert.AreEqual(Dir.East, _world.PlacementDirection);
            Press(Gap);
            Assert.IsTrue(_world.Machines.Has(Gap));
            Ticks(300);
            Assert.AreEqual(TutorialStep.PlaceRotator, _world.Tutorial.Current.Step, "its arrow must point up");

            _world.Tick(new InputCommand(removeHeld: true, cursorCell: Gap));
            Assert.IsFalse(_world.Machines.Has(Gap), "the player's own rotator can be taken away and redone");
            _world.Tick(InputCommand.None);
            Assert.AreEqual(30, _world.Economy.Shapes);

            _world.Tick(new InputCommand(cursorCell: Gap, selected: BuildKind.Rotator, rotateSteps: -1));
            Press(Gap);
            Assert.AreEqual(TutorialStep.LoadWaveShape, _world.Tutorial.Current.Step);
        }

        [Test]
        public void AGunHoldingAnyOtherShapeCannotPassTheLoadStep()
        {
            var task = new TutorialTask(TutorialStep.LoadWaveShape, "test", Gun, null, false);
            var script = new TutorialScript(null, new[] { task }, coached: true);
            var lesson = new TutorialState(script, _world.Machines, _world.Belts, _world.Economy, _world.Waves);

            lesson.Step();
            Assert.IsFalse(lesson.Complete, "an empty gun");

            _world.Machines.RefAt(Gun).Carried = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
            lesson.Step();
            Assert.IsFalse(lesson.Complete, "a top half is the shape the lesson opened on");

            _world.Machines.RefAt(Gun).Carried = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void OnlyTheRotatorIsHandedOverDuringTheLesson()
        {
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Rotator), "not before the line that introduces it");
            PlayToThePlacement();
            Assert.IsTrue(_world.IsUnlocked(BuildKind.Rotator));
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Belt));
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Cutter));
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Turret));
        }

        [Test]
        public void TheOpeningLineCannotBeRemovedDuringTheLesson()
        {
            foreach (Int2 cell in new[] { LaneBelt, CutterCell, CutterSecondTile, Gun })
            {
                _world.Tick(new InputCommand(removeHeld: true, cursorCell: cell));
                _world.Tick(InputCommand.None);
            }

            Assert.IsTrue(_world.Belts.Has(LaneBelt));
            Assert.IsTrue(_world.Machines.Has(CutterCell));
            Assert.IsTrue(_world.Machines.Has(Gun));
            Assert.AreEqual(30, _world.Economy.Shapes, "nothing was refunded");
        }

        [Test]
        public void TheGuidedFixWinsTheWaveWithTheCoreUntouched()
        {
            PlayToThePlacement();
            _world.Tick(new InputCommand(cursorCell: Gap, selected: BuildKind.Rotator, rotateSteps: -1));
            Press(Gap);
            for (int i = 0; i < 1800 && _world.Tutorial.Current.Step == TutorialStep.LoadWaveShape; i++)
                _world.Tick(InputCommand.None);

            Assert.AreEqual(TutorialStep.StartWave, _world.Tutorial.Current.Step);
            Ticks(300);
            Assert.AreEqual(0, _world.Waves.CurrentWave, "the ready line must not release itself");

            _world.Tick(new InputCommand(startWavePressed: true));
            Assert.AreEqual(1, _world.Waves.CurrentWave);
            Assert.AreEqual(TutorialStep.ClearWave, _world.Tutorial.Current.Step);

            float full = _world.Core.Hp;
            for (int i = 0; i < 5400 && _world.Status == GameStatus.Playing; i++) _world.Tick(InputCommand.None);
            Assert.AreEqual(GameStatus.Won, _world.Status);
            Assert.AreEqual(full, _world.Core.Hp, "one fixed gun holds this wave");
        }

        [Test]
        public void RestartStandsTheLineAgainAndReturnsToTheFirstLine()
        {
            PlayToThePlacement();
            _world.Tick(new InputCommand(cursorCell: Gap, selected: BuildKind.Rotator, rotateSteps: -1));
            Press(Gap);
            Ticks(300);

            _world.Restart();

            Assert.AreEqual(0, _world.Tutorial.StepsCompleted);
            Assert.IsFalse(_world.Machines.Has(Gap));
            Assert.IsTrue(_world.Machines.Has(CutterCell));
            Assert.IsTrue(_world.Machines.Has(Gun));
            Assert.IsTrue(_world.Belts.Has(LaneBelt));
            Assert.AreEqual(0, _world.Waves.CurrentWave);
            Assert.AreEqual(30, _world.Economy.Shapes);
            Ticks(1800);
            Assert.IsTrue(_world.Machines.TryGetSnapshot(Gun, out MachineSnapshot gun));
            Assert.IsTrue(gun.Shape.IsEmpty, "the restarted line still waits at the gap");
        }

        [Test]
        public void RotatorSelectionIsExcludedFromMetricThreeButItsOperationsAreIncluded()
        {
            Assert.IsFalse(ProgressMetricsReporter.IsKeyTask(TutorialStep.SelectRotator));
            Assert.IsTrue(ProgressMetricsReporter.IsKeyTask(TutorialStep.PlaceRotator));
            Assert.IsTrue(ProgressMetricsReporter.IsKeyTask(TutorialStep.LoadWaveShape));
        }

        /// <summary>Reads the two explanations and picks the rotator, which leaves the lesson on its placement.</summary>
        private void PlayToThePlacement()
        {
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(TutorialStep.Acknowledge, _world.Tutorial.Current.Step);
                _world.RequestTutorialNext();
                _world.Tick(InputCommand.None);
            }

            _world.Tick(new InputCommand(selected: BuildKind.Rotator));
            Assert.AreEqual(TutorialStep.PlaceRotator, _world.Tutorial.Current.Step);
        }

        private void Press(Int2 cell)
        {
            _world.Tick(new InputCommand(buildHeld: true, cursorCell: cell, selected: BuildKind.Rotator));
            _world.Tick(new InputCommand(cursorCell: cell, selected: BuildKind.Rotator, primaryReleased: true));
        }

        private void Ticks(int count)
        {
            for (int i = 0; i < count; i++) _world.Tick(InputCommand.None);
        }
    }
}
