using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The shapes, as pictures the HUD can draw.
    ///
    /// The content table spells each shape with one character, which the built-in IMGUI font does not
    /// carry in a built player, so every place the HUD quoted it prints a gap; the map draws shapes as
    /// polygons and the HUD now does the same. Each shape is rasterised once from the same silhouette
    /// and colour the belt items, ore and weakness icons use (<see cref="Palette.ShapeIcons"/>) - an
    /// authored Sprite is drawn as itself, a procedural override rasterised - and is used where a
    /// shape is a <em>mark</em>; inside a sentence the sentence says its name.
    /// </summary>
    public sealed class HudIcons
    {
        /// <summary>A picture of one shape: the texture to draw and the region of it that is the shape.</summary>
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

        /// <summary>Rasterised once and drawn scaled: 32 px reads round at the largest HUD size and is
        /// nothing to build.</summary>
        private const int Size = 32;

        /// <summary>The silhouette is built at this radius, then fitted into the icon with a margin so the
        /// outline has room to be drawn inside the box.</summary>
        private const float SilhouetteRadius = 0.5f;

        /// <summary>The outline's width as a fraction of the icon, matched to the map's own 2 px outline.</summary>
        private const float OutlineFraction = 0.11f;

        private readonly Palette _palette;
        private readonly Icon[] _byShape;
        private readonly Color32[] _pixels = new Color32[Size * Size];

        public HudIcons(Palette palette)
        {
            _palette = palette ?? throw new ArgumentNullException(nameof(palette));
            _byShape = new Icon[((ShapeType[])Enum.GetValues(typeof(ShapeType))).Length];
        }

        /// <summary>The picture of one shape, built on first ask and kept for the run. An invalid icon is
        /// <see cref="ShapeType.None"/> or a Palette that cannot name one, and the caller draws nothing
        /// rather than a gap.</summary>
        public Icon Shape(ShapeType shape)
        {
            int slot = (int)shape;
            if (slot <= 0 || slot >= _byShape.Length) return default;
            if (_byShape[slot].Valid) return _byShape[slot];

            _byShape[slot] = Build(shape);
            return _byShape[slot];
        }

        /// <summary>Free the textures built here. It is for the Editor's previews and anything else that
        /// builds one and throws it away; a built player never calls it.</summary>
        public void Dispose()
        {
            for (int i = 0; i < _byShape.Length; i++)
                if (_byShape[i].Texture != null) UnityEngine.Object.Destroy(_byShape[i].Texture);
            Array.Clear(_byShape, 0, _byShape.Length);
        }

        private Icon Build(ShapeType shape)
        {
            ShapeIconSet icons = _palette.ShapeIcons(SilhouetteRadius, 0f);
            VisualStyle style = icons.Style(shape);

            if (style != null && style.Source == VisualSource.Sprite && style.Sprite != null)
            {
                Sprite sprite = style.Sprite;
                Texture2D atlas = sprite.texture;
                Rect r = sprite.textureRect;
                return new Icon(atlas, new Rect(
                    r.x / atlas.width, r.y / atlas.height, r.width / atlas.width, r.height / atlas.height));
            }

            Vector2[] points = icons.Points(shape);
            if (points == null || points.Length < 3) return default;

            // Fit the silhouette's own bounding box into the icon, keeping its proportions and a margin, so
            // a half-disc and a square read as big as they can rather than small inside a box sized for a circle.
            Bounds2D box = Bounds2D.Of(points);
            float scale = (Size - 2f) / Mathf.Max(0.001f, Mathf.Max(box.Width, box.Height));
            Vector2 centre = box.Centre;

            Color fill = style != null ? style.Fill : _palette.ShapeColor(shape);
            Color line = style != null ? style.Outline : _palette.Outline;

            // The outline's width in the silhouette's own units: the rasteriser asks "is this pixel within
            // this much of an edge", and the edges are measured in tiles, not in icon pixels.
            float outline = (Size * OutlineFraction) / scale;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f - Size * 0.5f) / scale + centre.x,
                        (y + 0.5f - Size * 0.5f) / scale + centre.y);

                    _pixels[y * Size + x] = Inside(p, points)
                        ? DistanceToEdge(p, points) <= outline ? line : fill
                        : new Color(0f, 0f, 0f, 0f);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "FACET/HudIcon/" + shape,
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(_pixels);
            texture.Apply(false);

            return new Icon(texture, new Rect(0f, 0f, 1f, 1f));
        }

        // ---------------------------------------------------------------- the rasterising
        //
        // Two questions per pixel on the same polygon the map draws: inside (even-odd), and close enough
        // to an edge to be the outline (point-to-segment). Neither allocates.

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

        private static float DistanceToEdge(Vector2 p, Vector2[] points)
        {
            float best = float.MaxValue;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                float d = DistanceToSegment(p, points[j], points[i]);
                if (d < best) best = d;
            }

            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            float t = length <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>The box a point list occupies - the silhouette's own extent, whatever shape it is.</summary>
        private readonly struct Bounds2D
        {
            public readonly float MinX, MinY, MaxX, MaxY;

            private Bounds2D(float minX, float minY, float maxX, float maxY)
            {
                MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
            }

            public Vector2 Centre => new Vector2((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);
            public float Width => MaxX - MinX;
            public float Height => MaxY - MinY;

            public static Bounds2D Of(Vector2[] points)
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p = points[i];
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }

                return new Bounds2D(minX, minY, maxX, maxY);
            }
        }
    }
}
