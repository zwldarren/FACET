namespace Facet.Core
{
    /// <summary>
    /// What occupies a tile: the thing standing on the ground, as opposed to the ground itself.
    ///
    /// The split lets a map carry rock, water and the void outside it without every rule about buildings
    /// learning about them. What the ground is lives in <see cref="TerrainField"/>; this enum only ever
    /// answers "is something built here". The Core is the one kind the map places rather than the player,
    /// but it is a building all the same - which is why <see cref="TileKindExtensions.IsBuilding"/> counts it.
    ///
    /// Values are authored data: every row of <c>Assets/Data/ContentDatabase.asset</c> names its tile by
    /// ordinal, so a retired kind leaves its number dead rather than handing it to whatever is added
    /// next. The rework appended the three operators past the last authored value for exactly that
    /// reason.
    /// </summary>
    public enum TileKind : byte
    {
        Empty = 0,

        Belt = 1,

        /// <summary>The defended object, and the bank: a belt that delivers into it turns a complete
        /// shape into spendable stockpile. Not buildable by the player, and its HP is the fail state.
        /// Walkable - reaching it is what a wave is for.</summary>
        Core = 2,

        // 3 was ShapePatch. Retired and not to be reused: terrain became its own layer
        // (TerrainField), and leaving the number dead keeps every authored TileKind value in
        // Assets/Data/ContentDatabase.asset meaning what it meant before.

        /// <summary>Mines the ore under it and pushes a full shape onto the belt it faces.</summary>
        Drill = 4,

        // 5 was Decomposer. Retired and not to be reused: splitting is the cutter's job now, and the
        // cutter cuts whatever it is fed rather than running a recipe chosen by the input.

        /// <summary>Carries one item over a single tile: eats anything delivered to it and drops it
        /// on the belt two cells ahead in its facing, so two belts can cross. Transport, not a
        /// machine with a diet - it never jams.</summary>
        Pipe = 6,

        /// <summary>One hub whose ports are read off the belts around it: a belt pointing in is an
        /// input, a belt pointing away is an output, so 1-in-3-out, 3-in-1-out and 2-in-2-out are
        /// the same building on different streets.</summary>
        Splitter = 7,

        /// <summary>Eats any shape and fires it at whatever is in range. See <see cref="TurretDef"/>.</summary>
        Turret = 8,

        /// <summary>Routes by shape: the shape its definition filters for leaves by the side it faces,
        /// everything else by the other wired outlets. Like a splitter it reads its ports off the
        /// belts around it, so it is the same kind of hub on a different job.</summary>
        Sorter = 9,

        // 10 was Cutter, from the first pass at this game. Retired and not to be reused, for the same
        // reason as 3: every authored TileKind value in ContentDatabase.asset must keep its meaning.

        // 11 was Mortar. Retired: one universal chamber replaced both guns.

        /// <summary>Cheap solid block with HP and nothing else: no ports, no behaviour. Enemies walk
        /// around it, and chew through it when they cannot.</summary>
        Wall = 12,

        // 13 was Assembler. Retired: combining is the stacker's job now.

        /// <summary>Splits an item along the line its facing selects, one piece out of the front of each of
        /// the two tiles it stands on. The number is past every retired one, so nothing authored moved.</summary>
        Cutter = 14,

        /// <summary>Turns an item a quarter clockwise, out the side it faces.</summary>
        Rotator = 15,

        /// <summary>Unions two disjoint items into one, out through any wired outlet.</summary>
        Stacker = 16,

        /// <summary>Eats anything delivered into it and destroys it: one tile, no outlet, and no state
        /// that can hold, so a sink is never the reason a line stops. The number is past every retired one,
        /// so nothing authored moved.</summary>
        Sink = 17,
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

        /// <summary>
        /// True for the tiles that will actually <em>take</em> an item off a belt pointing at them: a belt
        /// hands it on, a machine with a behaviour eats it (every shipped machine's diet is "any shape"),
        /// and the Core banks or destroys it.
        ///
        /// A wall is the one solid that takes nothing. It is a building, so a belt docks with it and
        /// <see cref="Eats"/> says yes - which is the right answer to "did I hit something" and the wrong
        /// answer to "is this item ever going anywhere", because a wall has no behaviour, no intake and an
        /// empty diet. That difference is what the belt layer's dead-end marker and the belt view's short
        /// dangling exit lane are drawn from; an item parked against a wall waits forever, and the player
        /// has to be told so rather than left watching a line that looks docked.
        /// </summary>
        public static bool Takes(this TileKind kind) => kind.IsBuilding() && kind != TileKind.Wall;
    }
}
