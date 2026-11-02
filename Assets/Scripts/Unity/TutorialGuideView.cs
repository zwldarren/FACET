using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>The tutorial's guide: the line's standard layout drawn on the map as transparent ghosts, plus
    /// one chevron on the edge of the view when that layout is off screen.
    ///
    /// Ghosts rather than an arrow because a lesson's usual failure is not "which tile" but "what shape". Each
    /// ghost wears the silhouette its building will have, in that building's colour, with the HUD accent as its
    /// rim - the shape answers "what goes here", the rim answers "this is the plan". The two figures that are
    /// geometry rather than a lookup come from <see cref="MachineRenderer"/>, so a ghost cannot promise a shape
    /// the placed machine does not stand on. Belt ghosts are the exception: they wear the accent throughout,
    /// since a faded cream bed reads as a belt already laid.
    ///
    /// Only reads <see cref="TutorialState"/>: a line with no target or ghost draws nothing, and so does a
    /// finished lesson.</summary>
    [DefaultExecutionOrder(116)]
    public sealed class TutorialGuideView : MeshView
    {
        /// <summary>The off-screen chevron, in screen pixels: the guide is an annotation, so it reads the same
        /// at every zoom rather than growing with the tiles.</summary>
        private const float ChevronBackPixels = 7f;
        private const float ChevronHalfWidthPixels = 6f;
        private const float ChevronLengthPixels = 13f;

        /// <summary>How a machine ghost is weighted: a light wash where the body will stand, the plan's ink on
        /// its edge. The ink carries the drawing and the fill only says what will be covered, so the map the
        /// line is about stays the brightest thing on the tile. Belt ghosts have always been weighted this
        /// way.</summary>
        private const float GhostBodyAlpha = 0.38f;
        private const float GhostEdgeAlpha = 0.90f;

        /// <summary>The belt's three alphas, all in the guide's accent rather than in the belt's own cream: a bed
        /// in its real colour reads as a belt that was already laid, whichever way it was faded.</summary>
        private const float GhostBeltAlpha = 0.30f;
        private const float GhostRailAlpha = 0.50f;
        private const float GhostArrowAlpha = 0.85f;

        /// <summary>Fainter than the placement ghost's own ring: the ring is context, the body is the answer to
        /// "what goes here".</summary>
        private const float GhostRingAlpha = 0.30f;

        /// <summary>How far a machine ghost's body is mixed toward the outline, so its slate does not stand on
        /// pale ground as solidly as the building it promises. The belt is deliberately not mixed.</summary>
        private const float GhostInkMix = 0.30f;

        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        /// <summary>The same camera the locator is handed, so the two cannot disagree about what is on
        /// screen.</summary>
        private Camera _camera;

        /// <summary>Relayed by the driver the way the locator's is: a marker folded onto the edge must not fold
        /// under a card that is there.</summary>
        private bool _tutorialUp = true;

        private bool _infoGrown;

        private float _infoExtra;

        /// <summary>How many keys the build bar carries this frame: a tutorial's bar grows a key at a time, so
        /// the strip the marker folds past is the run's, not the full table's.</summary>
        private int _barTiles = HudLayout.BarTiles;

        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        private bool _seenHasGuide;
        private bool _seenComplete;
        private Int2 _seenTarget;
        private int _seenStep = -1;
        private int _seenGhosts = -1;

        /// <summary>Cached once: rebuilding a polygon per ghost per frame would allocate in the one view that
        /// redraws on every frame a lesson is up.</summary>
        private Vector2[] _operatorBody;
        private Vector2[] _turretBody;
        private Vector2[] _splitterHub;
        private Vector2[] _sorterBody;
        private Vector2[] _sorterTurned;
        private Vector2[] _wallBody;
        private Vector2[] _sinkBody;
        private Vector2[] _sinkHole;

        /// <summary>The cutter's fork, one body per facing: its silhouette lies along the flow, so its four
        /// facings are four shapes rather than one turned.</summary>
        private Vector2[][] _cutterBodies;

        private readonly Vector2[] _barrel = new Vector2[4];
        private readonly Vector2[] _bracket = new Vector2[4];
        private readonly Rect[] _frameBars = new Rect[4];

        /// <summary>The renderer this view draws through, and the plane its last frame was put on. One mesh on
        /// one object, but it draws two kinds of thing on different planes: see <see cref="PlaceOn"/>.</summary>
        private MeshRenderer _renderer;
        private float _placedZ = float.NaN;
        private int _placedOrder = int.MinValue;

        protected override Palette.Layer Layer => Colors.GuideLayer;

        /// <summary>Called on every map load, like the locator's own binding: the camera is the scene's and a
        /// map change does not replace it.</summary>
        public void InitializeGuide(Camera camera)
        {
            _camera = camera;
            MarkDirty();
        }

        /// <summary>So an arrow is not folded onto the edge underneath the card. One frame of lag, like the
        /// locator's.</summary>
        public void SetTutorialUp(bool up)
        {
            if (up == _tutorialUp) return;

            _tutorialUp = up;
            MarkDirty();
        }

        /// <summary>The other panel that is sometimes there. Relayed by the driver, one frame of lag.</summary>
        public void SetInfoGrown(bool grown)
        {
            if (grown == _infoGrown) return;

            _infoGrown = grown;
            MarkDirty();
        }

        /// <summary>An arrow folded onto the edge must not fold under the part of the card the base box did not
        /// cover.</summary>
        public void SetInfoExtra(float extra)
        {
            if (Mathf.Approximately(extra, _infoExtra)) return;

            _infoExtra = extra;
            MarkDirty();
        }

        /// <summary>The strip it folds past is the one on screen, not the full shelf.</summary>
        public void SetBarTiles(int tiles)
        {
            if (tiles == _barTiles) return;

            _barTiles = tiles;
            MarkDirty();
        }

        /// <summary>Built the way <see cref="MachineRenderer"/> builds the bodies it draws, plus the two rounded
        /// blocks (wall, sink) that machine has no operator row for.</summary>
        protected override void OnInitialized()
        {
            _renderer = GetComponent<MeshRenderer>();

            Palette.MachineLook look = Colors.Machines;
            float corner = look.CornerRadius;

            _operatorBody = ProcMesh.Rounded(ProcMesh.RegularPolygon(6, look.OperatorRadius, 30f), corner);
            _turretBody = ProcMesh.Rounded(ProcMesh.RegularPolygon(8, look.TurretRadius), corner);
            _splitterHub = ProcMesh.Rounded(
                ProcMesh.RegularPolygon(4, look.SplitterHubHalfSize * 1.4143f, 45f), corner);
            _sorterBody = ProcMesh.Rounded(ProcMesh.RegularPolygon(3, look.SorterRadius), corner * 0.75f);
            _sorterTurned = new Vector2[_sorterBody.Length];
            _wallBody = ProcMesh.Rounded(
                ProcMesh.RegularPolygon(4, (0.5f - look.WallInset) * 1.4143f, 45f), corner);
            _sinkBody = ProcMesh.Rounded(
                ProcMesh.RegularPolygon(4, (0.5f - look.SinkInset) * 1.4143f, 45f), corner);
            _sinkHole = ProcMesh.Rounded(
                ProcMesh.RegularPolygon(4, look.SinkHoleHalfSize * 1.4143f, 45f), corner * 0.6f);

            _cutterBodies = new Vector2[4][];
            for (int facing = 0; facing < _cutterBodies.Length; facing++)
                _cutterBodies[facing] = ProcMesh.Rounded(
                    MachineRenderer.CutterBodyFigure((Dir)facing, look.OperatorRadius), corner);
        }

        /// <summary>Watches the lesson, and then marks itself dirty every frame anyway while there is a guide:
        /// where an off-screen marker goes is the camera's answer, so a pan or a zoom moves it with no state
        /// changing. The lesson lasts a handful of ticks, so redrawing while it does is the cheap honest trade -
        /// the same one <see cref="ThreatRadarView"/> makes.</summary>
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
                _screen.Measure(_camera, _tutorialUp, look.EdgeInsetPixels, _infoGrown, _barTiles, _infoExtra);

            if (!view.IsOpen(centre))
            {
                // Out of view: fold the arrow onto the open edge along the sight line to the layout, pointing
                // the way the player has to go - the locator's own answer to the same question.
                Vector2 at = view.EdgeTowards(centre, out Vector2 direction);

                PlaceOn(Colors.GuideMarkerLayer);
                AppendChevron(at, DirFor(direction),
                    ChevronBackPixels * pixel, ChevronHalfWidthPixels * pixel, ChevronLengthPixels * pixel,
                    Colors.HudAccent);
                return;
            }

            PlaceOn(Colors.GuideLayer);
            AppendBlueprint();
        }

        /// <summary>Put this frame's geometry on one of the guide's two planes.
        ///
        /// The blueprint lies on the map's plane (<see cref="Palette.GuideLayer"/>), so the ground and a vein
        /// are behind it and everything built on that ground is in front: a plan drawn over a laid belt would
        /// cover the one answer the player needs next. The off-screen arrow gets the annotation plane
        /// (<see cref="Palette.GuideMarkerLayer"/>) instead, since it is not a tile and has to read over
        /// whatever the camera is pointed at. The two never share a frame, so one mesh on one renderer can carry
        /// both.</summary>
        private void PlaceOn(Palette.Layer layer)
        {
            if (layer.Z == _placedZ && layer.SortingOrder == _placedOrder) return;

            _placedZ = layer.Z;
            _placedOrder = layer.SortingOrder;

            transform.position = new Vector3(0f, 0f, layer.Z);
            if (_renderer != null) _renderer.sortingOrder = layer.SortingOrder;
        }

        /// <summary>Each piece is drawn from its own building rather than from a shared highlight, so the row
        /// reads as a plan of several kinds rather than as a run of identical cells.</summary>
        private void AppendBlueprint()
        {
            TutorialTask task = World.Tutorial.Current;
            TutorialGhost[] ghosts = task.Ghost;

            if (ghosts.Length == 0)
            {
                AppendFallbackFrame(_seenTarget);
                return;
            }

            for (int i = 0; i < ghosts.Length; i++) AppendGhost(ghosts, i);
        }

        /// <summary>One ghost, in its building's own figure. A belt is drawn first because it is not a machine
        /// row; everything else asks the run's content table for its behaviour, which is what lets a second gun
        /// be ghosted without a line here. A kind this table does not define is skipped: a partial table is
        /// legal, and a guide that threw over one would take the whole frame with it.</summary>
        private void AppendGhost(TutorialGhost[] ghosts, int index)
        {
            TutorialGhost ghost = ghosts[index];

            if (ghost.Build == BuildKind.Belt)
            {
                AppendBeltGhost(ghost, ghosts);
                return;
            }

            if (!World.Content.TryMachine(ghost.Build, out MachineDef machine)) return;

            Color body = Ink(Colors.BodyColor(machine.Behavior), GhostBodyAlpha);
            Color edge = Rim();

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

                case BehaviorKind.Pipe:
                    AppendPipeGhost(ghost.Cell, ghost.Direction, body);
                    break;

                case BehaviorKind.Sorter:
                    AppendSorterGhost(ghost.Cell, ghost.Direction, body, edge);
                    break;

                case BehaviorKind.Splitter:
                    AppendShape(_splitterHub, CellCentre(ghost.Cell), body, edge);
                    break;

                case BehaviorKind.Sink:
                    AppendSinkGhost(ghost.Cell, body, edge);
                    break;

                case BehaviorKind.None:
                    AppendShape(_wallBody, CellCentre(ghost.Cell), body, edge);
                    break;

                default:
                    // The chambered operators (rotator, stacker) and any future one-tile machine that is not
                    // otherwise named: the operator's own hexagon, so the family still reads as one.
                    AppendShape(_operatorBody, CellCentre(ghost.Cell), body, edge);
                    break;
            }
        }

        /// <summary>A belt's ghost: the bed, its rails and one lane per side something arrives from, exactly as
        /// the placed belt is drawn, plus the direction chevron - which way a run points is the one thing a bed
        /// cannot say alone. The feeds are read off the blueprint rather than off the world: a line is a picture
        /// of a run that does not exist yet, so its corners have to be inferred from the ghosts.</summary>
        private void AppendBeltGhost(in TutorialGhost ghost, TutorialGhost[] ghosts)
        {
            Palette.BeltLook look = Colors.Belts;
            float half = look.HalfWidth;
            float rail = Mathf.Max(0f, look.RailWidth);

            // Two passes, rails first, like the belt view: a bed laid over its own rail would leave the
            // neighbour's rail drawn across the joint. All three parts are the guide's accent, so the plan reads
            // as a plan at a glance.
            if (rail > 0f)
                AppendBed(ghost.Cell, ghost.Direction, ghosts, half + rail, WithAlpha(Colors.HudAccent, GhostRailAlpha));

            AppendBed(ghost.Cell, ghost.Direction, ghosts, half, WithAlpha(Colors.HudAccent, GhostBeltAlpha));

            AppendChevron(CellCentre(ghost.Cell), ghost.Direction,
                look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength,
                WithAlpha(Colors.HudAccent, GhostArrowAlpha));
        }

        /// <summary>One cell's bed at a half width: the hub every lane docks into, the exit lane to the cell's
        /// edge and a lane in from every side the blueprint feeds it from.</summary>
        private void AppendBed(Int2 cell, Dir direction, TutorialGhost[] ghosts, float half, Color color)
        {
            Vector2 centre = CellCentre(cell);
            Vector2 forward = ToVector(direction);

            AppendRect(centre.x - half, centre.y - half, centre.x + half, centre.y + half, color);
            AppendLane(centre, centre + forward * 0.5f, half, color);

            for (int i = 0; i < Sides.Length; i++)
            {
                if (!Feeds(ghosts, cell, Sides[i])) continue;

                AppendLane(centre + ToVector(Sides[i]) * 0.5f, centre, half, color);
            }
        }

        /// <summary>Does anything in this blueprint push an item onto <paramref name="cell"/> across the side
        /// <paramref name="side"/>? A belt that points at the cell feeds it; a machine feeds the tiles its own
        /// fronts are on, and a cutter's two fronts sit on both of its tiles.
        ///
        /// The answer has to name both who pushes and over which edge, or the belt cell in front of a drill
        /// grows a lane in from all four sides at once.</summary>
        private static bool Feeds(TutorialGhost[] ghosts, Int2 cell, Dir side)
        {
            Int2 from = cell + side.Offset();
            Dir into = side.Opposite();     // the way the neighbour across this side would have to push

            for (int i = 0; i < ghosts.Length; i++)
            {
                TutorialGhost other = ghosts[i];

                // A piece that does not push toward this cell feeds none of its sides, however close it is.
                if (other.Direction != into) continue;

                if (other.Cell == from) return true;     // a belt, a drill, an operator, the cutter's own tile

                // A cutter's second front sits on the lane tile beside it and pushes the same way, so the
                // displaced half arrives across the lane tile's own edge - one tile over from the machine.
                if (other.Build == BuildKind.Cutter &&
                    other.Cell + MachineFootprint.LaneOf(other.Direction).Offset() == from) return true;
            }

            return false;
        }

        /// <summary>A drill's ghost: the same hollow frame the machine stands in, so the ore under it stays the
        /// identity, plus its outlet chevron at the offset the real drill wears one. Each bar carries the rim,
        /// which a placed drill's bars do not: it is how the one machine with no body says "this is the plan".</summary>
        private void AppendDrillGhost(Int2 cell, Dir direction, Color body, Color edge)
        {
            Palette.MachineLook look = Colors.Machines;

            MachineRenderer.FrameBars(new Vector2(cell.X, cell.Y), look, _frameBars);
            for (int i = 0; i < _frameBars.Length; i++)
            {
                Rect bar = _frameBars[i];
                AppendBracket(bar, body, edge);
            }

            AppendMachineChevron(cell, direction);
        }

        /// <summary>An outlined quad, because <see cref="AppendRect"/> cannot wear a rim and the drill's frame
        /// needs one.</summary>
        private void AppendBracket(Rect bar, Color body, Color edge)
        {
            _bracket[0] = new Vector2(bar.xMin, bar.yMin);
            _bracket[1] = new Vector2(bar.xMax, bar.yMin);
            _bracket[2] = new Vector2(bar.xMax, bar.yMax);
            _bracket[3] = new Vector2(bar.xMin, bar.yMax);

            AppendPolygon(_bracket, Vector2.zero, body, edge, OutlineWidth);
        }

        /// <summary>The renderer's own two-tile fork, the blade down the lane the belt feeds, and a chevron in
        /// each lane it forks into - the fork's silhouette already says which way the machine is turned.</summary>
        private void AppendCutterGhost(Int2 cell, Dir facing, Color body, Color edge)
        {
            AppendShape(CutterBodyFor(facing), CellCentre(cell), body, edge);

            Vector2 flow = ToVector(facing);
            Vector2 middle = CellCentre(cell);
            Int2 laneCell = cell + MachineFootprint.LaneOf(facing).Offset();
            Vector2 laneMiddle = CellCentre(laneCell);

            AppendLane(middle - flow * 0.5f, middle + flow * 0.5f, OutlineWidth * 0.5f, edge);

            Palette.CursorLook look = Colors.Cursor;
            AppendChevron(middle, facing, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
            AppendChevron(laneMiddle, facing, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
        }

        /// <summary>A turret's ghost: the octagon, the barrel at its rest facing - the same readout a placed gun
        /// gives while it has nothing to aim at - and the range ring. The ring is part of the answer: a gun a
        /// tile too far from the lane never fires, so the blueprint draws the coverage being bought.</summary>
        private void AppendTurretGhost(Int2 cell, BuildKind kind, Dir direction, Color body, Color edge)
        {
            Vector2 centre = CellCentre(cell);
            Palette.MachineLook look = Colors.Machines;

            AppendShape(_turretBody, centre, body, edge);

            Vector2 forward = ToVector(direction);
            Vector2 tip = centre + forward * look.BarrelLength;
            float half = look.BarrelHalfWidth;
            var right = new Vector2(-forward.y, forward.x);

            // Wound CCW from the tip's left corner, the order the renderer's barrel uses.
            _barrel[0] = tip - right * half;
            _barrel[1] = tip + right * half;
            _barrel[2] = centre + right * half;
            _barrel[3] = centre - right * half;

            AppendPolygon(_barrel, Vector2.zero, body, edge, OutlineWidth);

            if (World.Content.IsTurret(kind))
                AppendRangeRing(centre, World.Content.Turret(kind).Range, WithAlpha(Colors.RangeRing, GhostRingAlpha));
        }

        /// <summary>A pipe's ghost: the two rails and their collars across the jumped tile, plus its
        /// chevron.</summary>
        private void AppendPipeGhost(Int2 cell, Dir direction, Color body)
        {
            Palette.MachineLook look = Colors.Machines;
            Vector2 centre = CellCentre(cell);
            Vector2 forward = ToVector(direction);
            var right = new Vector2(-forward.y, forward.x);

            Vector2 entry = centre - forward * 0.5f;
            Vector2 landing = centre + forward * 1.5f;

            AppendLane(entry + right * look.PipeRailOffset, landing + right * look.PipeRailOffset,
                look.PipeRailWidth, body);
            AppendLane(entry - right * look.PipeRailOffset, landing - right * look.PipeRailOffset,
                look.PipeRailWidth, body);

            float ct = look.PipeCollarThickness;
            float cw = look.PipeCollarHalfWidth;
            AppendLane(entry - forward * ct + right * cw, entry - forward * ct - right * cw, ct, body);
            AppendLane(landing + right * cw, landing - right * cw, ct, body);

            AppendMachineChevron(cell, direction);
        }

        /// <summary>A sorter's ghost: the triangle turned so its apex points out the exit side - the machine's
        /// facing readout, which is why a placed sorter wears no chevron either.</summary>
        private void AppendSorterGhost(Int2 cell, Dir direction, Color body, Color edge)
        {
            Rotated(_sorterBody, _sorterTurned, FacingAngle(direction));

            AppendShape(_sorterTurned, CellCentre(cell), body, edge);
        }

        /// <summary>A sink's ghost: the block, the pit and a chevron on every side pointing in - everything goes
        /// in, nothing comes out.</summary>
        private void AppendSinkGhost(Int2 cell, Color body, Color edge)
        {
            Vector2 centre = CellCentre(cell);
            Palette.MachineLook look = Colors.Machines;

            AppendShape(_sinkBody, centre, body, edge);
            AppendShape(_sinkHole, centre, Ink(Colors.Outline, GhostEdgeAlpha), edge);

            for (int i = 0; i < Sides.Length; i++)
            {
                Dir side = Sides[i];
                AppendChevron(centre + ToVector(side) * look.ChevronOffset, side.Opposite(),
                    look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
            }
        }

        /// <summary>The cached silhouette at the outline width a standing machine wears, so the plan and the
        /// build share an edge weight.</summary>
        private void AppendShape(IReadOnlyList<Vector2> points, Vector2 centre, Color body, Color edge)
            => AppendPolygon(points, centre, body, edge, OutlineWidth);

        /// <summary>The cutter's cached fork for one facing, with the operator body as the fallback for a
        /// direction the cache does not hold: a per-frame draw must not throw over a diagonal nobody
        /// authored.</summary>
        private Vector2[] CutterBodyFor(Dir flow)
        {
            int index = (int)flow;
            return index >= 0 && index < _cutterBodies.Length ? _cutterBodies[index] : _operatorBody;
        }

        /// <summary>A machine's outlet chevron, at the offset the placed machine draws its own: following it must
        /// not land the belt a tile off. It wears the plan's ink, not the body's wash - it is the one mark on a
        /// ghost that has to leave the machine's tile to be read, and a wash over the vein was a smudge rather
        /// than an arrow.</summary>
        private void AppendMachineChevron(Int2 cell, Dir direction)
        {
            Palette.MachineLook look = Colors.Machines;

            AppendChevron(CellCentre(cell) + ToVector(direction) * look.ChevronOffset, direction,
                look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, Rim());
        }

        /// <summary>A thin annulus of quads, so its width stays a constant number of screen pixels at any
        /// zoom.</summary>
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

        /// <summary>Kept for a line that has a target but no blueprint: a frame is the smallest honest
        /// answer.</summary>
        private void AppendFallbackFrame(Int2 cell)
        {
            Color accent = Colors.HudAccent;
            float thickness = Mathf.Min(Colors.OutlinePixels * 2f * WorldPerPixel, 0.16f);

            Color wash = WithAlpha(accent, accent.a * 0.12f);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + 1f, wash);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + thickness, accent);
            AppendRect(cell.X, cell.Y + 1f - thickness, cell.X + 1f, cell.Y + 1f, accent);
            AppendRect(cell.X, cell.Y, cell.X + thickness, cell.Y + 1f, accent);
            AppendRect(cell.X + 1f - thickness, cell.Y, cell.X + 1f, cell.Y + 1f, accent);
        }

        /// <summary>Mixed a little toward the outline so the slate reads as a plan on pale ground, and
        /// translucent because the ghost is one.</summary>
        private Color Ink(Color color, float alpha)
        {
            color = Color.Lerp(color, Colors.Outline, GhostInkMix);
            color.a = alpha;
            return color;
        }

        /// <summary>The HUD's accent - the colour the cards and the build bar use for "this is the lesson" -
        /// which is what keeps a ghost whose body is the building's own colour from reading as a building that is
        /// already there.</summary>
        private Color Rim() => WithAlpha(Colors.HudAccent, GhostEdgeAlpha);

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Vector2 ToVector(Dir direction)
        {
            Vec2 v = direction.ToVec();
            return new Vector2(v.X, v.Y);
        }

        /// <summary>The convention <see cref="ProcMesh.RegularPolygon"/> builds point lists in.</summary>
        private static float FacingAngle(Dir direction)
        {
            Vec2 facing = direction.ToVec();
            return Mathf.Atan2(facing.Y, facing.X) * Mathf.Rad2Deg;
        }

        /// <summary>Turned through a scratch array, so ghosting a sorter allocates nothing. Rotation preserves
        /// the counter-clockwise winding the outline normal depends on.</summary>
        private static void Rotated(IReadOnlyList<Vector2> points, Vector2[] scratch, float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                scratch[i] = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
            }
        }

        /// <summary>The cardinal a vector points closest to: a chevron is drawn on the tile grid, so a sight line
        /// at 37 degrees becomes the cardinal the eye reads as "that way". A vector too short to have a direction
        /// reads as north rather than as a division by nothing.</summary>
        private static Dir DirFor(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) < 1e-4f && Mathf.Abs(direction.y) < 1e-4f) return Dir.North;

            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
                return direction.x >= 0f ? Dir.East : Dir.West;

            return direction.y >= 0f ? Dir.North : Dir.South;
        }
    }
}
