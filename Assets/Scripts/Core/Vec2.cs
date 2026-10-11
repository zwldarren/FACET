using System;

namespace Facet.Core
{
    /// <summary>
    /// Float vector in world units (1 unit = 1 tile). Engine-free replacement for UnityEngine.Vector2.
    /// </summary>
    [Serializable]
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public float Magnitude => MathF.Sqrt(X * X + Y * Y);

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public static float Distance(Vec2 a, Vec2 b) => (b - a).Magnitude;

        public bool Equals(Vec2 other)
        {
            return MathF.Abs(X - other.X) < 1e-6f && MathF.Abs(Y - other.Y) < 1e-6f;
        }

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2);

        public override string ToString() => "(" + X.ToString("0.###") + "," + Y.ToString("0.###") + ")";
    }
}
