namespace Facet.Core
{
    /// <summary>
    /// Everything the player can put on the map, in hotkey order: the two production chains the
    /// design names (drill / decomposer / cannon on circles, cutter / mortar on squares) and the
    /// transport, routing and fortification pieces (belt, pipe, splitter, sorter, wall). The ordinal
    /// is the control that selects it and the field the content table indexes by, so the order is part
    /// of the interface - and a new kind is appended, never inserted, because the ordinal is also what
    /// an authored content asset stores. The tenth kind is key 0 beside keys 1..9.
    ///
    /// Adding a kind is this enum value plus its row in the <see cref="ContentDatabase"/> - there is no
    /// second list of kinds to update, and no switch anywhere that has to learn about it.
    /// </summary>
    public enum BuildKind : byte
    {
        Belt = 0,
        Drill = 1,
        Decomposer = 2,
        Pipe = 3,
        Splitter = 4,
        Cannon = 5,

        /// <summary>Routes by shape: the shape its definition filters for leaves by the side it faces,
        /// everything else by the other wired outlets.</summary>
        Sorter = 6,

        /// <summary>The second decomposer: runs the bisect recipe (a square into two half-squares).
        /// The same behaviour as the decomposer with a different recipe, which is what the content
        /// table is for.</summary>
        Cutter = 7,

        /// <summary>The second gun: eats half-squares, fires slower, hits harder, reaches further.</summary>
        Mortar = 8,

        /// <summary>Cheap, high-HP and behaviourless: the maze tool the siege mechanic needs. It has no
        /// <see cref="BehaviorKind"/>, so it is placed and forgotten - it just stands there and soaks
        /// whatever walks into it.</summary>
        Wall = 9,
    }

    /// <summary>
    /// Convenience reads over the <b>shipped</b> content table, for code that has no reason to care
    /// which table a run was built with (the digit-to-kind mapping in the input layer, for instance).
    /// Code that was handed a <see cref="ContentDatabase"/> should read that instead - these answer
    /// for <see cref="ContentDatabase.Default"/> only.
    /// </summary>
    public static class BuildCatalog
    {
        /// <summary>Every buildable kind, in ordinal (hotkey) order.</summary>
        public static BuildKind[] All => ContentDatabase.Default.BuildKinds;

        public static int Count => ContentDatabase.Default.BuildKinds.Length;

        /// <summary>What a placement of this kind puts on the tile.</summary>
        public static TileKind TileFor(BuildKind kind) => ContentDatabase.Default.Machine(kind).Tile;
    }
}
