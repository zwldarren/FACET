using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The build bar's preview of each <see cref="BuildKind"/>: a small rasterised figure in the palette's
    /// own colours, silhouettes and item marks, showing what a click would place.
    ///
    /// Generated at runtime and cached per kind - the project ships no machine art, and a figure built from
    /// the palette's numbers cannot drift from the mesh. Figures are arranged for a 44-pixel key rather than
    /// copied from the world: a cutter's two-tile footprint would be unreadable at that size.
    /// </summary>
    public sealed class BuildIcons
    {
        /// <summary>A picture of one building: the texture and the region of it that is the figure.</summary>
        public readonly struct Icon
        {
            public readonly Texture2D Texture;
            public readonly Rect Uv;

            public Icon(Texture2D texture, Rect uv)
            {
                Texture = texture;
                Uv = uv;
            }

            public bool Valid => Texture != null;
        }

        /// <summary>Rasterised once, drawn at about 40 px; trilinear and mipmapped so the outlines do not
        /// step when the HUD scales down.</summary>
        public const int Size = 64;

        /// <summary>Sub-pixel samples per texel: the edges are anti-aliased against the design-space
        /// polygons, not the pixel grid.</summary>
        private const int Supersample = 2;

        /// <summary>The plate's half-extent and corner radius, in design units (the figure spans -1..1).</summary>
        private const float PlateHalf = 0.94f;
        private const float PlateCorner = 0.16f;

        /// <summary>Body outline width in design units - about two pixels at the key's size.</summary>
        private const float BodyOutline = 0.085f;

        /// <summary>Fine details draw thinner, or they become solid ink at this size.</summary>
        private const float DetailOutline = 0.035f;

        private readonly Palette _palette;

        /// <summary>Item marks at the radius a key can show, so a shape reads the same here and on a belt.</summary>
        private readonly ShapeIconSet _badges;

        /// <summary>One texture per (kind, behaviour): a content table may give a kind a different behaviour.</summary>
        private readonly Dictionary<int, Icon> _byKey = new Dictionary<int, Icon>();

        /// <summary>This figure's marks, back to front. Reused, so a bar of keys allocates nothing.</summary>
        private readonly List<Mark> _marks = new List<Mark>(12);
        private float _machineScale = 1f;
        private readonly Color32[] _pixels = new Color32[Size * Size];

        /// <summary>One opaque shape: a filled polygon with its own outline. Later marks cover earlier ones.</summary>
        private readonly struct Mark
        {
            public readonly Vector2[] Points;
            public readonly Color32 Fill;
            public readonly Color32 Line;
            public readonly float Width;

            /// <summary>The mark's box, so a sample outside it skips the polygon walk.</summary>
            public readonly float MinX, MinY, MaxX, MaxY;

            public Mark(Vector2[] points, Color32 fill, Color32 line, float width)
            {
                Points = points;
                Fill = fill;
                Line = line;
                Width = width;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 point = points[i];
                    if (point.x < minX) minX = point.x;
                    if (point.x > maxX) maxX = point.x;
                    if (point.y < minY) minY = point.y;
                    if (point.y > maxY) maxY = point.y;
                }

                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public bool Contains(Vector2 p) => p.x >= MinX && p.x <= MaxX && p.y >= MinY && p.y <= MaxY;
        }

        public BuildIcons(Palette palette)
        {
            _palette = palette ?? throw new ArgumentNullException(nameof(palette));
            _badges = palette.ShapeIcons(0.20f, 0f);
        }

        /// <summary>The picture of one building, built on first ask and kept for the run. Invalid means the
        /// caller draws the key's words alone rather than a gap.</summary>
        public Icon Figure(BuildKind kind, BehaviorKind behavior)
        {
            int key = ((int)kind << 4) | ((int)behavior & 0xF);
            if (_byKey.TryGetValue(key, out Icon cached) && cached.Valid) return cached;

            _byKey[key] = Build(kind, behavior);
            return _byKey[key];
        }

        public void Dispose()
        {
            foreach (Icon icon in _byKey.Values)
                Free(icon.Texture);

            _byKey.Clear();
        }

        /// <summary>Free one picture the way the mode allows: outside play mode the Editor refuses
        /// <c>Destroy</c>, so a preview or a test would log an error per icon and leak every one.</summary>
        private static void Free(Texture2D texture)
        {
            if (texture == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return;
            }
#endif

            UnityEngine.Object.Destroy(texture);
        }

        // ---------------------------------------------------------------- the figures

        private Icon Build(BuildKind kind, BehaviorKind behavior)
        {
            _marks.Clear();
            _machineScale = 1f;

            Plate();

            // The belt is the one key whose subject is not a machine: it is the ground a building stands on.
            if (kind == BuildKind.Belt)
            {
                Belt();
                return Bake("Belt");
            }

            switch (behavior)
            {
                case BehaviorKind.Cutter: Cutter(); break;
                case BehaviorKind.Rotator: Rotator(); break;
                case BehaviorKind.Stacker: Stacker(); break;
                case BehaviorKind.Pipe: Pipe(); break;
                case BehaviorKind.Splitter: Splitter(); break;
                case BehaviorKind.Sorter: Sorter(); break;
                case BehaviorKind.Turret: Turret(); break;
                case BehaviorKind.Sink: Sink(); break;
                case BehaviorKind.Drill: Drill(); break;
                default: Block(); break;   // the retired wall, or a behaviour with no picture yet
            }

            return Bake(kind.ToString());
        }

        /// <summary>The light ground every figure stands on, so a dark body reads against the dark bar.</summary>
        private void Plate()
        {
            Color ground = Color.Lerp(_palette.Platform, _palette.Belt, 0.12f);
            Add(RoundedSquare(PlateHalf, PlateCorner), ground, _palette.BeltRail, 0.035f);
        }

        private void Belt()
        {
            // A bed with a rail each side, then two arrows along it.
            Add(Box(-0.88f, -0.34f, 0.88f, 0.34f, 0.06f), _palette.Belt, _palette.Outline, DetailOutline);
            Add(Box(-0.88f, 0.34f, 0.88f, 0.41f), _palette.BeltRail, _palette.Outline, 0.012f);
            Add(Box(-0.88f, -0.41f, 0.88f, -0.34f), _palette.BeltRail, _palette.Outline, 0.012f);

            Add(Arrow(-0.44f, 0f, 0.20f, 0.38f), _palette.BeltArrow, _palette.BeltArrow, 0f);
            Add(Arrow(0.22f, 0f, 0.20f, 0.38f), _palette.BeltArrow, _palette.BeltArrow, 0f);
        }

        /// <summary>The fork: one body, the blade down its middle, and the two halves it makes either side.</summary>
        private void Cutter()
        {
            Add(Box(-0.82f, -0.38f, 0.82f, 0.38f, 0.11f), BodyColor(BehaviorKind.Cutter),
                _palette.Outline, BodyOutline);

            // Light, because a blade in the body's own ink vanishes at this size.
            Add(Box(-0.80f, -0.05f, 0.80f, 0.05f), _palette.MachineIdle, _palette.Outline, 0.012f);

            Badge(Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty), 0.30f, -0.40f, 0.20f);
            Badge(Shape.Of(Quad.Empty, Quad.Empty, Quad.Square, Quad.Square), 0.30f, 0.40f, -0.20f);
        }

        private void Rotator()
        {
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(6, 0.44f, 30f), 0.06f)),
                BodyColor(BehaviorKind.Rotator), _palette.Outline, BodyOutline);

            // Almost a full turn, with an arrowhead: a quarter turn read as a stray fleck at forty pixels.
            const float end = -150f;
            Add(Arc(0.27f, 120f, end, 0.085f), _palette.MachineIdle, _palette.Outline, DetailOutline);

            float radians = end * Mathf.Deg2Rad;
            var tip = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 0.27f;
            Add(Arrow(tip.x, tip.y, 0.13f, 0.28f, end - 90f), _palette.MachineIdle, _palette.Outline,
                DetailOutline);
        }

        private void Stacker()
        {
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(6, 0.44f, 30f), 0.06f)),
                BodyColor(BehaviorKind.Stacker), _palette.Outline, BodyOutline);

            // The union: half-circle over half-square.
            Badge(Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square), 0.27f, -0.02f, -0.02f);
        }

        private void Pipe()
        {
            Color body = BodyColor(BehaviorKind.Pipe);

            // Two rails over the jumped tile, a collar at each end.
            Add(Box(-0.86f, 0.12f, 0.86f, 0.25f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.86f, -0.25f, 0.86f, -0.12f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.92f, -0.34f, -0.78f, 0.34f), body, _palette.Outline, DetailOutline);
            Add(Box(0.78f, -0.34f, 0.92f, 0.34f), body, _palette.Outline, DetailOutline);

            Badge(Shape.Full(Quad.Circle), 0.16f, 0f, 0f);
        }

        private void Splitter()
        {
            Color body = BodyColor(BehaviorKind.Splitter);

            // One hub, four stubs: in on the left, out on the other three sides.
            Add(Box(-0.78f, -0.11f, -0.22f, 0.11f), body, _palette.Outline, DetailOutline);
            Add(Box(0.22f, -0.11f, 0.78f, 0.11f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.11f, 0.22f, 0.11f, 0.78f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.11f, -0.78f, 0.11f, -0.22f), body, _palette.Outline, DetailOutline);
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(4, 0.30f * 1.4143f, 45f), 0.06f)),
                body, _palette.Outline, BodyOutline);

            Add(Arrow(0.48f, 0f, 0.09f, 0.17f), _palette.BeltArrow, _palette.Outline, 0f);
            Add(Arrow(0f, 0.48f, 0.09f, 0.17f, 90f), _palette.BeltArrow, _palette.Outline, 0f);
            Add(Arrow(0f, -0.48f, 0.09f, 0.17f, -90f), _palette.BeltArrow, _palette.Outline, 0f);

            Badge(Shape.Full(Quad.Circle), 0.15f, 0f, 0f);
        }

        private void Sorter()
        {
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(3, 0.52f), 0.045f)),
                BodyColor(BehaviorKind.Sorter), _palette.Outline, BodyOutline);

            // The apex faces east, as the map's sorter does.
            Badge(Shape.Full(Quad.Circle), 0.17f, -0.03f, 0f);
        }

        private void Turret()
        {
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(8, 0.44f), 0.06f)),
                BodyColor(BehaviorKind.Turret), _palette.Outline, BodyOutline);

            // The barrel out to the right, and the round in the chamber.
            Add(Box(0.14f, -0.11f, 0.92f, 0.11f), BodyColor(BehaviorKind.Turret), _palette.Outline,
                DetailOutline);

            Badge(Shape.Full(Quad.Circle), 0.22f, -0.08f, 0f);
        }

        private void Sink()
        {
            _machineScale = 1.22f;
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(4, (0.5f - 0.08f) * 1.4143f, 45f), 0.06f)),
                BodyColor(BehaviorKind.Sink), _palette.Outline, BodyOutline);

            // The pit, with every lane pointing in.
            Add(Box(-0.25f, -0.25f, 0.25f, 0.25f, 0.05f), _palette.Outline, _palette.Platform,
                DetailOutline);

            Add(Arrow(-0.46f, 0f, 0.12f, 0.22f), _palette.MachineIdle, _palette.Outline, 0f);
            Add(Arrow(0.46f, 0f, 0.12f, 0.22f, 180f), _palette.MachineIdle, _palette.Outline, 0f);
            Add(Arrow(0f, 0.46f, 0.12f, 0.22f, -90f), _palette.MachineIdle, _palette.Outline, 0f);
            Add(Arrow(0f, -0.46f, 0.12f, 0.22f, 90f), _palette.MachineIdle, _palette.Outline, 0f);
        }

        /// <summary>The drill is retired from the bar, but a map may still stand one: four bars, the hollow frame.</summary>
        private void Drill()
        {
            Color body = BodyColor(BehaviorKind.Drill);

            Add(Box(-0.82f, -0.82f, 0.82f, -0.62f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.82f, 0.62f, 0.82f, 0.82f), body, _palette.Outline, DetailOutline);
            Add(Box(-0.82f, -0.62f, -0.62f, 0.62f), body, _palette.Outline, DetailOutline);
            Add(Box(0.62f, -0.62f, 0.82f, 0.62f), body, _palette.Outline, DetailOutline);
        }

        /// <summary>A plain inset block: the wall, and the fallback for any behaviour with no figure of its own.</summary>
        private void Block()
        {
            Add(Scaled(ProcMesh.Rounded(ProcMesh.RegularPolygon(4, (0.5f - 0.1f) * 1.4143f, 45f), 0.06f)),
                BodyColor(BehaviorKind.None), _palette.Outline, BodyOutline);
        }

        // ---------------------------------------------------------------- marks

        private Color BodyColor(BehaviorKind behavior) => _palette.BodyColor(behavior);

        /// <summary>Add one opaque polygon, drawn over everything before it.</summary>
        private void Add(Vector2[] points, Color fill, Color line, float width)
        {
            if (points == null || points.Length < 3) return;

            _marks.Add(new Mark(points, fill, line, width));
        }

        /// <summary>The item mark for one shape, at a centre and size, from the same quadrants the map carries.</summary>
        private void Badge(Shape shape, float radius, float x, float y)
        {
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Quad mineral = shape.At(quadrant);
                if (mineral == Quad.Empty) continue;

                IReadOnlyList<Vector2> points = _badges.Points(mineral, quadrant);
                if (points == null || points.Count < 3) continue;

                float k = radius / _badges.Radius;
                var moved = new Vector2[points.Count];
                for (int i = 0; i < points.Count; i++)
                    moved[i] = new Vector2(points[i].x * k + x, points[i].y * k + y);

                Add(moved, _palette.MineralColor(mineral), _palette.Outline, DetailOutline);
            }
        }

        /// <summary>A mark in the machine's own frame, scaled to fit the plate.</summary>
        private Vector2[] Scaled(Vector2[] points)
        {
            if (Mathf.Approximately(_machineScale, 1f)) return points;

            var scaled = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) scaled[i] = points[i] * _machineScale;
            return scaled;
        }

        private static Vector2[] RoundedSquare(float half, float corner)
            => ProcMesh.Rounded(ProcMesh.RegularPolygon(4, half * 1.4143f, 45f), corner);

        /// <summary>An axis-aligned rectangle between two corners, rounded by <paramref name="corner"/>.</summary>
        private static Vector2[] Box(float xMin, float yMin, float xMax, float yMax, float corner = 0f)
        {
            float half = Mathf.Min(xMax - xMin, yMax - yMin) * 0.5f;
            if (corner > half) corner = half;

            var points = new[]
            {
                new Vector2(xMin, yMin),
                new Vector2(xMax, yMin),
                new Vector2(xMax, yMax),
                new Vector2(xMin, yMax),
            };

            if (corner <= 0f) return points;

            // Rounded() takes a counter-clockwise polygon and rounds every corner, which this is.
            return ProcMesh.Rounded(points, corner);
        }

        /// <summary>A filled arrowhead pointing along <paramref name="angleDegrees"/> (0 is east).</summary>
        private static Vector2[] Arrow(float x, float y, float halfWidth, float length,
            float angleDegrees = 0f)
        {
            Vector2 Direction(float degrees)
            {
                float a = degrees * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            Vector2 forward = Direction(angleDegrees);
            Vector2 right = Direction(angleDegrees + 90f);

            var baseCentre = new Vector2(x, y) - forward * (length * 0.5f);
            var tip = new Vector2(x, y) + forward * (length * 0.5f);

            return new[]
            {
                baseCentre + right * halfWidth,
                tip,
                baseCentre - right * halfWidth,
            };
        }

        /// <summary>A thick arc band, from <paramref name="startDeg"/> to <paramref name="endDeg"/>.</summary>
        private static Vector2[] Arc(float radius, float startDeg, float endDeg, float width)
        {
            const int Segments = 14;
            var points = new Vector2[(Segments + 1) * 2];

            float outer = radius + width * 0.5f;
            float inner = radius - width * 0.5f;

            for (int i = 0; i <= Segments; i++)
            {
                float t = i / (float)Segments;
                float a = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                points[i] = direction * outer;
                points[points.Length - 1 - i] = direction * inner;
            }

            return points;
        }

        // ---------------------------------------------------------------- the rasterising

        private Icon Bake(string name)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Color sum = Color.clear;

                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            var p = new Vector2(
                                ((x + (sx + 0.5f) / Supersample) / Size) * 2f - 1f,
                                ((y + (sy + 0.5f) / Supersample) / Size) * 2f - 1f);
                            sum += Shade(p);
                        }
                    }

                    _pixels[y * Size + x] = sum / (Supersample * Supersample);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "FACET/BuildIcon/" + name,
                hideFlags = HideFlags.DontSave,
                filterMode = UnityEngine.FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(_pixels);
            texture.Apply(true);

            return new Icon(texture, new Rect(0f, 0f, 1f, 1f));
        }

        /// <summary>One sample's colour: the last mark containing it wins, and its edge is the outline.</summary>
        private Color Shade(Vector2 p)
        {
            Color colour = Color.clear;

            for (int i = 0; i < _marks.Count; i++)
            {
                Mark mark = _marks[i];
                if (!mark.Contains(p) || !Inside(p, mark.Points)) continue;

                colour = NearEdge(p, mark) ? mark.Line : mark.Fill;
            }

            return colour;
        }

        /// <summary>Even-odd point-in-polygon; every mark here is a simple polygon.</summary>
        private static bool Inside(Vector2 p, Vector2[] points)
        {
            bool inside = false;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Vector2 a = points[i], b = points[j];
                if ((a.y > p.y) == (b.y > p.y)) continue;
                if (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }

        private static bool NearEdge(Vector2 p, Mark mark)
        {
            if (mark.Width <= 0f) return false;

            Vector2[] points = mark.Points;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                if (DistanceToSegment(p, points[j], points[i]) <= mark.Width) return true;

            return false;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            float t = length <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
