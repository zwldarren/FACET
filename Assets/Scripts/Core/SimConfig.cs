namespace Facet.Core
{
    /// <summary>Tunables for the simulation. Mutating these mid-run is allowed and instant.</summary>
    public sealed class SimConfig
    {
        /// <summary>Logic ticks per second. The renderer interpolates between them.</summary>
        public const float TickRate = 30f;

        public const float TickDt = 1f / TickRate;

        /// <summary>
        /// Belt travel speed in tiles per second. Because a belt cell holds exactly one item,
        /// this is also the belt's throughput in items per second - 1 tile/s therefore means
        /// exactly 1 item/s, which is the ratio every turret's supply is reasoned about with.
        /// </summary>
        public float BeltSpeed = 1f;

        /// <summary>HP of the defended object. Reaching 0 loses the run.</summary>
        public float CoreMaxHp = 100f;
    }
}
