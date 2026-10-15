using System;

namespace Facet.Core
{
    /// <summary>The way to the Core from every walkable cell, in three copies: the plain shortest way (a
    /// breadth-first flood from the Core's footprint, same queue and relaxation order, so a world with no
    /// turret walks bit-for-bit as it always did), the way a wave with sense would rather walk (a
    /// Dijkstra over the same ground where a cell under a standing turret's fire costs
    /// <see cref="Balance.PathThreatWeight"/> extra), and the way in when there is no way round (a
    /// Dijkstra where a cell carrying a building costs <see cref="Balance.PathBreachWeight"/>). Storing
    /// them lets the wave's one judgement be made once, whole: <see cref="FlankAt"/> weighs the open
    /// route against the covered one at the cell a walker starts from, the walker then follows its chosen
    /// field (<see cref="TryDirection"/>), and a walker whose field has no direction at all - the Core
    /// walled off behind the player's buildings - follows the breach field instead
    /// (<see cref="TryBreach"/>).
    ///
    /// "A walker may enter" is <see cref="TerrainField.Walkable"/> and nothing else, so rock, water and void
    /// are walls exactly as a building is - a map's own ground decides the way before the player builds,
    /// which makes a canyon a defence rather than scenery. The weighting is the wave's one piece of
    /// judgement: it will not walk far out of its way to dodge a gun it could overrun (the flanking rule
    /// demands a real margin), but a corridor swept end to end is flanked. The threat is asked of the
    /// machines, not the shots: a standing turret projects its range whether or not it is fed; belts and
    /// walls project nothing. The breach is the one field where the ground rule is relaxed: a building is
    /// crossable there at a price, so a wave sealed in walks to the cheapest hole and tears it, while
    /// rock - which nothing may chew - stays a wall to the end, and so does a building nothing may attack
    /// (<see cref="MachineDef.MaxHp"/> of 0), which is the same promise by another name.
    ///
    /// Recomputed lazily when <see cref="MachineField.Revision"/> says solidity changed; terrain is fixed for
    /// the world's lifetime, so it cannot invalidate this. Each cell stores the direction back toward the
    /// Core rather than a full path - enemies only need "which way from here" - and a cell no flood reached
    /// reads as no direction, which lets an enemy tell "walk this way" from "there is no way".</summary>
    public sealed class PathField
    {
        /// <summary>Relaxation order: the <see cref="Dir"/> enum's own order, N,E,S,W. Fixed so a
        /// rebuilt field is bit-identical to the one before it.</summary>
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly MachineField _machines;
        private readonly ContentDatabase _content;

        /// <summary>The plain shortest way: steps to the Core, or -1 for a cell the flood never reached.
        /// The field every enemy has always walked, and the one a walker falls back to when
        /// <see cref="FlankAt"/> says the open way does not pay for its swing.</summary>
        private readonly int[] _directCost;
        private readonly byte[] _directToward;

        /// <summary>What the plain shortest way <em>costs</em> once the guns are counted: the chain of
        /// steps the direct field prescribes, each priced 1 plus its cell's threat. Priced as the flood
        /// discovers each cell (the parent is final by then, breadth-first), so the flank rule compares the
        /// weighted route against the price of the way the wave would have walked - not a bare step count
        /// that would credit a flank for dodging fire the direct way never intended to eat.</summary>
        private readonly int[] _directWeighted;

        /// <summary>The threat-weighted way: the cost of the cheapest route that pays a covered cell
        /// <see cref="Balance.PathThreatWeight"/>, and the step it starts with.</summary>
        private readonly int[] _flankCost;
        private readonly byte[] _flankToward;

        /// <summary>The breach: the cost of the cheapest route that walks through the player's buildings
        /// at <see cref="Balance.PathBreachWeight"/> a cell, and the step it starts with. Read only by a
        /// walker no other field has a direction for.</summary>
        private readonly int[] _breachCost;
        private readonly byte[] _breachToward;

        /// <summary>1 where a standing turret's range covers the cell's centre, 0 elsewhere. Written before
        /// every rebuild - threat moves with the guns, so it is not a terrain fact.</summary>
        private readonly byte[] _threat;

        /// <summary>The direct flood's frontier: a ring over the grid's cells - each enters at most once,
        /// so its capacity is the cell count. Kept between rebuilds so a rebuild allocates nothing. The
        /// queue order is the breadth-first order the field has always been built in, which keeps a
        /// threat-free walk identical to the one this game has always simulated.</summary>
        private readonly int[] _queue;

