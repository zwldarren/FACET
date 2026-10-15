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

        /// <summary>The second gun: eats half-squares, fires slower, hits harder, reaches further, and
        /// its shell bursts over an area.</summary>
        Mortar = 7,

        /// <summary>Cheap, high-HP and behaviourless: the maze tool the siege mechanic needs. It has no
        /// <see cref="BehaviorKind"/>, so it is placed and forgotten - it just stands and soaks.</summary>
        Wall = 8,

        /// <summary>Fuses one half-circle and one half-square into a composite round: the deep end of
        /// the ammunition ladder, where the two mineral chains meet. Appended after the wall - new kinds
        /// go last, so the hotkey order of what shipped before never shifts under a player.</summary>
        Assembler = 9,

        // There was a Cutter between Sorter and Mortar until the two splitters merged: one decomposer
        // now runs every recipe, chosen by what is fed to it. Removing it shifted Mortar and Wall down a
        // slot; the content asset keys its rows by id, so only the ordinals pinned in ContentFormatTests
        // had to move.
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
