using System;

namespace Facet.Core
{
    /// <summary>
    /// The whole logic layer. Deterministic, engine-free, advanced exactly once per fixed tick by the
    /// host; nothing here knows about Unity, DeltaTime or frame rates.
    ///
    /// The tick order is the design's causal order - each step exists because the next needs its
    /// result: build commands, belts, machines, core sink, projectiles, path field, enemies, waves.
    ///
    /// <see cref="Events"/> is filled alongside all eight and never read back - it is derived, so the
    /// stream cannot change what a tick does, and reactions happen through it instead of by polling
    /// for edges.
    ///
    /// The seam: <b>the fields are the interface to the modules; the methods are only for what has no
    /// field to cross to.</b> Module verbs live on the module
    /// (<c>world.Belts.TrySpawnItem(...)</c>, <c>world.Waves.StartNextWave()</c>); what stays here
    /// forwards to deliberately private state (<see cref="_build"/>, <see cref="_machineSystem"/>)
    /// or lands on a tick boundary on the world's own clock (<see cref="RequestNextWave"/>,
    /// <see cref="RequestRestart"/>).
    /// </summary>
    public sealed class SimWorld
    {
        public readonly TileGrid TileGrid;
        public readonly TerrainField Terrain;
        public readonly BeltField Belts;
        public readonly MachineField Machines;
        public readonly EnemyField Enemies;
        public readonly ProjectileField Projectiles;
        public readonly WaveDirector Waves;
        public readonly SimConfig Config;
        public readonly EconomyState Economy;

        /// <summary>The content this run reads: every cost, machine stat, gun number and enemy profile.
        /// Injected rather than read from a static, so a test or a replay can run a different table and
        /// stay reproducible.</summary>
        public readonly ContentDatabase Content;

        /// <summary>
        /// What the simulation reported doing this run: built, removed, jammed, banked, shot, killed,
        /// damaged, and the wave and run endings. Filled by the tick, read by any reactor.
        ///
        /// <b>The simulation appends; the reader clears.</b> See <see cref="SimEventBuffer"/> for why
        /// that is the reader's job, not the tick's.
        /// </summary>
        public readonly SimEventBuffer Events = new SimEventBuffer();

        /// <summary>The map this run is played on. Fixed for the world's lifetime.</summary>
        public MapDefinition Map { get; }

        /// <summary>Ticks elapsed since the run started. The clock everything else reads.</summary>
        public int TickCount { get; private set; }

        /// <summary>Paused worlds do not advance and do not accept build commands.</summary>
        public bool Paused;

        /// <summary>Playing until the Core falls or the last wave is cleared.</summary>
        public GameStatus Status { get; private set; }

        /// <summary>The defended object, and the bank. Losing it loses the run.</summary>
        public CoreState Core;

        /// <summary>The building the player has selected, or null for the inspect cursor: nothing
        /// selected, so the left button reads the map instead of building on it.</summary>
        public BuildKind? SelectedKind => _build.Selected;

        /// <summary>The building the player last read - the tile the HUD's info card describes while
        /// nothing is selected. Out of bounds when nothing is pinned.</summary>
        public Int2 InspectedCell => _build.InspectedCell;

        /// <summary>Is there something at this cell worth reading? The cursor, the click and the info
        /// card all read this one answer.</summary>
        public bool CanInspect(Int2 cell) => _build.CanInspect(cell);

        /// <summary>Which way the ghost under the cursor faces, so the view can draw the same facing the
        /// simulation would use: the run's own direction while a belt is being dragged, the placement
        /// ghost's otherwise.</summary>
        public Dir PlacementDirection => _build.PlacementDirection;

        private readonly MachineSystem _machineSystem;
        private readonly CoreSinkSystem _sink;
        private readonly BuildController _build;

        /// <summary>The walkers' way to the Core, rebuilt in the tick whenever the machines' solidity
        /// changed (a placement, a removal, a building destroyed by enemies, a restart).</summary>
        private readonly PathField _paths;

        /// <summary>A start-wave request that has not been applied yet. Set between ticks, consumed
        /// by <see cref="Tick"/>.</summary>
        private bool _waveRequested;

        /// <summary>A restart request from the HUD's button, applied on the next tick so a click lands
        /// on a tick boundary like every other input instead of mutating the run mid-frame.</summary>
        private bool _restartRequested;

        public SimWorld(MapDefinition map, SimConfig config, ContentDatabase content = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Content = content ?? ContentDatabase.Default;

            TileGrid = new TileGrid(map.Width, map.Height);
            Terrain = new TerrainField(TileGrid);
            Belts = new BeltField(TileGrid, Terrain, Events);
            Machines = new MachineField(TileGrid, Terrain, Belts, Content, Events, Map);
            _paths = new PathField(TileGrid, Terrain, Machines, Content);
            Enemies = new EnemyField(Content, Events, TileGrid, Terrain, Machines, _paths);
            Projectiles = new ProjectileField();
            Waves = new WaveDirector(Enemies, map, Events, Config.FreshSeed);
            Economy = new EconomyState(Content, map.StartCircles);

            _machineSystem = new MachineSystem(TileGrid, Terrain, Belts, Machines, Enemies, Projectiles,
                Content, Events);
            _sink = new CoreSinkSystem(TileGrid, Belts, Economy, Events);
            _build = new BuildController(TileGrid, Terrain, map, Belts, Machines, Economy, Content, Events);

            // The Core first, then the ground: a map's terrain may not bury the thing the player
            // defends, so the Core's tiles are already claimed by the time the ground is stamped.
            PlaceCore(Map.CoreCell);
            Terrain.Stamp(map);
            Status = GameStatus.Playing;
        }

