using System;

namespace Facet.Core
{
    /// <summary>One wave's line in the matched-round analytics: how many shots landed with the round the
    /// wave was weak to, how many with any other, and how the wave ended.</summary>
    public readonly struct WaveMatchRow
    {
        /// <summary>The map's name, as the campaign names it.</summary>
        public readonly string Map;

        /// <summary>The wave's 1-based number on that map.</summary>
        public readonly int Wave;

        /// <summary>Hits that did full damage: the round matched the target's weakness.</summary>
        public readonly int Matched;

        /// <summary>Hits that did half damage: any other round.</summary>
        public readonly int Resisted;

        /// <summary>How the wave ended: <c>cleared</c> (and the map goes on), <c>won</c> (the map's last
        /// wave - the run is finished), <c>lost</c> (the Core fell during it - also a finished run), or
        /// <c>abandoned</c> (a restart or a map change mid-wave).</summary>
        public readonly string Outcome;

        public WaveMatchRow(string map, int wave, int matched, int resisted, string outcome)
        {
            Map = map;
            Wave = wave;
            Matched = matched;
            Resisted = resisted;
            Outcome = outcome;
        }

        /// <summary>Hits this wave, matched or not.</summary>
        public int Hits => Matched + Resisted;

        /// <summary>The metric itself: the share of hits that matched, 0..1, or 0 for a wave nobody shot.</summary>
        public float MatchRate => Hits == 0 ? 0f : (float)Matched / Hits;

        public override string ToString()
            => Map + " wave " + Wave + ": " + Matched + "/" + Hits + " matched (" + Outcome + ")";
    }

    /// <summary>
    /// Turns the run's running hit counts (<see cref="EnemyField.MatchedHits"/>,
    /// <see cref="EnemyField.ResistedHits"/>) into one <see cref="WaveMatchRow"/> per wave, for the
    /// matched-round analytics metric.
    ///
    /// It is a reader polled from outside the tick - once a frame by the analytics reporter, or by a test -
    /// and it reads only state the world already exposes, so the simulation does not know it exists and a
    /// run is identical with or without it. Plain C# with no engine reference, so it is tested in EditMode.
    ///
    /// A wave opens when <see cref="WaveDirector.CurrentWave"/> turns non-zero and closes when it changes or
    /// the run stops. The wave that ends a run says how: <c>won</c> or <c>lost</c>, so the sheet can tell a
    /// finished attempt at a map from one the player walked away from. A restart (the tick count going backwards) or a different world (a map change) closes
    /// an open wave as <c>abandoned</c>, with the counts last seen before the reset.
    /// </summary>
    public sealed class MatchRateTracker
    {
        private SimWorld _world;
        private int _lastTick;

        private int _wave;
        private string _map;
        private int _matchedAtStart;
        private int _resistedAtStart;

        private int _seenMatched;
        private int _seenResisted;

        /// <summary>The wave being counted, or 0 between waves.</summary>
        public int OpenWave => _wave;

        /// <summary>Read the world once. True, with the finished wave in <paramref name="row"/>, on the poll
        /// that sees a wave end; false on every other poll.</summary>
        public bool Poll(SimWorld world, out WaveMatchRow row)
        {
            row = default;
            if (world == null) return false;

            bool emitted = false;

            // A new world or a restarted one: whatever wave was open ends here, counted from what was seen
            // last - the counters behind it are already gone.
            if (_world != null && (!ReferenceEquals(world, _world) || world.TickCount < _lastTick))
            {
                if (_wave > 0)
                {
                    row = Close("abandoned");
                    emitted = true;
                }

                _wave = 0;
            }

            _world = world;
            _lastTick = world.TickCount;
            _seenMatched = world.Enemies.MatchedHits;
            _seenResisted = world.Enemies.ResistedHits;

            int current = world.Status == GameStatus.Playing ? world.Waves.CurrentWave : 0;

            if (!emitted && _wave > 0 && current != _wave)
            {
                row = Close(world.Status == GameStatus.Lost ? "lost"
                    : world.Status == GameStatus.Won ? "won"
                    : "cleared");
                emitted = true;
            }

            if (_wave == 0 && current > 0)
            {
                _wave = current;
                _map = world.Map.Name;
                _matchedAtStart = _seenMatched;
                _resistedAtStart = _seenResisted;
            }

            return emitted;
        }

        private WaveMatchRow Close(string outcome)
        {
            var row = new WaveMatchRow(_map, _wave,
                Math.Max(0, _seenMatched - _matchedAtStart),
                Math.Max(0, _seenResisted - _resistedAtStart),
                outcome);
            _wave = 0;
            return row;
        }
    }
}
