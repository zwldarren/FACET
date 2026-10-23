using System.Collections.Generic;
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
    /// <b>A ghost is the building, faded - not a highlighted tile.</b> An earlier pass drew every piece with
    /// one accent wash, which said "something goes here" for every cell alike: a line of identical squares
    /// never told a player whether this one wanted a belt or a gun. So each ghost now wears the silhouette
    /// its building will have - the drill's hollow frame, the operator's hexagon, the turret's octagon and
    /// barrel, the cutter's two-tile fork, the belt's bed, rails and arrow - and, where that colour is not
    /// itself what a finished one looks like, that colour too. The cell is recognised before the sentence is
    /// read. The two figures that are geometry rather than a lookup are the renderer's own
    /// (<see cref="MachineRenderer.CutterBodyFigure"/>, <see cref="MachineRenderer.FrameBars"/>), so a ghost
    /// cannot promise a shape the placed machine does not stand on.
    ///
    /// <b>Colour: the building's own, or the guide's accent when that would read as built work.</b> A
    /// machine ghost wears the body colour its building will have, mixed a little toward the outline and held
    /// at a wash's alpha, so its slate does not stand on pale ground as solidly as a standing machine's. A
    /// belt cannot do that: its bed is cream on paper, so a bed in its own colour - faded or darkened - is
    /// exactly what a laid belt looks like. The one transport on the map is therefore drawn in the guide's
    /// accent, pale for the bed and solid for the rails and the direction arrow, so a blue track says "this
    /// goes here" the way a cream one never can.
    ///
    /// <b>And why the rim is the accent.</b> A ghost whose body is the real building's colour is one glance
    /// away from reading as a building that is already there. Every machine ghost therefore wears the HUD's
    /// accent as its rim - the same colour the cards and the pulsing build-bar keys use for the lesson - so
    /// the shape answers "what goes here" and the rim answers "this is the plan". Nothing here casts a
    /// shadow, carries a badge or shows health, and the rim is fainter than a standing machine's outline.
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

        /// <summary>How a machine ghost is weighted: a light wash where the body will stand, the plan's ink on
        /// its edge.
        ///
        /// <b>A ghost is a drawing, so the ink carries it and the fill only says what will be covered.</b> The
        /// old 0.55 body was near enough to a standing machine's slate to be the darkest thing on the layer -
        /// darker than the accent rim that is supposed to answer "this is the plan" - and it buried what the
        /// ghost is drawn <em>on</em>: a drill's hollow frame put a grey slab over the vein it exists to point
        /// at, and a turret's body over the lane its range ring is bought to cover. So the body is now the
        /// palest wash that still fills its silhouette, the rim is the strongest line the layer draws, and the
        /// map a lesson is about stays the brightest thing on the tile. Belt ghosts have always been weighted
        /// this way - a 0.30 bed under 0.50 rails and a 0.85 arrow; this is the machine's half of the same
        /// rule.</summary>
        private const float GhostBodyAlpha = 0.38f;
        private const float GhostEdgeAlpha = 0.90f;

        /// <summary>The belt's three alphas, all in the guide's accent rather than in the belt's own cream:
        /// a bed in its real colour read as a belt that was already laid, whichever way it was faded. Blue
        /// is the guide's ink - it is what the machine rims and the lesson's key ring wear - so a blue track
        /// says "this goes here" the way a cream one never can.</summary>
        private const float GhostBeltAlpha = 0.30f;
        private const float GhostRailAlpha = 0.50f;
        private const float GhostArrowAlpha = 0.85f;

        /// <summary>The coverage a ghosted turret is bought for, fainter than the placement ghost's own ring:
        /// the ring is context, the body is the answer to "what goes here".</summary>
        private const float GhostRingAlpha = 0.30f;

        /// <summary>How far a machine ghost's body is mixed toward the outline, so its slate does not stand
        /// on pale ground as solidly as the building it promises. The belt is deliberately not mixed: a
        /// darkened cream bed is exactly what a laid belt looks like.</summary>
        private const float GhostInkMix = 0.30f;

        /// <summary>The four sides, in <see cref="Dir"/> order, for the belt beds' feed lanes.</summary>
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

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

        /// <summary>The shipped silhouettes the real renderer draws with, cached once: a ghost body is a
        /// polygon, and rebuilding one per ghost per frame would allocate in the one view that redraws every
        /// frame a lesson is up.</summary>
        private Vector2[] _operatorBody;
        private Vector2[] _turretBody;
        private Vector2[] _splitterHub;
        private Vector2[] _sorterBody;
        private Vector2[] _sorterTurned;
        private Vector2[] _wallBody;
        private Vector2[] _sinkBody;
        private Vector2[] _sinkHole;

        /// <summary>The cutter's fork, one body per facing, cached like the renderer's: its silhouette lies
        /// along the flow, so its four facings are four shapes rather than one turned.</summary>
        private Vector2[][] _cutterBodies;

        /// <summary>Scratch for the turret's barrel and the drill's four frame bars - written and read inside
        /// one ghost, so no ghost allocates.</summary>
        private readonly Vector2[] _barrel = new Vector2[4];
        private readonly Vector2[] _bracket = new Vector2[4];
        private readonly Rect[] _frameBars = new Rect[4];

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

        /// <summary>Every silhouette a ghost can wear, built the way <see cref="MachineRenderer"/> builds the
        /// bodies it draws, plus the two rounded blocks (wall, sink) that machine has no operator row for.</summary>
        protected override void OnInitialized()
        {
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

        /// <summary>Draw the current line's standard layout, ghost by ghost. Each piece is drawn from its own
        /// building rather than from a shared highlight, so the row reads as a plan of several kinds rather
        /// than as a run of identical cells.</summary>
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

        /// <summary>One ghost, in its building's own figure. A belt is drawn first because it is not a
        /// machine row; everything else asks the run's content table for its behaviour, which is what lets a
        /// second gun or a second operator be ghosted without a line here. A kind this table does not define
        /// is skipped: a partial table is legal, and a guide that threw over an unpainted row would take the
        /// whole frame with it.</summary>
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

        /// <summary>
        /// A belt's ghost: the bed, its rails and one lane per side something arrives from, exactly as the
        /// placed belt is drawn - a hub, an exit lane and a feed lane for every ghost that pushes into it -
        /// plus the direction chevron, because which way a run points is the one thing a bed cannot say alone.
        /// The feeds are read off the blueprint rather than off the world: a line is a picture of a run that
        /// does not exist yet, so its corners have to be inferred from the ghosts themselves or every cell
        /// would draw as a straight segment.
        /// </summary>
        private void AppendBeltGhost(in TutorialGhost ghost, TutorialGhost[] ghosts)
        {
            Palette.BeltLook look = Colors.Belts;
            float half = look.HalfWidth;
            float rail = Mathf.Max(0f, look.RailWidth);

            // Two passes, rails first, like the belt view: a bed laid over its own rail would leave the
            // neighbour's rail drawn across the joint where two cells meet. All three parts are the guide's
            // accent: a bed in the belt's own cream, faded or not, reads as a belt that was already laid on
            // pale ground, and the plan has to be read as a plan at a glance.
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
        /// <b>The lane is owed to one side, so the piece that answers has to be the neighbour across it.</b>
        /// This is asked once per side - whatever answers "yes" draws a lane on that side - so the answer must
        /// name both who pushes and over which edge, and a piece that is not the neighbour of
        /// <paramref name="side"/> has nothing to say about it at all. Asked of a machine as "is this tile your
        /// front?", the front tile answers yes whichever side is named: the belt cell in front of a drill grew
        /// a lane in from all four sides at once, and the join between a lesson's belt and its machine read as a
        /// cross standing on the track rather than as a run docking into the machine.</summary>
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
        /// identity, plus its outlet chevron at the same offset the real drill wears one. Each bar carries the
        /// rim, which a placed drill's bars do not: it is how the one machine with no body still says "this is
        /// the plan, not the drill".</summary>
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

        /// <summary>One axis-aligned bar as an outlined quad - <see cref="AppendRect"/> cannot wear a rim, and
        /// the drill's frame is the one bodyless machine that needs one.</summary>
        private void AppendBracket(Rect bar, Color body, Color edge)
        {
            _bracket[0] = new Vector2(bar.xMin, bar.yMin);
            _bracket[1] = new Vector2(bar.xMax, bar.yMin);
            _bracket[2] = new Vector2(bar.xMax, bar.yMax);
            _bracket[3] = new Vector2(bar.xMin, bar.yMax);

            AppendPolygon(_bracket, Vector2.zero, body, edge, OutlineWidth);
        }

        /// <summary>The cutter's ghost: the renderer's own two-tile fork, the blade down the lane the belt
        /// feeds, and a chevron in each lane it forks into. The fork already lies along the flow, so its
        /// silhouette says which way the machine is turned - the chevrons are the guide's addition, and say
        /// "both halves leave this way" where the body alone leaves the reader to work it out.</summary>
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

        /// <summary>A turret's ghost: the octagon, the barrel at its rest facing - the same readout a placed
        /// gun gives while it has nothing to aim at - and the range ring. The ring is part of the answer: a gun
        /// a tile too far from the lane never fires, so the blueprint draws the coverage being bought.</summary>
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

        /// <summary>A pipe's ghost: the two rails and their collars across the jumped tile, the same 'over' the
        /// machine reads as, plus its chevron.</summary>
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

        /// <summary>A sink's ghost: the block, the pit and a chevron on every side pointing in - everything
        /// goes in, nothing comes out, and the machine has no facing for an outlet arrow to describe.</summary>
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

        /// <summary>One ghost body: the cached silhouette, filled and outlined at the outline width a standing
        /// machine wears, so the plan and the build share an edge weight.</summary>
        private void AppendShape(IReadOnlyList<Vector2> points, Vector2 centre, Color body, Color edge)
            => AppendPolygon(points, centre, body, edge, OutlineWidth);

        /// <summary>The cutter's cached fork for one facing, with the operator body as the fallback for a
        /// direction the cache does not hold - the same guard the renderer makes, and for the same reason: a
        /// per-frame draw must not throw over a diagonal nobody authored.</summary>
        private Vector2[] CutterBodyFor(Dir flow)
        {
            int index = (int)flow;
            return index >= 0 && index < _cutterBodies.Length ? _cutterBodies[index] : _operatorBody;
        }

        /// <summary>A machine's outlet chevron, at the offset the placed machine draws its own: the guide must
        /// point where the real arrow points, or following it lands the belt a tile off.
        ///
        /// It wears the plan's ink, not the body's wash, and it is the one mark on a ghost that has to leave the
        /// machine's own tile to be read: a drill's arrow is drawn over the vein it stands on, where a body
        /// colour at a wash's alpha was a smudge inside the frame rather than an arrow out of it. The placed
        /// machine draws the same arrow in its own body colour; the plan draws the same arrow in the colour
        /// that means "plan" everywhere else on this layer.</summary>
        private void AppendMachineChevron(Int2 cell, Dir direction)
        {
            Palette.MachineLook look = Colors.Machines;

            AppendChevron(CellCentre(cell) + ToVector(direction) * look.ChevronOffset, direction,
                look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, Rim());
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
            float thickness = Mathf.Min(Colors.OutlinePixels * 2f * WorldPerPixel, 0.16f);

            Color wash = WithAlpha(accent, accent.a * 0.12f);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + 1f, wash);
            AppendRect(cell.X, cell.Y, cell.X + 1f, cell.Y + thickness, accent);
            AppendRect(cell.X, cell.Y + 1f - thickness, cell.X + 1f, cell.Y + 1f, accent);
            AppendRect(cell.X, cell.Y, cell.X + thickness, cell.Y + 1f, accent);
            AppendRect(cell.X + 1f - thickness, cell.Y, cell.X + 1f, cell.Y + 1f, accent);
        }

        /// <summary>A machine body colour as the ghost wears it: mixed a little toward the outline so the
        /// slate reads as a plan on pale ground, and translucent because the ghost is one. The belt does not
        /// come through here - see <see cref="AppendBeltGhost"/>.</summary>
        private Color Ink(Color color, float alpha)
        {
            color = Color.Lerp(color, Colors.Outline, GhostInkMix);
            color.a = alpha;
            return color;
        }

        /// <summary>The rim every machine ghost wears: the HUD's accent, the colour the cards and the build
        /// bar use for "this is the lesson". It is what keeps a ghost whose body is the real building's own
        /// colour from reading as a building that is already there.</summary>
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

        /// <summary>The angle a machine faces, in degrees, with an east-facing default's vertex at zero -
        /// the convention <see cref="ProcMesh.RegularPolygon"/> builds point lists in.</summary>
        private static float FacingAngle(Dir direction)
        {
            Vec2 facing = direction.ToVec();
            return Mathf.Atan2(facing.Y, facing.X) * Mathf.Rad2Deg;
        }

        /// <summary>One cached polygon turned about the origin through a scratch array, so ghosting a sorter
        /// allocates nothing. Rotation preserves the counter-clockwise winding the outline normal depends
        /// on.</summary>
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
