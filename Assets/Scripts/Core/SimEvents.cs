using System;

namespace Facet.Core
{
    /// <summary>Why a placement was refused - the half of "no" a player needs, so a click that builds
    /// nothing says what stopped it instead of vanishing into the map.</summary>
    public enum RejectionReason : byte
    {
        /// <summary>The stockpile cannot cover the cost - the one refusal the player can fix by waiting
        /// for a delivery.</summary>
        NoFunds = 0,

        /// <summary>Something already stands on the tile: a belt, a machine, or the Core.</summary>
        Occupied = 1,

        /// <summary>Wrong ground: a drill needs a shape patch under it, every other machine bare
        /// ground.</summary>
        BadGround = 2,

        /// <summary>Outside the map.</summary>
        OffMap = 3,

        /// <summary>The tile is a wave's doorway: the map's tile, kept walkable, so a machine there
        /// would seal the wave in (<see cref="MachineField.CanPlace"/>).</summary>
        Doorway = 4,

        /// <summary>This map has not unlocked the operator the building is
        /// (<see cref="MachineDef.Requires"/>): the campaign's progression, said as a refusal.</summary>
        Locked = 5,
    }

    /// <summary>Everything the simulation reports having done. A closed set for the same reason
    /// <see cref="BehaviorKind"/> is: the interface between the simulation and everything that reacts to
    /// it - a sound, a flash, a log line, a scoreboard - and a dozen known members beat a plugin system
    /// nobody needs yet.</summary>
    public enum SimEventKind : byte
    {
        /// <summary>A building was placed and paid for. <c>Amount</c> is what it cost.</summary>
        Built = 0,

        /// <summary>A building was deleted and refunded. <c>Amount</c> is the refund.</summary>
        Removed = 1,

        /// <summary>A wrong-shape delivery jammed a belt segment. <c>Shape</c> is the shape that
        /// caused it.</summary>
        Jammed = 2,

        /// <summary>A jammed segment was cleared. The offending item is destroyed with it.</summary>
        JamCleared = 3,

        /// <summary>A circle reached the Core and became spendable. <c>Cell</c> is the delivering belt.
        /// </summary>
        Banked = 4,

        /// <summary>A turret fired. <c>Shape</c> is the ammunition it spent, <c>Amount</c> the damage.
        /// </summary>
        ShotFired = 5,

        /// <summary>A shot landed and did not kill. <c>EnemyId</c> and <c>Position</c> say what was hit.
        /// </summary>
        EnemyDamaged = 6,

        /// <summary>A shot landed and killed. The one event a run is won by.</summary>
        EnemyKilled = 7,

        /// <summary>An enemy hit the Core. <c>Amount</c> is the damage.</summary>
        CoreDamaged = 8,

        /// <summary>A wave began. <c>Amount</c> is its 1-based number.</summary>
        WaveStarted = 9,

        /// <summary>A wave was cleared. <c>Amount</c> is its 1-based number.</summary>
        WaveCleared = 10,

        /// <summary>The last wave was cleared and the Core still stands.</summary>
        RunWon = 11,

        /// <summary>The Core fell.</summary>
        RunLost = 12,

        /// <summary>An enemy hit a building. <c>Cell</c> is the building's, <c>Amount</c> the damage,
        /// <c>Building</c> which building it was.</summary>
        BuildingDamaged = 13,

        /// <summary>A building was destroyed by enemies. <c>Cell</c> is where it stood.</summary>
        BuildingDestroyed = 14,

        /// <summary>A placement the player asked for was refused. <c>Cell</c> is where it was aimed,
        /// <c>Building</c> what was being built, <c>Reason</c> why it did not happen, and
        /// <c>Amount</c> what it would have cost.</summary>
        PlacementRejected = 15,
    }

