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
        private static readonly Int2 SecondVein = new Int2(10, 3);

        public static void Play(SimWorld world)
        {
            Assert.AreEqual(TutorialHud.None, world.Tutorial.Revealed, "the HUD starts empty");
            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "only one ore patch at the start");

            Next(world);                                                        // welcome: gate and Core
            Next(world);                                                        // the ore
            Next(world);                                                        // the build bar
            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Turret), "the turret waits for its own lesson");
            world.Tick(Gesture.Aim(new Int2(1, 1), BuildKind.Belt));
            Until(world, 4, 2);                                                 // belt picked up
            Sim.Drag(world, new Int2(4, 11), new Int2(4, 8));                   // from the ore into the Core
            Assert.IsTrue(world.Belts.Has(new Int2(4, 11)) && world.Belts.Has(new Int2(4, 9)),
                "a drag that starts on the ore lays a belt on it, all the way down");
            Until(world, 5, 600);                                               // the belt on ore mines, and banks

            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "the second patch waits for the turret");
            Next(world);                                                        // "Success!"
            Assert.IsTrue(world.Terrain.HasOre(SecondVein), "and is laid when the turret lesson starts");
            Next(world);                                                        // right-click deletes

            world.Tick(Gesture.Aim(new Int2(1, 1), BuildKind.Turret));
            Until(world, 8, 2);                                                 // turret picked up
            Sim.Place(world, BuildKind.Turret, new Int2(12, 3), Dir.East);
            Until(world, 9, 2);                                                 // turret placed
            Sim.Drag(world, SecondVein, new Int2(12, 3));                       // ore into the turret
            Until(world, 10, 600);                                              // turret loaded
            Next(world);                                                        // "Loaded!"
            Next(world);                                                        // Core health
            Next(world);                                                        // the wave line
            Next(world);                                                        // the weakness chips

            Assert.IsTrue(world.Tutorial.WaitsForWaveCall);
            Sim.Tick(world, 300);
            Assert.AreEqual(0, world.Enemies.AliveCount, "nothing comes until the player calls the wave");

            world.Tick(new InputCommand(startWavePressed: true));
            Until(world, 15, 2);

            for (int i = 0; i < 30 * 90 && world.Status == GameStatus.Playing; i++) world.Tick(InputCommand.None);
            Assert.AreEqual(GameStatus.Won, world.Status);
            Assert.AreEqual(100f, world.Core.Hp, 0f, "the build the lesson teaches holds its wave untouched");

            world.Restart();
            Assert.IsFalse(world.Terrain.HasOre(SecondVein), "a restart takes the laid patch away again");
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
