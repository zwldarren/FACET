namespace Facet.Core
{
    /// <summary>
    /// The first operator, and the one the campaign teaches first. It eats any shape, waits out its
    /// interval (1.5 s), and emits <em>both</em> pieces of the cut at once: the piece on the side the
    /// machine faces leaves by the facing outlet, the other by the outlet opposite it. Which line the cut
    /// runs along is the facing's own business - north and south cut across, east and west cut down the
    /// middle - so a 180-degree turn changes which outlet each piece uses and not which two pieces come
    /// out, exactly as turning a knife half a turn does not change the cut.
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
            MachineDef def = ctx.Content.Machine(cutter.Build);

            MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, in def);

            Dir front = cutter.Direction;
            Dir back = front.Opposite();

            // Each piece leaves by its own outlet. One per tick is far above belt capacity, so the belts -
            // not this line - set the output rate.
            if (cutter.OutputCount > 0 &&
                MachineDelivery.TryPushOutSide(ctx.Belts, cell, front, cutter.OutputShape))
                cutter.OutputCount--;

            if (cutter.OutputCount2 > 0 &&
                MachineDelivery.TryPushOutSide(ctx.Belts, cell, back, cutter.OutputShape2))
                cutter.OutputCount2--;

            if (!cutter.Carried.IsEmpty)
            {
                // Mid-split. The pieces are recomputed from the held item, so the machine reads nothing
                // back off the definition - a cutter cuts whatever it was handed.
                (Shape frontPiece, Shape backPiece) = Pieces(cutter.Carried, front);
                bool room = (frontPiece.IsEmpty || cutter.OutputCount < SlotBuffer)
                            && (backPiece.IsEmpty || cutter.OutputCount2 < SlotBuffer);
                if (!room) return;   // both pieces leave at once, so the split waits for both slots

                cutter.Cooldown -= ctx.Dt;
                if (cutter.Cooldown > 0f) return;

                cutter.Carried = Shape.None;
                if (!frontPiece.IsEmpty)
                {
                    cutter.OutputShape = frontPiece;
                    cutter.OutputCount++;
                }

                if (!backPiece.IsEmpty)
                {
                    cutter.OutputShape2 = backPiece;
                    cutter.OutputCount2++;
                }

                return;
            }

            // Intake: the first delivery the diet accepts whose two pieces this machine can actually
            // queue. A slot counts pieces of one shape, so a piece that is not what the slot already
            // holds has to wait for it to drain - the queue is never rewritten to a different item,
            // because three halves the player built are three halves and not whatever was cut last. The
            // wait is a stall, like the stacker's: the machine stops, visibly and safely.
            if (cutter.OutputCount >= SlotBuffer || cutter.OutputCount2 >= SlotBuffer) return;
            if (!MachineDelivery.TryFindDietDelivery(ctx.Belts, cell, in def,
                    out Int2 delivery, out Shape shape)) return;

            (Shape arriving, Shape other) = Pieces(shape, front);
            if (!Fits(cutter.OutputCount, cutter.OutputShape, arriving)) return;
            if (!Fits(cutter.OutputCount2, cutter.OutputShape2, other)) return;

            // The take can still refuse a jammed segment: nothing is committed unless the item really
            // left the belt, so a refused take cannot mint a copy of it inside the machine.
            if (!ctx.Belts.TryTakeItem(delivery, out Shape taken)) return;

            cutter.Carried = taken;
            cutter.Cooldown = def.Interval;
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
        /// The two pieces of cutting <paramref name="shape"/> with the machine facing
        /// <paramref name="facing"/>, in outlet order: the facing side first. A horizontal cut (facing
        /// north or south) yields the top and bottom pieces; a vertical one yields left and right. The
        /// piece on the side the machine faces is the one that leaves by that side - so facing north
        /// sends the top half out of the north, and facing south sends the bottom half out of the south.
        /// </summary>
        public static (Shape front, Shape back) Pieces(Shape shape, Dir facing)
        {
            bool vertical = facing == Dir.East || facing == Dir.West;
            (Shape a, Shape b) = Shape.Cut(shape, vertical);

            // Cut's first piece is the top for a horizontal cut and the left for a vertical one, and the
            // facing side is the top when facing north and the left when facing west.
            bool facingHoldsA = facing == Dir.North || facing == Dir.West;
            return facingHoldsA ? (a, b) : (b, a);
        }
    }
}
