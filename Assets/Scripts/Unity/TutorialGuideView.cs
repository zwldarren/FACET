using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The tutorial's guide: one accent-coloured frame around the tile the current task is about, and one
    /// chevron saying "here" - either beside the tile when the player can see it, or out on the edge of the
    /// view when they cannot.
    ///
    /// <b>Why a world-space arrow and an edge marker are the same view.</b> A tutorial's usual failure is
    /// pointing at something off-screen: the first line points at a vein nine tiles from the Core, and which
    /// of the two the camera happens to show is the camera's business. So the two cases are one piece of
    /// geometry with one branch - in view, the marker sits beside the target; out of view, it is folded onto
    /// the open screen's edge along the sight line to it, which is exactly what
    /// <see cref="ThreatRadarView"/> does for a threat. The reader reuses <see cref="ThreatRadar.ScreenReader"/>
    /// and <see cref="ThreatRadar.Viewport.EdgeTowards"/> rather than measuring the screen a second way, so the
    /// guide and the threat arrows agree about what "on screen" means - including the panels, because an arrow
    /// under a card points at nothing the player can see.
    ///
    /// <b>What it reads.</b> Only <see cref="TutorialState"/>: the current line and the one tile it names. A
    /// line with no target draws nothing, and so does a finished lesson - a guide that outlived its lesson
    /// would be an arrow to nowhere.
    /// </summary>
    [DefaultExecutionOrder(116)]
    public sealed class TutorialGuideView : MeshView
    {
        /// <summary>The frame around the target tile, and the chevron beside it, in screen pixels: the guide is
        /// an annotation, so it must read the same at every zoom rather than growing with the tiles.</summary>
        private const float FramePixels = 2f;
        private const float ChevronBackPixels = 7f;
        private const float ChevronHalfWidthPixels = 6f;
        private const float ChevronLengthPixels = 13f;

        /// <summary>How far outside the target tile's edge the beside-it chevron's centre sits, in screen
        /// pixels: far enough not to sit on the tile it points at, near enough to read as pointing at it.</summary>
        private const float ChevronGapPixels = 10f;

        /// <summary>The frame never gets thicker than this, whatever the zoom: a tile is one unit, and a frame
        /// wider than a fifth of it stops being a frame.</summary>
        private const float MaxFrame = 0.18f;

        /// <summary>The camera whose view decides whether the target is on screen - the same one the locator is
        /// handed, so the two cannot disagree.</summary>
        private Camera _camera;

        /// <summary>Is the tutorial's card up? Relayed by the driver the way the locator's is, because a marker
        /// folded onto the edge must not fold under a card that is there.</summary>
        private bool _tutorialUp = true;

        /// <summary>Is the info card wearing its taller sorter-picker band? Relayed the same way, and for the
        /// same reason: the band is a panel box the marker must fold past.</summary>
        private bool _infoGrown;

        /// <summary>How many keys the build bar carries this frame: a tutorial's bar grows a key at a time, so
        /// the strip the marker folds past is the run's, not the full table's.</summary>
        private int _barTiles = HudLayout.BarTiles;

        /// <summary>Scratch for the panel boxes <see cref="ThreatRadar.ScreenReader"/> measures, owned here so
        /// this view asks its question every frame without allocating.</summary>
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        private bool _seenHasTarget;
        private bool _seenComplete;
        private Int2 _seenTarget;
        private int _seenStep = -1;

        protected override Palette.Layer Layer => Colors.GuideLayer;

        /// <summary>Hand the view the camera to measure the screen against. Called on every map load, like the
        /// locator's own binding, because the camera is the scene's and a map change does not replace it.</summary>
        public void InitializeGuide(Camera camera)
        {
            _camera = camera;
            MarkDirty();
        }

        /// <summary>Tell the guide whether the tutorial card is up, so an arrow is not folded onto the edge
        /// underneath it. One frame of lag, like the locator's.</summary>
        public void SetTutorialUp(bool up)
        {
            if (up == _tutorialUp) return;

            _tutorialUp = up;
            MarkDirty();
        }

        /// <summary>Tell the guide whether the info card is wearing the sorter picker's band - the other panel
        /// that is sometimes there. Relayed by the driver, one frame of lag.</summary>
        public void SetInfoGrown(bool grown)
        {
            if (grown == _infoGrown) return;

            _infoGrown = grown;
            MarkDirty();
        }

        /// <summary>Tell the guide how many keys the build bar carries this frame, so the strip it folds past
        /// is the one on screen rather than the full shelf.</summary>
        public void SetBarTiles(int tiles)
        {
            if (tiles == _barTiles) return;

            _barTiles = tiles;
            MarkDirty();
        }

        /// <summary>
        /// Watches the lesson rather than the map, and then marks itself dirty every frame anyway: where the
        /// marker goes is the camera's answer and not the world's, so a pan or a zoom moves it with no state
        /// changing at all. The lesson is a handful of ticks long in a run's life, so the honest cheap thing is
        /// to redraw while it lasts - the same trade <see cref="ThreatRadarView"/> makes for the same reason.
        /// </summary>
        protected override void Observe(in ViewFrame frame)
        {
            TutorialState tutorial = World.Tutorial;

            bool complete = tutorial.Complete;
            bool hasTarget = !complete && tutorial.Active && tutorial.Current.Target.HasValue;
            Int2 target = hasTarget ? tutorial.Current.Target.Value : default;
            int step = tutorial.StepsCompleted;

            if (hasTarget != _seenHasTarget || complete != _seenComplete || target != _seenTarget ||
                step != _seenStep)
            {
                _seenHasTarget = hasTarget;
                _seenComplete = complete;
                _seenTarget = target;
                _seenStep = step;
                MarkDirty();
            }

            // While there is a guide to draw, every frame is a rebuild: the screen the marker is placed against
            // moves under it without the world changing.
            if (hasTarget) MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            if (!_seenHasTarget || _camera == null) return;

            float pixel = frame.WorldPerPixel;
            if (pixel <= 0f) return;

            Palette.RadarLook look = Colors.Radar;
            if (look == null) return;

            Vector2 centre = CellCentre(_seenTarget);

            // The same measurement the locator makes, with the same margin off the frame: the guide and the
            // threat arrows have to agree about what "on screen" means, panels included.
            ThreatRadar.Viewport view =
                _screen.Measure(_camera, _tutorialUp, look.EdgeInsetPixels, _infoGrown, _barTiles);

            if (!view.IsOpen(centre))
            {
                // Out of view: fold the arrow onto the open edge along the sight line to the tile, pointing the
                // way the player has to go - the locator's own answer to the same question.
                Vector2 at = view.EdgeTowards(centre, out Vector2 direction);

                AppendChevron(at, DirFor(direction),
                    ChevronBackPixels * pixel, ChevronHalfWidthPixels * pixel, ChevronLengthPixels * pixel,
                    Colors.HudAccent);
                return;
            }

            AppendTargetFrame(_seenTarget, pixel);

            // In view: a chevron on the near side, pointing into the tile. "Near side" is the side facing the
            // camera's centre, because that is the side the player is looking from - unless the tile is the
            // middle of the screen, where every side is equally near and north is the honest default.
            Dir facing = DirFor(centre - view.Centre);
            Vec2 step = facing.ToVec();
            var forward = new Vector2(step.X, step.Y);
            Vector2 beside = centre - forward * (0.5f + ChevronGapPixels * pixel);

            AppendChevron(beside, facing,
                ChevronBackPixels * pixel, ChevronHalfWidthPixels * pixel, ChevronLengthPixels * pixel,
                Colors.HudAccent);
        }

        /// <summary>The frame around the target tile: a faint wash so the tile reads as marked at any zoom, and
        /// a band on each edge so the mark is a rectangle rather than a tint under whatever stands there.</summary>
        private void AppendTargetFrame(Int2 cell, float pixel)
        {
            Color accent = Colors.HudAccent;
            var wash = new Color(accent.r, accent.g, accent.b, accent.a * 0.12f);
            float thickness = Mathf.Min(FramePixels * pixel, MaxFrame);

            float x = cell.X;
            float y = cell.Y;

            AppendRect(x, y, x + 1f, y + 1f, wash);
            AppendRect(x, y, x + 1f, y + thickness, accent);
            AppendRect(x, y + 1f - thickness, x + 1f, y + 1f, accent);
            AppendRect(x, y, x + thickness, y + 1f, accent);
            AppendRect(x + 1f - thickness, y, x + 1f, y + 1f, accent);
        }

        /// <summary>The cardinal direction a vector points closest to. A chevron is drawn on the tile grid, so a
        /// sight line at 37 degrees becomes the cardinal the player's eye reads as "that way"; a vector too
        /// short to have a direction at all - the target under the camera's centre - reads as north rather than
        /// as a division by nothing.</summary>
        private static Dir DirFor(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) < 1e-4f && Mathf.Abs(direction.y) < 1e-4f) return Dir.North;

            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x >= 0f ? Dir.East : Dir.West;

            return direction.y >= 0f ? Dir.North : Dir.South;
        }
    }
}
