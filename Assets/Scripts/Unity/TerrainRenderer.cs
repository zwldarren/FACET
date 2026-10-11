using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the map's own obstacles: rock, water, and the rim where either meets ground that is not
    /// itself. One mesh for all of them, merged along a row wherever the kind repeats, so a rock ridge
    /// costs a handful of quads instead of one per tile.
    ///
    /// This is the view that makes the map's shape legible: a wave walks around these tiles because the
    /// flow field reads the same ground, so what the player sees and what the enemies do come from one
    /// answer. Rock and water are drawn in their own colours and outlined on their own rim, which is what
    /// separates a wall the <em>map</em> drew from the wall a player <em>bought</em> - the machines'
    /// colour, drawn by the machine renderer.
    ///
    /// The void is deliberately not drawn here at all: it is the absence of ground, and the ground view
    /// is what leaves it out, so the background shows through where the map is not.
    ///
    /// Terrain is stamped once and never moves during a run, so this view has no rebuild reason of its
    /// own - only the gate's, which is the zoom moving the screen-space rim width.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class TerrainRenderer : MeshView
    {
        private TileGrid _grid;
        private TerrainField _terrain;

        protected override Palette.Layer Layer => Colors.TerrainLayer;

        protected override void OnInitialized()
        {
            _grid = World.TileGrid;
            _terrain = World.Terrain;
        }

        protected override void OnWorldRebound()
        {
            _grid = World.TileGrid;
            _terrain = World.Terrain;
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            float rim = OutlineWidth;

            AppendFills();
            AppendRims(rim);
        }

        /// <summary>Every run of one obstacle kind along a row, as one quad.</summary>
        private void AppendFills()
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                int x = 0;
                while (x < _grid.Width)
                {
                    TerrainKind kind = ObstacleAt(new Int2(x, y));
                    if (kind == TerrainKind.Ground) { x++; continue; }   // ground and ore have their own views

                    int start = x;
                    while (x < _grid.Width && ObstacleAt(new Int2(x, y)) == kind) x++;
                    AppendRect(start, y, x, y + 1, ColorOf(kind));
                }
            }
        }

        /// <summary>
        /// Outline every side of an obstacle tile that does not face the same kind. A ridge therefore
        /// gets one rim around its whole perimeter and nothing down its middle, which is what makes it
        /// read as one mass rather than as a row of blocks.
        /// </summary>
        private void AppendRims(float rim)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var cell = new Int2(x, y);
                    TerrainKind kind = ObstacleAt(cell);
                    if (kind == TerrainKind.Ground) continue;

                    if (ObstacleAt(cell + Dir.North.Offset()) != kind)
                        AppendLane(new Vector2(x, y + 1f), new Vector2(x + 1f, y + 1f), rim, Colors.Outline);

                    if (ObstacleAt(cell + Dir.South.Offset()) != kind)
                        AppendLane(new Vector2(x, y), new Vector2(x + 1f, y), rim, Colors.Outline);

                    if (ObstacleAt(cell + Dir.East.Offset()) != kind)
                        AppendLane(new Vector2(x + 1f, y), new Vector2(x + 1f, y + 1f), rim, Colors.Outline);

                    if (ObstacleAt(cell + Dir.West.Offset()) != kind)
                        AppendLane(new Vector2(x, y), new Vector2(x, y + 1f), rim, Colors.Outline);
                }
            }
        }

        /// <summary>
        /// The obstacle kind at a tile, or <see cref="TerrainKind.Ground"/> where this view has nothing
        /// of its own to draw - which is plain ground, ore, the void and the cells off the map alike.
        /// Collapsing all four into one "not an obstacle" answer is what keeps the rim rule to a single
        /// comparison: a side is outlined exactly when the tile across it is not the same obstacle.
        /// </summary>
        private TerrainKind ObstacleAt(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return TerrainKind.Ground;

            TerrainKind kind = _terrain.KindAt(cell);
            return kind == TerrainKind.Ore || kind == TerrainKind.Void ? TerrainKind.Ground : kind;
        }

        /// <summary>The colour an obstacle kind of ground draws as: one lookup for every surface that
        /// shows the map's ground. The map itself is stamped from it, and the HUD's inscription card
        /// reads the same table, so a kind of ground carries its look the way its row asks for it -
        /// in one place - and neither surface keeps a colour of its own to drift.</summary>
        internal static Color ColorOf(TerrainKind kind, Palette palette)
            => kind == TerrainKind.Water ? palette.Water : palette.Rock;

        private Color ColorOf(TerrainKind kind)
            => ColorOf(kind, Colors);
    }
}
