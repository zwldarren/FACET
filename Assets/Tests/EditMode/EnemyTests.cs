using System;
using System.Collections.Generic;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The walkers and the doors they walk in at: two things the flow field alone cannot do. Walkers
    /// keep a body's width between them however stacked a wave arrives; and a doorway, the map's tile
    /// kept walkable, refuses every machine that would seal it.
    /// </summary>
    public class EnemyTests
    {
        /// <summary>
        /// A door in the middle of the north edge of a featureless map, with a Core that outlasts the
        /// test: what is measured is where the walkers stand, not who wins. The group's demand is a full
        /// circle - this file is about bodies and doorways, so any exact item will do, and naming one keeps
        /// the wave legal.
        /// </summary>
        private static SimWorld DoorWorld(int enemies, float interval)
            => new SimWorld(
                new MapDefinition("door", 20, 12, Sim.TestCircles, new ShapePatch[0],
                    new[] { new Int2(10, 1) },
                    new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, enemies, 0, 0f, interval, Sim.Circle)) }),
                new SimConfig { CoreMaxHp = 1000000f });

        /// <summary>The closest two walkers stand, over every pair in the field.</summary>
        private static float ClosestPair(SimWorld world)
        {
            var enemies = new List<EnemySnapshot>();
            world.Enemies.GetEnemies(enemies);

            float closest = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                for (int j = i + 1; j < enemies.Count; j++)
                    closest = MathF.Min(closest, Vec2.Distance(enemies[i].Position, enemies[j].Position));
            }

            return closest;
        }

        /// <summary>The reason a click on this tile is refused, and the gesture that reports it: a press
        /// that never moved, then the release - what a one-tile click is.</summary>
        private static RejectionReason RefusalFor(SimWorld world, BuildKind kind, Int2 cell)
        {
            world.Tick(Gesture.Aim(cell, kind));
            world.Tick(Gesture.Release(cell, kind));

            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal),
                "a click that builds nothing says why");
            Assert.AreEqual(cell, refusal.Cell);

            return refusal.Reason;
        }

        [Test]
        public void EnemiesSetDownOnOneSpot_WalkOnAsABodyEach()
        {
            // The worst case the rule has to undo: three enemies on one point, where a group from one
            // door lands them when it enters faster than the lane drains. The separation pass moves a body
            // no faster than the walker moves itself, so this is a shade under a second of pulling apart.
            SimWorld world = Sim.NewEnduringWorld();
            for (int i = 0; i < 3; i++) world.Enemies.Spawn(EnemyKind.Runner, new Vec2(5.5f, 3.5f), Sim.Circle);

            Assert.AreEqual(0f, ClosestPair(world), "the test's premise: three walkers on one spot");

            Sim.Tick(world, 30);

            Assert.GreaterOrEqual(ClosestPair(world), Balance.EnemySpacing - Sim.Tol,
                "a second later they are three bodies, not one");
        }

        [Test]
        public void AnEnemyCrowd_OnTheCoreRing_StandsApart()
        {
            // The pile the player actually sees: a whole wave arriving at once. Every enemy stops on the
            // same ring around the Core, so without the rule the ring is one sprite with a queue of
            // attackers stacked on it. Twelve fit around that ring at a body's width, still in reach of
            // the Core - or the fix would be a bug of its own.
            SimWorld world = DoorWorld(enemies: 12, interval: 0.5f);
            world.Waves.StartNextWave();

            // Long enough for the whole wave to walk in (the ring is fifteen tiles of walking from the
            // door) and take up its places.
            Sim.Tick(world, 30 * 25);

            Assert.AreEqual(12, world.Enemies.AliveCount, "the whole wave arrived");
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "every one of them is in reach of the Core, not shoved out of the fight");
            Assert.GreaterOrEqual(ClosestPair(world), Balance.EnemySpacing - Sim.Tol,
                "and the crowd stands a body apart, on the ring and behind it");
        }

        [Test]
        public void ADoorway_IsTheMaps_SoNoMachineMayStandOnIt()
        {
            // The exploit this rule exists for: plug the wave's door with a wall and every enemy spawns
            // inside the wall, so a wave held at its door by two circles of wall is a wave the player
            // never has to fight. The map's door is not the player's tile.
            SimWorld world = Sim.Rig();                            // a map with a door on it
            Int2 door = world.Map.SpawnPoints[0];

            Assert.IsFalse(world.CanPlace(BuildKind.Wall, door), "the door stays open");

            // A click, not a direct TryPlace: the refusal is what the player is owed, and only the
            // gesture reports it (the click that built nothing has to say why).
            world.Tick(Gesture.Aim(door, BuildKind.Wall));
            world.Tick(Gesture.Release(door, BuildKind.Wall));

            Assert.IsFalse(world.Machines.Has(door), "and the click builds nothing");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal));
            Assert.AreEqual(RejectionReason.Doorway, refusal.Reason, "the click says why");
            Assert.AreEqual(door, refusal.Cell);
            Assert.AreEqual(BuildKind.Wall, refusal.Building);

            // Transport is not solidity: a belt is walkable, so it may cross a door - only a machine
            // would seal one.
            Assert.IsTrue(world.CanPlaceBelt(door), "a belt over a door blocks nothing");
            Assert.IsTrue(world.TryPlaceBelt(door, Dir.East));

            // And the wave that walks in there still walks in. The walk down the corridor to the Core is
            // the rig's own, so this waits on the Core's first hit rather than a stopwatch - two minutes,
            // longer than the walk and the wave's own schedule together.
            world.Waves.StartNextWave();
            for (int i = 0; i < 120 * SimConfig.TickRate && world.Events.CountOf(SimEventKind.CoreDamaged) == 0; i++)
                world.Tick(InputCommand.None);

            Assert.Greater(world.Enemies.AliveCount, 0, "the wave walked in");
            Assert.Greater(world.Events.CountOf(SimEventKind.CoreDamaged), 0,
                "and reached the Core, door and belt alike");
        }

        [Test]
        public void ABeltOnADoor_StillRefusesAMachineAsADoorway()
        {
            // A belt may cross a door, so one tile can be both taken and forbidden at once. The refusal
            // must name the rule it actually failed on - the doorway, which CanPlace reads before the tile -
            // or a toast naming the belt would send the player to clear a tile that is already clear.
            SimWorld world = Sim.Rig();
            Int2 door = world.Map.SpawnPoints[0];
            Assert.IsTrue(world.TryPlaceBelt(door, Dir.East), "a belt is walkable: it may cross a door");

            Assert.AreEqual(RejectionReason.Doorway, RefusalFor(world, BuildKind.Wall, door));
            Assert.IsFalse(world.Machines.Has(door), "and the click builds nothing on the door");
        }
    }
}
