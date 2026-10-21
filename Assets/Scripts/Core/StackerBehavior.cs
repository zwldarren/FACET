namespace Facet.Core
{
    /// <summary>
    /// The operator that makes mixed shapes: it holds one piece, and when a second arrives that the first
    /// does not overlap, it waits out its interval (2 s) and pushes the union out through any wired outlet.
    ///
    /// <b>Overlap stalls, it does not jam.</b> A second delivery the held piece cannot union with is left
    /// standing on its own belt - a visible queue that says "feed me the other half", which a jam would
    /// not: a jam destroys the item the player just spent a chain building. The union of two disjoint
    /// shapes is unambiguous without any layer concept, which is why stacking here is a plain OR and not
    /// shapez's layered merge.
    /// </summary>
    internal sealed class StackerBehavior : IMachineBehavior
    {
        public void Step(ref MachineState stacker, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(stacker.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, stacker.Direction, in def);

            // The finished union leaves the moment an outlet has room; one is held at a time, so a blocked
            // outlet means the machine stops taking pieces rather than piling them up invisibly.
            if (stacker.OutputCount > 0 &&
                MachineDelivery.TryPushOut(ctx.Belts, ref stacker, cell, stacker.OutputShape))
                stacker.OutputCount--;

            if (!stacker.Carried.IsEmpty && !stacker.Carried2.IsEmpty)
            {
                stacker.Cooldown -= ctx.Dt;
                if (stacker.Cooldown > 0f)
                {
                    MachineClock.Waiting(ref stacker, waiting: false);   // fusing
                    return;
                }

                stacker.OutputShape = Shape.Stacked(stacker.Carried, stacker.Carried2);
                stacker.OutputCount = 1;
                stacker.Carried = Shape.None;
                stacker.Carried2 = Shape.None;
                MachineClock.Waiting(ref stacker, waiting: false);
                return;
            }

            // The last union has not left yet: the one wait on this machine that is a stoppage rather than a
            // design. Holding a single piece for a second one is the other case, and is not counted. A full
            // outlet is not counted either - that is a line loaded as far as it goes, and the stop it stands
            // behind is reported by whatever is actually stuck (or by nothing, when the gun is only loaded).
            if (stacker.OutputCount > 0)
            {
                MachineClock.Waiting(ref stacker,
                    waiting: !MachineDelivery.HasOutlet(ctx.Belts, cell, DirMask.All));
                return;
            }

            MachineClock.Waiting(ref stacker, waiting: false);

            // Intake, in the ports' scan order. The first piece is anything the diet accepts; the second
            // must be disjoint from it, and a delivery that overlaps is skipped rather than stopped on -
            // a later port may hold the piece that fits.
            for (int i = 0; i < MachineDelivery.Ports.Length; i++)
            {
                if (!MachineDelivery.TryReadyAt(ctx.Belts, cell, MachineDelivery.Ports[i],
                        rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                if (!MachineDelivery.Eats(in def, state.Item)) continue;

                bool first = stacker.Carried.IsEmpty;
                if (!first && (!stacker.Carried2.IsEmpty || stacker.Carried.Overlaps(state.Item))) continue;

                // The take can still refuse a jammed segment, and a refused take must not leave the item
                // standing on its belt *and* inside the machine: the port is skipped and the scan moves on
                // to whichever delivery can really be picked up.
                if (!ctx.Belts.TryTakeItem(neighbour, out Shape taken)) continue;

                if (first) stacker.Carried = taken;
                else
                {
                    stacker.Carried2 = taken;
                    stacker.Cooldown = def.Interval;
                }

                return;
            }
        }
    }
}
