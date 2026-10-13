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

        /// <summary>
        /// The extra cost of walking one cell under a standing turret's fire, in the flow field's own
        /// units - a step's cost is 1, so this is the exchange rate between a covered cell and an open
        /// one: one for one.
        ///
        /// One is what keeps the two behaviours the field has to show on the right sides of the detour
        /// rule (<see cref="PathDetourFactor"/>): a wave will not walk far out of its way to dodge a
        /// gun it could simply overrun, but a corridor swept along its whole length is flanked for the
        /// open approach beside it, because twenty covered cells saved are worth the twelve steps the
        /// swing costs. That is the reading a defence should get - cover the whole approach, or accept
        /// that the wave finds the lane you did not.
        /// </summary>
        public const int PathThreatWeight = 1;

        /// <summary>
        /// How much cheaper than the covered way a flanking route has to be before the wave takes it:
        /// the open way is worth the swing while its threat-weighted cost undercuts what the plain
        /// shortest way would itself pay under fire by this share, plus <see cref="PathDetourSlack"/>
        /// steps of slop. Past that, the wave overruns the fire instead - the Core has to be reached,
        /// and a defence that covers every approach is crossed precisely because there is no way
        /// around it.
        ///
        /// 0.8 and 2 are the pair that separates the two cases the rule exists for: a skirt that saves
        /// two steps around a lone gun's edge pays about what it saves and is overrun, while a gauntlet
        /// swept end to end beside an open parallel corridor - half again the price - is flanked. At
        /// 1.0 the skirt is taken too, and a lone gun beside the lane stops mattering; at 0.5 even the
        /// honest flank is refused, and the avoidance never shows.
        /// </summary>
        public const float PathDetourFactor = 0.8f;

        /// <summary>The flat slop, in plain steps, added to the flanking budget: a wave does not count
        /// pennies. See <see cref="PathDetourFactor"/>.</summary>
        public const float PathDetourSlack = 2f;
    }
}
