using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The visual-override layer, unit-tested without a scene. It exists so a re-skin is an asset edit:
    /// every entry is override-only, so an untouched Palette resolves to exactly the built-in look and a
    /// ticked one resolves to itself. The tests pin both, plus the quadrant marks an item's figure is
    /// assembled from - the shipped quarter disc and quadrant cell, and where an authored silhouette lands.
    /// </summary>
    public class VisualCatalogTests
    {
        /// <summary>The sides the palette ships with, so a quarter disc's segment count is checkable.</summary>
        private const int CircleSides = 16;

        [Test]
        public void UntickedOverride_ResolvesToTheBuiltInLook()
        {
            var over = new VisualStyle();   // Override defaults to false

            VisualStyle resolved = VisualShapes.Resolve(over, VisualSource.Procedural, ProcShape.Circle,
                0.22f, Color.red, Color.black, 2f);

            Assert.IsTrue(resolved.Override, "a resolved default draws as if overridden");
            Assert.AreEqual(VisualSource.Procedural, resolved.Source);
            Assert.AreEqual(ProcShape.Circle, resolved.Shape);
            Assert.AreEqual(0.22f, resolved.Size, 1e-5f);
            Assert.AreEqual(Color.red, resolved.Fill);
            Assert.AreEqual(Color.black, resolved.Outline);
            Assert.AreEqual(2f, resolved.OutlinePixels, 1e-5f);
        }

        [Test]
        public void TickedOverride_ResolvesToItself()
        {
            var over = new VisualStyle { Override = true, Source = VisualSource.Sprite, Size = 1.5f };

            VisualStyle resolved = VisualShapes.Resolve(over, VisualSource.Procedural, ProcShape.Circle,
                0.22f, Color.red, Color.black, 2f);

            Assert.AreSame(over, resolved, "a ticked override is used exactly as authored");
        }

        [Test]
        public void Points_DrawTheExpectedSilhouettes()
        {
            Assert.AreEqual(16,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.Circle, Size = 1f }, CircleSides).Length);
            Assert.AreEqual(7,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.RegularPolygon, Sides = 7, Size = 1f }, CircleSides).Length);
            Assert.AreEqual(9,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.HalfDisc, Size = 1f }, CircleSides).Length);
            Assert.AreEqual(4,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.Rect, Size = 1f }, CircleSides).Length);
        }

        [Test]
        public void AQuadrantMark_FillsItsOwnQuadrantOfTheItem()
        {
            // The two shipped marks, quadrant by quadrant: a circle quadrant is a quarter disc about the
            // item's centre and a square quadrant is that quadrant's own cell. Both are built in item-local
            // space, which is what lets four of them compose into one figure instead of four icons. The item's
            // half-extent is the size the marks were drawn for, so the two agree everywhere here.
            const float radius = 1f;
            var disc = new VisualStyle { Shape = ProcShape.QuarterDisc, Size = radius };
            var cell = new VisualStyle { Shape = ProcShape.Rect, Size = radius };

            Assert.AreEqual(CircleSides / Shape.QuadrantCount + 2,
                VisualShapes.Points(disc, Shape.NW, radius, CircleSides).Length,
                "a quarter disc is the arc's points plus the item's centre");

            Assert.AreEqual(4, VisualShapes.Points(cell, Shape.SE, radius, CircleSides).Length,
                "a quadrant cell is four corners");

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                AssertMarkLies(VisualShapes.Points(disc, quadrant, radius, CircleSides), quadrant,
                    "quarter disc");
                AssertMarkLies(VisualShapes.Points(cell, quadrant, radius, CircleSides), quadrant,
                    "quadrant cell");
            }
        }

        [Test]
        public void AQuarterDisc_ReachesTheItemsRadius_AndNoFurther()
        {
            // The disc's silhouette: four quarter discs of radius r make one disc of radius r, which is what
            // "a full circle reads as a disc" means in numbers.
            float radius = 0.22f;
            var disc = new VisualStyle { Shape = ProcShape.QuarterDisc, Size = radius };

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                foreach (Vector2 p in VisualShapes.Points(disc, quadrant, radius, CircleSides))
                    Assert.LessOrEqual(p.magnitude, radius + 1e-4f, "a quadrant must not leave the item");
            }

            // The arc is what reaches out; the apex sits on the item's centre, where the four quadrants meet
            // (it is what draws the seams a cutter's cut line is chosen against), so the question is the
            // furthest point rather than every point.
            float furthest = 0f;
            foreach (Vector2 p in VisualShapes.Points(disc, Shape.NE, radius, CircleSides))
                furthest = Mathf.Max(furthest, p.magnitude);

            Assert.Greater(furthest, radius * 0.5f, "and it must reach out along its arc");
        }

        [Test]
        public void AnAuthoredMark_IsPlacedByTheItem_NotByItsOwnSize()
        {
            // The distinction an authored Size must not blur: the mark's own size is the author's, while where
            // its quadrant *is* belongs to the item. Taking the placement from the mark's size settles a
            // smaller mark against the item's centre instead of in the middle of its own quadrant, and lets a
            // larger one overrun the seam into its neighbour - neither of which the shipped marks can show,
            // because for them the two numbers are the same one.
            const float radius = 0.5f;   // the item's half-extent: what "one quadrant" is a quarter of

            foreach (float size in new[] { 0.2f, radius, 2f })
            {
                var mark = new VisualStyle { Shape = ProcShape.Circle, Override = true, Size = size };

                foreach (int quadrant in new[] { Shape.NW, Shape.NE, Shape.SE, Shape.SW })
                {
                    Vector2[] points = VisualShapes.Points(mark, quadrant, radius, CircleSides);
                    Vector2 centre = Centre(points);
                    Vector2 quadrantCentre = VisualShapes.QuadrantCentre(radius, quadrant);

                    Assert.AreEqual(quadrantCentre.x, centre.x, 1e-4f,
                        "a mark of size " + size + " is not centred on its quadrant");
                    Assert.AreEqual(quadrantCentre.y, centre.y, 1e-4f,
                        "a mark of size " + size + " is not centred on its quadrant");

                    // And it stays in that quadrant: a mark drawn no bigger than the item fills its own, and a
                    // bigger one is clamped into it rather than laid across the seam.
                    AssertMarkInQuadrant(points, quadrant, "an authored mark of size " + size);
                }
            }
        }

        [Test]
        public void AMineral_HasNoAuthoredLook_UntilOneIsAdded()
        {
            // The property that makes "add a mineral = an enum value plus a colour" true: the block is keyed
            // by mineral rather than naming the minerals, so one nobody has re-skinned simply has no entry.
            var machines = new MachineVisuals();
            var shapes = new ShapeVisuals();

            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
                Assert.IsNull(machines.For(kind), kind + " has no authored look until one is added");

            foreach (Quad mineral in (Quad[])Enum.GetValues(typeof(Quad)))
                Assert.IsNull(shapes.For(mineral), mineral + " has no authored look until one is added");
        }

        [Test]
        public void AnEntry_ResolvesForItsOwnMineralOnly()
        {
            var cutter = new VisualStyle { Override = true, Size = 1.5f };
            var machines = new MachineVisuals
            {
                Entries = new[] { new MachineVisuals.Entry { Build = BuildKind.Cutter, Style = cutter } },
            };

            var square = new VisualStyle { Override = true, Source = VisualSource.Sprite };
            var shapes = new ShapeVisuals
            {
                Entries = new[] { new ShapeVisuals.Entry { Mineral = Quad.Square, Style = square } },
            };

            Assert.AreSame(cutter, machines.For(BuildKind.Cutter));
            Assert.IsNull(machines.For(BuildKind.Rotator),
                "the second operator is the same body as the first and needs no entry of its own");
            Assert.IsNull(machines.For(BuildKind.Drill));

            Assert.AreSame(square, shapes.For(Quad.Square));
            Assert.IsNull(shapes.For(Quad.Circle));
        }

        [Test]
        public void ANullEntry_InTheList_IsSkippedRatherThanFatal()
        {
            // The Inspector leaves a null when a row is deleted, exactly as for the content asset's rows;
            // resolving a look must not trip over it.
            var machines = new MachineVisuals { Entries = new MachineVisuals.Entry[] { null } };
            var shapes = new ShapeVisuals { Entries = new ShapeVisuals.Entry[] { null } };

            Assert.IsNull(machines.For(BuildKind.Cutter));
            Assert.IsNull(shapes.For(Quad.Circle));
        }

        [Test]
        public void FreshPalette_HasEveryOverrideUnticked()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                Assert.IsNotNull(palette.Visuals, "a palette always carries an override block");
                Assert.IsFalse(palette.Visuals.Core.Override);
                Assert.IsFalse(palette.Visuals.Enemies.Runner.Override);
                Assert.IsFalse(palette.Visuals.Enemies.Tank.Override,
                    "the shipped Tank is told apart by body colour - a fixed hexagon would hide the health its polygon count reports");
                Assert.AreEqual(0, palette.Visuals.Items.Entries.Length,
                    "a fresh palette authors nothing, so every mineral draws the built-in mark");
                Assert.AreEqual(0, palette.Visuals.Patches.Entries.Length);
                Assert.AreEqual(0, palette.Visuals.Machines.Entries.Length,
                    "and so does every building");
                Assert.IsNull(palette.Visuals.Items.For(Quad.Circle));
                Assert.IsNull(palette.Visuals.Machines.For(BuildKind.Cutter));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void ShapeIcons_ResolveTheShippedMarkForEachMineral()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                ShapeIconSet icons = palette.ShapeIcons(0.5f, 2f);

                VisualStyle circle = icons.Style(Quad.Circle, Shape.NW);
                Assert.IsNotNull(circle, "a circle quadrant has a mark");
                Assert.AreEqual(VisualSource.Procedural, circle.Source);
                Assert.AreEqual(ProcShape.QuarterDisc, circle.Shape);
                Assert.AreEqual(palette.CircleMineral, circle.Fill, "the quadrant wears the mineral's colour");
                Assert.AreEqual(palette.Outline, circle.Outline);
                Assert.AreEqual(2f, circle.OutlinePixels, 1e-5f);

                VisualStyle square = icons.Style(Quad.Square, Shape.SE);
                Assert.IsNotNull(square);
                Assert.AreEqual(ProcShape.Rect, square.Shape);
                Assert.AreEqual(palette.SquareMineral, square.Fill);

                Assert.IsNotNull(icons.Points(Quad.Circle, Shape.NW));
                Assert.AreSame(icons.Points(Quad.Circle, Shape.NW), icons.Points(Quad.Circle, Shape.NW),
                    "the point list is resolved once and reused, never rebuilt on a view's frame");
                Assert.AreNotSame(icons.Points(Quad.Circle, Shape.NW), icons.Points(Quad.Circle, Shape.NE),
                    "and a quadrant's own mark is its own geometry");

                Assert.IsNull(icons.Style(Quad.Empty, Shape.NW), "a hole has nothing to draw");
                Assert.IsNull(icons.Points(Quad.Empty, Shape.NW));
                Assert.IsNull(icons.Points(Quad.Circle, Shape.QuadrantCount), "and neither has an index off the item");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void ShapeIcons_LetAProceduralOverrideReplaceOneMineralsMark()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                palette.Visuals.Items.Entries = new[]
                {
                    new ShapeVisuals.Entry
                    {
                        Mineral = Quad.Square,
                        Style = new VisualStyle
                        {
                            Override = true,
                            Shape = ProcShape.Circle,
                            Size = 0.5f,
                            Fill = Color.green,
                        },
                    },
                };

                ShapeIconSet icons = palette.ShapeIcons(0.5f, 2f);

                // An authored silhouette replaces the mark inside the quadrant, placed and bounded by the
                // item rather than by its own Size, so it composes with its neighbours rather than spilling
                // over them. Here the two numbers happen to agree - the mark is authored at the item's own
                // radius - and the mismatched case is pinned in its own test above.
                Vector2[] overridden = VisualShapes.Points(icons.Style(Quad.Square, Shape.SE), Shape.SE,
                    icons.Radius, palette.CircleSides);

                Assert.AreEqual(ProcShape.Circle, icons.Style(Quad.Square, Shape.SE).Shape);
                Assert.AreEqual(Color.green, icons.Style(Quad.Square, Shape.SE).Fill);

                foreach (Vector2 p in overridden)
                {
                    Assert.GreaterOrEqual(p.x, -1e-4f, "an authored mark stays inside its own quadrant");
                    Assert.LessOrEqual(p.y, 1e-4f, "an authored mark stays inside its own quadrant");
                }

                Assert.AreEqual(ProcShape.QuarterDisc, icons.Style(Quad.Circle, Shape.SE).Shape,
                    "and the other mineral is untouched by it");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void ShapeIcons_KeepShippedGeometryForASpriteOverride()
        {
            // A sprite has no point list, and the set says so through `Points` - but a sprite-overridden
            // mineral still has to answer with the shipped mark's geometry, because the HUD rasterises its
            // pictures off-screen from point lists and cannot sample a sprite. The contract is load-bearing
            // and two consumers depend on it agreeing with `Style`: a refactor that answered null here would
            // take the status and info cards' item figures down with it, silently and only at runtime.
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                palette.Visuals.Items.Entries = new[]
                {
                    new ShapeVisuals.Entry
                    {
                        Mineral = Quad.Square,
                        Style = new VisualStyle { Override = true, Source = VisualSource.Sprite, Size = 0.5f },
                    },
                };

                ShapeIconSet icons = palette.ShapeIcons(0.5f, 2f);

                Assert.AreEqual(VisualSource.Sprite, icons.Style(Quad.Square, Shape.SE).Source,
                    "the view that can draw a sprite does take the sprite path");

                // The same geometry an unauthored mineral of that kind would draw, quadrant by quadrant: a
                // square quadrant is the quadrant's own cell, and the four of them are four different cells.
                Palette plainPalette = ScriptableObject.CreateInstance<Palette>();
                try
                {
                    ShapeIconSet plain = plainPalette.ShapeIcons(0.5f, 2f);

                    for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                    {
                        IReadOnlyList<Vector2> points = icons.Points(Quad.Square, quadrant);

                        Assert.IsNotNull(points,
                            "a sprite mark still keeps the shipped geometry for off-screen rasterisation");
                        Assert.GreaterOrEqual(points.Count, 4);
                        Assert.AreEqual(plain.Points(Quad.Square, quadrant).Count, points.Count,
                            "and it is that geometry, not a placeholder");
                    }

                    Assert.AreNotSame(icons.Points(Quad.Square, Shape.NW), icons.Points(Quad.Square, Shape.NE),
                        "and each quadrant's cached geometry is its own");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(plainPalette);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void AMineralColour_IsNamedForEveryMineral_AndAHoleThrows()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                Assert.AreEqual(palette.CircleMineral, palette.MineralColor(Quad.Circle));
                Assert.AreEqual(palette.SquareMineral, palette.MineralColor(Quad.Square));

                Assert.Throws<ArgumentOutOfRangeException>(() => palette.MineralColor(Quad.Empty),
                    "a hole has no colour: an item's silhouette is its filled quadrants, so there is no " +
                    "'nothing draws as the outline' case left to fall back to");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        /// <summary>The middle of a mark's own bounding box, which is where a quadrant's mark is centred
        /// whatever its own size.</summary>
        private static Vector2 Centre(Vector2[] points)
        {
            float left = float.MaxValue, right = float.MinValue;
            float bottom = float.MaxValue, top = float.MinValue;

            foreach (Vector2 p in points)
            {
                left = Mathf.Min(left, p.x);
                right = Mathf.Max(right, p.x);
                bottom = Mathf.Min(bottom, p.y);
                top = Mathf.Max(top, p.y);
            }

            return new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f);
        }

        /// <summary>Every point of a quadrant's mark is inside that quadrant of the item's square, and at
        /// least one of them touches an outer edge - the property that makes four quadrants one figure
        /// rather than four separate marks. For the shipped marks, which fill their quadrant exactly: an
        /// authored mark smaller than the item is asked only for the first half of this, through
        /// <see cref="AssertMarkInQuadrant"/>.</summary>
        private static void AssertMarkLies(Vector2[] points, int quadrant, string what)
        {
            foreach (Vector2 p in points)
                if (Extreme(p, quadrant) >= 1f - 1e-3f) return;

            Assert.Fail(what + " must reach the item's edge in its own quadrant");
        }

        /// <summary>Every point of a quadrant's mark is on that quadrant's side of the item's two seams:
        /// the property that keeps a mark out of its neighbours, whatever size it was authored at.</summary>
        private static void AssertMarkInQuadrant(Vector2[] points, int quadrant, string what)
        {
            foreach (Vector2 p in points)
            {
                switch (quadrant)
                {
                    case Shape.NW:
                        Assert.LessOrEqual(p.x, 1e-4f, what + " left the NW quadrant's x");
                        Assert.GreaterOrEqual(p.y, -1e-4f, what + " left the NW quadrant's y");
                        break;
                    case Shape.NE:
                        Assert.GreaterOrEqual(p.x, -1e-4f, what + " left the NE quadrant's x");
                        Assert.GreaterOrEqual(p.y, -1e-4f, what + " left the NE quadrant's y");
                        break;
                    case Shape.SE:
                        Assert.GreaterOrEqual(p.x, -1e-4f, what + " left the SE quadrant's x");
                        Assert.LessOrEqual(p.y, 1e-4f, what + " left the SE quadrant's y");
                        break;
                    default:
                        Assert.LessOrEqual(p.x, 1e-4f, what + " left the SW quadrant's x");
                        Assert.LessOrEqual(p.y, 1e-4f, what + " left the SW quadrant's y");
                        break;
                }
            }
        }

        /// <summary>How far a point reaches out along its own quadrant's two axes.</summary>
        private static float Extreme(Vector2 p, int quadrant)
        {
            switch (quadrant)
            {
                case Shape.NW: return Mathf.Max(-p.x, p.y);
                case Shape.NE: return Mathf.Max(p.x, p.y);
                case Shape.SE: return Mathf.Max(p.x, -p.y);
                default: return Mathf.Max(-p.x, -p.y);
            }
        }
    }
}
