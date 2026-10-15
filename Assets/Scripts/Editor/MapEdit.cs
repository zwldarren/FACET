using System;
using Facet.Core;
using Facet.Game;
using UnityEditor;

namespace Facet.EditorTools
{
    /// <summary>
    /// A map-edit transaction: the one place the Editor remembers the ladder every caller used to climb by
    /// itself - <c>Undo.RecordObject</c> once, before the first write; <c>EditorUtility.SetDirty</c> once, for
    /// the whole edit; and the structural list writes handed to the asset's own operations.
    ///
    /// The two stroke surfaces (the painting window's grid, the Scene view's brush) hold one session between the
    /// press that opens a stroke and the release that ends it, which is what makes a drag one undo entry rather
    /// than one per tile. Single edits (an add-wave click, a committed field) open and close one session inside a
    /// <c>using</c>. What differs between surfaces stays on the surface - which button means "erase" is each
    /// one's contract with its hand - but the bookkeeping has one owner.
    ///
    /// Recording at open rather than at first change is deliberate: the snapshot must precede the mutation (a
    /// wave row is a class the designer types into directly), and a stroke cannot know whether the first cell
    /// will change anything. The asset's structural operations still answer "changed", so a caller that wants to
    /// know - a test, or a future "did this edit do anything?" - asks them, not the session.
    /// </summary>
    public static class MapEdit
    {
        public sealed class Session : IDisposable
        {
            /// <summary>The map this session edits. The surfaces keep their own reference for drawing; this one is
            /// here so operations read as what they act on.</summary>
            public readonly MapAsset Asset;

            /// <summary>Open the transaction: record the asset for Undo under <paramref name="undoName"/>
            /// and mark it dirty for the save pass - once, for everything the session writes.</summary>
            public Session(MapAsset asset, string undoName)
            {
                Asset = asset ?? throw new ArgumentNullException(nameof(asset));
                if (undoName == null) throw new ArgumentNullException(nameof(undoName));

                Undo.RecordObject(Asset, undoName);
                EditorUtility.SetDirty(Asset);
            }

            // ------------------------------------------------------------- the strokes
            // What a brush writes stays MapPainting's answer; the session only says when it is one edit.

            /// <summary>One cell of the active brush - or, with <paramref name="erase"/> set, "nothing here".
            /// Inside a stroke session, so a whole drag is still one undo entry.</summary>
            public void Paint(int x, int y, MapBrush brush, Quad ore, bool erase)
                => MapPainting.Stroke(Asset, brush, ore, x, y, erase);

            /// <summary>Fill the whole map with the active ground brush: the strokes of one sweep, recorded as the
            /// one edit it is.</summary>
            public void Fill(MapBrush brush, Quad ore) => MapPainting.Fill(Asset, brush, ore);

            // ------------------------------------------------------- the structural edits
            // Each wraps the asset's own operation and hands back its "changed" answer.

            public bool Resize(int width, int height) => Asset.SetSize(width, height);
            public bool SetCore(int x, int y) => Asset.SetCore(x, y);
            public bool CentreCore() => Asset.CentreCore();
            public bool AddDoor(int x, int y) => Asset.TryAddDoor(x, y);
            public bool RemoveDoor(int x, int y) => Asset.TryRemoveDoor(x, y);
            public bool AddWave() => Asset.AddWave();
            public bool RemoveWave(int index) => Asset.RemoveWave(index);
            public bool AddGroup(int waveIndex) => Asset.AddWaveGroup(waveIndex);
            public bool RemoveGroup(int waveIndex, int groupIndex) => Asset.RemoveWaveGroup(waveIndex, groupIndex);

            /// <summary>Close the transaction. The record and the dirty were paid at the open, so this says only
            /// that the edit is over - a stroke surface closes on its release, and a <c>using</c> closes what its
            /// body already did.</summary>
            public void Dispose()
            {
                // Nothing to unwind: the undo snapshot and the dirty flag are open-side bookkeeping.
            }
        }
    }
}
