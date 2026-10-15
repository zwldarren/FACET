using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The painting surface for a <see cref="MapAsset"/>: the ground, the veins in it, where the Core stands,
    /// the doors a wave walks in at, the map's size and its wave table - with the map's own validation kept
    /// beside it, because the mistakes a map makes do not throw, they leave a wave that stands still.
    ///
    /// Two rules run through the whole window: it keeps no copy of the map (every cell, field and problem is
    /// read off the asset in the pass that draws it, so nothing can drift and nothing has to be written back),
    /// and every edit goes through Undo and marks the asset dirty, so a stroke is one Ctrl+Z away and Save is
    /// the only thing that touches the disk.
    ///
    /// The one piece of machinery worth knowing: the rectangle the cells are drawn in is worked out in
    /// <em>every</em> pass and kept in fields, not only while painting. A mouse event arrives in a pass of its
    /// own, and a rect that existed only during Repaint would leave that pass with nothing to hit-test - the
    /// classic way a custom grid stops answering the mouse.
    ///
    /// The window is one type split by concern: this fragment is the shell - the target picker, the toolbar and
    /// the pass that draws them. The grid's geometry and painting live in <c>MapEditorWindow.Grid</c>, the side
    /// panel - size, Core, legend and validation - in <c>MapEditorWindow.Panel</c>, and the wave table in
    /// <c>MapEditorWindow.Waves</c>.
    /// </summary>
    public sealed partial class MapEditorWindow : EditorWindow
    {
        private const string WindowTitle = "FACET Map Editor";

        /// <summary>The side panel's width. Fixed rather than proportional: the grid is the thing that resizes
        /// with the window - that is what the fit arithmetic is for - and a panel that resized too would slide
        /// every field out from under the pointer as the window is dragged.</summary>
        private const float PanelWidth = 340f;

        /// <summary>The preview toggle's label. A GUIContent for its tooltip: the preview draws at the map's
        /// own coordinates, so "on but nothing in sight" is a state a designer can land in, and the tooltip is
        /// where the button beside it is explained.</summary>
        private static readonly GUIContent PreviewLabel = new GUIContent("Preview",
            "Draw this map in the Scene view with the game's own views, while painting it and without " +
            "Play mode. Press Focus to put the Scene view on the map.");

        /// <summary>Serialized so a script recompile does not forget which map the window was on; it is the
        /// picker's value, not a copy of anything the map holds.</summary>
        [SerializeField] private MapAsset _asset;

        /// <summary>Whether the map is drawn in the Scene view while it is painted. Serialized like the picker
        /// above, so a recompile does not quietly turn the preview off - the preview's own objects do not
        /// survive one, and this field is what says they should be built again.</summary>
        [SerializeField] private bool _preview;

        private MapBrush _brush = MapBrush.Ground;
        private Quad _ore = Quad.Circle;

        /// <summary>The zoom the toolbar's slider holds. Named by the fit arithmetic, not a pixel size:
        /// <see cref="GridFrame.Layout"/> multiplies the view's own fit by this.</summary>
        private float _zoom = 1f;

        private const float MinZoom = 0.25f;
        private const float MaxZoom = 8f;

        private void OnEnable()
        {
            // Opening the window with a map selected should edit that map: the Project window is where a
            // designer finds one, and the empty state is for when nothing is selected at all.
            if (_asset == null) _asset = Selection.activeObject as MapAsset;

            // A grid needs room to be painted in. A window small enough to collapse it would stop taking
            // clicks without saying why, which reads as a broken tool rather than as a small window.
            minSize = new Vector2(720f, 460f);
        }

        /// <summary>
        /// Closing the window takes the window's own request for a preview with it. It does not take the
        /// preview down: the Scene view's editor may be showing the same map, and which of the two the Scene
        /// view is answering is <see cref="MapEditTarget"/>'s to decide, not this window's.
        /// </summary>
        private void OnDisable() => MapEditTarget.FromWindow(null);

        /// <summary>
        /// Say what this window is asking for, in one place: the map it is pointed at while the toggle is on,
        /// and nothing at all when it is off.
        ///
        /// The window is also where a preview is asked for again - a script reload destroys the preview's
        /// objects, and a window whose fields survived it is the only thing that knows there was one. Saying it
        /// here rather than in each path that can change the toggle or the map keeps the preview from needing a
        /// line on every one of them. It is a request and not a decision: which map the Scene view ends up
        /// drawing is <see cref="MapEditTarget"/>'s.
        /// </summary>
        private void SyncPreview()
        {
            MapEditTarget.FromWindow(_preview ? _asset : null);
        }

        [MenuItem("FACET/Map Editor", priority = 30)]
        public static void Open()
        {
            GetWindow<MapEditorWindow>(WindowTitle);
        }

        private void OnGUI()
        {
            DrawTarget();

            if (_asset != null)
            {
                DrawToolbar(_asset);

                EditorGUILayout.BeginHorizontal();
                DrawGrid(_asset);
                EditorGUILayout.BeginVertical(GUILayout.Width(PanelWidth));
                DrawPanel(_asset);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }

            // Last, because the preview toggle is drawn above with everything else: this is the pass
            // that has just read it, and the map it points at.
            SyncPreview();
        }

        /// <summary>
        /// The asset the window is editing, and the one way to make a new one. Everything else on screen is
        /// drawn from whatever this field holds, so pointing it at another map is the whole of "switching maps".
        /// </summary>
        private void DrawTarget()
        {
            EditorGUILayout.BeginHorizontal();
            _asset = (MapAsset)EditorGUILayout.ObjectField("Map asset", _asset, typeof(MapAsset), false);
            if (GUILayout.Button("New Map", GUILayout.Width(80f))) _asset = MapAssets.New();
            EditorGUILayout.EndHorizontal();

            if (_asset == null)
            {
                EditorGUILayout.HelpBox(
                    "No map asset is selected. Pick one above, or make a new one: a new map starts with no " +
                    "doors and no waves, and the validation panel says what it still needs.",
                    MessageType.Info);
            }
        }

        /// <summary>The brushes, then the settings that go with them, the scene preview, and the two actions
        /// that write.</summary>
        private void DrawToolbar(MapAsset asset)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < MapPainting.Brushes.Length; i++)
            {
                MapBrush brush = MapPainting.Brushes[i];
                if (GUILayout.Toggle(_brush == brush, MapPainting.Label(brush), EditorStyles.miniButton, GUILayout.Width(60f)))
                    _brush = brush;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Left-drag paints, right-drag erases.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            // The mineral only means something while the ore brush is in hand, so it appears with it.
            if (_brush == MapBrush.Ore)
                _ore = (Quad)EditorGUILayout.EnumPopup(MapPainting.MineralLabel, _ore,
                    MapPainting.MineralChoices, false,
                    GUILayout.Width(180f));

            _zoom = EditorGUILayout.Slider("Zoom", _zoom, MinZoom, MaxZoom, GUILayout.Width(240f));
            if (GUILayout.Button("Fit", EditorStyles.miniButton, GUILayout.Width(40f))) _zoom = 1f;

            GUILayout.FlexibleSpace();

            // The scene preview: the map drawn by the game's own views, so it can be judged at its real
            // size and in its real colours without pressing Play. Focus is part of it because the preview
            // draws at the map's own coordinates - see MapPreview - and is disabled when there is nothing
            // to focus on.
            _preview = GUILayout.Toggle(_preview, PreviewLabel, EditorStyles.miniButton, GUILayout.Width(70f));
            EditorGUI.BeginDisabledGroup(!_preview);
            if (GUILayout.Button("Focus", EditorStyles.miniButton, GUILayout.Width(52f))) MapPreview.Focus();
            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();

            // Only the ground brushes have a whole-grid meaning: a grid of doors or of Core cells is not
            // something a map can hold.
            EditorGUI.BeginDisabledGroup(!MapPainting.IsGround(_brush));
            if (GUILayout.Button("Fill map", GUILayout.Width(80f))) FillMap(asset);
            EditorGUI.EndDisabledGroup();

            // Saving is a button because the asset is the work: writing to disk on every painted tile
            // would be a disk hit per cell of a drag.
            if (GUILayout.Button("Save", GUILayout.Width(60f))) AssetDatabase.SaveAssets();

            EditorGUILayout.EndHorizontal();
        }

        private void FillMap(MapAsset asset)
        {
            // The strokes of one sweep are one edit: the session holds the single undo entry, and
            // MapPainting.Fill holds the single batch of writes underneath it.
            using (var edit = new MapEdit.Session(asset, "Fill map"))
                edit.Fill(_brush, _ore);
        }
    }
}
