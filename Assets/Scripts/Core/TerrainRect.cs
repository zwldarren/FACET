namespace Facet.Core
{
    /// <summary>One rectangle of terrain, the way a map written in code states it. <see cref="ShapePatch"/>
    /// is the same idea for ore, separate because a patch also names a mineral.</summary>
    public readonly struct TerrainRect
    {
        public readonly Int2 Origin;
        public readonly int Width;
        public readonly int Height;
        public readonly TerrainKind Kind;

        public TerrainRect(Int2 origin, int width, int height, TerrainKind kind)
        {
            Origin = origin;
            Width = width;
            Height = height;
            Kind = kind;
        }
    }

    /// <summary>
    /// Turns rectangles into the cell array a <see cref="MapDefinition"/> carries. The array is the
    /// runtime form and rectangles the authoring form for code-written maps (the tests), where an
    /// 80x48 literal would be unreadable; both end up as the same row-major array.
    /// </summary>
    public static class TerrainLayout
    {
        /// <summary>Build a width x height cell array from rectangles, in order. A later rectangle paints
        /// over an earlier one, so a map reads as layers of paint, and anything off the edge is clipped
        /// rather than refused.</summary>
        public static TerrainKind[] Rects(int width, int height, params TerrainRect[] rects)
        {
            var cells = new TerrainKind[width * height];
            if (rects == null) return cells;

            for (int r = 0; r < rects.Length; r++)
            {
                TerrainRect rect = rects[r];
                for (int y = 0; y < rect.Height; y++)
                {
                    int cy = rect.Origin.Y + y;
                    if (cy < 0 || cy >= height) continue;

                    for (int x = 0; x < rect.Width; x++)
                    {
                        int cx = rect.Origin.X + x;
                        if (cx < 0 || cx >= width) continue;

                        cells[cy * width + cx] = rect.Kind;
                    }
                }
            }

            return cells;
        }
    }
}
