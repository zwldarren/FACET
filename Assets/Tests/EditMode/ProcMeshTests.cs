using System.Collections.Generic;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The polygon primitives every silhouette is built from, checked on their own rather than through a
    /// view. Rounding is the one piece of the look whose output has to stay a polygon a view can draw:
    /// convex, wound counter-clockwise, and with its edges where they were.
    /// </summary>
    public class ProcMeshTests
    {
        /// <summary>The unit square, counter-clockwise - the winding every polygon helper demands.</summary>
        private static Vector2[] Square() => new[]
        {
            new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f),
        };

        [Test]
        public void ARoundedSquare_KeepsItsEdges_AndLosesItsCorners()
        {
            const float radius = 0.1f;
            Vector2[] rounded = ProcMesh.Rounded(Square(), radius);

            // Tangency is what makes the rounding invisible on the edges: the arc meets each edge
            // without crossing it, so the figure is exactly as wide as it was.
            Bounds bounds = BoundsOf(rounded);
            Assert.AreEqual(-1f, bounds.min.x, 1e-4f, "the left edge has not moved");
            Assert.AreEqual(1f, bounds.max.x, 1e-4f, "nor the right one");
            Assert.AreEqual(1f, bounds.max.y, 1e-4f, "nor the top");
            Assert.AreEqual(-1f, bounds.min.y, 1e-4f, "nor the bottom");

            // And the corner itself is gone: the arc's nearest sample sits within a radius of it, where
            // the corner point was.
            float nearest = float.MaxValue;
            foreach (Vector2 v in rounded) nearest = Mathf.Min(nearest, (v - new Vector2(1f, 1f)).magnitude);
            Assert.Greater(nearest, radius * 0.4f, "the corner is cut back, not merely nudged");
            Assert.Less(nearest, radius, "and not cut back past the radius that was asked for");

            Assert.IsTrue(IsConvexCounterClockwise(rounded), "the rounded figure is still a drawable polygon");
            Assert.AreEqual(4 * 4, rounded.Length, "four corners of four samples each");
        }

        [Test]
        public void RoundingATriangleWithATooBigRadius_StillLeavesATriangle()
        {
            // The corner of a sharp point needs a longer tangent than a right angle does, so a radius that
            // suits a square would eat a triangle's whole edge and turn it inside out. The tangent is capped
            // at half an edge instead, which is what keeps two corners from reaching each other.
            Vector2[] triangle = ProcMesh.RegularPolygon(3, 1f);
            Vector2[] rounded = ProcMesh.Rounded(triangle, 10f);

            Assert.Greater(rounded.Length, triangle.Length, "the corners are still arcs");
            Assert.IsTrue(IsConvexCounterClockwise(rounded), "but the figure is still a convex triangle");
        }

        [Test]
        public void RoundingWithNoRadius_LeavesThePolygonAlone()
        {
            Vector2[] square = Square();
            Vector2[] rounded = ProcMesh.Rounded(square, 0f);

            Assert.AreEqual(square.Length, rounded.Length, "an author who dialled the rounding out gets the body back");
            for (int i = 0; i < square.Length; i++) Assert.AreEqual(square[i], rounded[i], "point for point");
        }

        [Test]
        public void RoundingSomethingThatIsNotAPolygon_DrawsNothing()
        {
            Assert.AreEqual(0, ProcMesh.Rounded(new Vector2[0], 0.1f).Length);
            Assert.AreEqual(0, ProcMesh.Rounded(new Vector2[] { Vector2.zero, Vector2.right }, 0.1f).Length);
            Assert.AreEqual(0, ProcMesh.Rounded(null, 0.1f).Length);
        }

        [Test]
        public void PolygonOutlineBands_RunPastTheirCorners()
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            ProcMesh.AppendPolygonOutline(verts, colors, tris, Square(), Vector2.zero, Color.white, 0.25f);

            Assert.AreEqual(4 * 4, verts.Count, "one quad per edge");
            for (int band = 0; band < verts.Count; band += 4)
            {
                Vector2 from = verts[band];
                Vector2 to = verts[band + 1];

                Assert.Greater((to - from).magnitude, 2f,
                    "each band runs past both ends of its edge, so the corner is covered twice rather than notched");
            }
        }

        [Test]
        public void AQuadrantsOutline_StopsAtItsOwnCorners()
        {
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            ProcMesh.AppendQuadrantOutline(verts, colors, tris, Square(), Vector2.zero, Color.white,
                0.25f, 0.1f, verticalSeam: false, horizontalSeam: false);

            Assert.AreEqual(4 * 4, verts.Count, "one quad per edge");
            for (int band = 0; band < verts.Count; band += 4)
            {
                Vector2 from = verts[band];
                Vector2 to = verts[band + 1];

                Assert.AreEqual(2f, (to - from).magnitude, 1e-4f,
                    "a quadrant band stops at its vertices: the neighbour owns the other half of the seam");
            }
        }

        /// <summary>Is every turn the same way, and is the figure counter-clockwise overall? The fill is a
        /// triangle fan from the centroid, which only tiles a convex CCW polygon.</summary>
        private static bool IsConvexCounterClockwise(Vector2[] points)
        {
            float area = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Length];
                Vector2 c = points[(i + 2) % points.Length];

                area += a.x * b.y - b.x * a.y;
                if ((b - a).x * (c - b).y - (b - a).y * (c - b).x < -1e-6f) return false;
            }

            return area > 0f;
        }

        private static Bounds BoundsOf(Vector2[] points)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 p in points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }
    }
}
