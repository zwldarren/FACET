namespace Facet.Core
{
    /// <summary>
    /// Runs a two-input <see cref="RecipeDef"/>: holds one of each input (<see cref="MachineState.Carried"/>
    /// and <see cref="MachineState.Carried2"/>), waits out the interval once both are held, and pushes the
    /// fused output through any wired outlet. Which recipes is the definition's business, as the
    /// converter's: one named recipe, or - naming none - every two-input recipe the table holds. The
    /// first item taken commits the recipe (table order decides between recipes that share an input); the
    /// second must complete it. A delivery no recipe accepts jams the feeding belt, whether or not the
    /// machine is busy; a duplicate of the input already held is not <em>wrong</em> - it waits on its
    /// belt, a visible stall instead of a jam.
    /// </summary>
    internal sealed class AssemblerBehavior : IMachineBehavior
    {
        public void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx)
        {
            ContentDatabase content = ctx.Content;
            MachineDef def = content.Machine(machine.Build);

            if (MachineDelivery.TryFindWrongDelivery(ctx.Belts, cell, content, in def, out Int2 wrong))
                ctx.Belts.TryJam(wrong);

            if (machine.OutputCount > 0 &&
                MachineDelivery.TryPushOut(ctx.Belts, ref machine, cell, machine.OutputShape))
                machine.OutputCount--;

            // Both inputs held: the fuse runs. Intake below only ever takes what completes the recipe
            // the first item committed to, so the pair held always resolves to that one.
            if (machine.Carried != ShapeType.None && machine.Carried2 != ShapeType.None)
            {
                // The committed recipe, not a fresh lookup by the held pair: a retuned table can drop
                // either recipe under a live machine, and dereferencing the null would take the tick loop
                // down. Stalling keeps the held items visible instead of eating them.
                RecipeDef running = content.RecipeOrNull(machine.CommittedRecipe);
                if (running == null) return;

                machine.Cooldown -= ctx.Dt;
                if (machine.Cooldown <= 0f)
                {
                    machine.Carried = ShapeType.None;
                    machine.Carried2 = ShapeType.None;
                    machine.CommittedRecipe = ContentId.None;
                    machine.OutputShape = running.Output;
                    machine.OutputCount += running.OutputCount;
                }
                return;
            }

            // What a second delivery must complete: the recipe the first input committed to. A table
            // retuned under a live machine can drop it, and then nothing can complete - stall visibly.
            RecipeDef committed = machine.Carried == ShapeType.None
                ? null
                : content.RecipeOrNull(machine.CommittedRecipe);
            if (machine.Carried != ShapeType.None && committed == null) return;

            // Intake, in the ports' scan order: the first delivery that commits to a recipe - the held
            // input's committed pair, or any two-input recipe's input while the machine is empty. A buffer
            // holds one shape at a time, so a different recipe's cycle waits for the pending batch to
            // drain. A delivery that does not fit is skipped, not a stop: a later port may hold one that
            // does, and aborting the scan would starve it behind the head of the line.
            for (int i = 0; i < MachineDelivery.Ports.Length; i++)
            {
                if (!MachineDelivery.TryReadyAt(ctx.Belts, cell, MachineDelivery.Ports[i],
                        rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;

                RecipeDef candidate = machine.Carried == ShapeType.None
                    ? content.RecipeForInput(in def, state.Item)
                    : committed.Accepts(state.Item) && state.Item != machine.Carried ? committed : null;
                if (candidate == null) continue;
                if (machine.OutputCount > 0 && machine.OutputShape != candidate.Output) continue;
                if (machine.OutputCount > candidate.Buffer - candidate.OutputCount) continue;

                ctx.Belts.TryTakeItem(neighbour, out _);
                if (machine.Carried == ShapeType.None)
                {
                    machine.Carried = state.Item;
                    machine.CommittedRecipe = candidate.Id;
                }
                else
                {
                    machine.Carried2 = state.Item;
                    machine.Cooldown = candidate.Interval;
                }
                return;
            }
        }
    }
}
