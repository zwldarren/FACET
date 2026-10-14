using System;

namespace Facet.Core
{
    /// <summary>
    /// The tile grid: what occupies every tile, plus world&lt;-&gt;tile conversion. Map space is
    /// [0,Width] x [0,Height] world units, so tile (x,y) covers [x,x+1] x [y,y+1] with centre at
    /// (x+0.5, y+0.5). This is the occupancy layer only; the ground itself is <see cref="TerrainField"/>.
    /// </summary>
    public sealed class TileGrid
    {
        public readonly int Width;
        public readonly int Height;

        private readonly TileKind[] _tiles;

        public TileGrid(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
            _tiles = new TileKind[width * height];
        }

        public bool InBounds(Int2 c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public TileKind Get(Int2 c) => InBounds(c) ? _tiles[Index(c)] : TileKind.Empty;

        public void Set(Int2 c, TileKind kind)
        {
            if (!InBounds(c)) return;
            _tiles[Index(c)] = kind;
        }

        /// <summary>True when something already occupies the tile as a building.</summary>
        public bool IsOccupied(Int2 c) => Get(c).IsBuilding();

        /// <summary>Wipe every tile back to empty ground. Only used by a restart, which re-stamps the
        /// terrain immediately afterwards.</summary>
        public void Clear() => Array.Clear(_tiles, 0, _tiles.Length);

        /// <summary>Centre of a tile in world units.</summary>
        public Vec2 CellCenter(Int2 c) => new Vec2(c.X + 0.5f, c.Y + 0.5f);

        /// <summary>Tile containing a world position. Out-of-bounds results are not clamped.</summary>
        public Int2 CellAt(Vec2 world)
        {
            return new Int2((int)MathF.Floor(world.X), (int)MathF.Floor(world.Y));
        }

        /// <summary>Linear index of a cell. Every field stores per-tile state in this same layout, so no
        /// field repeats the coordinate arithmetic. Bounds are the caller's business (see <see cref="Get"/>).</summary>
        internal int Index(Int2 c) => c.Y * Width + c.X;

        /// <summary>The cell at a linear index - the inverse of <see cref="Index"/>.</summary>
        internal Int2 CellOf(int index) => new Int2(index % Width, index / Width);
    }
}