    /// <summary>One thing that happened, as a value: no references, no allocation, and enough identity
    /// to place it in the world (cell, position, enemy, shape, number) and in time (<see cref="Tick"/>).
    /// Events are <b>derived</b>: nothing in the simulation reads one back, so emitting them cannot change
    /// what it does, and every reaction can hang off this one seam without risking determinism.</summary>
    public readonly struct SimEvent
    {
        public readonly SimEventKind Kind;

        /// <summary>The tick it happened on, stamped by the buffer from the world's clock.</summary>
        public readonly int Tick;

        /// <summary>The cell it happened in, for a reader that thinks in tiles (the cursor, a log line).</summary>
        public readonly Int2 Cell;

        /// <summary>Where it happened, for a reader that thinks in world space (a flash, a shake).</summary>
        public readonly Vec2 Position;

        /// <summary>The enemy involved, by id, so a view can flash the one that was hit rather than its
        /// whole kind. 0 when no enemy is involved.</summary>
        public readonly int EnemyId;

        public readonly EnemyKind Enemy;

        /// <summary>The shape involved, where one is: the item a jam is made of, the round a shot was
        /// fired with, the full circle that banked. Nothing where no shape is.</summary>
        public readonly Shape Shape;

        /// <summary>The building involved, for the events about one
        /// (<see cref="SimEventKind.BuildingDamaged"/>, <see cref="SimEventKind.BuildingDestroyed"/>).
        /// <see cref="BuildKind.Belt"/> - the default - when none is involved; the event's
        /// <see cref="Cell"/> says which one it was.</summary>
        public readonly BuildKind Building;

        /// <summary>The event's number: a cost, a refund, a damage, a wave number.</summary>
        public readonly float Amount;

        /// <summary>Why a <see cref="SimEventKind.PlacementRejected"/> event happened. The default -
        /// <see cref="RejectionReason.NoFunds"/> - is meaningless for every other kind, exactly as
        /// <see cref="Building"/> is.</summary>
        public readonly RejectionReason Reason;

        internal SimEvent(SimEventKind kind, int tick, Int2 cell, Vec2 position, int enemyId,
            EnemyKind enemy, Shape shape, float amount, BuildKind building = default,
            RejectionReason reason = default)
        {
            Kind = kind;
            Tick = tick;
            Cell = cell;
            Position = position;
            EnemyId = enemyId;
            Enemy = enemy;
            Shape = shape;
            Amount = amount;
            Building = building;
            Reason = reason;
        }

        public override string ToString()
            => Kind + "@" + Tick + " cell=" + Cell + " amount=" + Amount.ToString("0.##");
    }

    /// <summary>The stream of <see cref="SimEvent"/>s, as a fixed ring of value types: emitting one is an
    /// array store, so a behaviour can report freely and the tick never allocates.
    ///
    /// It is a ring, so it always holds the most recent events, and a reader needs no bookkeeping - it
    /// reads the window it cares about, by <see cref="SimEvent.Tick"/>, and the present is guaranteed to be
    /// in there. <see cref="Clear"/> is for a reader that wants each event <em>exactly once</em> (the
    /// tests), an explicit read-side act rather than something <see cref="SimWorld.Tick"/> does. A stream
    /// that outgrows the ring drops its oldest, and <see cref="Dropped"/> counts how many - reported rather
    /// than swallowed, so a reader can tell "nothing happened" from "I fell behind". Emitters call the
    /// named methods, so what each event *means* is written once, where it is emitted.</summary>
    public sealed class SimEventBuffer
    {
        /// <summary>Room for many ticks' worth of events; a pitched wave produces only a handful per
        /// tick.</summary>
        public const int DefaultCapacity = 256;

        private readonly SimEvent[] _events;

        /// <summary>Valid events, oldest first from <see cref="_start"/>.</summary>
        private int _count;
        private int _start;

        /// <summary>The tick being simulated, stamped onto every event pushed. Set by
        /// <see cref="SimWorld.Tick"/> before that tick's steps run.</summary>
        public int Tick;

        public SimEventBuffer(int capacity = DefaultCapacity)
        {
            _events = new SimEvent[Math.Max(1, capacity)];
        }

        /// <summary>Events currently held, oldest first.</summary>
        public int Count => _count;

        /// <summary>How many events were overwritten before a reader saw them, because the stream
        /// outgrew the ring.</summary>
        public int Dropped { get; private set; }

        /// <summary>The tick the most recent overwrite happened on, or -1 when the ring never dropped
        /// anything. A window reader ("the last second") needs this and not just <see cref="Dropped"/>: a
        /// cumulative count says the stream once overflowed somewhere in the run, while the tick says
        /// whether that fall-behind touched <em>this</em> window.</summary>
        public int LastDropTick { get; private set; } = -1;

        /// <summary>Oldest first, so index 0 is the oldest event still held.</summary>
        public SimEvent this[int index] => _events[(_start + index) % _events.Length];

        public void Clear()
        {
            _count = 0;
            _start = 0;
            Dropped = 0;
            LastDropTick = -1;
        }

        /// <summary>How many buffered events are of one kind - the questions a view actually asks
        /// ("did anything bank, did anything get shot") without walking the buffer itself.</summary>
        public int CountOf(SimEventKind kind)
        {
            int found = 0;
            for (int i = 0; i < _count; i++)
                if (this[i].Kind == kind) found++;

            return found;
        }

        /// <summary>The most recent event of a kind, if there is one. A HUD ticker reads the last of
        /// each rather than the whole stream.</summary>
        public bool TryLast(SimEventKind kind, out SimEvent found)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (this[i].Kind != kind) continue;
                found = this[i];
                return true;
            }

