using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws the out-of-view locator: the arrows and marks <see cref="ThreatRadar"/> folds onto the edge of
    /// the open screen, plus the danger frame the screen wears while the Core is chewed on somewhere out of
    /// view.
    ///
    /// A view and not a HUD panel because it is a mark on the window, not a card to read: the arrow points
    /// the way the map lies, so it sits on the edge of the map's viewport, and it has to be seen in the
    /// periphery. The HUD's threat line reports the same summary the arrows were made from, so the two are
    /// one answer in two registers rather than two rules that can drift.
    ///
    /// Two shapes carry the grammar: a <b>chevron</b> is something moving and coming at you, a <b>mark</b>
    /// is a place to go. Colours are the game's existing ones - an enemy's body colour, the jam's red, the
    /// Core's pale - so an arrow needs no legend. Everything is measured in screen pixels, multiplying a
    /// palette number by <see cref="MeshView.WorldPerPixel"/>, so an arrow is not a speck at the wide zoom
    /// or a blob at the close one.
    ///
    /// Like every other view it reads the simulation and never writes to it; its only extra input is the
    /// camera, which the driver hands it with the map, since the driver owns the rig.
    /// </summary>
    [DefaultExecutionOrder(114)]
    public sealed class ThreatRadarView : MeshView
    {
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>();
        private readonly List<Int2> _jams = new List<Int2>();

        /// <summary>Marker scratch, sized to the palette's cap: the arrows are folded into it and drawn
        /// straight out of it, so a wave off-screen costs no allocation per frame.</summary>
        private ThreatRadar.Marker[] _markers = new ThreatRadar.Marker[0];

        /// <summary>Point scratch, one per shape, reused for every marker this frame - a view drawing thirty
        /// marks must not allocate thirty point lists to do it.</summary>
        private readonly Vector2[] _chevron = new Vector2[3];
        private readonly Vector2[] _mark = new Vector2[4];

        private Camera _camera;
        private bool _tutorialUp;

        /// <summary>The window's panel boxes, measured fresh each frame: the same boxes the HUD draws, so a
        /// walker under one is out of view for the arrows and for the card alike.</summary>
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        protected override Palette.Layer Layer => Colors.RadarLayer;

        /// <summary>Point the locator at the camera whose view it is locating things out of, and say whether
        /// the tutorial card is up. The driver calls this on build and on every map change - the camera
        /// outlives a map, the card does not, and the arrows give up the strip the card wears.</summary>
        public void InitializeLocator(Camera camera, bool tutorialUp)
        {
            _camera = camera;
            _tutorialUp = tutorialUp;
            MarkDirty();
        }

        /// <summary>The camera moved, the wave moved, the zoom moved: any of the three moves an arrow, so this
        /// view rebuilds every frame like the enemies it points at.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            SimWorld world = World;
            Palette.RadarLook look = Colors.Radar;
            if (world == null || _camera == null || look == null) return;

            float pixel = frame.WorldPerPixel;
            if (pixel <= 0f) return;

            if (_markers.Length != look.MaxMarkers)
                _markers = new ThreatRadar.Marker[Mathf.Max(1, look.MaxMarkers)];

            var view = _screen.Measure(_camera, _tutorialUp, look.EdgeInsetPixels);

            world.Enemies.GetEnemies(_enemies);
            world.Belts.GetJammed(_jams);

            int arrows = ThreatRadar.Collect(view, _enemies, _jams, ThreatRadar.CoreCentre(world),
                look.MergePixels * pixel, _markers, out ThreatRadar.Summary summary);

            // The danger frame first, so an arrow that grazes the band draws over it rather than being tinted
            // by a translucent warning.
            //
            // The run's one losing condition, said at the screen's edge because the thing being lost is off
            // it: it holds while the Core is hit out of view, and only then - a Core the player is watching
            // already has its hit flashes and its shrinking bar.
            int age = ThreatRadar.CoreHitAge(world, look.DangerTicks);
            if (age >= 0 && summary.CoreIsOut) AppendDangerFrame(view, look, pixel, age);

            for (int i = 0; i < arrows; i++) AppendMarker(in _markers[i], look, pixel, world.TickCount);
        }

        /// <summary>One marker: a chevron pointing out along the way the subject lies, with as many pips as
        /// the cluster has members (up to the palette's cap), or a mark where the subject is a place rather
        /// than a threat.
        ///
        /// An enemy cluster wears its nearest member's colour and shouts when any of them is hitting
        /// something; anything merely far away fades.</summary>
        private void AppendMarker(in ThreatRadar.Marker marker, Palette.RadarLook look, float pixel, int tick)
        {
            Color colour;

            switch (marker.What)
            {
                case ThreatRadar.Subject.Threat:
                    colour = WithAlpha(Colors.EnemyColor(marker.Kind), marker.Urgent
                        ? Mathf.Lerp(look.UrgentPulseLow, 1f, Pulse(tick, look.UrgentPulseTicks))
                        : Fade(marker.Distance, look));
                    AppendChevron(marker, look, pixel, colour);
                    AppendPips(marker, look, pixel, colour);
                    return;

                case ThreatRadar.Subject.Jam:
                    AppendMark(marker.At, look.JamMarkerHalfPixels * pixel,
                        WithAlpha(Colors.Jam, Fade(marker.Distance, look)), 0f);
                    return;

                case ThreatRadar.Subject.Core:
                    // The way home: the Core's own pale, its dark outline, and the quietest thing on the
                    // edge. Nothing is lost by not looking at it; a lot is lost by not being able to find it.
                    AppendMark(marker.At, look.MarkerHalfWidthPixels * look.CoreMarkerScale * 1.6f * pixel,
                        WithAlpha(Colors.Core, look.CoreAlpha), Colors.OutlinePixels * pixel);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(marker), marker.What,
                        "a new locator subject needs a marker");
            }
        }

        /// <summary>The chevron: base in from the edge, tip pointing out along the way the subject lies. Tip
        /// last and the two base corners first, so the winding stays counter-clockwise the way the belt's own
        /// chevrons do it.</summary>
        private void AppendChevron(in ThreatRadar.Marker marker, Palette.RadarLook look, float pixel, Color colour)
        {
            Vector2 dir = marker.Direction;
            var right = new Vector2(-dir.y, dir.x);
            float length = look.MarkerLengthPixels * pixel;
            float width = look.MarkerHalfWidthPixels * pixel;

            _chevron[0] = marker.At - dir * (length * 0.5f) + right * width;
            _chevron[1] = marker.At - dir * (length * 0.5f) - right * width;
            _chevron[2] = marker.At + dir * (length * 0.5f);

            AppendPolygon(_chevron, Vector2.zero, colour, WithAlpha(Colors.Outline, colour.a),
                Colors.OutlinePixels * pixel);
        }

        /// <summary>One dot per member of the cluster, stacked along the edge and pulled in from the arrow so
        /// the two read as one object. Capped - a hundred enemies is an arrow with six dots and a number the
        /// status card says out loud - and a lone walker is just an arrow.</summary>
        private void AppendPips(in ThreatRadar.Marker marker, Palette.RadarLook look, float pixel, Color colour)
        {
            int pips = Mathf.Min(marker.Count, look.MaxPips);
            if (pips < 2) return;

            Vector2 dir = marker.Direction;
            var right = new Vector2(-dir.y, dir.x);
            float radius = look.PipRadiusPixels * pixel;
            float gap = radius * 2f + look.PipGapPixels * pixel;
            float back = look.MarkerLengthPixels * pixel * 0.8f + radius + gap * 0.2f;

            for (int i = 0; i < pips; i++)
            {
                float along = (i - (pips - 1) * 0.5f) * gap;
                AppendMark(marker.At - dir * back + right * along, radius, colour, 0f);
            }
        }

        /// <summary>A four-sided mark - a jam, a pip, the Core - standing on a corner, from a radius and an
        /// optional outline. Wound counter-clockwise: east, north, west, south.</summary>
        private void AppendMark(Vector2 at, float radius, Color colour, float outlineWidth)
        {
            _mark[0] = at + Vector2.right * radius;
            _mark[1] = at + Vector2.up * radius;
            _mark[2] = at - Vector2.right * radius;
            _mark[3] = at - Vector2.up * radius;
            AppendPolygon(_mark, Vector2.zero, colour,
                outlineWidth > 0f ? Colors.Outline : colour, outlineWidth);
        }

        /// <summary>The band around the free area - not the whole screen - that says the Core is being hit
        /// out of view. Drawn <em>inside</em> that area's edge, because the strip outside it belongs to a HUD
        /// panel and a frame laid there would be eaten by the build bar. It fades over
        /// <see cref="Palette.RadarLook.DangerTicks"/> from the hit, marking the beat rather than haunting the
        /// run.</summary>
        private void AppendDangerFrame(in ThreatRadar.Viewport view, Palette.RadarLook look, float pixel,
            int age)
        {
            float life = Mathf.Clamp01(1f - age / (float)Mathf.Max(1, look.DangerTicks));
            Color colour = WithAlpha(Colors.HudWarn, look.DangerAlpha * life);
            float thickness = look.DangerThicknessPixels * pixel;

            Rect free = view.Free;
            if (free.width <= 0f || free.height <= 0f) return;

            AppendRect(free.xMin, free.yMin, free.xMax, free.yMin + thickness, colour);
            AppendRect(free.xMin, free.yMax - thickness, free.xMax, free.yMax, colour);
            AppendRect(free.xMin, free.yMin, free.xMin + thickness, free.yMax, colour);
            AppendRect(free.xMax - thickness, free.yMin, free.xMax, free.yMax, colour);
        }

        /// <summary>Distance in tiles to the marker's strength: near things are drawn loud, far ones are a
        /// hint at the edge of vision. Urgent markers skip it - see <see cref="AppendMarker"/>.</summary>
        private static float Fade(float distance, Palette.RadarLook look)
        {
            float span = Mathf.Max(0.001f, look.FarTiles - look.NearTiles);
            return Mathf.Lerp(look.AlphaNear, look.AlphaFar,
                Mathf.Clamp01((distance - look.NearTiles) / span));
        }

        /// <summary>0..1 for a pulse across the palette's window, walked off the simulation's own clock so the
        /// beat is the run's beat and not the frame rate's.</summary>
        private static float Pulse(int tick, int ticks)
            => ticks <= 0 ? 1f : 0.5f + 0.5f * Mathf.Sin(tick / (float)ticks * Mathf.PI * 2f);

        private static Color WithAlpha(Color colour, float alpha)
            => new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(alpha));
    }
}
