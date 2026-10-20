using System;

namespace Facet.Core
{
    /// <summary>
    /// How far the player has come across runs: which map is being played, and how many this campaign
    /// has cleared. Deliberately not part of <see cref="SimWorld"/> - a world is one run and must not
    /// remember what came before, or "replay this map from these commands" stops being true. Plain C#
    /// so <c>FACET.Core</c> stays engine-free.
    /// </summary>
    public sealed class CampaignState
    {
        /// <summary>How many maps the campaign has. Fixed by the <c>MapSet</c> the driver is wired to.</summary>
        public int MapCount { get; }

        /// <summary>Zero-based index of the map being played.</summary>
        public int MapIndex { get; private set; }

        /// <summary>How many maps have been cleared, in this campaign. Only ever grows, so reporting
        /// the same clear twice - which polling the world's status every frame does - is harmless.</summary>
        public int ClearedCount { get; private set; }

        /// <summary>Is there a map after this one?</summary>
        public bool HasNext => MapIndex + 1 < MapCount;

        /// <summary>Is this map 0 - the map the tutorial card and its camera strip belong to? This is
        /// the map's number, not the order it is played in: a campaign started partway through opens on
        /// a later map and the tutorial stays down.</summary>
        public bool IsFirstMap => MapIndex == 0;

        /// <summary>Build a campaign at <paramref name="mapIndex"/>, carrying
        /// <paramref name="clearedCount"/> maps of progress in from a store.
        ///
        /// The count is deliberately not derived from the index: a campaign that has cleared three maps
        /// but starts on its first one again (a replay, a test) has still cleared three. It is clamped to
        /// the campaign's own length, because a saved count outlives an edit to the map set - a campaign
        /// edited shorter must not claim more cleared maps than it has.</summary>
        public CampaignState(int mapCount, int mapIndex = 0, int clearedCount = 0)
        {
            MapCount = Math.Max(1, mapCount);
            MapIndex = Math.Clamp(mapIndex, 0, MapCount - 1);
            ClearedCount = Math.Clamp(clearedCount, 0, MapCount);
        }

        /// <summary>Open a map by its index - the level list's door, and the only way to go <em>back</em>
        /// to a map already cleared. The index is clamped to the campaign, so a stale row can only ask
        /// for a map this campaign has. Unlock gating lives in the view: this opens any map, and a win on
        /// one past the frontier still advances <see cref="ClearedCount"/> to the map's own number.</summary>
        public void StartMap(int index) => MapIndex = Math.Clamp(index, 0, MapCount - 1);

        /// <summary>Record that the current map has been cleared. Idempotent, so a host can call it
        /// from an every-frame status check rather than having to watch for the edge itself.</summary>
        public void MarkCleared() => ClearedCount = Math.Max(ClearedCount, MapIndex + 1);

        /// <summary>Move on to the next map, reporting false when this was the last one.</summary>
        public bool EnterNext()
        {
            if (!HasNext) return false;

            MapIndex++;
            return true;
        }
    }
}
