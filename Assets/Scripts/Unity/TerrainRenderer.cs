using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws the map's own obstacles: rock, water, and the rim where either meets ground that is not
    /// itself. One mesh for all of them, merged along a row wherever the kind repeats, so a rock ridge costs a
    /// handful of quads instead of one per tile. This is what makes the map's shape legible: a wave walks around
    /// these tiles because the flow field reads the same ground, so what the player sees and what the enemies do
    /// come from one answer. Rock and water are drawn in their own colours and outlined on their own rim, which
    /// separates a wall the <em>map</em> drew from the wall a player <em>bought</em>.
    ///
    /// The void is deliberately drawn nowhere here: it is the absence of ground, left out by the ground view, so
    /// the background shows through. The ground rebuilds when the zoom moves the screen-space rim width - the gate's
    /// reason - and when a lesson opens a room mid-run and stamps its walls, which the terrain's version reports.</summary>
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

        /// <summary>The version the current mesh was built from: a lesson's reveal may stamp rock for a room's
        /// walls, and a restart re-stamps the authored layout.</summary>
        private int _seenVersion = -1;

        protected override void Observe(in ViewFrame frame)
        {
            int version = World.Terrain.Version;
            if (version == _seenVersion) return;

            _seenVersion = version;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            float rim = OutlineWidth;

            AppendFills();
            AppendWaterMarks(rim);
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

        /// <summary>Sparse, offset surface lines make water readable without repeating a tile pattern.
        /// Their positions are deterministic, so a terrain rebuild never shuffles the surface.</summary>
        private void AppendWaterMarks(float rim)
        {
            Color mark = Color.Lerp(ColorOf(TerrainKind.Water), Color.white, 0.28f);
            float thickness = Mathf.Max(0.01f, rim * 0.24f);
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    if (ObstacleAt(new Int2(x, y)) != TerrainKind.Water) continue;
                    int pattern = (x * 37 + y * 101 + x * y * 7) % 23;
                    if (pattern >= 8) continue;

                    float left = x + 0.12f + (pattern % 3) * 0.08f;
                    float height = y + 0.3f + (pattern % 4) * 0.1f;
                    float length = 0.28f + (pattern % 3) * 0.09f;
                    AppendLane(new Vector2(left, height), new Vector2(left + length, height), thickness, mark);
                    if (pattern == 2 || pattern == 7)
                        AppendLane(new Vector2(left + 0.13f, height + 0.17f),
                            new Vector2(left + 0.34f, height + 0.17f), thickness, mark);
                }
            }
        }

        /// <summary>Outline every side of an obstacle tile that does not face the same kind. A ridge therefore gets one
        /// rim around its whole perimeter and nothing down its middle, so it reads as one mass rather than a row of
        /// blocks.</summary>
        private void AppendRims(float rim)
        {
            for (int y = 0; y < _grid.Height; y++)
            {
                for (int x = 0; x < _grid.Width; x++)
                {
                    var cell = new Int2(x, y);
                    TerrainKind kind = ObstacleAt(cell);
                    if (kind == TerrainKind.Ground) continue;
                    DrawSide(kind, ObstacleAt(cell + Dir.North.Offset()),
                        new Vector2(x, y + 1f), new Vector2(x + 1f, y + 1f), rim);
                    DrawSide(kind, ObstacleAt(cell + Dir.South.Offset()),
                        new Vector2(x, y), new Vector2(x + 1f, y), rim);
                    DrawSide(kind, ObstacleAt(cell + Dir.East.Offset()),
                        new Vector2(x + 1f, y), new Vector2(x + 1f, y + 1f), rim);
                    DrawSide(kind, ObstacleAt(cell + Dir.West.Offset()),
                        new Vector2(x, y), new Vector2(x, y + 1f), rim);
                }
            }
        }

        // A water/rock boundary belongs to the bank, so it is drawn once rather than double-stroked.
        private static bool DrawEdge(TerrainKind kind, TerrainKind neighbour)
            => neighbour != kind && !(kind == TerrainKind.Water && neighbour == TerrainKind.Rock);

        private void DrawSide(TerrainKind kind, TerrainKind neighbour,
            Vector2 from, Vector2 to, float rim)
        {
            if (!DrawEdge(kind, neighbour)) return;

            // Rock keeps one continuous silhouette. Water yields the shared edge to rock.
            bool water = kind == TerrainKind.Water;
            Color edge = water
                ? Color.Lerp(ColorOf(kind), Colors.Outline, 0.16f)
                : Colors.Outline;
            AppendLane(from, to, water ? rim * 0.35f : rim, edge);
        }

        /// <summary>The obstacle kind at a tile, or <see cref="TerrainKind.Ground"/> where this view has nothing of its
        /// own to draw - plain ground, ore, the void and off-map cells alike. Collapsing all four into one "not an
        /// obstacle" answer keeps the rim rule to a single comparison: a side is outlined exactly when the tile across
        /// it is not the same obstacle.</summary>
        private TerrainKind ObstacleAt(Int2 cell)
        {
            if (!_grid.InBounds(cell)) return TerrainKind.Ground;

            TerrainKind kind = _terrain.KindAt(cell);
            return kind == TerrainKind.Ore || kind == TerrainKind.Void ? TerrainKind.Ground : kind;
        }

        /// <summary>The colour an obstacle kind of ground draws as: one lookup for every surface that shows the map's
        /// ground. The map is stamped from it, and the HUD's inscription card reads the same table, so a kind of ground
        /// carries its look in one place and neither surface keeps a colour of its own to drift.</summary>
        internal static Color ColorOf(TerrainKind kind, Palette palette)
            => kind == TerrainKind.Water
                ? Color.Lerp(palette.Water, new Color(0.7f, 0.85f, 0.91f, 1f), 0.55f)
                : palette.Rock;

        private Color ColorOf(TerrainKind kind)
            => ColorOf(kind, Colors);
    }
}
