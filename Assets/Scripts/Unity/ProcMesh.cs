using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Builds flat, vertex-coloured meshes at runtime. No textures, no gradients: every FACET visual
    /// is a flat fill plus a constant-width dark outline. All polygons must be convex and wound
    /// counter-clockwise. The Append* + <see cref="Finish"/> trio exists because a few hundred belts or items
    /// have to land in ONE mesh; a mesh per object would mean a GameObject per belt.</summary>
    public static class ProcMesh
    {
        /// <summary>The only built-in shader here that multiplies the mesh's vertex colours. The one material every
        /// view shares is an asset wired to the Palette (<see cref="Palette.ViewMaterial"/>), so a build carries the
        /// shader through that reference; this constant names the shader that asset must use, and is what the runtime
        /// fallback below looks up when no asset is assigned.</summary>
        public const string VertexColorShader = "Sprites/Default";

        private static Material _unlit;
        private static bool _shaderMissing;

        /// <summary>The runtime-built fallback material for when the Palette has no view material assigned: unlit,
        /// respecting per-vertex colour, or null when no usable shader is installed (in which case the error has
        /// already been logged). The Editor never strips shaders, so the fallback is always safe there; a build only
        /// survives it when the shader is otherwise included - which the Palette's material asset guarantees.</summary>
        public static Material UnlitMaterial()
        {
            if (_unlit != null) return _unlit;
            if (_shaderMissing) return null;

            // Every FACET polygon gets its colour from the mesh, so a swapped shader is not cosmetic - URP/Unlit
            // ignores vertex colours and would paint the whole game one flat colour. Never fall back silently.
            Shader shader = Shader.Find(VertexColorShader);
            if (shader == null)
            {
                _shaderMissing = true;
                Debug.LogError("FACET: shader '" + VertexColorShader + "' was not found, so no FACET " +
                    "view can be drawn in colour - every polygon would come out a single flat shade. " +
                    "Assign the Palette's View Material asset; in a build without it, add the shader to " +
                    "Project Settings > Graphics > Always Included Shaders.");
                return null;
            }

            _unlit = new Material(shader) { name = "FACET/UnlitVertexColor", hideFlags = HideFlags.DontSave };
            return _unlit;
        }

        /// <summary>Vertices of a regular polygon, counter-clockwise, first vertex at <paramref name="startAngleDeg"/>.</summary>
        public static Vector2[] RegularPolygon(int sides, float radius, float startAngleDeg = 0f)
        {
            if (sides < 3) sides = 3;
            var pts = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (startAngleDeg + 360f * i / sides) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return pts;
        }

        /// <summary>Vertices of a half-disc, dome up: the arc from 0 to 180 degrees, closed by the diameter.
        /// Convex and wound counter-clockwise, so the polygon helpers take it unchanged. Nothing ships with it
        /// as a body any more - an item's semicircle is two circle quadrants - so it is the authored-override
        /// silhouette for an author who wants a dome as one body, and the figure a cutter's ghost previews at
        /// one of its outlets.
        ///
        /// <paramref name="startAngleDeg"/> turns the dome: 0 is up, and the flat side is always the diameter
        /// through the origin, perpendicular to it. That is what lets the same geometry be "the top half leaves
        /// north" and "the right half leaves east" without a point list per side.</summary>
        public static Vector2[] HalfDisc(int arcSegments, float radius, float startAngleDeg = 0f)
        {
            if (arcSegments < 2) arcSegments = 2;
            var pts = new Vector2[arcSegments + 1];
            for (int i = 0; i <= arcSegments; i++)
            {
                float a = (startAngleDeg + 180f * i / arcSegments) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return pts;
        }

        /// <summary>
        /// Vertices of a quarter disc filling one quadrant of an item, with the item's centre as the
        /// origin: the arc runs across that quadrant's 90 degrees and the two radii close it back through
        /// the centre. Convex and counter-clockwise in every quadrant, so the polygon helpers take it
        /// unchanged - and the two straight radii are what draw the item's own quarter seams, which is the
        /// readout the cutter's cut line is chosen against.
        ///
        /// <paramref name="quadrant"/> is a <see cref="Shape"/> quadrant index; the arc's angles follow the
        /// compass, so NW spans 90..180 degrees and so on clockwise.
        /// </summary>
        public static Vector2[] QuarterDisc(int arcSegments, float radius, int quadrant)
        {
            if (arcSegments < 2) arcSegments = 2;

            float start;
            switch (quadrant)
            {
                case Shape.NW: start = 90f; break;
                case Shape.NE: start = 0f; break;
                case Shape.SE: start = 270f; break;
                case Shape.SW: start = 180f; break;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(quadrant), quadrant,
                        "an item has exactly four quadrants");
            }

            // The centre first, then the arc: going anticlockwise from the item's centre out along one
            // radius, round the rim and back down the other keeps the winding positive.
            var pts = new Vector2[arcSegments + 2];
            pts[0] = Vector2.zero;
            for (int i = 0; i <= arcSegments; i++)
            {
                float a = (start + 90f * i / arcSegments) * Mathf.Deg2Rad;
                pts[i + 1] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }

            return pts;
        }

        /// <summary>
        /// The same polygon with its corners rounded off, so a body reads as a moulded part rather than a
        /// cut-out. Each corner is replaced by an arc tangent to the two edges that meet there, so the
        /// silhouette keeps the polygon's own straight edges and loses only its points - which is the whole
        /// difference between a board of hexagons and a board of coins.
        ///
        /// The arc starts and ends r / tan(θ/2) in from the corner along each edge, where θ is the angle
        /// <em>inside</em> it; that tangent length is capped at half of the shorter edge, so two rounded
        /// corners on a short edge cannot cross and a triangle stays a triangle. The result is convex and
        /// keeps the polygon's winding, so <see cref="AppendPolygon"/> takes it unchanged - but the winding
        /// it keeps is counter-clockwise, which is what every ProcMesh polygon is and what this helper
        /// assumes: the bisector points into the figure and the arc sweeps by the exterior angle in the
        /// positive direction, both of which a clockwise input would turn inside out. A radius of zero (or
        /// a degenerate corner) leaves the polygon as it was, which is what an author who dialled the
        /// rounding out gets.
        /// </summary>
        public static Vector2[] Rounded(IReadOnlyList<Vector2> points, float radius, int segmentsPerCorner = 3)
        {
            int n = points != null ? points.Count : 0;
            var rounded = new List<Vector2>(n > 0 ? n * (Mathf.Max(1, segmentsPerCorner) + 1) : 0);
            if (n < 3) return rounded.ToArray();

            if (segmentsPerCorner < 1) segmentsPerCorner = 1;
            if (radius <= 0f)
            {
                for (int i = 0; i < n; i++) rounded.Add(points[i]);
                return rounded.ToArray();
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 here = points[i];
                Vector2 toPrevious = points[(i + n - 1) % n] - here;
                Vector2 toNext = points[(i + 1) % n] - here;

                float previousLength = toPrevious.magnitude;
                float nextLength = toNext.magnitude;
                if (previousLength <= CornerEpsilon || nextLength <= CornerEpsilon)
                {
                    rounded.Add(here);
                    continue;
                }

                Vector2 before = toPrevious / previousLength;
                Vector2 after = toNext / nextLength;

                // The angle inside the corner. The polygon is convex, so it is between 0 and 180 degrees,
                // and its bisector points into the figure - which is where the arc's centre is.
                float inside = Mathf.Acos(Mathf.Clamp(Vector2.Dot(before, after), -1f, 1f));
                float tangent = Mathf.Tan(inside * 0.5f);
                Vector2 bisector = (before + after).normalized;
                if (tangent <= CornerEpsilon || bisector.sqrMagnitude <= CornerEpsilon)
                {
                    rounded.Add(here);
                    continue;
                }

                // How far in from the corner the arc starts, and the radius that still fits when the edge
                // is too short for the one asked for.
                float cut = radius / tangent;
                float longest = Mathf.Min(previousLength, nextLength) * 0.5f;
                float arcRadius = radius;
                if (cut > longest)
                {
                    cut = longest;
                    arcRadius = cut * tangent;
                }

                Vector2 centre = here + bisector * (arcRadius / Mathf.Sin(inside * 0.5f));
                Vector2 start = here + before * cut;

                // Walking the polygon counter-clockwise, the arc turns by the corner's exterior angle.
                float from = Mathf.Atan2(start.y - centre.y, start.x - centre.x);
                float sweep = Mathf.PI - inside;

                for (int s = 0; s <= segmentsPerCorner; s++)
                {
                    float a = from + sweep * s / segmentsPerCorner;
                    rounded.Add(new Vector2(centre.x + Mathf.Cos(a) * arcRadius,
                        centre.y + Mathf.Sin(a) * arcRadius));
                }
            }

            return rounded.ToArray();
        }

        /// <summary>A point this close to another is the same corner, so a rounding step leaves it alone.</summary>
        private const float CornerEpsilon = 1e-5f;

        /// <summary>Drop one polygon into shared vertex/colour/index buffers, optionally displaced by
        /// <paramref name="offset"/> - what lets a renderer pack every belt or item into a single mesh instead of one
        /// GameObject per object. The outline is one quad per edge rather than a scaled polygon, so its width stays
        /// constant however many sides the shape has.
        ///
        /// Fill then outline, which is the right order for <em>one</em> polygon. Geometry assembled from several
        /// polygons that share an edge - an item's four quadrants - must use the two halves separately instead: see
        /// <see cref="AppendPolygonFill"/> for why.</summary>
        public static void AppendPolygon(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color fill, Color outline, float outlineWidth)
        {
            AppendPolygonFill(verts, colors, tris, points, offset, fill);
            AppendPolygonOutline(verts, colors, tris, points, offset, outline, outlineWidth);
        }

        /// <summary>
        /// The fill half on its own: a triangle fan from the centroid (valid because polygons are convex).
        ///
        /// Separate from the outline because of what a <b>figure of several polygons</b> does on screen. Every
        /// triangle lands in one mesh, so a later triangle paints over an earlier one - and an outline quad of one
        /// quadrant lies in its neighbour's territory, because the outline grows outward. Drawing each quadrant
        /// fill-then-outline therefore lets the <em>next</em> quadrant's fill erase the outline the previous one drew
        /// on their shared axis, so a disc of four quarter discs came out with a seam cross that was neither
        /// symmetrical nor centred on the item. All the fills first, then all the outlines
        /// (<see cref="AppendQuadrantOutline"/>), which is also where the shared edge gets its own width.
        /// </summary>
        public static void AppendPolygonFill(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color fill)
        {
            int n = points.Count;
            if (n < 3) return;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++) centroid += points[i];
            centroid = centroid / n + offset;

            for (int i = 0; i < n; i++)
            {
                int i0 = verts.Count;
                verts.Add(centroid);
                verts.Add(points[i] + offset);
                verts.Add(points[(i + 1) % n] + offset);
                colors.Add(fill);
                colors.Add(fill);
                colors.Add(fill);
                tris.Add(i0);
                tris.Add(i0 + 1);
                tris.Add(i0 + 2);
            }
        }

        /// <summary>The outline half on its own: one quad per edge, grown outward, so its width is a constant
        /// number of pixels however many sides the shape has. Appended after every fill of the figure it belongs
        /// to - see <see cref="AppendPolygonFill"/>.
        ///
        /// Each band runs a little past both ends of its edge, because two bands meeting at a corner leave a
        /// notch between them exactly where the corner is - invisible on a right angle at one pixel, a hairy
        /// white speck once a body's corners are rounded and there are a dozen joins to leave one at.</summary>
        public static void AppendPolygonOutline(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color outline, float outlineWidth)
            => AppendOutline(verts, colors, tris, points, offset, outline, outlineWidth, outlineWidth,
                verticalSeam: false, horizontalSeam: false, grow: outlineWidth);

        /// <summary>
        /// The outline of one <em>quadrant</em> of a figure, with the width split between the figure's rim and
        /// its seams.
        ///
        /// Two quadrants share every seam, and each outline grows outward into the neighbour across it, so a
        /// seam both of them drew at the rim's width came out <b>twice</b> as heavy as the figure's own edge: the
        /// cross through the middle of a disc was four pixels against the rim's two, and at the far end of the
        /// zoom - where a disc is thirteen pixels across - that cross was most of the item. Each quadrant
        /// drawing half of <paramref name="seamWidth"/> instead makes one line of the width asked for, and the
        /// halves still meet on the axis, so the seam stays symmetric about it.
        ///
        /// An edge is a seam when the whole of it lies on one of the figure's own axes (x == 0 or y == 0 in
        /// item-local space, where a quadrant polygon is placed) <em>and</em> the quadrant across that axis is
        /// filled - which is what <paramref name="verticalSeam"/> and <paramref name="horizontalSeam"/> say. The
        /// flat side of a half item is on an axis with a hole behind it, and that edge is the figure's rim like
        /// any other.
        /// </summary>
        public static void AppendQuadrantOutline(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color outline, float rimWidth, float seamWidth,
            bool verticalSeam, bool horizontalSeam)
            => AppendOutline(verts, colors, tris, points, offset, outline, rimWidth, seamWidth,
                verticalSeam, horizontalSeam, grow: 0f);

        /// <summary>How close to an axis a point has to be to count as lying on it. Quadrant polygons put their
        /// radial edges exactly on x == 0 and y == 0; float noise from the arc's angles is far below this.</summary>
        private const float SeamEpsilon = 1e-4f;

        /// <summary>The one outline loop: one outward quad per edge, at the rim's width or the seam's.
        /// <paramref name="grow"/> is how far each band runs past the ends of its own edge; the item figures
        /// pass zero, because their bands share a figure and a band reaching past a seam would cross the
        /// neighbour's half of it.</summary>
        private static void AppendOutline(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color outline, float rimWidth, float seamWidth,
            bool verticalSeam, bool horizontalSeam, float grow)
        {
            int n = points.Count;
            if (n < 3) return;
            if (outline.a <= 0f) return;

            // Half of the seam from this side: the neighbour across the axis draws the other half.
            float halfSeam = seamWidth * 0.5f;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % n];

                float width = rimWidth;
                if ((verticalSeam && Mathf.Abs(a.x) <= SeamEpsilon && Mathf.Abs(b.x) <= SeamEpsilon) ||
                    (horizontalSeam && Mathf.Abs(a.y) <= SeamEpsilon && Mathf.Abs(b.y) <= SeamEpsilon))
                    width = halfSeam;

                if (width <= 0f) continue;

                Vector2 d = b - a;
                if (d.sqrMagnitude <= 1e-12f) continue;
                // Outward normal of a CCW polygon edge.
                var nrm = new Vector2(d.y, -d.x).normalized * width;

                // A band that stops at its own corners leaves a notch at every join: half the edge at
                // most, so a short edge's band cannot reach across its neighbour's.
                Vector2 along = d.normalized * Mathf.Min(grow, d.magnitude * 0.5f);
                Vector2 from = a - along;
                Vector2 to = b + along;

                int i0 = verts.Count;
                verts.Add(from + offset);
                verts.Add(to + offset);
                verts.Add(to + nrm + offset);
                verts.Add(from + nrm + offset);
                colors.Add(outline);
                colors.Add(outline);
                colors.Add(outline);
                colors.Add(outline);
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
            }
        }

        /// <summary>Quad pushed into shared buffers. Four corners, CCW order.</summary>
        public static void AppendQuad(List<Vector3> verts, List<Color> colors, List<int> tris,
            Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i0 = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            verts.Add(d);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }

        /// <summary>Turn shared buffers into a mesh. Reuses <paramref name="into"/> when given, so a renderer that
        /// rebuilds every frame does not allocate a new mesh each time.</summary>
        public static Mesh Finish(List<Vector3> verts, List<Color> colors, List<int> tris, string name, Mesh into = null)
        {
            Mesh mesh = into != null ? into : new Mesh();
            mesh.name = name;
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
