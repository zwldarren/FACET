using System;

namespace Facet.Core
{
    /// <summary>
    /// The machine half of the tick, in one pass: walks the machine field's occupancy registry - not
    /// the whole grid - and dispatches each machine to the behaviour its definition names. Visiting in
    /// ascending map order is independent of build order, and equivalent to the old whole-grid sweeps
    /// except when one machine pushes onto the exact cell another empties this tick.
    /// </summary>
    public sealed class MachineSystem
    {
        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly BeltField _belts;
        private readonly MachineField _machines;
        private readonly EnemyField _enemies;
        private readonly ProjectileField _projectiles;
        private readonly ContentDatabase _content;

        /// <summary>The stream behaviours report on, handed to them through the tick context.</summary>
        private readonly SimEventBuffer _events;

        /// <summary>Behaviours by <see cref="BehaviorKind"/>. Built once; each is stateless, so one
        /// instance serves every machine of its kind.</summary>
        private readonly IMachineBehavior[] _byBehavior;

        /// <summary>Counters the behaviours bump. Read by the HUD and the tests.</summary>
        public SimTelemetry Telemetry { get; } = new SimTelemetry();

        /// <summary>Shots that have actually been fired this run.</summary>
        public int ShotsFired => Telemetry.ShotsFired;

        public MachineSystem(TileGrid grid, TerrainField terrain, BeltField belts, MachineField machines,
            EnemyField enemies, ProjectileField projectiles, ContentDatabase content, SimEventBuffer events)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            _byBehavior = new IMachineBehavior[Enum.GetValues(typeof(BehaviorKind)).Length];
            _byBehavior[(int)BehaviorKind.Drill] = new DrillBehavior();
            _byBehavior[(int)BehaviorKind.Converter] = new ConverterBehavior();
            _byBehavior[(int)BehaviorKind.Pipe] = new PipeBehavior();
            _byBehavior[(int)BehaviorKind.Splitter] = new SplitterBehavior();
            _byBehavior[(int)BehaviorKind.Sorter] = new SorterBehavior();
            _byBehavior[(int)BehaviorKind.Turret] = new TurretBehavior();

            // A content row asking for an unregistered behaviour would build and silently do nothing.
            // Refuse to start instead: register new behaviours above and no call site must remember this.
            var rows = content.Machines;
            for (int i = 0; i < rows.Count; i++)
            {
                MachineDef def = rows[i];
                if (def == null || def.Behavior == BehaviorKind.None) continue;

                if ((int)def.Behavior >= _byBehavior.Length || _byBehavior[(int)def.Behavior] == null)
                    throw new InvalidOperationException(
                        "content row \"" + def.Name + "\" (" + def.Id + ") asks for behaviour " +
                        def.Behavior + ", which MachineSystem does not implement - add it to the " +
                        "registry in this constructor.");
            }
        }

        public void Reset() => Telemetry.ShotsFired = 0;

        /// <summary>Step every machine once, in ascending map order.</summary>
        public void Step(float dt)
        {
            var ctx = new MachineTickContext(_grid, _terrain, _belts, _machines, _enemies, _projectiles,
                _content, Telemetry, _events, dt);

            // The registry only ever changes on placement, which the tick does not do, so walking it
            // by index while behaviours mutate the state it points at is safe.
            for (int k = 0; k < _machines.Occupied.Count; k++)
            {
                Int2 cell = _machines.CellAt(k);
                ref MachineState machine = ref _machines.RefAt(cell);

                BehaviorKind behavior = _content.Machine(machine.Build).Behavior;
                if (behavior == BehaviorKind.None) continue;

                _byBehavior[(int)behavior]?.Step(ref machine, cell, in ctx);
            }
        }
    }

    /// <summary>The drill's cooldown rule: count down first, then report whether it fired. Decrement
    /// before testing, or the tick that reaches zero is spent doing nothing and a one-second interval
    /// takes 31 ticks at 30 Hz instead of 30.</summary>
    internal static class MachineClock
    {
        public static bool Ticked(ref float cooldown, float dt)
        {
            cooldown = MathF.Max(0f, cooldown - dt);
            return cooldown <= 0f;
        }
    }
}
