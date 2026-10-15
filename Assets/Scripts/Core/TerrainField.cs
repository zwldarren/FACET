using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One tile of ore, flattened for the view layer: where it is and what it yields.</summary>
    public struct OreSnapshot
    {
        public Int2 Cell;
        public Quad Mineral;
    }

    /// <summary>
    /// The ground: one <see cref="TerrainKind"/> per tile, plus the mineral under the ore tiles.
    ///
    /// Stamped once from the map's data and never moved during a run - it is the map, not a thing that
    /// happens on it - so it needs no revision counter and no tick step, and a restart re-stamps the
    /// identical layout.
    ///
    /// <b>Terrain is deliberately not a <see cref="TileKind"/>.</b> The kind array answers "what is
    /// standing on this tile"; this one answers "what is the ground". A drill on a vein is
    /// <see cref="TileKind.Drill"/> above <see cref="TerrainKind.Ore"/>, and both answers survive the
    /// other's changes - keeping them in one byte made every terrain rule a special case of a building
    /// rule.
    ///
    /// The rules that read both layers live here, so "may a walker stand here", "may a belt be laid
    /// here" and "may a machine be built here" are each one answer.
    /// </summary>
    public sealed class TerrainField
    {
        private readonly TileGrid _grid;

        /// <summary>The ground of every tile, indexed exactly like <see cref="TileGrid"/>.</summary>
        private readonly TerrainKind[] _kind;

        /// <summary>The mineral under an ore tile, meaningless anywhere else.</summary>
        private readonly Quad[] _mineral;

        public TerrainField(TileGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _kind = new TerrainKind[grid.Width * grid.Height];
            _mineral = new Quad[grid.Width * grid.Height];
        }

        /// <summary>
        /// The ground at a cell. Out of bounds reads as <see cref="TerrainKind.Void"/>: every caller is
        /// asking whether something may be there, and off the map the answer is no.
        /// </summary>
        public TerrainKind KindAt(Int2 cell)
            => _grid.InBounds(cell) ? _kind[_grid.Index(cell)] : TerrainKind.Void;

        public bool HasOre(Int2 cell) => KindAt(cell) == TerrainKind.Ore;

        /// <summary>The mineral a cell yields, or <see cref="Quad.Empty"/> where the ground is not
        /// ore. The one answer to "what is under there", so the drill, the icon in the ground and the
        /// read cursor cannot disagree.</summary>
        public Quad OreAt(Int2 cell)
        {
            if (KindAt(cell) != TerrainKind.Ore) return Quad.Empty;
            return _mineral[_grid.Index(cell)];
        }

        /// <summary>
        /// Is this cell's ground crossable at all, whatever stands on it? The half of
        /// <see cref="Walkable"/> that is about the map rather than about what a player built, asked by
        /// the one reader that prices a building instead of refusing it
        /// (<see cref="PathField"/>'s breach flood). One statement of "the ground is open", so the two
        /// questions cannot come apart: rock, water and the void outside the map are the map's own walls.
        /// </summary>
        public bool GroundWalkable(Int2 cell)
            => _grid.InBounds(cell) && KindAt(cell).Walkable();

        /// <summary>
        /// May a walker stand here? The ground has to be open and nothing solid may stand on it. The
        /// Core's footprint is open ground - reaching it is the wave's purpose - and a belt is open too,
        /// because a conveyor is flat.
        /// </summary>
        public bool Walkable(Int2 cell)
            => GroundWalkable(cell) && !_grid.Get(cell).IsMachine();

        /// <summary>
        /// May transport be laid here? In bounds, nothing standing there, and ground that takes one -
        /// ground and ore, so a belt run can cross a vein.
        ///
        /// Ore qualifying is the point, not a technicality: a drill pushes its item onto the tile beside
        /// it, so inside a wider vein every neighbour is ore. Transport crosses ore; only the machine
        /// rule (<see cref="CanPlaceMachine"/>) reserves a patch for the drill that mines it.
        /// </summary>
        public bool CanLayBelt(Int2 cell)
            => _grid.InBounds(cell) && _grid.Get(cell) == TileKind.Empty && KindAt(cell).AllowsBelt();

        /// <summary>
        /// May a machine be built here? <paramref name="needsOre"/> is the drill's one difference: a drill
        /// is the only machine that may stand on a vein (that is what "mined" means), and a vein is the
        /// only ground that takes it. Every other machine wants plain ground, so rock, water and the void
        /// take nothing at all.
        /// </summary>
        public bool CanPlaceMachine(Int2 cell, bool needsOre)
        {
            if (!_grid.InBounds(cell) || _grid.Get(cell) != TileKind.Empty) return false;

            TerrainKind kind = KindAt(cell);
            return needsOre ? kind == TerrainKind.Ore : kind.AllowsMachine();
        }

        /// <summary>
        /// Set a tile's ground. Authoring, not gameplay: the map's own stamping goes through
        /// <see cref="Stamp"/>, and nothing in a running tick calls this - terrain is the one thing a
        /// player action can never touch.
        /// </summary>
        public void Set(Int2 cell, TerrainKind kind)
        {
            if (!_grid.InBounds(cell)) return;

            int i = _grid.Index(cell);
            _kind[i] = kind;

            // The mineral is only meaningful under ore, so a tile that stops being ore stops yielding:
            // leaving it behind would let a re-stamp hand back a patch nobody drew.
            if (kind != TerrainKind.Ore) _mineral[i] = Quad.Empty;
        }

        /// <summary>Stamp an ore tile: the ground a drill mines. The one place ore and its mineral are
        /// written together, so "is this ore" and "what does it yield" can never disagree.</summary>
        public void SetOre(Int2 cell, Quad mineral)
        {
            if (!_grid.InBounds(cell) || mineral == Quad.Empty) return;

            int i = _grid.Index(cell);
            _kind[i] = TerrainKind.Ore;
            _mineral[i] = mineral;
        }

        /// <summary>
        /// Put a tile back to bare ground after whatever stood on it is removed. Terrain lives in this
        /// field and is never written over, so a removal owes the map exactly this one line - no "was
        /// there a patch under here?" bookkeeping.
        /// </summary>
        public void Release(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return;
            _grid.Set(cell, TileKind.Empty);
        }

        /// <summary>
        /// Stamp a map's ground - terrain array and patches - through the map's own rule
        /// (<see cref="MapDefinition.StampTerrain"/>), the same rule <see cref="MapValidation"/> walks, so
        /// the map a designer is told about and the map a world is built from are one by construction.
        ///
        /// Called at construction and on restart, starting from plain ground, so a restart re-stamps the
        /// identical layout. Nothing here depends on the Core being placed first - its tiles come from the
        /// map's own <see cref="MapDefinition.CoreCell"/> and it keeps them whatever the ground says,
        /// because rock over the thing the player defends would be a map that cannot be played.
        /// </summary>
        public void Stamp(MapDefinition map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Clear();
            map.StampTerrain(_kind, _mineral);
        }

        /// <summary>Wipe every tile back to plain ground. Only used by a restart, which re-stamps the
        /// map immediately afterwards.</summary>
        public void Clear()
        {
            Array.Clear(_kind, 0, _kind.Length);
            Array.Clear(_mineral, 0, _mineral.Length);
        }

        /// <summary>
        /// Every tile of ore with its mineral, in stable linear-index order - the map the ground's icons
        /// are drawn from. Ore is asked for by name because the icons are the only reader; rock, water
        /// and the void are drawn straight off the grid by their own view.
        /// </summary>
        public void GetOre(List<OreSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _kind.Length; i++)
            {
                if (_kind[i] != TerrainKind.Ore) continue;
                if (_mineral[i] == Quad.Empty) continue;

                into.Add(new OreSnapshot { Cell = _grid.CellOf(i), Mineral = _mineral[i] });
            }
        }
    }
}
