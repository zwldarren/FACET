namespace Facet.Core
{
    /// <summary>
    /// The Core is also the bank. A belt that delivers into its footprint is emptied: a circle
    /// becomes one spendable stockpile, anything else (a stray half-circle) is destroyed - the sink
    /// is the overflow drain as well as the mint. Delivery uses the same rule every machine uses:
    /// the belt's own direction must point in, so "route the money home" is a layout problem, and
    /// the resource lines flowing inward cross the ammunition lines flowing outward.
    /// </summary>
    public sealed class CoreSinkSystem
    {
        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly EconomyState _economy;

        /// <summary>The stream deliveries are reported on, so a view can flash the Core the tick money
        /// actually lands.</summary>
        private readonly SimEventBuffer _events;

        public CoreSinkSystem(TileGrid grid, BeltField belts, EconomyState economy, SimEventBuffer events)
        {
            _grid = grid;
            _belts = belts;
            _economy = economy;
            _events = events;
        }

        /// <summary>Empty every belt delivering into the Core's footprint. Runs once per tick.</summary>
        public void Step(Int2 coreOrigin)
        {
            // The cells a delivery can come from: the ring one tile around the footprint, checked in
            // a fixed perimeter order so which of two ready belts banks first is the map's decision.
            int minX = coreOrigin.X - 1;
            int minY = coreOrigin.Y - 1;
            int maxX = coreOrigin.X + CoreState.Size;
            int maxY = coreOrigin.Y + CoreState.Size;

            for (int x = minX; x <= maxX; x++) TrySink(new Int2(x, minY));   // south row
            for (int x = minX; x <= maxX; x++) TrySink(new Int2(x, maxY));   // north row
            for (int y = coreOrigin.Y; y < coreOrigin.Y + CoreState.Size; y++)
            {
                TrySink(new Int2(minX, y));   // west column
                TrySink(new Int2(maxX, y));   // east column
            }
        }

        private void TrySink(Int2 cell)
        {
            if (!_belts.TryGet(cell, out BeltState belt)) return;
            if (belt.Jammed || belt.Item == ShapeType.None || belt.Progress < 1f) return;
            if (_grid.Get(cell + belt.Direction.Offset()) != TileKind.Core) return;

            _belts.TryTakeItem(cell, out ShapeType shape);
            if (shape == ShapeType.Circle)
            {
                _economy.Bank(1);
                _events.Banked(cell);
            }

            // Anything else is the overflow drain: a stray half-circle arriving at the Core is
            // destroyed, which is a fact visible on the belt rather than an event worth reporting.
        }
    }
}
