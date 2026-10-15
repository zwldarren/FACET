using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws the ore in the ground, one mesh for all of it: a bed quad plus the mineral's full
    /// shape at each ore tile. This is the player's map of where each resource lives before any drill is
    /// placed, so the mark is the same quadrant figure the belt items it becomes are drawn as - a disc for a
    /// circle vein, a square for a square one - in the mineral's own colour. Recognition is not learned
    /// twice.
    ///
    /// The mark's appearance comes from the Palette's <see cref="Palette.Visuals"/> mineral override, built
    /// once at initialization; unticked it is the shipped quarter discs or cells at the ore icon radius.
    ///
    /// The ore is terrain, stamped once from the map's data and never moving, and a restart re-stamps the identical
    /// layout - so this view has no rebuild reason of its own. The only thing that changes it is the gate's: the mark
    /// carries a screen-space outline, so a zoom rebuilds it and re-stamps the bed at no extra cost.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class OreRenderer : MeshView
    {
        private readonly List<OreSnapshot> _ore = new List<OreSnapshot>();
        private ShapeIconSet _icons;

        protected override Palette.Layer Layer => Colors.ShapePatchLayer;

        protected override void OnInitialized()
        {
            _icons = Colors.PatchIcons(Colors.Patches.IconRadius, Colors.OutlinePixels);
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Terrain.GetOre(_ore);
            BeginSprites();

            for (int i = 0; i < _ore.Count; i++)
            {
                OreSnapshot patch = _ore[i];
                if (patch.Mineral == Quad.Empty) continue;

                // Tile coordinates ARE world coordinates: (x,y) spans [x,x+1]x[y,y+1].
                float x = patch.Cell.X;
                float y = patch.Cell.Y;
                float bedInset = Colors.Patches.BedInset;
                AppendQuad(
                    new Vector2(x + bedInset, y + bedInset),
                    new Vector2(x + 1f - bedInset, y + bedInset),
                    new Vector2(x + 1f - bedInset, y + 1f - bedInset),
                    new Vector2(x + bedInset, y + 1f - bedInset),
                    Colors.ShapePatchBed);

                // A vein mines out as the mineral's whole shape, so that is what the ground promises: the
                // full figure, all four quadrants of it.
                var centre = new Vector2(x + 0.5f, y + 0.5f);
                AppendIcon(_icons, Shape.Full(patch.Mineral), centre, null);
            }

            EndSprites();
        }
    }
}
