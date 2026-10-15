using System;

namespace Facet.Core
{
    /// <summary>Tunables for the simulation. Mutating these mid-run is allowed and instant.</summary>
    public sealed class SimConfig
    {
        /// <summary>Logic ticks per second. The renderer interpolates between them.</summary>
        public const float TickRate = 30f;

        public const float TickDt = 1f / TickRate;

        /// <summary>Belt travel speed in tiles per second. A belt cell holds one item, so this is also
        /// throughput in items/s - 1 tile/s is exactly 1 item/s, the ratio turret supply is reasoned about with.</summary>
        public float BeltSpeed = 1f;

        /// <summary>HP of the defended object. Reaching 0 loses the run.</summary>
        public float CoreMaxHp = 100f;

        /// <summary>Where a new run's seed comes from (§5: one seed per run, shown in the HUD), drawn
        /// once per run - at construction and again on every restart, because a restarted run is a new
        /// run. Null - the default - plays the map's authored seed, which is what the engine-free tests
        /// and a demo map that must replay exactly want. Core stays engine-free by never drawing the
        /// number itself: entropy is the host's to inject.</summary>
        public Func<int> FreshSeed;
    }
}
