using System;

namespace Facet.Core
{
    /// <summary>
    /// The machine half of the tick, in one pass. It walks the machine field's occupancy registry -
    /// not the whole grid - and dispatches each machine to the behaviour its definition names:
    ///
    ///   DrillBehavior      mines the patch under it
    ///   ConverterBehavior  runs a recipe (the decomposer is the shipped one)
    ///   PipeBehavior       carries an item across a tile
    ///   SplitterBehavior   balances whatever the belts around it deliver
    ///   SorterBehavior     routes the shape its definition filters for out the side it faces
    ///   TurretBehavior     eats its diet and fires it
    ///
    /// This replaced three separate whole-grid sweeps (production, transit, turrets). Machines only
    /// ever interact through belts, so visiting them in one pass in ascending map order is equivalent
    /// to the old grouping except in the rare "one machine pushes onto the exact cell another empties
    /// this tick" case - and map order is the more defensible of the two, being independent of the
    /// order the player happened to build in.
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

            // A content row asking for a behaviour that is not registered would build, sit, and do
            // nothing - silence, the one failure a player reads as a broken game. The world refuses to
            // start instead, and names the row and the behaviour. New behaviour: register it above,
            // and no call site has to remember this check exists.
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

    /// <summary>
    /// The drill's cooldown rule: count it down first, then report whether it fired. Decrementing
    /// first is not cosmetic - an interval of exactly one second is exactly 30 ticks at 30 Hz, and
    /// testing the cooldown <em>before</em> decrementing it spends the tick it reaches zero doing
    /// nothing, so the drill would mine at 31 ticks per item.
    /// </summary>
    internal static class MachineClock
    {
        public static bool Ticked(ref float cooldown, float dt)
        {
            cooldown = MathF.Max(0f, cooldown - dt);
            return cooldown <= 0f;
        }
    }
}
