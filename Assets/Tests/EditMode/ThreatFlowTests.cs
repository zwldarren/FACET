using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The wave's one piece of judgement, on the field where it is made: a route under a standing
    /// turret's fire costs one plain step more per covered cell, so a wave flanks a corridor swept
    /// along its walked length for the open approach beside it, overruns a gun whose disc it cannot
    /// walk around, and reads a gun's fall as a route reopened. Plus the charging rule that goes
    /// with it: between two machines its notice reaches, the wave hunts the turret - but chews
    /// anything actually in reach first - because the turret is what is killing it.
    ///
    /// The shared geometry is two corridors cut through rock: the south one, rows y=6..8, runs from the
    /// west edge to a merge in front of the Core (centred at (22,12)-(25,15) on a 48x28 map, so the
    /// merge column x=26 is its only door); the north one, rows y=18..20, runs parallel a dozen rows
    /// above, and the two are joined only by the climb column x=0, which the rock leaves open. The
    /// south corridor is the straight way in; the north one is the flank.
    /// </summary>
    public class ThreatFlowTests
    {
        private const int W = 48;
        private const int H = 28;

        /// <summary>The south corridor's mouth: the straight route's first cell.</summary>
        private static readonly Vec2 Spawn = new Vec2(2.5f, 7.5f);

        /// <summary>The rock: a middle ridge (y=9..17) between the corridors, a slab under each, all
        /// stopping one column short of the west edge so the climb column at x=0 stays open.</summary>
        private static readonly TerrainRect Ridge = new TerrainRect(new Int2(1, 9), 25, 9, TerrainKind.Rock);
        private static readonly TerrainRect SouthSlab = new TerrainRect(new Int2(1, 0), 25, 6, TerrainKind.Rock);
        private static readonly TerrainRect NorthSlab = new TerrainRect(new Int2(1, 21), 25, 7, TerrainKind.Rock);

        /// <summary>Two cannons whose discs cover the walked length of the south corridor - x=3..26
        /// at y=7, with the gun cells themselves walked around - so the straight route pays roughly
        /// a step of threat for most of its length. The mouth cell stays outside range 7 and the
        /// climb route's 47 steps clear the 0.8x+2 budget with under two steps of margin: this is
        /// the decision boundary the balance constants define, and the canary for moving them.</summary>
        private static readonly Int2[] Guns = { new Int2(10, 7), new Int2(20, 7) };

        private static SimWorld EnduringWorld(params TerrainRect[] rects)
            => new SimWorld(
                new MapDefinition("threat", W, H, Sim.TestCircles, new ShapePatch[0],
                    new[] { new Int2(2, 7) }, new WaveDefinition[0],
                    rects.Length > 0 ? TerrainLayout.Rects(W, H, rects) : null),
                new SimConfig { CoreMaxHp = 1000000f });

        private static void PlaceGuns(SimWorld world)
        {
            foreach (Int2 gun in Guns) Sim.Place(world, BuildKind.Cannon, gun, Dir.East);
        }

        /// <summary>Assert the machine on a cell has lost health: something has chewed it. The
        /// yardstick is the definition's own max, which is what a fresh placement starts at.</summary>
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
            world.Enemies.Spawn(EnemyKind.Spike, Spawn);
            Sim.Tick(world, 60);

            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "an unswept corridor is walked as the crow flies");
            Assert.Less(world.Enemies.EnemyAt(0).Position.Y, 9.4f, "and along it, not out of it");

            // The guns go up mid-run: the field rebuilds on the placement, and the next walker off the
            // same mouth reads the corridor as the expensive one. Nearly every cell it would walk is
            // covered - twenty-odd steps of threat on the straight route - while the flank is the climb
            // column and the north corridor, open the whole way. The wave turns around and takes it.
            PlaceGuns(world);
            int flanker = world.Enemies.Spawn(EnemyKind.Spike, Spawn);
            Sim.Tick(world, 60);

            // By id, not by slot: a death anywhere reorders the slots, and this assertion is about
            // the flanking walker specifically.
            Assert.IsTrue(world.Enemies.TryGetPosition(flanker, out Vec2 flankPos),
                "the flanking walker survived the window - the guns are bare, they never fire");
            Assert.Less(flankPos.X, 2.0f,
                "a gauntlet is flanked: the wave walks back out the way it came");

            // The other half of the no-dither contract, for free: the control walker was committed
            // to the direct field before the guns went up, and it keeps walking the now-swept lane
            // east while a walker born at the same door after them turns around.
            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "the walker that set out direct keeps the direct way after the guns appear");
        }

        [Test]
        public void AWave_StaysDirect_WhenTheFlankSavesLessThanTheMargin()
        {
            // One gun, not two: the corridor is only partly swept, so the straight way pays under fire
            // but the open skirt up the climb column still costs more than PathDetourFactor's shave
            // plus PathDetourSlack allows. The wave overruns the half-covered lane rather than swing
            // wide - the marginal regime the two constants exist to tune, and the one a drift of
            // either would silently move on every shipped map. The gun is un-fed, so it projects
            // threat without shooting - and the overrun walk ends near x=5.5 inside the window, some
            // five tiles out from the gun's 2.5-tile notice radius, so nothing pulls the walker off
            // the lane.
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab);
            Sim.Place(world, BuildKind.Cannon, Guns[0], Dir.East);

            world.Enemies.Spawn(EnemyKind.Spike, Spawn);
            Sim.Tick(world, 60);

            Assert.Greater(world.Enemies.EnemyAt(0).Position.X, 3.5f,
                "a skirt that saves less than the margin is not taken");
            Assert.Less(world.Enemies.EnemyAt(0).Position.Y, 9.4f, "and the lane is walked, not fled");
        }

        [Test]
        public void AWave_OverrunsFire_RatherThanWalkingFarOutOfItsWay()
        {
            // The same corridors, but the climb column is sealed: the south corridor is now the only
            // way through, its whole length swept. A covered cell saves nothing when every route pays
            // for one, so the wave crosses the fire - and the crossing is a cost, which is what makes
            // coverage worth buying even where avoidance cannot be beaten.
            var seal = new TerrainRect(new Int2(0, 0), 2, H, TerrainKind.Rock);
            SimWorld world = EnduringWorld(Ridge, SouthSlab, NorthSlab, seal);

            PlaceGuns(world);
            Sim.LayRun(world, new Int2(9, 7), Dir.East, 1);

            // A Bulwark, because the crossing has to survive the fire it walks through: a Spike folds
            // to two shots inside the first gun's disc, and a dead wave crosses nothing. A Bulwark
            // resists half-circles - a quarter damage a shot - so it walks the swept length, reaches
            // the nearest gun, and chews it while it fires.
            world.Enemies.Spawn(EnemyKind.Bulwark, Spawn);

            float gunMaxHp = world.Content.Machine(BuildKind.Cannon).MaxHp;
            bool fired = false;
            bool crossed = false;
            bool damaged = false;
            for (int i = 0; i < 30 * SimConfig.TickRate; i++)
            {
                world.Belts.TrySpawnItem(new Int2(9, 7), ShapeType.HalfCircle);
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
            int committed = world.Enemies.Spawn(EnemyKind.Spike, Spawn);
            Sim.Tick(world, 60);
            Assert.Less(world.Enemies.EnemyAt(0).Position.X, 2.0f, "the gauntlet is flanked while the guns stand");

            // Right-click the guns away: each removal is a revision, the field rebuilds, and the same
            // mouth reads the corridor as the cheap way again. Coverage is a purchase that can be
            // lost, not a fact about the map. And the walker already on the ground keeps the flank it
            // chose at the door - the field rebuilt under it and it did not dither.
            foreach (Int2 gun in Guns)
                Assert.IsTrue(world.TryRemoveBuilding(gun), "the guns are the player's to remove");

            int reopened = world.Enemies.Spawn(EnemyKind.Spike, Spawn);
            Sim.Tick(world, 60);

            // By id, not by slot. The judgement that survives the rebuild is the walker's own: the
            // old flanker still reads AvoidsFire - it stays on the flank field it chose at the door,
            // whatever that field now costs - while the new spawn weighed the emptied ground and
            // reads direct. No death reorders these slots inside the window: the guns are bare.
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
            // A wall nearer than a cannon, both inside the notice radius: the wave charges the
            // cannon, because the gun is what empties the wave into the ground. The wall is left
            // standing - an obstruction is not worth a detour while a gun is in reach.
            SimWorld world = EnduringWorld();

            var wall = new Int2(11, 8);
            var gun = new Int2(13, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            Sim.Place(world, BuildKind.Cannon, gun, Dir.East);

            // Spawn between them, a hair closer to the wall: 2.12 tiles to the wall's centre against
            // 2.39 to the cannon's, both inside the Spike's 2.5-tile notice and outside its 1.6-tile
            // reach, so both are chargeable and the nearer one is not the one chosen.
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(12.2f, 10.5f));
            Sim.Tick(world, 90);

            AssertChewed(world, gun, "the gun is the machine the wave chews");
            Assert.AreEqual(world.Content.Machine(BuildKind.Wall).MaxHp, Sim.MachineAt(world, wall).Hp,
                Sim.Tol, "and the nearer wall is untouched");
        }

        [Test]
        public void AWave_ChewsTheWall_WhenNoGunIsInNotice()
        {
            // The preference with nothing to prefer: only a wall in reach, so the plain nearest rule
            // applies and the wave chews the obstruction - single-machine cases read exactly as they
            // did before the hunt-the-guns rule existed.
            SimWorld world = EnduringWorld();

            var wall = new Int2(11, 8);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);

            // The same standing spot as the test above, 2.12 tiles from the wall's centre: inside the
            // Spike's 2.5-tile notice, outside its 1.6-tile reach, so it charges and then hits.
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(12.2f, 10.5f));
            Sim.Tick(world, 90);

            AssertChewed(world, wall, "with no gun to hunt, the wall in notice is the one it chews");
        }

        [Test]
        public void AWave_ChewsTheWallShieldingTheGun_RatherThanStallingBeforeIt()
        {
            // The posture the README invites - a wall between the lane and a bare gun - must never
            // leave a walker standing still: the gun behind the wall is inside notice but outside
            // reach, and charging it forever would attack neither the wall it presses against nor
            // the gun. The reachable blocker outranks the dreamed-of turret; the wall is chewed,
            // and the gun is hunted the moment the wall is gone.
            SimWorld world = EnduringWorld();

            var wall = new Int2(12, 7);
            var gun = new Int2(13, 7);
            Sim.Place(world, BuildKind.Wall, wall, Dir.North);
            Sim.Place(world, BuildKind.Cannon, gun, Dir.East);

            // A Spike at (11.6, 7.5): 0.9 tiles from the wall's centre - inside its 1.6-tile reach -
            // and 1.9 from the cannon's, inside the 2.5-tile notice but beyond reach. Before the
            // fallback existed the turret won the scan and this walker pinned against the wall,
            // hitting nothing; now the wall takes the bites.
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(11.6f, 7.5f));
            Sim.Tick(world, 120);

            AssertChewed(world, wall, "a wall inside reach outranks a gun beyond it");

            // The re-hunt half: the wall cannot fall inside this window (120 hp against a Spike's
            // bites), so drop it by hand and watch the scan turn on the gun it was shielding. The
            // walker stands about a tile from the cannon's centre - inside reach the next tick.
            world.Machines.DamageAt(wall, world.Content.Machine(BuildKind.Wall).MaxHp);
            Sim.Tick(world, 90);

            AssertChewed(world, gun, "the gun is hunted the moment the wall is gone");
        }
    }
}
