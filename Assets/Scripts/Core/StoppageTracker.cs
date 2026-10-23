using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One wave's line in the line-stoppage analytics: how many times a production line stopped,
    /// split by the three ways the game says a line stops, and how the wave ended.</summary>
    public readonly struct WaveStoppageRow
    {
        /// <summary>The map's name, as the campaign names it.</summary>
        public readonly string Map;

        /// <summary>The wave's 1-based number on that map, or 0 for a stretch abandoned between waves.</summary>
        public readonly int Wave;

        /// <summary>Belts that became jammed: a machine refused the shape on them.</summary>
        public readonly int Jammed;

        /// <summary>Belts that became blocked: the line ends on bare ground, the map's edge or a wall.</summary>
        public readonly int Blocked;

        /// <summary>Machines that became stalled: their piece has had nowhere to go for the HUD's threshold.</summary>
        public readonly int Stalled;

        /// <summary>How the stretch ended - the same words as the matched-round rows: <c>cleared</c>,
        /// <c>won</c>, <c>lost</c> or <c>abandoned</c>.</summary>
        public readonly string Outcome;

        public WaveStoppageRow(string map, int wave, int jammed, int blocked, int stalled, string outcome)
        {
            Map = map;
            Wave = wave;
            Jammed = jammed;
            Blocked = blocked;
            Stalled = stalled;
            Outcome = outcome;
        }

        /// <summary>Every stoppage in the row, whatever its cause.</summary>
        public int Total => Jammed + Blocked + Stalled;

        public override string ToString()
            => Map + " wave " + Wave + ": " + Jammed + " jammed, " + Blocked + " blocked, " + Stalled +
               " stalled (" + Outcome + ")";
    }

    /// <summary>
    /// Turns the run's stopped lines into one <see cref="WaveStoppageRow"/> per wave, for the line-stoppage
    /// analytics metric (Metric 2 in the GDD): which of the game's three stoppages players run into, and how
    /// often.
    ///
    /// A stoppage is counted once, on the poll that first sees it: a cell that newly appears in
    /// <see cref="BeltField.GetJammed"/>, <see cref="BeltField.GetBlocked"/> or
    /// <see cref="MachineField.GetStalled"/> - the same three lists the status card and the locator read, so
    /// the number in the sheet is the number of warnings the player was shown. A jam that stands for a whole
    /// wave is one stoppage, not one per frame; one that is cleared and comes back is two.
    ///
    /// Unlike the matched-round rows, which only count while a wave is being fought, a row here covers
    /// everything since the previous row: most stoppages happen while the player is building, between
    /// waves, and the row for wave N is the one that carries the build-up before it. The stretch is cut when
    /// a wave ends, so the rows line up with the matched-round rows wave for wave. A restart or a map change
    /// closes whatever was counted as <c>abandoned</c>.
    ///
    /// Like <see cref="MatchRateTracker"/> it is a reader polled from outside the tick and never writes to
    /// the world, so a run is identical with or without it. Plain C# with no engine reference.
    /// </summary>
    public sealed class StoppageTracker
    {
        private SimWorld _world;
        private int _lastTick;
        private string _map;
        private int _wave;

        private int _jammed;
        private int _blocked;
        private int _stalled;

        private HashSet<Int2> _seenJammed = new HashSet<Int2>();
        private HashSet<Int2> _seenBlocked = new HashSet<Int2>();
        private HashSet<Int2> _seenStalled = new HashSet<Int2>();
        private HashSet<Int2> _now = new HashSet<Int2>();
        private readonly List<Int2> _cells = new List<Int2>();

        /// <summary>The wave being fought, or 0 between waves.</summary>
        public int OpenWave => _wave;

        /// <summary>Stoppages counted since the last row.</summary>
        public int Pending => _jammed + _blocked + _stalled;

        /// <summary>What the next row carries so far, cause by cause - for a test, or a world with no waves to
        /// cut a row at.</summary>
        public WaveStoppageRow Counted => new WaveStoppageRow(_map, _wave, _jammed, _blocked, _stalled, "open");

        /// <summary>Read the world once. True, with the finished stretch in <paramref name="row"/>, on the poll
        /// that sees a wave end (or a restart throw a stretch away); false on every other poll.</summary>
        public bool Poll(SimWorld world, out WaveStoppageRow row)
        {
            row = default;
            if (world == null) return false;

            bool emitted = false;

            // A new world or a restarted one: what was counted belongs to the run that just ended, and the
            // cells it was counted on mean nothing in the next one.
            if (_world != null && (!ReferenceEquals(world, _world) || world.TickCount < _lastTick))
            {
                if (_wave > 0 || Pending > 0)
                {
                    row = Close("abandoned");
                    emitted = true;
                }

                _wave = 0;
                _seenJammed.Clear();
                _seenBlocked.Clear();
                _seenStalled.Clear();
            }

            _world = world;
            _lastTick = world.TickCount;
            _map = world.Map.Name;

            // Only a run in play can stop a line; after a win or a loss the lists are what was left standing.
            if (world.Status == GameStatus.Playing)
            {
                world.Belts.GetJammed(_cells);
                _jammed += CountNew(ref _seenJammed);
                world.Belts.GetBlocked(_cells);
                _blocked += CountNew(ref _seenBlocked);
                world.Machines.GetStalled(_cells);
                _stalled += CountNew(ref _seenStalled);
            }

            // The same clock reading as the matched-round tracker, so the two sheets cut their rows at the
            // same moments: the phase says whether a wave is being fought, the number only labels it.
            bool fighting = world.Status == GameStatus.Playing && world.Waves.Phase == WavePhase.Running;
            int current = fighting ? world.Waves.CurrentWave : 0;

            if (!emitted && _wave > 0 && current != _wave)
            {
                row = Close(world.Status == GameStatus.Lost ? "lost"
                    : world.Status == GameStatus.Won ? "won"
                    : "cleared");
                emitted = true;
            }

            if (_wave == 0 && current > 0) _wave = current;

            return emitted;
        }

        /// <summary>How many of the cells in <see cref="_cells"/> were not stopped on the last poll; the set
        /// of stopped cells becomes this poll's.</summary>
        private int CountNew(ref HashSet<Int2> seen)
        {
            _now.Clear();
            int fresh = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                if (!_now.Add(_cells[i])) continue;
                if (!seen.Contains(_cells[i])) fresh++;
            }

            HashSet<Int2> previous = seen;
            seen = _now;
            _now = previous;
            return fresh;
        }

        private WaveStoppageRow Close(string outcome)
        {
            var row = new WaveStoppageRow(_map, _wave, _jammed, _blocked, _stalled, outcome);
            _wave = 0;
            _jammed = 0;
            _blocked = 0;
            _stalled = 0;
            return row;
        }
    }
}
