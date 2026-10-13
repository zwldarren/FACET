using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>
    /// State of one built machine. One struct covers drills, decomposers, pipes, splitters and
    /// turrets because they share the two things that make the twist work: a facing (where a
    /// drill/decomposer pushes, where a pipe carries, where a turret's barrel points) and being fed
    /// from the belt cells that deliver into them.
    ///
    /// The fields a given kind does not use are deliberate: five parallel arrays with five sets of
    /// accessors would cost more to read than this costs to store (a few tens of KB for the whole map).
    /// </summary>
    public struct MachineState
    {
        /// <summary>One of the machine <see cref="TileKind"/>s. <see cref="TileKind.Empty"/> means the
        /// cell holds no machine.</summary>
        public TileKind Kind;

        /// <summary>Which building this is. Turrets read their whole spec - diet, rate, damage,
        /// range - back out of it, so one field covers the kind and its numbers.</summary>
        public BuildKind Build;

        /// <summary>Drill and decomposer: the cell they push out to. Pipe: the direction it carries
        /// across. Splitter: unused - its ports are read off the belts around it. Sorter: which side
        /// the filtered shape leaves by; the other shapes leave by the other sides. Turret: where its
        /// barrel rests when it has no target.</summary>
        public Dir Direction;

        /// <summary>Seconds until this machine may act again. Pipe: the remaining transit time of the
        /// item inside. Decomposer: the remaining split time of the circle inside.</summary>
        public float Cooldown;

        /// <summary>What the machine is holding: a decomposer's circle being split, a pipe's item in
        /// transit, a splitter's buffered item, or None.</summary>
        public ShapeType Carried;

        /// <summary>Decomposer: outputs made but not yet pushed onto the belt, 0..the recipe's buffer
        /// (4 for the shipped decomposer).</summary>
        public int OutputCount;

        /// <summary>Splitter and sorter: the port the next item tries first, so a balanced hub rotates
        /// instead of always favouring the lowest-numbered output.</summary>
        public int Rotation;

        /// <summary>Turret: unit vector the barrel points at, updated every tick.</summary>
        public Vec2 Aim;

        /// <summary>Turret: the enemy it is currently shooting at, 0 for none. Shots home on the id,
        /// and <see cref="TargetPosition"/> is where that enemy was when the shot was fired.</summary>
        public int TargetId;

        public Vec2 TargetPosition;

        /// <summary>Turret: an enemy is in range this tick.</summary>
        public bool HasTarget;

        /// <summary>Turret: the right shape is waiting on one of its input belts, so it can fire the
        /// moment there is something to shoot. False is the "silenced" state the twist is about.</summary>
        public bool Armed;

        /// <summary>Health enemies have left to chew through. Initialized on placement from the
        /// building's <see cref="MachineDef.MaxHp"/>; a definition with MaxHp 0 leaves this at 0 and is
        /// never targeted.</summary>
        public float Hp;
    }

    /// <summary>One machine, flattened for the view layer.</summary>
    public struct MachineSnapshot
    {
        public Int2 Cell;
        public TileKind Kind;

        /// <summary>Which building this is, and which behaviour drives it. Both are carried so the view
        /// can dispatch on the behaviour ("draw a converter") while still resolving the per-building
        /// look override the player authored, without looking either up per frame.</summary>
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

        /// <summary>What the building draws on itself: a turret's diet, a sorter's filter shape (the
        /// badge that says which shape goes which way), a decomposer's pending halves (or the circle
        /// being split), a pipe or splitter's buffered item, None when idle.</summary>
        public ShapeType Shape;

        /// <summary>Work-in-progress fraction for the view to animate: a decomposer's split, a pipe's
        /// transit across its tile. 0 for machines with nothing in flight.</summary>
        public float Work;

        /// <summary>Sides a belt feeds this machine from, and sides it pushes onto - the same sets the
        /// tick acts on, computed once in Core by <see cref="MachineDelivery.PortMasks"/>. The view
        /// draws its stubs and its neighbours' docking lanes from these instead of re-deriving the
        /// rule, so the picture cannot drift from the simulation.</summary>
        public DirMask InMask;
        public DirMask OutMask;
    }

    /// <summary>
    /// Where the machines are and what they are doing. Storage and placement rules, the occupancy
    /// registry the tick walks, and the snapshots the view reads - the tick behaviour itself lives in
    /// the machine behaviours, dispatched by <c>MachineSystem</c>.
    ///
    /// It keeps an explicit <see cref="Occupied"/> list of the cells that hold a machine, so stepping
    /// the machines and counting turrets cost the number of machines and not the number of tiles. The
    /// list is kept in ascending linear-index order rather than insertion order: iteration order is
    /// then the map's, independent of the order the player happened to build in, which is what keeps a
    /// replay of the same commands identical.
    /// </summary>
    public sealed class MachineField
    {
        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly BeltField _belts;
        private readonly ContentDatabase _content;

        /// <summary>The map, for the one tile-rule that is the map's: a wave enters at its doorways, so
        /// no machine may stand on one.</summary>
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

        public bool TryGet(Int2 cell, out MachineState state)
        {
            if (!Has(cell))
            {
                state = default;
                return false;
            }

            state = _state[_grid.Index(cell)];
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

        /// <summary>Mutable ref for the machine behaviours, which update cooldowns, aim and armed
        /// state in place.</summary>
        public ref MachineState RefAt(Int2 cell) => ref _state[_grid.Index(cell)];

        /// <summary>
        /// May a machine of this kind stand here? A drill only on a vein (that is what "mined" means) and
        /// every other machine only on plain ground - a tile carrying a belt is occupied, so the belt
        /// comes out first, which is the whole cost of a belt crossing a vein - and no machine at all on a
        /// wave's doorway, the one place it would seal a wave inside itself. A belt stays legal there
        /// because only something solid would plug a door.
        ///
        /// The ground half of the rule is <see cref="TerrainField.CanPlaceMachine"/>, which is also where
        /// rock, water and the void outside the map refuse every machine at once.
        /// </summary>
        public bool CanPlace(BuildKind build, Int2 cell)
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
            if (!CanPlace(build, cell)) return false;

            MachineDef def = _content.Machine(build);
            int i = _grid.Index(cell);
            _state[i] = new MachineState
            {
                Kind = def.Tile,
                Direction = direction,
                Aim = direction.ToVec(),
                Build = build,
                Hp = def.MaxHp,
            };
            _grid.Set(cell, def.Tile);
            AddOccupied(i);
            Revision++;
            return true;
        }

        /// <summary>Remove a machine and put the tile back to its ground.</summary>
        public bool TryRemove(Int2 cell)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            _state[i] = default;
            RemoveOccupied(i);
            _terrain.Release(cell);
            Revision++;
            return true;
        }

        /// <summary>
        /// Apply enemy damage to the machine at a cell. Reports <see cref="SimEventKind.BuildingDamaged"/>
        /// and, when the last of its HP goes, <see cref="SimEventKind.BuildingDestroyed"/> - then removes
        /// the machine exactly as <see cref="TryRemove"/> does (terrain restored, carried item lost) but
        /// with no refund: destruction is not the player cashing out.
        ///
        /// False when there is nothing attackable there (no machine, or a definition with MaxHp 0, which
        /// is indestructible - see <see cref="MachineDef.MaxHp"/>). Returns true iff the building died.
        /// </summary>
        public bool DamageAt(Int2 cell, float damage)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            ref MachineState m = ref _state[i];
            if (_content.Machine(m.Build).MaxHp <= 0f) return false;

            m.Hp -= damage;
            _events.BuildingDamaged(cell, m.Build, damage);
            if (m.Hp > 0f) return false;

            _events.BuildingDestroyed(cell, m.Build);
            _state[i] = default;
            RemoveOccupied(i);
            _terrain.Release(cell);
            Revision++;
            return true;
        }

        /// <summary>
        /// The nearest attackable machine (<see cref="MachineDef.MaxHp"/> above 0) to
        /// <paramref name="centre"/> within <paramref name="range"/>. Ties go to the later occupied
        /// cell - the map's order, not the caller's - because the scan keeps overwriting on an exact
        /// tie, so the highest-index match wins for both the plain and the turret-preferring pick.
        /// With <paramref name="preferTurrets"/>, a standing
        /// turret is charged in preference to any other machine in the same radius - the wave hunts the
        /// gun before the granary, because the gun is what is emptying the wave into the ground; the
        /// gun chosen is the nearest one, and single-machine cases read exactly as the plain rule does.
        /// False when nothing is in range, in which case the output is unusable.
        /// </summary>
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
        /// content table whether a building is a turret rather than testing a tile kind, so the second
        /// gun counts without this method knowing it exists.</summary>
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

        private MachineSnapshot SnapshotOf(in MachineState m, Int2 cell)
        {
            MachineDelivery.PortMasks(_belts, _content, cell, in m, out DirMask inMask, out DirMask outMask);
            MachineDef def = _content.Machine(m.Build);

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
                Shape = MachineReading.Badge(m, def, _content),
                Work = MachineReading.Work(m, def, _content),
                InMask = inMask,
                OutMask = outMask,
            };
        }

        /// <summary>Insert an occupied cell, keeping the list sorted. Occupancy is monotone per cell,
        /// so a cell can never be added twice.</summary>
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
