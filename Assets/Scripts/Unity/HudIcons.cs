using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The item figures, as pictures the HUD can draw.
    ///
    /// The content table once spelled a shape with one character, which the built-in IMGUI font does not
    /// carry in a built player, so every place the HUD quoted it printed a gap; the map draws items as
    /// quadrant figures and the HUD now does the same, from the same resolved marks
    /// (<see cref="Palette.ShapeIcons"/>) - so a mark on a card and the item on the belt it describes are
    /// one picture. It is used where an item is a <em>mark</em>; inside a sentence the sentence says
    /// <see cref="Shape.Describe"/>.
    ///
    /// Rasterised per item rather than per shape word: an item is four quadrants, so one texture is built by
    /// drawing each filled quadrant's mark into it, and a code is only ever rasterised once. The code is the
    /// key because it is the whole of an item's identity - there are eighty of them, and the four-character
    /// code is what a test failure prints.
    /// </summary>
    public sealed class HudIcons
    {
        /// <summary>A picture of one item: the texture to draw and the region of it that is the figure.</summary>
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
        /// nothing to build. Public so the tests measure in the same pixels the rasteriser writes.</summary>
        public const int Size = 32;

        /// <summary>The figure is built at this item radius - the half-extent of the item's own square -
        /// then fitted into the icon with a margin so the outline has room to be drawn inside the box.</summary>
        private const float SilhouetteRadius = 0.5f;

        /// <summary>The outline's width as a fraction of the icon. 0.09 of a figure drawn at
        /// <see cref="HudLayout.DemandFigure"/> is about a pixel and a half at 720p, which leaves at least
        /// twelve pixels of shape inside it (issue #12). It was 0.11: on a quarter, the figure the outline
        /// cap does not reach, that drew a 2 px ring around 12 px of shape on the old 16 px chip, and most of
        /// the ink was edge.</summary>
        public const float OutlineFraction = 0.09f;

        private readonly Palette _palette;

        /// <summary>The four quadrant marks, resolved once from the same per-mineral overrides the views
        /// use, so a re-skin reaches the cards too.</summary>
        private readonly ShapeIconSet _marks;

        /// <summary>One texture per item code: 0 is nothing (never built), and every legal code is one of
        /// the eighty the algebra makes. Built on first ask, kept for the run.</summary>
        private readonly Icon[] _byCode = new Icon[256];

        /// <summary>The one unfitted picture the HUD ever needs - the sorter picker's canvas: the item's own
        /// square filling the icon, so its quadrants land in the icon's quadrants and the player is clicking
        /// the very cells the figure is drawn in. One slot, rebuilt only when the painted item changes,
        /// because the picker shows one item at a time.</summary>
        private Shape _canvasCode;
        private Icon _canvas;

        private readonly Color32[] _pixels = new Color32[Size * Size];

        /// <summary>Scratch for one build: each filled quadrant's point list (moved by the mineral's own
        /// offset) and its two colours, so rasterising an item allocates nothing but its own texture. The
        /// buffers are pooled per quadrant - a quadrant's mark is the same length every time it is built -
        /// and the compact lists are what the rasteriser walks.</summary>
        private readonly Vector2[][] _markScratch = new Vector2[Shape.QuadrantCount][];
        private readonly Vector2[][] _markPoints = new Vector2[Shape.QuadrantCount][];
        private readonly int[] _markLength = new int[Shape.QuadrantCount];
        private readonly Color32[] _markFill = new Color32[Shape.QuadrantCount];
        private readonly Color32[] _markLine = new Color32[Shape.QuadrantCount];

        /// <summary>Per mark: does the quadrant across this one's vertical (or horizontal) axis carry a
        /// mark too? Two filled quadrants share the edge between them, and a shared edge is a seam the two
        /// of them draw half of each - the map's mesh rule, asked here of the same figure.</summary>
        private readonly bool[] _markVerticalSeam = new bool[Shape.QuadrantCount];
        private readonly bool[] _markHorizontalSeam = new bool[Shape.QuadrantCount];
        private int _markCount;

        public HudIcons(Palette palette)
        {
            _palette = palette ?? throw new ArgumentNullException(nameof(palette));
            _marks = palette.ShapeIcons(SilhouetteRadius, 0f);
        }

        /// <summary>The picture of one item, built on first ask and kept for the run. An invalid icon is
        /// nothing at all (or a palette that cannot name a mineral), and the caller draws nothing rather
        /// than a gap.</summary>
        public Icon Mark(Shape shape)
        {
            if (shape.IsEmpty) return default;

            int code = (int)shape.Code;

            // The table is one slot per two-bit quadrant, so it holds every item the algebra can make - but
            // only because Shape's constructor masks the code to eight bits. A code outside the table is a
            // wider algebra than this cache expects, and the honest answer is the one this class already
            // promises for anything it cannot draw: nothing, rather than an index off the end of the array.
            if (code <= 0 || code >= _byCode.Length) return default;
            if (_byCode[code].Valid) return _byCode[code];

            _byCode[code] = Build(shape, fit: true);
            return _byCode[code];
        }

        /// <summary>
        /// The same figure drawn <em>unfitted</em>: the item's own square is the icon, so a quarter disc
        /// lands in the icon's own quadrant and the four quadrant buttons of the sorter picker sit exactly
        /// over the cells they paint. The fitted picture above scales the figure to its own bounding box,
        /// which is right for a chip and wrong for a canvas - a single quadrant scaled up would read as a
        /// whole item.
        /// </summary>
        public Icon Canvas(Shape shape)
        {
            if (shape.IsEmpty) return default;
            if (_canvas.Valid && _canvasCode == shape) return _canvas;

            Free(ref _canvas);

            _canvasCode = shape;
            _canvas = Build(shape, fit: false);
            return _canvas;
        }

        /// <summary>Free the textures built here. It is for the Editor's previews and anything else that
        /// builds one and throws it away; a built player never calls it - which is exactly why the free
        /// happens through <see cref="Free"/>: in the Editor, outside play mode, <c>Destroy</c> is refused
        /// (an object is destroyed permanently there), so a preview that freed its pictures this way would
        /// log an error per texture and leak every one of them.</summary>
        public void Dispose()
        {
            for (int i = 0; i < _byCode.Length; i++) Free(ref _byCode[i]);

            Free(ref _canvas);
            _canvasCode = Shape.None;
        }

        /// <summary>Release one icon's texture the way the mode allows, and empty the slot.</summary>
        private static void Free(ref Icon icon)
        {
            if (icon.Texture == null) return;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(icon.Texture);
                icon = default;
                return;
            }
#endif

            UnityEngine.Object.Destroy(icon.Texture);
            icon = default;
        }

        /// <summary>
        /// Rasterise one item: the union of its quadrants' marks, each in its own mineral's colour with the
        /// standard outline.
        ///
        /// A Sprite-overridden mineral is drawn here as the shipped mark in the palette's mineral colour,
        /// because this is the HUD's picture and it is assembled from point lists - a Sprite has no point
        /// list, and a card has no SpriteRenderer to hand. The override is not ignored anywhere else: on the
        /// map <see cref="MeshView.AppendIcon"/> draws that Sprite at the item's own quadrant.
        ///
        /// <paramref name="fit"/> is the choice between a chip and a canvas: true fits the figure's own box
        /// into the icon, false makes the icon the item's square.
        /// </summary>
        private Icon Build(Shape shape, bool fit)
        {
            _markCount = 0;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Quad mineral = shape.At(quadrant);
                if (mineral == Quad.Empty) continue;

                IReadOnlyList<Vector2> points = _marks.Points(mineral, quadrant);
                if (points == null || points.Count < 3) continue;

                VisualStyle style = _marks.Style(mineral, quadrant);
                bool sprite = style != null && style.Source == VisualSource.Sprite;
                Vector2 offset = style != null ? style.Offset : Vector2.zero;

                Vector2[] scratch = _markScratch[quadrant];
                if (scratch == null || scratch.Length < points.Count)
                    scratch = _markScratch[quadrant] = new Vector2[points.Count];

                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 p = points[i] + offset;
                    scratch[i] = p;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                }

                _markPoints[_markCount] = scratch;
                _markLength[_markCount] = points.Count;
                _markFill[_markCount] = sprite ? _palette.MineralColor(mineral) : style.Fill;
                _markLine[_markCount] = sprite ? _palette.Outline : style.Outline;
                _markVerticalSeam[_markCount] = shape.Has(Shape.AcrossVertical(quadrant));
                _markHorizontalSeam[_markCount] = shape.Has(Shape.AcrossHorizontal(quadrant));
                _markCount++;
            }

            if (_markCount == 0) return default;

            // A canvas is the item's own square, whatever the figure fills; a chip fits the figure's own
            // box, keeping its proportions and a margin, so a half disc and a square read as big as they
            // can rather than small inside a box sized for a disc. A half item therefore fills a chip and
            // sits in its half of a canvas - which is the difference between reading a mark and clicking a
            // quadrant.
            if (!fit)
            {
                minX = -SilhouetteRadius;
                minY = -SilhouetteRadius;
                maxX = SilhouetteRadius;
                maxY = SilhouetteRadius;
            }

            float scale = (Size - 2f) / Mathf.Max(0.001f, Mathf.Max(maxX - minX, maxY - minY));
            var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            // The outline's width in the figure's own units: the rasteriser asks "is this pixel within this
            // much of an edge", and the edges are measured in tiles, not in icon pixels. It is capped at a
            // share of the figure's own radius, exactly as the map's mesh caps it, so a mark drawn small
            // here is a figure wearing an outline rather than an outline with a figure inside it.
            float outline = Mathf.Min((Size * OutlineFraction) / scale,
                SilhouetteRadius * _palette.OutlineMaxRadiusFraction);

            // The seams - the lines between the quadrants - at their own, thinner width, half from each
            // side. See NearEdge.
            float seamOutline = outline * _palette.SeamFraction * 0.5f;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f - Size * 0.5f) / scale + centre.x,
                        (y + 0.5f - Size * 0.5f) / scale + centre.y);

                    _pixels[y * Size + x] = Sample(p, outline, seamOutline);
                }
            }

            // Mipmapped and trilinear: the figure is drawn at 13 to 35 px from a 32 px picture, and a plain
            // bilinear sample skips texels below half size - at 50 % the thin outline would flicker in and
            // out instead of averaging into an edge. The pyramid of a 32 px texture is 1.3 KB.
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = (fit ? "FACET/HudIcon/" : "FACET/HudCanvas/") + shape,
                hideFlags = HideFlags.DontSave,
                filterMode = UnityEngine.FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(_pixels);
            texture.Apply(true);

            return new Icon(texture, new Rect(0f, 0f, 1f, 1f));
        }

        /// <summary>What colour one pixel of the figure is: the mark it lands in, filled or outlined.
        /// Quadrant cells never overlap, so the first mark containing the point is the one it belongs to,
        /// and a point in none is the hole the shape does not occupy.</summary>
        private Color32 Sample(Vector2 p, float outline, float seamOutline)
        {
            for (int i = 0; i < _markCount; i++)
            {
                if (!Inside(p, _markPoints[i], _markLength[i])) continue;

                return NearEdge(p, i, outline, seamOutline)
                    ? _markLine[i]
                    : _markFill[i];
            }

            return new Color32(0, 0, 0, 0);
        }

        // ---------------------------------------------------------------- the rasterising
        //
        // Two questions per pixel on the same polygon the map draws: inside (even-odd), and close enough
        // to an edge to be the outline (point-to-segment). Neither allocates.

        private static bool Inside(Vector2 p, Vector2[] points, int count)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = points[i], b = points[j];
                if ((a.y > p.y) == (b.y > p.y)) continue;
                if (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }

        private bool NearEdge(Vector2 p, int mark, float outline, float seamOutline)
        {
            Vector2[] points = _markPoints[mark];
            int count = _markLength[mark];

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Vector2 a = points[j], b = points[i];

                // The same split the map's mesh makes (ProcMesh.AppendQuadrantOutline): an edge on one of
                // the figure's own axes, with the quadrant across it filled, is a seam, and each side of a
                // seam draws half of it. Here the outline is drawn inward instead of outward, so "half" is
                // half the thickness, and the two sides still meet in the middle of the line.
                float width = outline;
                if ((_markVerticalSeam[mark] && Mathf.Abs(a.x) <= SeamEpsilon && Mathf.Abs(b.x) <= SeamEpsilon) ||
                    (_markHorizontalSeam[mark] && Mathf.Abs(a.y) <= SeamEpsilon && Mathf.Abs(b.y) <= SeamEpsilon))
                    width = seamOutline;

                if (DistanceToSegment(p, a, b) <= width) return true;
            }

            return false;
        }

        /// <summary>How close to an axis a point has to be to count as lying on it - the map's own
        /// tolerance for the same question, so a card's cross and a belt item's cross are one rule.</summary>
        private const float SeamEpsilon = 1e-4f;

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float length = ab.sqrMagnitude;
            float t = length <= 0f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
