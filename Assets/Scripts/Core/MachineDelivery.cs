using System;

namespace Facet.Core
{
    /// <summary>
    /// The one rule connecting belts to machines: **a machine eats from every belt cell that delivers
    /// into it, and from nothing else.**
    ///
    /// A belt delivers when it points at the machine (its direction is the step from its own cell to
    /// the machine's) and its item has reached the exit edge of its cell (progress 1) - the same
    /// boundary a belt-to-belt hand-off happens on. A belt pointing the other way is a dead end and
    /// simply queues, a visible mistake instead of a silent one.
    ///
    /// Reading from all four sides makes "expand supply" a layout answer: belts converging on one gun keep
    /// it at its ceiling, and a jam upstream silences it without touching the gun.
    ///
    /// What a machine eats is <see cref="MachineDef.Diet"/>, and this file is the only place that rule is
    /// read - intake and the jam it causes are two halves of one question, so they cannot drift apart.
    /// Every shipped machine's diet is "any shape"; a table that narrows one gets the old routing tension
    /// back for free.
    /// </summary>
    internal static class MachineDelivery
    {
        /// <summary>
        /// Fixed scan order - North, East, South, West - so which of several ready items a machine takes
        /// is decided by the map, not by iteration order. The same order doubles as the round-robin
        /// order a machine pushes along, which is why it is public: every ported machine (the splitter and
        /// the sorter) must agree on it or disagree about which way round their outlets go.
        /// </summary>
        public static readonly Dir[] Ports = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>The diet rule, said once: may this machine take that item? An empty item is not a
        /// delivery, so it is nobody's food.</summary>
        public static bool Eats(in MachineDef def, Shape shape) => def.Diet.Matches(shape);

        /// <summary>
        /// First cell in scan order delivering a ready item this machine eats, with what it is. The intake
        /// half of the diet rule.
        /// </summary>
        public static bool TryFindDietDelivery(BeltField belts, Int2 cell, in MachineDef def,
            out Int2 deliveryCell, out Shape shape)
        {
            deliveryCell = default;
            shape = Shape.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                if (!Eats(in def, state.Item)) continue;

                deliveryCell = neighbour;
                shape = state.Item;
                return true;
            }

            return false;
        }

        /// <summary>
        /// And the jam half: the first cell delivering an item the machine does not eat. A delivery the
        /// machine refuses blocks the segment it landed on and stays blocked, which is what makes a
        /// narrow diet a layout constraint rather than a silent loss.
        /// </summary>
        public static bool TryFindWrongDelivery(BeltField belts, Int2 cell, in MachineDef def,
            out Int2 deliveryCell)
        {
            deliveryCell = default;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                if (Eats(in def, state.Item)) continue;

                deliveryCell = neighbour;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The intake preamble, said once: find the first delivery the diet refuses and jam the segment it
        /// landed on - <see cref="TryFindWrongDelivery"/>'s answer put to work. Every behaviour that takes
        /// from belts runs this before anything else, which is why it is one method: intake and the jam it
        /// causes are two halves of one question, and a copy-pasted preamble is how they drift apart. True
        /// when something was jammed - the turret is the one caller that acts on it, disarming instead of
        /// waiting out a delivery it will never eat.
        /// </summary>
        public static bool TryJamWrongDelivery(BeltField belts, Int2 cell, in MachineDef def)
        {
            if (!TryFindWrongDelivery(belts, cell, in def, out Int2 wrong)) return false;

            belts.TryJam(wrong);
            return true;
        }

        /// <summary>
        /// An outlet: a belt on this side pointing *away* from the machine. Whether it has room is
        /// <see cref="BeltField.TrySpawnItem"/>'s question, asked again per push.
        ///
        /// Ports are read off the belts rather than stored on the machine, so a stacker's two outlets are
        /// the same kind of thing as a splitter's four: re-pointing one belt re-wires the machine, and a
        /// machine with no belt pointing away waits instead of pushing into a dead end.
        /// </summary>
        public static bool IsOutput(BeltField belts, Int2 cell, Dir side)
            => belts.TryGet(cell + side.Offset(), out BeltState belt) && belt.Direction == side;

        /// <summary>
        /// Push <paramref name="shape"/> out through the first outlet with room, starting at the
        /// machine's rotation cursor and wrapping, then advance the cursor past the outlet used. Shared
        /// by the stacker and the splitter so both distribute round-robin - a stacker's two halves of one
        /// mixed round leave by different belts rather than queue behind one. Unfiltered: every wired
        /// outlet may take it.
        /// </summary>
        public static bool TryPushOut(BeltField belts, ref MachineState machine, Int2 cell, Shape shape)
            => TryPushOut(belts, ref machine, cell, shape, DirMask.All);

        /// <summary>
        /// The same push, narrowed to <paramref name="allowed"/>. A shape-routing machine (the sorter)
        /// narrows the set per item instead of re-implementing the search, so "which outlet" and "does
        /// it have room" stay one rule for every machine that pushes.
        /// </summary>
        public static bool TryPushOut(BeltField belts, ref MachineState machine, Int2 cell, Shape shape,
            DirMask allowed)
        {
            for (int k = 0; k < Ports.Length; k++)
            {
                int port = (machine.Rotation + k) & 3;   // Ports.Length is 4
                Dir side = Ports[port];
                if (!allowed.Has(side)) continue;
                if (!TryPushOutSide(belts, cell, side, shape)) continue;

                machine.Rotation = (port + 1) & 3;   // the next item starts past this outlet
                return true;
            }

            return false;
        }

        /// <summary>
        /// Push one item out through exactly one named side. The cutter's rule: each piece of a cut has
        /// its own outlet - the facing side and the one opposite it - so the machine does not choose an
        /// outlet per item, the piece does. False when there is no belt pointing away there, or no room
        /// on it; the caller keeps the piece buffered and tries again next tick.
        /// </summary>
        public static bool TryPushOutSide(BeltField belts, Int2 cell, Dir side, Shape shape)
            => !shape.IsEmpty && IsOutput(belts, cell, side) && belts.TrySpawnItem(cell + side.Offset(), shape);

        /// <summary>
        /// Take the first ready item delivered into <paramref name="cell"/> off its belt, in the fixed
        /// scan order every ported machine uses, skipping anything the machine's diet refuses - an item a
        /// carrying hub may not move is an item it must leave for the right-click that clears it. False
        /// means nothing is delivering: for a machine already holding an item, simple backpressure - it
        /// waits and lets the belts upstream queue.
        ///
        /// The intake mirror of <see cref="TryPushOut"/>, and the one rule for every machine that picks
        /// up and holds what it takes (the splitter hub and the sorter), put here so "an outlet is a
        /// belt pointing away" and "an intake is a belt pointing in" cannot drift apart.
        ///
        /// A jammed belt is skipped: these machines carry what they take to another side, so taking a
        /// jammed item would move it past the segment the player is meant to clear and hide the mistake.
        /// A machine that consumes or fires what it takes asks the other question - see
        /// <see cref="TryReadyAt"/>.
        /// </summary>
        public static bool TryTakeInput(BeltField belts, Int2 cell, in MachineDef def, out Shape shape)
        {
            shape = Shape.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: true, out Int2 neighbour, out BeltState state)) continue;
                if (!Eats(in def, state.Item)) continue;

