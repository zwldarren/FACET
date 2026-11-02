using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws the out-of-view locator: the arrows and marks <see cref="ThreatRadar"/> folds onto the
    /// edge of the open screen, plus the danger frame the screen wears while the Core is chewed on out of
    /// view.
    ///
    /// A view and not a card because it is a mark on the window: the arrow points the way the map lies, so it
    /// sits on the map's viewport edge and has to read in the periphery. A <b>chevron</b> is something coming
    /// at you, a <b>mark</b> is a place to go - told apart by fill, because both are the same four corners. Red
    /// is the wave's and nothing else. Everything is measured in screen pixels times
    /// <see cref="MeshView.WorldPerPixel"/>, so an arrow is not a speck at the wide zoom.</summary>
    [DefaultExecutionOrder(114)]
    public sealed class ThreatRadarView : MeshView
    {
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>();
        private readonly List<Int2> _jams = new List<Int2>();

        /// <summary>Segments whose item has nowhere to go. They mark the same way a jam does - to a player both
        /// are "something has stopped out there, on a tile you cannot see"; the card's line is where the two are
        /// told apart.</summary>
        private readonly List<Int2> _blocked = new List<Int2>();

        /// <summary>Machines holding a piece with no outlet to hand it on to - the third way a line stops, and
        /// the third mark on the same list. A full outlet is not here: that is a loaded line, and it stops
        /// nothing.</summary>
        private readonly List<Int2> _stalled = new List<Int2>();

        /// <summary>Marker scratch, sized to the palette's cap, so a wave off-screen costs no allocation per
        /// frame.</summary>
        private ThreatRadar.Marker[] _markers = new ThreatRadar.Marker[0];

        /// <summary>Point scratch, one per shape, reused for every marker this frame.</summary>
        private readonly Vector2[] _chevron = new Vector2[3];
        private readonly Vector2[] _mark = new Vector2[4];

        private Camera _camera;
        private bool _tutorialUp;
        private bool _infoGrown;
        private float _infoExtra;
        private int _barTiles = HudLayout.BarTiles;

        /// <summary>The window's panel boxes, measured fresh each frame: the same boxes the HUD draws.</summary>
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        protected override Palette.Layer Layer => Colors.RadarLayer;

        /// <summary>Point the locator at the camera whose view it is locating things out of. The driver calls this
        /// on build and on every map change: the camera outlives a map, the card does not.</summary>
        public void InitializeLocator(Camera camera, bool tutorialUp)
        {
            _camera = camera;
            _tutorialUp = tutorialUp;
            MarkDirty();
        }

        /// <summary>
        /// Say whether the HUD's info card is wearing its taller sorter-picker band this frame. An arrow placed
        /// on a strip the picker is drawn over points at nothing the player can see. Cheap to call every frame.
        /// </summary>
        public void SetInfoGrown(bool grown)
        {
            if (_infoGrown == grown) return;

            _infoGrown = grown;
            MarkDirty();
        }

        /// <summary>How much taller the read card is than its base size because its rows wrapped, in design
        /// units. Relayed with <see cref="SetInfoGrown"/>: a card the arrows do not know about is a place an
        /// enemy can stand unseen.</summary>
        public void SetInfoExtra(float extra)
        {
            if (Mathf.Approximately(_infoExtra, extra)) return;

            _infoExtra = extra;
            MarkDirty();
        }

        /// <summary>Say how many keys the build bar is carrying this frame: a tutorial's bar starts one key wide
        /// and grows, and the arrows fold past the strip the HUD actually drew.</summary>
        public void SetBarTiles(int tiles)
        {
            if (_barTiles == tiles) return;

            _barTiles = tiles;
            MarkDirty();
        }

        /// <summary>The player can hide or show the "how to play" card at any time, so the driver relays it
        /// every frame rather than only on a map change.</summary>
        public void SetTutorialUp(bool up)
        {
            if (_tutorialUp == up) return;

            _tutorialUp = up;
            MarkDirty();
        }

        /// <summary>The camera moved, the wave moved, the zoom moved: any of the three moves an arrow, so this
        /// view rebuilds every frame.</summary>
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

            var view = _screen.Measure(_camera, _tutorialUp, look.EdgeInsetPixels, _infoGrown, _barTiles,
                _infoExtra);

            world.Enemies.GetEnemies(_enemies);
            world.Belts.GetJammed(_jams);
            world.Belts.GetBlocked(_blocked);
            _jams.AddRange(_blocked);
            world.Machines.GetStalled(_stalled);
            _jams.AddRange(_stalled);

            int arrows = ThreatRadar.Collect(view, _enemies, _jams, ThreatRadar.CoreCentre(world),
                look.MergePixels * pixel, _markers, out ThreatRadar.Summary summary);

            // The danger frame first, so an arrow that grazes the band draws over it rather than being tinted
            // by a translucent warning.
            //
            // Holds while the Core is hit out of view and only then: a Core the player is watching already has
            // its hit flashes and its shrinking bar.
            int age = ThreatRadar.CoreHitAge(world, look.DangerTicks);
            if (age >= 0 && summary.CoreIsOut) AppendDangerFrame(view, look, pixel, age);

            for (int i = 0; i < arrows; i++) AppendMarker(in _markers[i], look, pixel, world.TickCount);
        }

        /// <summary>One marker: a chevron pointing out along the way the subject lies, with as many pips as the
        /// cluster has members, or a mark where the subject is a place rather than a threat.
        ///
        /// An enemy cluster wears its nearest member's colour and shouts when any of them is hitting something;
        /// anything merely far away fades. A stop is never drawn in that colour, however loud it gets: red is
        /// the player's shorthand for "the wave".</summary>
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
                    // A stopped line is a hole with an edge rather than a dot, in the amber a blocked segment
                    // wears on the bed: the mark only has to say "somewhere out there, on this bearing".
                    AppendStopMark(marker.At, look.StopMarkHalfPixels * pixel,
                        WithAlpha(Colors.BeltBlocked, Fade(marker.Distance, look)), look.StopMarkPixels * pixel);
                    return;

                case ThreatRadar.Subject.Core:
                    // The way home: the Core's own pale, its dark outline, and the quietest thing on the edge.
                    AppendMark(marker.At, look.MarkerHalfWidthPixels * look.CoreMarkerScale * 1.6f * pixel,
                        WithAlpha(Colors.Core, look.CoreAlpha), Colors.OutlinePixels * pixel);
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(marker), marker.What,
                        "a new locator subject needs a marker");
            }
        }

        /// <summary>The chevron: base in from the edge, tip pointing out along the way the subject lies. The
        /// winding stays counter-clockwise, the way the belt's own chevrons do it.</summary>
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
        /// the two read as one object. Capped, and a lone walker is just an arrow.</summary>
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

        /// <summary>
        /// A stopped line's mark: the same four corners as <see cref="AppendMark"/> with nothing inside them -
        /// a place on the edge of the screen rather than a thing standing on it. The stroke grows outward from
        /// those corners. Wound counter-clockwise, the order <see cref="ProcMesh.AppendPolygonOutline"/> needs to
        /// grow the band outward.
        /// </summary>
        private void AppendStopMark(Vector2 at, float radius, Color colour, float stroke)
        {
            _mark[0] = at + Vector2.right * radius;
            _mark[1] = at + Vector2.up * radius;
            _mark[2] = at - Vector2.right * radius;
            _mark[3] = at - Vector2.up * radius;
            AppendPolygonOutline(_mark, Vector2.zero, colour, stroke);
        }

        /// <summary>A filled four-sided mark - a pip, the Core - from a radius and an optional outline. Wound
        /// counter-clockwise: east, north, west, south.</summary>
        private void AppendMark(Vector2 at, float radius, Color colour, float outlineWidth)
        {
            _mark[0] = at + Vector2.right * radius;
            _mark[1] = at + Vector2.up * radius;
            _mark[2] = at - Vector2.right * radius;
            _mark[3] = at - Vector2.up * radius;
            AppendPolygon(_mark, Vector2.zero, colour,
                outlineWidth > 0f ? Colors.Outline : colour, outlineWidth);
        }

        /// <summary>The band around the free area - not the whole screen - that says the Core is being hit out of
        /// view. Drawn <em>inside</em> that area's edge: the strip outside it belongs to a HUD panel, and a frame
        /// laid there would be eaten by the build bar.</summary>
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

        /// <summary>Distance in tiles to the marker's strength: near things are drawn loud, far ones are a hint
        /// at the edge of vision. Urgent markers skip it.</summary>
        private static float Fade(float distance, Palette.RadarLook look)
        {
            float span = Mathf.Max(0.001f, look.FarTiles - look.NearTiles);
            return Mathf.Lerp(look.AlphaNear, look.AlphaFar,
                Mathf.Clamp01((distance - look.NearTiles) / span));
        }

        /// <summary>0..1 for a pulse across the palette's window, walked off the simulation's own clock so the
        /// beat is the run's and not the frame rate's.</summary>
        private static float Pulse(int tick, int ticks)
            => ticks <= 0 ? 1f : 0.5f + 0.5f * Mathf.Sin(tick / (float)ticks * Mathf.PI * 2f);

        private static Color WithAlpha(Color colour, float alpha)
            => new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(alpha));
    }
}
