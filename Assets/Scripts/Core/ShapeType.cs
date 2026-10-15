namespace Facet.Core
{
    /// <summary>
    /// The item shapes. Map 1 mines one mineral (the circle) and splits it into ammo (the half-circle);
    /// map 2 opens a second mineral (the square) splitting into a second ammo (the half-square); the
    /// assembler fuses the two halves into the deep-chain composite. Colours
    /// and polygon sides are the VIEW's business - Core only knows the identity.
    /// </summary>
    public enum ShapeType
    {
        None = 0,

        /// <summary>The raw mineral: mined from patches, and the currency every building costs.
        /// It becomes spendable only when a belt delivers it to the Core.</summary>
        Circle = 1,

        /// <summary>Half a circle, pushed out of the decomposer in pairs. The Cannon's only diet.</summary>
        HalfCircle = 2,

        /// <summary>The second mineral: mined from square patches, and what the second chain splits
        /// into ammunition.</summary>
        Square = 3,

        /// <summary>Half a square, pushed out of the decomposer in pairs. The Mortar's only diet.</summary>
        HalfSquare = 4,

        /// <summary>A half-circle fused with a half-square by the assembler: the deep-chain ammunition.
        /// It matches every weakness - the item's structure literally contains both minerals - which is
        /// what the third production stage buys over a bare half.</summary>
        Composite = 5,
    }

    public static class ShapeTypeExtensions
    {
        /// <summary>True for the real shapes, false for <see cref="ShapeType.None"/>. Written as "not
        /// nothing" so a new mineral is one enum value and one patch, not a value plus more lists.</summary>
        public static bool IsShape(this ShapeType shape) => shape != ShapeType.None;
    }
}
