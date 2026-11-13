using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>A round that is not the enemy's weakness does a share of the gun's damage set by the map: a half by
    /// default, a quarter on the maps that teach cutting and turning, where the right shape has to be worth it.</summary>
    public class MismatchDamageTests
    {
        private static readonly Shape Top = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);

        private static float HitWith(float mismatch, Shape ammo)
        {
            var map = new MapDefinition("damage", 20, 12, 80, new ShapePatch[0], new[] { new Int2(19, 6) },
                new WaveDefinition[0], mismatchDamage: mismatch);
            var world = new SimWorld(map, new SimConfig());
            int id = world.Enemies.Spawn(EnemyKind.Runner, new Vec2(15f, 6f), Top);
            float hp = world.Content.Enemy(EnemyKind.Runner).Hp;
            float before = Fraction(world, id);
            world.Enemies.ApplyDamage(id, 4f, ammo);
            return (before - Fraction(world, id)) * hp;
        }

        private static float Fraction(SimWorld world, int id)
        {
            var all = new System.Collections.Generic.List<EnemySnapshot>();
            world.Enemies.GetEnemies(all);
            foreach (EnemySnapshot e in all)
                if (e.Id == id) return e.HealthFraction;
            return 0f;
        }

        [Test]
        public void ByDefault_AMismatchedRoundDoesAHalf()
        {
            var map = new MapDefinition("plain", 20, 12, 80, new ShapePatch[0], new Int2[0], new WaveDefinition[0]);
            Assert.AreEqual(0.5f, map.MismatchDamage, 1e-4f);
            Assert.AreEqual(2f, HitWith(map.MismatchDamage, Shape.Full(Quad.Circle)), 1e-4f);
        }

        [Test]
        public void AMapCanLowerIt_ToAQuarter()
        {
            Assert.AreEqual(1f, HitWith(0.25f, Shape.Full(Quad.Circle)), 1e-4f, "a quarter of 4");
            Assert.AreEqual(4f, HitWith(0.25f, Top), 1e-4f, "and a matched round is untouched");
        }

        [Test]
        public void TheEndlessCopy_KeepsIt()
        {
            var map = new MapDefinition("x", 20, 12, 80, new ShapePatch[0], new Int2[0], new WaveDefinition[0],
                mismatchDamage: 0.25f);
            Assert.AreEqual(0.25f, map.WithEndless().MismatchDamage, 1e-4f);
        }

        [TestCase("Assets/Data/Maps/Workshop.asset", 0.25f)]
        [TestCase("Assets/Data/Maps/MainMap.asset", 0.25f)]
        [TestCase("Assets/Data/Maps/Tutorial.asset", 0.5f)]
        public void TheShippedMaps_SayTheirShare(string path, float share)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>(path);
            Assert.IsNotNull(asset, path);
            Assert.AreEqual(share, asset.ToCore().MismatchDamage, 1e-4f);
        }

        [Test]
        public void TheTurretsCard_SaysTheMapsShare()
        {
            ContentDatabase content = ContentDatabase.Default;
            MachineDef turret = content.Machine(BuildKind.Turret);
            StringAssert.Contains("25%", MachineReading.Blurb(turret, content, 0.25f));
            StringAssert.Contains("1 if not", MachineReading.Stats(turret, content, new SimConfig(), 0.25f));
        }
    }
}
