namespace Facet.Core
{
    /// <summary>
    /// The object the player defends. Sim-owned state; the view only reads it.
    /// Enemies path toward <see cref="Cell"/> and drain <see cref="Hp"/>;
    /// reaching zero is the loss condition.
    ///
    /// It occupies a <see cref="Size"/> x <see cref="Size"/> block of tiles, so <see cref="Cell"/> is
    /// the block's lower-left tile, not its middle - the middle of a 4x4 block is not a tile.
    /// </summary>
    public struct CoreState
    {
        /// <summary>Footprint in tiles, on both axes.</summary>
        public const int Size = 4;

        /// <summary>Lower-left tile of the footprint.</summary>
        public Int2 Cell;

        public float Hp;

        public float MaxHp;

        public bool Alive => Hp > 0f;

        /// <summary>0..1, for drawing a damage ring without the view knowing MaxHp.</summary>
        public float HealthFraction => MaxHp <= 0f ? 0f : Hp / MaxHp;

        /// <summary>Centre of the footprint in world units.</summary>
        public Vec2 Center => new Vec2(Cell.X + Size * 0.5f, Cell.Y + Size * 0.5f);
    }
}
