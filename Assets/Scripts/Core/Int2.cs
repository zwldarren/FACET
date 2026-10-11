using System;

namespace Facet.Core
{
    /// <summary>
    /// Integer tile coordinate on the map. (0,0) is the bottom-left tile.
    /// Deliberately NOT UnityEngine.Vector2Int so the simulation layer stays engine-free.
    /// </summary>
    [Serializable]
    public readonly struct Int2 : IEquatable<Int2>
    {
        public readonly int X;
        public readonly int Y;

        public Int2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.X + b.X, a.Y + b.Y);
        public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.X - b.X, a.Y - b.Y);
        public static Int2 operator *(Int2 a, int s) => new Int2(a.X * s, a.Y * s);
        public static bool operator ==(Int2 a, Int2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Int2 a, Int2 b) => !(a == b);

        public bool Equals(Int2 other) => this == other;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
