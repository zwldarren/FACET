using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// EditMode tests for the campaign machinery: the little state that outlives any one run, and the
    /// catalog the driver walks. <see cref="SimWorld"/> is one run on one map, rebuilt from its map's
    /// data, so what is left here is where the player is across maps (<see cref="CampaignState"/>) and
    /// how a list of maps becomes a progression (<see cref="MapCatalog"/>). No shipped map is read: the
    /// maps are designer data, and asserting their contents would only test today's file.
    /// </summary>
    public class CampaignTests
    {
        [Test]
        public void CampaignState_WalksForward_AndStopsAtTheEnd()
        {
            const int mapCount = 3;
            var campaign = new CampaignState(mapCount);

            Assert.AreEqual(0, campaign.MapIndex, "a campaign starts on the first map");
            Assert.AreEqual(0, campaign.ClearedCount);
            Assert.AreEqual(mapCount, campaign.MapCount);
            Assert.IsTrue(campaign.HasNext);

            // Clearing is idempotent, because the host polls the world's status every frame rather than
            // watching for the edge itself.
            campaign.MarkCleared();
            campaign.MarkCleared();
            Assert.AreEqual(1, campaign.ClearedCount);

            // Walk it out one map at a time, however many the campaign has: the progression is the map
            // table's length, not a number written down here.
            for (int i = 1; i < mapCount; i++)
            {
                Assert.IsTrue(campaign.HasNext, "there is a map " + (i + 1) + " to move on to");
                Assert.IsTrue(campaign.EnterNext());
                Assert.AreEqual(i, campaign.MapIndex);
                campaign.MarkCleared();
                Assert.AreEqual(i + 1, campaign.ClearedCount);
            }

            // The last map: there is nowhere after it, and asking does not move the campaign.
            Assert.IsFalse(campaign.HasNext);
            Assert.IsFalse(campaign.EnterNext());
            Assert.AreEqual(mapCount - 1, campaign.MapIndex);
            Assert.AreEqual(mapCount, campaign.ClearedCount);
        }

        [Test]
        public void CampaignState_ClampsWhereItStarts()
        {
            // The driver's start-map field is authored data, so a stale index - a map deleted from the
            // list - has to land on a real map instead of throwing in Awake.
            Assert.AreEqual(0, new CampaignState(2, -5).MapIndex);
            Assert.AreEqual(1, new CampaignState(2, 99).MapIndex);
            Assert.AreEqual(0, new CampaignState(0).MapIndex, "a campaign always has at least one map");
            Assert.AreEqual(1, new CampaignState(0).MapCount);
        }

        [Test]
        public void CampaignState_CarriesSavedProgressIn_WithoutDerivingItFromTheMap()
        {
            // A store can say three maps are cleared while the player replays the first one: the count is
            // progress, the index is where they are, and neither follows from the other.
            var campaign = new CampaignState(6, 3, 3);

            Assert.AreEqual(3, campaign.ClearedCount);
            Assert.AreEqual(3, campaign.MapIndex);

            campaign.StartMap(0);
            Assert.AreEqual(0, campaign.MapIndex, "a cleared map can be gone back to");
            Assert.AreEqual(3, campaign.ClearedCount, "and replaying it does not un-clear the campaign");

            // The driver polls MarkCleared every frame a world is won, so a replay that is won again is the
            // case that matters: ClearedCount must not fall back to one.
            campaign.MarkCleared();
            Assert.AreEqual(3, campaign.ClearedCount, "winning the replay still reads three, not one");

            campaign.StartMap(99);
            Assert.AreEqual(5, campaign.MapIndex, "a stale row lands on the last map, not off the end");
        }

        [Test]
        public void ANegativeIndexOrCount_IsClampedToTheBottom()
        {
            // Both values arrive from outside the process - a stored count and a UI row - so the low side
            // of the clamp is pinned as well as the high side.
            var campaign = new CampaignState(6, -3, -1);

            Assert.AreEqual(0, campaign.MapIndex);
            Assert.AreEqual(0, campaign.ClearedCount);

            campaign.StartMap(-7);
            Assert.AreEqual(0, campaign.MapIndex, "a stale row cannot ask below the first map");
        }

        [Test]
        public void AFinishedCampaign_StaysFinished_WhenItsLastMapIsPlayedAgain()
        {
            var allCleared = new CampaignState(3, 2, 3);

            allCleared.StartMap(2);
            allCleared.MarkCleared();

            Assert.AreEqual(3, allCleared.ClearedCount, "the campaign stays fully cleared");
            Assert.AreEqual(2, allCleared.MapIndex, "and its last map stays the one being played");
        }

        [Test]
        public void ASavedCountPastTheEndOfAShorterCampaign_IsClamped()
        {
            // A count outlives an edit to the map set: a campaign cut from six maps to two must not claim
            // six cleared, or the level list would call every row CLEARED and still offer nothing to play.
            var campaign = new CampaignState(2, 1, 99);

            Assert.AreEqual(2, campaign.ClearedCount);
            Assert.AreEqual(2, campaign.MapCount);
        }

        [Test]
        public void WaveSpawnIndices_WrapOntoTheMapsEntryPoints()
        {
            // A wave table names its door by position in the map's spawn list, and the index wraps so a
            // table stays loadable by a map with fewer doors instead of indexing off the end of the
            // entry points - and off the end of the marker list the view sizes from them.
            var map = new MapDefinition("two doors", 8, 8, 10, new ShapePatch[0],
                new[] { new Int2(0, 0), new Int2(7, 0) },
                new[] { new WaveDefinition(0f, new SpawnGroup(EnemyKind.Runner, 1, 3, 0f, 1f, Shape.Full(Quad.Circle))) });

            Assert.AreEqual(0, map.ResolveSpawn(0), "the first door");
            Assert.AreEqual(1, map.ResolveSpawn(1), "the second");
            Assert.AreEqual(0, map.ResolveSpawn(2), "and a third wraps back to the first");
            Assert.AreEqual(1, map.ResolveSpawn(3), "which is what the table's own index resolves to");

            Assert.AreEqual(0, map.SpawnIndexOf(new Int2(0, 0)), "a cell reads back as its door");
            Assert.AreEqual(1, map.SpawnIndexOf(new Int2(7, 0)));
            Assert.AreEqual(-1, map.SpawnIndexOf(new Int2(4, 4)), "and bare ground is no door at all");

            var doorless = new MapDefinition("no doors", 8, 8, 10, new ShapePatch[0], new Int2[0],
                new WaveDefinition[0]);
            Assert.AreEqual(-1, doorless.ResolveSpawn(0), "a map with no doors resolves none");
            Assert.AreEqual(-1, map.ResolveSpawn(-1), "and a negative position is not a door either");
        }

        [Test]
        public void MapCatalog_SkipsAnEntryThatNamesNoMap()
        {
            // A set being filled in is a shorter campaign rather than a hole in one: the empty slots the
            // Inspector shows are what a half-authored campaign looks like, and the map after the gap
            // still has to load.
            var one = NewMap("One");
            var two = NewMap("Two");
            var set = ScriptableObject.CreateInstance<MapSet>();

            try
            {
                set.Maps = new[] { one, null, two };
                MapCatalog catalog = MapCatalog.From(set);

                Assert.AreEqual(2, catalog.Count, "the two entries that name a map");
                Assert.AreEqual("One", catalog.At(0).Name);
                Assert.AreEqual("Two", catalog.At(1).Name, "in the order the set lists them");
            }
            finally
            {
                Object.DestroyImmediate(one);
                Object.DestroyImmediate(two);
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void MapCatalog_ClampsTheStaleIndexTheDriverStartsOn()
        {
            // "Which map is next" is asked by the driver, the campaign's count and the HUD's carry-on
            // button, so the arithmetic lives in one place: a stale index lands on a real map instead of
            // throwing in Awake.
            var one = NewMap("One");
            var two = NewMap("Two");
            var three = NewMap("Three");
            var set = ScriptableObject.CreateInstance<MapSet>();

            try
            {
                set.Maps = new[] { one, two, three };
                MapCatalog catalog = MapCatalog.From(set);

                Assert.IsTrue(catalog.HasNext(0), "three maps are more than one");
                Assert.IsFalse(catalog.HasNext(catalog.Count - 1), "and the last one is the end of the run");
                Assert.AreEqual("Two", catalog.NextAfter(0).Name, "the map after the first is the second");
                Assert.AreEqual("Three", catalog.NextAfter(catalog.Count - 1).Name,
                    "and asking past the end names the last map rather than nothing");
                Assert.AreEqual("One", catalog.At(-5).Name, "a stale index is clamped, not thrown");
                Assert.AreEqual("Three", catalog.At(9999).Name);
            }
            finally
            {
                Object.DestroyImmediate(one);
                Object.DestroyImmediate(two);
                Object.DestroyImmediate(three);
                Object.DestroyImmediate(set);
            }
        }

        [Test]
        public void MapCatalog_ForASetWithNoMaps_IsNoCatalogAtAll()
        {
            // The state the driver refuses to start in - reported once, where it can be acted on, rather
            // than an empty list every reader of At() would have to check for.
            var empty = ScriptableObject.CreateInstance<MapSet>();

            try
            {
                Assert.IsNull(MapCatalog.From(empty), "a set that names no map is not a campaign");
                Assert.IsNull(MapCatalog.From((MapSet)null), "and neither is nothing at all");
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        /// <summary>An asset with nothing in it but a name: enough to be a map in a catalog, and nothing a
        /// test that is about the catalog has to paint.</summary>
        private static MapAsset NewMap(string name)
        {
            var asset = ScriptableObject.CreateInstance<MapAsset>();
            asset.Name = name;
            return asset;
        }
    }
}
