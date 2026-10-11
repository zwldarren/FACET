using System;

namespace Facet.Core
{
    /// <summary>How a run ended, or that it has not.</summary>
    public enum GameStatus : byte
    {
        Playing = 0,
        Won = 1,
        Lost = 2,
    }

    /// <summary>
    /// What the wave clock is doing right now. Readers used to compose this themselves from two
    /// sentinels - <see cref="WaveDirector.CurrentWave"/> == 0 meant "waiting", with
    /// <see cref="WaveDirector.NextWave"/> == 0 for "nothing left to wait for" - and a renderer stated
    /// "a countdown is running" a third way, by a non-zero total. Each recomposition was a chance to
    /// state the rule wrong; the state is the director's, so the answer is too. Branch on this.
    /// </summary>
    public enum WavePhase : byte
    {
        /// <summary>A wave is on the clock: its groups are spawning or its enemies are still alive.</summary>
        Running = 0,

        /// <summary>Between two waves, with a clock running - the wave after this one starts itself
        /// when the countdown ends.</summary>
        Intermission = 1,

        /// <summary>The wait before the map's first wave: no clock runs, and only the player sends it
        /// (<see cref="WaveDirector.FirstWaveHeld"/>).</summary>
        Held = 2,

        /// <summary>Every wave in the table has been fought. The run's own end is
        /// <see cref="GameStatus"/>'s; this is the clock's answer, and the clock keeps no score.</summary>
        Over = 3,
    }

    /// <summary>
    /// The wave clock: which wave is running, what is left of it, and how long the intermission has to
    /// go. Waves after the first auto-start when their intermission runs out, and N starts one early -
    /// countdown pressure as the design asks for, with a player who has just re-plumbed a line still
    /// able to choose "now".
    ///
    /// The first wave is the exception, and deliberately so: it has no countdown and never sends
    /// itself (<see cref="FirstWaveHeld"/>). A run opens in the player's hands - mine a circle, belt it
    /// home, split it into ammunition, place a cannon - and the wave walks in when they say so, not
    /// when a clock they never agreed to says so. Only what they built, and when they chose to call
    /// it, decide whether the Core holds.
    ///
    /// It owns spawning and the definition of "this wave is over"; whether that wins the run is the
    /// world's business (<see cref="GameStatus"/>), so there is exactly one place that decides it.
    /// </summary>
    public sealed class WaveDirector
    {
        /// <summary>Room for every group of a wave. The table is fixed; this is only a bound.</summary>
        private const int MaxGroups = 16;

        private readonly EnemyField _enemies;
        private readonly MapDefinition _map;

        /// <summary>The stream waves are announced on, so a view can react to a wave starting or being
        /// cleared without polling the director's state for edges.</summary>
        private readonly SimEventBuffer _events;

        private readonly float[] _groupTimer = new float[MaxGroups];
        private readonly int[] _groupSpawned = new int[MaxGroups];

        private int _waveIndex = -1;     // 0-based index of the wave being fought, -1 during intermission

        /// <summary>
        /// The countdown is kept in ticks, not in seconds. Subtracting a float tick from a float
        /// remaining value drifts: after exactly 1800 subtractions of 1/30 s the residue can still be
        /// positive, so a 60 second intermission would end on tick 1801 or 1799 depending on rounding.
        /// An integer countdown makes "60 seconds" exactly 1800 ticks, every run.
        /// </summary>
        private int _intermissionTicks;

        /// <summary>
        /// What the clock is doing (<see cref="WavePhase"/>), in one word. The sentinel readings it
        /// replaces - "CurrentWave == 0 is an intermission", "NextWave == 0 is over", "a non-zero
        /// total is counting down" - all agree with this, and readers now branch here instead of
        /// recomposing the state from numbers.
        /// </summary>
        public WavePhase Phase
        {
            get
            {
                if (_waveIndex >= 0) return WavePhase.Running;
                if (Finished || NextWave == 0) return WavePhase.Over;
                return FirstWaveHeld ? WavePhase.Held : WavePhase.Intermission;
            }
        }

        /// <summary>Is there a wave the player can start right now? The question the N key, the HUD's
        /// button and a test's "run to the end" all ask; starting is still a no-op while
        /// <see cref="WavePhase.Running"/> or <see cref="WavePhase.Over"/>, but readers should not
        /// offer what they would have to swallow.</summary>
        public bool HasWaveToStart => Phase == WavePhase.Held || Phase == WavePhase.Intermission;

        /// <summary>1-based number of the wave being fought, or 0 during an intermission. For printing
        /// the number; for *branching*, read <see cref="Phase"/>.</summary>
        public int CurrentWave { get; private set; }

        /// <summary>1-based number of the wave the intermission is counting down to, 0 when none is left.</summary>
        public int NextWave { get; private set; }

        /// <summary>
        /// Seconds until the next wave starts by itself: 0 while a wave is running, and 0 while the
        /// first wave waits for the player, which no clock ever starts.
        /// </summary>
        public float IntermissionRemaining { get; private set; }

        /// <summary>
        /// What that countdown started at, so a HUD can draw it as a fraction. 0 when there is no
        /// countdown at all: while a wave runs, or while the first wave is held for the player.
        /// </summary>
        public float IntermissionTotal { get; private set; }

