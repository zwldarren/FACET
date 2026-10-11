using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The Scene view as a map editing surface: the map drawn by the game's own views, a brush chosen from
    /// an overlay, and a stroke that goes straight into the <see cref="MapAsset"/> on screen.
    ///
    /// It exists because the painting window can only say what a tile <em>is</em> - a grid of tones and a
    /// legend, at whatever size the window happens to be - while a map is a picture of the world the player
    /// will walk: the width of a rock ridge, the mouth a wave walks in at, the distance between the two.
    /// The Scene view is where that picture already belongs, and <see cref="MapPreview"/> is already how it
    /// is drawn there with no Play mode; this is the missing half, the brush.
    ///
    /// Five things are worth knowing before reading it.
    ///
    /// <b>It is entered, not always on.</b> Nothing here runs until <c>FACET ▸ Edit Map in Scene</c> names a
    /// map - by <see cref="MapEditTarget"/>, which is also what draws it. A Scene view that painted
    /// whenever a map asset happened to be selected would be a Scene view that can no longer select
    /// anything, so the mode is asked for, and left with Done, with Escape, or with the menu item that
    /// stops it.
    ///
    /// <b>A stroke is one undo entry.</b> The press opens a <see cref="MapEdit.Session"/> and the release
    /// closes it: the asset is recorded once, not per cell, because a drag is one edit the designer made,
    /// and a Ctrl+Z that stepped back a single tile would be a Ctrl+Z that does not undo what was done.
    /// The painting window's grid holds a stroke the same way - the protocol, like the brush itself, is
    /// shared rather than restated.
    ///
    /// <b>Erasing is the Shift key, not the right button.</b> The right button already belongs to the view
    /// - it looks around, and it has to keep doing that while a map is being painted - so "take it away" is
    /// the modifier that means it everywhere else in the editor.
    ///
    /// <b>The brush owns the mouse only over the map.</b> A click that lands on a tile is consumed, so the
    /// Scene view cannot read it as a click on the world behind the map and drop the selection the map is
    /// being edited from. A click outside the map is left alone, so everything else in the Scene view still
    /// answers while a brush is in hand. Alt still orbits and the view tools still pan, because moving the
    /// camera is not painting.
    ///
    /// <b>And the pointer is read through the map's own plane.</b> The map is flat and lies where the game
    /// draws it, so the tile under the pointer is the view's ray met with that plane - which is what makes
    /// the brush land where it looks like it lands, at any zoom and from any angle, rather than a screen
    /// rectangle divided by a cell size that is only right in the one camera the preview will draw.
    /// </summary>
    [InitializeOnLoad]
    internal static class MapSceneEditor
    {
        private const string BrushKey = "Facet.MapScene.Brush";
        private const string OreKey = "Facet.MapScene.Ore";

        /// <summary>Where the overlay sits in the view: clear of the tab strip above it and of the view
        /// tools down the left edge, which is where a panel in the other corner would be unusable.</summary>
        private const float PanelX = 48f;
        private const float PanelY = 66f;
        private const float PanelWidth = 620f;
        private const float PanelHeight = 62f;

        /// <summary>The hovered tile is marked at a z in front of every layer of the map - in front of the
        /// cursor layer rather than on the ground - because the mark is the Scene view's own and not part
        /// of the map; drawn at the map's own z it would z-fight the ground it is drawn over.</summary>
        private const float HighlightZ = -0.3f;

        private static readonly Color HoverFill = new Color(1f, 1f, 1f, 0.20f);
        private static readonly Color HoverEdge = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>The four corners of the mark, reused: this is drawn on every pass that has a pointer in
        /// it, and the outline API takes an array.</summary>
        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>The stroke in progress - one <see cref="MapEdit.Session"/>, hence one undo entry,
        /// between the press that began it and its release. A drag that began elsewhere - on the
        /// overlay, on a handle - is not a stroke and must not paint the map.</summary>
        private static MapEdit.Session _stroke;

        /// <summary>Whether the stroke in progress erases. Latched where the stroke began rather than read
        /// per event, so letting go of Shift in the middle of a drag does not turn the rest of it into the
        /// other gesture - and so the undo entry's name matches everything it undoes.</summary>
        private static bool _erasing;

        static MapSceneEditor()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>The brush in hand. Session state rather than a field, because a script reload rebuilds
        /// every static in this class: saving a file should not put the ground brush back in a hand that
        /// was holding the water brush.</summary>
        private static MapBrush Brush
        {
            get
            {
                var stored = (MapBrush)SessionState.GetInt(BrushKey, (int)MapBrush.Ground);
                return System.Array.IndexOf(MapPainting.Brushes, stored) >= 0 ? stored : MapBrush.Ground;
            }
            set => SessionState.SetInt(BrushKey, (int)value);
        }

        /// <summary>The mineral the ore brush paints. Read back through <c>IsShape</c> for the same reason
        /// the brush is read back through the array: session state outlives a recompile, and a shape the
        /// enum no longer has would be a brush that refuses to paint without saying why.</summary>
        private static ShapeType Ore
        {
            get
            {
                var stored = (ShapeType)SessionState.GetInt(OreKey, (int)ShapeType.Circle);
                return stored.IsShape() ? stored : ShapeType.Circle;
            }
            set => SessionState.SetInt(OreKey, (int)value);
        }

        [MenuItem("FACET/Edit Map in Scene", priority = 32)]
        public static void EditInScene()
        {
            // Whatever is already being edited, then whatever is selected, then a new map: the one command
            // that means "let me paint a map" has to work when no map has been made yet.
            MapAsset asset = MapEditTarget.Current;
            if (asset == null) asset = Selection.activeObject as MapAsset;
            if (asset == null) asset = MapAssets.New();

            Selection.activeObject = asset;
            MapEditTarget.EditInScene(asset);

            // The overlay and the map are the Scene view's, and nothing has asked it to redraw: this is the
            // command arriving from a menu, which no Scene view sees.
            SceneView.RepaintAll();
        }

        [MenuItem("FACET/Edit Map in Scene", true)]
        private static bool CanEditInScene() => !Application.isPlaying;

        [MenuItem("FACET/Stop Editing Map", priority = 33)]
        public static void StopEditing()
        {
            MapEditTarget.StopEditingScene();
            SceneView.RepaintAll();
        }

        [MenuItem("FACET/Stop Editing Map", true)]
        private static bool CanStopEditing() => MapEditTarget.SceneMap != null;

        private static void OnSceneGui(SceneView view)
        {
            Event current = Event.current;

            // Nothing here is the game's. While it runs, the driver's own views are drawing this map and
            // the brush would be writing an asset that the running run has already read.
            if (current == null || Application.isPlaying)
            {
                EndStroke();
                return;
            }

            MapAsset asset = MapEditTarget.SceneMap;
            if (asset == null)
            {
                EndStroke();
                return;
            }

            DrawOverlay(view, asset);

            // Escape first, so it answers in every state the brush can be in - including the two below,
            // where it is the only thing left that can.
            HandleKeyboard(current);

            // The preview draws nothing through a camera that is not orthographic, so painting into one
            // would be painting blind. The overlay says which button fixes it.
            if (!view.orthographic)
            {
                EndStroke();
                return;
            }

            // A view tool is the view's own: the hand and the orbit win over the brush, which is also a way
            // out of a brush that is in the way.
            if (Tools.viewTool != ViewTool.None)
            {
                EndStroke();
                return;
            }

            HandleMouse(asset, current);
        }

        /// <summary>
        /// Escape, which is what a hand holding one thing expects it to mean: with a stroke running it ends
        /// the stroke - what has been painted stays, and one Ctrl+Z takes it back - and with none it gives
        /// the Scene view back the way it was found.
        /// </summary>
        private static void HandleKeyboard(Event current)
        {
            if (current.type != EventType.KeyDown || current.keyCode != KeyCode.Escape) return;
            if (current.alt || current.control || current.command || current.shift) return;

            if (_stroke != null) EndStroke();
            else MapEditTarget.StopEditingScene();

            current.Use();
        }

        /// <summary>
        /// The mouse, once the overlay has been drawn. The mark over the pointer's tile is drawn whatever
        /// the pointer is doing, because it is the only thing that says where a stroke would land; the
        /// stroke itself is only begun by a press that landed on the map with no modifier on it.
        /// </summary>
        private static void HandleMouse(MapAsset asset, Event current)
        {
            bool over = TryCell(current.mousePosition, out Int2 cell) && Inside(asset, cell);

            if (!over)
            {
                // Nothing is marked and nothing is painted while the pointer is off the map, and a drag
                // that wanders back on carries on: a brush is a brush. What ends a stroke is letting the
                // button go, or taking the pointer out of the Scene view altogether - the second one
                // because there is no longer a drag to come back with.
                if (current.type == EventType.MouseUp || current.type == EventType.MouseLeaveWindow)
                    EndStroke();
                return;
            }

            DrawHighlight(cell);

            // Alt is the view's own. A brush that painted under an orbit would paint every time the camera
            // was moved, which is the one gesture a map editor cannot take away.
            if (current.alt) return;

            // The brush takes the click, so that the Scene view cannot read a click on the map as a click
            // on the world behind it and drop the selection the map is being edited from. It is asked for
            // on the layout pass of the frame the event belongs to - the pass every mouse event is preceded
            // by - so that the events after it are the brush's, and never while Alt is down.
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                _erasing = current.shift;

                // One entry for the whole stroke: the session records once, here on the press, and the
                // cells below write through it - see the class comment. Which button modifier means
                // "erase" is this surface's own contract (Shift here, the right button in the window -
                // the right button belongs to the camera while a map is being painted).
                _stroke = new MapEdit.Session(asset, _erasing ? "Erase map" : "Paint map");
                Paint(cell);
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && _stroke != null && current.button == 0)
            {
                Paint(cell);
                current.Use();
            }
            else if (current.type == EventType.MouseUp)
            {
                EndStroke();
            }
        }

        /// <summary>One cell of the stroke in hand, through the session and the brush both painting
        /// surfaces share: the record and the dirty flag are the session's, written once for the edit,
        /// and what a brush writes is <see cref="MapPainting"/>'s answer - so the Scene view and the
        /// window cannot come out meaning different things by a rock.</summary>
        private static void Paint(Int2 cell)
        {
            _stroke?.Paint(cell.X, cell.Y, Brush, Ore, _erasing);
        }

        /// <summary>Close the stroke in progress, if there is one. What has been painted stays - one
        /// Ctrl+Z takes the whole drag back - and the asset is already dirty from the press.</summary>
        private static void EndStroke()
        {
            _stroke?.Dispose();
            _stroke = null;
        }

        /// <summary>
        /// The tile under a point in the Scene view: the view's ray met with the plane the game draws the
        /// map in. One piece of arithmetic for every camera, and false in the two cases where it has no
        /// answer - a view edge-on to the map, and a map behind the camera.
        /// </summary>
        private static bool TryCell(Vector2 guiPoint, out Int2 cell)
        {
            cell = default;

            Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
            if (Mathf.Approximately(ray.direction.z, 0f)) return false;

            float distance = -ray.origin.z / ray.direction.z;
            if (distance < 0f) return false;    // the map is behind the camera

            Vector3 world = ray.origin + ray.direction * distance;
            cell = new Int2(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));
            return true;
        }

        /// <summary>Whether a tile is one of the map's own. The brush is not offered the world past the
        /// map's edge: a tile there is a tile the asset could not hold. The answer is the asset's own
        /// <see cref="MapAsset.InBounds"/>, so the Scene view and the painting window cannot disagree
        /// about where the map ends.</summary>
        private static bool Inside(MapAsset asset, Int2 cell)
            => asset != null && asset.InBounds(cell.X, cell.Y);

        /// <summary>
        /// The tile the brush would paint. The Core brush marks the footprint it will take rather than one
        /// tile, because what that brush writes is the footprint's lower-left corner: a one-tile mark would
        /// say the Core is one tile big. During a stroke the mark is the tile itself, so that what a drag is
        /// painting is where the pointer is.
        /// </summary>
        private static void DrawHighlight(Int2 cell)
        {
            float size = Brush == MapBrush.Core && _stroke == null ? CoreState.Size : 1f;
            Outline(cell.X, cell.Y, size, size, HoverFill, HoverEdge);
        }

        /// <summary>One rectangle in the map's own coordinates, in front of it.</summary>
        private static void Outline(float x, float y, float width, float height, Color fill, Color edge)
        {
            Corners[0] = new Vector3(x, y, HighlightZ);
            Corners[1] = new Vector3(x + width, y, HighlightZ);
            Corners[2] = new Vector3(x + width, y + height, HighlightZ);
            Corners[3] = new Vector3(x, y + height, HighlightZ);

            Handles.DrawSolidRectangleWithOutline(Corners, fill, edge);
        }

        /// <summary>
        /// The brush, stated: which map is being edited and what else can be done with it on the first row,
        /// the brushes with the mineral and the mouse contract on the second.
        ///
        /// It is drawn on every pass, like every IMGUI overlay, because a button is not a button if the code
        /// that draws it only runs on the passes that show it.
        /// </summary>
        private static void DrawOverlay(SceneView view, MapAsset asset)
        {
            Handles.BeginGUI();

            GUILayout.BeginArea(new Rect(PanelX, PanelY, PanelWidth, PanelHeight), EditorStyles.helpBox);

            GUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var picked = (MapAsset)EditorGUILayout.ObjectField(asset, typeof(MapAsset), false,
                GUILayout.Width(176f));
            if (EditorGUI.EndChangeCheck() && picked != null && picked != asset)
            {
                Selection.activeObject = picked;
                MapEditTarget.EditInScene(picked);
            }

            if (GUILayout.Button("New", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                MapEditTarget.EditInScene(MapAssets.New());
            }

            if (GUILayout.Button("Focus", EditorStyles.miniButton, GUILayout.Width(50f))) MapPreview.Focus();
            if (GUILayout.Button("Window", EditorStyles.miniButton, GUILayout.Width(60f))) MapEditorWindow.Open();

            // Saving is a button because the asset is the work: writing to disk on every painted tile would
            // be a disk hit per cell of a stroke.
            if (GUILayout.Button("Save", EditorStyles.miniButton, GUILayout.Width(46f))) AssetDatabase.SaveAssets();
            if (GUILayout.Button("Done", EditorStyles.miniButton, GUILayout.Width(46f))) MapEditTarget.StopEditingScene();

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(asset.Width + " x " + asset.Height + ", " +
                asset.SpawnPoints.Length + " entry points", EditorStyles.miniLabel, GUILayout.Width(170f));

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            for (int i = 0; i < MapPainting.Brushes.Length; i++)
            {
                MapBrush brush = MapPainting.Brushes[i];
                if (GUILayout.Toggle(Brush == brush, MapPainting.Label(brush), EditorStyles.miniButton,
                        GUILayout.Width(52f)))
                {
                    Brush = brush;
                }
            }

            // The mineral only means something while the ore brush is in hand, so it appears with it.
            if (Brush == MapBrush.Ore)
                Ore = (ShapeType)EditorGUILayout.EnumPopup(MapPainting.MineralLabel, Ore,
                    MapPainting.ShapeChoices, false,
                    GUILayout.Width(150f));

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(Hint(view), EditorStyles.miniLabel, GUILayout.Width(240f));

            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            Handles.EndGUI();
        }

        /// <summary>
        /// What the overlay says about the brush: the mouse contract while there is nothing in the way of
        /// it, and the one thing to do about it when there is.
        /// </summary>
        private static string Hint(SceneView view)
        {
            if (!view.orthographic) return "the map draws in 2D only - press Focus";
            if (Tools.viewTool != ViewTool.None) return "a view tool is in hand - the brush is off";

            return "L-drag paints, Shift+L-drag erases, Esc leaves";
        }
    }
}
