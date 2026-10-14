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
    /// Reading from all four sides makes "expand supply" a layout answer: belts converging on one
    /// cannon keep it at its 1 shot/s ceiling, and a jam upstream silences it without touching the turret.
    /// </summary>
    internal static class MachineDelivery
    {
        /// <summary>
        /// Fixed scan order - North, East, South, West - so which of several ready items a machine takes
        /// is decided by the map, not by iteration order. The same order doubles as the round-robin
        /// order a machine pushes along, which is why it is public: every ported machine (the splitter
        /// and the decomposer) must agree on it or disagree about which way round their outlets go.
        /// </summary>
        public static readonly Dir[] Ports = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>
        /// First cell in scan order delivering a ready item into <paramref name="cell"/>.
        /// With <paramref name="requireShape"/> the item must be exactly <paramref name="wanted"/>.
        /// </summary>
        public static bool TryFindDelivery(BeltField belts, Int2 cell, ShapeType wanted, bool requireShape,
            out Int2 deliveryCell, out ShapeType shape)
        {
            deliveryCell = default;
            shape = ShapeType.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                if (requireShape && state.Item != wanted) continue;

                deliveryCell = neighbour;
                shape = state.Item;
                return true;
            }

            return false;
        }

        /// <summary>
        /// First cell delivering an item of the *wrong* shape: the delivery that jams. Separate from a
        /// good delivery because a wrong shape always jams, even while the turret cools down - the rule
        /// is about the delivery, not the shot.
        /// </summary>
        public static bool TryFindWrongDelivery(BeltField belts, Int2 cell, ShapeType ammo, out Int2 deliveryCell)
        {
            deliveryCell = default;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                if (state.Item == ammo) continue;

                deliveryCell = neighbour;
                return true;
            }

            return false;
        }

        /// <summary>
        /// An outlet: a belt on this side pointing *away* from the machine. Whether it has room is
        /// <see cref="BeltField.TrySpawnItem"/>'s question, asked again per push.
        ///
        /// Ports are read off the belts rather than stored on the machine, so a decomposer's two
        /// outlets are the same kind of thing as a splitter's four: re-pointing one belt re-wires the
        /// machine, and a machine with no belt pointing away waits instead of pushing into a dead end.
        /// </summary>
        public static bool IsOutput(BeltField belts, Int2 cell, Dir side)
            => belts.TryGet(cell + side.Offset(), out BeltState belt) && belt.Direction == side;

        /// <summary>
        /// Push <paramref name="shape"/> out through the first outlet with room, starting at the
        /// machine's rotation cursor and wrapping, then advance the cursor past the outlet used. Shared
        /// by the decomposer and the splitter so both distribute round-robin - two halves of one split
        /// leave by different belts, not queue behind one. Unfiltered: every wired outlet may take it.
        /// </summary>
        public static bool TryPushOut(BeltField belts, ref MachineState machine, Int2 cell, ShapeType shape)
            => TryPushOut(belts, ref machine, cell, shape, DirMask.All);

        /// <summary>
        /// The same push, narrowed to <paramref name="allowed"/>. A shape-routing machine (the sorter)
        /// narrows the set per item instead of re-implementing the search, so "which outlet" and "does
        /// it have room" stay one rule for every machine that pushes.
        /// </summary>
        public static bool TryPushOut(BeltField belts, ref MachineState machine, Int2 cell, ShapeType shape,
            DirMask allowed)
        {
            for (int k = 0; k < Ports.Length; k++)
            {
                int port = (machine.Rotation + k) & 3;   // Ports.Length is 4
                Dir side = Ports[port];
                if (!allowed.Has(side)) continue;
                if (!IsOutput(belts, cell, side)) continue;
                if (!belts.TrySpawnItem(cell + side.Offset(), shape)) continue;

                machine.Rotation = (port + 1) & 3;   // the next item starts past this outlet
                return true;
            }

            return false;
        }

        /// <summary>
        /// Take the first ready item delivered into <paramref name="cell"/> off its belt, in the fixed
        /// scan order every ported machine uses. False means nothing is delivering - for a machine
        /// already holding an item, simple backpressure: it waits and lets the belts upstream queue.
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
        public static bool TryTakeInput(BeltField belts, Int2 cell, out ShapeType shape)
        {
            shape = ShapeType.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: true, out Int2 neighbour, out _)) continue;

                belts.TryTakeItem(neighbour, out shape);
                return true;
            }

            return false;
        }

        /// <summary>Is there a belt on the <paramref name="side"/> of <paramref name="cell"/>, pointing
        /// into it, with an item parked on its exit edge? <paramref name="rejectJammed"/> is the
        /// difference between the two ways this gets asked: a machine that consumes or fires the item
        /// it takes ignores the jam flag - taking the item is how a line gets going again - while a
        /// machine that carries it somewhere else must not pick a jammed item up.</summary>
        private static bool TryReadyAt(BeltField belts, Int2 cell, Dir side, bool rejectJammed,
            out Int2 neighbour, out BeltState state)
        {
            neighbour = cell + side.Offset();
            if (!belts.TryGet(neighbour, out state)) return false;
            if (state.Direction != side.Opposite()) return false;   // it must point at the machine
            if (rejectJammed && state.Jammed) return false;
            return state.Item != ShapeType.None && state.Progress >= 1f;
        }

        /// <summary>
        /// The machine's two port sets, computed once so the simulation and the view read the same rule.
        ///
        /// <paramref name="inMask"/>: every side a belt points at the machine from - the sides it can be
        /// fed on, which is what makes a stub pointing inward read as an input.
        ///
        /// <paramref name="outMask"/>: every side a belt points away on, when the machine's kind has an
        /// output there. A drill's facing <em>is</em> its output (exactly one); a decomposer, cutter,
        /// splitter and sorter read their outlets off the belts around them (every side wired that way);
        /// a pipe (lands two cells ahead) and a turret (never pushes) get none.
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
                case BehaviorKind.Drill: return facing == side;
                case BehaviorKind.Converter:
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
