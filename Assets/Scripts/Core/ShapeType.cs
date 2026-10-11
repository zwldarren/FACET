namespace Facet.Core
{
    /// <summary>
    /// The item shapes. Map 1 mines one mineral (the circle) and splits it into ammunition (the
    /// half-circle); map 2 opens a second mineral (the square) that splits into a second ammunition
    /// (the half-square) for a second gun. Colours and polygon sides are the VIEW's business, not the
    /// simulation's - Core only knows the identity, so a new mineral is a value here and a patch in a
    /// map, with no code anywhere that has to learn about it.
    /// </summary>
    public enum ShapeType
    {
        None = 0,

        /// <summary>The raw mineral: mined from patches, and the currency every building costs.
        /// It becomes spendable only when a belt delivers it to the Core.</summary>
        Circle = 1,

        /// <summary>Half a circle, pushed out of the decomposer in pairs. The Cannon's only diet.</summary>
        HalfCircle = 2,

        /// <summary>The second mineral: mined from square patches, and the Cutter's only food.</summary>
        Square = 3,

        /// <summary>Half a square, pushed out of the cutter in pairs. The Mortar's only diet.</summary>
        HalfSquare = 4,
    }

    public static class ShapeTypeExtensions
    {
        /// <summary>
        /// True for the real shapes, false for <see cref="ShapeType.None"/>. Written as "not nothing"
        /// rather than a list of the shapes that exist, deliberately: a new mineral must be one enum
        /// value and one patch, not a value plus three lists that have to remember to include it.
        /// </summary>
        public static bool IsShape(this ShapeType shape) => shape != ShapeType.None;
    }
}
