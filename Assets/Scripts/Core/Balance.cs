namespace Facet.Core
{
    /// <summary>
    /// The global rules not tied to one piece of content: how close an enemy must be to hit the world's
    /// one losing object, how forgiving a projectile's hit test is, how long a stray shot lives, how much
    /// room a wave gives itself, and what hitting an enemy's weakness is worth.
    ///
    /// Everything else - build costs, turret and enemy stats, the machine table, the decomposer recipe -
    /// lives in <see cref="ContentDatabase"/>, the single source of truth the simulation, the view and the
    /// Editor asset all read. Call sites that mean the shipped table say <c>ContentDatabase.Default</c>;
    /// those that mean <em>this</em> world say <c>world.Content</c>. No table, no lookup here.
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
        /// units: a step's cost is 1, so this is one for one.
        ///
        /// One keeps the two behaviours the field must show on the right sides of the detour rule
        /// (<see cref="PathDetourFactor"/>): a wave will not walk far out of its way to dodge a gun it
        /// could overrun, but a corridor swept along its length is flanked via the open approach beside
        /// it. Cover the whole approach, or accept the wave finds the lane you did not.
        /// </summary>
        public const int PathThreatWeight = 1;

        /// <summary>
        /// How much cheaper than the covered way a flanking route must be before the wave takes it: the
        /// open way is worth the swing while its threat-weighted cost undercuts what the plain shortest
        /// way would itself pay under fire by this share, plus <see cref="PathDetourSlack"/> steps of slop.
        /// Past that the wave overruns the fire - the Core must be reached, and a defence covering every
        /// approach is crossed precisely because there is no way around it.
        ///
        /// 0.8 and 2 separate the two cases the rule exists for: a skirt saving two steps around a lone
        /// gun's edge is overrun, while a gauntlet swept end to end beside an open corridor is flanked.
        /// </summary>
        public const float PathDetourFactor = 0.8f;

        /// <summary>The flat slop, in plain steps, added to the flanking budget: a wave does not count
        /// pennies. See <see cref="PathDetourFactor"/>.</summary>
        public const float PathDetourSlack = 2f;

        /// <summary>
        /// What one cell of a standing building costs the breach field - the route a wave takes when the
        /// way round is shut and the only way in is through what the player built. A step of open ground
        /// costs 1, so a thousand is not a detour a wave weighs: the field walks the map's own ground to
        /// the last step before it names a building, and then names the *cheapest* hole - the short
        /// crossing, one wall deep rather than four. Which building that turns out to be is a fact about
        /// the route the field chose, never a comparison of healths.
        /// </summary>
        public const int PathBreachWeight = 1000;
    }
}
