using System;

namespace Facet.Core
{
    /// <summary>
    /// How many lines each belt carries, read off the layout - the rule that lets a merge keep up.
    ///
    /// A belt cell holds one item, so a run carries <see cref="SimConfig.BeltSpeed"/> items a second and no
    /// more. Two lines merged into one - by a splitter, or by one belt running into the side of another -
    /// bring twice that, and at one speed the merged run could only take half: both feeders backed up behind
    /// the join and the merge bought nothing. So a belt runs as many times the base speed as there are lines
    /// feeding it. An ordinary run carries 1; the run out of a two-into-one splitter carries 2, and so does
    /// everything downstream of it, through turns and across a pipe, until the line is split again.
    ///
    /// <list type="bullet">
    /// <item>A belt carries the sum of what flows into it: every belt pointing at it, the splitter it is an
    /// outlet of, and the pipe that lands on it. Anything else that delivers - a drill, an operator - starts a
    /// line rather than adding to one, so a belt never carries fewer than 1.</item>
    /// <item>A splitter shares what comes in across its outlets, rounded up: two in and one out is 2, one in
    /// and two out is 1 each, three in and two out is 2 each. Rounding up is what keeps the outlets able to
    /// take everything the inputs bring.</item>
    /// <item>A pipe hands on what came into it, so crossing a line does not slow a merged run down.</item>
    /// </list>
    ///
    /// Capped at <see cref="MaxLines"/>, so a chain of merges cannot send a run past what the eye can follow.
    /// A loop of belts feeds itself nothing: the walk upstream stops where it came in, which also keeps it
    /// from running forever. Every walk goes in linear-index order with the fixed port order, so the answer is
    /// a function of the layout alone.
    ///
    /// Read again only when a belt or a building changes - a laid, re-pointed or removed belt, a placed or
    /// destroyed machine - since nothing else moves the answer.
    /// </summary>
    internal sealed class BeltFlow
    {
        /// <summary>The most lines one belt carries: four times the base speed, past what a single splitter can
        /// merge (three in, one out) but not by much.</summary>
        public const int MaxLines = 4;

        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly MachineField _machines;
        private readonly ContentDatabase _content;

        /// <summary>Per cell: a belt's lines once worked out this pass, 0 until then.</summary>
        private readonly byte[] _lines;

        /// <summary>Per cell: on the walk upstream right now - a belt or a machine met again is a loop.</summary>
        private readonly bool[] _visiting;

        private int _seenBelts = -1;
        private int _seenMachines = -1;

        public BeltFlow(TileGrid grid, BeltField belts, MachineField machines, ContentDatabase content)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _content = content ?? throw new ArgumentNullException(nameof(content));

            int cells = grid.Width * grid.Height;
            _lines = new byte[cells];
            _visiting = new bool[cells];
        }

        /// <summary>Re-read the layout if a belt or a building has changed since the last read, and hand every
        /// belt its lines. Call once per tick, before the belts move.</summary>
        public void Update()
        {
            if (_belts.Revision == _seenBelts && _machines.Revision == _seenMachines) return;

            Array.Clear(_lines, 0, _lines.Length);
            for (int i = 0; i < _lines.Length; i++)
            {
                if (_grid.Get(_grid.CellOf(i)) != TileKind.Belt) continue;
                _belts.SetLines(i, LinesOf(i));
            }

            // Read after the writes: a changed answer bumps the belts' revision so the renderer redraws, and
            // that bump is this read's own, not a change to read again next tick.
            _seenBelts = _belts.Revision;
            _seenMachines = _machines.Revision;
        }

        /// <summary>The lines belt <paramref name="index"/> carries: the sum of its feeds, at least 1 and at
        /// most <see cref="MaxLines"/>. 0 for a belt already on the walk - a loop adds nothing to itself.</summary>
        private int LinesOf(int index)
        {
            if (_lines[index] != 0) return _lines[index];
            if (_visiting[index]) return 0;

            _visiting[index] = true;
            Int2 cell = _grid.CellOf(index);
            _belts.TryGet(cell, out BeltState belt);

            int sum = 0;
            for (int p = 0; p < MachineDelivery.Ports.Length; p++)
            {
                Dir side = MachineDelivery.Ports[p];
                Int2 next = cell + side.Offset();
                if (!_grid.InBounds(next)) continue;

                // A belt pointing at this one feeds it - straight on, round a turn, or into its side.
                if (_belts.TryGet(next, out BeltState feeder))
                {
                    if (feeder.Direction == side.Opposite()) sum += LinesOf(_grid.Index(next));
                    continue;
                }

                // A splitter feeds this belt when it is one of its outlets: seen from the splitter, this belt
                // is on its far side, and an outlet is any belt there that does not point back in.
                if (Is(next, BehaviorKind.Splitter, out _) &&
                    MachineDelivery.IsOutletDirection(belt.Direction, side.Opposite()))
                    sum += SplitterShare(next);
            }

            // A pipe two cells behind, facing this way, lands its items here.
            for (int p = 0; p < MachineDelivery.Ports.Length; p++)
            {
                Dir facing = MachineDelivery.Ports[p];
                Int2 pipe = cell - facing.Offset() * 2;
                if (!_grid.InBounds(pipe)) continue;
                if (Is(pipe, BehaviorKind.Pipe, out MachineState state) && state.Direction == facing)
                    sum += Intake(pipe);
            }

            _visiting[index] = false;

            int lines = Math.Min(MaxLines, Math.Max(1, sum));
            _lines[index] = (byte)lines;
            return lines;
        }

        /// <summary>What each of a splitter's outlets carries: everything coming in, shared across the outlets
        /// and rounded up. The outlets are counted by the same rule the splitter pushes by
        /// (<see cref="MachineDelivery.IsOutput"/>), so a belt the hub would deal to is a belt it shares
        /// with.</summary>
        private int SplitterShare(Int2 splitter)
        {
            int outlets = 0;
            for (int p = 0; p < MachineDelivery.Ports.Length; p++)
                if (MachineDelivery.IsOutput(_belts, splitter, MachineDelivery.Ports[p])) outlets++;

            if (outlets == 0) return 0;

            int incoming = Intake(splitter);
            return (incoming + outlets - 1) / outlets;
        }

        /// <summary>The lines delivering into a building: every belt beside it pointing in. A building met
        /// again on the same walk is a loop through it, and adds nothing.</summary>
        private int Intake(Int2 building)
        {
            int index = _grid.Index(building);
            if (_visiting[index]) return 0;

            _visiting[index] = true;
            int sum = 0;
            for (int p = 0; p < MachineDelivery.Ports.Length; p++)
            {
                Dir side = MachineDelivery.Ports[p];
                Int2 next = building + side.Offset();
                if (_belts.TryGet(next, out BeltState feeder) && feeder.Direction == side.Opposite())
                    sum += LinesOf(_grid.Index(next));
            }
            _visiting[index] = false;
            return sum;
        }

        /// <summary>Is there a building of this behaviour standing on <paramref name="cell"/>?</summary>
        private bool Is(Int2 cell, BehaviorKind behavior, out MachineState state)
            => _machines.TryGet(cell, out state) && _content.Machine(state.Build).Behavior == behavior;
    }
}
