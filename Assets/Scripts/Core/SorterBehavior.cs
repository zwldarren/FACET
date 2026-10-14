namespace Facet.Core
{
    /// <summary>
    /// Routes by shape: the filtered shape leaves by the side the sorter faces, every other shape by
    /// the other wired outlets. Reads its ports off the surrounding belts like a splitter, holds one
    /// item with no cooldown, and blocks a needed outlet (backpressure, not loss) rather than misroute.
    /// </summary>
    internal sealed class SorterBehavior : IMachineBehavior
    {
        /// <summary>Port scan order, fixed so ties are the map's decision, and shared with
        /// <see cref="MachineDelivery"/> so every ported machine agrees on it.</summary>
        private static readonly Dir[] Ports = MachineDelivery.Ports;

        public void Step(ref MachineState sorter, Int2 cell, in MachineTickContext ctx)
        {
            // Push first, so a sorter that empties can take again in the same tick.
            if (sorter.Carried != ShapeType.None &&
                MachineDelivery.TryPushOut(ctx.Belts, ref sorter, cell, sorter.Carried,
                    Allowed(ctx.Content, in sorter, sorter.Carried)))
                sorter.Carried = ShapeType.None;

            // Still holding: the routed outlet had no room, or the item has nowhere to go. Waiting is
            // the honest signal for a mis-wired stage, the same as a drill facing bare ground.
            if (sorter.Carried != ShapeType.None) return;

            // Take from any side that delivers, in the same fixed order every ported machine uses.
            if (MachineDelivery.TryTakeInput(ctx.Belts, cell, out ShapeType shape))
                sorter.Carried = shape;
        }

        /// <summary>Which outlets may take the held item: the facing side for the filtered shape, every
        /// other side for the rest. The filter is read from the machine's own definition, so a retuned
        /// sorter routes differently with no code change.</summary>
        private static DirMask Allowed(ContentDatabase content, in MachineState sorter, ShapeType shape)
        {
            bool filtered = shape == content.Machine(sorter.Build).Filter;
            DirMask mask = DirMask.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                // The filtered shape leaves by the facing side, every other shape by the remaining
                // sides. The two sets partition the four sides, so an item is never routed wrongly.
                bool facing = Ports[i] == sorter.Direction;
                if (filtered ? facing : !facing) mask = mask.With(Ports[i]);
            }

            return mask;
        }
    }
}