        /// <summary>The weighted flood's frontier: a binary min-heap over (cell, cost), lazily deleted - a
        /// pop whose cost is stale against <see cref="_flankCost"/> is skipped rather than repaired. Each
        /// cell is pushed at most once per incoming edge (a push is a strict improvement), so 4 entries per
        /// cell is a bound; the arrays are kept between rebuilds.</summary>
        private int[] _heapCell;
        private int[] _heapCost;
        private int _heapCount;

        /// <summary>The machine revision the current field was built from. -1 forces the first build;
        /// <see cref="MachineField.Revision"/> only ever counts up, so the two can be compared.</summary>
        private int _builtAt = -1;

        public PathField(TileGrid grid, TerrainField terrain, MachineField machines, ContentDatabase content)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _content = content ?? throw new ArgumentNullException(nameof(content));

            int cells = grid.Width * grid.Height;
            _directCost = new int[cells];
            _directToward = new byte[cells];
            _directWeighted = new int[cells];
            _flankCost = new int[cells];
            _flankToward = new byte[cells];
            _breachCost = new int[cells];
            _breachToward = new byte[cells];
            _threat = new byte[cells];
            _queue = new int[cells];
            _heapCell = new int[4 * cells];
            _heapCost = new int[4 * cells];
        }

        /// <summary>Rebuild every field if the buildings have changed since the last rebuild, and do
        /// nothing otherwise - the common case, since a tick usually places nothing.</summary>
        public void Sync()
        {
            if (_builtAt == _machines.Revision) return;
            _builtAt = _machines.Revision;

            MarkThreat();
            FloodDirect();
            FloodFlank();
            FloodBreach();
        }

        /// <summary>Is the open way the one worth walking from this cell? True when the threat-weighted
        /// route undercuts what the plain shortest way would itself pay under fire - by more than
        /// <see cref="Balance.PathDetourFactor"/>'s shave, with <see cref="Balance.PathDetourSlack"/> steps of
        /// slop - the wave's whole flanking judgement, made once at the cell it starts from: a walker that
        /// sets out flanking keeps flanking, one that sets out to overrun keeps overrunning, so the fields
        /// are never mixed and the walk cannot dither. False - walk the plain way - when there is no gun, the
        /// covered route is cheaper anyway, or this cell is sealed off.</summary>
        public bool FlankAt(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return false;

            int index = _grid.Index(cell);
            if (_directCost[index] < 0 || _flankCost[index] < 0) return false;

            // The flank has to be an actual saving: with no gun there is nothing to undercut, and a
            // slack-only win would send a threat-free walk down the flank field, whose heap tie-breaking
            // need not match the direct flood's. The budget test does the detour judgement - a skirt that
            // only saves a step or two inside the slack is overrun, not flanked.
            int flankCost = _flankCost[index];
            int directWeighted = _directWeighted[index];
            return flankCost < directWeighted
                && flankCost < directWeighted * Balance.PathDetourFactor + Balance.PathDetourSlack;
        }

        /// <summary>The one step toward the Core from a cell, along the field the walker chose at
        /// <see cref="FlankAt"/>. False when there is no way from there: out of bounds, solid, or walled off
        /// from the Core - the caller's cue to attack the obstruction rather than walk at it.</summary>
        public bool TryDirection(Int2 cell, bool flank, out Dir direction)
        {
            if (!_grid.InBounds(cell))
            {
                direction = Dir.North;
                return false;
            }

            int index = _grid.Index(cell);
            byte toward = flank ? _flankToward[index] : _directToward[index];
            direction = toward == 0 ? Dir.North : (Dir)(toward - 1);
            return toward != 0;
        }

        /// <summary>The way in for a walker both other fields have no direction for: the first step of the
        /// cheapest route that carries it <em>through</em> the player's buildings, at
        /// <see cref="Balance.PathBreachWeight"/> a building cell. False when even that route does not
        /// exist - the map's own rock has sealed this cell off from the Core, and no amount of chewing
        /// opens it - which is the caller's cue to press on as it always did.</summary>
        public bool TryBreach(Int2 cell, out Dir direction)
        {
            if (!_grid.InBounds(cell))
            {
                direction = Dir.North;
                return false;
            }

            int index = _grid.Index(cell);
            byte toward = _breachToward[index];
            direction = toward == 0 ? Dir.North : (Dir)(toward - 1);
            return toward != 0;
        }

