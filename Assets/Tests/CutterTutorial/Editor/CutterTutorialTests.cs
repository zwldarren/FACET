using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Facet.Tests
{
    public sealed class CutterTutorialTests
    {
        private static readonly Int2 CutterCell = new Int2(10, 6);
        private static readonly Int2 UpperExit = new Int2(11, 6);
        private static readonly Int2 LowerExit = new Int2(11, 5);
        private SimWorld _world;

        private static MapAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/CutterTutorial.asset");
            Assert.IsNotNull(asset);
            return asset;
        }

        [SetUp]
        public void SetUp() => _world = new SimWorld(Load().ToCore(), new SimConfig());

        [Test]
        public void CutterLessonIsAValidSmallCoachedMapWithOneWave()
        {
            MapAsset asset = Load();
            var problems = asset.Validate();
            Assert.AreEqual(0, problems.Count, problems.Count > 0 ? problems[0].ToString() : "");
            Assert.IsTrue(asset.TutorialCoached);
            Assert.AreEqual(1, asset.Waves.Length);
            Assert.AreEqual(24, asset.Width);
            Assert.AreEqual(16, asset.Height);
        }

        [Test]
        public void ExistingSerializedStepNumbersArePreserved()
        {
            Assert.AreEqual(0, (int)TutorialStep.PlaceDrill);
            Assert.AreEqual(3, (int)TutorialStep.MakeHalf);
            Assert.AreEqual(14, (int)TutorialStep.ClearWave);
            Assert.Greater((int)TutorialStep.SelectCutter, 14);
            Assert.Greater((int)TutorialStep.PlaceCutterTurrets, 14);
        }

        [Test]
        public void EveryPopupCanCompleteAndTheFirstWaveWaitsForThePlayer()
        {
            int guard = 0;
            while (!_world.Tutorial.Complete && guard++ < 40)
            {
                int before = _world.Tutorial.StepsCompleted;
                TutorialTask task = _world.Tutorial.Current;
                if (task.Step != TutorialStep.ClearWave)
                    Assert.AreEqual(0, _world.Waves.CurrentWave, "no wave during guided setup");
                if (task.Step == TutorialStep.ClearWave)
                {
                    // The shipped world stops ticking at Won. Its final ClearWave popup is
                    // replaced by the coach's victory panel, just like the original tutorial.
                    for (int i = 0; i < 3600 && _world.Status == GameStatus.Playing; i++)
                        _world.Tick(InputCommand.None);
                    Assert.AreEqual(GameStatus.Won, _world.Status, "guided layout must win its short wave");
                    break;
                }
                if (task.Step == TutorialStep.Acknowledge)
                {
                    _world.RequestTutorialNext();
                    _world.Tick(InputCommand.None);
                }
                else if (task.Step == TutorialStep.SelectTurret || task.Step == TutorialStep.SelectCutter)
                {
                    BuildKind kind = task.Step == TutorialStep.SelectTurret ? BuildKind.Turret : BuildKind.Cutter;
                    _world.Tick(new InputCommand(selected: kind));
                }
                else if (task.Step == TutorialStep.StartWave)
                {
                    for (int i = 0; i < 300; i++) _world.Tick(InputCommand.None);
                    Assert.AreEqual(0, _world.Waves.CurrentWave, "ready popup must not release itself");
                    _world.Tick(new InputCommand(startWavePressed: true));
                }
                else
                {
                    foreach (TutorialGhost ghost in task.Ghost)
                    {
                        if (_world.Belts.Has(ghost.Cell) || _world.Machines.Has(ghost.Cell)) continue;
                        Place(ghost.Build, ghost.Cell, ghost.Direction);
                    }
                    for (int i = 0; i < 3600 && _world.Tutorial.StepsCompleted == before; i++)
                        _world.Tick(InputCommand.None);
                }
                Assert.Greater(_world.Tutorial.StepsCompleted, before, "stuck at " + task.Title);
            }
            Assert.GreaterOrEqual(_world.Tutorial.StepsCompleted, 15);
            Assert.IsTrue(_world.Waves.Finished);
            Assert.AreEqual(GameStatus.Won, _world.Status);
            Assert.Greater(_world.Core.Hp, 0f);
        }

        [Test]
        public void PickingTheCutterRequiresItsSelection()
        {
            var lesson = Single(TutorialStep.SelectCutter, CutterGhost());
            lesson.NoteSelection(BuildKind.Belt);
            lesson.Step();
            Assert.IsFalse(lesson.Complete);
            lesson.NoteSelection(BuildKind.Cutter);
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void ASecondCutterElsewhereDoesNotCompleteTheHighlightedPlacement()
        {
            var lesson = Single(TutorialStep.PlaceCutter, CutterGhost());
            Assert.IsTrue(_world.Machines.TryPlace(new Int2(7, 10), BuildKind.Cutter, Dir.East));
            lesson.Step();
            Assert.IsFalse(lesson.Complete);
            PutCutter();
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void AWrongFacingCannotCompleteThePlacementStep()
        {
            var lesson = Single(TutorialStep.PlaceCutter, CutterGhost());
            Assert.IsTrue(_world.Machines.TryPlace(CutterCell, BuildKind.Cutter, Dir.North));
            lesson.Step();
            Assert.IsFalse(lesson.Complete);
            Assert.IsTrue(_world.Machines.TryRemove(CutterCell));
            PutCutter();
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void EmptyInputDoesNotCountAsFeedingTheCutter()
        {
            PutCutter();
            var lesson = Single(TutorialStep.FeedCutter, CutterGhost());
            lesson.Step();
            Assert.IsFalse(lesson.Complete);
            _world.Machines.RefAt(CutterCell).Carried = Shape.Full(Quad.Circle);
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void UpperOutputRequiresTheWholeGhostRouteAndCorrectArrows()
        {
            PutCutter();
            var end = new Int2(12, 6);
            var lesson = Single(TutorialStep.WireCutterPrimary,
                new TutorialGhost(BuildKind.Belt, UpperExit, Dir.East),
                new TutorialGhost(BuildKind.Belt, end, Dir.East));
            Assert.IsTrue(_world.Belts.TryPlace(UpperExit, Dir.East));
            lesson.Step();
            Assert.IsFalse(lesson.Complete, "one exit tile must not hide the unfinished route");
            Assert.IsTrue(_world.Belts.TryPlace(end, Dir.West));
            lesson.Step();
            Assert.IsFalse(lesson.Complete, "backwards arrow is not a completed route");
            Assert.IsTrue(_world.Belts.TrySetDirection(end, Dir.East));
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void LowerOutputRequiresBothExitsOnTheSameCutter()
        {
            PutCutter();
            var lesson = Single(TutorialStep.WireCutterSecondary,
                new TutorialGhost(BuildKind.Belt, LowerExit, Dir.East));
            Assert.IsTrue(_world.Belts.TryPlace(LowerExit, Dir.East));
            lesson.Step();
            Assert.IsFalse(lesson.Complete);
            Assert.IsTrue(_world.Belts.TryPlace(UpperExit, Dir.East));
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void BothHighlightedGunsMustBePlaced()
        {
            var first = new Int2(14, 8);
            var second = new Int2(15, 8);
            var lesson = Single(TutorialStep.PlaceCutterTurrets,
                new TutorialGhost(BuildKind.Turret, first, Dir.North),
                new TutorialGhost(BuildKind.Turret, second, Dir.North));
            Assert.IsTrue(_world.Machines.TryPlace(first, BuildKind.Turret, Dir.North));
            Assert.IsTrue(_world.Machines.TryPlace(new Int2(20, 13), BuildKind.Turret, Dir.North));
            lesson.Step();
            Assert.IsFalse(lesson.Complete, "a gun elsewhere is not the second highlighted gun");
            Assert.IsTrue(_world.Machines.TryPlace(second, BuildKind.Turret, Dir.North));
            lesson.Step();
            Assert.IsTrue(lesson.Complete);
        }

        [Test]
        public void CutterSelectionIsExcludedFromMetricThreeButItsOperationsAreIncluded()
        {
            Assert.IsFalse(ProgressMetricsReporter.IsKeyTask(TutorialStep.SelectCutter));
            Assert.IsTrue(ProgressMetricsReporter.IsKeyTask(TutorialStep.PlaceCutter));
            Assert.IsTrue(ProgressMetricsReporter.IsKeyTask(TutorialStep.WireCutterSecondary));
        }

        [Test]
        public void RestartReturnsToTheFirstPopupWithNoCutterAndNoWave()
        {
            _world.RequestTutorialNext();
            _world.Tick(InputCommand.None);
            PutCutter();
            _world.Restart();
            Assert.AreEqual(0, _world.Tutorial.StepsCompleted);
            Assert.IsFalse(_world.Machines.Has(CutterCell));
            Assert.AreEqual(0, _world.Waves.CurrentWave);
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Cutter));
        }

        [Test]
        public void CutterLessonFitsTheOriginalFixedTutorialCamera()
        {
            var go = new GameObject("Cutter tutorial camera test");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                camera.aspect = 16f / 9f;
                CameraRig rig = go.AddComponent<CameraRig>();
                rig.Initialize(new TileGrid(48, 72), new Vec2(24, 7));
                rig.FrameWholeMap(24, 16, 12f);
                Assert.GreaterOrEqual(camera.orthographicSize, 10.125f);
                Assert.LessOrEqual(camera.orthographicSize, 18f);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheLessonIsKept_ButOutOfTheCampaign()
        {
            // The main map teaches the Cutter in its own stage 3, so the standalone lesson is off the list. The
            // asset stays, valid, for a rollback.
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>("Assets/Data/Campaign.asset");
            Assert.IsNotNull(campaign);
            CollectionAssert.DoesNotContain(campaign.Maps, Load());
        }

        private TutorialState Single(TutorialStep step, params TutorialGhost[] ghost)
        {
            var task = new TutorialTask(step, "test", CutterCell, null, false, ghost: ghost);
            var script = new TutorialScript(null, new[] { task }, coached: true);
            return new TutorialState(script, _world.Machines, _world.Belts, _world.Economy, _world.Waves);
        }

        private static TutorialGhost CutterGhost()
            => new TutorialGhost(BuildKind.Cutter, CutterCell, Dir.East);

        private void PutCutter()
            => Assert.IsTrue(_world.Machines.TryPlace(CutterCell, BuildKind.Cutter, Dir.East));

        private void Place(BuildKind kind, Int2 cell, Dir facing)
        {
            // Instant test clicks skip reading time; let the working Core belt earn any missing coins.
            for (int i = 0; i < 1200 && !_world.Economy.CanAfford(kind); i++)
                _world.Tick(InputCommand.None);
            Assert.IsTrue(_world.Economy.CanAfford(kind), "Core income cannot fund " + kind);
            int turns = ((int)facing - (int)_world.PlacementDirection + 4) & 3;
            _world.Tick(new InputCommand(buildHeld: true, cursorCell: cell, selected: kind, rotateSteps: turns));
            _world.Tick(new InputCommand(cursorCell: cell, selected: kind, primaryReleased: true));
            Assert.IsTrue(kind == BuildKind.Belt ? _world.Belts.Has(cell) : _world.Machines.Has(cell),
                "could not place " + kind + " at " + cell);
        }
    }
}
