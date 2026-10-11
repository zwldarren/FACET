using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the ore in the ground, one mesh for all of it: a bed quad plus the mineral's own
    /// silhouette at each ore tile. This is the player's map of where each resource lives
    /// before any drill is placed, so the icon uses the same shape and colour as the belt items it
    /// will become - recognition should not have to be learned twice.
    ///
    /// The icon's appearance comes from the Palette's <see cref="Palette.Visuals"/> shape override,
    /// built once at initialization; unticked it is the built-in silhouette at the ore icon radius,
    /// so an untouched Palette draws exactly what it always did.
    ///
    /// The ore is terrain, stamped once from the map's data and never moving during a run,
    /// and a restart re-stamps the identical layout - so this view has no rebuild reason of its own.
    /// The only thing that changes it is the one reason the gate owns: the icon carries an outline, a
    /// screen-space constant, so a zoom rebuilds it and re-stamps the bed at no extra cost.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class OreRenderer : MeshView
    {
        private readonly List<OreSnapshot> _ore = new List<OreSnapshot>();
        private ShapeIconSet _icons;

        protected override Palette.Layer Layer => Colors.ShapePatchLayer;

        protected override void OnInitialized()
        {
            _icons = Colors.ShapeIcons(Colors.Patches.IconRadius, Colors.OutlinePixels);
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Terrain.GetOre(_ore);
            BeginSprites();

            for (int i = 0; i < _ore.Count; i++)
            {
                OreSnapshot patch = _ore[i];
                if (patch.Shape == ShapeType.None) continue;

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

                var centre = new Vector2(x + 0.5f, y + 0.5f);
                AppendIcon(_icons, patch.Shape, centre, null);
            }

            EndSprites();
        }
    }
}
