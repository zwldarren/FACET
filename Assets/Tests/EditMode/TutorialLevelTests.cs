using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>
    /// The shipped tutorial level (Assets/Data/Maps/Tutorial.asset): it is valid, it is the campaign's first
    /// map, and a player who does exactly what each popup says gets from the first popup to a won run.
    ///
    /// Unlike the simulation tests, this one reads the real map file on purpose: the level is the lesson, so a
    /// repaint that moves an ore patch out from under a ghost, or a turret spot the lane no longer passes, is
    /// exactly what should fail here.
    /// </summary>
    public class TutorialLevelTests
    {
        private const string TutorialPath = "Assets/Data/Maps/Tutorial.asset";
        private const string CampaignPath = "Assets/Data/Campaign.asset";

        private static MapAsset Load()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapAsset>(TutorialPath);
            Assert.IsNotNull(map, TutorialPath + " is missing");
            return map;
        }

        [Test]
        public void TheTutorial_IsValid_AndCoached()
        {
            MapAsset map = Load();
            var problems = map.Validate();
            Assert.AreEqual(0, problems.Count, problems.Count > 0 ? problems[0].ToString() : "");
            Assert.IsTrue(map.TutorialCoached);
            Assert.IsTrue(map.TutorialSpotlight, "the first tutorial dims around its boxes");
        }

        [Test]
        public void TheTutorial_IsTheCampaignsFirstMap()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>(CampaignPath);
            Assert.IsNotNull(campaign);
            Assert.AreSame(Load(), campaign.Maps[0]);
        }

        [Test]
        public void FollowingEveryPopup_WinsTheRun()
        {
            TutorialLevelPlay.Play(new SimWorld(Load().ToCore(), new SimConfig()));
        }
    }

    /// <summary>The tutorial level played the way its popups say, line by line. Shared so the level's own test
    /// and any test that builds the same map by hand walk one script.</summary>
    public static class TutorialLevelPlay
    {
        private static readonly Int2 FirstVein = new Int2(14, 11);
        private static readonly Int2 SecondVein = new Int2(20, 3);
        private static readonly Int2 SouthVein = new Int2(21, 11);
        private static readonly Int2 WestVein = new Int2(2, 2);
        private static readonly Int2 WestWall = new Int2(0, 5);

        public static void Play(SimWorld world)
        {
            Assert.AreEqual(TutorialHud.None, world.Tutorial.Revealed, "the HUD starts empty");
            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "only one ore patch at the start");
            Assert.AreEqual(TerrainKind.Void, world.Terrain.KindAt(WestVein),
                "the west room is void until its lesson opens it");

            Next(world);                                                        // welcome: gate and Core
            Next(world);                                                        // the ore
            Next(world);                                                        // buildings cost ore
            Next(world);                                                        // the build bar
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Turret), "the turret waits for its own lesson");
            world.Tick(Gesture.Select(BuildKind.Belt));                          // the bar's belt, not a map stroke
            Until(world, 5, 2);                                                 // belt picked up
            Sim.Drag(world, FirstVein, new Int2(14, 8));                        // from the ore into the Core
            Assert.IsTrue(world.Belts.Has(FirstVein) && world.Belts.Has(new Int2(14, 9)),
                "a drag that starts on the ore lays a belt on it, all the way down");
            Until(world, 6, 600);                                               // the belt on ore mines, and banks

            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "the second patch waits for the turret");
            Next(world);                                                        // "Success!"
            Assert.IsTrue(world.Terrain.HasOre(SecondVein), "and is laid when the turret lesson starts");
            Next(world);                                                        // right-click deletes

            world.Tick(Gesture.Select(BuildKind.Turret));
            Until(world, 9, 2);                                                 // turret picked up
            Sim.Place(world, BuildKind.Turret, new Int2(22, 3), Dir.East);
            Until(world, 10, 2);                                                // turret placed
            Sim.Drag(world, SecondVein, new Int2(22, 3));                       // ore into the turret
            Until(world, 11, 600);                                              // turret loaded
            Next(world);                                                        // "Loaded!"
            Next(world);                                                        // Core health
            Next(world);                                                        // the wave line
            Next(world);                                                        // the weakness chips

            Assert.IsTrue(world.Tutorial.WaitsForWaveCall);
            Sim.Tick(world, 300);
            Assert.AreEqual(0, world.Enemies.AliveCount, "nothing comes until the player calls the wave");

            world.Tick(new InputCommand(startWavePressed: true));
            Until(world, 16, 2);                                                // wave 1 called
            Assert.IsFalse(world.Terrain.HasOre(SouthVein), "the south vein waits for wave 1 to be cleared");
            Until(world, 17, 30 * 120);                                         // cleared: the second vein
            Assert.AreEqual(100f, world.Core.Hp, 0f, "one turret holds the first wave untouched");
            Assert.IsTrue(world.Terrain.HasOre(SouthVein), "and the lesson lays the vein the second gun feeds on");

            Next(world);                                                        // "Two guns": the wave is down
            Sim.Place(world, BuildKind.Turret, new Int2(23, 11), Dir.East);
            Until(world, 19, 2);                                                // a second turret, anywhere
            Sim.Drag(world, SouthVein, new Int2(23, 11));                       // and its feed
            Until(world, 20, 600);                                              // both chambers have a round

            world.Tick(new InputCommand(startWavePressed: true));
            Until(world, 21, 2);                                                // wave 2 called
            Until(world, 22, 30 * 180);                                         // cleared: the west opens
            Assert.AreEqual(100f, world.Core.Hp, 0f, "two turrets hold the second wave untouched");
            Assert.AreEqual(TerrainKind.Ore, world.Terrain.KindAt(WestVein),
                "the west room the map was authored without is now floor, with a vein in it");
            Assert.AreEqual(TerrainKind.Ground, world.Terrain.KindAt(new Int2(5, 2)),
                "and the floor reaches past the vein");
            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(WestWall), "with its lane walled");
            Assert.AreEqual(TerrainKind.Ground, world.Terrain.KindAt(new Int2(10, 7)),
                "and its doorway opened onto the old map");
            Assert.IsTrue(world.Terrain.HasOre(WestVein), "the vein is there to mine");
            Assert.AreEqual(0, world.Enemies.AliveCount);

            Next(world);                                                        // "Look around"
            Next(world);                                                        // "The west opens"
            Sim.Place(world, BuildKind.Turret, new Int2(6, 4), Dir.East);
            Until(world, 25, 2);                                                // a third turret, anywhere
            Sim.Drag(world, new Int2(3, 3), new Int2(6, 4));                    // its feed from the new vein
            Assert.IsTrue(world.Belts.Has(new Int2(6, 3)), "the run turns up into the turret");
            Until(world, 26, 600);                                              // all three armed

            world.Tick(new InputCommand(startWavePressed: true));
            Until(world, 27, 2);                                                // wave 3 called

            for (int i = 0; i < 30 * 180 && world.Status == GameStatus.Playing; i++) world.Tick(InputCommand.None);
            Assert.AreEqual(GameStatus.Won, world.Status, "the lesson's three waves are the run");
            Assert.AreEqual(100f, world.Core.Hp, 0f, "the west gun holds the last wave untouched");

            world.Restart();
            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "a restart takes the laid patch away again");
            Assert.IsFalse(world.Terrain.HasOre(SouthVein));
            Assert.IsFalse(world.Terrain.HasOre(WestVein));
            Assert.AreEqual(TerrainKind.Void, world.Terrain.KindAt(WestVein),
                "and closes the room the map was authored without");
        }

        private static void Next(SimWorld world)
        {
            Assert.IsTrue(world.Tutorial.WaitsForAcknowledge, "expected a popup at line " + world.Tutorial.StepsCompleted);
            int before = world.Tutorial.StepsCompleted;
            world.RequestTutorialNext();
            world.Tick(InputCommand.None);
            Assert.AreEqual(before + 1, world.Tutorial.StepsCompleted);
        }

        private static void Until(SimWorld world, int lines, int maxTicks)
        {
            for (int i = 0; i < maxTicks && world.Tutorial.StepsCompleted < lines; i++) world.Tick(InputCommand.None);
            Assert.AreEqual(lines, world.Tutorial.StepsCompleted, "stuck at line " + world.Tutorial.StepsCompleted);
        }
    }
}
