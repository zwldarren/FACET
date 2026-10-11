using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The grid half of the <see cref="MapEditorWindow"/>: the map itself, painted - the fills and
    /// overlays that show its data, the stroke the mouse makes on it, and the scroll-to-cell the
    /// validation panel's Focus asks for. The tones the grid and the legend paint in live here too,
    /// because the tones are the grid's key to the map's data and the legend only states them.
    ///
    /// Three modules carry what this fragment used to hold in its own fields. The geometry - fit, zoom,
    /// clamp, the flip, the hit-test - is <see cref="GridFrame"/>'s arithmetic, computed once per pass
    /// into <see cref="_frame"/> and read by drawing, mouse and focusing alike, so no second copy of
    /// the flip can slide out of step (and the arithmetic is testable without a mouse). What the map
    /// <em>is</em> to be drawn comes from the asset's one canonical walk,
    /// <see cref="MapAsset.DrawableView"/> - the same enumeration the preview's change-digest hashes,
    /// so the picture and the digest cannot disagree about a cell. And every edit is a
    /// <see cref="MapEdit.Session"/>: one undo entry for a whole stroke, the record and the dirty flag
    /// owned by the session rather than restated around this mouse handler.
    ///
    /// The one IMGUI rule the adapter still obeys: the frame is computed in <em>every</em> pass, not
    /// only while painting - a mouse event arrives in a pass of its own, and a frame that existed only
    /// during Repaint would leave that pass with nothing to hit-test, the classic way a custom grid
    /// stops answering the mouse.
    /// </summary>
    public sealed partial class MapEditorWindow
    {
        /// <summary>Cells at least this big get the 1px boundary between them. Smaller than this a line is
        /// a sixth of a cell wide, and the grid reads as a grey wash rather than as tiles.</summary>
        private const float GridLineCellSize = 6f;

        /// <summary>Cells at least this big get their entry point's number drawn inside them.</summary>
        private const float LabelCellSize = 16f;

        /// <summary>The tones the grid is painted in. They are the window's own: Core has no colour for a
        /// mineral or a kind of ground - a look is the view layer's business - so the window states the
        /// key it uses in its own legend rather than borrowing one that does not exist.</summary>
        private static readonly Color GroundTone = new Color(0.80f, 0.80f, 0.82f);
        private static readonly Color RockTone = new Color(0.34f, 0.36f, 0.42f);
        private static readonly Color WaterTone = new Color(0.24f, 0.46f, 0.80f);
        private static readonly Color CircleTone = new Color(0.98f, 0.80f, 0.35f);
        private static readonly Color HalfCircleTone = new Color(0.86f, 0.55f, 0.20f);
        private static readonly Color SquareTone = new Color(0.45f, 0.85f, 0.80f);
        private static readonly Color HalfSquareTone = new Color(0.26f, 0.60f, 0.62f);
        private static readonly Color LineTone = new Color(0.45f, 0.45f, 0.45f, 0.45f);

        /// <summary>Tinted rather than opaque, for the same reason on both: a door standing on rock or a
        /// Core standing on water is exactly what the validator reports, and a block that hid the ground
        /// underneath would be the window hiding the mistake.</summary>
        private static readonly Color CoreTone = new Color(0.55f, 0.35f, 0.85f, 0.60f);
        private static readonly Color DoorTone = new Color(0.20f, 0.80f, 0.35f, 0.65f);

        /// <summary>The stroke in progress, or null between presses. Held across event passes exactly
        /// as the geometry is: the press opens one undo entry for everything the drag writes, and
        /// only the release closes it.</summary>
        private MapEdit.Session _stroke;

        /// <summary>The grid's geometry as of the last pass - computed before anything is drawn and
        /// kept so a mouse event in its own pass and the validation panel's Focus button both work
        /// against the layout that was actually drawn. All arithmetic lives in
        /// <see cref="GridFrame"/>.</summary>
        private GridFrame _frame;

        /// <summary>The grid's own scroll offset, unlike the panel's. The frame clamps what it is
        /// asked for; what the scroll view actually applied is read back after the pass, which is
        /// what the next frame is laid out from.</summary>
        private Vector2 _scroll;

        /// <summary>
        /// The map itself, painted. The frame is laid out in every pass, the cells are drawn through
        /// the asset's canonical walk, and the mouse is answered after the view ends.
        /// </summary>
        private void DrawGrid(MapAsset asset)
        {
            Rect view = EditorGUILayout.GetControlRect(false, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _frame = GridFrame.Layout(view, asset.Width, asset.Height, _zoom, _scroll);

            if (!_frame.Valid)
            {
                // Nothing has been laid out yet, the window is too small for a grid, or the map itself
                // has no tiles: this pass draws nothing - but the mouse still answers, because a
                // release that landed while the grid was degenerate must still close the stroke.
                PaintFromEvent(asset);
                return;
            }

            _scroll = GUI.BeginScrollView(view, _frame.Scroll, _frame.ScrollContent);

            DrawnMap drawn = asset.DrawableView();
            DrawFills(drawn, _frame);
            DrawGridLines(_frame);
            DrawCore(drawn, _frame);
            DrawDoors(drawn, _frame);

            GUI.EndScrollView();

            PaintFromEvent(asset);
        }

        /// <summary>
        /// Every cell's ground, then every vein. One rectangle per cell rather than one per terrain
        /// run, and straight off the canonical walk: the fills see exactly the cells the digest
        /// counted, first-wins and clipped to the map, because it is the same walk.
        /// </summary>
        private static void DrawFills(DrawnMap drawn, GridFrame frame)
        {
            for (int y = 0; y < drawn.Height; y++)
            {
                for (int x = 0; x < drawn.Width; x++)
                {
                    TerrainKind kind = drawn.GroundAt(x, y);

                    // The void keeps the window's own background, exactly as it keeps the scene's in
                    // game: not drawing it is what makes an irregular map read as a shape.
                    if (!kind.IsDrawn()) continue;

                    EditorGUI.DrawRect(frame.CellRect(x, y), TerrainTone(kind));
                }
            }

            // Veins come off the same walk, which is the only place a mineral is written down. A tile
            // it does not name is plain ground to the simulation and is drawn as ground.
            for (int y = 0; y < drawn.Height; y++)
                for (int x = 0; x < drawn.Width; x++)
                {
                    ShapeType shape = drawn.MineralAt(x, y);
                    if (!shape.IsShape()) continue;

                    EditorGUI.DrawRect(frame.CellRect(x, y), OreTone(shape));
                }
        }

        /// <summary>
        /// Tile boundaries, drawn over the whole rectangle including the void: they are the painting
        /// surface's, not the map's, so a designer can see where the next void tile is.
        /// </summary>
        private static void DrawGridLines(GridFrame frame)
        {
            if (frame.Cell < GridLineCellSize) return;

            for (int x = 0; x <= frame.MapWidth; x++)
                EditorGUI.DrawRect(new Rect(x * frame.Cell, 0f, 1f, frame.ContentSize.y), LineTone);

            for (int y = 0; y <= frame.MapHeight; y++)
                EditorGUI.DrawRect(new Rect(0f, y * frame.Cell, frame.ContentSize.x, 1f), LineTone);
        }

        /// <summary>The Core's footprint where it will stand, clipped to the grid: an out-of-bounds
        /// footprint is the validator's problem to report, and the overlay must show the data rather than
        /// draw over the panel.</summary>
        private static void DrawCore(DrawnMap drawn, GridFrame frame)
        {
            Vector2Int core = drawn.Core;

            int left = Mathf.Max(0, core.x);
            int bottom = Mathf.Max(0, core.y);
            int right = Mathf.Min(drawn.Width, core.x + CoreState.Size);
            int top = Mathf.Min(drawn.Height, core.y + CoreState.Size);
            if (right <= left || top <= bottom) return;

            var rect = new Rect(left * frame.Cell, (drawn.Height - top) * frame.Cell,
                (right - left) * frame.Cell, (top - bottom) * frame.Cell);

            EditorGUI.DrawRect(rect, CoreTone);
        }

        /// <summary>
        /// The wave entry points, numbered with the position a wave group names them by - the walk
        /// carries that position with the cell, so a door dropped off an edge never renumbers its
        /// siblings. The number is drawn only once a cell is wide enough to read a digit in; below
        /// that the marker marks the tile and the panel says how many there are.
        /// </summary>
        private static void DrawDoors(DrawnMap drawn, GridFrame frame)
        {
            for (int i = 0; i < drawn.DoorCells.Length; i++)
            {
                Vector2Int door = drawn.DoorCells[i];
                Rect rect = frame.CellRect(door.x, door.y);
                EditorGUI.DrawRect(rect, DoorTone);

                if (frame.Cell >= LabelCellSize)
                    GUI.Label(rect, drawn.DoorPositions[i].ToString());
            }
        }

        /// <summary>
        /// The mouse, once the grid has been drawn. Which tile a point is over is the frame's answer -
        /// view, content, scrollbars and the flip were all decided where they were drawn, so this pass
        /// cannot disagree with the pixels. The stroke is a <see cref="MapEdit.Session"/>: opened by
        /// the press, holding one undo entry for the whole drag, closed by the release wherever that
        /// lands. Erasing is the right button here (the Scene view's brush binds it to Shift, where the
        /// right button belongs to the camera); what a brush writes is <see cref="MapPainting"/>'s
        /// answer, so the two surfaces cannot come out meaning different things by a rock.
        /// </summary>
        private void PaintFromEvent(MapAsset asset)
        {
            Event current = Event.current;

            if (current.type == EventType.MouseUp)
            {
                _stroke?.Dispose();
                _stroke = null;
                return;
            }

            bool down = current.type == EventType.MouseDown;
            if (!down && current.type != EventType.MouseDrag) return;

            // The middle button is nobody's brush, and a drag that began somewhere else - on the zoom
            // slider, say - is not a stroke either.
            if (current.button != 0 && current.button != 1) return;
            if (!down && _stroke == null) return;

            if (!_frame.TryCellAt(current.mousePosition, out int x, out int y)) return;

            bool erase = current.button == 1;
            if (down)
                _stroke = new MapEdit.Session(asset, erase ? "Erase map" : "Paint map");

            _stroke.Paint(x, y, _brush, _oreShape, erase);

            // A drag event does not redraw the window on its own, so without this the tile just painted
            // would not appear until some unrelated event came along.
            Repaint();
            current.Use();
        }

        /// <summary>
        /// Scroll the grid so a tile sits in the middle of what is visible. A scroll offset rather than a
        /// selection: the window has no notion of a selected tile, and inventing one to host a Focus
        /// button would be a second piece of state that could disagree with the map. The frame answers
        /// where the tile is and where the offset has to stop; this only says the window's scroll is
        /// now that answer, picked up by the redraw the click itself causes.
        /// </summary>
        private void FocusCell(int x, int y)
        {
            _scroll = _frame.ScrollTo(x, y);
        }

        /// <summary>The tone of a kind of ground. Ore is the tone of ground here because a vein's own tone
        /// is its mineral's, painted over it in the same pass.</summary>
        private static Color TerrainTone(TerrainKind kind)
        {
            switch (kind)
            {
                case TerrainKind.Rock: return RockTone;
                case TerrainKind.Water: return WaterTone;
                default: return GroundTone;
            }
        }

        /// <summary>
        /// A mineral's tone. Distinct per shape because a shape is the only identity a mineral has in
        /// Core, and a glyph would need a texture or a font the window does not own - so the tone is the
        /// key, and the legend above states it. None draws as ground, which is what a vein nobody
        /// assigned yields; an unknown shape throws rather than drawing ground-coloured - a vein the
        /// editor silently cannot show is a map bug authored one brush stroke at a time.
        /// </summary>
        private static Color OreTone(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.None: return GroundTone;
                case ShapeType.Circle: return CircleTone;
                case ShapeType.HalfCircle: return HalfCircleTone;
                case ShapeType.Square: return SquareTone;
                case ShapeType.HalfSquare: return HalfSquareTone;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(shape), shape,
                        "a new shape must get a tone here (editor grid) and in the Palette");
            }
        }
    }
}
