using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The ground as the simulation reads it: what a map's terrain data becomes in a world, and what
    /// the three things that move do about it. Rock, water and the void turned "is a machine here" into
    /// a layer of its own, and these pin the rules that layer holds to.
    /// </summary>
    public class TerrainTests
    {
        [Test]
        public void Terrain_StampsTheMapsGround_AndOnlyTheMaps()
        {
            // A rock field, a void band along the top, bare ground elsewhere: the three answers a world
            // gives back, including the default for a map that never said.
            SimWorld world = TerrainWorld(12, 10, TerrainLayout.Rects(12, 10,
                new TerrainRect(new Int2(2, 2), 6, 4, TerrainKind.Rock),
                new TerrainRect(new Int2(0, 8), 12, 2, TerrainKind.Void)));

            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(new Int2(3, 4)), "the ridge is rock");
            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(new Int2(2, 5)), "all the way across it");
            Assert.AreEqual(TerrainKind.Void, world.Terrain.KindAt(new Int2(1, 9)), "the band is void");
            Assert.AreEqual(TerrainKind.Ground, world.Terrain.KindAt(new Int2(10, 7)), "and the rest is ground");

            // A map with no terrain array is plain ground: the shape every map used to be, and what keeps
            // "a map with no obstacles" from being a special case anywhere.
            SimWorld plain = Sim.NewWorld(12, 10);
            Assert.IsNull(plain.Map.Terrain);
            Assert.AreEqual(TerrainKind.Ground, plain.Terrain.KindAt(new Int2(0, 0)));
            Assert.AreEqual(TerrainKind.Ground, plain.Terrain.KindAt(new Int2(11, 9)));
        }

        [Test]
        public void Terrain_TheCoreKeepsItsTiles_EvenWhereTheMapDisagrees()
        {
            // The Core is stamped before the ground, so a map whose terrain covers the Core's own tiles
            // still produces a playable world - the thing being defended cannot be buried by rock the
            // author drew over it. MapValidation is what tells the author; this asserts the world, at
            // least, is not broken by it.
            var core = new Int2(4, 3);
            SimWorld world = TerrainWorld(12, 10,
                TerrainLayout.Rects(12, 10, new TerrainRect(core, 4, 4, TerrainKind.Rock)));

            Assert.AreEqual(core, world.Core.Cell, "the Core is centred, this map's size, as it always was");

            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    var cell = new Int2(core.X + x, core.Y + y);
                    Assert.AreEqual(TileKind.Core, world.TileGrid.Get(cell), cell + " is part of the Core");
                    Assert.AreEqual(TerrainKind.Ground, world.Terrain.KindAt(cell),
                        "and the ground under it was left alone");
                }
            }

            Assert.IsNotEmpty(MapValidation.Validate(world.Map),
                "a rock drawn over the Core is a map problem, and the validator is what says so");
        }

        [Test]
        public void Terrain_AMapCanPutItsCoreWhereItLikes()
        {
            // The centre of a rectangle is a fine place for a tutorial's Core and a poor one for an
            // irregular map, so the map states it and the world obeys.
            var map = new MapDefinition("corner", 12, 10, Sim.TestShapes, new ShapePatch[0],
                new Int2[0], new WaveDefinition[0],
                TerrainLayout.Rects(12, 10, new TerrainRect(new Int2(0, 6), 4, 4, TerrainKind.Rock)),
                coreCell: new Int2(2, 2));

            var world = new SimWorld(map, new SimConfig());
            Assert.AreEqual(new Int2(2, 2), world.Core.Cell);
            Assert.AreEqual(TileKind.Core, world.TileGrid.Get(new Int2(3, 3)));

            // And the map can put it in the corner of an irregular map: the block still fits, and the
            // void it sits beside is not ground.
            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(new Int2(0, 6)));
        }

        [Test]
        public void Terrain_RockIsAWallToWalkers_SoTheWaveFindsTheGate()
        {
            // The claim a map's own rock makes: a wave walks around it in the flow field without the
            // player building anything. A full-height rock wall with one gate at the bottom, and a walker
            // on the far side: if the ground were not read by the path field the walker would cross the
            // rock, and if the gate were not read it would never arrive.
            const int width = 20;
            const int height = 12;

            SimWorld world = TerrainWorld(width, height, TerrainLayout.Rects(width, height,
                new TerrainRect(new Int2(4, 2), 1, height - 2, TerrainKind.Rock)));

            Assert.Greater(world.Enemies.Spawn(EnemyKind.Runner, new Vec2(0.5f, 5.5f), Sim.Circle), 0);

            var enemies = new List<EnemySnapshot>();
            int ticks = (int)(90 * SimConfig.TickRate);
            for (int i = 0; i < ticks && world.Core.Hp >= world.Config.CoreMaxHp; i++)
            {
                world.Tick(InputCommand.None);

                world.Enemies.GetEnemies(enemies);
                for (int e = 0; e < enemies.Count; e++)
                {
                    Int2 cell = world.TileGrid.CellAt(enemies[e].Position);
                    Assert.IsTrue(world.Terrain.Walkable(cell),
                        "a walker stood at " + cell + " - rock is not ground a wave may cross");
                }
            }

            Assert.Less(world.Core.Hp, world.Config.CoreMaxHp,
                "the wave went round by the gate instead of standing at the wall");
        }

        [Test]
        public void Terrain_NothingIsBuiltOnTheMapsOwnGround()
        {
            SimWorld world = TerrainWorld(12, 10, TerrainLayout.Rects(12, 10,
                new TerrainRect(new Int2(2, 2), 2, 2, TerrainKind.Rock),
                new TerrainRect(new Int2(6, 2), 2, 2, TerrainKind.Water),
                new TerrainRect(new Int2(9, 8), 2, 2, TerrainKind.Void)));

            var rock = new Int2(2, 2);
            var water = new Int2(6, 2);
            var hole = new Int2(9, 8);
            var bare = new Int2(10, 4);

            foreach (Int2 cell in new[] { rock, water, hole })
            {
                Assert.IsFalse(world.CanPlace(BuildKind.Turret, cell, Dir.East), "no machine on " + cell);
                Assert.IsFalse(world.CanPlace(BuildKind.Wall, cell, Dir.East), "not even a wall on " + cell);
                Assert.IsFalse(world.CanPlaceBelt(cell), "and no belt across " + cell);
            }

            Assert.IsTrue(world.CanPlace(BuildKind.Turret, bare, Dir.East), "plain ground takes anything");
            Assert.IsTrue(world.CanPlaceBelt(bare), "including a belt");

            // Ore is the one ground with an exception, and it is the drill's alone.
            var ore = new Int2(4, 8);
            world.Terrain.SetOre(ore, Quad.Circle);
            Assert.IsTrue(world.CanPlace(BuildKind.Drill, ore, Dir.East), "a vein takes the machine that mines it");
            Assert.IsFalse(world.CanPlace(BuildKind.Turret, ore, Dir.East));
            Assert.IsTrue(world.CanPlaceBelt(ore), "and transport still crosses it");
        }

        [Test]
        public void Terrain_ABeltFacingRock_StallsAtFullProgress()
        {
            // A belt pointed at something that will never take an item settles at progress 1 - the same
            // as at a wall the player built. Rock does not eat the item and does not jam the line; it is
            // simply not a destination, which is what the last cell of a wrong turn looks like.
            SimWorld world = TerrainWorld(12, 10,
                TerrainLayout.Rects(12, 10, new TerrainRect(new Int2(6, 2), 2, 2, TerrainKind.Rock)));

            var belt = new Int2(5, 2);
            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.Belts.TrySpawnItem(belt, Sim.Circle));

            Sim.Tick(world, (int)(5 * SimConfig.TickRate));

            Assert.IsTrue(world.Belts.HasItemAt(belt), "the item is still on the line, waiting");
            Assert.AreEqual(1f, Sim.ProgressAt(world, belt), Sim.ProgressTol, "parked at the far edge");
            Assert.AreEqual(0, world.Belts.JamCount, "rock is not a wrong-shape delivery");
        }

        [Test]
        public void Terrain_Restart_LeavesTheGroundExactlyAsTheMapDrewIt()
        {
            // Terrain is the map, and nothing the player does may change it - including the one action
            // that wipes the run. A restart rebuilds from the map's own data, so the vein has to be in that
            // data to come back: this is re-stamping, not a leftover in a field.
            var ore = new Int2(10, 8);
            var map = new MapDefinition("terrain", 16, 12, Sim.TestShapes,
                new[] { new ShapePatch(ore, 2, 1, Quad.Square) }, new Int2[0], new WaveDefinition[0],
                TerrainLayout.Rects(16, 12, new TerrainRect(new Int2(1, 1), 3, 3, TerrainKind.Water)));

            var world = new SimWorld(map, new SimConfig());

            Sim.Place(world, BuildKind.Drill, ore, Dir.West);
            Assert.IsTrue(world.TryPlaceBelt(new Int2(6, 8), Dir.East));

            world.Tick(new InputCommand(restartPressed: true));

            Assert.AreEqual(TerrainKind.Water, world.Terrain.KindAt(new Int2(2, 2)), "the pond is still there");
            Assert.IsTrue(world.Terrain.HasOre(ore), "and so is the vein");
            Assert.AreEqual(Quad.Square, world.Terrain.OreAt(ore), "with its own mineral");
            Assert.AreEqual(TileKind.Empty, world.TileGrid.Get(ore), "with nothing standing on it");
            Assert.IsTrue(world.TryPlace(BuildKind.Drill, ore, Dir.West), "so it can be drilled again");
        }

        /// <summary>A world on a map whose whole point is its ground: no ore but what a test stamps, no
        /// waves, and a stockpile that never runs dry.</summary>
        private static SimWorld TerrainWorld(int width, int height, TerrainKind[] terrain)
            => new SimWorld(new MapDefinition("terrain", width, height, Sim.TestShapes,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0], terrain), new SimConfig());

        private static bool Has(TerrainKind[] ground, TerrainKind kind)
        {
            for (int i = 0; i < ground.Length; i++)
                if (ground[i] == kind) return true;

            return false;
        }
    }
}
