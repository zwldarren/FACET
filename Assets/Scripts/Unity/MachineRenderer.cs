using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every machine as one mesh. A turret's Aim and Armed state, an operator's held piece, a pipe's
    /// item in transit and a splitter's ports all change while the player watches, so this view rebuilds
    /// every frame; outline widths therefore stay a true 2 px at any zoom.
    ///
    /// A building's <em>body</em> may be overridden from the Palette's <see cref="Palette.Visuals"/> - a
    /// Sprite replacing the flat body, or a procedural override picking a different silhouette/colour.
    /// Functional overlays are never overridden (a turret keeps its barrel and round, an operator and a
    /// splitter their port stubs and chamber badge), so a re-skinned building still reads its state.
    ///
    /// Drawing dispatches on the machine's <see cref="BehaviorKind"/>, not its tile kind, and asks for the
    /// override of the <em>build kind</em> it was handed - which is what lets a second gun or a second
    /// operator exist without a line here.
    ///
    /// Each behaviour's readout: drill - a hollow frame so the patch mark underneath is the identity;
    /// operator (cutter, rotator, stacker) - the chambered body, a stub per wired port and the piece in the
    /// chamber as its badge; pipe - the tube across the jumped tile and the item riding it; splitter - the
    /// hub with a stub per live port read off the belts; sorter - a triangle whose apex points out the exit
    /// side, with its badge at the centre; turret - the barrel and the round in its chamber, so an empty
    /// badge is the "starved" state; wall - a plain block whose damage tint is its state; sink - the same
    /// block with a pit in it and a lane in from every side, which is the one building whose arrows all
    /// point <em>in</em>.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class MachineRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly List<MachineSnapshot> _machines = new List<MachineSnapshot>();

        /// <summary>Scratch barrel points, reused so a per-machine-per-frame array is not allocated.</summary>
        private readonly Vector2[] _barrel = new Vector2[4];

        /// <summary>The shipped silhouettes, cached; a procedural override supplies its own points.</summary>
        private Vector2[] _operatorBody;
        private Vector2[] _turretBody;
        private Vector2[] _splitterHub;

        /// <summary>The sorter's triangle at its default facing, plus the scratch it is rotated into
        /// each frame, so the facing allocates nothing.</summary>
        private Vector2[] _sorterBody;
        private Vector2[] _sorterRotated;

        /// <summary>The wall's inset block, cached like the other silhouettes.</summary>
        private Vector2[] _wallBody;

        /// <summary>The sink's block and the pit in its middle, cached like the other silhouettes.</summary>
        private Vector2[] _sinkBody;
        private Vector2[] _sinkHole;

        private ShapeIconSet _carriedIcons;
        private ShapeIconSet _roundIcons;
        private ShapeIconSet _pipeIcons;
        private ShapeIconSet _sorterIcons;

        protected override Palette.Layer Layer => Colors.MachineLayer;

        /// <summary>The player's authored look for one building, or null when none is authored - the
        /// normal case, since the override block is purely additive. Callers read through
        /// <see cref="BodyOf"/> / <see cref="FillOf"/> / <see cref="OutlineOf"/> or guard the reference.</summary>
        private VisualStyle Over(BuildKind kind) => Colors.Visuals.Machines.For(kind);

        /// <summary>The points to draw a body with: the authored shape when the override ticks one,
        /// otherwise the shipped silhouette.</summary>
        private Vector2[] BodyOf(VisualStyle style, Vector2[] shipped)
            => Overridden(style) ? VisualShapes.Points(style, Colors.CircleSides) : shipped;

        /// <summary>A body's fill: the authored colour when overridden, otherwise what the machine's
        /// behaviour calls for.</summary>
        private Color FillOf(VisualStyle style, BehaviorKind behavior)
            => Overridden(style) ? style.Fill : Colors.BodyColor(behavior);

        /// <summary>A body's fill after damage: reddened toward <see cref="CoreView"/>'s hurt colour as
        /// health drains. An indestructible building (MaxHp 0) is never tinted; sprite bodies pass
        /// through too, with white as the healthy end.</summary>
        private Color Damaged(Color body, in MachineSnapshot machine)
            => machine.MaxHp <= 0f ? body : Color.Lerp(Colors.CoreDamage, body, machine.HealthFraction);

        private Color OutlineOf(VisualStyle style)
            => Overridden(style) ? style.Outline : Colors.Outline;

        /// <summary>The outline width a body draws with: the authored width when the style overrides
        /// the body procedurally, otherwise the run's default.</summary>
        private float BodyOutlineWidth(VisualStyle style, float outlineWidth)
            => Overridden(style) ? OutlineWidthFor(style) : outlineWidth;

        /// <summary>Whether the palette authored a replacement for this content's body.</summary>
        private static bool Overridden(VisualStyle style) => style != null && style.Override;

        /// <summary>Whether the replacement is a sprite rather than a procedural silhouette; a sprite
        /// replaces the body only, keeping the functional overlays.</summary>
        private static bool IsSprite(VisualStyle style)
            => Overridden(style) && style.Source == VisualSource.Sprite;

        protected override void OnInitialized()
        {
            Palette.MachineLook look = Colors.Machines;

            // First vertex at 30 degrees puts a flat edge up, matching the ground ore marks; a
            // procedural override supplies its own. The three operators share this chambered body - what
            // tells a cutter from a rotator on screen is its badge and its role colour, exactly as a
            // second gun is the first one's octagon in a darker body colour.
            _operatorBody = ProcMesh.RegularPolygon(6, look.OperatorRadius, 30f);
            _turretBody = ProcMesh.RegularPolygon(8, look.TurretRadius);
            _splitterHub = ProcMesh.RegularPolygon(4, look.SplitterHubHalfSize * 1.4143f, 45f);

            // The sorter's triangle: first vertex at angle 0 points east, rotated to the machine's direction.
            _sorterBody = ProcMesh.RegularPolygon(3, look.SorterRadius);

            // The wall's block: a square inset from the cell edge, so neighbours still show the grid line.
            _wallBody = ProcMesh.RegularPolygon(4, (0.5f - look.WallInset) * 1.4143f, 45f);

            // The sink: the same kind of block with a pit cut in it. Both are a square, because both are a
            // building with no machinery on top - what tells them apart is the hole, not the outline.
            _sinkBody = ProcMesh.RegularPolygon(4, (0.5f - look.SinkInset) * 1.4143f, 45f);
            _sinkHole = ProcMesh.RegularPolygon(4, look.SinkHoleHalfSize * 1.4143f, 45f);

            // The badges follow the item overrides, so a re-skinned mineral reads the same everywhere.
            // (Decomposer* are the chamber's two radii; the field names keep their old spelling because
            // renaming a serialized field would silently drop its value out of Assets/Data/Palette.asset.)
            _carriedIcons = Colors.ShapeIcons(look.OperatorIconRadius, Colors.OutlinePixels);
            _roundIcons = Colors.ShapeIcons(look.AmmoIconRadius, Colors.OutlinePixels);
            _pipeIcons = Colors.ShapeIcons(look.PipeItemRadius, Colors.OutlinePixels);
            _sorterIcons = Colors.ShapeIcons(look.SorterIconRadius, Colors.OutlinePixels);
        }

        /// <summary>Everything this view draws moves or changes state every tick, so it rebuilds
        /// every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Machines.GetMachines(_machines);
            float outlineWidth = OutlineWidth;
            BeginSprites();

            for (int i = 0; i < _machines.Count; i++)
            {
                switch (_machines[i].Behavior)
                {
                    case BehaviorKind.Drill: AppendDrill(_machines[i]); break;
                    case BehaviorKind.Cutter: AppendCutter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Rotator:
                    case BehaviorKind.Stacker: AppendOperator(_machines[i], outlineWidth); break;
                    case BehaviorKind.Pipe: AppendPipe(_machines[i], outlineWidth); break;
                    case BehaviorKind.Splitter: AppendSplitter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Sorter: AppendSorter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Turret: AppendTurret(_machines[i], outlineWidth); break;
                    case BehaviorKind.Sink: AppendSink(_machines[i], outlineWidth); break;
                    case BehaviorKind.None: AppendWall(_machines[i], outlineWidth); break;
                    default:
                        // An undrawn behaviour would be invisible on screen; say so instead. (MachineSystem's
                        // start-up check normally catches this first; this is the view's half of the rule.)
                        throw new System.ArgumentOutOfRangeException(
                            nameof(_machines), _machines[i].Behavior,
                            "a new behaviour must get a drawing case in MachineRenderer");
                }
            }

            EndSprites();
        }

        /// <summary>A hollow frame plus an outlet chevron. No outline: the ore mark underneath IS the
        /// drill's identity, so the frame only frames it.</summary>
        private void AppendDrill(in MachineSnapshot machine)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
                AppendChevron(machine.Cell, machine.Direction, FillOf(style, machine.Behavior));
                return;
            }

            float x = machine.Cell.X;
            float y = machine.Cell.Y;
            Color body = Damaged(FillOf(style, machine.Behavior), machine);

            Palette.MachineLook look = Colors.Machines;
            float lo = look.FrameInset;
            float hiInner = 1f - look.FrameInset - look.FrameThickness;
            float hi = 1f - look.FrameInset;
            float thickness = look.FrameThickness;

            AppendRect(x + lo, y + lo, x + hi, y + lo + thickness, body);                    // bottom
            AppendRect(x + lo, y + hiInner, x + hi, y + hi, body);                            // top
            AppendRect(x + lo, y + lo + thickness, x + lo + thickness, y + hiInner, body);    // left
            AppendRect(x + hi - thickness, y + lo + thickness, x + hi, y + hiInner, body);    // right

            AppendChevron(machine.Cell, machine.Direction, body);
        }

        /// <summary>Hexagon body with the piece in the chamber as its badge (nothing when the chamber is
        /// empty), plus a stub on every wired side; no facing chevron, since the outlets are read off the
        /// belts as the tick does.
        ///
        /// The rotator and the stacker share this path because they are one archetype: a machine on one tile
        /// with a chamber and the belts around it. The cutter is the third operator and does not - it stands
        /// on two tiles and draws the fork, see <see cref="AppendCutter"/>. The badge is the snapshot's, read
        /// by <see cref="MachineField"/> through <see cref="MachineReading.Badge"/>, so a new operator needs
        /// no line here.</summary>
        private void AppendOperator(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            // 0.8 r is inside the hexagon's flat edge (apothem 0.866 r), so the body covers each stub's inner end.
            AppendPortStubs(machine, centre, Colors.Machines.OperatorRadius * 0.8f);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendPolygon(BodyOf(style, _operatorBody), centre,
                    Damaged(FillOf(style, machine.Behavior), machine),
                    OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            AppendIcon(_carriedIcons, machine.Shape, centre, null);
        }

        /// <summary>
        /// The cutter: the one machine drawn over two tiles, because the second tile is the whole point of it.
        /// One body across the pair, the blade down the lane the belt feeds, a docking stub in each of the two
        /// lanes it forks into, and each lane's pending piece worn as its own badge - so a cutter with a
        /// blocked outlet shows <em>which</em> half is waiting where.
        ///
        /// The blade lies <em>along</em> the flow, in the lane the belt runs in, rather than across the cell: the
        /// item is cut on its own centre line as it arrives, one half carries on down that lane and the other
        /// leaves a lane over. That is why the second tile is there, and why the drawn knife sits in the lane
        /// rather than between them.
        /// </summary>
        private void AppendCutter(in MachineSnapshot machine, float outlineWidth)
        {
            Palette.MachineLook look = Colors.Machines;
            VisualStyle style = Over(machine.Build);

            Dir flow = machine.Direction;
            Dir lane = MachineFootprint.LaneOf(flow);
            Int2 laneCell = machine.Cell + lane.Offset();

            Vec2 flow2 = flow.ToVec();
            var forward = new Vector2(flow2.X, flow2.Y);
            var centre = CellCentre(machine.Cell);
            var laneCentre = CellCentre(laneCell);
            Color body = Damaged(FillOf(style, machine.Behavior), machine);

            // Each tile's own ports: the snapshot's mask is the machine's, which is what the card wants, but a
            // stub belongs on the tile whose side the belt docks with - and the two lanes are two tiles.
            World.Machines.PortMasksAt(machine.Cell, out DirMask inHere, out DirMask outHere);
            World.Machines.PortMasksAt(laneCell, out DirMask inThere, out DirMask outThere);
            AppendPortStubs(inHere, outHere, centre, look.OperatorRadius * 0.8f);
            AppendPortStubs(inThere, outThere, laneCentre, look.OperatorRadius * 0.8f);

            if (IsSprite(style))
            {
                // An authored body replaces the machine's mark; where it stands is still the pair of tiles the
                // blade and the badges are drawn against.
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                // An authored procedural body is one silhouette, so it stands on the tile the player placed and
                // the second lane keeps its blade, stub and badge; the shipped look is the fork itself.
                Vector2[] points = Overridden(style)
                    ? BodyOf(style, _operatorBody)
                    : CutterBodyFigure(flow, look.OperatorRadius);

                AppendPolygon(points, centre, body, OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            // The blade: the item's own centre line, along the flow, where the cut is made.
            AppendLane(centre - forward * 0.5f, centre + forward * 0.5f, outlineWidth * 0.5f, Colors.Outline);

            AppendIcon(_carriedIcons, machine.Shape, centre, null);
            AppendIcon(_carriedIcons, machine.Shape2, laneCentre, null);
        }

        /// <summary>
        /// The cutter's body, in the tile's own frame with the machine's cell at the origin: one box spanning
        /// the two tiles it stands on, as wide across the flow as every other operator's body and twice as long
        /// along the lane, so the silhouette itself says "this machine is two tiles wide".
        /// </summary>
        public static Vector2[] CutterBodyFigure(Dir facing, float half)
        {
            Vec2 facing2 = facing.ToVec();
            var forward = new Vector2(facing2.X, facing2.Y);

            // The lane lies to the machine's right, which is the flow turned a quarter clockwise - the same
            // handedness MachineFootprint states, read here in a right-handed frame so the winding stays CCW.
            var right = new Vector2(-forward.y, forward.x);

            Vector2 Corner(float along, float across) => forward * along + right * across;

            return new[]
            {
                Corner(-half, -1f - half),
                Corner(half, -1f - half),
                Corner(half, half),
                Corner(-half, half),
            };
        }

        /// <summary>Two rails from this cell's entry edge across the jumped tile to the landing belt,
        /// with a collar at each end - the collars and the gap under the tube read as "over". The item
        /// rides the tube at its work fraction.</summary>
        private void AppendPipe(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            Vec2 forward2 = machine.Direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var right = new Vector2(-forward.y, forward.x);
            VisualStyle style = Over(machine.Build);
            Color body = Damaged(FillOf(style, machine.Behavior), machine);

            Palette.MachineLook look = Colors.Machines;
            Vector2 entry = centre - forward * 0.5f;
            Vector2 landing = centre + forward * 1.5f;

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendLane(entry + right * look.PipeRailOffset, landing + right * look.PipeRailOffset,
                    look.PipeRailWidth, body);
                AppendLane(entry - right * look.PipeRailOffset, landing - right * look.PipeRailOffset,
                    look.PipeRailWidth, body);

                float ct = look.PipeCollarThickness;
                float cw = look.PipeCollarHalfWidth;
                AppendLane(entry - forward * ct + right * cw, entry - forward * ct - right * cw, ct, body);
                AppendLane(landing + right * cw, landing - right * cw, ct, body);
            }

            AppendChevron(machine.Cell, machine.Direction, body);

            AppendIcon(_pipeIcons, machine.Shape, entry + forward * (2f * machine.Work), null);
        }

        /// <summary>The hub plus one stub per live port, read off the belts as the simulation does; the
        /// buffered item sits at the centre.</summary>
        private void AppendSplitter(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            AppendPortStubs(machine, centre, Colors.Machines.SplitterHubHalfSize);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendPolygon(BodyOf(style, _splitterHub), centre,
                    Damaged(FillOf(style, machine.Behavior), machine),
                    OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            AppendIcon(_carriedIcons, machine.Shape, centre, null);
        }

        /// <summary>The router: a triangle whose apex points out the side the filtered item leaves by,
        /// with that routing badge at the centre and a stub per live port - both things a sorter can be
        /// wrong about (side and filter) on screen at once.
        ///
        /// Core's badge names the exact item a sorter routes for; a <em>contains a mineral</em> rule has no
        /// single item to name, so the badge is that mineral's whole figure - the same swatch the HUD's
        /// picker paints, so the machine on the map and the card beside it say the same thing rather than one
        /// of them saying nothing.
        ///
        /// The apex is the direction readout, so a sorter needs no chevron. Stubs are drawn first and the
        /// body covers their inner ends, as with the operators and the splitter.</summary>
        private void AppendSorter(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            // 0.75 r reaches inside the triangle's incircle (r/2), so the body covers each stub's inner end.
            AppendPortStubs(machine, centre, Colors.Machines.SorterRadius * 0.75f);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                Vector2[] body = BodyOf(style, _sorterBody);
                AppendRotatedPolygon(body, ScratchFor(ref _sorterRotated, body.Length), centre,
                    FacingAngle(machine.Direction), Damaged(FillOf(style, machine.Behavior), machine),
                    OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            // The badge is Core's own reading of the machine's filter - a mineral rule drawn as the full
            // mineral it asks for - so the view draws what it is handed and resolves nothing itself.
            AppendIcon(_sorterIcons, machine.Shape, centre, null);
        }

        /// <summary>A cached polygon rotated about the origin by an angle in degrees, through a scratch
        /// array, so a per-frame view allocates nothing to turn a shape. Rotation preserves the
        /// counter-clockwise winding <see cref="ProcMesh.AppendPolygon"/>'s outline normals depend on.</summary>
        private void AppendRotatedPolygon(Vector2[] points, Vector2[] scratch, Vector2 centre,
            float angleDegrees, Color fill, Color outline, float width)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);

            for (int i = 0; i < points.Length; i++)
            {
                Vector2 p = points[i];
                scratch[i] = new Vector2(p.x * cos - p.y * sin, p.x * sin + p.y * cos);
            }

            AppendPolygon(scratch, centre, fill, outline, width);
        }

        /// <summary>The rotation scratch, grown to fit whatever body it is handed - an override's
        /// silhouette may have more corners than the shipped triangle.</summary>
        private static Vector2[] ScratchFor(ref Vector2[] scratch, int length)
        {
            if (scratch == null || scratch.Length < length) scratch = new Vector2[length];
            return scratch;
        }

        /// <summary>The angle a machine faces, in degrees, with an east-facing default's vertex at
        /// zero - which is the convention <see cref="ProcMesh.RegularPolygon"/> builds point lists in.</summary>
        private static float FacingAngle(Dir direction)
        {
            Vec2 facing = direction.ToVec();
            return Mathf.Atan2(facing.Y, facing.X) * Mathf.Rad2Deg;
        }

        /// <summary>One stub per live port on a cell, drawn straight from the machine's computed
        /// <see cref="MachineSnapshot.InMask"/> / <see cref="MachineSnapshot.OutMask"/>: an input side
        /// gets a stub with an inward chevron, an output side one with an outward chevron, a side with
        /// no belt (or a belt running past) gets nothing. Shared by the splitter, the operators and
        /// the sorter - the machines whose ports *are* the belts around them - and read from the same
        /// mask the tick pushes through, so the picture cannot drift from the rule. All of them draw
        /// their body after this, which is what hides each stub's inner end.
        ///
        /// SplitterStubHalfWidth is the palette's one stub width; it keeps its name because it is the
        /// same stub on every machine, and renaming a serialized field would silently drop the value
        /// out of Assets/Data/Palette.asset.</summary>
        private void AppendPortStubs(in MachineSnapshot machine, Vector2 centre, float innerRadius)
            => AppendPortStubs(machine.InMask, machine.OutMask, centre, innerRadius);

        /// <summary>The same stubs from one tile's masks, which is what a machine that stands on two tiles has
        /// to ask for: each of its lanes draws its own ports.</summary>
        private void AppendPortStubs(DirMask inMask, DirMask outMask, Vector2 centre, float innerRadius)
        {
            Palette.MachineLook look = Colors.Machines;

            for (int i = 0; i < Sides.Length; i++)
            {
                Dir side = Sides[i];
                bool inbound = inMask.Has(side);
                bool outbound = outMask.Has(side);
                if (!inbound && !outbound) continue;

                Vec2 side2 = side.ToVec();
                var away = new Vector2(side2.X, side2.Y);
                var stubCentre = centre + away * 0.5f * (1f - innerRadius);

                if (inbound)
                {
                    AppendLane(centre + away * 0.5f, centre + away * innerRadius,
                        look.SplitterStubHalfWidth, Colors.Belt);
                    AppendChevron(stubCentre, side.Opposite(), look.ChevronBack,
                        look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
                }
                else
                {
                    AppendLane(centre + away * innerRadius, centre + away * 0.5f,
                        look.SplitterStubHalfWidth, Colors.Belt);
                    AppendChevron(stubCentre, side, look.ChevronBack,
                        look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
                }
            }
        }

        /// <summary>Octagon body, barrel along Aim, then the round in its chamber. Draw order matters: barrel
        /// after body, badge last so it is never covered - and the badge <em>is</em> the chamber, so a gun with
        /// a round in hand shows the shape and a gun with nothing draws nothing at all.
        ///
        /// The one gun shares this path with any second one: the badge is the snapshot's, read by
        /// <see cref="MachineField"/> through <see cref="MachineReading.Badge"/>, because what the turret
        /// fires is whatever the factory fed it rather than a property of the gun.</summary>
        private void AppendTurret(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);
            Color body = Damaged(FillOf(style, machine.Behavior), machine);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendPolygon(BodyOf(style, _turretBody), centre, body, OutlineOf(style),
                    BodyOutlineWidth(style, outlineWidth));
            }

            AppendBarrel(machine, centre, outlineWidth, body);

            // The chamber's round: what the line has loaded the gun with, drawn until the shot leaves. A gun
            // with nothing loaded draws no badge at all, which is the same news as the HUD's "starved" line
            // said in a picture - there is no last-round-to-grey-out any more, because a loaded chamber is
            // what the badge now means.
            AppendIcon(_roundIcons, machine.Shape, centre, null);
        }

        /// <summary>One quad from the body centre out along the aim, normalized by the tick; a zero Aim
        /// (never saw an enemy) falls back to the rest direction. Takes the body's colour, so a
        /// re-skinned gun is one colour.</summary>
        private void AppendBarrel(in MachineSnapshot machine, Vector2 centre, float outlineWidth, Color body)
        {
            Vec2 aim = machine.Aim;
            Vector2 dir = new Vector2(aim.X, aim.Y);
            if (dir.sqrMagnitude < 1e-8f)
            {
                Vec2 rest = machine.Direction.ToVec();
                dir = new Vector2(rest.X, rest.Y);
            }
            var right = new Vector2(-dir.y, dir.x);

            Vector2 tip = centre + dir * Colors.Machines.BarrelLength;
            float halfWidth = Colors.Machines.BarrelHalfWidth;
            var nearRight = centre + right * halfWidth;
            var nearLeft = centre - right * halfWidth;
            var tipRight = tip + right * halfWidth;
            var tipLeft = tip - right * halfWidth;

            // Wound CCW: starting at the tip's left corner keeps every edge cross positive for
            // any aim angle, the same trick BeltRenderer's chevron uses.
            _barrel[0] = tipLeft;
            _barrel[1] = tipRight;
            _barrel[2] = nearRight;
            _barrel[3] = nearLeft;

            AppendPolygon(_barrel, Vector2.zero, body, Colors.Outline, outlineWidth);
        }

        /// <summary>A plain inset block in the machine-body colour, reddening as it is chewed; the
        /// silhouette is the whole readout.
        ///
        /// Only walls reach here: belts are also <see cref="BehaviorKind.None"/> but are not machines, so
        /// never appear in <see cref="MachineField.GetMachines"/>.</summary>
        private void AppendWall(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
                return;
            }

            AppendPolygon(BodyOf(style, _wallBody), centre,
                Damaged(FillOf(style, machine.Behavior), machine),
                OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
        }

        /// <summary>
        /// The drain: an inset block with a pit in the middle and a lane in from every side. The lanes are
        /// the hubs' own stub-and-chevron, wired on all four sides whether or not a belt is there - a sink
        /// eats from every side at once and has no outlet, so a port that lit up per belt would say the
        /// machine had a state it does not have. They are also what points the arrowheads inward, which is
        /// the one thing the silhouette cannot say by itself.
        ///
        /// The pit is drawn dark rather than as a hole the ground shows through: at the far zoom a small
        /// square of ground colour reads as a gap in the floor, while a black square reads as "nothing comes
        /// out of here" at any size. Its pale lip is the same trick the outlines elsewhere play, and it is
        /// what keeps the pit from merging into the block when both are small.
        /// </summary>
        private void AppendSink(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);
            Palette.MachineLook look = Colors.Machines;

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendPolygon(BodyOf(style, _sinkBody), centre,
                    Damaged(FillOf(style, machine.Behavior), machine),
                    OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            AppendPortStubs(DirMask.All, DirMask.None, centre, look.SinkHoleHalfSize);
            AppendPolygon(_sinkHole, centre, Colors.Outline, Colors.Platform, outlineWidth * 0.5f);
        }

        private void AppendChevron(Int2 cell, Dir direction, Color color)        {
            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var centre = CellCentre(cell) + forward * Colors.Machines.ChevronOffset;

            Palette.MachineLook look = Colors.Machines;
            AppendChevron(centre, direction, look.ChevronBack, look.ChevronHalfWidth,
                look.ChevronLength, color);
        }
    }
}
