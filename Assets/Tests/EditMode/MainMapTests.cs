using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>
    /// The main map: one fixed map after the tutorial, played in stages. Each stage hands over one new thing by
    /// research and the wave right after it tests that thing, then the generator takes over, endless.
    /// </summary>
    public class MainMapTests
    {
        private const string MainMapPath = "Assets/Data/Maps/MainMap.asset";
        private const string TutorialPath = "Assets/Data/Maps/Tutorial.asset";
        private const string CampaignPath = "Assets/Data/Campaign.asset";

        private static MapAsset Load()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapAsset>(MainMapPath);
            Assert.IsNotNull(map, MainMapPath + " is missing");
            return map;
        }

        private static readonly Shape TopHalfCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
        private static readonly Shape RightHalfCircle = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);

        [Test]
        public void TheCampaign_IsTheTutorial_ThenTheMainMap()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>(CampaignPath);
            Assert.IsNotNull(campaign);
            Assert.AreEqual(2, campaign.Maps.Length);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<MapAsset>(TutorialPath), campaign.Maps[0]);
            Assert.AreSame(Load(), campaign.Maps[1]);
        }

        [Test]
        public void TheMainMap_IsValid_Endless_AndTeachesNoLesson()
        {
            MapAsset asset = Load();
            CollectionAssert.IsEmpty(asset.Validate());

            MapDefinition map = asset.ToCore();
            Assert.AreEqual(-1, map.GeneratedWaves, "endless: the generator takes over after the table");
            Assert.AreEqual(15, map.Waves.Length, "stages 1-5 are authored, wave by wave");
            Assert.IsNull(map.Tutorial, "it teaches by research cards, not by a scripted lesson");
            Assert.AreEqual(2, map.SpawnPoints.Length, "a right door from the start and a top door for stage 5");
        }

        [Test]
        public void TheRun_OpensWithBeltAndTurretOnly()
        {
            var world = new SimWorld(Load().ToCore(), new SimConfig());

            Assert.IsTrue(world.IsUnlocked(BuildKind.Belt));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Turret));
            foreach (BuildKind kind in new[] { BuildKind.Splitter, BuildKind.Cutter, BuildKind.Sink,
                         BuildKind.Rotator, BuildKind.Pipe, BuildKind.Stacker, BuildKind.Sorter })
            {
                Assert.IsFalse(world.IsUnlocked(kind), kind + " waits for research");
                Assert.IsFalse(world.Researchable(kind), kind + " is not even on offer yet");
            }
        }

        [Test]
        public void TheSchedule_IsTheAgreedOrder()
        {
            ResearchOffer[] offers = Load().ToCore().Research.Offers;

            Assert.AreEqual(6, offers.Length);
            AssertLesson(offers[0], 3, BuildKind.Splitter);
            AssertLesson(offers[1], 5, BuildKind.Cutter, BuildKind.Sink);
            AssertLesson(offers[2], 8, BuildKind.Rotator);
            AssertHint(offers[3], 10, BuildKind.Pipe);
            AssertLesson(offers[4], 11, BuildKind.Stacker);
            AssertHint(offers[5], 15, BuildKind.Sorter);
        }

        private static void AssertLesson(ResearchOffer offer, int afterWave, params BuildKind[] builds)
        {
            Assert.AreEqual(afterWave, offer.AfterWave);
            CollectionAssert.AreEquivalent(builds, offer.Builds);
            Assert.IsNotNull(offer.Lesson, "an operator is taught");
            Assert.IsEmpty(offer.Hint, "by its lesson rather than a line");
            Assert.AreEqual(TutorialStep.ResearchBuilding, offer.Lesson[0].Step, "and the first thing is buying it");
            Assert.AreEqual(builds[0], offer.Lesson[0].Opens[0]);

            for (int i = 0; i < offer.Lesson.Count; i++)
            {
                TutorialTask line = offer.Lesson[i];
                Assert.IsNotEmpty(line.Title, "every line has a heading");
                Assert.IsNotEmpty(line.Text, "and something to say");
                Assert.LessOrEqual(line.Text.Length, 110, "and says it briefly: " + line.Title);
                Assert.AreEqual(1, line.Opens.Length, "and names the one building it is about: " + line.Title);
            }
        }

        private static void AssertHint(ResearchOffer offer, int afterWave, params BuildKind[] builds)
        {
            Assert.AreEqual(afterWave, offer.AfterWave);
            CollectionAssert.AreEquivalent(builds, offer.Builds);
            Assert.IsNull(offer.Lesson, "a logistics tool is not an exam");
            Assert.IsNotEmpty(offer.Hint, "a line is enough");
        }

        [Test]
        public void TheWaveAfterAnOperatorIsOffered_TestsThatOperator()
        {
            MapDefinition map = Load().ToCore();

            foreach (SpawnGroup group in map.Waves[5].Groups)
                Assert.AreEqual(TopHalfCircle, group.Weakness, "wave 6 asks for top halves only: the Cutter's exam");
            foreach (SpawnGroup group in map.Waves[8].Groups)
                Assert.AreEqual(RightHalfCircle, group.Weakness, "wave 9 asks for right halves only: the Rotator's");
        }

        [Test]
        public void TheTopDoor_StaysShut_UntilStageFive()
        {
            MapDefinition map = Load().ToCore();

            for (int w = 0; w < 11; w++)
                foreach (SpawnGroup group in map.Waves[w].Groups)
                    Assert.AreEqual(0, group.SpawnPoint, "wave " + (w + 1) + " comes only from the right");

            bool topUsed = false;
            foreach (SpawnGroup group in map.Waves[11].Groups) topUsed |= group.SpawnPoint == 1;
            Assert.IsTrue(topUsed, "wave 12 opens the top door");
        }
    }
}
