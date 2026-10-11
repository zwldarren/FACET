using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every machine as one mesh. A turret's Aim and Armed state, a converter's split, a
    /// pipe's item in transit and a splitter's ports all change while the player is watching them,
    /// so this view rebuilds every frame rather than on a watched value. Because the mesh is
    /// rebuilt every frame, outline widths stay a true 2 px at any zoom.
    ///
    /// Each building's <em>body</em> may be overridden from the Palette's
    /// <see cref="Palette.Visuals"/> - a Sprite replaces the flat body polygon, or a procedural
    /// override picks a different silhouette/colour. The functional overlays are deliberately never
    /// overridden: a turret keeps its barrel and ammo icon, a converter and splitter keep their port
    /// stubs and held-item icon, so however a building is re-skinned its state is still readable.
    ///
    /// The drawing is dispatched on the machine's <see cref="BehaviorKind"/>, not on its tile kind, and
    /// each routine asks for the override of the <em>build kind</em> it was handed. That split is what
    /// lets a second gun and a second converter exist without a line here: the mortar draws down the
    /// turret path with the mortar's own skin, and the cutter down the converter path with the
    /// cutter's, and neither is named anywhere in this file.
    ///
    /// The readout each behaviour owes the player:
    ///   drill       - a hollow frame, so the patch icon underneath stays the identity
    ///   converter   - the shape it holds: a circle or a square mid-split, or the parts waiting to
    ///                 leave, plus a stub on every side a belt feeds or drains
    ///   pipe        - the tube across the jumped tile, and the item riding over it
    ///   splitter    - the hub, with a stub per live port read off the belts around it
    ///   sorter      - a triangle whose apex points out the side the filtered shape leaves by, with
    ///                 that shape as a badge: the silhouette says "router", the apex says "this way"
    ///   turret      - the barrel, and the ammo icon whose grey-out is the "silenced" state
    ///   wall        - a plain block; damage tint is its whole state
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class MachineRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly List<MachineSnapshot> _machines = new List<MachineSnapshot>();

        /// <summary>Scratch array: AppendPolygon wants a point list per shape, and newing one up
        /// per machine per frame is exactly the allocation the view layer must not do.</summary>
        private readonly Vector2[] _barrel = new Vector2[4];

        /// <summary>The shipped silhouettes, cached at their default geometry. A build whose override
        /// ticks a procedural shape supplies its own points instead, which is why these are only the
        /// fallback rather than the only option.</summary>
        private Vector2[] _converterBody;
        private Vector2[] _turretBody;
        private Vector2[] _splitterHub;

        /// <summary>The sorter's triangle at its default facing, plus the array it is rotated into
        /// every frame - so the facing costs six multiplies and no allocation.</summary>
        private Vector2[] _sorterBody;
        private Vector2[] _sorterRotated;

        /// <summary>The wall's inset block: an axis-aligned square, cached like the other silhouettes
        /// so the wall costs no allocation either.</summary>
        private Vector2[] _wallBody;

        private ShapeIconSet _carriedIcons;
        private ShapeIconSet _ammoIcons;
        private ShapeIconSet _pipeIcons;
        private ShapeIconSet _sorterIcons;

        protected override Palette.Layer Layer => Colors.MachineLayer;

        /// <summary>The player's authored look for one building, or null when the palette authors
        /// none for it - which is the normal case, since the override block is purely additive. Every
        /// caller therefore reads through <see cref="BodyOf"/> / <see cref="FillOf"/> /
        /// <see cref="OutlineOf"/> or guards the reference itself; a building the palette has not
        /// heard of draws the shipped silhouette its behaviour already chose.</summary>
        private VisualStyle Over(BuildKind kind) => Colors.Visuals.Machines.For(kind);

        /// <summary>The points to draw a body with: the authored shape when the override ticks one,
        /// otherwise the shipped silhouette.</summary>
        private Vector2[] BodyOf(VisualStyle style, Vector2[] shipped)
            => Overridden(style) ? VisualShapes.Points(style, Colors.CircleSides) : shipped;

        /// <summary>A body's fill: the authored colour when overridden, otherwise what the machine's
        /// behaviour calls for - so a second gun is dark like a gun without being listed here.</summary>
        private Color FillOf(VisualStyle style, BehaviorKind behavior)
            => Overridden(style) ? style.Fill : Colors.BodyColor(behavior);

        /// <summary>
        /// A body's fill after damage: reddened toward the Core's hurt colour as health drains, the
        /// same language <see cref="CoreView"/> uses, so a chewed building reads at a glance. An
        /// indestructible building (MaxHp 0) is never damaged and is never tinted. Sprite-overridden
        /// bodies go through this too, with white as the healthy end, exactly as the Core's sprite does.
        /// </summary>
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

        /// <summary>Whether that replacement is a sprite rather than a procedural silhouette. Only the
        /// body path changes; a sprite keeps its functional overlays, so a turret still shows its barrel
        /// and ammo icon.</summary>
        private static bool IsSprite(VisualStyle style)
            => Overridden(style) && style.Source == VisualSource.Sprite;

        protected override void OnInitialized()
        {
            Palette.MachineLook look = Colors.Machines;

            // The decomposer's first vertex sits at 30 degrees so a flat edge faces up, matching
            // the shape patches the shape patch view lays in the ground; a procedural override
            // supplies its own silhouette instead. The cutter shares both, because it is the same
            // machine on a different recipe.
            _converterBody = ProcMesh.RegularPolygon(6, look.DecomposerRadius, 30f);
            _turretBody = ProcMesh.RegularPolygon(8, look.TurretRadius);
            _splitterHub = ProcMesh.RegularPolygon(4, look.SplitterHubHalfSize * 1.4143f, 45f);

            // The sorter's triangle: 3 sides with the first vertex at angle 0 points east, which the
            // draw rotates to the machine's direction.
            _sorterBody = ProcMesh.RegularPolygon(3, look.SorterRadius);

            // The wall's block: a square (4 sides turned 45 degrees) inset from the cell edge, so two
            // neighbouring walls still show the grid line between them.
            _wallBody = ProcMesh.RegularPolygon(4, (0.5f - look.WallInset) * 1.4143f, 45f);

            // The held / carried / ammo icons follow the item-shape overrides, so re-skinning a shape
            // is consistent on the belts, in the ground and on the machines that handle it.
            _carriedIcons = Colors.ShapeIcons(look.DecomposerIconRadius, Colors.OutlinePixels);
            _ammoIcons = Colors.ShapeIcons(look.AmmoIconRadius, Colors.OutlinePixels);
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
                    case BehaviorKind.Converter: AppendConverter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Pipe: AppendPipe(_machines[i], outlineWidth); break;
                    case BehaviorKind.Splitter: AppendSplitter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Sorter: AppendSorter(_machines[i], outlineWidth); break;
                    case BehaviorKind.Turret: AppendTurret(_machines[i], outlineWidth); break;
                    case BehaviorKind.None: AppendWall(_machines[i], outlineWidth); break;
                    default:
                        // A behaviour this view does not draw would simply be absent from the screen -
                        // the machine built, working, invisible. Say so instead. (MachineSystem's
                        // start-up check normally catches this before the world exists; this is the
                        // view's own half of the same rule.)
                        throw new System.ArgumentOutOfRangeException(
                            nameof(_machines), _machines[i].Behavior,
                            "a new behaviour must get a drawing case in MachineRenderer");
                }
            }

            EndSprites();
        }

        /// <summary>A hollow frame plus an outlet chevron. No outline anywhere: an outline is baked
        /// at the current pixel width, but more importantly the patch icon underneath IS the drill's
        /// identity, so the frame only frames it.</summary>
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

        /// <summary>Hexagon body with the shape it holds as its icon - what its recipe is eating or
        /// has made, nothing - the idle state - when empty - plus a stub on every side the belts
        /// actually wired, so two outlets look like two outlets. There is no facing chevron: which
        /// sides are outlets is read off the belts, exactly as the tick reads them.
        ///
        /// Both converters come down this path. The icon is the snapshot's shape, which
        /// <see cref="MachineField"/> reads out of the recipe, so the decomposer shows its circles and
        /// the cutter its squares without either being named here.</summary>
        private void AppendConverter(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            // 0.8 r is inside the hexagon's flat edge (the apothem is 0.866 r) and the cardinal sides are
            // mid-edge, so the body polygon drawn next covers each stub's inner end.
            AppendPortStubs(machine, centre, Colors.Machines.DecomposerRadius * 0.8f);

            if (IsSprite(style))
            {
                DrawSprite(style, centre, Damaged(Color.white, machine));
            }
            else
            {
                AppendPolygon(BodyOf(style, _converterBody), centre,
                    Damaged(FillOf(style, machine.Behavior), machine),
                    OutlineOf(style), BodyOutlineWidth(style, outlineWidth));
            }

            AppendIcon(_carriedIcons, machine.Shape, centre, null);
        }

        /// <summary>The crossing piece: two rails running from this cell's entry edge across the
        /// jumped tile to the landing belt, with a collar at each end - the collars and the gap under
        /// the tube are what read as "over". The item in transit rides the tube at its work fraction,
        /// so a backed-up pipe shows its item parked over the landing belt.</summary>
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

        /// <summary>The hub plus one stub per live port, read off the belts around it exactly the
        /// way the simulation does. The buffered item sits at the hub's centre.</summary>
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

        /// <summary>
        /// The router: a triangle whose apex points out the side the filtered shape leaves by, with
        /// that shape as a badge at its centre, and a stub per live port - so the two things a sorter
        /// can be wrong about (which side, and which shape) are both on screen at once.
        ///
        /// The apex is the direction readout, which is why a sorter needs no chevron: no other
        /// building is a triangle, so the silhouette says "router" before the badge says which way.
        /// Port stubs are drawn first and the body covers their inner ends, exactly like the
        /// converters and the splitter.
        /// </summary>
        private void AppendSorter(in MachineSnapshot machine, float outlineWidth)
        {
            var centre = CellCentre(machine.Cell);
            VisualStyle style = Over(machine.Build);

            // 0.75 r reaches inside the triangle's incircle (r/2), so the body covers each stub's
            // inner end the way the converters' hexagon does.
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

            AppendIcon(_sorterIcons, machine.Shape, centre, null);
        }

        /// <summary>
        /// A cached polygon rotated about the origin by an angle in degrees, through a scratch array,
        /// so a view that rebuilds every frame allocates nothing to turn a shape. Rotation preserves
        /// the counter-clockwise winding that <see cref="ProcMesh.AppendPolygon"/>'s outline normals
        /// depend on.
        /// </summary>
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
        /// no belt (or a belt running past) gets nothing. Shared by the splitter, the converters and
        /// the sorter - the machines whose ports *are* the belts around them - and read from the same
        /// mask the tick pushes through, so the picture cannot drift from the rule. All of them draw
        /// their body after this, which is what hides each stub's inner end.
        ///
        /// SplitterStubHalfWidth is the palette's one stub width; it keeps its name because it is the
        /// same stub on every machine, and renaming a serialized field would silently drop the value
        /// out of Assets/Data/Palette.asset.</summary>
        private void AppendPortStubs(in MachineSnapshot machine, Vector2 centre, float innerRadius)
        {
            Palette.MachineLook look = Colors.Machines;

            for (int i = 0; i < Sides.Length; i++)
            {
                Dir side = Sides[i];
                bool inbound = machine.InMask.Has(side);
                bool outbound = machine.OutMask.Has(side);
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

        /// <summary>Octagon body, barrel along Aim, then the ammo icon. Draw order matters: barrel
        /// after body so it reads as the gun, icon last so the armed/silenced colour is never
        /// covered. The icon's grey is the "silenced" state - the single most important readout,
        /// because a turret with no ammo is a dead building.
        ///
        /// Both guns come down this path, and the icon is the snapshot's shape - which
        /// <see cref="MachineField"/> reads out of that gun's <see cref="TurretDef"/> - so the cannon
        /// shows half-circles and the mortar half-squares without either being named here.</summary>
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

            // A turret's diet is always a real shape (ContentDatabase.Turret); the icon is total, so
            // the null branch is written out rather than assumed away.
            Color? ammoTint = machine.Armed ? (Color?)null : Colors.MachineIdle;
            AppendIcon(_ammoIcons, machine.Shape, centre, ammoTint);
        }

        /// <summary>One quad from the body centre out along the aim. Aim is already normalized by
        /// the tick; a zero Aim (a turret that has never seen an enemy) falls back to its rest
        /// direction so the barrel still points somewhere sane. The barrel takes the body's colour so
        /// a re-skinned gun is one colour, and the default is the turret shade either way.</summary>
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

        /// <summary>
        /// The wall: a plain inset block in the machine-body colour, reddening as it is chewed. It has
        /// no behaviour and no state to show - the point of a wall is that there is nothing to read
        /// but "solid, and how close to falling" - so the silhouette is the whole readout.
        ///
        /// Only walls reach here. Belts are also <see cref="BehaviorKind.None"/> but they are not
        /// machines, so they never appear in <see cref="MachineField.GetMachines"/>.
        /// </summary>
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

        private void AppendChevron(Int2 cell, Dir direction, Color color)
        {
            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var centre = CellCentre(cell) + forward * Colors.Machines.ChevronOffset;

            Palette.MachineLook look = Colors.Machines;
            AppendChevron(centre, direction, look.ChevronBack, look.ChevronHalfWidth,
                look.ChevronLength, color);
        }
    }
}