            found = default;
            return false;
        }

        // ------------------------------------------------------------------ windows in time
        //
        // A reader asking "what happened in the last second" is asking about ticks, and only the buffer
        // knows the ring still reaches that far. These three state that rule once, next to Dropped and
        // LastDropTick they depend on.

        /// <summary>How many events of one kind happened on or after <paramref name="sinceTick"/>.
        /// The window question a ticker asks ("circles banked in the last second"), answered without
        /// the reader walking the ring.</summary>
        public int CountSince(SimEventKind kind, int sinceTick)
        {
            int found = 0;
            for (int i = 0; i < _count; i++)
                if (this[i].Kind == kind && this[i].Tick >= sinceTick) found++;

            return found;
        }

        /// <summary>The most recent event of a kind, if one happened on or after
        /// <paramref name="sinceTick"/>. The toast question ("is there a refusal still on screen"):
        /// an event older than the window is treated as if it were never there.</summary>
        public bool TryLastWithin(SimEventKind kind, int sinceTick, out SimEvent found)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                SimEvent e = this[i];
                if (e.Kind != kind || e.Tick < sinceTick) continue;
                found = e;
                return true;
            }

            found = default;
            return false;
        }

        /// <summary>Did the ring overwrite an event on or after <paramref name="sinceTick"/>? Then a
        /// window count may be quietly low, and an honest reader says so; a drop in the distant past does
        /// not touch the window being reported now. <see cref="LastDropTick"/>'s "never dropped" is -1, and
        /// a window opened in the run's first second reaches back past tick 0, so the sentinel is refused
        /// here rather than compared - otherwise the first frame would announce a full stream.</summary>
        public bool DroppedSince(int sinceTick) => LastDropTick >= 0 && LastDropTick >= sinceTick;

        // ------------------------------------------------------------------ what the simulation reports

        public void Built(Int2 cell, int cost)
            => Push(SimEventKind.Built, cell, amount: cost);

        public void Removed(Int2 cell, int refund)
            => Push(SimEventKind.Removed, cell, amount: refund);

        public void Jammed(Int2 cell, Shape item)
            => Push(SimEventKind.Jammed, cell, shape: item);

        public void JamCleared(Int2 cell)
            => Push(SimEventKind.JamCleared, cell);

        /// <summary>One circle reached the Core and became currency. A full circle is the only thing the
        /// Core banks - anything else it destroys - so the shape is the caller's invariant rather than a
        /// parameter: <see cref="CoreSinkSystem"/> guards on <c>Shape.Full(Quad.Circle)</c> before calling
        /// here, and a second banking path, or a change to what counts as currency, has to keep that
        /// guard or this event will report a circle for whatever was really banked.</summary>
        public void Banked(Int2 cell)
            => Push(SimEventKind.Banked, cell, amount: 1f, shape: Shape.Full(Quad.Circle));

        public void ShotFired(Int2 cell, Shape ammo, float damage)
            => Push(SimEventKind.ShotFired, cell, amount: damage, shape: ammo);

        public void EnemyDamaged(int id, EnemyKind kind, Vec2 position, float damage)
            => Push(SimEventKind.EnemyDamaged, amount: damage, position: position, enemyId: id, enemy: kind);

        public void EnemyKilled(int id, EnemyKind kind, Vec2 position)
            => Push(SimEventKind.EnemyKilled, position: position, enemyId: id, enemy: kind);

        public void CoreDamaged(float damage)
            => Push(SimEventKind.CoreDamaged, amount: damage);

        public void WaveStarted(int wave)
            => Push(SimEventKind.WaveStarted, amount: wave);

        public void WaveCleared(int wave)
            => Push(SimEventKind.WaveCleared, amount: wave);

        public void RunEnded(bool won)
            => Push(won ? SimEventKind.RunWon : SimEventKind.RunLost);

        /// <summary>An enemy hit a building. Cell is the building's, Amount the damage.</summary>
        public void BuildingDamaged(Int2 cell, BuildKind building, float damage)
            => Push(SimEventKind.BuildingDamaged, cell: cell, amount: damage, building: building);

        /// <summary>A building was destroyed by enemies. Cell is where it stood.</summary>
        public void BuildingDestroyed(Int2 cell, BuildKind building)
            => Push(SimEventKind.BuildingDestroyed, cell: cell, building: building);

        /// <summary>A placement was refused, and why: the answer to a click that built nothing.
        /// <paramref name="cost"/> is what the building would have cost.</summary>
        public void PlacementRejected(Int2 cell, BuildKind building, RejectionReason reason, int cost)
            => Push(SimEventKind.PlacementRejected, cell: cell, amount: cost, building: building, reason: reason);

        private void Push(SimEventKind kind, Int2 cell = default, float amount = 0f,
            Vec2 position = default, int enemyId = 0, EnemyKind enemy = default,
            Shape shape = default, BuildKind building = default,
            RejectionReason reason = default)
        {
            var e = new SimEvent(kind, Tick, cell, position, enemyId, enemy, shape, amount, building, reason);

            if (_count < _events.Length)
            {
                _events[(_start + _count) % _events.Length] = e;
                _count++;
                return;
            }

            // Full: overwrite the oldest, so a reader that never clears still sees the present rather
            // than a frozen past. Dropped counts the cost and LastDropTick says when it was paid.
            // Telemetry, not state - dropping is cheaper than growing an array on the hot path.
            _events[_start] = e;
            _start = (_start + 1) % _events.Length;
            Dropped++;
            LastDropTick = Tick;
        }
    }
}
