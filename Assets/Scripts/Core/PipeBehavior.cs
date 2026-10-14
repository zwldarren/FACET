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
            if (pipe.Carried == ShapeType.None)
            {
                // Empty pipe: swallow whatever is being delivered, from any side.
                if (MachineDelivery.TryFindDelivery(ctx.Belts, cell, ShapeType.None, requireShape: false,
                        out Int2 feed, out ShapeType shape))
                {
                    ctx.Belts.TryTakeItem(feed, out _);
                    pipe.Carried = shape;
                    pipe.Cooldown = ctx.Content.Machine(pipe.Build).Interval;
                }
                return;
            }

            pipe.Cooldown = MathF.Max(0f, pipe.Cooldown - ctx.Dt);
            if (pipe.Cooldown > 0f) return;   // still crossing

            // Land on the belt two cells ahead. No belt or no room: the item waits at the far end.
            if (ctx.Belts.TrySpawnItem(MachineReading.PipeLanding(cell, pipe.Direction), pipe.Carried))
                pipe.Carried = ShapeType.None;
        }
    }
}
