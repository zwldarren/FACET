using System;

namespace Facet.Core
{
    /// <summary>
    /// Counters the machine behaviours bump. Telemetry, not state: it tells a player (and a test)
    /// whether a line is delivering, and nothing in the simulation reads it back.
    /// </summary>
    public sealed class SimTelemetry
    {
        /// <summary>Shots that have actually been fired this run.</summary>
        public int ShotsFired;
    }

    /// <summary>
    /// Everything a machine behaviour may read or touch in one tick: the fields it acts on, the content
    /// table it reads numbers from, and the step length. Passed by <c>in</c> alongside the machine's
    /// cell, so a behaviour is a pure function of state and its own outputs, never reaching for a global.
    /// This is the seam the content layer exists for: a new machine is a definition pointing at a behaviour.
    /// </summary>
    public readonly struct MachineTickContext
    {
        public readonly TileGrid Grid;
        public readonly TerrainField Terrain;
        public readonly BeltField Belts;
        public readonly MachineField Machines;
        public readonly EnemyField Enemies;
        public readonly ProjectileField Projectiles;
        public readonly ContentDatabase Content;
        public readonly SimTelemetry Telemetry;

        /// <summary>Where a behaviour reports what it did, for the view and the HUD. Derived telemetry -
        /// nothing in the simulation reads it back, so a behaviour may report freely.</summary>
        public readonly SimEventBuffer Events;

        /// <summary>Fixed step length, in seconds.</summary>
        public readonly float Dt;

        public MachineTickContext(TileGrid grid, TerrainField terrain, BeltField belts,
            MachineField machines, EnemyField enemies, ProjectileField projectiles,
            ContentDatabase content, SimTelemetry telemetry, SimEventBuffer events, float dt)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Belts = belts ?? throw new ArgumentNullException(nameof(belts));
            Machines = machines ?? throw new ArgumentNullException(nameof(machines));
            Enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            Projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            Dt = dt;
        }
    }

    /// <summary>One machine's per-tick work. Implemented once per <see cref="BehaviorKind"/> and selected
    /// by the machine's definition, so the tick dispatches on data and machines share behaviour code.</summary>
    public interface IMachineBehavior
    {
        /// <summary>Advance one machine by one tick. <paramref name="machine"/> is a live ref, so the
        /// behaviour updates cooldowns, cargo and aim in place.</summary>
        void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx);
    }
}
