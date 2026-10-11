namespace Facet.Core
{
    /// <summary>
    /// The global rules that are not tied to one piece of content: how close an enemy must be to hit
    /// the world's one losing object, how forgiving a projectile's hit test is, how long a stray shot
    /// lives, how much room a wave gives itself, and what hitting an enemy's weakness is worth.
    ///
    /// Everything else that used to live in this file - build costs, turret and enemy stats, the
    /// machine table, the decomposer recipe - lives in <see cref="ContentDatabase"/>, the single
    /// source of truth the simulation, the view and the Editor asset all read. The read-through
    /// facade this class used to keep over the shipped table is gone: it was a second spelling of
    /// <see cref="ContentDatabase.Default"/>, and a second spelling is a second thing to keep true.
    /// Call sites that mean the shipped table - tests that pin its numbers, input that maps a digit -
    /// now say <c>ContentDatabase.Default</c>; call sites that mean <em>this</em> world say
    /// <c>world.Content</c>. What is left here is exactly the rules above: no table, no lookup.
    /// </summary>
    public static class Balance
    {
        /// <summary>How close to the Core's centre an enemy has to be before it stops and hits it.
        /// The Core's own half-extent: enemies stop at its edge, not inside it.</summary>
        public static readonly float CoreAttackRadius = CoreState.Size * 0.5f;

        /// <summary>A projectile bursts this close to its target. Generous, because a miss is
        /// indistinguishable from a bug to the player.</summary>
        public static readonly float ProjectileHitRadius = 0.35f;

        /// <summary>A shot that never lands is destroyed after this long, so a killed target cannot
        /// leave a projectile flying forever.</summary>
        public static readonly float ProjectileMaxLifetime = 6f;

        /// <summary>
        /// How close two walkers may stand before they push each other apart, in tiles: a body's width,
        /// and the one source of it - the view draws bodies at half this, in
        /// <c>Palette.EnemyLook.BodyRadius</c>. A closer pair is pushed apart by
        /// <see cref="EnemyField"/>'s separation pass; without the rule the flow field would walk a
        /// wave as one sprite.
        /// </summary>
        public static readonly float EnemySpacing = 0.84f;

        /// <summary>Shots matching an enemy's weakness deal their full listed damage.</summary>
        public const float WeaknessDamageMultiplier = 1f;

        /// <summary>Shots that do not match the enemy's weakness deal reduced damage.</summary>
        public const float ResistantDamageMultiplier = 0.25f;
    }
}
