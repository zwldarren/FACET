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
    }
}
