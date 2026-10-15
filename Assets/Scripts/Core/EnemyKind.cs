namespace Facet.Core
{
    /// <summary>
    /// The enemy <em>archetypes</em>: what walks at the Core, as opposed to what kills it. The two fields
    /// are independent by design (§4.2) - the archetype is the movement and attack profile, and the
    /// weakness is a shape the wave generator assigns per group - so one Runner can demand a top
    /// half-circle in wave 3 and a four-quadrant mixed round in wave 9.
    ///
    /// The ordinal is the identity the definition table indexes by; numbers live in
    /// <see cref="ContentDatabase.Enemy"/>, and the demand lives in the wave.
    /// </summary>
    public enum EnemyKind : byte
    {
        /// <summary>Fast and light: the archetype a shallow demand comes with. Two matched shots bring
        /// one down, so the question is only how fast the line behind the gun can feed it.</summary>
        Runner = 0,

        /// <summary>Slow and heavy: the archetype a deep demand comes with. The same six health as a
        /// Runner, but it walks at two-thirds the speed and hits the Core for ten - and it is worth
        /// building the deeper round for, because a resisted shot barely marks it.</summary>
        Tank = 1,
    }
}
