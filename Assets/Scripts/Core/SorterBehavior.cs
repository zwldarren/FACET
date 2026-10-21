namespace Facet.Core
{
    /// <summary>
    /// Routes by shape: what the machine's filter matches leaves by the side it faces, every other shape
    /// by the other wired outlets. The filter is a row's own (<see cref="MachineDef.Filter"/>) and has two
    /// modes - "anything with this mineral" or "exactly this item" - so one building answers both "keep one
    /// mineral off the other line" and "send the top half-circles this way and the rest that way". It cannot
    /// name the currency, which is a <em>complete</em> shape rather than a mineral: completeness is not one
    /// of the filter's modes.
    ///
    /// Reads its ports off the surrounding belts like a splitter, holds one item with no cooldown, and
    /// blocks a needed outlet (backpressure, not loss) rather than misroute.
    /// </summary>
    internal sealed class SorterBehavior : IMachineBehavior
    {
        /// <summary>Port scan order, fixed so ties are the map's decision, and shared with
        /// <see cref="MachineDelivery"/> so every ported machine agrees on it.</summary>
        private static readonly Dir[] Ports = MachineDelivery.Ports;

        public void Step(ref MachineState sorter, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(sorter.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, sorter.Direction, in def);

            // Push first, so a sorter that empties can take again in the same tick.
            if (!sorter.Carried.IsEmpty &&
                MachineDelivery.TryPushOut(ctx.Belts, ref sorter, cell, sorter.Carried,
                    Allowed(in sorter, sorter.Carried)))
                sorter.Carried = Shape.None;

            // Still holding: the routed outlet had no room, or the shape has nowhere to go. Only the second
            // is a stoppage - a full outlet is backpressure, and a line loaded behind a full consumer is a
            // line doing its job. A shape whose routed side has no belt at all will never leave, so that
            // wait is the mis-wired stage this counter exists for, and it is counted.
            if (!sorter.Carried.IsEmpty)
            {
                MachineClock.Waiting(ref sorter,
                    waiting: !MachineDelivery.HasOutlet(ctx.Belts, cell, Allowed(in sorter, sorter.Carried)));
                return;
            }

            // Empty again: whatever happens next, this machine is not waiting on an outlet.
            MachineClock.Waiting(ref sorter, waiting: false);

            // Take from any side that delivers, in the same fixed order every ported machine uses.
            if (MachineDelivery.TryTakeInput(ctx.Belts, cell, in def, out Shape shape))
                sorter.Carried = shape;
        }

        /// <summary>Which outlets may take the held item: the facing side when the filter matches it, every
        /// other side when it does not. The filter is the machine's own - seeded from its row and editable
        /// through the picker - so re-routing a standing sorter needs no rebuild.</summary>
        private static DirMask Allowed(in MachineState sorter, Shape shape)
        {
            bool matched = sorter.Filter.Matches(shape);
            DirMask mask = DirMask.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                // The matching shape leaves by the facing side, every other shape by the remaining
                // sides. The two sets partition the four sides, so an item is never routed wrongly.
                bool facing = Ports[i] == sorter.Direction;
                if (matched ? facing : !facing) mask = mask.With(Ports[i]);
            }

            return mask;
        }
    }
}
