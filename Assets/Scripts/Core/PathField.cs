using System;

namespace Facet.Core
{
    /// <summary>
    /// The way to the Core from every walkable cell: a breadth-first flood from the Core's footprint
    /// over cells a walker may enter, storing per cell the one step that moves toward the Core.
    ///
    /// "A walker may enter" is <see cref="TerrainField.Walkable"/> and nothing else, which is the point:
    /// the flood used to test one thing (is a machine here) and now tests the whole rule, so rock, water
    /// and the void outside the map are walls to it exactly as a building is. A map's own ground
    /// therefore decides which way a wave walks before the player has built anything - which is what
    /// makes a canyon a defence rather than scenery.
    ///
    /// Recomputed lazily when <see cref="MachineField.Revision"/> says the solidity changed: a BFS over
    /// the whole map costs microseconds and only runs on ticks where a building came or went (including
    /// a restart, which clears the field and so bumps the revision). Terrain deliberately has no
    /// revision of its own - it is fixed for the world's lifetime, so it cannot invalidate this.
    ///
    /// The flood stores, per cell, the direction back toward the Core rather than a full path: enemies
    /// only ever need "which way from here", so one byte per cell replaces a path per enemy. Cells the
    /// flood never reached - sealed in by machines or by rock, or out of bounds - read as no direction,
    /// which is what lets an enemy tell "walk this way" apart from "there is no way" and start chewing
    /// instead.
    /// </summary>
    public sealed class PathField
    {
        /// <summary>Relaxation order: the <see cref="Dir"/> enum's own order, N,E,S,W. Fixed so a
        /// rebuilt field is bit-identical to the one before it.</summary>
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly MachineField _machines;

        /// <summary>Steps to the Core, or -1 for a cell the flood never reached.</summary>
        private readonly int[] _distance;

        /// <summary>The step from this cell toward the Core: 0 for none, otherwise
        /// <c>(byte)Dir + 1</c> so the default of the array means "no direction".</summary>
        private readonly byte[] _toward;

        /// <summary>BFS frontier, a ring over the grid's cells - each cell enters it at most once, so
        /// its capacity is the cell count. Kept between rebuilds so a rebuild allocates nothing.</summary>
        private readonly int[] _queue;

        /// <summary>The machine revision the current field was built from. -1 forces the first build;
        /// <see cref="MachineField.Revision"/> only ever counts up, so the two can be compared.</summary>
        private int _builtAt = -1;

        public PathField(TileGrid grid, TerrainField terrain, MachineField machines)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));

            int cells = grid.Width * grid.Height;
            _distance = new int[cells];
            _toward = new byte[cells];
            _queue = new int[cells];
        }

        /// <summary>Rebuild the field if the buildings have changed since the last rebuild, and do
        /// nothing otherwise - the common case, since a tick usually places nothing.</summary>
        public void Sync()
        {
            if (_builtAt == _machines.Revision) return;
            _builtAt = _machines.Revision;

            Array.Fill(_distance, -1);
            Array.Fill(_toward, (byte)0);

            int head = 0;
            int tail = 0;

            // Seed: the Core's whole footprint, distance 0. Terrain does not change at runtime, but
            // scanning for the seeds rather than remembering them keeps this field from holding a
            // second copy of where the Core is.
            for (int i = 0; i < _distance.Length; i++)
            {
                if (_grid.Get(_grid.CellOf(i)) != TileKind.Core) continue;
                _distance[i] = 0;
                _queue[tail++] = i;
            }

            while (head < tail)
            {
                int current = _queue[head++];
                Int2 cell = _grid.CellOf(current);
                int next = _distance[current] + 1;

                for (int k = 0; k < Sides.Length; k++)
                {
                    Dir side = Sides[k];
                    Int2 neighbour = cell + side.Offset();
                    if (!_terrain.Walkable(neighbour)) continue;

                    int index = _grid.Index(neighbour);
                    if (_distance[index] >= 0) continue;

                    _distance[index] = next;
                    // The step toward the Core from the neighbour is back the way we came.
                    _toward[index] = (byte)((byte)side.Opposite() + 1);
                    _queue[tail++] = index;
                }
            }
        }

        /// <summary>
        /// The one step toward the Core from a cell. False when there is no way from there: out of
        /// bounds, solid, or walled off from the Core - the caller's cue to attack the obstruction
        /// rather than walk at it.
        /// </summary>
        public bool TryDirection(Int2 cell, out Dir direction)
        {
            if (!_grid.InBounds(cell))
            {
                direction = Dir.North;
                return false;
            }

            byte toward = _toward[_grid.Index(cell)];
            direction = toward == 0 ? Dir.North : (Dir)(toward - 1);
            return toward != 0;
        }
    }
}
