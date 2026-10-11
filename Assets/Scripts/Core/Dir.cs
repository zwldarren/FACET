namespace Facet.Core
{
    /// <summary>Cardinal direction on the tile grid.</summary>
    public enum Dir
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public static class DirExtensions
    {
        /// <summary>Tile step for this direction.</summary>
        public static Int2 Offset(this Dir d)
        {
            switch (d)
            {
                case Dir.North: return new Int2(0, 1);
                case Dir.East: return new Int2(1, 0);
                case Dir.South: return new Int2(0, -1);
                default: return new Int2(-1, 0);
            }
        }

        public static Vec2 ToVec(this Dir d)
        {
            Int2 o = d.Offset();
            return new Vec2(o.X, o.Y);
        }

        /// <summary>The direction facing back the other way. Machines read their input from the cell
        /// on this side of them, so "which way does the belt at my back point" is this comparison.</summary>
        public static Dir Opposite(this Dir d)
        {
            switch (d)
            {
                case Dir.North: return Dir.South;
                case Dir.East: return Dir.West;
                case Dir.South: return Dir.North;
                default: return Dir.East;
            }
        }
    }
}
