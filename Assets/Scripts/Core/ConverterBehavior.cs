namespace Facet.Core
{
    /// <summary>
    /// Runs a <see cref="RecipeDef"/>: eats its input, waits out the interval, then pushes output through
    /// the sides with a belt pointing away. The machine's definition names the recipe, so a furnace or
    /// assembler is another row, not another behaviour. Outputs buffer (two splits deep for the shipped
    /// decomposer) and leave one per tick while an outlet has room, so a split is never lost to a blocked
    /// output - and intake starts only when a cycle's worth of buffer is free. A wrong-shaped delivery
    /// jams the feeding belt whether or not the machine is busy.
    /// </summary>
    internal sealed class ConverterBehavior : IMachineBehavior
    {
        public void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx)
        {
            RecipeDef recipe = ctx.Content.Recipe(ctx.Content.Machine(machine.Build).RecipeId);

            if (MachineDelivery.TryFindWrongDelivery(ctx.Belts, cell, recipe.Input, out Int2 wrong))
                ctx.Belts.TryJam(wrong);

            // Push buffered outputs out any wired outlet. One per tick is far above belt capacity, so
            // the belts - not this line - set the output rate; two outlets split one batch across belts.
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

            // Intake only when one more cycle's output fits in the buffer.
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
