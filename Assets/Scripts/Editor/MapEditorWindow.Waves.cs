using System.Collections.Generic;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The wave table half of the <see cref="MapEditorWindow"/>: a foldout per wave and a plain block per group,
    /// and the add/remove operations behind its buttons - each one a <see cref="MapEdit.Session"/> around the
    /// asset's own structural edit, so the copy-append-reassign dance, the bounds and the Undo record live below
    /// this file and not in it.
    /// </summary>
    public sealed partial class MapEditorWindow
    {
        /// <summary>Whether each wave's foldout is open. The window's own state, not the map's: nothing in the
        /// asset says how a wave should be looked at.</summary>
        private readonly List<bool> _waveOpen = new List<bool>();

        /// <summary>
        /// The wave table, a foldout per wave and a plain block per group. Deliberately not a ReorderableList:
        /// this window has to build headlessly as well as by hand, and a widget with fewer moving parts has fewer
        /// ways to break in a batch build.
        ///
        /// Every button only records which row it wants changed; the change itself waits until the loops are
        /// done, so removing a wave cannot renumber the row the loop is about to draw.
        /// </summary>
        private void DrawWaveTable(MapAsset asset)
        {
            EditorGUILayout.LabelField("Waves", EditorStyles.boldLabel);

            // A group names its door by *position* in this list, and the runtime wraps a position past
            // the end, so how many there are is the number those fields read against.
            EditorGUILayout.LabelField(asset.SpawnPoints.Length + " entry points;", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("a group names one by position, and the runtime wraps it.", EditorStyles.miniLabel);

            SyncWaveFoldouts(asset.Waves.Length);

            int removeWave = -1;
            int addGroupTo = -1;
            int removeGroupFrom = -1;
            int removeGroup = -1;

            for (int w = 0; w < asset.Waves.Length; w++)
            {
                MapAsset.WaveRow wave = asset.Waves[w];
                int enemies = wave.Total;

                EditorGUILayout.BeginHorizontal();
                _waveOpen[w] = EditorGUILayout.Foldout(_waveOpen[w],
                    "Wave " + (w + 1) + " - " + enemies + (enemies == 1 ? " enemy" : " enemies"));
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(20f))) removeWave = w;
                EditorGUILayout.EndHorizontal();

                if (!_waveOpen[w]) continue;

                // A row the asset does not have. The conversion treats a missing row as an empty wave,
                // so the window names it rather than drawing nothing where a wave should be.
                if (wave == null)
                {
                    EditorGUILayout.HelpBox("Wave " + (w + 1) + " has no row. x removes it; the map reads " +
                        "it as an empty wave until it does.", MessageType.Warning);
                    continue;
                }

                EditorGUI.indentLevel++;
                wave.Intermission = FloatField(asset, "Intermission", wave.Intermission, "Edit wave");

                for (int g = 0; g < wave.Groups.Length; g++)
                {
                    MapAsset.SpawnGroupRow group = wave.Groups[g];

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    if (group == null)
                    {
                        EditorGUILayout.LabelField("Group " + (g + 1) + " has no row.", EditorStyles.miniLabel);
                    }
                    else
                    {
                        // The floors are the asset's own [Min] attributes: a group that spawns a negative
                        // number of enemies, or one every zero seconds, is not a state to write down.
                        group.Kind = EnemyKindField(asset, "Kind", group.Kind, "Edit wave");
                        group.Count = Mathf.Max(1, IntField(asset, "Count", group.Count, "Edit wave"));
                        group.SpawnPoint = IntField(asset, "Entry point", group.SpawnPoint, "Edit wave");
                        group.FirstDelay = FloatField(asset, "First delay", group.FirstDelay, "Edit wave");
                        group.Interval = Mathf.Max(0.01f, FloatField(asset, "Interval", group.Interval, "Edit wave"));
                    }

                    if (GUILayout.Button("Remove group", EditorStyles.miniButton))
                    {
                        removeGroupFrom = w;
                        removeGroup = g;
                    }

                    EditorGUILayout.EndVertical();
                }

                if (GUILayout.Button("+ Group", EditorStyles.miniButton)) addGroupTo = w;
                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button("+ Wave")) AddWave(asset);

            if (removeWave >= 0) RemoveWave(asset, removeWave);
            if (removeGroupFrom >= 0) RemoveGroup(asset, removeGroupFrom, removeGroup);
            if (addGroupTo >= 0) AddGroup(asset, addGroupTo);
        }

        /// <summary>Add an empty wave. How many enemies a wave should hold is the designer's answer, and the
        /// validation panel says the wave has none until it is given some. The append, the array identity and the
        /// bounds are the asset's own edit operation; the record-once and the dirty flag are the session's.</summary>
        private static void AddWave(MapAsset asset)
        {
            using (var edit = new MapEdit.Session(asset, "Add wave")) edit.AddWave();
        }

        private static void RemoveWave(MapAsset asset, int index)
        {
            using (var edit = new MapEdit.Session(asset, "Remove wave")) edit.RemoveWave(index);
        }

        /// <summary>Add a group with the row's own defaults, so a group dropped into a wave is a group the
        /// runtime can already spawn rather than one the designer has to fill in from nothing.</summary>
        private static void AddGroup(MapAsset asset, int waveIndex)
        {
            using (var edit = new MapEdit.Session(asset, "Add wave group")) edit.AddGroup(waveIndex);
        }

        private static void RemoveGroup(MapAsset asset, int waveIndex, int groupIndex)
        {
            using (var edit = new MapEdit.Session(asset, "Remove wave group"))
                edit.RemoveGroup(waveIndex, groupIndex);
        }

        /// <summary>
        /// Whether a wave is open is the window's own state - nothing in the map says how to look at it - so it
        /// is the one thing kept here rather than read back off the asset. Waves are only ever added and removed,
        /// so stretching the list keeps each open wave with its own row.
        /// </summary>
        private void SyncWaveFoldouts(int waves)
        {
            while (_waveOpen.Count < waves) _waveOpen.Add(true);
            while (_waveOpen.Count > waves) _waveOpen.RemoveAt(_waveOpen.Count - 1);
        }
    }
}
