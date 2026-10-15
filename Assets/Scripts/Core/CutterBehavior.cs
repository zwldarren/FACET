namespace Facet.Core
{
    /// <summary>
    /// The first operator, and the one the campaign teaches first. It eats any shape, waits out its
    /// interval (1.5 s), and emits <em>both</em> pieces of the cut at once - out of the front of the two
    /// tiles it stands on (see <see cref="MachineFootprint"/>): the piece on the machine's left runs
    /// straight on through the tile the belt fed, and the piece on its right leaves one lane over.
    ///
    /// <b>A fork, not a turn.</b> The belt feeds the cutter's back, a belt on each front carries a piece
    /// away, and the two of them head off the way the line was already going. That is the whole reason the
    /// cutter is two tiles wide: one tile of lane is what the displaced piece needs, and with it a player
    /// can drop a cutter <em>into</em> a run instead of building a corner for it. The cut line runs along the
    /// flow - north and south cut left from right, east and west cut top from bottom - so a quarter turn
    /// changes which pieces come out as well as which way they leave. A half turn only swaps which lane is
    /// which, exactly as turning a fork half a turn does not change the fork.
    ///
    /// The two pieces buffer separately (<see cref="MachineState.OutputCount"/> and
    /// <see cref="MachineState.OutputCount2"/>): routing one piece onward while the other queues for a
    /// busy belt is the puzzle, and losing a piece to a blocked neighbour would make it a race instead.
    /// A slot counts pieces of <em>one</em> shape - one count cannot describe a queue of two different
    /// items - so an item whose pieces are not what the slots already hold waits on its belt for them to
    /// drain rather than rewriting what is queued behind it.
    /// Cutting a quarter yields the quarter plus nothing, so "an item can be cut at most twice" needs no
    /// rule of its own.
    /// </summary>
    internal sealed class CutterBehavior : IMachineBehavior
    {
        /// <summary>How many pieces one outlet may hold while its belt is blocked. Four is a couple of
        /// cycles' worth: enough that a momentary blockage does not stall the line, small enough that a
        /// line nobody drains backs up where the player can see it.</summary>
        public const int SlotBuffer = 4;

        public void Step(ref MachineState cutter, Int2 cell, in MachineTickContext ctx)
        {
            // The machine answers "am I waiting on an outlet" at every one of its exits, so the counter the
            // HUD reads cannot drift from the tick's own decision to stop.
            MachineClock.Waiting(ref cutter, Cut(ref cutter, cell, in ctx));
        }

        /// <summary>
        /// One tick of the fork, reporting <b>whether it is waiting on an outlet</b> - the state
        /// <see cref="MachineState.StallTicks"/> counts. Three ways it can be waiting, and they are the three
        /// the machine's own comments below name: a lane at its buffer, a split whose two pieces have nowhere
        /// to go yet, and an item it cannot take because a slot still holds something else. Cooldown and
        /// intake are not waiting: the machine is working, or nothing is coming.
        /// </summary>
        private static bool Cut(ref MachineState cutter, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(cutter.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, cutter.Direction, in def);

            Dir flow = cutter.Direction;
            Int2 lane = cell + MachineFootprint.LaneOf(flow).Offset();

            // Each piece leaves by its own lane, both out of the front. One per tick is far above belt
            // capacity, so the belts - not this line - set the output rate.
            if (cutter.OutputCount > 0 &&
                MachineDelivery.TryPushOutSide(ctx.Belts, cell, flow, cutter.OutputShape))
                cutter.OutputCount--;

            if (cutter.OutputCount2 > 0 &&
                MachineDelivery.TryPushOutSide(ctx.Belts, lane, flow, cutter.OutputShape2))
                cutter.OutputCount2--;

            if (!cutter.Carried.IsEmpty)
            {
                // Mid-split. The pieces are recomputed from the held item, so the machine reads nothing
                // back off the definition - a cutter cuts whatever it was handed.
                (Shape straight, Shape displaced) = Pieces(cutter.Carried, flow);
                bool room = (straight.IsEmpty || cutter.OutputCount < SlotBuffer)
                            && (displaced.IsEmpty || cutter.OutputCount2 < SlotBuffer);
                if (!room) return true;   // both pieces leave at once, so the split waits for both slots

                cutter.Cooldown -= ctx.Dt;
                if (cutter.Cooldown > 0f) return false;   // still cutting

                cutter.Carried = Shape.None;
                if (!straight.IsEmpty)
                {
                    cutter.OutputShape = straight;
                    cutter.OutputCount++;
                }

                if (!displaced.IsEmpty)
                {
                    cutter.OutputShape2 = displaced;
                    cutter.OutputCount2++;
                }

                return false;
            }

            // Intake: the first delivery the diet accepts whose two pieces this machine can actually
            // queue. A slot counts pieces of one shape, so a piece that is not what the slot already
            // holds has to wait for it to drain - the queue is never rewritten to a different item,
            // because three halves the player built are three halves and not whatever was cut last. The
            // wait is a stall, like the stacker's: the machine stops, visibly and safely. Both of the
            // machine's tiles are fed on: a belt laid into the back of either one delivers.
            if (cutter.OutputCount >= SlotBuffer || cutter.OutputCount2 >= SlotBuffer) return true;
            if (!MachineDelivery.TryFindDietDelivery(ctx.Belts, cell, flow, in def,
                    out Int2 delivery, out Shape shape)) return false;

            (Shape arriving, Shape other) = Pieces(shape, flow);
            if (!Fits(cutter.OutputCount, cutter.OutputShape, arriving)) return true;
            if (!Fits(cutter.OutputCount2, cutter.OutputShape2, other)) return true;

            // The take can still refuse a jammed segment: nothing is committed unless the item really
            // left the belt, so a refused take cannot mint a copy of it inside the machine.
            if (!ctx.Belts.TryTakeItem(delivery, out Shape taken)) return false;

            cutter.Carried = taken;
            cutter.Cooldown = def.Interval;
            return false;
        }

        /// <summary>
        /// May a slot already holding <paramref name="count"/> pieces of <paramref name="held"/> take
        /// <paramref name="piece"/>? An empty piece touches no slot, so it always may; an empty slot takes
        /// any piece; otherwise the piece must be what the slot already holds, because one count cannot
        /// describe a queue of two different items.
        /// </summary>
        private static bool Fits(int count, Shape held, Shape piece)
            => piece.IsEmpty || count <= 0 || held == piece;

        /// <summary>
        /// The two pieces of cutting <paramref name="shape"/> with the machine flowing
        /// <paramref name="facing"/>, in lane order: the one that keeps the tile the belt fed first, then
        /// the one displaced a lane over.
        ///
        /// The cut runs <em>along</em> the flow, so the two pieces are the halves either side of it: flowing
        /// north or south they are left and right, flowing east or west top and bottom. The piece on the
        /// machine's left-hand side keeps its lane - the left of east is north, of north is west - and the
        /// piece on its right is the one that leaves one lane over. <see cref="Shape.Cut"/> hands back the
        /// top for a horizontal cut and the left for a vertical one, which is the first piece when the
        /// machine flows north or east.
        /// </summary>
        public static (Shape straight, Shape displaced) Pieces(Shape shape, Dir facing)
        {
            bool vertical = facing == Dir.North || facing == Dir.South;
            (Shape a, Shape b) = Shape.Cut(shape, vertical);
            bool facingHoldsA = facing == Dir.North || facing == Dir.East;
            return facingHoldsA ? (a, b) : (b, a);
        }
    }
}
