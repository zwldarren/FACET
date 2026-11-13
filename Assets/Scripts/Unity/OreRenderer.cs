using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws the ore in the ground, one mesh for all of it: a bed quad plus the mineral's figure at each
    /// ore tile. This is the player's map of where each resource lives before any drill is placed, so the mark is
    /// built from the same mineral colour every item wears - a disc for a circle vein, a square for a square one.
    /// Recognition is not learned twice.
    ///
    /// Two veins are one shape. Adjacent tiles of the same mineral share a bed with no seam between them, so a
    /// patch reads as the deposit it is rather than as four tiles that happen to be next to each other; the
    /// inset edge is drawn only where the vein meets ground that is not itself, which is what gives it an
    /// outline. And the mark is the mineral's whole figure in one piece: a vein is one mineral, so the quadrant
    /// seams an item wears - the readout a cutter cuts against - would be a cross drawn for no reason and would
    /// read as a target painted on the ground.
    ///
    /// The mark's appearance comes from the Palette's <see cref="Palette.Visuals"/> mineral override, built
    /// once at initialization; unticked it is the shipped disc or cell at the ore icon radius. An authored
    /// sprite still draws through the quadrant path, since a sprite is a whole mark in its own frame.
    ///
    /// The ore is terrain, stamped once from the map's data and never moving, and a restart re-stamps the identical
    /// layout - so this view has no rebuild reason of its own. The only thing that changes it is the gate's: the mark
    /// carries a screen-space outline, so a zoom rebuilds it and re-stamps the bed at no extra cost.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class OreRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>Grains of mineral scattered through each tile's bed: what says a patch is rock with
        /// something in it rather than a coloured square with a symbol on it. Three is enough to read as
        /// texture and few enough to stay quiet at the tutorial's zoom, where a tile is thirty pixels.</summary>
        private const int Grains = 3;

        private readonly List<OreSnapshot> _ore = new List<OreSnapshot>();
        private ShapeIconSet _icons;

        /// <summary>The two shipped marks as one polygon each, built once: a vein wears the mineral whole.</summary>
        private Vector2[] _disc;
        private Vector2[] _cell;

        protected override Palette.Layer Layer => Colors.ShapePatchLayer;

        protected override void OnInitialized()
        {
            float radius = Colors.Patches.IconRadius;
            _icons = Colors.PatchIcons(radius, Colors.OutlinePixels);

            // The same figures ShapeIconSet's shipped quadrant marks compose into: a quarter disc repeated is a
            // disc, and the quadrant's own cell repeated is the square it makes.
            _disc = ProcMesh.RegularPolygon(Mathf.Max(3, Colors.CircleSides), radius);
            _cell = new[]
            {
                new Vector2(-radius, -radius), new Vector2(radius, -radius),
                new Vector2(radius, radius), new Vector2(-radius, radius),
            };
        }

        private int _seenVersion = -1;
        private int _seenBelts = -1;

        /// <summary>The ground changes mid-run only when a coached lesson lays its next vein; a restart
        /// re-stamps it. Either bumps the field's version. A belt laid on a vein also changes what is drawn - the
        /// mark under it steps aside - so the belt field's revision is a rebuild reason too.</summary>
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

            float inset = Colors.Patches.BedInset;

            for (int i = 0; i < _ore.Count; i++)
            {
                OreSnapshot patch = _ore[i];
                if (patch.Mineral == Quad.Empty) continue;

                // Tile coordinates ARE world coordinates: (x,y) spans [x,x+1]x[y,y+1]. A side the vein carries on
                // across is drawn flush - the neighbour's own bed meets it - and a side it ends on is inset, so
                // the deposit has an edge only where it actually has one.
                float x = patch.Cell.X;
                float y = patch.Cell.Y;
                float left = x + (Joins(patch, Dir.West) ? 0f : inset);
                float right = x + 1f - (Joins(patch, Dir.East) ? 0f : inset);
                float bottom = y + (Joins(patch, Dir.South) ? 0f : inset);
                float top = y + 1f - (Joins(patch, Dir.North) ? 0f : inset);

                AppendRect(left, bottom, right, top, Colors.ShapePatchBed);
                AppendGrains(patch);

                // A cell a belt has been laid on keeps the bed and loses the mark: the mark's job is "there is
                // ore here, put something on it", and the belt is the answer. Left drawn, a strip narrower than
                // the figure cuts it into two crescents and reads as a mistake rather than as mining; what says
                // the run is eating the vein is the belt itself, tinted toward the mineral it stands on.
                if (World.Belts.TryGet(patch.Cell, out _)) continue;

                AppendMark(patch.Mineral, new Vector2(x + 0.5f, y + 0.5f));
            }

            EndSprites();
        }

        /// <summary>Does this vein carry on across <paramref name="side"/>? Only the same mineral continues it -
        /// a square patch beside a circle one is two deposits that touch, and drawing them as one bed would
        /// promise a drill either of them could stand on.</summary>
        private bool Joins(OreSnapshot patch, Dir side)
            => World.Terrain.OreAt(patch.Cell + side.Offset()) == patch.Mineral;

        /// <summary>Three grains of the mineral, in a bed that would otherwise be one flat colour.
        ///
        /// Each grain sits in a corner of its tile, because the mark owns the middle and a belt laid on the
        /// cell owns the cross through it - the corners are the only part of the bed that is always visible,
        /// whatever stands on the tile. They are placed from the cell's own coordinates rather than at random,
        /// so the ground is identical on every rebuild, every zoom and every replay: a texture that reshuffled
        /// would read as the map flickering, and it would flicker hardest while the camera moved.
        ///
        /// The tone is the bed pulled a third of the way to its mineral, not the mineral itself: the mark is
        /// what says which mineral this is, and grains at full strength would compete with it while reading as
        /// a second, smaller mark.</summary>
        private void AppendGrains(OreSnapshot patch)
        {
            Color bed = Colors.ShapePatchBed;
            Color mineral = Colors.MineralColor(patch.Mineral);
            float cx = patch.Cell.X + 0.5f;
            float cy = patch.Cell.Y + 0.5f;

            uint h = Hash(patch.Cell.X, patch.Cell.Y);

            for (int i = 0; i < Grains; i++)
            {
                h = Mix(h);
                float sx = (h & 1u) == 0u ? -1f : 1f;
                h = Mix(h);
                float sy = (h & 1u) == 0u ? -1f : 1f;
                h = Mix(h);
                float rx = 0.27f + Unit(h) * 0.08f;
                h = Mix(h);
                float ry = 0.27f + Unit(h) * 0.08f;
                h = Mix(h);
                bool upright = (h & 1u) != 0u;
                h = Mix(h);
                float length = 0.05f + Unit(h) * 0.12f;
                h = Mix(h);
                float thick = 0.028f + Unit(h) * 0.03f;
                h = Mix(h);

                // Not all grains the same tone or size: one dark inclusion and two pale flecks in a bed reads as
                // rock, where three identical bars read as a pattern the eye starts counting.
                Color grain = Color.Lerp(bed, mineral, 0.28f + Unit(h) * 0.24f);

                float x = cx + sx * rx;
                float y = cy + sy * ry;
                float half = length * 0.5f;
                float halfThick = thick * 0.5f;

                if (upright)
                    AppendRect(x - halfThick, y - half, x + halfThick, y + half, grain);
                else
                    AppendRect(x - half, y - halfThick, x + half, y + halfThick, grain);
            }
        }

        /// <summary>Where a cell's grains go: a hash of its coordinates, spread by the same two constants
        /// every GPU random uses. Deterministic, cheap, and indifferent to the order cells are visited in.</summary>
        private static uint Hash(int x, int y) => Mix((uint)x * 0x9e3779b1u ^ Mix((uint)y * 0x85ebca6bu));

        /// <summary>Scramble all bits of <paramref name="h"/>: the multiply-shift finalizer, so neighbouring
        /// cells draw unrelated grains rather than a pattern that steps along with the map.</summary>
        private static uint Mix(uint h)
        {
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return h;
        }

        /// <summary>0..1 from the low bits of a hash.</summary>
        private static float Unit(uint h) => (h & 0xFFFFFFu) / 16777215f;

        /// <summary>The mineral's whole figure in one piece: fill and one outline around the finished shape, so
        /// no seam is drawn through the middle of a single-mineral vein. An authored sprite is a whole mark in
        /// its own frame and takes the quadrant path instead, which is the one place a sprite can be drawn.</summary>
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
            float radius = Colors.Patches.IconRadius;
            float rim = Mathf.Min(OutlineWidthFor(style), radius * Colors.OutlineMaxRadiusFraction);

            AppendPolygon(points, centre, style.Fill, style.Outline, rim);
        }
    }
}
