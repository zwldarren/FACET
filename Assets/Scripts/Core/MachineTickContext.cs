using System;

namespace Facet.Core
{
    /// <summary>
    /// Counters the machine behaviours bump while they run. Telemetry, not state: it is what tells a
    /// player (and a test) whether a line is delivering, as opposed to what it could deliver, and
    /// nothing in the simulation reads it back.
    /// </summary>
    public sealed class SimTelemetry
    {
        /// <summary>Shots that have actually been fired this run.</summary>
        public int ShotsFired;
    }

    /// <summary>
    /// Everything a machine behaviour may read or touch in one tick: the fields it acts on, the
    /// content table it reads its numbers from, and the step's own length. Passed by <c>in</c>, with
    /// the machine's cell passed alongside the machine itself, so a behaviour is a pure function of
    /// state plus its own outputs and never reaches for a global.
    ///
    /// This is the seam the whole content layer exists for: a new machine is a definition pointing at
    /// one of these behaviours, and a new mechanic is a new behaviour - neither has to edit a switch.
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

        /// <summary>Where a behaviour reports what it did, for the view and the HUD. Derived telemetry
        /// - nothing in the simulation reads it back, so a behaviour may report freely.</summary>
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

    /// <summary>
    /// One machine's per-tick work. Implemented once per <see cref="BehaviorKind"/> and selected by
    /// the machine's own definition, so the tick dispatches on data instead of a <c>switch</c> over
    /// machine kinds, and two machines sharing a behaviour share its code.
    /// </summary>
    public interface IMachineBehavior
    {
        /// <summary>Advance one machine by one tick. <paramref name="machine"/> is a live ref, so the
        /// behaviour updates cooldowns, cargo and aim in place.</summary>
        void Step(ref MachineState machine, Int2 cell, in MachineTickContext ctx);
    }
}
