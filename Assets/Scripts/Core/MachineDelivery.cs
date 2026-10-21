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
        ///
        /// A machine that stands on two tiles is fed on both: the walk goes over the tiles
        /// <see cref="MachineFootprint"/> names, in that order, and over each one's four sides in the fixed
        /// scan order - so which of several ready items a machine takes is decided by the map and the
        /// machine's own footprint, never by iteration order.
        /// </summary>
        public static bool TryFindDietDelivery(BeltField belts, Int2 cell, Dir facing, in MachineDef def,
            out Int2 deliveryCell, out Shape shape)
            => TryFindDelivery(belts, cell, facing, in def, ready: true, out deliveryCell, out shape);

        /// <summary>
        /// The same walk asking the other half of the question: <b>what is on its way in</b>, ready or not.
        ///
        /// The tick may only take a delivery that has reached its exit edge, which is a one-tick window per
        /// item; a card or a ghost asking "what would this machine do with what is coming" wants the item
        /// still travelling down the lane, which is what a lane holds almost all of the time. Same tiles, same
        /// port order, same diet - the readiness gate is the only difference, so the two answers cannot
        /// disagree about <em>which</em> delivery the machine is about to be handed.
        ///
        /// A jammed segment is skipped, unlike <see cref="TryFindDietDelivery"/>'s take: an item that has been
        /// refused is not on its way in, and a preview of the pieces it would become would be a promise the
        /// machine has already declined to keep.
        /// </summary>
        public static bool TryFindIncoming(BeltField belts, Int2 cell, Dir facing, in MachineDef def,
            out Int2 deliveryCell, out Shape shape)
            => TryFindDelivery(belts, cell, facing, in def, ready: false, out deliveryCell, out shape);

        private static bool TryFindDelivery(BeltField belts, Int2 cell, Dir facing, in MachineDef def,
            bool ready, out Int2 deliveryCell, out Shape shape)
        {
            deliveryCell = default;
            shape = Shape.None;

            for (int tile = 0; tile < MachineFootprint.Tiles(def.Behavior); tile++)
            {
                if (!TryTile(def.Behavior, cell, facing, tile, out Int2 at)) continue;

                for (int i = 0; i < Ports.Length; i++)
                {
                    // The gates are the caller's, and they are the only difference between the two walks: the
                    // tick may take what has arrived (and takes a jammed item, unsticking the line it was
                    // refused on), the preview wants what is still travelling (and a refused item is not on
                    // its way in).
                    bool delivered = ready
                        ? TryReadyAt(belts, at, Ports[i], rejectJammed: false, out Int2 neighbour,
                            out BeltState state)
                        : TryIncomingAt(belts, at, Ports[i], rejectJammed: true, out neighbour, out state);
                    if (!delivered) continue;
                    if (!Eats(in def, state.Item)) continue;

                    deliveryCell = neighbour;
                    shape = state.Item;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// And the jam half: the first cell delivering an item the machine does not eat. A delivery the
        /// machine refuses blocks the segment it landed on and stays blocked, which is what makes a
        /// narrow diet a layout constraint rather than a silent loss. Same walk as
        /// <see cref="TryFindDietDelivery"/>.
        /// </summary>
        public static bool TryFindWrongDelivery(BeltField belts, Int2 cell, Dir facing, in MachineDef def,
            out Int2 deliveryCell)
        {
            deliveryCell = default;

            for (int tile = 0; tile < MachineFootprint.Tiles(def.Behavior); tile++)
            {
                if (!TryTile(def.Behavior, cell, facing, tile, out Int2 at)) continue;

                for (int i = 0; i < Ports.Length; i++)
                {
                    if (!TryReadyAt(belts, at, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
                    if (Eats(in def, state.Item)) continue;

                    deliveryCell = neighbour;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The tiles a machine stands on, by index, for the two walks above: index 0 is the tile
        /// its state lives on, and 1 is the second of a two-tile machine - false when it has none, which is
        /// every machine but the cutter.</summary>
        private static bool TryTile(BehaviorKind behavior, Int2 cell, Dir facing, int index, out Int2 tile)
        {
            if (index == 0)
            {
                tile = cell;
                return true;
            }

            return MachineFootprint.TrySecond(behavior, cell, facing, out tile);
        }

        /// <summary>
        /// The intake preamble, said once: find the first delivery the diet refuses and jam the segment it
        /// landed on - <see cref="TryFindWrongDelivery"/>'s answer put to work. Every behaviour that takes
        /// from belts runs this before anything else, which is why it is one method: intake and the jam it
        /// causes are two halves of one question, and a copy-pasted preamble is how they drift apart. True
        /// when something was jammed - the turret is the one caller that acts on it, disarming instead of
        /// waiting out a delivery it will never eat.
        /// </summary>
        public static bool TryJamWrongDelivery(BeltField belts, Int2 cell, Dir facing, in MachineDef def)
        {
            if (!TryFindWrongDelivery(belts, cell, facing, in def, out Int2 wrong)) return false;

            belts.TryJam(wrong);
            return true;
        }

        /// <summary>
        /// An outlet: a belt on this side that does not point back at the machine - the same statement as
        /// "not an input" (<see cref="TryIncomingAt"/>), so the two sets stay disjoint and no belt is both.
        /// The belt takes the delivery in from the machine-facing edge and sends it out along its own
        /// direction, turning inside the one cell if it has to - which is what lets a machine with a single
        /// free cell beside rock, water or a wall still get its product out. Whether it has room is
        /// <see cref="BeltField.TrySpawnItem"/>'s question, asked again per push.
        ///
        /// Ports are read off the belts rather than stored on the machine, so a stacker's two outlets are
        /// the same kind of thing as a splitter's four: re-pointing one belt re-wires the machine, and a
        /// machine with no outlet belt waits instead of pushing into a dead end.
        /// </summary>
        public static bool IsOutput(BeltField belts, Int2 cell, Dir side)
            => belts.TryGet(cell + side.Offset(), out BeltState belt) && IsOutletDirection(belt.Direction, side);

        /// <summary>The outlet rule on its own: a belt on <paramref name="side"/> of a machine pointing
        /// <paramref name="beltDirection"/> carries the machine's product unless it points straight back in.
        /// One statement, read by the push, the port masks and the drill alike.</summary>
        public static bool IsOutletDirection(Dir beltDirection, Dir side) => beltDirection != side.Opposite();

        /// <summary>
        /// Is there an outlet belt on any side in <paramref name="allowed"/> at all - <b>room or no room</b>?
        /// The question a machine holding something asks when it has to tell two waits apart: a machine
        /// with <em>nowhere</em> to hand its piece on is stopped (the mis-wiring the HUD counts), while a
        /// machine whose outlet is merely full is backed up (the line is loaded as far as it goes, which is
        /// what a factory waiting out an intermission looks like).
        ///
        /// A full outlet is not worth counting because it is never the whole story: the belt behind it is
        /// either jammed or blocked (both counted on their own) or queueing behind a consumer that is full
        /// by design - a loaded gun, and every line feeding one.
        /// </summary>
        public static bool HasOutlet(BeltField belts, Int2 cell, DirMask allowed)
        {
            for (int i = 0; i < Ports.Length; i++)
                if (allowed.Has(Ports[i]) && IsOutput(belts, cell, Ports[i])) return true;

            return false;
        }

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
        /// Push one item out through exactly one named side. The cutter's rule: each piece of a cut leaves
        /// by the front of its own tile - the one the belt fed, and the one a lane over - so the machine does
        /// not choose an outlet per item, the piece does. False when there is no outlet belt there, or
        /// no room on it; the caller keeps the piece buffered and tries again next tick.
        /// </summary>
        public static bool TryPushOutSide(BeltField belts, Int2 cell, Dir side, Shape shape)
            => !shape.IsEmpty && IsOutput(belts, cell, side) && belts.TrySpawnItem(cell + side.Offset(), shape, side);

        /// <summary>
        /// Take the first ready item delivered into <paramref name="cell"/> off its belt, in the fixed
        /// scan order every ported machine uses, skipping anything the machine's diet refuses - an item a
        /// carrying hub may not move is an item it must leave for the right-click that clears it. False
        /// means nothing is delivering: for a machine already holding an item, simple backpressure - it
        /// waits and lets the belts upstream queue.
        ///
        /// The intake mirror of <see cref="TryPushOut"/>, and the one rule for every machine that picks
        /// up and holds what it takes (the splitter hub and the sorter), put here so "an outlet is a
        /// belt not pointing in" and "an intake is a belt pointing in" cannot drift apart.
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

        /// <summary>
        /// Eat the first ready delivery off <paramref name="cell"/>'s belts, whatever state it is in: the
        /// sink's intake, and the one intake that does not skip a jammed segment. A machine that
        /// <em>carries</em> what it takes must leave a jammed item alone for the right-click that clears it
        /// (<see cref="TryTakeInput"/>), but a machine that destroys what it takes is a way <em>out</em> of a
        /// jam rather than a way around one - which is what makes a sink laid at the end of a stuck line
        /// unstick it. The take clears the flag with the item; see <see cref="BeltField.TryTakeItem"/>.
        /// </summary>
        public static bool TryEat(BeltField belts, Int2 cell, in MachineDef def, out Shape shape)
        {
            shape = Shape.None;

            for (int i = 0; i < Ports.Length; i++)
            {
                if (!TryReadyAt(belts, cell, Ports[i], rejectJammed: false, out Int2 neighbour, out BeltState state)) continue;
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
            => TryIncomingAt(belts, cell, side, rejectJammed, out neighbour, out state) &&
               state.Progress >= 1f;

        /// <summary>
        /// The same belt question minus the exit edge: a belt pointing into this machine with an item on it,
        /// <em>wherever along its cell the item has got to</em>. The one statement of "something is being
        /// delivered into this cell", which <see cref="TryReadyAt"/> is one gate away from - so a preview that
        /// wants the item still travelling down the lane asks this rather than re-spelling "a belt pointing in
        /// with something on it" and drifting from the tick.
        /// </summary>
        public static bool TryIncomingAt(BeltField belts, Int2 cell, Dir side, bool rejectJammed,
            out Int2 neighbour, out BeltState state)
        {
            neighbour = cell + side.Offset();
            if (!belts.TryGet(neighbour, out state)) return false;
            if (state.Direction != side.Opposite()) return false;   // it must point at the machine
            if (rejectJammed && state.Jammed) return false;
            return !state.Item.IsEmpty;
        }

        /// <summary>
        /// The machine's two port sets, computed once so the simulation and the view read the same rule.
        ///
        /// <paramref name="inMask"/>: every side a belt points at the machine from - the sides it can be
        /// fed on, which is what makes a stub pointing inward read as an input.
        ///
        /// <paramref name="outMask"/>: every side with an outlet belt on it (<see cref="IsOutletDirection"/>:
        /// anything not pointing back in), when the machine's kind has an output there. A drill's facing <em>is</em> its output (exactly one); a cutter's is the side it
        /// faces, on each of the two tiles it stands on (the two pieces of its cut leave by the front of
        /// one tile each); a rotator's is the side it faces; a stacker, splitter and sorter read their
        /// outlets off the belts around them (every side wired that way); a pipe (lands two cells ahead) and
        /// a turret (never pushes) get none.
        ///
        /// This answers for <em>one tile</em>. A machine that stands on two has ports on both, so its
        /// snapshot carries the union of the two (<see cref="MachineField.SnapshotOf"/>) while a view that
        /// draws a stub per side asks <see cref="MachineField.PortMasksAt"/>.
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
                else if (IsOutletDirection(belt.Direction, side) && HasOutputOn(behavior, machine.Direction, side))
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

                // Both pieces leave at once, each out of the front of its own tile: the blade tile the belt
                // fed, and - a lane over - the second tile. So on either tile it is the facing side that is
                // an outlet; the rest are inputs. See MachineFootprint for why the cutter is two tiles.
                case BehaviorKind.Cutter: return facing == side;

                case BehaviorKind.Stacker:
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter: return true;

                case BehaviorKind.None:
                case BehaviorKind.Pipe:
                // A pipe lands two cells ahead, never on an adjoining belt, and neither a turret nor a sink
                // pushes anything out: a turret fires, and a sink has no product at all.
                case BehaviorKind.Turret:
                case BehaviorKind.Sink: return false;

                default:
                    throw new ArgumentOutOfRangeException(nameof(behavior), behavior,
                        "a new behaviour must declare here whether it pushes out on a wired side");
            }
        }
    }
}
