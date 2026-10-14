namespace Facet.Core
{
    /// <summary>
    /// The enemies a map may spawn. As with <see cref="BuildKind"/>, the ordinal is the identity the
    /// definition table indexes by; numbers and weakness live in <see cref="ContentDatabase.Enemy"/>.
    /// </summary>
    public enum EnemyKind : byte
    {
        /// <summary>Fast, light, weak to ◠. Two cannon shots bring one down, so the question is
        /// only how fast the line behind the cannon can feed it.</summary>
        Spike = 0,

        /// <summary>Slow, heavy, weak to ◡. Same six health as a Spike, but walks at two-thirds the speed,
        /// hits the Core for ten instead of six, and the cannon that answers a Spike barely scratches it.</summary>
        Bulwark = 1,
    }
}
