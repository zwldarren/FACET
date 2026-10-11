namespace Facet.Core
{
    /// <summary>
    /// What the ground is, asked independently of what stands on it.
    ///
    /// Terrain is the <em>map's</em>, not the player's: it is stamped once when the world is built and
    /// never moves during a run, which is what keeps a run reproducible from its map. A cell's terrain
    /// and its <see cref="TileKind"/> are two different questions - "what is the ground here" and "what
    /// is standing on it" - and keeping them apart is the whole reason this enum exists. A new kind of
    /// ground is a row here plus the look it draws as, never a case in every rule that touches a tile.
    ///
    /// The default value is <see cref="Ground"/>, so a map that carries no terrain at all is exactly the
    /// plain buildable rectangle every map used to be.
    /// </summary>
    public enum TerrainKind : byte
    {
        /// <summary>Plain buildable ground: belts and every machine belong here.</summary>
        Ground = 0,

        /// <summary>A shape patch in the ground. A drill mines it, transport crosses it, and it is what a
        /// chain is built around. Which mineral it yields is <see cref="TerrainField.OreAt"/>.</summary>
        Ore = 1,

        /// <summary>
        /// Map-authored rock: impassable to walkers and closed to every belt and machine, so it is the
        /// map's own way of deciding which way a wave walks. It is <em>not</em> the player's wall - that
        /// is a building with health that an enemy can chew through - so a route a map draws in rock is a
        /// route, and a route a player draws in walls is a purchase they can lose.
        /// </summary>
        Rock = 2,

        /// <summary>
        /// Map-authored water: the same rules as <see cref="Rock"/> and a different thing to look at.
        /// The two exist so a map can say which one it means - and so a later rule that treats one of
        /// them differently (a bridge, a shallow ford) has somewhere to land without splitting Rock.
        /// </summary>
        Water = 3,

        /// <summary>
        /// Outside the map: impassable, unbuildable, and not drawn at all, so the background shows
        /// through where it is. This is how a map becomes something other than a rectangle while the
        /// grid stays rectangular - every field, the camera's pan bound and the mesh buffers are still
        /// sized by width x height, so only the rules and the ground drawing have to know.
        /// </summary>
        Void = 4,
    }

    public static class TerrainKindExtensions
    {
        /// <summary>
        /// True where a walker may stand. Ground and ore are open - and so is the Core's own footprint,
        /// which carries <see cref="TerrainKind.Ground"/> like any other tile, because reaching the Core
        /// is what a wave is for. What a walker still cannot enter is a machine; that half of the rule
        /// lives in <see cref="TerrainField.Walkable"/>, which asks both.
        /// </summary>
        public static bool Walkable(this TerrainKind kind)
            => kind == TerrainKind.Ground || kind == TerrainKind.Ore;

        /// <summary>
        /// True where transport may be laid. This agrees with <see cref="Walkable"/> today and is still
        /// its own rule: "a belt may cross it" and "a walker may stand on it" are two questions, and ore
        /// is the proof that they are - a vein takes a belt because a drill has to push onto a tile
        /// <em>beside</em> itself, which is impossible if the whole patch is dead ground.
        /// </summary>
        public static bool AllowsBelt(this TerrainKind kind)
            => kind == TerrainKind.Ground || kind == TerrainKind.Ore;

        /// <summary>
        /// True where a machine other than a drill may stand. Ore is the exception and it is handled one
        /// level up, in <see cref="TerrainField.CanPlaceMachine"/>: a patch only ever wants the drill
        /// that mines it.
        /// </summary>
        public static bool AllowsMachine(this TerrainKind kind) => kind == TerrainKind.Ground;

        /// <summary>
        /// True for ground that deliberately stops something: rock, water and the void outside the map.
        /// What the read cursor offers to describe, so a player can tell a wall the map drew from a wall
        /// they bought.
        /// </summary>
        public static bool IsObstacle(this TerrainKind kind)
            => kind == TerrainKind.Rock || kind == TerrainKind.Water || kind == TerrainKind.Void;

        /// <summary>True where the ground is drawn at all. The void is the one kind that is not, which is
        /// what makes an irregular map read as a shape rather than as a rectangle with holes in it.</summary>
        public static bool IsDrawn(this TerrainKind kind) => kind != TerrainKind.Void;
    }
}
