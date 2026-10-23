using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>The cursor. With a building selected it is the ghost that shows where the next placement
    /// would land, whether it would be allowed, and what it would be: the belt's chevron, a machine's body
    /// and facing, a cutter's two tiles and the blade between them, and the two pieces that cutter is about to
    /// make of the item on its way in, a turret's range ring. Green is buildable, amber
    /// means the stockpile cannot cover it, red
    /// means something is already there (or, for a drill, no shape patch under it) - feedback that makes
    /// dragging a run across an existing build predictable. With nothing selected there is no ghost, because
    /// nothing is being built: the cursor reads. The tile under the pointer carries a border - the accent over
    /// anything worth reading (a building, a vein, a doorway), a plain edge over bare ground - and the info
    /// card's subject stays marked while the mouse moves away, so the card and the map cannot disagree.
    ///
    /// Its rebuild reasons are its own - the cursor cell and whether it is worth reading, the selected
    /// building and facing, validity and affordability, the inspected cell and its contents, the two pieces
    /// the selected cutter would cut (which change as a different item comes down the lane), the stockpile,
    /// which dirties it because a cell the player cannot afford turns green the tick a delivery banks, plus
    /// the gate's zoom reason, since the border and the ring are screen-space constants.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class CursorView : MeshView
    {
        private Int2 _seenCell = new Int2(int.MinValue, int.MinValue);
        private Int2 _seenInspected = new Int2(int.MinValue, int.MinValue);
        private bool _seenValid;
        private bool _seenAffordable;
        private bool _seenVisible;
        private bool _seenReadable;
        private bool _seenPinned;
        private BuildKind? _seenKind;
        private BuildKind? _seenPinnedTurret;
        private Dir _seenDirection;
        private int _seenShapes = -1;

        /// <summary>The two pieces the selected cutter's ghost is previewing, so a different item coming down
        /// the lane redraws the ghost while the cursor has not moved.</summary>
        private Shape _seenStraight = Shape.None;
        private Shape _seenDisplaced = Shape.None;

        /// <summary>The stacker ghost's preview (#19): the two pieces on their way in and the round they would
        /// join into, or nothing when no disjoint pair is coming.</summary>
        private Shape _seenStackFirst = Shape.None;
        private Shape _seenStackSecond = Shape.None;
        private Shape _seenStackUnion = Shape.None;

        /// <summary>The figures the cutter's ghost previews, at the radius and outline the belt items and the
        /// machine badges are drawn with - so a previewed half is the very figure that will ride the belt.</summary>
        private ShapeIconSet _pieceIcons;

        protected override Palette.Layer Layer => Colors.CursorLayer;

        protected override void OnInitialized()
            => _pieceIcons = Colors.ShapeIcons(Colors.Items.Radius, Colors.OutlinePixels);

        protected override void Observe(in ViewFrame frame)
        {
            Int2 cell = frame.CursorCell;
            Int2 inspected = World.InspectedCell;
            BuildKind? kind = World.SelectedKind;
            Dir direction = World.PlacementDirection;
            int shapes = World.Economy.Shapes;

            bool visible = World.TileGrid.InBounds(cell);
            bool readable = visible && World.CanInspect(cell);
            bool valid = kind.HasValue && visible && World.CanPlace(kind.Value, cell, direction);
            bool affordable = kind.HasValue && World.Economy.CanAfford(kind.Value);

            // What the selected cutter would cut of the item on its way in, read here rather than when the ghost
            // draws: a different item coming down the lane is a reason to redraw a ghost the cursor has not
            // moved. Only a placement that is legal previews anything - the pieces are what this machine will
            // make, and a machine that cannot be built makes nothing.
            Shape straight = Shape.None;
            Shape displaced = Shape.None;
            if (kind == BuildKind.Cutter && valid)
                MachineReading.TryPreviewCut(World, cell, direction, out _, out straight, out displaced);

            // The stacker's half of the same idea: what the two belts pointing into this cell would hand it,
            // and what comes out.
            Shape stackFirst = Shape.None;
            Shape stackSecond = Shape.None;
            Shape stackUnion = Shape.None;
            if (kind.HasValue && valid && World.Content.Machine(kind.Value).Behavior == BehaviorKind.Stacker)
                MachineReading.TryPreviewStack(World, cell, out stackFirst, out stackSecond, out stackUnion);

            // What the inspected cell holds, so the marker and ring follow a building placed or removed under the
            // pin rather than describing the tile it used to be.
            bool pinned = World.CanInspect(inspected);
            BuildKind? pinnedTurret = null;
            if (pinned && World.Machines.TryGet(inspected, out MachineState machine) &&
                World.Content.IsTurret(machine.Build))
                pinnedTurret = machine.Build;

            if (visible == _seenVisible && cell == _seenCell && valid == _seenValid &&
                affordable == _seenAffordable && shapes == _seenShapes &&
                kind == _seenKind && direction == _seenDirection &&
                readable == _seenReadable && inspected == _seenInspected &&
                straight == _seenStraight && displaced == _seenDisplaced &&
                stackFirst == _seenStackFirst && stackSecond == _seenStackSecond && stackUnion == _seenStackUnion &&
                pinned == _seenPinned && pinnedTurret == _seenPinnedTurret) return;

            _seenCell = cell;
            _seenInspected = inspected;
            _seenValid = valid;
            _seenAffordable = affordable;
            _seenVisible = visible;
            _seenReadable = readable;
            _seenPinned = pinned;
            _seenKind = kind;
            _seenPinnedTurret = pinnedTurret;
            _seenDirection = direction;
            _seenShapes = shapes;
            _seenStraight = straight;
            _seenDisplaced = displaced;
            _seenStackFirst = stackFirst;
            _seenStackSecond = stackSecond;
            _seenStackUnion = stackUnion;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            // The sprite pool is opened and closed around the whole frame: a previewed piece is drawn through
            // the same AppendIcon the items are, and a mark the palette overrides with a sprite is taken from
            // this view's own pool.
            BeginSprites();
            AppendCursor(in frame);
            EndSprites();
        }

        private void AppendCursor(in ViewFrame frame)
        {
            // Nothing selected: no ghost, because the next click builds nothing - instead draw what it would read.
            if (!_seenKind.HasValue)
            {
                if (_seenVisible) AppendReadBorder(_seenCell, _seenReadable, pinned: false);
                if (_seenPinned) AppendReadBorder(_seenInspected, readable: true, pinned: true);
                if (_seenPinnedTurret.HasValue)
                    AppendRangeRing(Centre(_seenInspected), _seenPinnedTurret.Value, Colors.RangeRing);

                return;
            }

            if (!_seenVisible) return;

            BuildKind kind = _seenKind.Value;
            Palette.CursorLook look = Colors.Cursor;
            var x = (float)_seenCell.X;
            var y = (float)_seenCell.Y;
            var centre = new Vector2(x + 0.5f, y + 0.5f);
            Color edge;
            if (!_seenValid) edge = Colors.CursorBlocked;
            else if (_seenAffordable) edge = Colors.CursorOk;
            else edge = Colors.CursorNoFunds;
            Color fill = edge;
            fill.a = look.FillAlpha;
            edge.a = look.EdgeAlpha;

            AppendQuad(
                new Vector2(x, y), new Vector2(x + 1f, y),
                new Vector2(x + 1f, y + 1f), new Vector2(x, y + 1f), fill);

            float t = Mathf.Min(Colors.CursorEdgePixels * frame.WorldPerPixel, look.MaxEdgeThickness);
            AppendBorder(new Vector2(x, y), 1f, t, 0f, edge);

            if (kind == BuildKind.Belt)
            {
                AppendChevron(new Vector2(x + 0.5f, y + 0.5f), _seenDirection,
                    look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
            }
            else
            {
                // A machine is a footprint with a facing, so the ghost is its body plus the build arrow -
                // except the machines with no facing, whose ports are read off surrounding belts, where an
                // arrow would promise what Q/E cannot turn.
                Color body = edge;
                body.a = look.BodyAlpha;

                if (World.Content.Machine(kind).Behavior == BehaviorKind.Cutter)
                {
                    AppendCutterGhost(_seenCell, _seenDirection, edge, body, t);
                    return;
                }

                if (World.Content.Machine(kind).Behavior == BehaviorKind.Sink)
                {
                    AppendSinkGhost(_seenCell, edge, body);
                    return;
                }

                AppendBodyQuad(_seenCell, body, look.BodyInset);

                if (HasFacing(kind))
                {
                    AppendChevron(centre, _seenDirection,
                        look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
                }

                if (World.Content.Machine(kind).Behavior == BehaviorKind.Stacker) AppendStackPreview(_seenCell);
            }

            if (World.Content.IsTurret(kind))
                AppendRangeRing(Centre(_seenCell), kind, _seenValid ? Colors.RangeRing : Colors.CursorBlocked);
        }

        /// <summary>
        /// The cutter's ghost: the two tiles it will stand on, the blade down the lane the belt runs in, a
        /// chevron in each lane saying which way the line runs, and - when something is on its way in - the two
        /// pieces of the item it is about to be handed, each drawn on the tile it leaves by.
        ///
        /// This is the whole explanation of a cutter, on the screen before the click rather than in the
        /// manual. The body covers both tiles, so "this machine is two tiles wide across the belt" is
        /// visible; the blade lies along the flow through the lane the belt runs in, so where the cut is made
        /// is visible; a chevron in each lane says both lanes leave that way; and the two pieces answer the
        /// question the blade cannot - <em>which</em> half ends up in which lane, which is the one thing about
        /// this machine a player otherwise has to simulate in their head. A player who reads it never
        /// has to guess that the aim is theirs - which is exactly what a cutter that drew nothing but a knife
        /// line across its cell left open, and how a fresh player concludes that a half can only be turned by
        /// a rotator.
        ///
        /// The pieces are a <b>read of the belts in front of the cursor</b>, not a promise about the item: what
        /// is on its way in is what the lane is carrying this second, and the figures are
        /// <see cref="MachineReading.TryPreviewCut"/>'s - the tick's own port, intake and cut rules. With
        /// nothing on its way in, nothing is drawn: the machine cuts whatever it is handed, and there is then
        /// nothing to hand it.
        /// </summary>
        private void AppendCutterGhost(Int2 cell, Dir facing, Color edge, Color body, float borderThickness)
        {
            Palette.CursorLook look = Colors.Cursor;

            Dir lane = MachineFootprint.LaneOf(facing);         // the machine's right: where the second tile is
            Int2 laneCell = cell + lane.Offset();

            var flow = new Vector2(facing.ToVec().X, facing.ToVec().Y);
            var middle = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
            var laneMiddle = new Vector2(laneCell.X + 0.5f, laneCell.Y + 0.5f);

            // The second tile: tinted like the first and carrying its own body, but deliberately without a
            // border of its own - the border is the tile the click lands on, and two of them would read as
            // two machines rather than as one.
            Color tint = edge;
            tint.a = look.FillAlpha;
            AppendQuad(new Vector2(laneCell.X, laneCell.Y), new Vector2(laneCell.X + 1f, laneCell.Y),
                new Vector2(laneCell.X + 1f, laneCell.Y + 1f), new Vector2(laneCell.X, laneCell.Y + 1f), tint);

            AppendBodyQuad(cell, body, look.BodyInset);
            AppendBodyQuad(laneCell, body, look.BodyInset);

            // The blade: the item's own centre line along the flow, where the cut is made - through the middle
            // of the lane the belt runs in, not between the machine's two tiles. The second tile is where the
            // displaced half is dropped, one lane over.
            AppendLane(middle - flow * 0.5f, middle + flow * 0.5f, borderThickness * 0.5f, edge);

            // A chevron in each lane, at the front where the belt leaves: the same "this way" a belt wears, so
            // the two lanes the machine forks into read as two belts rather than as one body with a mark.
            var ahead = flow * look.ChevronOffset;
            AppendChevron(middle + ahead, facing, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
            AppendChevron(laneMiddle + ahead, facing, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);

            // What it would cut, each piece on the tile it leaves by: the lane the belt fed for the piece that
            // runs straight on, and the second tile's lane for the one that leaves a lane over.
            if (!_seenValid ||
                !MachineReading.TryPreviewCut(World, cell, facing, out _, out Shape straight, out Shape displaced))
                return;

            Int2 straightCell = cell + facing.Offset();
            Int2 displacedCell = laneCell + facing.Offset();
            if (World.TileGrid.InBounds(straightCell))
                AppendIcon(_pieceIcons, straight, Centre(straightCell), null);
            if (World.TileGrid.InBounds(displacedCell))
                AppendIcon(_pieceIcons, displaced, Centre(displacedCell), null);
        }

        /// <summary>
        /// The stacker ghost's preview (#19), the counterpart of the cutter's pieces: the round the two pieces on
        /// their way in would join into, drawn on the machine itself - the pieces are already on screen riding
        /// their belts, so the one thing the player cannot see yet is what they make. Nothing is drawn when no
        /// disjoint pair is coming: the honest answer, since the machine would wait.
        /// </summary>
        private void AppendStackPreview(Int2 cell)
        {
            if (!_seenValid || _seenStackUnion.IsEmpty) return;
            AppendIcon(_pieceIcons, _seenStackUnion, Centre(cell), null);
        }

        /// <summary>
        /// The sink's ghost: a block with a pit at its centre and a chevron on each side pointing in.
        ///
        /// The machine has no facing, so Q/E turns nothing and an arrow would promise otherwise - what the
        /// ghost has to say instead is the one thing its name cannot: everything goes in, from any side, and
        /// nothing comes out. The four inward arrows say both halves of that before the click, and they are
        /// the same marks the placed machine wears, so the ghost and the building it becomes agree.
        /// </summary>
        private void AppendSinkGhost(Int2 cell, Color edge, Color body)
        {
            Palette.CursorLook look = Colors.Cursor;
            Palette.MachineLook machine = Colors.Machines;
            var centre = Centre(cell);

            AppendBodyQuad(cell, body, look.BodyInset);

            // The pit, in the cursor's own colour: a hole is the whole readout, and it has to be as visible as
            // the border around it at every zoom - which a fill alpha would not be.
            float pit = machine.SinkHoleHalfSize;
            AppendQuad(
                new Vector2(centre.x - pit, centre.y - pit), new Vector2(centre.x + pit, centre.y - pit),
                new Vector2(centre.x + pit, centre.y + pit), new Vector2(centre.x - pit, centre.y + pit), edge);

            for (int i = 0; i < 4; i++)
            {
                Dir side = (Dir)i;
                Vec2 away2 = side.ToVec();
                var away = new Vector2(away2.X, away2.Y);
                AppendChevron(centre + away * machine.ChevronOffset, side.Opposite(),
                    machine.ChevronBack, machine.ChevronHalfWidth, machine.ChevronLength, edge);
            }
        }

        /// <summary>An inset body quad on one tile of a machine's footprint.</summary>
        private void AppendBodyQuad(Int2 cell, Color body, float inset)
        {
            float x = cell.X;
            float y = cell.Y;
            AppendQuad(
                new Vector2(x + inset, y + inset), new Vector2(x + 1f - inset, y + inset),
                new Vector2(x + 1f - inset, y + 1f - inset), new Vector2(x + inset, y + 1f - inset), body);
        }

        /// <summary>The read cursor - what a click with nothing selected would describe. Over something worth
        /// reading (a building, a vein, a doorway) it is the HUD's accent, the colour the info card marks its
        /// subject with; over bare ground it is a plain dark edge, the honest answer to "what is here": nothing.
        /// The pinned cell draws the same border thicker and pushed in, whether or not the pointer is on it: it is
        /// what the info card is describing, and the player must be able to see which building that is from
        /// wherever the mouse has gone.</summary>
        private void AppendReadBorder(Int2 cell, bool readable, bool pinned)
        {
            Palette.CursorLook look = Colors.Cursor;

            Color colour = readable ? Colors.HudAccent : Colors.Outline;
            colour.a = readable ? look.EdgeAlpha : look.EdgeAlpha * 0.6f;

            float scale = pinned ? 1.6f : 1f;
            float thickness = Mathf.Min(Colors.CursorEdgePixels * WorldPerPixel * scale, look.MaxEdgeThickness);

            // Pushed in so the pinned ring and the pointer's ring cannot read as one shape at a far zoom; capped so
            // the four edges cannot cross either.
            float inset = pinned ? Mathf.Min(thickness, 0.25f) : 0f;
            AppendBorder(new Vector2(cell.X, cell.Y), 1f, thickness, inset, colour);

            if (!readable) return;

            Color fill = colour;
            fill.a = look.FillAlpha;
            AppendQuad(
                new Vector2(cell.X, cell.Y), new Vector2(cell.X + 1f, cell.Y),
                new Vector2(cell.X + 1f, cell.Y + 1f), new Vector2(cell.X, cell.Y + 1f), fill);
        }

        private static Vector2 Centre(Int2 cell) => new Vector2(cell.X + 0.5f, cell.Y + 0.5f);

        /// <summary>Does a placement of this kind show its facing? The rule lives on the machine definition
        /// (<see cref="MachineDef.UsesFacing"/>), because the HUD that names a built building's facing needs the
        /// same answer. It reads the run's table, not <see cref="ContentDatabase.Default"/>: a custom table can
        /// make a second operator or a second hub, and a ghost reading the shipped numbers would disagree
        /// with the ring the same method draws beside it.</summary>
        private bool HasFacing(BuildKind kind) => World.Content.Machine(kind).UsesFacing;

        /// <summary>An inset border around a cell, as four quads.</summary>
        private void AppendBorder(Vector2 origin, float size, float thickness, float inset, Color color)
        {
            float a = inset;
            float b = size - inset;
            float inner = b - thickness;

            AppendQuad(
                new Vector2(origin.x + a, origin.y + a), new Vector2(origin.x + b, origin.y + a),
                new Vector2(origin.x + b, origin.y + a + thickness), new Vector2(origin.x + a, origin.y + a + thickness), color);
            AppendQuad(
                new Vector2(origin.x + a, origin.y + inner), new Vector2(origin.x + b, origin.y + inner),
                new Vector2(origin.x + b, origin.y + b), new Vector2(origin.x + a, origin.y + b), color);
            AppendQuad(
                new Vector2(origin.x + a, origin.y + a), new Vector2(origin.x + a + thickness, origin.y + a),
                new Vector2(origin.x + a + thickness, origin.y + b), new Vector2(origin.x + a, origin.y + b), color);
            AppendQuad(
                new Vector2(origin.x + inner, origin.y + a), new Vector2(origin.x + b, origin.y + a),
                new Vector2(origin.x + b, origin.y + b), new Vector2(origin.x + inner, origin.y + b), color);
        }

        /// <summary>The ring every turret is placed for - and, with nothing selected, the ring of the turret the
        /// info card describes. A thin annulus of quads rather than a scaled outline, so its width stays a constant
        /// number of screen pixels at any zoom.</summary>
        private void AppendRangeRing(Vector2 centre, BuildKind turret, Color color)
        {
            float radius = World.Content.Turret(turret).Range;
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
    }
}
