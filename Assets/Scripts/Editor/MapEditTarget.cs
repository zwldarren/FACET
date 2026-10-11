using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The one answer to "which map is the Scene view showing", and the one thing that turns the drawing
    /// of it on and off.
    ///
    /// It exists because two surfaces can ask for a map to be drawn - the painting window's Preview
    /// toggle and the Scene view's own editor - and a preview with two owners is a preview that is left
    /// on by whichever one was closed last, or turned off by one while the other is still painting into
    /// it. So neither owner touches <see cref="MapPreview"/>: they say what they are editing, and this
    /// decides what is drawn from all of it. The Scene view's editor outranks the window's toggle, because
    /// it is the surface whose whole subject is the map on screen, while the window may merely be pointed
    /// at one while the designer looks elsewhere.
    ///
    /// The second thing it owns is the camera, and that is not a detail. A preview draws at the map's own
    /// coordinates - tile coordinates are world coordinates, in the Editor as much as in game - which is
    /// nowhere near wherever the Scene view was left. A designer who asks to edit a map and is shown an
    /// empty patch of the world has been shown a tool that does not work, so the map is framed when it
    /// becomes the one being edited, once per map rather than once per call: the camera belongs to the
    /// designer, and a preview that re-framed itself on every repaint would be unusable.
    /// </summary>
    [InitializeOnLoad]
    internal static class MapEditTarget
    {
        /// <summary>What the painting window's Preview toggle asked for, or null for its off state.</summary>
        private static MapAsset _window;

        /// <summary>What the Scene view's own editor is editing, or null when it is not editing a map.</summary>
        private static MapAsset _scene;

        /// <summary>The map the Scene view was last put on, so that framing happens once per map and not
        /// once per request. Cleared when nothing is being edited, because coming back to a map after
        /// leaving it is a new look at it and wants the camera again.</summary>
        private static MapAsset _framed;

        static MapEditTarget()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>The map the Scene view should be drawing, or null when it should be drawing none.</summary>
        public static MapAsset Current => _scene != null ? _scene : _window;

        /// <summary>The map the Scene view's brush writes to, or null when there is no brush in hand. It is
        /// not the same question as <see cref="Current"/>: a window may show a map nobody is editing.</summary>
        public static MapAsset SceneMap => _scene;

        /// <summary>Edit this map in the Scene view. The Scene view's own way in, and the way to switch
        /// maps once inside.</summary>
        public static void EditInScene(MapAsset asset)
        {
            _scene = asset;
            Reconcile();
        }

        /// <summary>Give the Scene view back. The map stays drawn if the window's toggle is still asking
        /// for it, which is the window's business and not this one's to guess at.</summary>
        public static void StopEditingScene()
        {
            _scene = null;
            Reconcile();
        }

        /// <summary>Show this map in the Scene view for as long as the window's Preview toggle is on. Null
        /// is that toggle being turned off, or the window being closed.</summary>
        public static void FromWindow(MapAsset asset)
        {
            _window = asset;
            Reconcile();
        }

        /// <summary>
        /// Bring the drawing up to whatever is being edited. Safe to call whenever anything may have
        /// changed: showing a map that is already showing costs two reference comparisons.
        /// </summary>
        private static void Reconcile()
        {
            // While the game runs the driver builds its own views of the same map, and the preview stands
            // down for the whole of it. Nothing here may frame the camera either: it would be the editor
            // taking the view away from whoever is playing.
            if (Application.isPlaying) return;

            MapAsset wanted = Current;

            if (wanted == null)
            {
                _framed = null;
                MapPreview.Hide();
                return;
            }

            MapPreview.Show(wanted);

            if (wanted != _framed)
            {
                _framed = wanted;
                MapPreview.Focus();
            }
        }

        /// <summary>
        /// Coming back out of Play mode is the one moment a map that was being edited needs to be pointed
        /// at again: the preview's objects are switched off for the run rather than destroyed, and the
        /// repaint they need on the way out is not something this class can wait for. Entering Play is
        /// answered by doing nothing at all - see Reconcile.
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) Reconcile();
        }
    }
}
