using System;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The visual-override layer, unit-tested without a scene. It exists so that a re-skin is an
    /// asset edit: every entry is override-only, so an untouched Palette must resolve to exactly the
    /// built-in look, and a ticked one must resolve to itself. The tests pin both, plus the
    /// silhouettes the procedural path draws.
    /// </summary>
    public class VisualCatalogTests
    {
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
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.Circle, Size = 1f }, 16).Length);
            Assert.AreEqual(7,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.RegularPolygon, Sides = 7, Size = 1f }, 16).Length);
            Assert.AreEqual(9,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.HalfDisc, Size = 1f }, 16).Length);
            Assert.AreEqual(4,
                VisualShapes.Points(new VisualStyle { Shape = ProcShape.Rect, Size = 1f }, 16).Length);
        }

        [Test]
        public void AKindWithNoEntry_HasNoAuthoredLook_AndThatIsEveryKindByDefault()
        {
            // The property that makes "add a building = an enum value plus a content row" true: the block is
            // keyed by kind rather than naming the kinds, so a kind nobody has re-skinned simply has no
            // entry - and no switch has to learn about a building that did not exist when it was written.
            var machines = new MachineVisuals();
            var shapes = new ShapeVisuals();

            foreach (BuildKind kind in (BuildKind[])Enum.GetValues(typeof(BuildKind)))
                Assert.IsNull(machines.For(kind), kind + " has no authored look until one is added");

            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
                Assert.IsNull(shapes.For(shape), shape + " has no authored look until one is added");
        }

        [Test]
        public void AnEntry_ResolvesForItsOwnKindOnly()
        {
            var cannon = new VisualStyle { Override = true, Size = 1.5f };
            var machines = new MachineVisuals
            {
                Entries = new[] { new MachineVisuals.Entry { Build = BuildKind.Cannon, Style = cannon } },
            };

            var square = new VisualStyle { Override = true, Source = VisualSource.Sprite };
            var shapes = new ShapeVisuals
            {
                Entries = new[] { new ShapeVisuals.Entry { Shape = ShapeType.Square, Style = square } },
            };

            Assert.AreSame(cannon, machines.For(BuildKind.Cannon));
            Assert.IsNull(machines.For(BuildKind.Mortar),
                "the second gun is the same body as the cannon and needs no entry of its own");
            Assert.IsNull(machines.For(BuildKind.Drill));

            Assert.AreSame(square, shapes.For(ShapeType.Square));
            Assert.IsNull(shapes.For(ShapeType.Circle));
        }

        [Test]
        public void ANullEntry_InTheList_IsSkippedRatherThanFatal()
        {
            // The Inspector leaves a null when a row is deleted, exactly as it does for the content
            // asset's rows; resolving a look must not trip over it.
            var machines = new MachineVisuals { Entries = new MachineVisuals.Entry[] { null } };
            var shapes = new ShapeVisuals { Entries = new ShapeVisuals.Entry[] { null } };

            Assert.IsNull(machines.For(BuildKind.Cannon));
            Assert.IsNull(shapes.For(ShapeType.Circle));
        }

        [Test]
        public void FreshPalette_HasEveryOverrideUnticked()
        {
            Palette palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                Assert.IsNotNull(palette.Visuals, "a palette always carries an override block");
                Assert.IsFalse(palette.Visuals.Core.Override);
                Assert.IsFalse(palette.Visuals.Enemies.Spike.Override);
                Assert.IsFalse(palette.Visuals.Enemies.Bulwark.Override,
                    "the shipped Bulwark is told apart by body colour - a fixed hexagon would hide the health its polygon count reports");
                Assert.AreEqual(0, palette.Visuals.Items.Entries.Length,
                    "a fresh palette authors nothing, so every shape draws the built-in look");
                Assert.AreEqual(0, palette.Visuals.Patches.Entries.Length);
                Assert.AreEqual(0, palette.Visuals.Machines.Entries.Length,
                    "and so does every building");
                Assert.IsNull(palette.Visuals.Items.For(ShapeType.Circle));
                Assert.IsNull(palette.Visuals.Machines.For(BuildKind.Cannon));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }
    }
}
