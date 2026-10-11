namespace Facet.Core
{
    /// <summary>
    /// Routes by shape. It reads its ports off the belts around it exactly like a splitter - a belt
    /// pointing in is an input, a belt pointing away is an outlet - but where a splitter rotates an
    /// item across whatever outlets it has, a sorter sends the shape its own definition filters for
    /// out the side it faces, and every other shape out the other outlets.
    ///
    /// That one bit of data is what makes the doc's twist playable at scale. Map 1 asks the player to
    /// keep circles (money) and half-circles (ammo) apart, and with no router the answer is geometry:
    /// lay two entirely separate lines from two patches. One sorter facing the Core turns that into
    /// "split the line once" - the money banks, the ammo carries on - so the interesting decision
    /// moves from where the belts go to which building is worth its cost.
    ///
    /// Routing is strict, and that is the point. If the outlet a shape needs is blocked or not wired,
    /// the sorter holds the item and lets the belts behind it back up rather than pushing the shape
    /// somewhere that will never want it - the same "backpressure, not loss" rule a splitter follows.
    /// A mis-wired outlet is then a visible queue at the sorter, not a silent wrong-shaped delivery
    /// that jams something four cells downstream.
    ///
    /// Holds one item, reads the belts fresh every tick, and has no cooldown of its own: routing is
    /// instant, so the line's speed is its belts' speed and never the sorter's.
    /// </summary>
    internal sealed class SorterBehavior : IMachineBehavior
    {
        /// <summary>Port scan order, fixed so ties are the map's decision, not iteration order's.
        /// Borrowed from <see cref="MachineDelivery"/> so every ported machine agrees on the order.</summary>
        private static readonly Dir[] Ports = MachineDelivery.Ports;

        public void Step(ref MachineState sorter, Int2 cell, in MachineTickContext ctx)
        {
            // Push first, so a sorter that empties can take again in the same tick.
            if (sorter.Carried != ShapeType.None &&
                MachineDelivery.TryPushOut(ctx.Belts, ref sorter, cell, sorter.Carried,
                    Allowed(ctx.Content, in sorter, sorter.Carried)))
                sorter.Carried = ShapeType.None;

            // Still holding: the routed outlet had no room, or the item has nowhere to go at all.
            // Waiting is the honest signal for "this stage is wired wrong", the same as a drill facing
            // bare ground.
            if (sorter.Carried != ShapeType.None) return;

            // Take from any side that delivers, in the same fixed order every ported machine uses.
            if (MachineDelivery.TryTakeInput(ctx.Belts, cell, out ShapeType shape))
                sorter.Carried = shape;
        }

        /// <summary>
        /// Which outlets may take the item the sorter is holding: the facing side for the filtered
        /// shape, every other side for the rest. The filter is read from the machine's own definition,
        /// so a sorter retuned in the Editor routes differently without a code change - and two
        /// sorters filtering different shapes are two content rows, not two classes.
        /// </summary>
        private static DirMask Allowed(ContentDatabase content, in MachineState sorter, ShapeType shape)
        {
            bool filtered = shape == content.Machine(sorter.Build).Filter;
            DirMask mask = DirMask.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                // The filtered shape leaves by the side the sorter faces; every other shape leaves by
                // the remaining sides. The two sets partition the four sides, so no item can ever be
                // routed to a side it does not belong on.
                bool facing = Ports[i] == sorter.Direction;
                if (filtered ? facing : !facing) mask = mask.With(Ports[i]);
            }

            return mask;
        }
    }
}
