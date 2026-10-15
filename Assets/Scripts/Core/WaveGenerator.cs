using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>
    /// The wave generator: the default source of waves, replacing the hand-authored table (§5). One wave
    /// is a budget of enemies, and every group in it pairs an <em>archetype</em> with a <em>demand</em> -
    /// the exact shape that group is weak to - drawn from the items this map can actually build.
    ///
    /// Four properties make it a design tool rather than a random spawner:
    ///
    /// <list type="bullet">
    /// <item><b>Determinism.</b> The only randomness in Core is this class's own stream, seeded once per
    /// run, so a run is reproducible from the seed in the HUD. No <c>System.Random</c> - its algorithm is
    /// not part of any contract, and a run that replays differently after a runtime upgrade is not a
    /// replay.</item>
    ///
    /// <item><b>The legal pool.</b> Weakness shapes are drawn only from <see cref="ShapePool"/> - the
    /// closure of the map's minerals under its <em>unlocked</em> operators. A map with no rotator is never
    /// briefed a left half-circle, because no line on it could ever build one. This is where the unlock
    /// ladder gets its teeth.</item>
    ///
    /// <item><b>Depth as progression.</b> A shape's depth is the number of operator steps that make it, so
    /// "harder" is a property of the algebra rather than of a stats table: the opening waves demand halves
    /// and quarters, later ones mixed and full shapes. The deeper the demand, the tougher and slower the
    /// group that carries it - a depth-1 demand comes as Runners, anything deeper as Tanks.</item>
    ///
    /// <item><b>Budget.</b> Wave n's budget is <c>base * growth^(n-1)</c>, jittered by up to
    /// <see cref="Jitter"/>, and enemies are bought out of it until it is spent - or until the wave reaches
    /// <see cref="MaxEnemies"/>, which is what stops a curve with no bound from asking for a number nobody
    /// will ever see. The curve is the only place difficulty grows, and it is one number to retune.</item>
    /// </list>
    /// </summary>
    public sealed class WaveGenerator
    {
        /// <summary>What wave 1 may spend, in enemy-cost units.</summary>
        public const float BaseBudget = 4f;

        /// <summary>How much the budget grows per wave: 1.25 is a quarter more pressure each time.</summary>
        public const float Growth = 1.25f;

        /// <summary>How far the budget may wander from its curve, as a share. 0.15 is the design's "±15%
        /// jitter": enough that two runs of one map do not play out identically, little enough that a
        /// surprise wave is never a wall.</summary>
        public const float Jitter = 0.15f;

        /// <summary>What a Runner costs the budget.</summary>
        public const float RunnerCost = 1f;

        /// <summary>What a Tank costs the budget: heavier in every sense, so one Tank is worth two and a
        /// half Runners of pressure.</summary>
        public const float TankCost = 2.5f;

        /// <summary>Seconds between one generated wave being cleared and the next arriving. Shorter than
        /// the scripted maps' opening (which is deliberately generous) and long enough to re-plumb a line.</summary>
        public const float Intermission = 25f;

        /// <summary>Seconds between two enemies of one group.</summary>
        private const float GroupInterval = 0.9f;

        /// <summary>Seconds between one group of a wave starting and the next - a wave arrives as a
        /// sequence, not as one block, so a player can read what is coming while it comes.</summary>
        private const float GroupStagger = 1.4f;

        /// <summary>How many groups one generated wave may hold: the director's own bound, referenced
        /// rather than repeated, because the two cannot be allowed to drift. A wave with more distinct
        /// archetype-and-demand pairs than this would not fit the director's fixed group timers, so the
        /// generator packs the tail into the groups it already has rather than asking for room it has not
        /// got.</summary>
        public const int MaxGroups = WaveDirector.MaxGroups;

        /// <summary>
        /// The most enemies one generated wave may hold, however far the budget curve has climbed:
        /// <see cref="MaxGroups"/> groups of twenty-five, which at the groups' own spacing is a quarter of a
        /// minute of arrivals per group. Two things need it. It is the pacing ceiling - a wave is a sequence
        /// a player reads, and past this the curve has outrun anything the director could spawn - and it is
        /// what keeps a deep wave number an <em>answer</em> rather than a loop, since the budget grows
        /// geometrically and there are wave numbers in the hundreds that a preview or a test may ask about.
        /// Retune it with <see cref="Growth"/> if endless runs should bite sooner.
        /// </summary>
        public const int MaxEnemies = MaxGroups * 25;

        private readonly int _seed;
        private readonly int _spawnPoints;
        private readonly ShapePool _pool;

        public WaveGenerator(int seed, ShapePool pool, int spawnPoints)
        {
            _seed = seed;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _spawnPoints = Math.Max(1, spawnPoints);
        }

        public ShapePool Pool => _pool;

        /// <summary>
        /// Wave <paramref name="number"/> (1-based; the run's own wave number). Pure: the same number always
        /// yields the same wave for a given seed, so the preview, the spawner and a test all read one answer
        /// rather than three draws of one stream.
        /// </summary>
        public WaveDefinition Wave(int number)
        {
            int n = Math.Max(1, number);

            var rng = new Rng(SeedFor(n));
            float budget = BudgetFor(n, ref rng);
            var groups = new List<SpawnGroup>();
            var used = new List<Shape>();

            int cap = DepthCap(n);
            int floor = DepthFloor(cap);

            while (budget >= RunnerCost)
            {
                // A pair the wave has not briefed yet, drawn from the band. A wave *briefs* each shape once:
                // the count that follows comes from Spread, not from drawing the same shape again.
                Shape demand = DrawFresh(rng, floor, cap, used);

                // Nothing left to brief: the band is spent, or the wave already holds every pair it may hold.
                // What remains of the budget is count on the pairs it has - which is what keeps the curve
                // buying pressure instead of the wave stopping at one enemy per group.
                if (demand.IsEmpty || groups.Count >= MaxGroups) break;

                // The archetype rides on the demand: a shallow ask comes as fast fragile Runners, a deeper
                // one as the slower Tank - so the wave's own hardness and its demand are one decision.
                EnemyKind kind = KindFor(demand);
                float cost = kind == EnemyKind.Runner ? RunnerCost : TankCost;

                // A pair too expensive for what is left ends the wave - but never before the first enemy
                // exists, so a budget below one Tank still buys one: a wave is never empty.
                if (cost > budget && groups.Count > 0) break;

                // A group of its own, entering at the next door in turn and staggered behind the one before
                // it. The demand is spent here, so a wave never briefs the same shape twice.
                groups.Add(new SpawnGroup(kind, 1, groups.Count % _spawnPoints,
                    groups.Count * GroupStagger, GroupInterval, demand));
                used.Add(demand);
                budget -= cost;
            }

            Spread(groups, budget);

            return new WaveDefinition(Intermission, groups.ToArray());
        }

        /// <summary>
        /// Spend what is left of a wave's budget on the pairs it already holds, in one pass over them. Each
        /// pair takes an even share of what remains at its own cost, so a wave's counts stay proportionate
        /// instead of piling onto the first group, and the wave stops at <see cref="MaxEnemies"/> however much
        /// budget is left over.
        ///
        /// <b>One pass, not one enemy at a time.</b> Buying them a unit at a time gives the same answer and
        /// does not terminate: the budget grows geometrically, so wave 200's is around 10^19 and a loop that
        /// spends it an enemy at a time never finishes - which is what a preview, or a test that asks an
        /// endless run what wave 200 looks like, would hang on.
        /// </summary>
        private static void Spread(List<SpawnGroup> groups, float budget)
        {
            if (groups.Count == 0) return;

            int total = 0;
            for (int i = 0; i < groups.Count; i++) total += groups[i].Count;

            for (int i = 0; i < groups.Count && budget >= RunnerCost; i++)
            {
                int room = MaxEnemies - total;
                if (room <= 0) return;

                SpawnGroup group = groups[i];
                float cost = group.Kind == EnemyKind.Runner ? RunnerCost : TankCost;

                // This pair's share of the ceiling and of what is left to spend, whichever is smaller - so
                // the counts stay proportionate instead of piling onto the first group. Both are bounded by
                // the slice of the ceiling, which is what makes the cast safe however large the budget is.
                int remaining = groups.Count - i;
                int evenRoom = (room + remaining - 1) / remaining;
                int buy = (int)MathF.Min(budget / (cost * remaining), evenRoom);

                // At least one while the budget still affords this pair at all, so the pass always makes
                // progress and leaves less than one enemy's worth behind.
                if (buy < 1) buy = 1;
                if (buy > room) buy = room;

                groups[i] = new SpawnGroup(group.Kind, group.Count + buy, group.SpawnPoint,
                    group.FirstDelay, group.Interval, group.Weakness);
                total += buy;
                budget -= buy * cost;
            }
        }

        /// <summary>Wave <paramref name="number"/>'s budget before jitter, for a HUD that wants to say
        /// what is coming. The curve's own number.</summary>
        public static float BudgetCurve(int number)
            => BaseBudget * MathF.Pow(Growth, Math.Max(1, number) - 1);

        /// <summary>The deepest operator step a wave may demand. The first two waves stay shallow on
        /// purpose - halves and quarters are what the opening's three machines can make - and the band
        /// widens with the run, up to whatever this map's operators and minerals allow. A map that can only
        /// mine has nothing past depth 0, and 0 is what it answers here.</summary>
        public int DepthCap(int number)
        {
            if (_pool.MaxDepth <= 0) return 0;   // ores only: the mined minerals are all there is to ask for

            int cap = number <= 2 ? 1 : number <= 6 ? 2 : _pool.MaxDepth;
            return Math.Min(cap, _pool.MaxDepth);
        }

        /// <summary>The shallowest tier a wave may draw from. One, because a briefed demand is meant to be
        /// something the player has to *build* and the mined ores themselves are depth 0 - except on a map
        /// with no operators at all, where depth 0 is all there is: a floor of 1 there would draw from an
        /// empty tier at every wave and hand the director waves nothing could ever arrive in.</summary>
        private static int DepthFloor(int cap) => cap <= 0 ? 0 : 1;

        /// <summary>The archetype a demand rides: the deeper the shape, the tougher and slower the group
        /// that carries it, so a wave's hardness and its demand stay one decision.</summary>
        private EnemyKind KindFor(Shape demand)
            => _pool.Depth(demand) <= 1 ? EnemyKind.Runner : EnemyKind.Tank;

        private float BudgetFor(int number, ref Rng rng)
        {
            float jitter = 1f + (rng.Unit() * 2f - 1f) * Jitter;
            return BudgetCurve(number) * jitter;
        }

        /// <summary>
        /// A demand of a depth picked from [floor, cap], preferring one this wave has not briefed yet. When
        /// the drawn tier is spent the rest of the band is tried - shallowest first, so the fallback biases
        /// the way the band does - before the answer is "nothing": on a small pool one tier can be a single
        /// shape, and ending the wave at the first exhausted tier would leave the budget unspent and make
        /// the curve a promise the wave does not keep.
        /// </summary>
        private Shape DrawFresh(Rng rng, int floor, int cap, List<Shape> used)
        {
            int depth = floor + rng.Next(cap - floor + 1);
            Shape drawn = Draw(rng, depth, used);
            if (!drawn.IsEmpty) return drawn;

            for (int d = floor; d <= cap; d++)
            {
                if (d == depth) continue;

                Shape other = Draw(rng, d, used);
                if (!other.IsEmpty) return other;
            }

            return Shape.None;
        }

        /// <summary>A demand of exactly this depth, drawn uniformly from the items at it that
        /// <paramref name="used"/> does not already name. Nothing when the tier is empty or spent.</summary>
        private Shape Draw(Rng rng, int depth, List<Shape> used)
        {
            IReadOnlyList<Shape> tier = _pool.AtDepth(depth);
            if (tier.Count == 0) return Shape.None;

            int start = rng.Next(tier.Count);
            for (int i = 0; i < tier.Count; i++)
            {
                Shape candidate = tier[(start + i) % tier.Count];
                if (!used.Contains(candidate)) return candidate;
            }

            return Shape.None;
        }

        /// <summary>
        /// One run seed plus one wave number, hashed into a stream of its own. Distinct per wave - so the
        /// preview of wave 7 does not disturb wave 3 - and stable across machines, because it is arithmetic
        /// rather than a framework's random.
        /// </summary>
        private uint SeedFor(int number)
        {
            unchecked
            {
                uint hash = (uint)_seed * 2654435761u;
                hash ^= (uint)number * 40503u;
                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                return hash == 0 ? 1u : hash;
            }
        }

        /// <summary>
        /// A xorshift stream, twelve lines and no contract to break: Core's determinism is a promise the
        /// whole test suite rests on, and the framework's random generator is not part of one.
        /// </summary>
        private struct Rng
        {
            private uint _state;

            public Rng(uint seed) => _state = seed == 0 ? 1u : seed;

            public uint Next()
            {
                unchecked
                {
                    _state ^= _state << 13;
                    _state ^= _state >> 17;
                    _state ^= _state << 5;
                    return _state;
                }
            }

            /// <summary>A uniform index in [0, max). Zero for a degenerate range, because a caller asking
            /// for no choice gets the first one.</summary>
            public int Next(int max) => max <= 1 ? 0 : (int)(Next() % (uint)max);

            /// <summary>A uniform value in [0, 1).</summary>
            public float Unit() => (Next() >> 8) * (1f / 16777216f);
        }
    }
}
