using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    public sealed class FirstStandTests
    {
        private static readonly Int2 FirstGun = new Int2(11, 10);
        private static readonly Int2 SecondGun = new Int2(13, 10);
        private static readonly Int2 CutterCell = new Int2(15, 13);
        private static readonly Int2 TopGun = new Int2(17, 10);
        private static readonly Int2 BottomGun = new Int2(16, 10);
        private const int TimerTicks = (int)(90 * SimConfig.TickRate);
        private SimWorld _world;

        private static MapAsset Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/FirstStand.asset");
            Assert.IsNotNull(asset);
            return asset;
        }

        [SetUp]
        public void SetUp() => _world = new SimWorld(Load().ToCore(), new SimConfig());

        [Test]
        public void FirstStandIsAValidSmallCoachedMapWithOneWave()
        {
            MapAsset asset = Load();
            var problems = asset.Validate();
            Assert.AreEqual(0, problems.Count, problems.Count > 0 ? problems[0].ToString() : "");
            Assert.IsTrue(asset.TutorialCoached);
            Assert.IsTrue(asset.TutorialClockRuns);
            Assert.AreEqual(1, asset.Waves.Length);
            Assert.AreEqual(1, asset.TutorialTasks.Length, "the player builds it alone");
            Assert.AreEqual(0, asset.TutorialPrebuilt.Length);
        }

        [Test]
        public void CampaignPlacesItAfterTheCutterAndBeforeTheRotator()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>("Assets/Data/Campaign.asset");
            Assert.AreEqual("Cutter Tutorial", campaign.Maps[1].Name);
            Assert.AreSame(Load(), campaign.Maps[2]);
            Assert.AreEqual("Rotator Tutorial", campaign.Maps[3].Name);
        }

        [Test]
        public void TheLevelHandsOverWhatTheLessonsTaughtAndNoRotator()
        {
            Assert.AreEqual(Operators.Cutter, _world.Map.Unlocked);
            Assert.IsTrue(_world.IsUnlocked(BuildKind.Belt));
            Assert.IsTrue(_world.IsUnlocked(BuildKind.Cutter));
            Assert.IsTrue(_world.IsUnlocked(BuildKind.Turret));
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Rotator));
            Assert.IsFalse(_world.IsUnlocked(BuildKind.Stacker));
        }

        [Test]
        public void TheTimerWaitsForTheFirstBuilding()
        {
            Ticks(3600);
            Assert.AreEqual(WavePhase.Held, _world.Waves.Phase);
            Assert.AreEqual(0, _world.Waves.CurrentWave);
        }

        [Test]
        public void TheWaveArrivesWhenTheTimerRunsOut()
        {
            Belts((11, 3, Dir.West));
            Ticks(TimerTicks - 30);
            Assert.AreEqual(WavePhase.Intermission, _world.Waves.Phase);
            Assert.AreEqual(0, _world.Waves.CurrentWave);

            Ticks(60);
            Assert.AreEqual(1, _world.Waves.CurrentWave);
        }

        [Test]
        public void NCallsTheWaveEarly()
        {
            Belts((11, 3, Dir.West));
            _world.Tick(new InputCommand(startWavePressed: true));
            Assert.AreEqual(1, _world.Waves.CurrentWave);
        }

        [Test]
        public void TheOtherLessonsStillHoldTheirClocks()
        {
            Assert.IsFalse(LessonLeavesTheClockAlone("Tutorial"));
            Assert.IsFalse(LessonLeavesTheClockAlone("CutterTutorial"));
            Assert.IsFalse(LessonLeavesTheClockAlone("RotatorTutorial"));
        }

        [Test]
        public void AnUndefendedCoreFalls()
        {
            Fight();
            Assert.AreEqual(GameStatus.Lost, _world.Status);
        }

        [Test]
        public void OneGunIsNotEnough()
        {
            FirstGunLine();
            MoneyLine();
            Ticks(300);
            Fight();
            Assert.AreEqual(GameStatus.Lost, _world.Status);
        }

        [Test]
        public void TwoGunsOnWholeCirclesWinWithoutACutter()
        {
            int shots = WholeCircleShots();
            Assert.AreEqual(GameStatus.Won, _world.Status);
            Assert.Greater(shots, 0);
        }

        [Test]
        public void TwoGunsHoldWhenTheTimerBringsTheWave()
        {
            TwoGuns();
            Ticks(TimerTicks + 5400);
            Assert.AreEqual(GameStatus.Won, _world.Status);
        }

        [Test]
        public void TheCutterLineWinsWithoutARotatorAndNeedsFewerShots()
        {
            var circles = new FirstStandTests { _world = new SimWorld(Load().ToCore(), new SimConfig()) };
            int circleShots = circles.WholeCircleShots();

            MoneyLine();
            Afford(15);
            Assert.IsTrue(_world.TryPlace(BuildKind.Cutter, CutterCell, Dir.East));
            Belts((11, 13, Dir.East), (12, 13, Dir.East), (13, 13, Dir.East), (14, 13, Dir.East),
                (16, 13, Dir.East), (17, 13, Dir.South), (17, 12, Dir.South), (17, 11, Dir.South),
                (16, 12, Dir.South), (16, 11, Dir.South));
            Gun(TopGun);
            Gun(BottomGun);
            Ticks(600);
            Assert.AreEqual(0, _world.Waves.CurrentWave, "the cutter line fits inside the timer");

            Assert.IsTrue(_world.Machines.TryGetSnapshot(TopGun, out MachineSnapshot top));
            Assert.IsTrue(_world.Machines.TryGetSnapshot(BottomGun, out MachineSnapshot bottom));
            Assert.AreEqual(Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty), top.Shape);
            Assert.AreEqual(Shape.Of(Quad.Empty, Quad.Empty, Quad.Circle, Quad.Circle), bottom.Shape);

            Fight();
            Assert.AreEqual(GameStatus.Won, _world.Status);
            Assert.Less(_world.ShotsFired, circleShots, "the matched shape is what the wave rewards");
        }

        private int WholeCircleShots()
        {
            TwoGuns();
            Assert.AreEqual(0, _world.Waves.CurrentWave, "two guns fit inside the timer");
            Fight();
            return _world.ShotsFired;
        }

        private void TwoGuns()
        {
            FirstGunLine();
            MoneyLine();
            Belts((13, 12, Dir.South), (13, 11, Dir.South));
            Gun(SecondGun);
            Ticks(300);
        }

        private static bool LessonLeavesTheClockAlone(string map)
            => AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/" + map + ".asset").TutorialClockRuns;

        private void FirstGunLine()
        {
            Belts((11, 12, Dir.South), (11, 11, Dir.South));
            Gun(FirstGun);
        }

        private void MoneyLine()
        {
            Belts((11, 3, Dir.West), (10, 3, Dir.West), (9, 3, Dir.West), (8, 3, Dir.West), (7, 3, Dir.West),
                (6, 3, Dir.North), (6, 4, Dir.North), (6, 5, Dir.North));
        }

        private void Gun(Int2 cell)
        {
            Afford(20);
            Assert.IsTrue(_world.TryPlace(BuildKind.Turret, cell, Dir.North));
        }

        private void Belts(params (int x, int y, Dir direction)[] cells)
        {
            foreach (var (x, y, direction) in cells)
            {
                Afford(1);
                Assert.IsTrue(_world.TryPlaceBelt(new Int2(x, y), direction));
            }
        }

        private void Afford(int shapes)
        {
            for (int i = 0; i < 18000 && _world.Economy.Shapes < shapes; i++) _world.Tick(InputCommand.None);
            Assert.GreaterOrEqual(_world.Economy.Shapes, shapes);
        }

        private void Fight()
        {
            _world.Tick(new InputCommand(startWavePressed: true));
            for (int i = 0; i < 5400 && _world.Status == GameStatus.Playing; i++) _world.Tick(InputCommand.None);
        }

        private void Ticks(int count)
        {
            for (int i = 0; i < count; i++) _world.Tick(InputCommand.None);
        }
    }
}
