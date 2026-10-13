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
    /// One wave: how long the intermission before it lasts, and what walks in. Every wave has one - the
    /// first included - and the wave starts itself when it runs out unless the player cuts it short with
    /// `N`. The first wave's countdown is the longest number on the table, because the opening is built
    /// under it, and it does not start until the player's first building arms it
    /// (see <see cref="WaveDirector.ArmFirstWave"/>): planning is free, building is not.
    /// </summary>
    public sealed class WaveDefinition
    {
        /// <summary>
        /// Countdown before this wave, in seconds. The wave starts itself when it runs out, so 0 means
        /// "the moment this wave's wait is armed" - the run opening, for a first wave in a test rig;
        /// never what a shipped map's first wave wants, since the player has to mine, belt, split and
        /// shoot before anything walks in. That is a design rule a map is authored to, not a rule the
        /// simulation holds map data to.
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
