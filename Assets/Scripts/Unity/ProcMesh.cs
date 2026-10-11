using System.Collections.Generic;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Builds flat, vertex-coloured meshes at runtime. No textures, no gradients: every FACET visual
    /// is a flat fill plus a constant-width dark outline. All polygons must be convex and wound
    /// counter-clockwise.
    ///
    /// The Append* + <see cref="Finish"/> trio exists because a few hundred belts or items have to
    /// land in ONE mesh; building a mesh per object would mean a GameObject per belt.
    /// </summary>
    public static class ProcMesh
    {
        /// <summary>The only built-in shader here that multiplies the mesh's vertex colours. The one
        /// material every view shares is an asset wired to the Palette (<see cref="Palette.ViewMaterial"/>),
        /// so a build carries the shader through that reference; this constant names the shader that asset
        /// must use, and is what the runtime fallback below looks up when no asset is assigned.</summary>
        public const string VertexColorShader = "Sprites/Default";

        private static Material _unlit;
        private static bool _shaderMissing;

        /// <summary>
        /// The runtime-built fallback material for when the Palette has no view material assigned:
        /// unlit, respecting per-vertex colour, or null when no usable shader is installed (in which
        /// case the error has already been logged). The Editor never strips shaders, so the fallback is
        /// always safe here; a build only survives it when the shader is otherwise included, which is
        /// exactly what the Palette's material asset guarantees.
        /// </summary>
        public static Material UnlitMaterial()
        {
            if (_unlit != null) return _unlit;
            if (_shaderMissing) return null;

            // Every FACET polygon gets its colour from the mesh, so a swapped shader is not a
            // cosmetic difference - URP/Unlit ignores vertex colours and would paint the whole game
            // one flat colour. So: never fall back silently to some other shader.
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

        /// <summary>
        /// Vertices of a half-disc, dome up: the arc from 0 to 180 degrees, closed by the diameter.
        /// Convex and wound counter-clockwise, so the polygon helpers take it unchanged. The dome-up
        /// orientation is the fixed one, so an ammo icon and the enemy weak to it read as one shape.
        /// </summary>
        public static Vector2[] HalfDisc(int arcSegments, float radius)
        {
            if (arcSegments < 2) arcSegments = 2;
            var pts = new Vector2[arcSegments + 1];
            for (int i = 0; i <= arcSegments; i++)
            {
                float a = (180f * i / arcSegments) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return pts;
        }

        /// <summary>
        /// Drop one polygon into shared vertex/colour/index buffers, optionally displaced by
        /// <paramref name="offset"/>. This is what lets a renderer pack every belt or every item
        /// into a single mesh instead of one GameObject per object.
        /// The outline is built as one quad per edge rather than by scaling the polygon outward,
        /// so its width stays constant no matter how many sides the shape has.
        /// </summary>
        public static void AppendPolygon(List<Vector3> verts, List<Color> colors, List<int> tris,
            IReadOnlyList<Vector2> points, Vector2 offset, Color fill, Color outline, float outlineWidth)
        {
            int n = points.Count;
            if (n < 3) return;

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++) centroid += points[i];
            centroid = centroid / n + offset;

            // Fill: triangle fan from the centroid (valid because polygons are convex).
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

            if (outlineWidth > 0f && outline.a > 0f)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[(i + 1) % n];
                    Vector2 d = b - a;
                    if (d.sqrMagnitude <= 1e-12f) continue;
                    // Outward normal of a CCW polygon edge.
                    var nrm = new Vector2(d.y, -d.x).normalized * outlineWidth;

                    int i0 = verts.Count;
                    verts.Add(a + offset);
                    verts.Add(b + offset);
                    verts.Add(b + nrm + offset);
                    verts.Add(a + nrm + offset);
                    colors.Add(outline);
                    colors.Add(outline);
                    colors.Add(outline);
                    colors.Add(outline);
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
                }
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

        /// <summary>Turn shared buffers into a mesh. Reuses <paramref name="into"/> when given, so a
        /// renderer that rebuilds every frame does not allocate a new mesh each time.</summary>
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
