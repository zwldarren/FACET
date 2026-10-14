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

    /// <summary>What the wave clock is doing right now. Readers used to compose this from two sentinels -
    /// <see cref="WaveDirector.CurrentWave"/> == 0 meant "waiting", <see cref="WaveDirector.NextWave"/> == 0
    /// for "nothing left to wait for" - and a renderer stated "a countdown is running" a third way. The
    /// state is the director's, so the answer is too. Branch on this.</summary>
    public enum WavePhase : byte
    {
        /// <summary>A wave is on the clock: its groups are spawning or its enemies are still alive.</summary>
        Running = 0,

        /// <summary>Between two waves, with a clock running - the wave after this one starts itself when
        /// the countdown ends. Every wait but the opening is one of these, and the opening becomes one the
        /// moment the player's first building arms it.</summary>
        Intermission = 1,

        /// <summary>The opening, before its clock has been armed: the first wave waits for the player's
        /// first building (<see cref="WaveDirector.ArmFirstWave"/>), and no time passes - so reading the map
        /// and planning cost nothing. <see cref="WaveDirector.StartNextWave"/> ends the wait with no
        /// building placed.</summary>
        Held = 2,

        /// <summary>Every wave in the table has been fought. The run's own end is
        /// <see cref="GameStatus"/>'s; this is the clock's answer, and the clock keeps no score.</summary>
        Over = 3,
    }

    /// <summary>The wave clock: which wave is running, what is left of it, and how long the intermission
    /// has to go. Every wave - the first included - starts itself when its intermission runs out, and
    /// <see cref="StartNextWave"/> starts one early, so a player ready sooner can choose "now".
    ///
    /// The opening is the one wait that does not begin with the run: it is held until the player's first
    /// building, so studying the map costs no time. Its countdown is the longest on the table, because the
    /// whole opening is built under it - about 16 s for the bootstrap line to bank the defence's 66 circles
    /// and 35 s for the ammo line to fill, measured from the first building - rounded up for a slower
    /// player. It is a ceiling on the wait, not a floor: a player done early calls the wave with `N`.
    ///
    /// It owns spawning and the definition of "this wave is over"; whether that wins the run is the
    /// world's business (<see cref="GameStatus"/>).</summary>
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

        /// <summary>The countdown is kept in ticks, not seconds. Subtracting a float tick from a float
        /// remaining value drifts: after exactly 1800 subtractions of 1/30 s the residue can still be
        /// positive, so a 60 s intermission would end on tick 1801 or 1799 depending on rounding. An integer
        /// countdown makes "60 seconds" exactly 1800 ticks, every run.</summary>
        private int _intermissionTicks;

        /// <summary>The opening's clock is not armed yet: the first wave waits for the player's first
        /// building (<see cref="ArmFirstWave"/>) rather than time. Only the opening behaves this way -
        /// every later intermission is armed when the wave before it clears - so the bit is set once per run
        /// by <see cref="Reset"/>, spent by whatever starts the opening, and never set again.</summary>
        private bool _openingWaiting;

        /// <summary>What the clock is doing (<see cref="WavePhase"/>), in one word. The sentinel readings
        /// it replaces - "CurrentWave == 0 is an intermission", "NextWave == 0 is over", "a non-zero total
        /// is counting down" - all agree with this, and readers branch here instead of recomposing the state
        /// from numbers.</summary>
        public WavePhase Phase
        {
            get
            {
                if (_waveIndex >= 0) return WavePhase.Running;
                if (Finished || NextWave == 0) return WavePhase.Over;
                return _openingWaiting ? WavePhase.Held : WavePhase.Intermission;
            }
        }

        /// <summary>Is there a wave the player can start right now? The question the N key, the HUD button
        /// and a test's "run to the end" all ask - the opening included, held or already counting down.
        /// Starting is still a no-op while <see cref="WavePhase.Running"/> or <see cref="WavePhase.Over"/>,
        /// but readers should not offer what they would have to swallow.</summary>
        public bool HasWaveToStart => Phase == WavePhase.Held || Phase == WavePhase.Intermission;

        /// <summary>1-based number of the wave being fought, or 0 during an intermission. For printing
        /// the number; for *branching*, read <see cref="Phase"/>.</summary>
        public int CurrentWave { get; private set; }

        /// <summary>1-based number of the wave the intermission is counting down to, 0 when none is left.</summary>
        public int NextWave { get; private set; }

        /// <summary>Seconds until the next wave starts by itself: 0 while a wave runs, and 0 once the
        /// table is exhausted. While the opening is <see cref="WavePhase.Held"/> this is the countdown that
        /// will run once the first building arms it, not one already running - <see cref="Phase"/> says
        /// which of the two it is.</summary>
        public float IntermissionRemaining { get; private set; }

        /// <summary>What that countdown starts at, so a HUD can draw it as a fraction. 0 when there is no
        /// countdown at all: while a wave runs, or once the table is exhausted. Held for the first building,
        /// it is the whole countdown waiting to be armed.</summary>
        public float IntermissionTotal { get; private set; }

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

            // A restart puts the opening back in the player's hands: the clock is armed by a building
            // again, so a restarted run is not already late before the first thing is laid.
            _openingWaiting = true;
        }

        /// <summary>Arm the opening: the first wave's countdown starts now, because the player has begun
        /// to build. Called by the world on the tick a run's first building lands, and idempotent, so the
        /// world may say it every tick after. A run where the player never builds never arms it - the point,
        /// since reading the map costs no time. Calling the wave early (<see cref="StartNextWave"/>) ends the
        /// opening without arming it.</summary>
        public void ArmFirstWave() => _openingWaiting = false;

        /// <summary>Arm the wait before the wave numbered <paramref name="waveNumber"/> (0 when there is
        /// none): the countdown every wave runs, the first included, so "this wave is coming" is one rule
        /// and its length is the table's number rather than a special case. The opening's wait begins
        /// ticking when the first building arms it (<see cref="ArmFirstWave"/>); every other one begins the
        /// moment it is armed here.</summary>
        private void BeginIntermission(int waveNumber)
        {
            IntermissionTotal = waveNumber > 0 ? Table[waveNumber - 1].Intermission : 0f;
            _intermissionTicks = SecondsToTicks(IntermissionTotal);
            IntermissionRemaining = IntermissionTotal;
        }

        public void Step(float dt)
        {
            if (Finished) return;

            if (_waveIndex < 0)
            {
                if (NextWave == 0) return;

                // The opening holds until the player builds something: no tick passes while it is Held,
                // so the countdown the table declares is spent building, not reading.
                if (_openingWaiting) return;

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

        /// <summary>Start the next wave now, the way N does: cut the countdown short, or end the opening's
        /// wait before a building has armed it. The same verb serves every wait, the first included - a
        /// player who finished early, or wants the wave before building anything, need not watch the
        /// clock.</summary>
        public void StartNextWave()
        {
            if (Finished || _waveIndex >= 0 || NextWave == 0) return;
            StartWave(NextWave - 1);
        }

        private void StartWave(int index)
        {
            // However the wait ended - a countdown running out, or the player calling it - the opening is
            // over, so the bit that held it is spent here rather than by the path that got here.
            _openingWaiting = false;

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
            // CurrentWave is the number a reader *prints*; the state it cannot print is what Phase says.
            // This once tripped a reader that took "still the wave just fought" for "still running" and
            // reported a cleared wave as one with nothing left - the branch belongs on Phase, and the
            // number goes to 0 while nobody is being fought.
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
