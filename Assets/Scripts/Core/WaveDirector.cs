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
        /// the countdown ends. Every wait but a held one is one of these, and a held one becomes one the
        /// moment it is released.</summary>
        Intermission = 1,

        /// <summary>A wait the run is holding back, with no time passing at all. The opening is held until
        /// the player's first building (<see cref="WaveDirector.ArmFirstWave"/>), so reading the map and
        /// planning cost nothing; a level that scripts its own opening holds <em>every</em> wait until its
        /// tutorial lets it go (<see cref="WaveDirector.HoldEveryWait"/>,
        /// <see cref="WaveDirector.ReleaseWait"/>), so its waves arrive when the player has been taught what
        /// they demand rather than when a countdown says so. <see cref="WaveDirector.StartNextWave"/> ends a
        /// non-scripted wait with no building placed.</summary>
        Held = 2,

        /// <summary>Every wave this run has has been fought. The run's own end is
        /// <see cref="GameStatus"/>'s; this is the clock's answer, and the clock keeps no score.</summary>
        Over = 3,
    }

    /// <summary>
    /// The wave clock: which wave is running, what is left of it, and how long the intermission has to go.
    /// Every wave - the first included - starts itself when its intermission runs out, and
    /// <see cref="StartNextWave"/> starts one early, so a player ready sooner can choose "now".
    ///
    /// <b>Where a wave comes from is the map's decision.</b> A map's authored table is played first, and
    /// then the <see cref="WaveGenerator"/> takes over if the map asked for generated waves
    /// (<see cref="MapDefinition.GeneratedWaves"/>): a scripted opening followed by a procedurally
    /// generated run, or - a map that authors nothing - a generated run from wave 1. Endless mode is the
    /// same mechanism with no bound, which is why it needed no separate mode.
    ///
    /// Lookups are by wave <em>number</em> and pure, so the briefing can show the wave after next without
    /// disturbing the stream the running wave came from. The generator's answers are memoised, because a
    /// HUD asks for the preview every frame.
    ///
    /// The opening is the one wait that does not begin with the run: it is held until the player's first
    /// building, so studying the map costs no time. Its countdown is the longest on the table, because the
    /// whole opening is built under it. It is a ceiling on the wait, not a floor: a player done early calls
    /// the wave with `N`.
    ///
    /// A level that teaches has a different arrangement, and it is the only other one: under
    /// <see cref="HoldEveryWait"/> the clock is the tutorial's (<see cref="TutorialState"/>), and no wait
    /// counts down at all - it is held until a line of the lesson is ticked, and that tick <em>is</em> the
    /// wave's start. The lesson is the announcement, so a taught run is paced by what the player has been
    /// taught and not by seconds; the wave's own intermission is the pause of the waits the clock arms itself
    /// (the opening of a map with no lesson, and the waits after a lesson is over). `N` is refused while such
    /// a wait is held, because a gate that a key can bypass is a gate the second wave does not have.
    ///
    /// It owns spawning and the definition of "this wave is over"; whether that wins the run is the
    /// world's business (<see cref="GameStatus"/>).
    /// </summary>
    public sealed class WaveDirector
    {
        /// <summary>Room for every group of a wave. The table is fixed; this is only a bound - and it is
        /// public because the generator packs its tail into the groups it already has against this same
        /// number, so a wave can never be built past what the timers below can hold.</summary>
        public const int MaxGroups = 16;

        private readonly EnemyField _enemies;
        private readonly MapDefinition _map;

        /// <summary>Where a new run's seed comes from - the host's injection of §5's "one seed per run".
        /// Null plays the map's authored seed: the engine-free default, and what a map that pins its seed
        /// for a reproducible demo asks for.</summary>
        private readonly Func<int> _freshSeed;

        /// <summary>The run's seed, drawn by <see cref="Reset"/>. The generator's only input, and what the
        /// HUD shows so a run is reproducible from the number on the screen.</summary>
        private int _seed;

        /// <summary>The stream waves are announced on, so a view can react to a wave starting or being
        /// cleared without polling the director's state for edges.</summary>
        private readonly SimEventBuffer _events;

        private readonly float[] _groupTimer = new float[MaxGroups];
        private readonly int[] _groupSpawned = new int[MaxGroups];

        /// <summary>The generator, built on first use: a map that plays only its authored table never pays
        /// for the pool walk.</summary>
        private WaveGenerator _generator;

        /// <summary>Memo for <see cref="Definition"/>, two slots deep because the briefing asks for the
        /// next wave and the one after it on every frame. Two is enough because of the invariant
        /// <see cref="NextWave"/> states: while a wave runs, it still names that wave, so the clock bar's
        /// lookup of the running wave collapses onto the same two numbers. Three live numbers at once would
        /// make every lookup miss and rebuild the generator's answer several times a frame.</summary>
        private readonly int[] _memoNumber = { 0, 0 };
        private readonly WaveDefinition[] _memoWave = new WaveDefinition[2];

        private int _waveIndex = -1;     // 0-based index of the wave being fought, -1 during intermission

        /// <summary>The countdown is kept in ticks, not seconds. Subtracting a float tick from a float
        /// remaining value drifts: after exactly 1800 subtractions of 1/30 s the residue can still be
        /// positive, so a 60 s intermission would end on tick 1801 or 1799 depending on rounding. An integer
        /// countdown makes "60 seconds" exactly 1800 ticks, every run.</summary>
        private int _intermissionTicks;

        /// <summary>The wait's clock is not armed yet: it waits for whoever releases it rather than for
        /// time, and no tick passes while it is held. The opening is held from <see cref="Reset"/> and
        /// released by the player's first building (<see cref="ArmFirstWave"/>); a run with
        /// <see cref="HoldEveryWait"/> set is held again at every wait and released by
        /// <see cref="ReleaseWait"/>. Never set again after it is spent on a run that holds only the
        /// opening.</summary>
        private bool _waitHeld;

        /// <summary>A release the lesson granted while no wait was held. A line can be satisfied in the middle
        /// of a wave - the cut a later wave is weak to is often made during the one before it - and there is
        /// then no wait to un-hold and nothing to count down. The grant is banked here and consumed by the wait
        /// that follows the wave, so the cue calls the wave it was about instead of being swallowed by the
        /// clearing. Counted rather than flagged because two lines can cue while one wave is being fought, and
        /// each cue is worth one wait.</summary>
        private int _releaseGrants;

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
                return _waitHeld ? WavePhase.Held : WavePhase.Intermission;
            }
        }

        /// <summary>Is there a wave the player can start right now? The question the N key, the HUD button
        /// and a test's "run to the end" all ask - the opening included, held or already counting down.
        /// A wait a tutorial is holding back is not startable: the lesson is what lets it go, and N must not
        /// cut it short. Starting is still a no-op while <see cref="WavePhase.Running"/> or
        /// <see cref="WavePhase.Over"/>, but readers should not offer what they would have to swallow.</summary>
        public bool HasWaveToStart
            => Phase == WavePhase.Intermission || (Phase == WavePhase.Held && !HoldEveryWait);

        /// <summary>Does this run hold <b>every</b> wait, not just the opening? Set by the world from
        /// <see cref="TutorialState.GatesWaves"/>: a level with a tutorial left to teach holds each wave back
        /// until a line of it is ticked, so no countdown can arrive before the player has been told what the
        /// wave will ask for. False for every ordinary map, whose waits are the opening's alone.</summary>
        public bool HoldEveryWait { get; set; }

        /// <summary>1-based number of the wave being fought, or 0 during an intermission. For printing
        /// the number; for *branching*, read <see cref="Phase"/>.</summary>
        public int CurrentWave { get; private set; }

        /// <summary>1-based number of the wave the intermission is counting down to, 0 when none is left.
        /// <b>While a wave runs this still names that wave</b> - it is the number the clock is pointed at,
        /// and a running wave is what it is pointed at - which is why the briefing's three per-frame
        /// lookups (<see cref="Definition"/> of the running wave and of the two ahead) are only ever two
        /// distinct numbers, and why <see cref="Definition"/>'s memo can be two slots deep.</summary>
        public int NextWave { get; private set; }

        /// <summary>Seconds until the next wave starts by itself: 0 while a wave runs, and 0 once the
        /// run's waves are exhausted. While a wait is <see cref="WavePhase.Held"/> this is the countdown
        /// that will run once the first building arms it, not one already running - <see cref="Phase"/> says
        /// which of the two it is.</summary>
        public float IntermissionRemaining { get; private set; }

        /// <summary>What that countdown starts at, so a HUD can draw it as a fraction. 0 when there is no
        /// countdown at all: while a wave runs, or once the run is out of waves. Held for the first
        /// building, it is the whole countdown waiting to be armed.</summary>
        public float IntermissionTotal { get; private set; }

        /// <summary>True once every wave this run has has been fought and cleared.</summary>
        public bool Finished { get; private set; }

        /// <summary>The wave table this director runs: the map's own data, so a different map is a
        /// different campaign without a code change here.</summary>
        private WaveDefinition[] Table => _map.Waves;

        /// <summary>How many authored waves the map has, before the generator takes over.</summary>
        public int AuthoredCount => Table.Length;

        /// <summary>Is this run endless - the generator past the last map with no bound (§5)? Then
        /// <see cref="TotalWaves"/> is 0 and the HUD prints the wave number alone.</summary>
        public bool Endless => _map.GeneratedWaves < 0;

        /// <summary>How many waves the run has, or 0 for an endless one.</summary>
        public int TotalWaves => Endless ? 0 : Table.Length + Math.Max(0, _map.GeneratedWaves);

        /// <summary>The run's seed: the generator's only input, and what the HUD shows so a run is
        /// reproducible from the number on the screen. Drawn once per run - construction and every
        /// restart - from the source the host injected, or the map's authored seed when there is none.</summary>
        public int Seed => _seed;

        public WaveDirector(EnemyField enemies, MapDefinition map, SimEventBuffer events,
            Func<int> freshSeed = null)
        {
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _freshSeed = freshSeed;
            Reset();
        }

        /// <summary>Enemies of the running wave that have not spawned yet.</summary>
        public int PendingInWave
        {
            get
            {
                if (_waveIndex < 0) return 0;

                WaveDefinition wave = Definition(_waveIndex + 1);
                if (wave == null) return 0;

                int pending = 0;
                for (int g = 0; g < wave.Groups.Length && g < MaxGroups; g++)
                    pending += Math.Max(0, wave.Groups[g].Count - _groupSpawned[g]);

                return pending;
            }
        }

        /// <summary>The definition of the wave the intermission is waiting on, or null while one runs or
        /// once the run is out of waves.</summary>
        public WaveDefinition NextWaveDefinition => NextWave > 0 ? Definition(NextWave) : null;

        /// <summary>The wave after that one, so the briefing can show both (§5: preview the next wave and
        /// the one after). Null when the run has only one wave left.</summary>
        public WaveDefinition WaveAfterNextDefinition
        {
            get
            {
                if (NextWave <= 0) return null;
                if (!Endless && NextWave + 1 > TotalWaves) return null;

                return Definition(NextWave + 1);
            }
        }

        /// <summary>
        /// The wave numbered <paramref name="number"/> (1-based): the map's authored row, or - past the
        /// table - the generator's. Pure and memoised, so the preview and the spawner cannot disagree and a
        /// preview does not advance anything. Null for a number this run does not have.
        /// </summary>
        public WaveDefinition Definition(int number)
        {
            if (number < 1) return null;

            if (number <= Table.Length) return Table[number - 1];
            if (!_map.GeneratesWaves) return null;

            int generated = number - Table.Length;
            if (!Endless && generated > _map.GeneratedWaves) return null;

            for (int i = 0; i < _memoNumber.Length; i++)
                if (_memoNumber[i] == number && _memoWave[i] != null) return _memoWave[i];

            // The generator is asked by the *run's* wave number, not by how many waves it has produced, and
            // it is told where the table handed over: a map that scripts its opening keeps climbing the
            // budget curve and the depth bands from where its own table left off - the finale's budget is
            // the generated curve's base and its deepest demand the band's floor - instead of dropping back
            // to a first wave's worth of pressure and shallows. For a map that authors nothing the two
            // numbers agree and the from-scratch curve applies.
            WaveDefinition wave = Generator().Wave(number);
            _memoNumber[number % _memoNumber.Length] = number;
            _memoWave[number % _memoNumber.Length] = wave;
            return wave;
        }

        public void Reset()
        {
            // The seed is the run's, not the clock's: a restarted run is a new run, so a source draws
            // again here, and a run without one plays the map's authored seed.
            _seed = _freshSeed != null ? _freshSeed() : _map.Seed;

            _waveIndex = -1;
            CurrentWave = 0;
            NextWave = HasWaves ? 1 : 0;

            // A table with nothing to fight is NOT a finished run: `Finished` means "every wave this run has
            // was fought and cleared", and it is set by clearing the last one. A map with no waves and no
            // generator therefore never ends - which is what an unplayable map deserves (MapValidation names
            // it `NoWaves`, and the Editor refuses to author it), and what the whole engine-free test rig
            // leans on, since its maps carry no waves at all. Marking it finished here won the run on tick 1
            // and froze the tick loop with it, so nothing advanced at all.
            Finished = false;

            // Cues granted by the lesson that just ended are the old run's and do not survive it - dropped
            // before the opening is armed, so a grant left over cannot start the new run's first wave on its
            // first tick.
            _releaseGrants = 0;
            BeginIntermission(NextWave);
            Array.Clear(_groupTimer, 0, _groupTimer.Length);
            Array.Clear(_groupSpawned, 0, _groupSpawned.Length);
            Array.Clear(_memoNumber, 0, _memoNumber.Length);
            Array.Clear(_memoWave, 0, _memoWave.Length);

            // A restart puts the opening back in the player's hands: the clock is armed by a building
            // again, so a restarted run is not already late before the first thing is laid.
            _waitHeld = true;
        }

        /// <summary>Is there a first wave at all? A map with no authored waves and no generator is a map
        /// error the validator names; this is the clock's own answer to it.</summary>
        private bool HasWaves => Table.Length > 0 || _map.GeneratesWaves;

        /// <summary>Arm the wait: the first wave's countdown starts now, because the player has begun to
        /// build. Called by the world on the tick a run's first building lands, and idempotent, so the world
        /// may say it every tick after. A run where the player never builds never arms it - the point, since
        /// reading the map costs no time. Calling the wave early (<see cref="StartNextWave"/>) ends the wait
        /// without arming it.
        ///
        /// Building is <b>not</b> what releases a wait on a run that <see cref="HoldEveryWait"/>: there the
        /// tutorial's lines are the only cue, and a belt laid on the third tick is not a lesson learned.</summary>
        public void ArmFirstWave()
        {
            if (HoldEveryWait) return;

            _waitHeld = false;
        }

        /// <summary>Release a wait the run is holding back. The tutorial's verb: a line that
        /// <see cref="TutorialTask.StartsWave"/> calls this, and the wave it names starts on that tick - the
        /// lesson's cue is the wave's announcement, so the clock has nothing left to announce and no countdown
        /// follows it. A taught run is paced by lessons rather than seconds: the player has just been told what
        /// the wave asks for, and a pause between the sentence and the enemies it warned about is dead time.
        /// (The wave's own <see cref="WaveDefinition.Intermission"/> is the pause of a wait the clock arms
        /// itself - the opening on an ordinary map, and the waits of a run whose lesson is over.)
        ///
        /// A line can tick while a wave is still being fought, so the verb does not require a held wait to
        /// exist right now: the grant is banked and the wave that follows the clearing starts with it
        /// (<see cref="BeginIntermission"/>).</summary>
        public void ReleaseWait()
        {
            if (_waitHeld)
            {
                // A wait is held right now: the cue is this wave's, and the wave is due here rather than
                // after a countdown the lesson has just made pointless. A held wait on a map that has no
                // wave at all (a broken map the validator names) is only released.
                _waitHeld = false;
                if (NextWave > 0) StartWave(NextWave - 1);
                return;
            }

            // Nothing held: the line's wave is not waiting yet. A wave in flight clears into the next wait on
            // its own, and that wait is the one the line was about.
            if (_waveIndex >= 0) _releaseGrants++;
        }

        /// <summary>Arm the wait before the wave numbered <paramref name="waveNumber"/> (0 when there is
        /// none): the countdown every wave runs, the first included, so "this wave is coming" is one rule
        /// and its length is the wave's own number rather than a special case. The opening's wait begins
        /// ticking when the first building arms it (<see cref="ArmFirstWave"/>); every other one begins the
        /// moment it is armed here.</summary>
        private void BeginIntermission(int waveNumber)
        {
            WaveDefinition wave = waveNumber > 0 ? Definition(waveNumber) : null;
            IntermissionTotal = wave?.Intermission ?? 0f;
            _intermissionTicks = SecondsToTicks(IntermissionTotal);
            IntermissionRemaining = IntermissionTotal;

            if (!HoldEveryWait)
            {
                // A run that holds only its opening has nothing for a banked grant to release.
                _releaseGrants = 0;
                return;
            }

            // Every wait on a scripted run starts held, so the countdown that follows a wave is the lesson's
            // to start rather than the clearing's - unless a line already granted this wait while the wave
            // before it was still being fought (<see cref="ReleaseWait"/>). Then the cue is this wave's start
            // and there is nothing left to count down: the wave begins as the one before it ends rather than
            // after a pause nobody asked for.
            if (_releaseGrants > 0)
            {
                _releaseGrants--;
                StartWave(waveNumber - 1);
                return;
            }

            _waitHeld = true;
        }

        public void Step(float dt)
        {
            if (Finished) return;

            if (_waveIndex < 0)
            {
                if (NextWave == 0) return;

                // A held wait lets no tick pass: the countdown a map declares is spent building (or being
                // taught), not reading the clock.
                if (_waitHeld) return;

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

            // A tutorial's hold is the lesson's to release, so N cannot cut a teaching gate short. Every other
            // held wait is the opening's, and skipping it is the point: a player ready sooner chooses "now".
            if (_waitHeld && HoldEveryWait) return;

            StartWave(NextWave - 1);
        }

        private void StartWave(int index)
        {
            WaveDefinition wave = Definition(index + 1);
            if (wave == null)
            {
                // No such wave: the run is over. Reachable only for a table this director was not told
                // about, but ending the run is the honest answer rather than spawning nothing forever -
                // and it leaves the clock in the same state the normal end does, so a reader that asks
                // the countdown directly rather than through Phase sees a finished run, not a stale one.
                Finished = true;
                NextWave = 0;
                _waitHeld = false;
                IntermissionRemaining = 0f;
                IntermissionTotal = 0f;
                return;
            }

            // However the wait ended - a countdown running out, or the player calling it - the wait is over,
            // so the bit that held it is spent here rather than by the path that got here.
            _waitHeld = false;

            _waveIndex = index;
            CurrentWave = index + 1;
            IntermissionRemaining = 0f;
            IntermissionTotal = 0f;

            for (int g = 0; g < wave.Groups.Length && g < MaxGroups; g++)
            {
                _groupTimer[g] = wave.Groups[g].FirstDelay;
                _groupSpawned[g] = 0;
            }

            _events.WaveStarted(CurrentWave);
        }

        private void Spawn(float dt)
        {
            WaveDefinition wave = Definition(_waveIndex + 1);
            if (wave == null) return;

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
                    _enemies.Spawn(group.Kind, _map.SpawnCenter(group.SpawnPoint), group.Weakness);
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

            // A run with no bound - endless mode - never runs out: the generator simply keeps answering.
            if (!Endless && cleared >= TotalWaves)
            {
                Finished = true;
                NextWave = 0;
                return;
            }

            NextWave = cleared + 1;
            BeginIntermission(NextWave);
        }

        /// <summary>The generator for this map, built once. The pool is the closure of the map's own
        /// minerals under its own unlocked operators, so every demand it can draw is buildable here; the
        /// three handover numbers are read off the authored table itself, so the generator never keeps a
        /// copy of the waves it sits behind.</summary>
        private WaveGenerator Generator()
        {
            if (_generator != null) return _generator;

            ShapePool pool = _map.Pool();
            _generator = new WaveGenerator(_seed, pool, _map.SpawnPoints.Length,
                Table.Length, AuthoredEndBudget(), AuthoredEndDepth(pool));
            return _generator;
        }

        /// <summary>The budget the authored table's last wave spends, in the generator's own cost units -
        /// the point its curve hands over at, so the wave after the table carries the pressure the table
        /// ended on. 0 with no table, which is the generator's from-scratch curve's own business.</summary>
        private float AuthoredEndBudget()
        {
            if (Table.Length == 0) return 0f;

            WaveDefinition last = Table[Table.Length - 1];
            float budget = 0f;
            for (int g = 0; g < last.Groups.Length; g++)
                budget += last.Groups[g].Count * (last.Groups[g].Kind == EnemyKind.Runner
                    ? WaveGenerator.RunnerCost
                    : WaveGenerator.TankCost);

            return budget;
        }

        /// <summary>The deepest operator depth the authored table ever demanded - the floor of the
        /// generated stretch's depth band, so the generator never asks for shallower shapes than the
        /// table itself taught. 0 with no table.</summary>
        private int AuthoredEndDepth(ShapePool pool)
        {
            if (Table.Length == 0) return 0;

            int deepest = 0;
            for (int w = 0; w < Table.Length; w++)
                for (int g = 0; g < Table[w].Groups.Length; g++)
                    deepest = Math.Max(deepest, Math.Max(0, pool.Depth(Table[w].Groups[g].Weakness)));

            return deepest;
        }
    }
}
