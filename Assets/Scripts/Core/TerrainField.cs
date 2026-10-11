using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One tile of ore, flattened for the view layer: where it is and what it yields.</summary>
    public struct OreSnapshot
    {
        public Int2 Cell;
        public ShapeType Shape;
    }

    /// <summary>
    /// The ground: one <see cref="TerrainKind"/> per tile, plus the mineral under the ore tiles.
    ///
    /// This is the layer that makes a map something other than a rectangle. It is stamped once from the
    /// map's data and never moves during a run - it is the map, not a thing that happens on it - so it
    /// needs no revision counter and no tick step, and a restart re-stamps the identical layout.
    ///
    /// <b>Terrain is deliberately not a <see cref="TileKind"/>.</b> The kind array answers "what is
    /// standing on this tile" and this one answers "what is the ground"; a cell holding a drill on a vein
    /// is <see cref="TileKind.Drill"/> above <see cref="TerrainKind.Ore"/>, and both answers survive the
    /// other's changes. Keeping them in one byte was what made every terrain rule a special case of a
    /// building rule - a patch was "not a building but also not nothing" in five different places - and
    /// it is why a second kind of ground used to cost an edit in every one of them.
    ///
    /// The rules that read both layers live here rather than in each caller, so "may a walker stand
    /// here", "may a belt be laid here" and "may a machine be built here" are each one answer.
    /// </summary>
    public sealed class TerrainField
    {
        private readonly TileGrid _grid;

        /// <summary>The ground of every tile, indexed exactly like <see cref="TileGrid"/>.</summary>
        private readonly TerrainKind[] _kind;

        /// <summary>The mineral under an ore tile, meaningless anywhere else.</summary>
        private readonly ShapeType[] _shape;

        public TerrainField(TileGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _kind = new TerrainKind[grid.Width * grid.Height];
            _shape = new ShapeType[grid.Width * grid.Height];
        }

        /// <summary>
        /// The ground at a cell. Out of bounds reads as <see cref="TerrainKind.Void"/> rather than as
        /// ground: every caller that asks is asking whether something may be there, and off the map the
        /// answer is no.
        /// </summary>
        public TerrainKind KindAt(Int2 cell)
            => _grid.InBounds(cell) ? _kind[_grid.Index(cell)] : TerrainKind.Void;

        public bool HasOre(Int2 cell) => KindAt(cell) == TerrainKind.Ore;

        /// <summary>The mineral a cell yields, or <see cref="ShapeType.None"/> where the ground is not
        /// ore. The one answer to "what is under there", so the drill, the icon in the ground and the
        /// read cursor cannot disagree.</summary>
        public ShapeType OreAt(Int2 cell)
        {
            if (KindAt(cell) != TerrainKind.Ore) return ShapeType.None;
            return _shape[_grid.Index(cell)];
        }

        /// <summary>
        /// May a walker stand here? Both halves of the rule in one place: the ground has to be open, and
        /// nothing solid may be standing on it. The Core's footprint is open ground - reaching it is the
        /// wave's whole purpose - while a belt is open too, because a conveyor is flat.
        /// </summary>
        public bool Walkable(Int2 cell)
            => _grid.InBounds(cell) && !_grid.Get(cell).IsMachine() && KindAt(cell).Walkable();

        /// <summary>
        /// May transport be laid here? In bounds, nothing standing there, and ground that takes one -
        /// which is ground and ore, so a belt run can cross a vein.
        ///
        /// Ore qualifying is the point rather than a technicality. A drill pushes its item onto the tile
        /// beside it, so inside a wider vein every neighbour is ore - a 3x3 patch was nine cells of ore
        /// with the middle one unworkable, and the drill standing on it mined nothing forever with no way
        /// for the player to see why. Transport crosses ore; only the machine rule
        /// (<see cref="CanPlaceMachine"/>) reserves a patch for the drill that mines it.
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
            // leaving the shape behind would let a re-stamp hand back a patch nobody drew.
            if (kind != TerrainKind.Ore) _shape[i] = ShapeType.None;
        }

        /// <summary>Stamp an ore tile: the ground a drill mines. The one place ore and its mineral are
        /// written together, so "is this ore" and "what does it yield" can never disagree.</summary>
        public void SetOre(Int2 cell, ShapeType shape)
        {
            if (!_grid.InBounds(cell) || !shape.IsShape()) return;

            int i = _grid.Index(cell);
            _kind[i] = TerrainKind.Ore;
            _shape[i] = shape;
        }

        /// <summary>
        /// Put a tile back to bare ground after whatever stood on it is removed. This is the whole of
        /// what a removal owes the map now: terrain lives in this field and is never written over, so
        /// handing the tile back is one line instead of the "was there a patch under here?" bookkeeping
        /// that a belt crossing a vein used to need.
        /// </summary>
        public void Release(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return;
            _grid.Set(cell, TileKind.Empty);
        }

        /// <summary>
        /// Stamp a map's ground: its terrain array and its patches, through the map's own rule
        /// (<see cref="MapDefinition.StampTerrain"/>) - which is also the rule
        /// <see cref="MapValidation"/> walks, so the map a designer is told about and the map a world is
        /// built from are the same map by construction.
        ///
        /// Called at world construction and again on restart, and it starts from plain ground: a restart
        /// re-stamps the identical layout, which is what makes "the ground never moves" true of a restart
        /// as well as of a tick. Nothing here depends on the Core having been placed first - the Core's
        /// tiles come from the map's own <see cref="MapDefinition.CoreCell"/> - and the Core keeps them
        /// whatever the ground says, because a rock drawn over the thing the player defends would be a
        /// map that cannot be played.
        /// </summary>
        public void Stamp(MapDefinition map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Clear();
            map.StampTerrain(_kind, _shape);
        }

        /// <summary>Wipe every tile back to plain ground. Only used by a restart, which re-stamps the
        /// map immediately afterwards.</summary>
        public void Clear()
        {
            Array.Clear(_kind, 0, _kind.Length);
            Array.Clear(_shape, 0, _shape.Length);
        }

        /// <summary>
        /// Every tile of ore with its mineral, in stable linear-index order - the map of where each
        /// resource lives, which is what the ground's icons are drawn from. Ore is asked for by name
        /// rather than filtered out of "all terrain" because the icons are the only reader: rock, water
        /// and the void are drawn straight off the grid by their own view.
        /// </summary>
        public void GetOre(List<OreSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _kind.Length; i++)
            {
                if (_kind[i] != TerrainKind.Ore) continue;
                if (!_shape[i].IsShape()) continue;

                into.Add(new OreSnapshot { Cell = _grid.CellOf(i), Shape = _shape[i] });
            }
        }
    }
}
