using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>State of one built machine. One struct covers drills, cutters, rotators, stackers, pipes,
    /// splitters, sorters and turrets because they share the two things that make the twist work: a facing
    /// (where a drill pushes, a cutter's cut line, a rotator's outlet, a turret's barrel) and being fed
    /// from the belt cells that deliver into them. The unused fields per kind are deliberate: eight parallel
    /// arrays would cost more to read than this costs to store.</summary>
    public struct MachineState
    {
        /// <summary>One of the machine <see cref="TileKind"/>s. <see cref="TileKind.Empty"/> means the
        /// cell holds no machine.</summary>
        public TileKind Kind;

        /// <summary>
        /// Where this machine's state lives: the cell itself for a machine on one tile and for the
        /// <em>primary</em> tile of a machine on two, and the primary's cell for the other tile of one.
        ///
        /// A cutter's two tiles both hold a state - both are solid ground, both can be read and either can be
        /// taken away - but only the primary is in the tick's registry, so the machine is stepped once and
        /// counted once. Every reader therefore resolves through this before touching anything else, and the
        /// second tile's own copy is a marker that never goes stale because nothing reads past it.
        /// </summary>
        public Int2 Home;

        /// <summary>Which building this is. Turrets read their whole spec - rate, damage, range - back out
        /// of it, so one field covers the kind and its numbers.</summary>
        public BuildKind Build;

        /// <summary>Drill: the cell it pushes onto. Cutter: the way the belt runs through it, which picks
        /// the cut line and which of its two tiles each piece leaves by. Rotator: the side it turns out of.
        /// Pipe: the direction it carries across. Splitter: unused - its ports are read off the belts around
        /// it. Sorter: which side the filtered shape leaves by. Turret: where its barrel rests when it has no
        /// target.</summary>
        public Dir Direction;

        /// <summary>Seconds until this machine may act again. Pipe: the remaining transit time of the
        /// item inside. Cutter: the remaining split. Rotator: the remaining turn. Stacker: the remaining
        /// fuse.</summary>
        public float Cooldown;

        /// <summary>What the machine is holding: a cutter's item being split, a rotator's item being
        /// turned, a stacker's first piece, a pipe's item in transit, a splitter's buffered item, a
        /// turret's <em>chambered</em> round - the one round it is loaded with and will fire next, held from
        /// the moment a belt delivered it until the shot leaves, so that the badge and the card can always
        /// say what the line is feeding the gun - or nothing.</summary>
        public Shape Carried;

        /// <summary>Stacker: the second held piece, once the first is in <see cref="Carried"/>. The fuse
        /// runs while both are held, and both clear when the union lands in the output.</summary>
        public Shape Carried2;

        /// <summary>The machine's first output slot: outputs made but not yet pushed, 0 up to the
        /// behaviour's buffer. A cutter fills it with the piece its facing side owns; a stacker, with the
        /// union it just made.</summary>
        public int OutputCount;

        /// <summary>What the first output slot holds, remembered from the cycle that made it: a cutter
        /// picks a different piece per item, so the slot cannot be read back off the definition.</summary>
        public Shape OutputShape;

        /// <summary>The second output slot, and the reason the state grew one in the rework: a cutter
        /// emits <em>two</em> pieces at once, one per side of the cut, and they must buffer separately -
        /// a single slot would lose one piece whenever the other side's belt was full, and "cut, then
        /// route the two halves differently" is the whole puzzle.</summary>
        public int OutputCount2;

        /// <summary>What the second output slot holds.</summary>
        public Shape OutputShape2;

        /// <summary>Splitter and sorter: the port the next item tries first, so a balanced hub rotates
        /// instead of always favouring the lowest-numbered output.</summary>
        public int Rotation;

        /// <summary>
        /// Sorter: what this machine routes for, seeded from its definition's row when it is placed and
        /// editable afterwards - the picker beside the info card is the player's, and a routing decision made
        /// without rebuilding the machine is what makes the sorter a tool rather than a purchase. Nothing for
        /// every other behaviour.
        /// </summary>
        public ShapeFilter Filter;

        /// <summary>Turret: unit vector the barrel points at, updated every tick.</summary>
        public Vec2 Aim;

        /// <summary>Turret: the enemy it is currently shooting at, 0 for none. Shots home on the id,
        /// and <see cref="TargetPosition"/> is where that enemy was when the shot was fired.</summary>
        public int TargetId;

        public Vec2 TargetPosition;

        /// <summary>Turret: an enemy is in range this tick.</summary>
        public bool HasTarget;

        /// <summary>Turret: there is a round in the chamber, so it can fire the moment there is something to
        /// shoot. Derived from <see cref="Carried"/> by the behaviour rather than tracked on its own - the flag
        /// the HUD warns on and the badge on screen are one fact. False is the "starved" state.</summary>
        public bool Armed;

        /// <summary>Health enemies have left to chew through. Initialized on placement from the
        /// building's <see cref="MachineDef.MaxHp"/>; MaxHp 0 leaves this at 0 and is never targeted.</summary>
        public float Hp;

        /// <summary>Sink: how many items it has destroyed since it was placed - the one number that says a
        /// drain is doing its job, since a sink that works looks exactly like a sink that is fed nothing.
        /// Nothing reads it back: the card prints it and a test may count it.</summary>
        public int Swallowed;

        /// <summary>
        /// How many ticks in a row this machine has held something its outlets would not take: the cutter
        /// with a full lane, the sorter waiting on the side it faces, the pipe whose landing belt is busy,
        /// the rotator turned and waiting for room, the stacker with a finished round and nowhere to put it.
        /// Reset the tick anything moves.
        ///
        /// It is the one way a line stops that nothing else on the HUD names. A <b>jam</b> is a machine's
        /// refusal and a <b>blocked</b> segment is an item with nowhere to go at all, but a machine holding a
        /// piece an outlet merely would not take is neither: the belts behind it are not blocked (the machine
        /// <em>would</em> take them) and nothing was refused. Without this counter the line stands still and
        /// the screen says nothing at all - which is what it is here to end.
        ///
        /// Two waits are deliberately <em>not</em> counted, so that the number stays one piece of news: a
        /// drill's (its wait is its own one-second clock, not an outlet, and a lane that has genuinely
        /// dead-ended behind it is already <see cref="BeltField.BlockedCount"/>), and a stacker waiting for a
        /// piece that fits (a design, not a stoppage - the item it cannot merge is one the player spent a
        /// chain building). A turret's wait for a target is <see cref="Armed"/>'s other state, "starved".
        /// </summary>
        public int StallTicks;
    }

    /// <summary>One machine, flattened for the view layer.</summary>
    public struct MachineSnapshot
    {
        public Int2 Cell;
        public TileKind Kind;

        /// <summary>Which building this is, and which behaviour drives it. Both are carried so the view
        /// can dispatch on the behaviour ("draw a cutter") while resolving the per-building look
        /// override, without looking either up per frame.</summary>
        public BuildKind Build;
        public BehaviorKind Behavior;

        public Dir Direction;
        public Vec2 Aim;
        public bool HasTarget;
        public bool Armed;

        /// <summary>Health left, and the pool it started from, so the view can tint a damaged building
        /// the way it tints a damaged Core without looking the definition up.</summary>
        public float Hp;
        public float MaxHp;

        /// <summary>0..1, for the damage tint. An indestructible building (MaxHp 0) reports 0 but is
        /// never damaged - the view checks <see cref="MaxHp"/> before tinting, not this.</summary>
        public float HealthFraction => MaxHp <= 0f ? 0f : Hp / MaxHp;

        /// <summary>What the building draws on itself: a turret's chambered round, a sorter's filter
        /// shape (the badge that says which shape goes which way), a cutter's pending piece, a pipe or
        /// splitter's buffered item, nothing when idle.</summary>
        public Shape Shape;

        /// <summary>The second badge, where a machine has one: the cutter's other pending piece. Empty for
        /// every other behaviour, and the view draws nothing extra for it.</summary>
        public Shape Shape2;

        /// <summary>What a sorter routes for, as it stands: the machine's own configuration rather than its
        /// row's, so the info card and the picker describe the machine that is actually on the map.</summary>
        public ShapeFilter Filter;

        /// <summary>Work-in-progress fraction for the view to animate: a cutter's split, a rotator's turn,
        /// a stacker's fuse, a pipe's transit. 0 for machines with nothing in flight.</summary>
        public float Work;

        /// <summary>Sides a belt feeds this machine from, and sides it pushes onto - the same sets the
        /// tick acts on, computed once in Core by <see cref="MachineDelivery.PortMasks"/> and gathered over
        /// every tile the machine stands on. The view draws its stubs and its neighbours' docking lanes from
        /// these instead of re-deriving the rule, so the picture cannot drift from the simulation.</summary>
        public DirMask InMask;
        public DirMask OutMask;
    }

    /// <summary>Where the machines are and what they are doing: storage and placement rules, the
    /// occupancy registry the tick walks, and the snapshots the view reads. The tick behaviour lives in
    /// the machine behaviours, dispatched by <c>MachineSystem</c>.
    ///
    /// It keeps an explicit <see cref="Occupied"/> list of the cells that hold a machine, so stepping
    /// machines and counting turrets cost the number of machines, not tiles. The list is kept in ascending
    /// linear-index order rather than insertion order, so iteration order is the map's, independent of
    /// build order - which keeps a replay of the same commands identical.</summary>
    public sealed class MachineField
    {
        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly BeltField _belts;
        private readonly ContentDatabase _content;

        /// <summary>The map, for the one tile-rule that is the map's: waves enter at its doorways, so no
        /// machine may stand on one.</summary>
        private readonly MapDefinition _map;

        /// <summary>The stream enemy damage to buildings is reported on. Derived telemetry: nothing
        /// reads it back, so it cannot affect a run's outcome.</summary>
        private readonly SimEventBuffer _events;

        private readonly MachineState[] _state;

        /// <summary>Occupied cells, as linear indices, ascending. The only iteration order.</summary>
        private readonly List<int> _occupied = new List<int>();

        /// <summary>Bumped whenever the set of solid cells changes - placement, removal, and a
        /// building destroyed by enemies. <see cref="PathField"/> reads it to know when to rebuild.</summary>
        public int Revision { get; private set; }

        public MachineField(TileGrid grid, TerrainField terrain, BeltField belts, ContentDatabase content,
            SimEventBuffer events, MapDefinition map)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _state = new MachineState[grid.Width * grid.Height];
        }

        /// <summary>Every occupied cell, ascending by linear index. The tick and the turret count walk
        /// this; nothing walks the whole grid.</summary>
        public IReadOnlyList<int> Occupied => _occupied;

        /// <summary>The cell at a position in <see cref="Occupied"/>, in the same stable order.</summary>
        public Int2 CellAt(int occupiedIndex) => _grid.CellOf(_occupied[occupiedIndex]);

        public bool Has(Int2 cell) => _grid.InBounds(cell) && _state[_grid.Index(cell)].Kind.IsMachine();

        /// <summary>True for a machine, on either of its tiles, and the cell its state lives on. The one
        /// place the two-tile resolution happens: every reader below goes through it, so a caller that was
        /// handed the second tile of a cutter reads the cutter rather than a marker.</summary>
        private bool TryHome(Int2 cell, out Int2 home)
        {
            home = cell;
            if (!Has(cell)) return false;

            Int2 stored = _state[_grid.Index(cell)].Home;
            if (stored != cell && Has(stored)) home = stored;
            return true;
        }

        public bool TryGet(Int2 cell, out MachineState state)
        {
            if (!TryHome(cell, out Int2 home))
            {
                state = default;
                return false;
            }

            state = _state[_grid.Index(home)];
            return true;
        }

        /// <summary>One machine, ready for the view, with its port masks resolved.</summary>
        public bool TryGetSnapshot(Int2 cell, out MachineSnapshot snapshot)
        {
            if (!TryGet(cell, out MachineState state))
            {
                snapshot = default;
                return false;
            }

            snapshot = SnapshotOf(state, cell);
            return true;
        }

        /// <summary>Mutable ref for the machine behaviours, which update cooldowns, cargo and aim
        /// in place. Resolves a machine's second tile to the tile its state lives on, so a ref taken for
        /// either tile of a cutter is the cutter.</summary>
        public ref MachineState RefAt(Int2 cell)
        {
            int i = _grid.Index(cell);
            if (_state[i].Kind.IsMachine())
            {
                Int2 stored = _state[i].Home;
                if (stored != cell && Has(stored)) i = _grid.Index(stored);
            }

            return ref _state[i];
        }

        /// <summary>May a machine of this kind stand here? A drill only on a vein and every other machine
        /// only on plain ground - a belt is occupied, so it comes out first, the whole cost of a belt
        /// crossing a vein - and no machine on a wave's doorway, the one place it would seal a wave in. A
        /// belt stays legal there because only something solid would plug a door. The ground half of the
        /// rule is <see cref="TerrainField.CanPlaceMachine"/>, which is also where rock, water and the void
        /// refuse every machine at once.
        ///
        /// <paramref name="facing"/> is what a two-tile machine needs to answer: a cutter's second tile is
        /// one lane over from the first, so turning the ghost is turning the footprint, and the answer is
        /// only yes when both tiles are legal.</summary>
        public bool CanPlace(BuildKind build, Int2 cell, Dir facing)
        {
            if (!CanPlaceOne(build, cell)) return false;

            BehaviorKind behavior = _content.Machine(build).Behavior;
            return !MachineFootprint.TrySecond(behavior, cell, facing, out Int2 second) || CanPlaceOne(build, second);
        }

        private bool CanPlaceOne(BuildKind build, Int2 cell)
        {
            if (!_grid.InBounds(cell)) return false;

            TileKind kind = _content.Machine(build).Tile;
            if (kind == TileKind.Belt) return false;                       // belts are BeltField's business
            if (_map.SpawnIndexOf(cell) >= 0) return false;                // a doorway stays open

            // A drill needs a patch to mine, and a patch only ever wants a drill.
            return _terrain.CanPlaceMachine(cell, needsOre: kind == TileKind.Drill);
        }

        public bool TryPlace(Int2 cell, BuildKind build, Dir direction)
        {
            if (!CanPlace(build, cell, direction)) return false;

            MachineDef def = _content.Machine(build);
            int i = _grid.Index(cell);
            _state[i] = new MachineState
            {
                Kind = def.Tile,
                Direction = direction,
                Aim = direction.ToVec(),
                Build = build,
                Hp = def.MaxHp,
                Home = cell,

                // The row's filter is the machine's starting configuration, not its definition: the player
                // may re-route a standing sorter without rebuilding it.
                Filter = def.Filter,
            };
            _grid.Set(cell, def.Tile);
            AddOccupied(i);

            // The second tile of a two-tile machine: marked in the ground so nothing is built over it and so
            // a walker treats it as solid, marked in the state so it reads as the machine, and deliberately
            // kept out of the registry - the machine is stepped once, from the tile its state lives on.
            if (MachineFootprint.TrySecond(def.Behavior, cell, direction, out Int2 second))
            {
                _state[_grid.Index(second)] = new MachineState
                {
                    Kind = def.Tile,
                    Direction = direction,
                    Aim = direction.ToVec(),
                    Build = build,
                    Hp = def.MaxHp,
                    Home = cell,
                };
                _grid.Set(second, def.Tile);
            }

            Revision++;
            return true;
        }

        /// <summary>Remove a machine and put the tile back to its ground. Either of a two-tile machine's
        /// tiles will do: the machine is one thing standing on two tiles, and taking one away takes it.</summary>
        public bool TryRemove(Int2 cell)
        {
            if (!TryHome(cell, out Int2 home) || !Clear(home)) return false;

            Revision++;
            return true;
        }

        /// <summary>Empty the tiles a machine stands on, and the registry entry that steps it. The machine's
        /// own tile first, so the registry, the ground and the state are consistent even if the second tile
        /// has been scribbled over by something else.</summary>
        private bool Clear(Int2 home)
        {
            int i = _grid.Index(home);
            BehaviorKind behavior = _content.Machine(_state[i].Build).Behavior;
            Dir facing = _state[i].Direction;

            _state[i] = default;
            RemoveOccupied(i);
            _terrain.Release(home);

            if (!MachineFootprint.TrySecond(behavior, home, facing, out Int2 second)) return true;

            _state[_grid.Index(second)] = default;
            _terrain.Release(second);
            return true;
        }

        /// <summary>Apply enemy damage to the machine at a cell. Reports
        /// <see cref="SimEventKind.BuildingDamaged"/> and, when the last HP goes,
        /// <see cref="SimEventKind.BuildingDestroyed"/> - then removes the machine as <see cref="TryRemove"/>
        /// does (terrain restored, carried item lost) but with no refund. False when nothing attackable is
        /// there (no machine, or MaxHp 0 - indestructible, see <see cref="MachineDef.MaxHp"/>). Returns true
        /// iff the building died. Either tile of a two-tile machine will do, and the damage lands on the one
        /// the machine keeps its health on, so a bite out of the lane and a bite out of the blade are bites
        /// out of the same machine.</summary>
        public bool DamageAt(Int2 cell, float damage)
        {
            if (!TryHome(cell, out Int2 home)) return false;

            int i = _grid.Index(home);
            ref MachineState m = ref _state[i];
            if (_content.Machine(m.Build).MaxHp <= 0f) return false;

            m.Hp -= damage;
            _events.BuildingDamaged(home, m.Build, damage);
            if (m.Hp > 0f) return false;

            _events.BuildingDestroyed(home, m.Build);
            Clear(home);
            Revision++;
            return true;
        }

        /// <summary>The nearest attackable machine (<see cref="MachineDef.MaxHp"/> above 0) to
        /// <paramref name="centre"/> within <paramref name="range"/>. Ties go to the later occupied cell -
        /// the map's order, not the caller's - because the scan keeps overwriting on an exact tie. With
        /// <paramref name="preferTurrets"/>, a standing turret is charged in preference to any other machine
        /// in range: the wave hunts the gun before the granary. False when nothing is in range, in which
        /// case the output is unusable.</summary>
        public bool TryFindNearest(Vec2 centre, float range, out Int2 cell, bool preferTurrets = false)
        {
            float bestSq = range * range;
            float bestGunSq = range * range;
            cell = default;
            Int2 gunCell = default;
            bool found = false;
            bool foundGun = false;

            for (int k = 0; k < _occupied.Count; k++)
            {
                int i = _occupied[k];
                ref readonly MachineState m = ref _state[i];
                if (_content.Machine(m.Build).MaxHp <= 0f) continue;

                Int2 here = _grid.CellOf(i);
                Vec2 delta = _grid.CellCenter(here) - centre;
                float sq = delta.X * delta.X + delta.Y * delta.Y;

                if (sq <= bestSq)
                {
                    bestSq = sq;
                    cell = here;
                    found = true;
                }

                // Tracked separately rather than inside the nearest-match block above: the gun worth
                // chewing may well be the farther machine, and preferring it is the whole point.
                if (preferTurrets && _content.IsTurret(m.Build) && sq <= bestGunSq)
                {
                    bestGunSq = sq;
                    gunCell = here;
                    foundGun = true;
                }
            }

            if (foundGun)
            {
                cell = gunCell;
                return true;
            }

            return found;
        }

        public void Clear()
        {
            Array.Clear(_state, 0, _state.Length);
            _occupied.Clear();
            Revision++;
        }

        /// <summary>Every machine, in stable linear-index order.</summary>
        public void GetMachines(List<MachineSnapshot> into)
        {
            into.Clear();
            for (int k = 0; k < _occupied.Count; k++)
            {
                int i = _occupied[k];
                into.Add(SnapshotOf(_state[i], _grid.CellOf(i)));
            }
        }

        /// <summary>Turret count and how many are armed - the HUD's one diagnostic number. Asks the
        /// content table whether a building is a turret rather than testing a tile kind, so a custom table
        /// with a second gun counts without this method knowing it exists.</summary>
        public void CountTurrets(out int total, out int armed)
        {
            total = 0;
            armed = 0;
            for (int k = 0; k < _occupied.Count; k++)
            {
                ref readonly MachineState m = ref _state[_occupied[k]];
                if (!_content.IsTurret(m.Build)) continue;

                total++;
                if (m.Armed) armed++;
            }
        }

        /// <summary>
        /// How long a machine has to have been waiting on an outlet before the HUD calls it stopped: one
        /// second at the fixed tick rate. Long enough that a full lane the line is about to drain is not
        /// news, short enough that a factory standing still says so while the player is still looking at it.
        /// </summary>
        public static readonly int StallTicks = (int)SimConfig.TickRate;

        /// <summary>How many machines have been holding something they cannot pass on for at least
        /// <see cref="StallTicks"/> - <see cref="MachineState.StallTicks"/>'s "how many", and the number the
        /// HUD's production line says out loud. Machines, not tiles: a cutter standing on two is one.</summary>
        public void CountStalled(out int stalled)
        {
            stalled = 0;
            for (int k = 0; k < _occupied.Count; k++)
                if (_state[_occupied[k]].StallTicks >= StallTicks) stalled++;
        }

        /// <summary>Which machines those are, in the registry's stable order - <see cref="BeltField.GetJammed"/>'s
        /// counterpart for a machine rather than a segment, so the locator can point at a stoppage the player
        /// cannot see. A dedicated walk for the same reason: a stalled machine is a place to go and look.</summary>
        public void GetStalled(List<Int2> into)
        {
            into.Clear();
            for (int k = 0; k < _occupied.Count; k++)
            {
                int i = _occupied[k];
                if (_state[i].StallTicks < StallTicks) continue;

                into.Add(_grid.CellOf(i));
            }
        }

        private MachineSnapshot SnapshotOf(in MachineState m, Int2 cell)
        {
            MachineDef def = _content.Machine(m.Build);
            MachineDelivery.PortMasks(_belts, _content, cell, in m, out DirMask inMask, out DirMask outMask);

            // A machine that stands on two tiles has ports on both of them, and the snapshot is of the
            // machine: the masks are the union of its tiles', so the info card reads a cutter's own outlets
            // rather than the blade tile's half of them. The view that draws them per side asks
            // <see cref="PortMasksAt"/> instead, which answers for one tile.
            if (MachineFootprint.TrySecond(def.Behavior, cell, m.Direction, out Int2 second))
            {
                MachineDelivery.PortMasks(_belts, _content, second, in m, out DirMask inSecond,
                    out DirMask outSecond);
                inMask = inMask.With(inSecond);
                outMask = outMask.With(outSecond);
            }

            return new MachineSnapshot
            {
                Cell = cell,
                Kind = m.Kind,
                Build = m.Build,
                Behavior = def.Behavior,
                Direction = m.Direction,
                Aim = m.Aim,
                HasTarget = m.HasTarget,
                Armed = m.Armed,
                Hp = m.Hp,
                MaxHp = def.MaxHp,
                Shape = MachineReading.Badge(m, def),
                Shape2 = MachineReading.Badge2(m, def),
                Filter = m.Filter,
                Work = MachineReading.Work(m, def),
                InMask = inMask,
                OutMask = outMask,
            };
        }

        /// <summary>
        /// The ports on the sides of <em>one tile</em> of the machine standing there, in and out. The
        /// snapshot's masks are the machine's (see <see cref="SnapshotOf"/>); this is the one-tile answer,
        /// for a reader that has to know which side of which tile a belt docks with - a belt drawing the
        /// lane it feeds, or a two-tile body drawing a stub on each of its own fronts.
        /// </summary>
        public void PortMasksAt(Int2 cell, out DirMask inMask, out DirMask outMask)
        {
            if (!TryGet(cell, out MachineState m))
            {
                inMask = DirMask.None;
                outMask = DirMask.None;
                return;
            }

            MachineDelivery.PortMasks(_belts, _content, cell, in m, out inMask, out outMask);
        }

        /// <summary>Insert an occupied cell, keeping the list sorted. Occupancy is monotone per cell, so a
        /// cell can never be added twice.</summary>
        private void AddOccupied(int index)
        {
            int at = _occupied.BinarySearch(index);
            if (at < 0) _occupied.Insert(~at, index);
        }

        private void RemoveOccupied(int index)
        {
            int at = _occupied.BinarySearch(index);
            if (at >= 0) _occupied.RemoveAt(at);
        }
    }
}
