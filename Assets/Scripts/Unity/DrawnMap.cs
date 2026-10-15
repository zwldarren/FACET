using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>What a <see cref="MapAsset"/> draws, as one canonical enumeration: the ground and mineral of every
    /// in-map cell, the in-bounds doors each carrying the wave-table position it is named by, and the Core's
    /// footprint. Built by <see cref="MapAsset.DrawableView"/> and read-only after - every array is exactly the
    /// walked size, so a reader indexes cells without restating bounds or clipping.
    ///
    /// It exists because the same authoring data used to be hand-walked by each surface that draws it - the painting
    /// window's fills and doors, the preview's change-digest, the Scene overlay - and a new drawable field had to be
    /// remembered in every walk. There is now one walk, and <see cref="MapAsset.ContentHash"/> is its checksum: two
    /// answers about the same map agree because they are the same enumeration read twice, not two enumerations hoped
    /// to match.</summary>
    public sealed class DrawnMap
    {
        /// <summary>Map width in tiles; every array below is <see cref="Count"/> long, row-major.</summary>
        public readonly int Width;

        /// <summary>Map height in tiles.</summary>
        public readonly int Height;

        /// <summary>The ground of every cell, row-major (y * Width + x).</summary>
        public readonly TerrainKind[] Ground;

        /// <summary>The mineral of every cell, row-major, <see cref="Quad.Empty"/> where nothing is painted:
        /// the first ore entry for a cell counts, exactly as it counts in the conversion.</summary>
        public readonly Quad[] Mineral;

        /// <summary>The entry points inside the map, in table order.</summary>
        public readonly Vector2Int[] DoorCells;

        /// <summary>The position each door above holds in the asset's raw table - what a wave group names it by, and the
        /// number drawn beside the marker. The two arrays are parallel.</summary>
        public readonly int[] DoorPositions;

        /// <summary>Lower-left tile of the Core's footprint, the centred tile where the map names none.</summary>
        public readonly Vector2Int Core;

        internal DrawnMap(int width, int height, TerrainKind[] ground, Quad[] mineral,
            Vector2Int[] doorCells, int[] doorPositions, Vector2Int core)
        {
            Width = width;
            Height = height;
            Ground = ground;
            Mineral = mineral;
            DoorCells = doorCells;
            DoorPositions = doorPositions;
            Core = core;
        }

        /// <summary>How many cells this map holds. One place to state it, for walks that would otherwise multiply
        /// Width by Height themselves.</summary>
        public int Count => Width * Height;

        /// <summary>The ground at a cell the walk already covered.</summary>
        public TerrainKind GroundAt(int x, int y) => Ground[y * Width + x];

        /// <summary>The mineral at a cell, or <see cref="Quad.Empty"/> - the cell-indexed answer the fills draw,
        /// already first-wins and clipped to the map.</summary>
        public Quad MineralAt(int x, int y) => Mineral[y * Width + x];

        /// <summary>The row-major index of a cell - the arithmetic every walk of this map needs, said once here instead
        /// of re-multiplied per surface.</summary>
        public int IndexAt(int x, int y) => y * Width + x;
    }
}