        /// <summary>Stamp <see cref="_threat"/> from the standing turrets: every cell whose centre sits
        /// inside a gun's range is under fire. The comparison is the turret's own - a cell centre against a
        /// range radius - so a lane the wave avoids is one the turret could shoot into, and the two fields
        /// cannot disagree about "covered".</summary>
        private void MarkThreat()
        {
            Array.Clear(_threat, 0, _threat.Length);

            var occupied = _machines.Occupied;
            for (int k = 0; k < occupied.Count; k++)
            {
                Int2 at = _machines.CellAt(k);
                if (!_machines.TryGet(at, out MachineState machine)) continue;
                if (!_content.IsTurret(machine.Build)) continue;

                float range = _content.Turret(machine.Build).Range;
                float rangeSq = range * range;
                Vec2 centre = _grid.CellCenter(at);
                int reach = (int)MathF.Ceiling(range);

                int minX = Math.Max(0, at.X - reach);
                int maxX = Math.Min(_grid.Width - 1, at.X + reach);
                int minY = Math.Max(0, at.Y - reach);
                int maxY = Math.Min(_grid.Height - 1, at.Y + reach);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Int2 cell = new Int2(x, y);
                        Vec2 delta = _grid.CellCenter(cell) - centre;
                        if (delta.X * delta.X + delta.Y * delta.Y <= rangeSq)
                            _threat[_grid.Index(cell)] = 1;
                    }
                }
            }
        }

        /// <summary>The plain flood, as it has always run: breadth-first from the Core's whole footprint
        /// (cost 0) over walkable cells, each first discovery writing the step back and, alongside it, the
        /// cell's price under fire (1 plus its own threat, on top of the parent's price, fixed when the parent
        /// was discovered - breadth-first prices a tree root-first, so no second pass is owed). The queue
        /// order fixes which of two equal-length parents a cell keeps, so this flood is bit-identical to the
        /// one this game has always simulated.</summary>
        private void FloodDirect()
        {
            Array.Fill(_directCost, -1);
            Array.Fill(_directToward, (byte)0);
            Array.Fill(_directWeighted, -1);

            int head = 0;
            int tail = 0;

            // Seed: the Core's whole footprint, cost 0. Terrain does not change at runtime, but scanning
            // for the seeds rather than remembering them keeps this field from holding a second copy of
            // where the Core is.
            for (int i = 0; i < _directCost.Length; i++)
            {
                if (_grid.Get(_grid.CellOf(i)) != TileKind.Core) continue;
                _directCost[i] = 0;
                _directWeighted[i] = 0;
                _queue[tail++] = i;
            }

            while (head < tail)
            {
                int current = _queue[head++];
                Int2 cell = _grid.CellOf(current);
                int next = _directCost[current] + 1;

                for (int k = 0; k < Sides.Length; k++)
                {
                    Dir side = Sides[k];
                    Int2 neighbour = cell + side.Offset();
                    if (!_terrain.Walkable(neighbour)) continue;

                    int index = _grid.Index(neighbour);
                    if (_directCost[index] >= 0) continue;

                    _directCost[index] = next;
                    // The step toward the Core from the neighbour is back the way we came; its price is
                    // this step's, under fire, on top of the way the direct field already prescribes from
                    // there.
                    _directToward[index] = (byte)((byte)side.Opposite() + 1);
                    _directWeighted[index] = 1 + _threat[index] * Balance.PathThreatWeight + _directWeighted[current];
                    _queue[tail++] = index;
                }
            }
        }

        /// <summary>The weighted flood: the same walkable ground out of the Core's footprint, a step priced
        /// 1 plus <see cref="Balance.PathThreatWeight"/> where the cell walked into is under fire - a
        /// Dijkstra, because the costs are no longer uniform. The rebuild is deterministic - the same pushes,
        /// sifts and lazy deletions every time - so a rebuilt field is bit-identical to the one before it. The
        /// heap's pop order among equal costs is its own, not the direct flood's FIFO, which is why
        /// <see cref="FlankAt"/> refuses the flank unless it is an actual saving.</summary>
        private void FloodFlank()
        {
            Array.Fill(_flankCost, -1);
            Array.Fill(_flankToward, (byte)0);
            _heapCount = 0;

            for (int i = 0; i < _flankCost.Length; i++)
            {
                if (_grid.Get(_grid.CellOf(i)) != TileKind.Core) continue;
                _flankCost[i] = 0;
                Push(i, 0);
            }

            while (_heapCount > 0)
            {
                int current = Pop(out int popped);
                if (popped > _flankCost[current]) continue;   // a stale entry: the cell found a cheaper way

                Int2 cell = _grid.CellOf(current);

                for (int k = 0; k < Sides.Length; k++)
                {
                    Dir side = Sides[k];
                    Int2 neighbour = cell + side.Offset();
                    if (!_terrain.Walkable(neighbour)) continue;

                    int index = _grid.Index(neighbour);
                    int next = _flankCost[current] + 1 + _threat[index] * Balance.PathThreatWeight;
                    if (_flankCost[index] >= 0 && _flankCost[index] <= next) continue;

                    _flankCost[index] = next;
                    _flankToward[index] = (byte)((byte)side.Opposite() + 1);
                    Push(index, next);
                }
            }
        }

        /// <summary>The breach flood: the way in when there is no way round. The same ground as the other
        /// two fields - rock, water and the void are walls to the end - but a standing building is crossable
        /// at <see cref="Balance.PathBreachWeight"/> a cell, so the field prices the player's wall instead of
        /// refusing it and names the cheapest hole to tear. A building nothing may attack is priced as what
        /// it is, the map's own rock: <see cref="MachineDef.MaxHp"/> of 0 is the siege's opt-out, and a wall
        /// no walker may chew must never be named as the way in.
        ///
        /// Deterministic like the flank flood - the same pushes, sifts and lazy deletions every time - and
        /// the heap is the flank flood's, reset here, because the two never run at once.</summary>
        private void FloodBreach()
        {
            Array.Fill(_breachCost, -1);
            Array.Fill(_breachToward, (byte)0);
            _heapCount = 0;

            for (int i = 0; i < _breachCost.Length; i++)
            {
                if (_grid.Get(_grid.CellOf(i)) != TileKind.Core) continue;
                _breachCost[i] = 0;
                Push(i, 0);
            }

            while (_heapCount > 0)
            {
                int current = Pop(out int popped);
                if (popped > _breachCost[current]) continue;   // a stale entry: the cell found a cheaper way

                Int2 cell = _grid.CellOf(current);

                for (int k = 0; k < Sides.Length; k++)
                {
                    Dir side = Sides[k];
                    Int2 neighbour = cell + side.Offset();
                    if (!_terrain.GroundWalkable(neighbour)) continue;

                    int step = 1;
                    if (_grid.Get(neighbour).IsMachine())
                    {
                        if (!Chewable(neighbour)) continue;
                        step = Balance.PathBreachWeight;
                    }

                    int index = _grid.Index(neighbour);
                    int next = _breachCost[current] + step;
                    if (_breachCost[index] >= 0 && _breachCost[index] <= next) continue;

                    _breachCost[index] = next;
                    _breachToward[index] = (byte)((byte)side.Opposite() + 1);
                    Push(index, next);
                }
            }
        }

        /// <summary>May the walkers tear this building down? The siege's own rule, asked here so the
        /// breach field and the enemy that walks it cannot disagree: a definition with no health is solid,
        /// untargetable and permanent, so there is no point walking at it.</summary>
        private bool Chewable(Int2 cell)
            => _machines.TryGet(cell, out MachineState machine) && _content.Machine(machine.Build).MaxHp > 0f;

        /// <summary>Push a (cell, cost) pair, sifted up. The arrays are sized for the bound of one push per
        /// incoming edge; a world that exceeds it grows them rather than corrupting.</summary>
        private void Push(int cell, int cost)
        {
            if (_heapCount == _heapCell.Length)
            {
                Array.Resize(ref _heapCell, _heapCell.Length * 2);
                Array.Resize(ref _heapCost, _heapCost.Length * 2);
            }

            int i = _heapCount++;
            _heapCell[i] = cell;
            _heapCost[i] = cost;

            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_heapCost[parent] <= _heapCost[i]) break;

                (_heapCell[parent], _heapCell[i]) = (_heapCell[i], _heapCell[parent]);
                (_heapCost[parent], _heapCost[i]) = (_heapCost[i], _heapCost[parent]);
                i = parent;
            }
        }

        /// <summary>Pop the cheapest entry, sifted down, handing back the cost it was pushed with so a
        /// stale entry can be recognised against <see cref="_flankCost"/>.</summary>
        private int Pop(out int cost)
        {
            cost = _heapCost[0];
            int cell = _heapCell[0];
            _heapCount--;

            if (_heapCount > 0)
            {
                _heapCell[0] = _heapCell[_heapCount];
                _heapCost[0] = _heapCost[_heapCount];

                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1;
                    if (left >= _heapCount) break;

                    int child = left + 1 < _heapCount && _heapCost[left + 1] < _heapCost[left]
                        ? left + 1
                        : left;
                    if (_heapCost[i] <= _heapCost[child]) break;

                    (_heapCell[i], _heapCell[child]) = (_heapCell[child], _heapCell[i]);
                    (_heapCost[i], _heapCost[child]) = (_heapCost[child], _heapCost[i]);
                    i = child;
                }
            }

            return cell;
        }
    }
}
