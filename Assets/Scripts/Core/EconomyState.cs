using System;

namespace Facet.Core
{
    /// <summary>
    /// The stockpile: circles banked by belting them into the Core, minus what building has spent. A
    /// circle on a belt is inventory in transit, not spendable, which makes resource lines a logistics
    /// problem rather than a timer.
    /// </summary>
    public sealed class EconomyState
    {
        private readonly ContentDatabase _content;

        /// <summary>Spendable circles right now. Read by the HUD and by placement.</summary>
        public int Circles { get; private set; }

        /// <summary>Total banked at the Core this run, including what has been spent. Telemetry for
        /// the end-of-run report: how much economy the player built.</summary>
        public int TotalBanked { get; private set; }

        public EconomyState(ContentDatabase content, int startCircles)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            Circles = startCircles;
        }

        public bool CanAfford(BuildKind kind) => Circles >= _content.Machine(kind).Cost;

        /// <summary>What one of these costs in this run's table. A reader reporting spend and refund
        /// ("built for 10, refunded 10") needs the same number the spend used.</summary>
        public int CostOf(BuildKind kind) => _content.Machine(kind).Cost;

        /// <summary>Charge for one placement. False when the stockpile cannot cover it.</summary>
        public bool TrySpend(BuildKind kind)
        {
            int cost = _content.Machine(kind).Cost;
            if (Circles < cost) return false;

            Circles -= cost;
            return true;
        }

        /// <summary>Full refund on removal: the cost gates how fast you can expand, not whether
        /// you dare to experiment. A cleared jam removes nothing and refunds nothing.</summary>
        public void Refund(BuildKind kind) => Circles += _content.Machine(kind).Cost;

        /// <summary>Bank circles delivered to the Core.</summary>
        public void Bank(int circles)
        {
            Circles += circles;
            TotalBanked += circles;
        }

        public void Reset(int startCircles)
        {
            Circles = startCircles;
            TotalBanked = 0;
        }
    }
}