        /// <summary>
        /// True while the wave being waited on is the map's first (see <see cref="FirstWaveHeld"/>).
        /// </summary>
        public bool FirstWaveHeld => NextWave == 1;

        /// <summary>True once every wave in the table has been fought and cleared.</summary>
        public bool Finished { get; private set; }

        /// <summary>The wave table this director runs: the map's own data, so a different map is a
        /// different campaign without a code change here.</summary>
        private WaveDefinition[] Table => _map.Waves;

        public WaveDirector(EnemyField enemies, MapDefinition map, SimEventBuffer events)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            Reset();
        }

        /// <summary>Enemies of the running wave that have not spawned yet.</summary>
        public int PendingInWave
        {
            get
            {
                if (_waveIndex < 0) return 0;

                WaveDefinition wave = Table[_waveIndex];
                int pending = 0;
                for (int g = 0; g < wave.Groups.Length; g++)
                    pending += Math.Max(0, wave.Groups[g].Count - _groupSpawned[g]);

                return pending;
            }
        }

        /// <summary>The definition of the wave the intermission is waiting on, or null while one runs.</summary>
        public WaveDefinition NextWaveDefinition => NextWave > 0 ? Table[NextWave - 1] : null;

        public void Reset()
        {
            _waveIndex = -1;
            CurrentWave = 0;
            NextWave = Table.Length > 0 ? 1 : 0;
            Finished = false;
            BeginIntermission(NextWave);
            Array.Clear(_groupTimer, 0, _groupTimer.Length);
            Array.Clear(_groupSpawned, 0, _groupSpawned.Length);
        }

        /// <summary>
        /// Arm the wait before the wave numbered <paramref name="waveNumber"/> (0 when there is none).
        /// Wave 1 gets no countdown - <see cref="FirstWaveHeld"/> says why - so its clock reads zero
        /// and only <see cref="StartNextWave"/> can end the wait. Every later wave counts down.
        /// </summary>
        private void BeginIntermission(int waveNumber)
        {
            bool counted = waveNumber > 1;
            IntermissionTotal = counted ? Table[waveNumber - 1].Intermission : 0f;
            _intermissionTicks = SecondsToTicks(IntermissionTotal);
            IntermissionRemaining = IntermissionTotal;
        }

        public void Step(float dt)
        {
            if (Finished) return;

            if (_waveIndex < 0)
            {
                if (NextWave == 0) return;

                // The first wave holds for as long as the player wants: no countdown to run out,
                // nothing to start. It arrives when they call it - N, or the HUD's button.
                if (FirstWaveHeld) return;

                if (_intermissionTicks > 0) _intermissionTicks--;
                IntermissionRemaining = _intermissionTicks * SimConfig.TickDt;
                if (_intermissionTicks == 0) StartWave(NextWave - 1);
                return;
            }

            Spawn(dt);

            if (PendingInWave == 0 && _enemies.AliveCount == 0) CompleteWave();
        }

        /// <summary>Round a duration up to whole ticks, so a countdown never ends early.</summary>
        private static int SecondsToTicks(float seconds)
            => (int)MathF.Ceiling(MathF.Max(0f, seconds) * SimConfig.TickRate);

        /// <summary>
        /// Start the next wave now, the way N does. This is the only way the first wave ever arrives.
        /// </summary>
        public void StartNextWave()
        {
            if (Finished || _waveIndex >= 0 || NextWave == 0) return;
            StartWave(NextWave - 1);
        }

        private void StartWave(int index)
        {
            _waveIndex = index;
            CurrentWave = index + 1;
            IntermissionRemaining = 0f;
            IntermissionTotal = 0f;

            WaveDefinition wave = Table[index];
            for (int g = 0; g < wave.Groups.Length && g < MaxGroups; g++)
            {
                _groupTimer[g] = wave.Groups[g].FirstDelay;
                _groupSpawned[g] = 0;
            }

            _events.WaveStarted(CurrentWave);
        }

        private void Spawn(float dt)
        {
            WaveDefinition wave = Table[_waveIndex];

            for (int g = 0; g < wave.Groups.Length && g < MaxGroups; g++)
            {
                SpawnGroup group = wave.Groups[g];
                if (_groupSpawned[g] >= group.Count) continue;

                _groupTimer[g] -= dt;
                // A zero-interval group would spin here forever, so the spacing is clamped rather
                // than trusted: the table is data, and data does not get to hang the game.
                float interval = MathF.Max(group.Interval, SimConfig.TickDt);

                while (_groupTimer[g] <= 0f && _groupSpawned[g] < group.Count)
                {
                    _enemies.Spawn(group.Kind, _map.SpawnCenter(group.SpawnPoint));
                    _groupSpawned[g]++;
                    _groupTimer[g] += interval;
                }
            }
        }

        private void CompleteWave()
        {
            // CurrentWave is the number a reader *prints*; the state it cannot print is what
            // Phase says. This once tripped a reader that took "still the wave just fought" as
            // "still running" and reported a cleared wave as one with nothing left in it - the
            // branch belongs on Phase, and the number goes to 0 while nobody is being fought.
            int cleared = CurrentWave;
            _waveIndex = -1;
            CurrentWave = 0;
            _events.WaveCleared(cleared);

            if (cleared >= Table.Length)
            {
                Finished = true;
                NextWave = 0;
                return;
            }

            NextWave = cleared + 1;
            BeginIntermission(NextWave);
        }
    }
}
