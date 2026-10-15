namespace Facet.Core
{
    /// <summary>
    /// Everything the player can put on the map, in the order the design's final machine list gives
    /// (DESIGN §3): mine, then the three operators that build the shape ladder, then transport, routing,
    /// the maze tool and the gun. The ordinal is both the selecting control and the
    /// <see cref="ContentDatabase"/> index, so the order is the hotkey order and a new kind is appended.
    ///
    /// The rework reshaped this enum rather than appending to it: the decomposer, the mortar and the
    /// assembler are retired and their processing is <em>operators</em> now, so the ladder reads in
    /// teaching order - which is what a player's first ten minutes are spent on.
    /// </summary>
    public enum BuildKind : byte
    {
        Belt = 0,
        Drill = 1,

        /// <summary>Splits one item along the line its facing selects, over the two tiles it stands on.
        /// Both pieces leave by the front, one of them a lane over, and a buffer per lane means one blocked
        /// lane never loses the other piece.</summary>
        Cutter = 2,

        /// <summary>Turns an item a quarter clockwise. The one machine that makes orientation fixable:
        /// the briefing asked for a top half-circle and the line is running bottom halves, so one turn
        /// repairs it. Counter-clockwise is three of these in series, on purpose.</summary>
        Rotator = 3,

        /// <summary>Unions two disjoint items into one: the operator that makes every mixed shape. An
        /// overlapping pair stalls at the intake - a visible queue, not a jam.</summary>
        Stacker = 4,

        Pipe = 5,
        Splitter = 6,

        /// <summary>Routes by shape: what its filter matches leaves by the side it faces, everything else
        /// by the other wired outlets. The filter is either an exact item or "contains this mineral".</summary>
        Sorter = 7,

        /// <summary>Cheap, high-HP and behaviourless: the maze tool the siege mechanic needs. It has no
        /// <see cref="BehaviorKind"/>, so it is placed and forgotten - it just stands and soaks.</summary>
        Wall = 8,

        /// <summary>The one gun, merged from the cannon and the mortar: a universal chamber that eats any
        /// non-empty shape, one round per shot, no diet table and no splash. What a shot is worth is the
        /// shape it was made of against the enemy it hits - see <see cref="TurretBehavior"/>.</summary>
        Turret = 9,

        /// <summary>The drain: eats anything delivered into it and destroys it. It exists because a fork
        /// makes two pieces and a layout often wants one - the surplus has to go <em>somewhere</em>, and
        /// without a sink "route the unwanted half" has no ending, so its lane fills and the machine that
        /// made it stalls. Appended past the ten the keyboard reaches, which is why it is the one kind with
        /// a letter for a hotkey rather than a digit.</summary>
        Sink = 10,
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
