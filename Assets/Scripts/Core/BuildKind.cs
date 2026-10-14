namespace Facet.Core
{
    /// <summary>
    /// Everything the player can put on the map, in hotkey order. The ordinal is both the selecting
    /// control and the <see cref="ContentDatabase"/> index, so a new kind is appended, never inserted;
    /// adding one is an enum value plus its row, with no second list or switch to update.
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

        /// <summary>The second decomposer: runs the bisect recipe (a square into two half-squares),
        /// the same behaviour with a different recipe.</summary>
        Cutter = 7,

        /// <summary>The second gun: eats half-squares, fires slower, hits harder, reaches further.</summary>
        Mortar = 8,

        /// <summary>Cheap, high-HP and behaviourless: the maze tool the siege mechanic needs. It has no
        /// <see cref="BehaviorKind"/>, so it is placed and forgotten - it just stands and soaks.</summary>
        Wall = 9,
    }

    /// <summary>Convenience reads over the <b>shipped</b> content table, for code that does not care
    /// which table a run was built with. These answer for <see cref="ContentDatabase.Default"/> only -
    /// code handed a <see cref="ContentDatabase"/> should read that instead.</summary>
    public static class BuildCatalog
    {
        /// <summary>Every buildable kind, in ordinal (hotkey) order.</summary>
        public static BuildKind[] All => ContentDatabase.Default.BuildKinds;

        public static int Count => ContentDatabase.Default.BuildKinds.Length;

        /// <summary>What a placement of this kind puts on the tile.</summary>
        public static TileKind TileFor(BuildKind kind) => ContentDatabase.Default.Machine(kind).Tile;
    }
}
