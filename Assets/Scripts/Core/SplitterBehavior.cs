namespace Facet.Core
{
    /// <summary>
    /// The balancer: one hub whose four ports are read off the belts around it every tick - a belt
    /// pointing into the hub is an input, a belt pointing away from it is an output, so 1-in-3-out,
    /// 3-in-1-out and 2-in-2-out are the same building on different streets, and re-pointing one belt
    /// re-wires the hub without touching it.
    ///
    /// The hub holds one item at a time and pushes it round-robin across the outputs (skipping any
    /// whose belt is full or jammed), which makes balanced distribution the default and backpressure -
    /// not loss - the answer to a blocked output.
    /// </summary>
    internal sealed class SplitterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState splitter, Int2 cell, in MachineTickContext ctx)
        {
            // Push first, so a hub that empties can take again in the same tick. Round-robin across
            // the outlets - the shared rule every machine with ports uses, so a splitter and a
            // converter distribute the same way.
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
