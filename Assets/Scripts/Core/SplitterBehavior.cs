namespace Facet.Core
{
    /// <summary>
    /// The balancer: one hub whose four ports are read off the belts around it every tick - a belt
    /// pointing in is an input, any other an output. It holds one item and pushes it round-robin
    /// across the outputs, skipping full or jammed ones: balanced distribution by default, backpressure
    /// (not loss) when every outlet is blocked.
    /// </summary>
    internal sealed class SplitterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState splitter, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(splitter.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, splitter.Direction, in def);

            // Push first, so a hub that empties can take again in the same tick. Round-robin across the
            // outlets - the shared rule every ported machine uses.
            if (!splitter.Carried.IsEmpty &&
                MachineDelivery.TryPushOut(ctx.Belts, ref splitter, cell, splitter.Carried))
                splitter.Carried = Shape.None;

            // Still holding: every outlet it may use is full or jammed. That is a stoppage the belts behind
            // it cannot see - the hub takes nothing, so nothing upstream is ever "blocked" - which is why it
            // is counted here.
            if (!splitter.Carried.IsEmpty)
            {
                MachineClock.Waiting(ref splitter, waiting: true);
                return;
            }

            MachineClock.Waiting(ref splitter, waiting: false);

            // Take from any side that delivers, in the same fixed order every ported machine uses.
            if (MachineDelivery.TryTakeInput(ctx.Belts, cell, in def, out Shape shape))
                splitter.Carried = shape;
        }
    }
}
