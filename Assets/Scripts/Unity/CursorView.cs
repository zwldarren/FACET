using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The cursor: with a building selected, the ghost that shows where the next placement would land,
    /// whether it would be allowed there, and what it would be: the belt's chevron, a machine's body and
    /// facing, and a turret's range ring. Green means buildable, amber means the stockpile cannot cover
    /// it, red means something is already there (or, for a drill, that there is no shape patch under it)
    /// - which is the feedback that makes dragging a run across an existing build predictable rather
    /// than surprising.
    ///
    /// With nothing selected there is no ghost, because nothing is being built: the cursor *reads*. The
    /// tile under the pointer carries a border - the accent over anything worth reading (a building, a
    /// vein in the ground, a wave's doorway), a plain edge over bare ground - and whatever the info card
    /// is describing stays marked while the mouse moves away from it, so the card and the map cannot
    /// disagree about which building "it" is. A turret's range ring follows the read building too.
    ///
    /// Its rebuild reasons are its own - the cursor cell and whether it is worth reading, the selected
    /// building and facing, whether the placement is valid and affordable, the inspected cell and what it
    /// holds - plus the gate's zoom reason, because the border and the ring are screen-space constants.
    /// The stockpile moving also dirties it: a cell the player cannot afford turns green the tick a
    /// delivery banks.
    /// </summary>
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
        private int _seenCircles = -1;

        protected override Palette.Layer Layer => Colors.CursorLayer;

        protected override void Observe(in ViewFrame frame)
        {
            Int2 cell = frame.CursorCell;
            Int2 inspected = World.InspectedCell;
            BuildKind? kind = World.SelectedKind;
            Dir direction = World.PlacementDirection;
            int circles = World.Economy.Circles;

            bool visible = World.TileGrid.InBounds(cell);
            bool readable = visible && World.CanInspect(cell);
            bool valid = kind.HasValue && visible && World.CanPlace(kind.Value, cell);
            bool affordable = kind.HasValue && World.Economy.CanAfford(kind.Value);

            // What the inspected cell holds, so the marker and the ring follow a building being placed
            // or removed under the pin rather than describing the tile it used to be.
            bool pinned = World.CanInspect(inspected);
            BuildKind? pinnedTurret = null;
            if (pinned && World.Machines.TryGet(inspected, out MachineState machine) &&
                World.Content.IsTurret(machine.Build))
                pinnedTurret = machine.Build;

            if (visible == _seenVisible && cell == _seenCell && valid == _seenValid &&
                affordable == _seenAffordable && circles == _seenCircles &&
                kind == _seenKind && direction == _seenDirection &&
                readable == _seenReadable && inspected == _seenInspected &&
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
            _seenCircles = circles;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            // Nothing selected: no ghost to draw, because the next click builds nothing. What the
            // cursor draws instead is what it would read.
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
                // A machine is a single tile with a facing, so the ghost is its footprint plus the arrow
                // it will be built facing - except the machines with no facing, whose ports are read off
                // the belts around them, where an arrow would promise what Q/E cannot turn.
                Color body = edge;
                body.a = look.BodyAlpha;
                float bodyInset = look.BodyInset;
                AppendQuad(
                    new Vector2(x + bodyInset, y + bodyInset), new Vector2(x + 1f - bodyInset, y + bodyInset),
                    new Vector2(x + 1f - bodyInset, y + 1f - bodyInset), new Vector2(x + bodyInset, y + 1f - bodyInset), body);

                if (HasFacing(kind))
                    AppendChevron(new Vector2(x + 0.5f, y + 0.5f), _seenDirection,
                        look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, edge);
            }

            if (World.Content.IsTurret(kind))
                AppendRangeRing(Centre(_seenCell), kind, _seenValid ? Colors.RangeRing : Colors.CursorBlocked);
        }

        /// <summary>
        /// The read cursor - what a click with nothing selected would describe. Over something worth
        /// reading (a building, a vein, a doorway) it is the HUD's accent, the colour the info card marks
        /// what it is talking about with; over bare ground it is a plain dark edge, which is also the
        /// honest answer to "what is here": nothing.
        ///
        /// The pinned cell draws the same border thicker and pushed in, whether or not the pointer is on
        /// it: it is what the info card is describing, and the player has to be able to see which
        /// building that is from wherever the mouse has gone.
        /// </summary>
        private void AppendReadBorder(Int2 cell, bool readable, bool pinned)
        {
            Palette.CursorLook look = Colors.Cursor;

            Color colour = readable ? Colors.HudAccent : Colors.Outline;
            colour.a = readable ? look.EdgeAlpha : look.EdgeAlpha * 0.6f;

            float scale = pinned ? 1.6f : 1f;
            float thickness = Mathf.Min(Colors.CursorEdgePixels * WorldPerPixel * scale, look.MaxEdgeThickness);

            // Pushed a little way in so the pinned ring and the pointer's own ring can never be read as
            // one shape at a far zoom; capped so the four edges cannot cross each other either.
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

        /// <summary>Does a placement of this kind show its facing? The rule lives on the machine
        /// definition (<see cref="MachineDef.UsesFacing"/>) because the HUD that names a built building's
        /// facing needs the same answer.
        ///
        /// The run's table and not <see cref="ContentDatabase.Default"/>: a custom table can make any
        /// building a converter or a splitter, and a ghost that read the shipped numbers would then
        /// disagree with the ring the same method draws beside it.</summary>
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

        /// <summary>
        /// The ring every turret is placed for - and, with nothing selected, the ring of the turret the
        /// info card is describing. It is a thin annulus built from quads rather than a scaled outline,
        /// so its width stays a constant number of screen pixels at any zoom.
        /// </summary>
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