                belts.TryTakeItem(neighbour, out shape);
                return true;
            }

            return false;
        }

        /// <summary>Is there a belt on the <paramref name="side"/> of <paramref name="cell"/>, pointing
        /// into it, with an item parked on its exit edge? <paramref name="rejectJammed"/> is the
        /// difference between the two ways this gets asked: a machine that consumes or fires the item
        /// it takes ignores the jam flag - taking the item is how a line gets going again - while a
        /// machine that carries it somewhere else must not pick a jammed item up. Public so a behaviour
        /// with a rule between the two packaged scans - the stacker's disjointness test - can ask it
        /// directly.</summary>
        public static bool TryReadyAt(BeltField belts, Int2 cell, Dir side, bool rejectJammed,
            out Int2 neighbour, out BeltState state)
        {
            neighbour = cell + side.Offset();
            if (!belts.TryGet(neighbour, out state)) return false;
            if (state.Direction != side.Opposite()) return false;   // it must point at the machine
            if (rejectJammed && state.Jammed) return false;
            return !state.Item.IsEmpty && state.Progress >= 1f;
        }

        /// <summary>
        /// The machine's two port sets, computed once so the simulation and the view read the same rule.
        ///
        /// <paramref name="inMask"/>: every side a belt points at the machine from - the sides it can be
        /// fed on, which is what makes a stub pointing inward read as an input.
        ///
        /// <paramref name="outMask"/>: every side a belt points away on, when the machine's kind has an
        /// output there. A drill's facing <em>is</em> its output (exactly one); a cutter's are the side it
        /// faces and the one opposite it (the two pieces of its cut); a rotator's is the side it faces; a
        /// stacker, splitter and sorter read their outlets off the belts around them (every side wired that
        /// way); a pipe (lands two cells ahead) and a turret (never pushes) get none.
        ///
        /// Which a machine is comes from its definition's <see cref="BehaviorKind"/>, so a new machine of
        /// an existing kind docks correctly without being added to a list here.
        /// </summary>
        public static void PortMasks(BeltField belts, ContentDatabase content, Int2 cell,
            in MachineState machine, out DirMask inMask, out DirMask outMask)
        {
            inMask = DirMask.None;
            outMask = DirMask.None;

            BehaviorKind behavior = content.Machine(machine.Build).Behavior;

            for (int i = 0; i < Ports.Length; i++)
            {
                Dir side = Ports[i];
                if (!belts.TryGet(cell + side.Offset(), out BeltState belt)) continue;

                if (belt.Direction == side.Opposite()) inMask = inMask.With(side);
                else if (belt.Direction == side && HasOutputOn(behavior, machine.Direction, side))
                    outMask = outMask.With(side);
            }
        }

        /// <summary>
        /// Does a machine with this behaviour push items out on <paramref name="side"/>? Every behaviour
        /// answers for itself and a new one must too: there is no default here, because the wrong answer
        /// as a fallback is a machine with no output port and no complaint.
        /// </summary>
        private static bool HasOutputOn(BehaviorKind behavior, Dir facing, Dir side)
        {
            switch (behavior)
            {
                case BehaviorKind.Drill:
                case BehaviorKind.Rotator: return facing == side;

                // Both pieces leave at once, one per side of the cut: the facing side and the one
                // opposite it. The other two sides are not this machine's outlets at all, so a belt
                // laid there reads as an input (which is what it is).
                case BehaviorKind.Cutter: return facing == side || facing.Opposite() == side;

                case BehaviorKind.Stacker:
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter: return true;

                case BehaviorKind.None:
                case BehaviorKind.Pipe:
                // A pipe lands two cells ahead, never on an adjoining belt, and a turret only eats.
                case BehaviorKind.Turret: return false;

                default:
                    throw new ArgumentOutOfRangeException(nameof(behavior), behavior,
                        "a new behaviour must declare here whether it pushes out on a wired side");
            }
        }
    }
}
