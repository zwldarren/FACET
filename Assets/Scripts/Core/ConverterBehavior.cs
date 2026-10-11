namespace Facet.Core
{
    /// <summary>
    /// Runs a <see cref="RecipeDef"/>: eats its input, waits out the interval, then pushes its output
    /// through whichever sides have a belt pointing away. There is no hardcoded decomposer here - the
    /// machine's definition names the recipe, so a furnace, a smelter or an assembler is another row,
    /// not another behaviour.
    ///
    /// The shipped recipe doubles what it is fed: one circle in, two halves out per 1.5 s split, which
    /// is 1.33 halves/s - more than a single belt can carry away. So a converter at full tilt is the
    /// first building that asks for a second outlet: point belts away from two of its sides and it
    /// runs at its own rate, or feed a splitter to share the halves between two cannons.
    ///
    /// The finished outputs sit in a buffer (the recipe's, two splits deep for the decomposer) and
    /// leave one per tick while an outlet has room, so a split is never lost to a blocked output - the
    /// machine waits <em>after</em> the work, not in the middle of it. Intake starts whenever a split's
    /// worth of buffer is free. The wrong-shape rule holds at this input too: anything delivered that
    /// is not this recipe's input jams the feeding belt, whether or not the machine is busy.
    /// </summary>
    internal sealed class ConverterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx)
        {
            RecipeDef recipe = ctx.Content.Recipe(ctx.Content.Machine(machine.Build).RecipeId);

            if (MachineDelivery.TryFindWrongDelivery(ctx.Belts, cell, recipe.Input, out Int2 wrong))
                ctx.Belts.TryJam(wrong);

            // Push buffered outputs out through any wired outlet. One per tick is far above what belts
            // can carry, so the belts - not this line - set the real output rate; with two outlets the
            // two halves of one split leave by different belts.
            if (machine.OutputCount > 0 &&
                MachineDelivery.TryPushOut(ctx.Belts, ref machine, cell, recipe.Output))
                machine.OutputCount--;

            if (machine.Carried == recipe.Input)
            {
                machine.Cooldown -= ctx.Dt;
                if (machine.Cooldown <= 0f)
                {
                    machine.Carried = ShapeType.None;
                    machine.OutputCount += recipe.OutputCount;
                }
                return;
            }

            // Intake only when one more cycle's output fits in the buffer, so a cycle never has
            // nowhere to go.
            if (machine.OutputCount > recipe.Buffer - recipe.OutputCount) return;

            if (MachineDelivery.TryFindDelivery(ctx.Belts, cell, recipe.Input, requireShape: true,
                    out Int2 delivery, out _))
            {
                ctx.Belts.TryTakeItem(delivery, out _);
                machine.Carried = recipe.Input;
                machine.Cooldown = recipe.Interval;
            }
        }
    }
}
