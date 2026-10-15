using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The wave's routing judgement: a route under standing turret fire costs one plain step more per
    /// covered cell, so a wave flanks a swept corridor, overruns a gun it cannot walk around, and reads
    /// a fallen gun as a route reopened; and between two machines it notices it hunts the turret, but
    /// chews anything actually in reach first. The shared geometry is two rock corridors (south y=6..8,
    /// north y=18..20 on a 48x28 map) joined only by the climb column x=0.
    /// </summary>
    public class ThreatFlowTests
    {
        private const int W = 48;
        private const int H = 28;

        /// <summary>The south corridor's mouth: the straight route's first cell.</summary>
        private static readonly Vec2 Spawn = new Vec2(2.5f, 7.5f);

        /// <summary>The rock: a middle ridge (y=9..17) between the corridors and a slab under each, stopping
        /// one column short of the west edge so the climb column at x=0 stays open.</summary>
        private static readonly TerrainRect Ridge = new TerrainRect(new Int2(1, 9), 25, 9, TerrainKind.Rock);
        private static readonly TerrainRect SouthSlab = new TerrainRect(new Int2(1, 0), 25, 6, TerrainKind.Rock);
        private static readonly TerrainRect NorthSlab = new TerrainRect(new Int2(1, 21), 25, 7, TerrainKind.Rock);

        /// <summary>Two turrets covering the walked length of the south corridor (x=3..26 at y=7, gun cells
        /// walked around), so the straight route pays roughly a step of threat most of its way; the mouth cell
        /// stays outside range 7 and the 47-step climb route clears the 0.8x+2 budget by under two steps - the
        /// decision boundary the balance constants define, and the canary for moving them.</summary>
        private static readonly Int2[] Guns = { new Int2(10, 7), new Int2(20, 7) };

        private static SimWorld EnduringWorld(params TerrainRect[] rects)
            => new SimWorld(
                new MapDefinition("threat", W, H, Sim.TestShapes, new ShapePatch[0],
                    new[] { new Int2(2, 7) }, new WaveDefinition[0],
                    rects.Length > 0 ? TerrainLayout.Rects(W, H, rects) : null),
                new SimConfig { CoreMaxHp = 1000000f });

        private static void PlaceGuns(SimWorld world)
        {
            foreach (Int2 gun in Guns) Sim.Place(world, BuildKind.Turret, gun, Dir.East);
        }

        /// <summary>Assert the machine on a cell has lost health, measured against its definition's max.</summary>
        private static void AssertChewed(SimWorld world, Int2 cell, string because)
        {
            MachineState machine = Sim.MachineAt(world, cell);
            Assert.Less(machine.Hp, world.Content.Machine(machine.Build).MaxHp, because);
        }

        [Test]
        public void AWave_FlanksAGauntlet_WhenTheOpenWayIsCheap()
        {
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab);

            // Control first: with nothing standing in the corridor, the wave walks it east.
            world.Enemies.Spawn(EnemyKind.Runner, Spawn, Sim.Circle);
            Sim.Tick(world, 60);

            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "an unswept corridor is walked as the crow flies");
            Assert.Less(world.Enemies.EnemyAt(0).Position.Y, 9.4f, "and along it, not out of it");

            // The guns go up mid-run: the field rebuilds, and the next walker off the same mouth reads the
            // corridor as the expensive one - twenty-odd steps of threat on the direct route, while the flank
            // (climb column plus north corridor) stays open. The wave turns around and takes it.
            PlaceGuns(world);
            int flanker = world.Enemies.Spawn(EnemyKind.Runner, Spawn, Sim.Circle);
            Sim.Tick(world, 60);

            // By id, not by slot: a death anywhere reorders slots, and this assertion is about the flanking walker.
            Assert.IsTrue(world.Enemies.TryGetPosition(flanker, out Vec2 flankPos),
                "the flanking walker survived the window - the guns are bare, they never fire");
            Assert.Less(flankPos.X, 2.0f,
                "a gauntlet is flanked: the wave walks back out the way it came");

            // The other half of the no-dither contract: the control walker was committed to the direct
            // field before the guns went up, so it keeps walking the now-swept lane east while a new spawn turns.
            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "the walker that set out direct keeps the direct way after the guns appear");
        }

        [Test]
        public void AWave_StaysDirect_WhenTheFlankSavesLessThanTheMargin()
        {
            // One gun, not two: the corridor is only partly swept, so the direct way pays under fire but
            // the open skirt up the climb column still costs more than PathDetourFactor's shave plus
            // PathDetourSlack allows - the marginal regime the two constants exist to tune. The gun is un-fed
            // (threat without shooting), and the overrun ends near x=5.5, far outside its 2.5-tile notice.
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab);
            Sim.Place(world, BuildKind.Turret, Guns[0], Dir.East);

            world.Enemies.Spawn(EnemyKind.Runner, Spawn, Sim.Circle);
            Sim.Tick(world, 60);

            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "a skirt that saves less than the margin is not taken");
            Assert.Less(world.Enemies.EnemyAt(0).Position.Y, 9.4f, "and the lane is walked, not fled");
        }

        [Test]
        public void AWave_OverrunsFire_RatherThanWalkingFarOutOfItsWay()
        {
            // The same corridors with the climb column sealed: the south corridor is the only way through,
            // its whole length swept. A covered cell saves nothing when every route pays for one, so the wave
            // crosses the fire - and that crossing is a cost, which is what makes coverage worth buying.
            var seal = new TerrainRect(new Int2(0, 0), 2, H, TerrainKind.Rock);
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab, seal);

            PlaceGuns(world);
            Sim.LayRun(world, new Int2(9, 7), Dir.East, 1);

            // A Tank, because the crossing must survive the fire it walks through: ten health against a
            // half of the turret's four damage is five rounds, and a saturated belt fires one a second -
            // which would fell it before it arrived. The supply here is one round every two seconds, so the
            // walk is what is being measured and not the gun's rate: it walks the swept length, reaches the
            // nearest gun, and chews it while it fires.
            world.Enemies.Spawn(EnemyKind.Tank, Spawn, Sim.Circle);

            float gunMaxHp = world.Content.Machine(BuildKind.Turret).MaxHp;
            bool fired = false;
            bool crossed = false;
            bool damaged = false;
            for (int i = 0; i < 30 * SimConfig.TickRate; i++)
            {
                if (i % 60 == 0) world.Belts.TrySpawnItem(new Int2(9, 7), Sim.TopHalfCircle);
                world.Tick(InputCommand.None);
                fired |= world.ShotsFired > 0;
                crossed |= world.Enemies.AliveCount > 0 && world.Enemies.EnemyAt(0).Position.X > 8.0f;
                damaged |= world.Machines.TryGet(Guns[0], out MachineState gun) && gun.Hp < gunMaxHp;
            }

            Assert.IsTrue(fired, "the wave walked into the guns' range, and the guns answered");
            Assert.IsTrue(crossed, "and the wave kept walking the swept length regardless");
            Assert.IsTrue(damaged, "and the wave chewed the gun it reached - fire is a cost, not a wall");
        }

        [Test]
        public void AFallenGun_ReopensTheLaneItCovered()
        {
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab);

            PlaceGuns(world);
            int committed = world.Enemies.Spawn(EnemyKind.Runner, Spawn, Sim.Circle);
            Sim.Tick(world, 60);
            Assert.Less(world.Enemies.EnemyAt(0).Position.X, 2.0f, "the gauntlet is flanked while the guns stand");

            // Right-click the guns away: each removal is a revision that rebuilds the field, and the same
            // mouth reads the corridor as cheap again - coverage is a purchase that can be lost, not a fact
            // about the map. The walker on the ground keeps the flank it chose at the door, no dither on rebuild.
            foreach (Int2 gun in Guns)
                Assert.IsTrue(world.TryRemoveBuilding(gun), "the guns are the player's to remove");

            int reopened = world.Enemies.Spawn(EnemyKind.Runner, Spawn, Sim.Circle);
            Sim.Tick(world, 60);

            // By id, not by slot: the judgement that survives the rebuild is the walker's own. The old
            // flanker still reads AvoidsFire (it stays on the field it chose at the door, whatever it now
            // costs), while the new spawn weighed the emptied ground and reads direct. The guns are bare.
            Assert.IsTrue(world.Enemies.TryGetPosition(reopened, out Vec2 lanePos),
                "the reopened walker survived the window");
            Assert.Greater(lanePos.X, 3.5f, "the reopened lane is walked straight");
            Assert.Less(lanePos.Y, 9.4f, "and along it, as before the guns");
            Assert.IsTrue(world.Enemies.TryGetPosition(committed, out _),
                "the committed walker survived too");
            Assert.IsTrue(world.Enemies.EnemyAt(0).AvoidsFire,
                "the field rebuilt under the walking wave and its choice did not dither");
            Assert.IsFalse(world.Enemies.EnemyAt(1).AvoidsFire,
                "and the new spawn weighs the reopened ground instead");
        }

        [Test]
        public void AWave_ChargesTheTurret_BeforeTheMachineBesideIt()
        {
            // A wall nearer than a turret, both inside notice: the wave charges the turret, because the gun
            // is what kills it. The wall is left standing - an obstruction is not worth a detour while a gun is in reach.
            SimWorld world = EnduringWorld();

            var wall = new Int2(11, 8);
            var gun = new Int2(13, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            Sim.Place(world, BuildKind.Turret, gun, Dir.East);

            // Spawn between them, a hair closer to the wall: 2.12 tiles to the wall's centre against 2.39
            // to the gun's, both inside the Runner's 2.5-tile notice and outside its 1.6-tile reach.
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(12.2f, 10.5f), Sim.Circle);
            Sim.Tick(world, 90);

            AssertChewed(world, gun, "the gun is the machine the wave chews");
            Assert.AreEqual(world.Content.Machine(BuildKind.Wall).MaxHp, Sim.MachineAt(world, wall).Hp,
                Sim.Tol, "and the nearer wall is untouched");
        }

        [Test]
        public void AWave_ChewsTheWall_WhenNoGunIsInNotice()
        {
            // The preference with nothing to prefer: only a wall in reach, so the plain nearest rule applies
            // and the wave chews it - a single-machine case that reads as before the hunt-the-guns rule.
            SimWorld world = EnduringWorld();

            var wall = new Int2(11, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);

            // The same standing spot as above, 2.12 tiles from the wall's centre: inside notice, outside
            // reach, so it charges and then hits.
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(12.2f, 10.5f), Sim.Circle);
            Sim.Tick(world, 90);

            AssertChewed(world, wall, "with no gun to hunt, the wall in notice is the one it chews");
        }

        [Test]
        public void AWave_ChewsTheWallShieldingTheGun_RatherThanStallingBeforeIt()
        {
            // The README's posture - a wall between the lane and a bare gun - must never leave a walker
            // standing still: the gun behind the wall is in notice but out of reach, so charging it forever
            // would hit nothing. The reachable blocker outranks the dreamed-of turret: the wall is chewed,
            // and the gun hunted the moment it is gone.
            SimWorld world = EnduringWorld();

            var wall = new Int2(12, 7);
            var gun = new Int2(13, 7);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            Sim.Place(world, BuildKind.Turret, gun, Dir.East);

            // A Runner at (11.6, 7.5): 0.9 tiles from the wall's centre (inside its 1.6-tile reach) and 1.9
            // from the gun's (inside its 2.5-tile notice, beyond reach). Before the fallback the turret won
            // the scan and this walker pinned against the wall, hitting nothing; now the wall takes the bites.
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(11.6f, 7.5f), Sim.Circle);
            Sim.Tick(world, 120);

            AssertChewed(world, wall, "a wall inside reach outranks a gun beyond it");

            // The re-hunt half: the wall outlasts the window, so drop it by hand and watch the scan turn on
            // the gun it was shielding - the walker stands about a tile from the turret's centre, inside reach.
            world.Machines.DamageAt(wall, world.Content.Machine(BuildKind.Wall).MaxHp);
            Sim.Tick(world, 90);

            AssertChewed(world, gun, "the gun is hunted the moment the wall is gone");
        }
    }
}
