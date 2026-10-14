using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The side panel half of the <see cref="MapEditorWindow"/>: everything the grid cannot say in colour -
    /// the map's size, where the Core stands, the legend that states the grid's tones, the map's own validation,
    /// and the fields through which an edit reaches the asset through Undo.
    /// </summary>
    public sealed partial class MapEditorWindow
    {
        private const string WidthFieldName = "MapEditorWidth";
        private const string HeightFieldName = "MapEditorHeight";

        private static readonly Color NoTone = new Color(0f, 0f, 0f, 0f);

        /// <summary>The green the panel draws its "playable" line in, tinted enough to read but not to bury the
        /// sentence on it.</summary>
        private static readonly Color PlayableTone = new Color(0.18f, 0.48f, 0.24f, 0.55f);

        /// <summary>The size the designer has typed and not yet applied. It is the one buffered value in the
        /// window, because a resize throws away everything past the new edge and a half-typed number must not do
        /// that.</summary>
        private int _editWidth = 1;
        private int _editHeight = 1;

        private Vector2 _panelScroll;

        /// <summary>The map's own problems as of the last repaint, and whether one has ever been computed. The
        /// flood MapValidation runs is the only expensive thing this panel does, and the window's OnGUI passes
        /// that draw nothing - layout, mouse moves - would run it several times between repaints, so the answer
        /// is computed where it is drawn: the repaint pass is the one that produces the pixels, and every change
        /// to the map's data repaints the window on its way to being seen, so the panel states the answer to
        /// what it shows.</summary>
        private List<MapProblem> _problems;
        private bool _problemsComputed;

        /// <summary>The side panel: everything the grid cannot say in colour.</summary>
        private void DrawPanel(MapAsset asset)
        {
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll, GUILayout.ExpandHeight(true));

            DrawMapFields(asset);
            DrawCoreField(asset);
            DrawLegend();
            DrawWaveTable(asset);
            DrawValidation(asset);

            EditorGUILayout.EndScrollView();
        }

        private void DrawMapFields(MapAsset asset)
        {
            EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);
            asset.Name = TextField(asset, "Name", asset.Name, "Rename map");
            asset.StartCircles = IntField(asset, "Start circles", asset.StartCircles, "Edit the map's stockpile");

            // Width and Height are the one pair of fields not written straight back. A resize re-lays the
            // terrain array and drops everything past the new edge, so a half-typed number must not do it; the
            // buffer is read back off the asset whenever neither field has focus, so the window never shows a
            // size the map does not have.
            if (GUI.GetNameOfFocusedControl() != WidthFieldName && GUI.GetNameOfFocusedControl() != HeightFieldName)
            {
                _editWidth = asset.Width;
                _editHeight = asset.Height;
            }

            GUI.SetNextControlName(WidthFieldName);
            _editWidth = EditorGUILayout.IntField("Width", _editWidth);
            GUI.SetNextControlName(HeightFieldName);
            _editHeight = EditorGUILayout.IntField("Height", _editHeight);

            if (GUILayout.Button("Apply size")) ApplySize(asset);
        }

        /// <summary>
        /// Resize on request, through a <see cref="MapEdit.Session"/> so the record and the dirty are the
        /// session's one pair. An Apply that would change nothing is not an edit: the same floor the asset
        /// applies to itself is read before the session opens, so a no-op Apply records nothing - which is also
        /// why the answer, not the click, marks the boundary.
        /// </summary>
        private void ApplySize(MapAsset asset)
        {
            int width = Mathf.Max(1, _editWidth);
            int height = Mathf.Max(1, _editHeight);
            if (width == asset.Width && height == asset.Height) return;

            using (var edit = new MapEdit.Session(asset, "Resize map"))
                edit.Resize(width, height);

            _editWidth = asset.Width;
            _editHeight = asset.Height;
        }

        /// <summary>Where the Core is, and the one button that puts it back in the middle.</summary>
        private void DrawCoreField(MapAsset asset)
        {
            EditorGUILayout.LabelField("Core", EditorStyles.boldLabel);

            bool centred = asset.CoreCell.x < 0 || asset.CoreCell.y < 0;
            string value = centred ? "centred, at " + asset.CoreFootprint : asset.CoreCell + ", lower-left";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Core cell", value, EditorStyles.miniLabel);

            // Disabled while it is already centred, which is also how the window says that it is.
            EditorGUI.BeginDisabledGroup(centred);
            if (GUILayout.Button("Centre", EditorStyles.miniButton, GUILayout.Width(56f)))
            {
                using (var edit = new MapEdit.Session(asset, "Centre the Core"))
                    edit.CentreCore();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// The colours the grid uses, stated. The tones are the only key to the ground kinds and to which mineral
        /// a vein carries, so the window has to say what they mean somewhere other than the map.
        /// </summary>
        private void DrawLegend()
        {
            EditorGUILayout.LabelField("Tones", EditorStyles.boldLabel);

            LegendRow("ground", GroundTone);
            LegendRow("rock", RockTone);
            LegendRow("water", WaterTone);
            LegendRow("void - not drawn", NoTone);
            LegendRow("circle vein", CircleTone);
            LegendRow("half-circle vein", HalfCircleTone);
            LegendRow("square vein", SquareTone);
            LegendRow("half-square vein", HalfSquareTone);
            LegendRow("core footprint", CoreTone);
            LegendRow("wave entry point", DoorTone);
        }

        /// <summary>One legend line. A tone with no alpha is drawn as nothing at all, which is the state the
        /// void's own row is describing.</summary>
        private static void LegendRow(string name, Color tone)
        {
            Rect row = EditorGUILayout.GetControlRect(false, 16f);
            var swatch = new Rect(row.x, row.y + 2f, 16f, 12f);
            if (tone.a > 0f) EditorGUI.DrawRect(swatch, tone);

            GUI.Label(new Rect(swatch.xMax + 6f, row.y, row.width - swatch.width - 6f, row.height), name);
        }

        private void DrawValidation(MapAsset asset)
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            // Computed where it is drawn - see the field comment for why the repaint pass is the only place the
            // flood has to run, and why first use pays for itself whether or not a repaint is what asked.
            if (!_problemsComputed || Event.current.type == EventType.Repaint)
            {
                _problems = asset.Validate();
                _problemsComputed = true;
            }

            List<MapProblem> problems = _problems;

            if (problems.Count == 0)
            {
                // A rect rather than a tinted HelpBox: HelpBox has no green, and recolouring a shared
                // EditorStyles instance in place would recolour every label the Editor draws with it.
                Rect row = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.DrawRect(row, PlayableTone);
                GUI.Label(row, "  No problems - this map is playable.", EditorStyles.boldLabel);
                return;
            }

            EditorGUILayout.LabelField(problems.Count + (problems.Count == 1 ? " problem" : " problems"),
                EditorStyles.miniLabel);

            for (int i = 0; i < problems.Count; i++)
            {
                MapProblem problem = problems[i];

                EditorGUILayout.BeginHorizontal();

                // The kind leads the sentence so a report can be read by eye the way it would be filtered
                // in code: the message already names the map and the tile.
                EditorGUILayout.HelpBox(problem.Kind + ": " + problem.Message, MessageType.Error);

                // Only a problem about one tile can be focused; (-1,-1) is the map as a whole, which no
                // scroll position can point at.
                if (problem.Cell.X >= 0 && problem.Cell.Y >= 0 &&
                    GUILayout.Button("Focus", EditorStyles.miniButton, GUILayout.Width(50f)))
                {
                    FocusCell(problem.Cell.X, problem.Cell.Y);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// One field whose edit reaches the asset through Undo.
        ///
        /// The widget buffers the value and the write happens once the change is known, because the asset has to
        /// be recorded <em>before</em> it is written: a wave row is a class, so the object a designer types into
        /// is already the asset's own, and recording after the assignment would record the change it exists to
        /// undo. Delaying the commit also makes a number one edit rather than one per keystroke. The
        /// record-commit shape lives in <see cref="CommitChange{T}"/>; these are the four widgets that reach it,
        /// no more.
        /// </summary>
        private static string TextField(MapAsset asset, string label, string value, string undo)
        {
            EditorGUI.BeginChangeCheck();
            string edited = EditorGUILayout.DelayedTextField(label, value);
            return CommitChange(asset, undo, EditorGUI.EndChangeCheck(), value, edited);
        }

        /// <inheritdoc cref="TextField"/>
        private static int IntField(MapAsset asset, string label, int value, string undo)
        {
            EditorGUI.BeginChangeCheck();
            int edited = EditorGUILayout.DelayedIntField(label, value);
            return CommitChange(asset, undo, EditorGUI.EndChangeCheck(), value, edited);
        }

        /// <inheritdoc cref="TextField"/>
        private static float FloatField(MapAsset asset, string label, float value, string undo)
        {
            EditorGUI.BeginChangeCheck();
            float edited = EditorGUILayout.DelayedFloatField(label, value);
            return CommitChange(asset, undo, EditorGUI.EndChangeCheck(), value, edited);
        }

        /// <inheritdoc cref="TextField"/>
        /// <summary>Named for what it offers: this is the wave group's popup over the enemy kinds, so the name
        /// says the kind it edits rather than a generic word for every popup in the window.</summary>
        private static EnemyKind EnemyKindField(MapAsset asset, string label, EnemyKind value, string undo)
        {
            EditorGUI.BeginChangeCheck();
            var edited = (EnemyKind)EditorGUILayout.EnumPopup(label, value);
            return CommitChange(asset, undo, EditorGUI.EndChangeCheck(), value, edited);
        }

        /// <summary>The one place an edit of a plain field is recorded and committed: the
        /// <see cref="MapEdit.Session"/> records the asset <em>before</em> the caller's assignment lands - a wave
        /// row is a class, so the object the designer typed into is already the asset's own, and a record after
        /// the assignment would record the change the entry exists to undo - and it dirties once, whatever the
        /// field. A field that did not reach the end of the widget change keeps the value it came in with.</summary>
        private static T CommitChange<T>(MapAsset asset, string undo, bool changed, T value, T edited)
        {
            if (!changed) return value;

            using (new MapEdit.Session(asset, undo)) { }
            return edited;
        }
    }
}
