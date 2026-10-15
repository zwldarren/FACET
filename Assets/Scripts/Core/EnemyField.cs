using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One live enemy. Ids are stable for the enemy's whole life: shots in flight track them.</summary>
    public struct EnemyState
    {
        public int Id;
        public EnemyKind Kind;

        /// <summary>What kills it, assigned by the wave at spawn rather than by its kind: the generator
        /// draws a demand per group, so the same archetype asks a different question every wave.</summary>
        public Shape Weakness;
        public float Hp;
        public float MaxHp;
        public Vec2 Position;
        public Vec2 PreviousPosition;
        public float AttackCooldown;

        /// <summary>True once it has stopped to hit something - the Core, or a machine in reach.</summary>
        public bool Attacking;

        /// <summary>The walk this one chose when it spawned: true to flank (follow the threat-weighted
        /// field), false to overrun (the plain shortest way). The choice is the whole route weighed once at
        /// the door - <see cref="PathField.FlankAt"/> - and kept for the enemy's life, so a field rebuilt
        /// under a walking wave never dithers it; the wave's next spawn weighs the new ground instead.</summary>
        public bool AvoidsFire;
    }

    /// <summary>One enemy, flattened for the view layer.</summary>
    public struct EnemySnapshot
    {
        public int Id;
        public EnemyKind Kind;
        public Shape Weakness;
        public Vec2 PreviousPosition;
        public Vec2 Position;
        public float HealthFraction;
        public bool Attacking;
    }

    /// <summary>Every enemy on the map, plus the four things that happen to them: walking at the Core,
    /// being shot, stopping to tear down whatever machine stands in the way, and giving each other room.
    ///
    /// <b>Machines block and are attackable; belts are walked over.</b> An enemy walks a
    /// <see cref="PathField"/>'s flow toward the Core - around machines, not through them - the plain
    /// shortest way unless it weighed the ground at its door and chose to flank the guns' coverage. It stops
    /// when an attackable machine is within <see cref="EnemyDef.AggroRange"/> <em>and in plain view</em> and
    /// charges one it sees within <see cref="EnemyDef.DetectionRange"/>: aggressive targeting, not just
    /// obstacle avoidance, so a building beside the path is a target rather than scenery. "Sees" is the one
    /// thing that keeps that aggression honest - the line from the walker to the machine's cell centre, over
    /// ground it can cross (<see cref="Sees"/>) - so a gun behind rock is walked round, never ground against.
    /// Between two machines in reach, the turret is the one it charges - the wave hunts the guns, which is
    /// why a bare gun wants a wall between it and the lane.
    ///
    /// When the flow has no direction (the Core is sealed in behind what the player built), it walks the
    /// breach field instead (<see cref="PathField.TryBreach"/>): the cheapest way in that walks *through*
    /// buildings, so the wave goes to the hole rather than pressing at whichever rock happens to lie between
    /// it and the Core - which is what an enemy that walked straight at the Core used to do, and what made a
    /// wall plus a boulder a wave-shaped hole in the map. Standing beside the building on that route, it
    /// chews it down (step 2 does the hitting, and a building it stands beside is edge-adjacent, so always
    /// in view). Only when the map's own rock has sealed the walker in does it press on as it always did -
    /// nothing there can be chewed, so there is nothing better to do.
    ///
    /// The Core's rules are untouched: reaching the attack ring stops an enemy and drains
    /// <see cref="CoreState.Hp"/> at its own damage and interval. <b>Walkers keep their distance from each
    /// other:</b> one separation pass per tick pushes any pair closer than <see cref="Balance.EnemySpacing"/>
    /// apart (see <see cref="Separate"/>).</summary>
    public sealed class EnemyField
    {
        /// <summary>Slack on the Core's reach ring. The ring position is computed by scaling a vector, so
        /// the radius it lands on is <c>reach * (1 +- 1e-7)</c>: without this, an enemy that arrives stands a
        /// float hair outside the ring, fails the range test every tick, and hovers forever. A thousandth of
        /// a tile is far below anything visible.</summary>
        private const float ReachEpsilon = 1e-3f;

        /// <summary>How close to a cell corner a line has to pass to count as running through it, in units
        /// of the line's own length: <see cref="Sees"/> asks both cells meeting at a corner it crosses, and
        /// two crossings that differ by less than this are the same crossing - the one a float cannot tell
        /// apart. A thousandth of a tile is far below anything the player can see, and far above the noise
        /// of the arithmetic, so which of the two a walker's exact position lands on never changes a
        /// decision.</summary>
        private const float CornerEpsilon = 1e-3f;

        private EnemyState[] _enemies;
        private int _count;

        /// <summary>Separation scratch, one entry per enemy: this tick's pushes, accumulated pair by
        /// pair and applied once (<see cref="Separate"/>). Sized with <see cref="_enemies"/>.</summary>
        private Vec2[] _push;

        private readonly ContentDatabase _content;

        /// <summary>The stream enemy damage, kills and Core hits are reported on. Derived telemetry:
        /// nothing reads it back, so it cannot affect a run's outcome.</summary>
        private readonly SimEventBuffer _events;

        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly MachineField _machines;
        private readonly PathField _paths;

        /// <summary>Id -> slot. Only ever used for lookup, never enumerated, so it cannot leak order
        /// into the simulation the way an enumerated dictionary would.</summary>
        private readonly Dictionary<int, int> _slotById = new Dictionary<int, int>();

        private int _nextId = 1;

        /// <summary>Every enemy in the field is alive: a killed one is removed the moment it dies,
        /// so this is the wave's remaining work, not a slot count.</summary>
        public int AliveCount => _count;

        public EnemyField(ContentDatabase content, SimEventBuffer events, TileGrid grid,
            TerrainField terrain, MachineField machines, PathField paths, int capacity = 64)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _enemies = new EnemyState[Math.Max(8, capacity)];
            _push = new Vec2[_enemies.Length];
        }

        public void Clear()
        {
            Array.Clear(_enemies, 0, _count);
            _count = 0;
            _slotById.Clear();
            _nextId = 1;
        }

        /// <summary>Create one enemy, weak to <paramref name="weakness"/>. Returns its id, which is what
        /// shots home in on. The field is synced here rather than trusted to have been stepped this tick: a
        /// spawner outside the tick - a test, mostly - can place a building and spawn in the same breath,
        /// and the flanking judgement (<see cref="EnemyState.AvoidsFire"/>) must read the ground as it
        /// stands. Sync is a no-op when nothing changed.
        ///
        /// <paramref name="weakness"/> must not be <see cref="Shape.None"/>: under exact-identity damage an
        /// enemy nothing matches takes a quarter damage from every round and can never be killed
        /// efficiently - a silent trap rather than a loud one. Every shipped path is safe (the generator
        /// never draws an empty demand, and <see cref="MapValidation"/> names an authored group that names
        /// none); this note is here because a new call site that forgets the argument gets
        /// <c>default(Shape)</c> for free.</summary>
        public int Spawn(EnemyKind kind, Vec2 position, Shape weakness)
        {
            if (_count == _enemies.Length)
            {
                Array.Resize(ref _enemies, _enemies.Length * 2);
                Array.Resize(ref _push, _push.Length * 2);
            }

            _paths.Sync();
            EnemyDef spec = _content.Enemy(kind);
            int id = _nextId++;

            _enemies[_count] = new EnemyState
            {
                Id = id,
                Kind = kind,
                Weakness = weakness,
                Hp = spec.Hp,
                MaxHp = spec.Hp,
                Position = position,
                PreviousPosition = position,
                AttackCooldown = 0f,

                // The one decision a walker makes for itself, at the door: flank the covered way or
                // overrun it. The field is rebuilt on the same revision the spawner's tick used, so the
                // judgement reads the ground as it stands.
                AvoidsFire = _paths.FlankAt(_grid.CellAt(position)),
            };

            _slotById[id] = _count;
            _count++;

            return id;
        }

        /// <summary>Advance every enemy, in this order each: hold at the Core's ring and hit it; stop and
        /// hit an attackable machine in reach; walk one step along the flow field toward the Core; or, when
        /// the flow has no direction, walk straight at the Core and hit what blocks the way. One separation
        /// pass afterwards gives the walkers their own room.</summary>
        public void Step(float dt, ref CoreState core)
        {
            Vec2 centre = core.Center;
            float reach = Balance.CoreAttackRadius;
            float stopAt = reach + ReachEpsilon;

            for (int i = 0; i < _count; i++)
            {
                ref EnemyState e = ref _enemies[i];
                e.PreviousPosition = e.Position;

                EnemyDef spec = _content.Enemy(e.Kind);
                float distance = (centre - e.Position).Magnitude;

                // 1. The Core, first: an enemy inside the ring hits it even if a machine is also in reach,
                // so a defence built on the Core cannot distract an enemy from losing the run. Unchanged
                // from the straight-line days, including the exact landing on the ring.
                if (distance <= stopAt)
                {
                    e.Attacking = true;
                    e.AttackCooldown -= dt;
                    if (e.AttackCooldown <= 0f)
                    {
                        core.Hp = MathF.Max(0f, core.Hp - spec.Damage);
                        e.AttackCooldown += spec.AttackInterval;
                        _events.CoreDamaged(spec.Damage);
                    }
                    continue;
                }

                // 2. Machines, nearest first: inside the attack reach it stops and hits the machine;
                // between the reach and the notice range it charges it - leaving its path for the
                // building, which stops a machine two tiles off the lane from being furniture. Both want
                // the machine in plain view: a target it cannot see across rock is not a target, and
                // walking at one anyway is how a wave used to end up grinding against a boulder with a
                // wall behind it. The scan covers both, and the nothing-can-be-closer-than-the-nearest
                // invariant still holds, because a preferred turret beyond reach falls back to the nearest
                // machine inside it. The same cooldown covers the Core and every machine, so the damage
                // rate is the enemy's whatever it hits. A gun is charged before a granary anywhere in
                // reach of its notice, nearer or not - the turret is what is killing the wave, and a bare
                // gun wants a wall between it and the lane it covers.
                float notice = MathF.Max(spec.AggroRange, spec.DetectionRange);
                if (_machines.TryFindNearest(e.Position, notice, out Int2 target, preferTurrets: true))
                {
                    float gap = (_grid.CellCenter(target) - e.Position).Magnitude;
                    bool seen = Sees(e.Position, target);

                    // A machine the walker can actually chew outranks one it can only dream of: when the
                    // preferred gun sits beyond reach - behind the wall this posture invites - charging it
                    // would pin the walker against the wall forever, attacking neither. Chew the blocker;
                    // the scan re-hunts the gun the moment it is gone. "Can chew" is asked of the blocker
                    // too, so a building behind rock is stepped around rather than pressed against.
                    if ((gap > spec.AggroRange || !seen)
                        && _machines.TryFindNearest(e.Position, spec.AggroRange, out Int2 reachable)
                        && Sees(e.Position, reachable))
                    {
                        target = reachable;
                        gap = (_grid.CellCenter(target) - e.Position).Magnitude;
                        seen = true;
                    }

                    if (seen)
                    {
                        if (gap <= spec.AggroRange)
                        {
                            e.Attacking = true;
                            e.AttackCooldown -= dt;
                            if (e.AttackCooldown <= 0f)
                            {
                                _machines.DamageAt(target, spec.Damage);
                                e.AttackCooldown += spec.AttackInterval;
                            }
                            continue;
                        }

                        // A charge, and only ever along a line it can see: the step below never has to
                        // grind, because a line clear of solid cells is a line it can walk.
                        e.Attacking = false;
                        e.Position = Advance(e.Position, _grid.CellCenter(target), spec.Speed * dt);
                        continue;
                    }
                }

                e.Attacking = false;
                float step = spec.Speed * dt;

                // 3. The flow field - the one this walker chose at the door - steer at the centre of the
                // next cell on the way to the Core, and land exactly on it rather than stepping past:
                // overshooting would oscillate around the centre as it would around the Core's ring. With
                // no way along that field, the breach field answers instead: the cheapest route that
                // carries it through the player's buildings, which is the only way in once the Core is
                // walled off. Rock is a wall to both, so a walker a map's own ground has sealed in gets no
                // answer and presses on below.
                Int2 cell = _grid.CellAt(e.Position);
                bool routed = _paths.TryDirection(cell, e.AvoidsFire, out Dir dir)
                    || _paths.TryBreach(cell, out dir);
                if (routed)
                {
                    e.Position = Advance(e.Position, _grid.CellCenter(cell + dir.Offset()), step);
                    continue;
                }

                // 4. Sealed in by the map itself: no way from here through ground or building, so walk
                // straight at the Core as an enemy always did - but never into a solid cell. The Core's own
                // ring is the only thing left to reach for, and a map that draws one is a map that cannot be
                // walked, which is the designer's call and not the walker's.
                e.Position = Advance(e.Position, centre, step);
            }

            Separate(dt);
        }

        /// <summary>The walkers' own collision: any two closer than <see cref="Balance.EnemySpacing"/> push
        /// each other apart by half the overlap each. Symmetric by construction - every pair is walked once
        /// and both halves accumulated before anything moves, so the result does not depend on pair order -
        /// and the push goes through <see cref="Advance"/> capped at the walker's own speed, so separation can
        /// neither shove a walker into a solid cell nor teleport a deep pile clear in one tick.
        /// Exactly-overlapping walkers part along X, an axis fixed by the code, so a replay still matches.
        /// Spawning is deliberately not special-cased: this pass, not the door, pulls a stacked arrival out
        /// of the queue.</summary>
        private void Separate(float dt)
        {
            if (_count < 2) return;

            Array.Clear(_push, 0, _count);

            float space = Balance.EnemySpacing;
            float spaceSq = space * space;

            for (int i = 0; i < _count; i++)
            {
                Vec2 from = _enemies[i].Position;
                for (int j = i + 1; j < _count; j++)
                {
                    Vec2 delta = _enemies[j].Position - from;
                    float sq = delta.X * delta.X + delta.Y * delta.Y;
                    if (sq >= spaceSq) continue;

                    float distance = MathF.Sqrt(sq);
                    Vec2 axis = distance > 0f ? delta * (1f / distance) : new Vec2(1f, 0f);
                    float half = (space - distance) * 0.5f;

                    _push[i] = _push[i] - axis * half;
                    _push[j] = _push[j] + axis * half;
                }
            }

            for (int i = 0; i < _count; i++)
            {
                Vec2 push = _push[i];
                float length = push.Magnitude;
                if (length <= 0f) continue;

                ref EnemyState e = ref _enemies[i];

                // A push never moves a walker faster than it moves itself: the pass resolves an overlap at
                // walking pace - a few ticks for a pair that spawned on the same spot, invisible for the
                // hundredth of a tile the ordinary case closes by.
                float cap = _content.Enemy(e.Kind).Speed * dt;
                if (cap <= 0f) continue;
                if (length > cap) push = push * (cap / length);

                e.Position = Advance(e.Position, e.Position + push, push.Magnitude);
            }
        }

        /// <summary>May a walker enter this cell? Ground and ore are open, and so are belts and the Core; a
        /// machine is a wall whether or not it has HP left, and so is the map's rock, water and void. One
        /// rule, stated once, in <see cref="TerrainField.Walkable"/>.</summary>
        private bool Open(Int2 cell) => _terrain.Walkable(cell);

        /// <summary>
        /// Can a walker at <paramref name="from"/> reach what stands in <paramref name="target"/>? The
        /// straight line from the walker to that cell's centre, cell by cell: the cell the line ends in is
        /// the machine itself and is not asked about - it is solid by definition - but every other cell the
        /// line crosses is. So rock, water, the void and a second building all put a target out of view, and
        /// a walker walks the way round instead of pressing at the corner of something it cannot chew. A
        /// line that runs exactly through a cell corner asks both cells that meet there, so the corner
        /// itself is not a way through.
        ///
        /// The one rule the machine-hunting half of <see cref="Step"/> reads besides range, and the reason it
        /// cannot jam: a line with no solid cell on it is a line <see cref="Advance"/> can walk, so a charge
        /// always takes a step. Deterministic to the float, like everything else here.
        /// </summary>
        private bool Sees(Vec2 from, Int2 target)
        {
            int cx = _grid.CellAt(from).X;
            int cy = _grid.CellAt(from).Y;
            if (cx == target.X && cy == target.Y) return true;

            Vec2 to = _grid.CellCenter(target);
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;

            int stepX = dx > 0f ? 1 : -1;
            int stepY = dy > 0f ? 1 : -1;

            // Where along the line each axis is first crossed, and how far apart the crossings are after
            // that - in units of the line itself, so the smaller of the two is simply the next cell. An
            // axis the line never moves on is never crossed.
            float crossX = dx == 0f ? float.PositiveInfinity : ((dx > 0f ? cx + 1f : cx) - from.X) / dx;
            float crossY = dy == 0f ? float.PositiveInfinity : ((dy > 0f ? cy + 1f : cy) - from.Y) / dy;
            float spanX = dx == 0f ? float.PositiveInfinity : 1f / MathF.Abs(dx);
            float spanY = dy == 0f ? float.PositiveInfinity : 1f / MathF.Abs(dy);

            // A notice range is a couple of tiles, so the cell count is small; the bound is the cells the
            // line can cross at all, and running out of it means the line ended on a boundary the float
            // could not resolve - no view, which is the safe answer.
            int limit = 1 + Math.Abs(target.X - cx) + Math.Abs(target.Y - cy);
            for (int i = 0; i < limit; i++)
            {
                float lead = crossX - crossY;
                if (lead < -CornerEpsilon)
                {
                    cx += stepX;
                    crossX += spanX;
                }
                else if (lead > CornerEpsilon)
                {
                    cy += stepY;
                    crossY += spanY;
                }
                else
                {
                    // Through the corner: both cells that meet there are under the line.
                    if (!Open(new Int2(cx + stepX, cy))) return false;
                    if (!Open(new Int2(cx, cy + stepY))) return false;
                    cx += stepX;
                    cy += stepY;
                    crossX += spanX;
                    crossY += spanY;
                }

                if (cx == target.X && cy == target.Y) return true;
                if (!Open(new Int2(cx, cy))) return false;
            }

            return false;
        }

        /// <summary>Walk one step from <paramref name="from"/> toward <paramref name="to"/>, landing exactly
        /// on it when the step reaches it - and never into a solid cell: a blocked step slides along one
        /// axis, then the other, and stands still if both are closed. That guard presses a charging enemy
        /// against the machine it is headed for instead of through it, and keeps the siege honest.</summary>
        private Vec2 Advance(Vec2 from, Vec2 to, float step)
        {
            Vec2 delta = to - from;
            float distance = delta.Magnitude;
            Vec2 wish = distance <= step || distance <= 0f ? to : from + delta * (step / distance);

            if (Open(_grid.CellAt(wish))) return wish;

            var slideX = new Vec2(wish.X, from.Y);
            if (Open(_grid.CellAt(slideX))) return slideX;

            var slideY = new Vec2(from.X, wish.Y);
            if (Open(_grid.CellAt(slideY))) return slideY;

            return from;
        }
        /// <summary>Hits one enemy for what the shot is worth against it: a round matching the enemy's
        /// weakness lands its full damage, anything else a quarter. So a gun's damage is what the round it
        /// was fed does to the enemy it was aimed at, and the briefing's demand shape is a real decision
        /// rather than a label on the card.</summary>
        public void ApplyDamage(int id, float baseDamage, Shape ammo)
        {
            if (!_slotById.TryGetValue(id, out int slot)) return;
            ApplyDamageAtSlot(slot, baseDamage, ammo);
        }

        private void ApplyDamageAtSlot(int slot, float baseDamage, Shape ammo)
        {
            // Weakness is exact identity now: the round is the shape the briefing named, or it is not.
            float damage = baseDamage * (ammo == _enemies[slot].Weakness
                ? Balance.WeaknessDamageMultiplier
                : Balance.ResistantDamageMultiplier);

            _enemies[slot].Hp -= damage;

            // Read what the report needs *before* RemoveAt, which reorders the array it would be read
            // from: the kind and the position are gone the moment the enemy is.
            bool killed = _enemies[slot].Hp <= 0f;
            int id = _enemies[slot].Id;
            EnemyKind kind = _enemies[slot].Kind;
            Vec2 position = _enemies[slot].Position;

            if (killed)
            {
                _events.EnemyKilled(id, kind, position);
                RemoveAt(slot);
            }
            else
            {
                _events.EnemyDamaged(id, kind, position, damage);
            }
        }

        /// <summary>One enemy by slot, for a reader that wants a single slot. Returns a copy on
        /// purpose: nobody may hold a reference into an array that kills reorder.</summary>
        public EnemyState EnemyAt(int index) => _enemies[index];

        /// <summary>The enemy a turret fed <paramref name="ammo"/> should shoot: the nearest one weak to it,
        /// or the nearest in range at all when nothing weak is there. A weakness match is worth four times the
        /// damage, so a turret prefers the enemy it was built for - but it never holds fire while something it
        /// can still scratch is in range, because a resisted shot is some damage and a held one is none. Ties
        /// go to the lower slot, so which of two equidistant enemies it picks is a function of the map state,
        /// not the caller. False when nothing is in range, in which case the outputs are unusable.</summary>
        public bool TryFindPreferred(Vec2 centre, float range, Shape ammo, out int id, out Vec2 position)
        {
            float bestSq = range * range;
            float bestWeakSq = range * range;
            int nearest = 0;
            int nearestWeak = 0;
            Vec2 nearestAt = Vec2.Zero;
            Vec2 nearestWeakAt = Vec2.Zero;

            for (int i = 0; i < _count; i++)
            {
                ref readonly EnemyState enemy = ref _enemies[i];
                if (enemy.Hp <= 0f) continue;

                Vec2 delta = enemy.Position - centre;
                float sq = delta.X * delta.X + delta.Y * delta.Y;

                if (sq <= bestSq)
                {
                    bestSq = sq;
                    nearest = enemy.Id;
                    nearestAt = enemy.Position;
                }

                // Tracked separately rather than after a "nearer than the nearest" skip: the enemy
                // this ammo hurts may well be the farther one - preferring it is the whole point.
                if (enemy.Weakness == ammo && sq <= bestWeakSq)
                {
                    bestWeakSq = sq;
                    nearestWeak = enemy.Id;
                    nearestWeakAt = enemy.Position;
                }
            }

            if (nearestWeak != 0)
            {
                id = nearestWeak;
                position = nearestWeakAt;
                return true;
            }

            id = nearest;
            position = nearestAt;
            return id != 0;
        }

        public bool TryGetPosition(int id, out Vec2 position)
        {
            if (_slotById.TryGetValue(id, out int slot))
            {
                position = _enemies[slot].Position;
                return true;
            }

            position = Vec2.Zero;
            return false;
        }

        /// <summary>Every enemy, in slot order, with the positions the view interpolates between.</summary>
        public void GetEnemies(List<EnemySnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _count; i++)
            {
                ref readonly EnemyState e = ref _enemies[i];
                into.Add(new EnemySnapshot
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    Weakness = e.Weakness,
                    PreviousPosition = e.PreviousPosition,
                    Position = e.Position,
                    HealthFraction = e.MaxHp <= 0f ? 0f : e.Hp / e.MaxHp,
                    Attacking = e.Attacking,
                });
            }
        }

        private void RemoveAt(int slot)
        {
            int id = _enemies[slot].Id;
            _slotById.Remove(id);

            int last = _count - 1;
            if (slot != last)
            {
                _enemies[slot] = _enemies[last];
                _slotById[_enemies[slot].Id] = slot;
            }

            _enemies[last] = default;
            _count--;
        }
    }
}
