namespace Facet.Core
{
    /// <summary>
    /// What occupies a tile: the thing standing on the ground, as opposed to the ground itself.
    ///
    /// The split lets a map carry rock, water and the void outside it without every rule about buildings
    /// learning about them. What the ground is lives in <see cref="TerrainField"/>; this enum only ever
    /// answers "is something built here". The Core is the one kind the map places rather than the player,
    /// but it is a building all the same - which is why <see cref="TileKindExtensions.IsBuilding"/> counts it.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,

        Belt = 1,

        /// <summary>The defended object, and the bank: a belt that delivers into it turns a circle
        /// into spendable stockpile. Not buildable by the player, and its HP is the fail state. Walkable -
        /// reaching it is what a wave is for.</summary>
        Core = 2,

        // 3 was ShapePatch. Retired and not to be reused: terrain became its own layer
        // (TerrainField), and leaving the number dead keeps every authored TileKind value in
        // Assets/Data/ContentDatabase.asset meaning what it meant before.

        /// <summary>Mines the ore under it and pushes a shape onto the belt it faces.</summary>
        Drill = 4,

        /// <summary>Splits one circle into two half-circles, pushed out the way it faces. Eats only
        /// circles - a half-circle delivered back to it jams the feeding belt.</summary>
        Decomposer = 5,

        /// <summary>Carries one item over a single tile: eats anything delivered to it and drops it
        /// on the belt two cells ahead in its facing, so two belts can cross. Transport, not a
        /// machine with a diet - it never jams.</summary>
        Pipe = 6,

        /// <summary>One hub whose ports are read off the belts around it: a belt pointing in is an
        /// input, a belt pointing away is an output, so 1-in-3-out, 3-in-1-out and 2-in-2-out are
        /// the same building on different streets.</summary>
        Splitter = 7,

        /// <summary>Fires half-circles and eats only half-circles. See <see cref="TurretDef"/>.</summary>
        Turret = 8,

        /// <summary>Routes by shape: the shape its definition filters for leaves by the side it faces,
        /// everything else by the other wired outlets. Like a splitter it reads its ports off the
        /// belts around it, so it is the same kind of hub on a different job.</summary>
        Sorter = 9,

        /// <summary>The second converter: runs the bisect recipe (square in, half-squares out). The
        /// same <see cref="BehaviorKind.Converter"/> as the decomposer - a different row, not a
        /// different behaviour.</summary>
        Cutter = 10,

        /// <summary>The second turret: fires half-squares, slower and harder than the cannon. Same
        /// <see cref="BehaviorKind.Turret"/>; its diet and numbers come from its
        /// <see cref="TurretDef"/>.</summary>
        Mortar = 11,

        /// <summary>Cheap solid block with HP and nothing else: no ports, no behaviour. Enemies walk
        /// around it, and chew through it when they cannot.</summary>
        Wall = 12,
    }

    public static class TileKindExtensions
    {
        /// <summary>True for a building: something that occupies its tile, the Core included. Bare ground
        /// is the only kind that is not one, so this is simply "is something here".</summary>
        public static bool IsBuilding(this TileKind kind) => kind != TileKind.Empty;

        /// <summary>True for the built machines, which all carry a <see cref="MachineState"/>.
        ///
        /// Written as a range rather than a list of today's kinds: a hand-written OR chain is a second
        /// list, and a new machine left out silently never enters the registry. The open end defaults to
        /// "machine" deliberately. If a future tile kind is ever built but carries no MachineState, split
        /// this range <em>here</em>, on purpose - <see cref="ContentFormatTests"/> nails the ordinals.</summary>
        public static bool IsMachine(this TileKind kind) => kind >= TileKind.Drill;

        /// <summary>
        /// True for the tiles a belt can hand an item to: anything solid. The same question as
        /// <see cref="IsBuilding"/>, written down separately because it is the belt's own rule read aloud
        /// - "the belt docked with something, rather than pointing at bare ground".
        /// </summary>
        public static bool Eats(this TileKind kind) => kind.IsBuilding();
    }
}
