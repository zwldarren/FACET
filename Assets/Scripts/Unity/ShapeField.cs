using System;
using Facet.Core;
using UnityEngine;

// Core's shape-filter mode collides with UnityEngine.FilterMode (the texture filter), and this file is the
// one place the two namespaces meet, so the alias is made here rather than qualification scattered through
// every field and switch below.
using FilterMode = Facet.Core.FilterMode;

namespace Facet.Game
{
    /// <summary>
    /// One item, as an Inspector edits it: four quadrants, each a mineral or a hole. This is the authoring
    /// form of <see cref="Shape"/> - a wave group's demand, a sorter's filter - and it is deliberately the
    /// same four cells the player paints on the sorter's picker and the same four cells the view draws, so
    /// "what the map asked for", "what the player routed for" and "what is on screen" are one figure seen
    /// three times.
    ///
    /// A struct of public fields rather than a <see cref="Shape"/> field: Unity's serializer writes
    /// non-readonly fields only, and Core's encoding is a readonly <c>uint</c> on purpose, so the boundary
    /// converts rather than leaking the simulation's representation into an asset.
    /// </summary>
    [Serializable]
    public struct ShapeField
    {
        public Quad NW, NE, SE, SW;

        public ShapeField(Quad nw, Quad ne, Quad se, Quad sw)
        {
            NW = nw;
            NE = ne;
            SE = se;
            SW = sw;
        }

        /// <summary>
        /// The four quadrants as Core's item. A cell holding something the algebra does not define - a
        /// value left behind by an older build, or hand-edited into the YAML - reads as a hole rather than
        /// being packed: <see cref="Shape.Of"/> masks each quadrant to its two bits, so an unknown value
        /// would otherwise survive as a raw <c>11</c> and enter an eighty-item state space that has no such
        /// quadrant, where Core deliberately never throws and nothing can ever match or build it. This
        /// boundary is the only place the mistake can be caught.
        /// </summary>
        public Shape ToCore() => Shape.Of(Known(NW), Known(NE), Known(SE), Known(SW));

        /// <summary>The cell's mineral, with anything past the algebra's own values read as a hole.</summary>
        private static Quad Known(Quad quadrant)
            => quadrant == Quad.Circle || quadrant == Quad.Square ? quadrant : Quad.Empty;

        public static ShapeField FromCore(Shape shape)
            => new ShapeField(shape.At(Shape.NW), shape.At(Shape.NE), shape.At(Shape.SE), shape.At(Shape.SW));

        public bool IsEmpty => NW == Quad.Empty && NE == Quad.Empty && SE == Quad.Empty && SW == Quad.Empty;

        /// <summary>What the shape is, in words - the same sentence the HUD prints, so an author reading the
        /// Inspector and a player reading the briefing are told the same thing.</summary>
        public override string ToString() => IsEmpty ? "nothing" : ToCore().Describe();
    }

    /// <summary>
    /// A rule over shapes as an Inspector edits it: a mode, an exact item for <see cref="FilterMode.Exact"/>
    /// and a mineral for <see cref="FilterMode.Mineral"/>. The unused half stays default, exactly as
    /// <see cref="ShapeFilter"/> keeps them, and the conversion is the only place the two meet.
    /// </summary>
    [Serializable]
    public struct ShapeFilterField
    {
        [Tooltip("None routes nothing, Any eats/routes every shape, Mineral matches any item containing the " +
            "mineral below, Exact matches the one item below it.")]
        public FilterMode Mode;

        [Tooltip("The exact item, in Exact mode. Left empty in the other modes.")]
        public ShapeField Shape;

        [Tooltip("The mineral an item must contain, in Mineral mode.")]
        public Quad Mineral;

        /// <summary>
        /// The rule as Core's. The unused half of the pair is ignored, and a mode whose payload the asset
        /// does not actually carry - Mineral with no mineral, Exact with an empty item - reads as
        /// <see cref="ShapeFilter.None"/> rather than being minted: an exact rule over nothing admits
        /// nothing, and a contains-mineral rule over nothing would match every item with a hole in it. Both
        /// are the author forgetting a field, and the content table names the row that comes out of it.
        /// </summary>
        public ShapeFilter ToCore()
        {
            switch (Mode)
            {
                case FilterMode.Any: return ShapeFilter.Any;

                case FilterMode.Mineral:
                    return Mineral == Quad.Circle || Mineral == Quad.Square
                        ? ShapeFilter.Has(Mineral)
                        : ShapeFilter.None;

                case FilterMode.Exact:
                    Shape exact = Shape.ToCore();
                    return exact.IsEmpty ? ShapeFilter.None : ShapeFilter.Is(exact);

                default: return ShapeFilter.None;
            }
        }

        public static ShapeFilterField FromCore(ShapeFilter filter)
        {
            switch (filter.Mode)
            {
                case FilterMode.Any:
                    return new ShapeFilterField { Mode = FilterMode.Any };

                case FilterMode.Mineral:
                    return new ShapeFilterField { Mode = FilterMode.Mineral, Mineral = filter.Mineral };

                case FilterMode.Exact:
                    return new ShapeFilterField { Mode = FilterMode.Exact, Shape = ShapeField.FromCore(filter.Exact) };

                default:
                    return new ShapeFilterField { Mode = FilterMode.None };
            }
        }

        public override string ToString() => ToCore().Describe();
    }
}
