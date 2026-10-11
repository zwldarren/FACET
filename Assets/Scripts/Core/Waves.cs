namespace Facet.Core
{
    /// <summary>One block of "N of this enemy, entering at this point, starting then, this far apart".</summary>
    public readonly struct SpawnGroup
    {
        public readonly EnemyKind Kind;
        public readonly int Count;
        public readonly int SpawnPoint;
        public readonly float FirstDelay;
        public readonly float Interval;

        public SpawnGroup(EnemyKind kind, int count, int spawnPoint, float firstDelay, float interval)
        {
            Kind = kind;
            Count = count;
            SpawnPoint = spawnPoint;
            FirstDelay = firstDelay;
            Interval = interval;
        }
    }

    /// <summary>
    /// One wave: how long the intermission before it lasts, and what walks in. A map's first wave is
    /// the one exception to the countdown - it holds until the player starts it by hand.
    /// </summary>
    public sealed class WaveDefinition
    {
        /// <summary>
        /// Countdown before this wave, in seconds. Unread for a map's first wave, which has no
        /// countdown at all: it holds until the player starts it. Give it 0.
        /// </summary>
        public readonly float Intermission;
        public readonly SpawnGroup[] Groups;

        public WaveDefinition(float intermission, params SpawnGroup[] groups)
        {
            Intermission = intermission;
            Groups = groups;
        }

        /// <summary>How many of one enemy this wave contains - the number the preview reads out.</summary>
        public int CountOf(EnemyKind kind)
        {
            int total = 0;
            for (int i = 0; i < Groups.Length; i++)
                if (Groups[i].Kind == kind) total += Groups[i].Count;

            return total;
        }

        public int Total
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Groups.Length; i++) total += Groups[i].Count;
                return total;
            }
        }
    }
}
