namespace Facet.Core
{
    /// <summary>
    /// The balancer: one hub whose four ports are read off the belts around it every tick - a belt
    /// pointing in is an input, one pointing away an output. It holds one item and pushes it round-robin
    /// across the outputs, skipping full or jammed ones: balanced distribution by default, backpressure
    /// (not loss) when every outlet is blocked.
    /// </summary>
    internal sealed class SplitterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState splitter, Int2 cell, in MachineTickContext ctx)
        {
            // Push first, so a hub that empties can take again in the same tick. Round-robin across the
            // outlets - the shared rule every ported machine uses.
            if (splitter.Carried != ShapeType.None &&
                MachineDelivery.TryPushOut(ctx.Belts, ref splitter, cell, splitter.Carried))
                splitter.Carried = ShapeType.None;

            if (splitter.Carried != ShapeType.None) return;

            // Take from any side that delivers, in the same fixed order every ported machine uses.
            if (MachineDelivery.TryTakeInput(ctx.Belts, cell, out ShapeType shape))
                splitter.Carried = shape;
        }
    }
}