        /// <summary>Advance one fixed step. Call at exactly SimConfig.TickRate Hz.</summary>
        public void Tick(InputCommand cmd)
        {
            // Stamp the stream before anything can report: every event this tick carries this tick's
            // number, which is what lets a reader place it in time after several catch-up ticks.
            Events.Tick = TickCount;

            // Restart is honoured even while paused or after the run ended: it is the only way back.
            // The HUD's button goes through RequestRestart, for the same reason the start-wave button
            // goes through RequestNextWave - a click happens between ticks and this world knows the order.
            if (cmd.RestartPressed || _restartRequested)
            {
                _restartRequested = false;
                Restart();
                return;
            }

            if (Paused || Status != GameStatus.Playing)
            {
                // A request that arrives while the world is stopped is dropped, not queued: pausing
                // and clicking the button in the same breath must not start a wave on resume.
                _waveRequested = false;

                // Selection, the inspect pin, the ghost's facing and the sorter's filter are not world
                // changes - the HUD's build bar keeps responding while the run is held, and a paused
                // player can read their own base and re-aim a line. They come through the same command
                // as a number key.
                _build.ApplySelection(cmd);
                return;
            }

            _build.Apply(cmd);

            // The opening countdown starts on the tick the player's first building lands, so planning
            // costs nothing and building costs time. Said every tick once they have built - the clock
            // decides that only the first one means anything.
            if (_build.HasBuilt) Waves.ArmFirstWave();

            Belts.Step(SimConfig.TickDt, Config.BeltSpeed);
            _machineSystem.Step(SimConfig.TickDt);
            _sink.Step(Core.Cell);
            Projectiles.Step(SimConfig.TickDt, Enemies);
            _paths.Sync();
            Enemies.Step(SimConfig.TickDt, ref Core);
            Waves.Step(SimConfig.TickDt);

            // The N key and the HUD's button, applied *after* the wave step: step 8 ends a wave on the
            // tick its last enemy dies, so a request in that same tick would find a wave still running
            // and be swallowed. Here it starts the intermission step 8 just opened; a running wave or a
            // finished run ignores it, so this stays "start it now, only once".
            if (cmd.StartWavePressed || _waveRequested)
            {
                _waveRequested = false;
                Waves.StartNextWave();
            }

            if (!Core.Alive)
            {
                Status = GameStatus.Lost;
                Events.RunEnded(won: false);
            }
            else if (Waves.Finished)
            {
                Status = GameStatus.Won;
                Events.RunEnded(won: true);
            }

            TickCount++;
        }

        /// <summary>Wipe the run and rebuild it from the map's own data, keeping this object alive so
        /// every view that holds a reference to the world keeps working.</summary>
        public void Restart()
        {
            TileGrid.Clear();
            Terrain.Clear();

            Machines.Clear();
            Belts.Clear();
            Enemies.Clear();
            Projectiles.Clear();

            PlaceCore(Map.CoreCell);
            Terrain.Stamp(Map);

            Waves.Reset();
            _machineSystem.Reset();
            _build.Reset();
            Economy.Reset(Map.StartCircles);

            Paused = false;
            Status = GameStatus.Playing;
            TickCount = 0;
            _waveRequested = false;
            _restartRequested = false;
            Events.Tick = 0;
            Events.Clear();
        }

        /// <summary>Ask for the next wave on the next tick. The HUD's button calls this because a click
        /// happens between ticks and only this world knows the tick order. A test that means to start a
        /// wave writes <c>world.Waves.StartNextWave()</c> on the clock directly.</summary>
        public void RequestNextWave() => _waveRequested = true;

        /// <summary>Ask for the run to restart on the next tick. The HUD's restart button calls this;
        /// the R key still goes through <see cref="InputCommand.RestartPressed"/>.</summary>
        public void RequestRestart() => _restartRequested = true;

        public bool CanPlace(BuildKind kind, Int2 cell) => _build.CanPlace(kind, cell);

        /// <summary>Has this run unlocked the operator that building needs? The build bar greys the rest
        /// out; the campaign's progression is this set and nothing else.</summary>
        public bool IsUnlocked(BuildKind kind) => _build.IsUnlocked(kind);

        /// <summary>Re-route the sorter standing on a cell - the picker's verb. False when there is no
        /// sorter there or the filter would route nothing.</summary>
        public bool SetFilter(Int2 cell, ShapeFilter filter) => _build.SetFilter(cell, filter);

        public bool TryPlace(BuildKind kind, Int2 cell, Dir direction) => _build.TryPlace(kind, cell, direction);

        /// <summary>Clear a jam or remove a building (refunding it), the way right-click does - the
        /// player's verb. The no-refund low-level removal lives on <see cref="Belts"/>.</summary>
        public bool TryRemoveBuilding(Int2 cell) => _build.Remove(cell);

        public bool CanPlaceBelt(Int2 cell) => _build.CanPlace(BuildKind.Belt, cell);

        public bool TryPlaceBelt(Int2 cell, Dir direction) => _build.TryPlace(BuildKind.Belt, cell, direction);

        /// <summary>Shots fired this run. The machine system is private state, so this read has no
        /// field to cross to - the one telemetry the world owns and hands out numbered.</summary>
        public int ShotsFired => _machineSystem.ShotsFired;

        private void PlaceCore(Int2 cell)
        {
            // The whole footprint is Core tiles, so nothing can be built on or under it.
            for (int y = 0; y < CoreState.Size; y++)
            {
                for (int x = 0; x < CoreState.Size; x++)
                {
                    TileGrid.Set(new Int2(cell.X + x, cell.Y + y), TileKind.Core);
                }
            }

            Core = new CoreState { Cell = cell, Hp = Config.CoreMaxHp, MaxHp = Config.CoreMaxHp };
        }
    }
}
