using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>Playtest batch one, in the simulation: the first lesson's Core holds instead of falling, and a turret
    /// is told apart by whether its range reaches the road the enemies walk.</summary>
    public class PlaytestBatchOneTests
    {
        /// <summary>A 20x12 map, Core at (8,4)-(11,7), one gate on the right edge at (19,6).</summary>
        private static SimWorld World(bool coreHolds)
        {
            var map = new MapDefinition("batch", 20, 12, 200, new ShapePatch[0], new[] { new Int2(19, 6) },
                new[] { new WaveDefinition(0f) }, coreHolds: coreHolds);
            return new SimWorld(map, new SimConfig());
        }

        [Test]
        public void AHoldingCore_StopsAtOne_AndTheRunGoesOn()
        {
            SimWorld world = World(coreHolds: true);
            world.Core.Hp = 0.5f;
            world.Tick(InputCommand.None);

            Assert.AreEqual(1f, world.Core.Hp, 1e-4f, "the Core stops at 1");
            Assert.AreEqual(GameStatus.Playing, world.Status, "and the run is not lost");
            Assert.AreEqual(1, world.CoreSaves, "the HUD has a save to say");

            world.Core.Hp = 0.2f;
            world.Tick(InputCommand.None);
            Assert.AreEqual(1, world.CoreSaves, "a Core already at the floor is not saved twice");

            world.Restart();
            Assert.AreEqual(0, world.CoreSaves, "a new run starts with none");
        }

        [Test]
        public void AnOrdinaryCore_StillFalls()
        {
            SimWorld world = World(coreHolds: false);
            world.Core.Hp = 0f;
            world.Tick(InputCommand.None);

            Assert.AreEqual(GameStatus.Lost, world.Status);
            Assert.AreEqual(0, world.CoreSaves);
        }

        [Test]
        public void OnlyTheFirstLesson_HoldsItsCore()
        {
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/Tutorial.asset").ToCore().CoreHolds);
            Assert.IsFalse(AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/Workshop.asset").ToCore().CoreHolds);
            Assert.IsFalse(AssetDatabase.LoadAssetAtPath<MapAsset>("Assets/Data/Maps/MainMap.asset").ToCore().CoreHolds);
        }

        [Test]
        public void ATurretBesideTheRoad_ReachesIt_AndOneAcrossTheMap_DoesNot()
        {
            SimWorld world = World(coreHolds: false);
            float range = world.Content.Turret(BuildKind.Turret).Range;
            Assert.Less(range, 12f, "this test's far corner has to be out of a turret's range");

            Assert.IsTrue(world.TurretReachesEnemies(new Int2(15, 8), BuildKind.Turret),
                "two tiles off the road from the gate to the Core");
            Assert.IsFalse(world.TurretReachesEnemies(new Int2(0, 11), BuildKind.Turret),
                "the far top-left corner: the road runs from the right edge to the Core, more than a range away");
        }

        [Test]
        public void ABuildingThatIsNotAGun_IsNeverOutOfReach()
        {
            SimWorld world = World(coreHolds: false);
            Assert.IsTrue(world.TurretReachesEnemies(new Int2(0, 0), BuildKind.Belt));
        }

    }
}
