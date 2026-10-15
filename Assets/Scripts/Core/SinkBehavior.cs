using System;

namespace Facet.Core
{
    /// <summary>
    /// The drain. Eats whatever is delivered into it, from any side, and destroys it: no outlet, no
    /// product, and no state that can hold, so a sink is never the reason a line stops.
    ///
    /// This is the ending a fork needs. A cutter makes two pieces where a layout usually wants one, and
    /// without somewhere for the surplus to go its lane fills, the machine that made it stalls, and the
    /// player's only recourse is right-clicking a jammed segment every few seconds. Deleting a shape is
    /// value-neutral by construction - money is banked at the Core and nowhere else - so the drain can be
    /// cheap and freely placeable, which is what makes it an answer rather than another problem.
    ///
    /// A jammed or dead-ended belt is eaten like any other: taking the item is how a stuck segment gets
    /// going again, the rule the turret already follows, and what makes a sink laid at the end of a stuck
    /// line clear it without a right-click.
    /// </summary>
    internal sealed class SinkBehavior : IMachineBehavior
    {
        public void Step(ref MachineState sink, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(sink.Build);

            // The whole tick. A machine with a diet runs TryJamWrongDelivery first; this one's diet is
            // everything, so there is nothing a belt could deliver that it has to turn away.
            if (!MachineDelivery.TryEat(ctx.Belts, cell, in def, out _)) return;

            // A tally, not state: it decides nothing, so it may wrap without consequence.
            sink.Swallowed++;
        }
    }
}
