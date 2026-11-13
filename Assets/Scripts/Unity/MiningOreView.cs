using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The vein's figure, drawn back on top of a belt that mines it (<see cref="BeltMining"/>), so the run reads
    /// as coming out of the ore: the belt starts under the figure, a mined piece appears under it and slides
    /// out along the belt, and nothing frames the tile.
    ///
    /// The ore view itself lies under the belts and leaves a belted cell's figure out (a belt strip drawn across
    /// it cut it in two); this view draws that one figure again, over the belt and the pieces on it. The figure
    /// is the ore view's own - same disc or square, same colours - a touch larger, so it covers the belt's hub
    /// and the belt seems to leave from its edge. Looks only.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class MiningOreView : MeshView
    {
        private readonly List<OreSnapshot> _ore = new List<OreSnapshot>();
        private ShapeIconSet _icons;
        private Vector2[] _disc;
        private Vector2[] _cell;
        private float _radius;

        private int _seenVersion = -1;
        private int _seenBelts = -1;

        /// <summary>Over the belts and the pieces riding them, so a piece is mined out from under the figure.</summary>
        protected override Palette.Layer Layer => Colors.MachineLayer;

        protected override void OnInitialized()
        {
            // Big enough to hide the belt's hub and rails under it, never smaller than the ore's own figure.
            Palette.BeltLook belts = Colors.Belts;
            _radius = Mathf.Max(Colors.Patches.IconRadius, (belts.HalfWidth + Mathf.Max(0f, belts.RailWidth)) * 1.12f);

            _icons = Colors.PatchIcons(_radius, Colors.OutlinePixels);
            _disc = ProcMesh.RegularPolygon(Mathf.Max(3, Colors.CircleSides), _radius);
            _cell = new[]
            {
                new Vector2(-_radius, -_radius), new Vector2(_radius, -_radius),
                new Vector2(_radius, _radius), new Vector2(-_radius, _radius),
            };
        }

        protected override void Observe(in ViewFrame frame)
        {
            int version = World.Terrain.Version;
            int belts = World.Belts.Revision;
            if (version == _seenVersion && belts == _seenBelts) return;

            _seenVersion = version;
            _seenBelts = belts;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Terrain.GetOre(_ore);
            BeginSprites();

            for (int i = 0; i < _ore.Count; i++)
            {
                OreSnapshot ore = _ore[i];
                if (ore.Mineral == Quad.Empty || !World.Belts.Has(ore.Cell)) continue;
                AppendMark(ore.Mineral, new Vector2(ore.Cell.X + 0.5f, ore.Cell.Y + 0.5f));
            }

            EndSprites();
        }

        /// <summary>The ore view's figure, at this view's radius.</summary>
        private void AppendMark(Quad mineral, Vector2 centre)
        {
            VisualStyle style = _icons.Style(mineral, Shape.NE);
            if (style == null) return;

            if (style.Source == VisualSource.Sprite)
            {
                AppendIcon(_icons, Shape.Full(mineral), centre, null);
                return;
            }

            Vector2[] points = mineral == Quad.Circle ? _disc : _cell;
            float rim = Mathf.Min(OutlineWidthFor(style), _radius * Colors.OutlineMaxRadiusFraction);
            AppendPolygon(points, centre, style.Fill, style.Outline, rim);
        }
    }
}
