using System;

namespace Facet.Core
{
    /// <summary>
    /// The crossing piece. Eats anything delivered, carries it for its definition's interval (0.25 s),
    /// and drops it on the belt two cells ahead in its facing - jumping one tile, belt or bare ground.
    /// Every shape is accepted and nothing jams; a blocked landing holds the item at the far end.
    /// </summary>
    internal sealed class PipeBehavior : IMachineBehavior
    {
        public void Step(ref MachineState pipe, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(pipe.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, pipe.Direction, in def);

            if (pipe.Carried.IsEmpty)
            {
                // Empty pipe: swallow whatever is being delivered, from any side - but never a jammed
                // item, because this machine carries what it takes to another side, and carrying it would
                // move it past the segment the player is meant to clear. TryTakeInput asks the diet, the
                // jam flag and the take as one question, so a refusal commits nothing: scanning and then
                // taking separately is how an item ends up on the belt *and* inside the pipe at once.
                if (MachineDelivery.TryTakeInput(ctx.Belts, cell, in def, out Shape shape))
                {
                    pipe.Carried = shape;
                    pipe.Cooldown = def.Interval;
                }

                MachineClock.Waiting(ref pipe, waiting: false);   // an empty pipe is not holding anything back
                return;
            }

            pipe.Cooldown = MathF.Max(0f, pipe.Cooldown - ctx.Dt);
            if (pipe.Cooldown > 0f)
            {
                MachineClock.Waiting(ref pipe, waiting: false);   // still crossing
                return;
            }

            // Land on the belt two cells ahead. No belt or no room: the item waits at the far end - and that
            // wait is a stoppage the belts behind it cannot report, because the pipe is a machine holding an
            // item rather than a segment with nowhere to put one.
            bool landed = ctx.Belts.TrySpawnItem(MachineReading.PipeLanding(cell, pipe.Direction), pipe.Carried);
            if (landed) pipe.Carried = Shape.None;

            MachineClock.Waiting(ref pipe, waiting: !landed);
        }
    }
}
