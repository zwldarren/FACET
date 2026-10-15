using System;

namespace Facet.Core
{
    /// <summary>
    /// The orientation fix. It eats any shape, waits out its interval (0.75 s) and turns the item a
    /// quarter clockwise, out the side it faces. There is exactly one direction of turn, on purpose:
    /// counter-clockwise is three of these in series, which is a logistics puzzle in itself rather than a
    /// second machine's worth of content.
    ///
    /// Its second job is repair. The briefing asks for a top half-circle and the line is running bottom
    /// halves - one turn fixes it, and two turns would be the piece the briefing did not ask for, because
    /// a half turn is not the identity here.
    ///
    /// <b>The order of the two steps is the whole machine.</b> An item is turned <em>first</em> and offered
    /// to the outlet only afterwards, because the other way round - push, then turn - hands the outlet the
    /// item exactly as it arrived and leaves the turn to happen only while the outlet is blocked. Which of
    /// the two states the held item is in is carried by the cooldown's sign and by nothing else, so the two
    /// cannot drift apart: above zero it is still turning, at zero it is turned and waiting for room. That
    /// is also why the cooldown is not reset after a turn - a blocked outlet retries the push every tick
    /// instead of spinning the item a quarter turn per tick.
    /// </summary>
    internal sealed class RotatorBehavior : IMachineBehavior
    {
        public void Step(ref MachineState rotator, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(rotator.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, rotator.Direction, in def);

            if (!rotator.Carried.IsEmpty)
            {
                if (rotator.Cooldown > 0f)
                {
                    rotator.Cooldown = MathF.Max(0f, rotator.Cooldown - ctx.Dt);
                    if (rotator.Cooldown > 0f) return;   // still turning

                    rotator.Carried = rotator.Carried.RotatedCW();
                }

                // Turned, and waiting only for room. It leaves on the tick its turn completes when the outlet
                // is free; when it is not, this is where the machine waits, and the item is not turned again.
                if (MachineDelivery.TryPushOutSide(ctx.Belts, cell, rotator.Direction, rotator.Carried))
                    rotator.Carried = Shape.None;
                return;
            }

            if (!MachineDelivery.TryFindDietDelivery(ctx.Belts, cell, rotator.Direction, in def,
                    out Int2 delivery, out _)) return;

            // Nothing enters the machine unless it really left the belt: the scan answers "something is
            // being delivered", the take answers "and it is mine now", and a refused take - a jammed
            // segment - leaves the item where it was rather than copying it in.
            if (!ctx.Belts.TryTakeItem(delivery, out Shape taken)) return;

            rotator.Carried = taken;

            // At least one tick, so "still turning" can never be skipped: the interval is data, and data does
            // not get to turn the rotator into a pass-through by asking for zero.
            rotator.Cooldown = MathF.Max(def.Interval, SimConfig.TickDt);
        }
    }
}
