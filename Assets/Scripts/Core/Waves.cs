namespace Facet.Core
{
    /// <summary>One block of "N of this enemy, entering at this point, starting then, this far apart, weak
    /// to this shape".</summary>
    public readonly struct SpawnGroup
    {
        public readonly EnemyKind Kind;
        public readonly int Count;
        public readonly int SpawnPoint;
        public readonly float FirstDelay;
        public readonly float Interval;

        /// <summary>
        /// What the group is weak to, one exact item - <b>a demand, not a stat</b>. It used to live on the
        /// enemy's kind, which made every wave of that kind the same puzzle; the wave generator draws it per
        /// group now, and a hand-authored wave states it here. There is deliberately no default: a group
        /// with no demand can never be matched, so every author has to answer the question.
        /// </summary>
        public readonly Shape Weakness;

        public SpawnGroup(EnemyKind kind, int count, int spawnPoint, float firstDelay, float interval,
            Shape weakness)
        {
            Kind = kind;
            Count = count;
            SpawnPoint = spawnPoint;
            FirstDelay = firstDelay;
            Interval = interval;
            Weakness = weakness;
        }
    }

    /// <summary>
    /// One wave: the intermission before it and what walks in. Every wave has one, and it starts itself
    /// when the intermission runs out unless the player cuts it short with `N`. The first wave does not
    /// start until the player's first building arms it (see <see cref="WaveDirector.ArmFirstWave"/>).
    ///
    /// A wave is authored by a map's table or produced by the <see cref="WaveGenerator"/>; the director
    /// treats the two identically, which is what lets a map script its opening and generate the rest.
    /// </summary>
    public sealed class WaveDefinition
    {
        /// <summary>Countdown before this wave, in seconds; 0 means "the moment the wait is armed" (a
        /// test rig's opening, never a shipped map's first wave, since the player must mine, cut and shoot
        /// first). A design rule maps are authored to, not one the simulation holds them to.</summary>
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
