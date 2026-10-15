using System;

namespace Facet.Core
{
    /// <summary>
    /// The stockpile: complete shapes banked by belting them into the Core, minus what building has spent.
    /// A shape on a belt is inventory in transit, not spendable, which makes resource lines a logistics
    /// problem rather than a timer.
    ///
    /// The unit is a <b>shape</b> rather than a circle, because the Core banks any complete item
    /// (<see cref="Shape.IsFull"/>): a full square pays exactly what a full circle pays, so the money of a
    /// run is a property of the algebra and not of the map's vein.
    /// </summary>
    public sealed class EconomyState
    {
        private readonly ContentDatabase _content;

        /// <summary>Spendable shapes right now. Read by the HUD and by placement.</summary>
        public int Shapes { get; private set; }

        /// <summary>Total banked at the Core this run, including what has been spent. Telemetry for
        /// the end-of-run report: how much economy the player built.</summary>
        public int TotalBanked { get; private set; }

        public EconomyState(ContentDatabase content, int startShapes)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Shapes = startShapes;
        }

        public bool CanAfford(BuildKind kind) => Shapes >= _content.Machine(kind).Cost;

        /// <summary>What one of these costs in this run's table. A reader reporting spend and refund
        /// ("built for 10, refunded 10") needs the same number the spend used.</summary>
        public int CostOf(BuildKind kind) => _content.Machine(kind).Cost;

        /// <summary>Charge for one placement. False when the stockpile cannot cover it.</summary>
        public bool TrySpend(BuildKind kind)
        {
            int cost = _content.Machine(kind).Cost;
            if (Shapes < cost) return false;

            Shapes -= cost;
            return true;
        }

        /// <summary>Full refund on removal: the cost gates how fast you can expand, not whether
        /// you dare to experiment. A cleared jam removes nothing and refunds nothing.</summary>
        public void Refund(BuildKind kind) => Shapes += _content.Machine(kind).Cost;

        /// <summary>Bank shapes delivered to the Core.</summary>
        public void Bank(int shapes)
        {
            Shapes += shapes;
            TotalBanked += shapes;
        }

        public void Reset(int startShapes)
        {
            Shapes = startShapes;
            TotalBanked = 0;
        }
    }
}
