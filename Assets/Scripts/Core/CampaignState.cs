using System;

namespace Facet.Core
{
    /// <summary>
    /// How far the player has come, across runs: which map is being played, and how many maps this
    /// campaign has cleared.
    ///
    /// Deliberately not part of <see cref="SimWorld"/>. A world is one run and is rebuilt from its
    /// map's own data; it knows nothing about what came before it and must not, or "replay this map
    /// from these commands" would stop being true. The only thing that carries over between maps is
    /// the one number that says where the player is, and this is where it lives - so progression is a
    /// thing the host drives, not a thing the simulation accumulates.
    ///
    /// Plain C# on purpose: <c>FACET.Core</c> stays engine-free, and a test can walk a whole campaign
    /// without a scene.
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

        /// <summary>
        /// Is this map 0 - the map the tutorial card and its camera strip belong to? The one thing
        /// that is true of that map and no other, so the HUD's "how to play" card and the camera's
        /// bound for it cannot disagree about when it is up.
        ///
        /// This is the map's number, not the order it happens to be played in: a campaign started
        /// partway through (the driver's start map) opens on a later map, and the tutorial stays down
        /// there - its text is about this map's chain, not about whatever map came first for that run.
        /// </summary>
        public bool IsFirstMap => MapIndex == 0;

        public CampaignState(int mapCount, int mapIndex = 0)
        {
            MapCount = Math.Max(1, mapCount);
            MapIndex = Math.Clamp(mapIndex, 0, MapCount - 1);
        }

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
