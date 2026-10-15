namespace Facet.Core
{
    /// <summary>
    /// Runs <see cref="RecipeDef"/>s: eats what it has a recipe for, waits out the interval, then pushes
    /// the output through the sides with a belt pointing away. Which recipes is the definition's
    /// business: a machine naming one runs it, and one naming none runs every recipe in the table,
    /// chosen per delivery by the input shape - the shipped decomposer splits both minerals this way.
    /// Outputs buffer (two splits deep for the shipped recipes) under <see cref="MachineState.OutputShape"/>,
    /// remembered from the recipe that made them, and leave one per tick while an outlet has room, so a
    /// split is never lost to a blocked output. A buffer holds one shape at a time: while a batch waits
    /// to drain, intake of a different recipe's input waits with it. A delivery the machine has no
    /// recipe for jams the feeding belt whether or not the machine is busy.
    /// </summary>
    internal sealed class ConverterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx)
        {
            ContentDatabase content = ctx.Content;
            MachineDef def = content.Machine(machine.Build);

            if (MachineDelivery.TryFindWrongDelivery(ctx.Belts, cell, content, in def, out Int2 wrong))
                ctx.Belts.TryJam(wrong);

            // Push buffered outputs out any wired outlet. One per tick is far above belt capacity, so
            // the belts - not this line - set the output rate; two outlets split one batch across belts.
            if (machine.OutputCount > 0 &&
                MachineDelivery.TryPushOut(ctx.Belts, ref machine, cell, machine.OutputShape))
                machine.OutputCount--;

            if (machine.Carried != ShapeType.None)
            {
                // The recipe the intake committed to, re-resolved from the input it holds. A table
                // retuned under a live machine can leave the held shape with no recipe at all, and
                // dereferencing the null would take the tick loop down: stall and let the line back up
                // visibly instead of eating the item. MachineReading.Work guards the same lookup.
                RecipeDef running = content.RecipeForInput(in def, machine.Carried);
                if (running == null) return;

                machine.Cooldown -= ctx.Dt;
                if (machine.Cooldown <= 0f)
                {
                    machine.Carried = ShapeType.None;
                    machine.OutputShape = running.Output;
                    machine.OutputCount += running.OutputCount;
                }
                return;
            }

            // Intake: the first delivery a recipe covers, once the pending batch is that recipe's own
            // output (a buffer holds one shape at a time) and one more cycle's output fits in the buffer.
            // The two gates live inside the scan, so a blocked delivery cannot hide a takeable one.
            if (!MachineDelivery.TryFindRecipeDelivery(ctx.Belts, cell, content, in def,
                    machine.OutputShape, machine.OutputCount,
                    out Int2 delivery, out ShapeType shape, out RecipeDef next)) return;

            ctx.Belts.TryTakeItem(delivery, out _);
            machine.Carried = shape;
            machine.Cooldown = next.Interval;
        }
    }
}
