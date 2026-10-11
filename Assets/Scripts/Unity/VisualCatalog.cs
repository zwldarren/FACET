using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>How an override draws its subject.</summary>
    public enum VisualSource : byte
    {
        /// <summary>A flat vertex-coloured polygon built at runtime - the built-in FACET look.</summary>
        Procedural = 0,

        /// <summary>A sprite. The mesh path still draws the functional overlays around it
        /// (a turret's barrel, a machine's port stubs), so a sprite replaces the body only.</summary>
        Sprite = 1,
    }

    /// <summary>The procedural silhouettes an override may pick.</summary>
    public enum ProcShape : byte
    {
        Circle = 0,
        RegularPolygon = 1,
        HalfDisc = 2,
        Rect = 3,

        /// <summary>Half a rect: a square cut down the middle. The second chain's ammunition, so
        /// "half of a square" reads off the silhouette the same way "half of a circle" does.</summary>
        HalfRect = 4,
    }

    /// <summary>
    /// One content's appearance override. The catalog is <em>override-only</em> on purpose: with
    /// <see cref="Override"/> unticked the view keeps drawing exactly what it drew before, so an
    /// untouched Palette reproduces the shipped look pixel for pixel and a missing entry can never
    /// silently blank something out.
    /// </summary>
    [Serializable]
    public sealed class VisualStyle
    {
        [Tooltip("Tick to replace this content's built-in look. Unticked = the code default.")]
        public bool Override;

        [Tooltip("Procedural = a flat polygon (uses the fields below). Sprite = a Sprite asset.")]
        public VisualSource Source = VisualSource.Procedural;

        [Tooltip("Which procedural silhouette to draw. Ignored when Source is Sprite.")]
        public ProcShape Shape = ProcShape.Circle;

        [Tooltip("Sides for RegularPolygon (3+). Ignored by the other shapes.")]
        public int Sides = 16;

        [Tooltip("Half-extent in tiles: the radius of a circle or half-disc, or the half-width of a " +
            "rect. For a Sprite it is the half-width the sprite is scaled to; 0 leaves the sprite at " +
            "its native size.")]
        public float Size = 0.22f;

        [Tooltip("Body colour of the procedural shape.")]
        public Color Fill = Color.white;

        [Tooltip("Outline colour of the procedural shape.")]
        public Color Outline = new Color(0.157f, 0.176f, 0.216f, 1f);

        [Tooltip("Outline thickness in screen pixels; 0 draws no outline. Kept in pixels so it stays " +
            "constant at any zoom, exactly like every other FACET outline.")]
        public float OutlinePixels = 2f;

        [Tooltip("The sprite to draw when Source is Sprite. A centred pivot is assumed.")]
        public Sprite Sprite;

        [Tooltip("Position offset from the cell centre, in tiles.")]
        public Vector2 Offset = Vector2.zero;

        [Tooltip("Rotation in degrees, clockwise.")]
        public float Rotation;
    }

    /// <summary>
    /// Per-shape overrides (the belt items, or the patch icons). Entries are <em>keyed by kind</em>
    /// rather than listed one serialized field per shape, so re-skinning a mineral the player has not
    /// seen yet - a square, a half-square - is one entry in this block, and adding a mineral is a
    /// value in <see cref="ShapeType"/> with no change here at all. A shape with no entry keeps the
    /// built-in look, exactly like an unticked one.
    ///
    /// The kind-keyed list is the point, not an implementation detail: a per-shape field plus a
    /// <c>switch</c> to read it is a second list of the shapes that exists, and a new shape has to be
    /// added to both - which is the bug <see cref="ShapeIconSet"/> was already changed to avoid.
    /// </summary>
    [Serializable]
    public sealed class ShapeVisuals
    {
        /// <summary>One shape's authored look.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Which shape this look belongs to.")]
            public ShapeType Shape = ShapeType.Circle;

            [Tooltip("The look. Unticked = the built-in silhouette and colour for this shape.")]
            public VisualStyle Style = new VisualStyle();
        }

        [Tooltip("One entry per shape you re-skin. A shape with no entry draws the built-in look, so a " +
            "new mineral needs no change here.")]
        public Entry[] Entries = new Entry[0];

        /// <summary>The authored look for a shape, or null when nothing is authored for it - never a
        /// silent default, because "no entry" and "an unticked entry" have to be distinguishable to
        /// the caller that resolves the built-in look.</summary>
        public VisualStyle For(ShapeType shape)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.Shape == shape) return entry.Style;
            }

            return null;
        }
    }

    /// <summary>Per-enemy overrides.</summary>
    [Serializable]
    public sealed class EnemyVisuals
    {
        public VisualStyle Spike = new VisualStyle();

        /// <summary>
        /// An authored alternative body for the Bulwark: a heavier steel-blue hexagon. Left unticked
        /// in the shipped look on purpose - an enemy's polygon count already says how damaged it is,
        /// so a fixed six-sided body would hide the Bulwark's health (and, at full health, draw the
        /// exact hexagon a fresh Spike wears). What tells the two kinds apart out of the box is body
        /// colour - <see cref="Palette.BulwarkBody"/> against <see cref="Palette.SpikeBody"/> - because
        /// which gun can hurt which is the whole rule the second mineral exists to teach.
        /// </summary>
        public VisualStyle Bulwark = new VisualStyle
        {
            Source = VisualSource.Procedural,
            Shape = ProcShape.RegularPolygon,
            Sides = 6,
            Size = 0.30f,
            Fill = new Color(0.35f, 0.55f, 0.85f, 1f),
            Outline = new Color(0.10f, 0.15f, 0.25f, 1f),
            OutlinePixels = 3f,
        };

        /// <summary>
        /// The shipped look for an enemy: every kind is named here and in
        /// <see cref="Palette.EnemyColor"/>, and a kind that is in neither fails loudly at the view's
        /// initialisation rather than drawing the Spike and telling nobody. A per-kind authored override
        /// in the Palette asset still wins over these defaults through the style itself.
        /// </summary>
        public VisualStyle For(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Spike: return Spike;
                case EnemyKind.Bulwark: return Bulwark;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(kind), kind,
                        "a new enemy must get a shipped look here (EnemyVisuals) and a colour in Palette");
            }
        }
    }

    /// <summary>Per-building overrides. Belts are not here: they are strips, not a single body, and
    /// the Palette's own belt look already covers them.</summary>
    [Serializable]
    public sealed class MachineVisuals
    {
        /// <summary>One building's authored look.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Which building this look belongs to. A building with no entry draws the " +
                "built-in silhouette for its behaviour.")]
            public BuildKind Build = BuildKind.Drill;

            [Tooltip("The look. Unticked = the built-in silhouette and colour for this building.")]
            public VisualStyle Style = new VisualStyle();
        }

        [Tooltip("One entry per building you re-skin. A building with no entry draws the built-in " +
            "silhouette for its behaviour - so the second converter is the decomposer's silhouette " +
            "and the second gun the cannon's a shade darker, without either being listed here.")]
        public Entry[] Entries = new Entry[0];

        /// <summary>The authored look for a building, or null when nothing is authored for it. A
        /// building of an existing behaviour therefore needs no entry: the view falls back to the
        /// shipped silhouette its behaviour already chooses.</summary>
        public VisualStyle For(BuildKind kind)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.Build == kind) return entry.Style;
            }

            return null;
        }
    }

    /// <summary>
    /// Every per-content appearance override, in one serializable block on the <see cref="Palette"/>.
    ///
    /// It lives on the Palette rather than in its own asset so that it is edited in the asset the
    /// project already wires into the scene (Assets/Data/Palette.asset) - one look asset, no new
    /// wiring. Each entry is override-only: unticked means "draw the built-in look", so this block
    /// can grow without changing any existing appearance.
    /// </summary>
    [Serializable]
    public sealed class VisualCatalog
    {
        [Tooltip("The defended Core.")]
        public VisualStyle Core = new VisualStyle { Size = 2f };

        [Tooltip("The shapes riding the belts.")]
        public ShapeVisuals Items = new ShapeVisuals();

        [Tooltip("The shape icons in the ground patches.")]
        public ShapeVisuals Patches = new ShapeVisuals();

        [Tooltip("The enemies.")]
        public EnemyVisuals Enemies = new EnemyVisuals();

        [Tooltip("The built machines. The body may be replaced; the functional overlays stay.")]
        public MachineVisuals Machines = new MachineVisuals();
    }

    /// <summary>
    /// Turns a <see cref="VisualStyle"/> into the geometry the views draw, so no view has to know
    /// how a shape is built. The point arrays are meant to be cached by the caller: a view resolves
    /// its handful of styles once, then reuses the arrays every frame.
    /// </summary>
    public static class VisualShapes
    {
        /// <summary>Silhouette points for a procedural style. Null for a sprite style or no shape.</summary>
        public static Vector2[] Points(VisualStyle style, int circleSides)
        {
            if (style == null) return null;

            switch (style.Shape)
            {
                case ProcShape.RegularPolygon:
                    return ProcMesh.RegularPolygon(Mathf.Max(3, style.Sides), style.Size, 0f);
                case ProcShape.HalfDisc:
                    return ProcMesh.HalfDisc(Mathf.Max(2, circleSides / 2), style.Size);
                case ProcShape.Rect:
                    return Rect(style.Size, style.Size);
                case ProcShape.HalfRect:
                    return Rect(style.Size * 0.5f, style.Size);
                default:
                    return ProcMesh.RegularPolygon(Mathf.Max(3, circleSides), style.Size, 0f);
            }
        }

        /// <summary>An axis-aligned rectangle of half-extents, counter-clockwise.</summary>
        private static Vector2[] Rect(float halfWidth, float halfHeight)
        {
            return new[]
            {
                new Vector2(-halfWidth, -halfHeight),
                new Vector2(halfWidth, -halfHeight),
                new Vector2(halfWidth, halfHeight),
                new Vector2(-halfWidth, halfHeight),
            };
        }

        /// <summary>
        /// The style as it will actually be drawn: the override when it is ticked, otherwise a
        /// style built from the given defaults. Views call this once per content and keep the result.
        /// </summary>
        public static VisualStyle Resolve(VisualStyle over, VisualSource source, ProcShape shape,
            float size, Color fill, Color outline, float outlinePixels, int sides = 16)
        {
            if (over != null && over.Override) return over;

            return new VisualStyle
            {
                Override = true,
                Source = source,
                Shape = shape,
                Sides = sides,
                Size = size,
                Fill = fill,
                Outline = outline,
                OutlinePixels = outlinePixels,
            };
        }
    }

    /// <summary>
    /// The shape icons a view draws on itself or in the ground - a decomposer's held circle, a
    /// cutter's held square, a splitter's carried item, a turret's ammo, an enemy's weakness, a patch
    /// in the ground. Resolved once from the same item-shape overrides the belt items use, so
    /// re-skinning a shape stays consistent everywhere it appears, and each use supplies its own
    /// radius so an untouched Palette draws exactly what it always did.
    ///
    /// Every shape the simulation knows gets an entry, built from the Palette's own silhouette and
    /// colour for it, so the set does not have to be told when a mineral is added - the enum is the
    /// list.
    /// </summary>
    public sealed class ShapeIconSet
    {
        private static readonly ShapeType[] Shapes = (ShapeType[])Enum.GetValues(typeof(ShapeType));

        private readonly VisualStyle[] _styles = new VisualStyle[Shapes.Length];
        private readonly Vector2[][] _points = new Vector2[Shapes.Length][];

        public ShapeIconSet(Palette palette, ShapeVisuals over, float radius, float outlinePixels)
        {
            for (int i = 0; i < Shapes.Length; i++)
            {
                ShapeType shape = Shapes[i];
                if (!shape.IsShape()) continue;   // ShapeType.None has nothing to draw

                _styles[i] = VisualShapes.Resolve(over != null ? over.For(shape) : null,
                    VisualSource.Procedural, palette.ProcShapeOf(shape), radius,
                    palette.ShapeColor(shape), palette.Outline, outlinePixels,
                    sides: palette.SidesOf(shape));
                _points[i] = VisualShapes.Points(_styles[i], palette.CircleSides);
            }
        }

        public VisualStyle Style(ShapeType shape) => At(_styles, shape);

        public Vector2[] Points(ShapeType shape) => At(_points, shape);

        private static T At<T>(T[] slots, ShapeType shape) where T : class
        {
            int i = (int)shape;
            return i > 0 && i < slots.Length ? slots[i] : null;
        }
    }
}
