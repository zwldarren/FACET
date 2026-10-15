using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>How an override draws its subject.</summary>
    public enum VisualSource : byte
    {
        /// <summary>A flat vertex-coloured polygon built at runtime - the built-in FACET look.</summary>
        Procedural = 0,

        /// <summary>A sprite; the mesh path still draws the functional overlays, so a sprite replaces
        /// the body only.</summary>
        Sprite = 1,
    }

    /// <summary>The procedural silhouettes an override may pick: the shapes a <em>body</em> is drawn as,
    /// plus the two quadrant marks an item's figure is assembled from.</summary>
    public enum ProcShape : byte
    {
        Circle = 0,
        RegularPolygon = 1,
        HalfDisc = 2,
        Rect = 3,

        /// <summary>Half a rect, kept for bodies an author drew that way.</summary>
        HalfRect = 4,

        /// <summary>
        /// A quarter disc about the item's centre, filling one quadrant: the circle mineral's shipped
        /// mark. Four of them make a disc, a top pair a half disc - which is why an item's figure is
        /// built from quadrants rather than from a silhouette per item.
        /// </summary>
        QuarterDisc = 5,
    }

    /// <summary>One content's appearance override. The catalog is <em>override-only</em> on purpose:
    /// with <see cref="Override"/> unticked the view draws exactly what it drew before, so an untouched
    /// Palette reproduces the shipped look and a missing entry can never silently blank something out.</summary>
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
    /// Per-mineral overrides (belt items, or patch icons), <em>keyed by mineral</em> rather than one
    /// serialized field per shape: an item is a figure of four quadrants, so a mineral's look is one entry
    /// and the figure of any of the 80 items follows from it. A mineral with no entry keeps the built-in
    /// mark.
    ///
    /// The mineral-keyed list is the point: an item-keyed one would need an entry per item the algebra can
    /// make, and the enum it was keyed on could disagree with the quadrants it claims to describe - the bug
    /// <see cref="ShapeIconSet"/> avoids by deriving the figure instead of listing it.</summary>
    [Serializable]
    public sealed class ShapeVisuals
    {
        /// <summary>One mineral's authored look.</summary>
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Which mineral's quadrants this look belongs to.")]
            public Quad Mineral = Quad.Circle;

            [Tooltip("The look. Unticked = the built-in quadrant mark for this mineral.")]
            public VisualStyle Style = new VisualStyle();
        }

        [Tooltip("One entry per mineral you re-skin. A mineral with no entry draws the built-in quadrant " +
            "figure, so a new mineral needs no change here.")]
        public Entry[] Entries = new Entry[0];

        /// <summary>The authored look for a mineral, or null when nothing is authored - never a silent
        /// default, since "no entry" and "an unticked entry" must stay distinguishable.</summary>
        public VisualStyle For(Quad mineral)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                if (entry != null && entry.Mineral == mineral) return entry.Style;
            }

            return null;
        }
    }

    /// <summary>Per-enemy overrides.</summary>
    [Serializable]
    public sealed class EnemyVisuals
    {
        public VisualStyle Runner = new VisualStyle();

        /// <summary>An authored alternative body for the Tank: a heavier steel-blue hexagon. Left
        /// unticked in the shipped look on purpose - a fixed six-sided body would hide the Tank's
        /// health, since an enemy's polygon count says how damaged it is. Body colour out of the box -
        /// <see cref="Palette.TankBody"/> against <see cref="Palette.RunnerBody"/> - tells the archetypes
        /// apart, since which round can hurt which is the rule the second mineral teaches.</summary>
        public VisualStyle Tank = new VisualStyle
        {
            Source = VisualSource.Procedural,
            Shape = ProcShape.RegularPolygon,
            Sides = 6,
            Size = 0.30f,
            Fill = new Color(0.35f, 0.55f, 0.85f, 1f),
            Outline = new Color(0.10f, 0.15f, 0.25f, 1f),
            OutlinePixels = 3f,
        };

        /// <summary>The shipped look for an enemy: every kind is named here and in
        /// <see cref="Palette.EnemyColor"/>, and one in neither fails loudly at view initialisation. A
        /// per-kind authored override still wins over these defaults through the style itself.</summary>
        public VisualStyle For(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Runner: return Runner;
                case EnemyKind.Tank: return Tank;
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
            "silhouette for its behaviour - so the second drill is the first drill's silhouette and the " +
            "second operator the first one's, without either being listed here.")]
        public Entry[] Entries = new Entry[0];

        /// <summary>The authored look for a building, or null when nothing is authored; it then falls
        /// back to the shipped silhouette its behaviour already chooses.</summary>
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

    /// <summary>Every per-content appearance override, in one serializable block on the
    /// <see cref="Palette"/>.
    ///
    /// It lives on the Palette so it is edited in the asset the project already wires into the scene -
    /// one look asset, no new wiring. Each entry is override-only, so this block can grow without
    /// changing any existing appearance.</summary>
    [Serializable]
    public sealed class VisualCatalog
    {
        [Tooltip("The defended Core.")]
        public VisualStyle Core = new VisualStyle { Size = 2f };

        [Tooltip("The item quadrants riding the belts.")]
        public ShapeVisuals Items = new ShapeVisuals();

        [Tooltip("The ore marks in the ground patches.")]
        public ShapeVisuals Patches = new ShapeVisuals();

        [Tooltip("The enemies.")]
        public EnemyVisuals Enemies = new EnemyVisuals();

        [Tooltip("The built machines. The body may be replaced; the functional overlays stay.")]
        public MachineVisuals Machines = new MachineVisuals();
    }

    /// <summary>Turns a <see cref="VisualStyle"/> into the geometry the views draw, so no view knows how
    /// a shape is built. Point arrays are cached by the caller, which resolves its styles once and reuses
    /// the arrays every frame.</summary>
    public static class VisualShapes
    {
        /// <summary>Silhouette points for a procedural style, in the style's own frame: an enemy body, a
        /// machine body, a sprite-free mark. Null for a sprite style or no shape.</summary>
        public static Vector2[] Points(VisualStyle style, int circleSides)
        {
            if (style == null) return null;

            switch (style.Shape)
            {
                case ProcShape.RegularPolygon:
                    return ProcMesh.RegularPolygon(Mathf.Max(3, style.Sides), style.Size, 0f);
                case ProcShape.QuarterDisc:
                    // A quarter disc has no frame of its own - which quadrant it fills is the whole of
                    // its placement - so the body path answers the first quadrant and the quadrant
                    // overload below is where it is actually built.
                    return ProcMesh.QuarterDisc(Mathf.Max(2, circleSides / Shape.QuadrantCount), style.Size,
                        Shape.NE);
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

        /// <summary>
        /// Points of the figure one mineral draws in one quadrant, in <b>item-local space</b>: the origin
        /// is the item's centre, so a view appends the list at the item's own position and orientation and
        /// the quadrants compose into one figure.
        ///
        /// The two shipped marks are the design's: a circle quadrant is a <em>quarter disc</em> about the
        /// item's centre, so four of them read as a disc and a top pair as a half disc, and a square
        /// quadrant is that quadrant's own <em>cell</em>, so a full square is four cells and a mixed item
        /// is half a disc beside half a square. Neither is a silhouette per item, which is why the old
        /// per-shape table retired: the figure is derived from the quadrants and cannot disagree with them.
        ///
        /// An authored (non-default) silhouette replaces the mark <em>inside</em> the quadrant. Where that
        /// is and how much of it the mark may fill are the <em>item's</em> numbers, not the mark's: the
        /// authored Size says how big the mark is, while <paramref name="itemRadius"/> - the half-extent the
        /// set was built for - says where the quadrant is and how big its share is. So a re-skin composes
        /// with its neighbours instead of sitting in the wrong place or spilling over them. Null once there
        /// is nothing to draw, which is the same answer a sprite style gives - the sprite is a whole mark
        /// and has no point list.
        /// </summary>
        public static Vector2[] Points(VisualStyle style, int quadrant, float itemRadius, int circleSides)
        {
            if (style == null || style.Source != VisualSource.Procedural) return null;

            switch (style.Shape)
            {
                case ProcShape.QuarterDisc:
                    return ProcMesh.QuarterDisc(Mathf.Max(2, circleSides / Shape.QuadrantCount), style.Size,
                        quadrant);

                case ProcShape.Rect:
                    // The cell grows from the item's centre out, so a smaller one sits where it belongs and
                    // a larger one is clamped for the same reason the centred marks are.
                    return QuadrantCell(Mathf.Min(style.Size, itemRadius), quadrant);

                case ProcShape.Circle:
                case ProcShape.RegularPolygon:
                case ProcShape.HalfDisc:
                case ProcShape.HalfRect:
                    return Centred(Points(style, circleSides), QuadrantCentre(itemRadius, quadrant),
                        itemRadius);

                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style.Shape,
                        "a new procedural shape must say how it fills a quadrant");
            }
        }

        /// <summary>The centre of one quadrant of an item whose half-extent is
        /// <paramref name="halfExtent"/>: the item's square runs from -halfExtent to +halfExtent about its
        /// centre, so each quadrant is centred half of that out along both axes.</summary>
        public static Vector2 QuadrantCentre(float halfExtent, int quadrant)
        {
            float offset = halfExtent * 0.5f;

            switch (quadrant)
            {
                case Shape.NW: return new Vector2(-offset, offset);
                case Shape.NE: return new Vector2(offset, offset);
                case Shape.SE: return new Vector2(offset, -offset);
                case Shape.SW: return new Vector2(-offset, -offset);
                default:
                    throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant,
                        "an item has exactly four quadrants");
            }
        }

        /// <summary>The quadrant's own square cell, counter-clockwise: from the item's centre out to that
        /// quadrant's corner. This is the square mineral's shipped mark, and a full square item is four of
        /// them sharing only their edges.</summary>
        private static Vector2[] QuadrantCell(float halfExtent, int quadrant)
        {
            switch (quadrant)
            {
                case Shape.NW: return Rect(-halfExtent, 0f, 0f, halfExtent);
                case Shape.NE: return Rect(0f, halfExtent, 0f, halfExtent);
                case Shape.SE: return Rect(0f, halfExtent, -halfExtent, 0f);
                case Shape.SW: return Rect(-halfExtent, 0f, -halfExtent, 0f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant,
                        "an item has exactly four quadrants");
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

        /// <summary>An axis-aligned rectangle from its edges, counter-clockwise in every quadrant - the
        /// winding the outline normals depend on.</summary>
        private static Vector2[] Rect(float left, float right, float bottom, float top)
        {
            return new[]
            {
                new Vector2(left, bottom),
                new Vector2(right, bottom),
                new Vector2(right, top),
                new Vector2(left, top),
            };
        }

        /// <summary>
        /// The same points, fitted inside one quadrant of an item whose half-extent is
        /// <paramref name="itemRadius"/> and moved to that quadrant's centre - how an authored mark composes
        /// with its neighbours instead of spilling over them. A mark no larger than the item fills its
        /// quadrant exactly, which is what the shipped marks do; a larger one is clamped to it, so the
        /// promise holds whatever an author types into Size.
        /// </summary>
        private static Vector2[] Centred(Vector2[] points, Vector2 centre, float itemRadius)
        {
            if (points == null) return null;

            float extent = 0f;
            for (int i = 0; i < points.Length; i++)
                extent = Mathf.Max(extent, Mathf.Abs(points[i].x), Mathf.Abs(points[i].y));

            // A quadrant is half the item across, so half is the fitted scale; a mark that would overrun it
            // is scaled down to fit rather than drawn across the seam.
            float room = itemRadius * 0.5f;
            float scale = extent > 0f ? Mathf.Min(0.5f, room / extent) : 0f;

            var moved = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++) moved[i] = points[i] * scale + centre;

            return moved;
        }

        /// <summary>The style as actually drawn: the override when ticked, otherwise one built from the
        /// given defaults. Views call this once per content and keep the result.</summary>
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
    /// The quadrant figures a view draws on itself or in the ground - a belt item, a carried item, a
    /// turret's chambered round, an enemy's weakness, an ore patch, a sorter's badge. Resolved once from the same
    /// per-mineral overrides the belt items use, so a re-skin stays consistent everywhere; each use supplies
    /// its own item radius.
    ///
    /// <b>Keyed by mineral, cached by (mineral, quadrant)</b>: an item is four quadrants, so the only thing
    /// that has to be looked up is how one mineral fills one quadrant, and every item the algebra can make -
    /// all 80 - then assembles itself from those. Nothing is resolved or allocated on the per-frame path,
    /// which is what lets a busy belt rebuild its mesh every frame.
    /// </summary>
    public sealed class ShapeIconSet
    {
        /// <summary>Mineral slots, derived from the mineral enum minus <see cref="Quad.Empty"/> - which is 0
        /// and is never stored, so the live minerals' ordinals index the tables from zero. Derived rather than
        /// counted by hand, so a mineral added to the algebra extends the tables instead of silently drawing
        /// nothing; its look still needs a <see cref="Palette.MineralColor"/> case, which fails loud.</summary>
        private static readonly int MineralSlots = Enum.GetValues(typeof(Quad)).Length - 1;

        private readonly VisualStyle[] _styles = new VisualStyle[MineralSlots * Shape.QuadrantCount];
        private readonly Vector2[][] _points = new Vector2[MineralSlots * Shape.QuadrantCount][];

        /// <summary>The item half-extent this set was built for: what "one quadrant" means, and therefore
        /// where an authored mark sits and how big a fraction of it the mark may fill. A view that places a
        /// sprite by hand needs it, since a sprite's own size is the author's rather than the item's.</summary>
        public float Radius { get; }

        public ShapeIconSet(Palette palette, ShapeVisuals over, float radius, float outlinePixels)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));

            Radius = radius;

            for (int m = 0; m < MineralSlots; m++)
            {
                var mineral = (Quad)(m + 1);
                VisualStyle authored = over != null ? over.For(mineral) : null;

                // The shipped mark, mineral by mineral: a quarter disc for the circle, the quadrant's own
                // cell for the square. Only the circle's geometry depends on which quadrant it fills.
                ProcShape shipped = mineral == Quad.Circle ? ProcShape.QuarterDisc : ProcShape.Rect;
                Color fill = palette.MineralColor(mineral);

                for (int q = 0; q < Shape.QuadrantCount; q++)
                {
                    VisualStyle style = VisualShapes.Resolve(authored, VisualSource.Procedural, shipped,
                        radius, fill, palette.Outline, outlinePixels);

                    // A sprite override has no point list, but Points still answers: a mark rasterised
                    // off-screen - the HUD's - cannot sample a sprite, so it draws the shipped geometry
                    // while the views that can draw a sprite read Style and take the sprite path.
                    VisualStyle geometry = style.Source == VisualSource.Procedural
                        ? style
                        : VisualShapes.Resolve(null, VisualSource.Procedural, shipped, radius, fill,
                            palette.Outline, outlinePixels);

                    int slot = SlotOf(mineral, q);
                    _styles[slot] = style;
                    _points[slot] = VisualShapes.Points(geometry, q, radius, palette.CircleSides);
                }
            }
        }

        /// <summary>How one mineral's quadrant is drawn: the authored override where one is ticked,
        /// otherwise the shipped mark. Null for a quadrant holding nothing.</summary>
        public VisualStyle Style(Quad mineral, int quadrant)
        {
            int slot = SlotOf(mineral, quadrant);
            return slot < 0 ? null : _styles[slot];
        }

        /// <summary>The mineral's mark in one quadrant, in item-local space, cached. Null for a quadrant
        /// holding nothing, or a style with no procedural geometry at all.</summary>
        public IReadOnlyList<Vector2> Points(Quad mineral, int quadrant)
        {
            int slot = SlotOf(mineral, quadrant);
            return slot < 0 ? null : _points[slot];
        }

        /// <summary>The table slot for one (mineral, quadrant), or -1 when there is nothing to draw there.
        /// A quadrant off the item and the <see cref="Quad.Empty"/> hole both answer -1 rather than throw:
        /// the caller walks an item's quadrants and asks about each, and "a hole has no mark" is the honest
        /// answer on that path, exactly as <see cref="Shape.At"/> answers a hole for an index off the
        /// item. A mineral the algebra has and these tables do not is a different case and does throw, so a
        /// mineral added to <see cref="Quad"/> cannot end up drawing nothing at all.
        /// </summary>
        private static int SlotOf(Quad mineral, int quadrant)
        {
            if (quadrant < 0 || quadrant >= Shape.QuadrantCount) return -1;
            if (mineral == Quad.Empty) return -1;   // a hole has no mark

            if (mineral != Quad.Circle && mineral != Quad.Square)
                throw new ArgumentOutOfRangeException(nameof(mineral), mineral,
                    "a new mineral must get a slot here and a colour on the Palette");

            // The live minerals index the tables from zero: the slot the constructor filled for this
            // mineral, plus which quadrant of it is asked about.
            return ((int)mineral - 1) * Shape.QuadrantCount + quadrant;
        }
    }
}
