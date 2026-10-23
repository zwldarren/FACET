using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The tutorial's guide: the line's standard layout drawn on the map as transparent ghosts, plus one
    /// chevron on the edge of the view when that layout is off screen.
    ///
    /// <b>Why ghosts instead of an arrow.</b> A lesson's usual failure is not "which tile" but "what shape":
    /// a cutter is two tiles across a belt, a run has to be dragged tile by tile, and a turret only matters
    /// where its range reaches the lane. One highlighted cell says none of that. So a line carries a
    /// blueprint (<see cref="TutorialGhost"/>) and this view draws it as the finished building would look -
    /// belts as beds with their arrows, a cutter across both its tiles, a turret with its range ring - so the
    /// player can copy the answer instead of inferring it. The ghost is a picture, never a placement: it
    /// casts no shadow, occupies nothing and does not stop the player building somewhere else.
    ///
    /// <b>Off screen is the old problem, unchanged.</b> The first line points at a vein nine tiles from the
    /// Core, and whether the camera shows it is the camera's business. The edge marker reuses
    /// <see cref="ThreatRadar.ScreenReader"/> and <see cref="ThreatRadar.Viewport.EdgeTowards"/> rather than
    /// measuring the screen a second way, so the guide and the threat arrows agree about what "on screen"
    /// means - including the panels, because an arrow under a card points at nothing the player can see.
    ///
    /// <b>What it reads.</b> Only <see cref="TutorialState"/>: the current line, its primary target and its
    /// ghost list. A line with neither draws nothing, and so does a finished lesson - a guide that outlived
    /// its lesson would be an arrow to nowhere.
    /// </summary>
    [DefaultExecutionOrder(116)]
    public sealed class TutorialGuideView : MeshView
    {
        /// <summary>The off-screen chevron, in screen pixels: the guide is an annotation, so it must read
        /// the same at every zoom rather than growing with the tiles.</summary>
        private const float ChevronBackPixels = 7f;
        private const float ChevronHalfWidthPixels = 6f;
        private const float ChevronLengthPixels = 13f;

        /// <summary>How much of the accent a ghost's body carries, and how solid its outline is. Low enough
        /// to read as a plan laid over the map, high enough to survive a belt, an item and a vein under it.
        /// </summary>
        private const float GhostFillAlpha = 0.28f;
        private const float GhostEdgeAlpha = 0.72f;
        private const float GhostBedAlpha = 0.16f;

        /// <summary>The inset of a machine's ghost body inside its tile, matching the placement ghost so
        /// the tutorial's picture and the player's own ghost describe a footprint the same way.</summary>
        private const float GhostBodyInset = 0.13f;

        /// <summary>A ghost outline's screen width, capped so a distant zoom cannot turn a one-tile body
        /// into an outline with a hole in it.</summary>
        private const float GhostOutlinePixels = 2f;
        private const float MaxGhostOutline = 0.16f;

        /// <summary>The camera whose view decides whether the layout is on screen - the same one the locator
        /// is handed, so the two cannot disagree.</summary>
        private Camera _camera;

        /// <summary>Is the tutorial's card up? Relayed by the driver the way the locator's is, because a
        /// marker folded onto the edge must not fold under a card that is there.</summary>
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

        private bool _seenHasGuide;
        private bool _seenComplete;
        private Int2 _seenTarget;
        private int _seenStep = -1;
        private int _seenGhosts = -1;

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
        /// Watches the lesson rather than the map, and then marks itself dirty every frame anyway while there
        /// is a guide: where an off-screen marker goes is the camera's answer and not the world's, so a pan or
        /// a zoom moves it with no state changing at all. The lesson is a handful of ticks long in a run's
        /// life, so the honest cheap thing is to redraw while it lasts - the same trade
        /// <see cref="ThreatRadarView"/> makes for the same reason.
        /// </summary>
        protected override void Observe(in ViewFrame frame)
        {
            TutorialState tutorial = World.Tutorial;

            bool complete = tutorial.Complete;
            bool active = tutorial.Active && !complete;
            TutorialTask task = active ? tutorial.Current : TutorialScript.Empty;

            Int2? primary = task.Target;
            if (!primary.HasValue && task.Ghost.Length > 0) primary = task.Ghost[0].Cell;

            bool hasGuide = primary.HasValue;
            Int2 target = hasGuide ? primary.Value : default;
            int step = tutorial.StepsCompleted;
            int ghosts = task.Ghost.Length;

            if (hasGuide != _seenHasGuide || complete != _seenComplete || target != _seenTarget ||
                step != _seenStep || ghosts != _seenGhosts)
            {
                _seenHasGuide = hasGuide;
                _seenComplete = complete;
                _seenTarget = target;
                _seenStep = step;
                _seenGhosts = ghosts;
                MarkDirty();
            }

            // While there is a guide to draw, every frame is a rebuild: the screen the marker is placed
            // against moves under it without the world changing.
            if (hasGuide) MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            if (!_seenHasGuide || _camera == null) return;

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
                // Out of view: fold the arrow onto the open edge along the sight line to the layout, pointing
                // the way the player has to go - the locator's own answer to the same question.
                Vector2 at = view.EdgeTowards(centre, out Vector2 direction);

                AppendChevron(at, DirFor(direction),
                    ChevronBackPixels * pixel, ChevronHalfWidthPixels * pixel, ChevronLengthPixels * pixel,
                    Colors.HudAccent);
                return;
            }

            AppendBlueprint();
        }

        /// <summary>Draw the current line's standard layout, ghost by ghost. The alpha and the outline are
        /// the same for every piece, so a multi-tile run reads as one plan rather than as a row of unrelated
        /// highlights.</summary>
        private void AppendBlueprint()
        {
            TutorialTask task = World.Tutorial.Current;
            if (task.Ghost.Length == 0)
            {
                AppendFallbackFrame(_seenTarget);
                return;
            }

            for (int i = 0; i < task.Ghost.Length; i++) AppendGhost(task.Ghost[i]);
        }

        /// <summary>One ghost. A belt has no machine row, so it is drawn first; everything else asks the run's
        /// own content table for its behaviour, which is what lets a second gun or a second operator be
        /// ghosted without a line here. A kind this table does not define is skipped: a partial table is
        /// legal, and a guide that threw over an unpainted row would take the whole frame with it.</summary>
        private void AppendGhost(in TutorialGhost ghost)
        {
            Color accent = Colors.HudAccent;
            Color edge = accent;
            edge.a = GhostEdgeAlpha;

            if (ghost.Build == BuildKind.Belt)
            {
                Color bed = Color.Lerp(Colors.BodyColor(BehaviorKind.None), accent, 0.35f);
                bed.a = GhostFillAlpha;
                AppendBeltGhost(ghost.Cell, ghost.Direction, bed, edge);
                return;
            }

            if (!World.Content.TryMachine(ghost.Build, out MachineDef machine)) return;

            Color body = Color.Lerp(Colors.BodyColor(machine.Behavior), accent, 0.35f);
            body.a = GhostFillAlpha;

            switch (machine.Behavior)
            {
                case BehaviorKind.Drill:
                    AppendDrillGhost(ghost.Cell, ghost.Direction, body, edge);
                    break;

                case BehaviorKind.Cutter:
                    AppendCutterGhost(ghost.Cell, ghost.Direction, body, edge);
                    break;

                case BehaviorKind.Turret:
                    AppendTurretGhost(ghost.Cell, ghost.Build, ghost.Direction, body, edge);
                    break;

                case BehaviorKind.Sink:
                    AppendSinkGhost(ghost.Cell, body, edge);
                    break;

                default:
                    AppendBodyQuad(ghost.Cell, body, edge);
                    if (machine.UsesFacing) AppendBuildChevron(CellCentre(ghost.Cell), ghost.Direction, edge);
                    break;
            }
        }

        /// <summary>A belt's ghost: a faint bed and a bright chevron. The bed says "a belt goes here"; the
        /// chevron says the one thing a belt cannot be inferred without - which way the run points.</summary>
        private void AppendBeltGhost(Int2 cell, Dir direction, Color bed, Color edge)
        {
            Color fill = bed;
            fill.a = GhostBedAlpha;
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + 1f, fill);
            AppendCellBorder(cell, edge, Colors.Cursor.EdgeAlpha * 0.55f);
            AppendBuildChevron(CellCentre(cell), direction, edge);
        }

        /// <summary>A drill's ghost: the same hollow frame the machine draws, so the ore under it stays the
        /// identity, plus its output chevron.</summary>
        private void AppendDrillGhost(Int2 cell, Dir direction, Color body, Color edge)
        {
            Palette.MachineLook look = Colors.Machines;
            float lo = look.FrameInset;
            float thickness = look.FrameThickness;
            float hi = 1f - lo;
            float hiInner = hi - thickness;

            float x = cell.X;
            float y = cell.Y;
            AppendRect(x + lo, y + lo, x + hi, y + lo + thickness, body);
            AppendRect(x + lo, y + hiInner, x + hi, y + hi, body);
            AppendRect(x + lo, y + lo, x + lo + thickness, y + hi, body);
            AppendRect(x + hi - thickness, y + lo, x + hi, y + hi, body);

            AppendCellBorder(cell, edge, Colors.Cursor.EdgeAlpha * 0.40f);
            AppendBuildChevron(CellCentre(cell), direction, edge);
        }

        /// <summary>The cutter's ghost: one body on each tile, the blade down the lane the belt feeds and a
        /// chevron in each lane it forks into - the same picture the placement ghost makes, so a player who
        /// follows the tutorial already knows what the cutter asks of them.</summary>
        private void AppendCutterGhost(Int2 cell, Dir facing, Color body, Color edge)
        {
            Dir lane = MachineFootprint.LaneOf(facing);
            Int2 laneCell = cell + lane.Offset();

            AppendBodyQuad(cell, body, edge);
            AppendBodyQuad(laneCell, body, edge);

            Vec2 flow2 = facing.ToVec();
            var flow = new Vector2(flow2.X, flow2.Y);
            var middle = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
            var laneMiddle = new Vector2(laneCell.X + 0.5f, laneCell.Y + 0.5f);

            float blade = Mathf.Min(GhostOutlinePixels * WorldPerPixel, MaxGhostOutline) * 0.6f;
            AppendLane(middle - flow * 0.5f, middle + flow * 0.5f, blade, edge);

            AppendBuildChevron(middle, facing, edge);
            AppendBuildChevron(laneMiddle, facing, edge);
        }

        /// <summary>A turret's ghost: its body, its rest barrel and its range ring. The ring is the whole
        /// reason a turret's tile matters - a gun placed a tile too far from the lane never fires - so the
        /// blueprint draws the coverage the player is buying, not only where the body goes.</summary>
        private void AppendTurretGhost(Int2 cell, BuildKind kind, Dir direction, Color body, Color edge)
        {
            AppendBodyQuad(cell, body, edge);

            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var centre = CellCentre(cell);
            var tip = centre + forward * Colors.Machines.BarrelLength;
            float halfWidth = Colors.Machines.BarrelHalfWidth;

            AppendLane(centre, tip, halfWidth, edge);

            if (World.Content.IsTurret(kind))
                AppendRangeRing(centre, World.Content.Turret(kind).Range, WithAlpha(edge, 0.28f));
        }

        /// <summary>A sink's ghost: a body with a pit and an inward chevron on every side, matching the
        /// machine and the placement ghost - everything goes in, nothing comes out.</summary>
        private void AppendSinkGhost(Int2 cell, Color body, Color edge)
        {
            AppendBodyQuad(cell, body, edge);

            var centre = CellCentre(cell);
            float pit = Colors.Machines.SinkHoleHalfSize;
            AppendRect(centre.x - pit, centre.y - pit, centre.x + pit, centre.y + pit, edge);

            for (int i = 0; i < 4; i++)
            {
                Dir side = (Dir)i;
                Vec2 away2 = side.ToVec();
                var away = new Vector2(away2.X, away2.Y);
                AppendBuildChevron(centre + away * Colors.Machines.ChevronOffset, side.Opposite(), edge);
            }
        }

        /// <summary>An inset body quad with a screen-constant outline: the generic machine ghost, and half of
        /// the two-tile cutter's picture.</summary>
        private void AppendBodyQuad(Int2 cell, Color body, Color edge)
        {
            float lo = GhostBodyInset;
            float hi = 1f - GhostBodyInset;

            AppendRect(cell.X + lo, cell.Y + lo, cell.X + hi, cell.Y + hi, body);
            AppendCellBorder(cell, edge, Colors.Cursor.EdgeAlpha * 0.55f);
        }

        /// <summary>An inset border around a cell. Thin, because the ghost's body already carries the
        /// footprint: the edge is what keeps it visible over a bright vein.</summary>
        private void AppendCellBorder(Int2 cell, Color color, float alphaFraction)
        {
            float thickness = Mathf.Min(GhostOutlinePixels * WorldPerPixel, MaxGhostOutline);
            float a = 0.05f;
            float b = 0.95f;

            Color ink = WithAlpha(color, color.a * alphaFraction);
            AppendRect(cell.X + a, cell.Y + a, cell.X + b, cell.Y + a + thickness, ink);
            AppendRect(cell.X + a, cell.Y + b - thickness, cell.X + b, cell.Y + b, ink);
            AppendRect(cell.X + a, cell.Y + a, cell.X + a + thickness, cell.Y + b, ink);
            AppendRect(cell.X + b - thickness, cell.Y + a, cell.X + b, cell.Y + b, ink);
        }

        /// <summary>The chevron every ghost wears, in the same geometry the cursor uses: world-space, so it
        /// reads as an arrow printed on the tile rather than as UI floating over it.</summary>
        private void AppendBuildChevron(Vector2 centre, Dir direction, Color color)
        {
            Palette.CursorLook look = Colors.Cursor;
            AppendChevron(centre, direction, look.ChevronBack, look.ChevronHalfWidth,
                look.ChevronLength, color);
        }

        /// <summary>The ring a turret ghost projects, as a thin annulus of quads: its width stays a constant
        /// number of screen pixels at any zoom, like the placement ghost's ring.</summary>
        private void AppendRangeRing(Vector2 centre, float radius, Color color)
        {
            float half = Mathf.Max(Colors.GridLinePixels * 2f * WorldPerPixel, 0.012f) * 0.5f;
            int segments = Colors.Cursor.RingSegments;

            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * 2f * i / segments;
                float a1 = Mathf.PI * 2f * (i + 1) / segments;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AppendQuad(
                    centre + d0 * (radius - half), centre + d0 * (radius + half),
                    centre + d1 * (radius + half), centre + d1 * (radius - half), color);
            }
        }

        /// <summary>The old one-tile frame, kept for a line that has a target but no blueprint: a map that
        /// authored only the arrow should still show something, and a frame is the smallest honest answer.</summary>
        private void AppendFallbackFrame(Int2 cell)
        {
            Color accent = Colors.HudAccent;
            float thickness = Mathf.Min(GhostOutlinePixels * WorldPerPixel, MaxGhostOutline);

            Color wash = WithAlpha(accent, accent.a * 0.12f);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + 1f, wash);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + thickness, accent);
            AppendRect(cell.X, cell.Y + 1f - thickness, cell.X + 1f, cell.Y + 1f, accent);
            AppendRect(cell.X, cell.Y, cell.X + thickness, cell.Y + 1f, accent);
            AppendRect(cell.X + 1f - thickness, cell.Y, cell.X + 1f, cell.Y + 1f, accent);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
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
