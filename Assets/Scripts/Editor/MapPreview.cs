using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// Draws a map in the Scene view with the game's own views, while it is being painted and with no
    /// Play mode: the ground, the map's own rock and water with their rims, the veins, the Core and the
    /// wave entry points, in the palette the game will draw them in.
    ///
    /// It exists because the painting window can only say what a tile <em>is</em> - its tones are the
    /// editing key, and its legend is the only thing that says what they mean. The look of the game is the
    /// view layer's, so the only way to see that look before pressing Play is to run the view layer: the
    /// preview builds what <see cref="SimulationDriver"/> builds for a map - a <see cref="SimWorld"/> from
    /// the asset's own <see cref="MapAsset.ToCore"/>, the same views, the same palette - and hands them one
    /// frame per Scene view repaint. Nothing here decides how a map looks; it decides when a frame is
    /// asked for.
    ///
    /// Four things are worth knowing before reading it.
    ///
    /// <b>It waits for a repaint.</b> A <see cref="MeshView"/> rebuilds its mesh when it wants to, and the
    /// frames arrive from <c>SceneView.duringSceneGui</c>, which is also the only moment there is a camera
    /// to measure a screen pixel against - so the preview's outlines are as many pixels wide as the game's
    /// would be at the zoom the designer is looking at, and a map seen from far away looks like a map seen
    /// from far away in game.
    ///
    /// <b>It watches the data.</b> A map is written by the painting window, by the Inspector and by undo,
    /// and no two of those are the same writer, so rather than depend on being told, the preview digests
    /// the fields it draws and rebuilds its world when the digest moves. An idle preview pays one integer
    /// comparison per editor tick, and a map edited in the Inspector - a path that has nowhere to say "I
    /// changed" - still repaints.
    ///
    /// <b>It owns everything it makes, and saves none of it.</b> The objects are <c>DontSave</c>, so a
    /// preview can never be written into the scene it is drawn in, and a script reload - which drops the
    /// statics that own them without destroying them - is answered twice: they are taken down before the
    /// reload, and any that outlived a reload that ran without that hook are swept when the next preview
    /// starts. A <c>DontSave</c> preview is not merely unsaved: the Scene view keeps rendering it, hidden
    /// from the hierarchy, for as long as nobody can name it. The whole of its lifetime is <see cref="Show"/>
    /// and <see cref="Hide"/>.
    ///
    /// <b>And it stands down while the game runs.</b> The driver builds its own views the moment Play
    /// starts, at the same depths as these, so a preview left on would draw a second copy of the map
    /// underneath them - and merely switching it off is not enough, because these objects outlive the
    /// transition and a frame drawn while their meshes have gone is an exception. The Play-mode entry takes
    /// them down; the first repaint after it puts them back.
    /// </summary>
    internal static class MapPreview
    {
        /// <summary>Named for what it is, because it turns up in the Scene view's hierarchy beside the
        /// scene's own objects and is not one of them.</summary>
        private const string RootName = "FACET Map Preview (editor only)";

        /// <summary>How often the map is digested for changes. An editor tick is far more frequent than a
        /// map changes, and a digest is proportional to the map's size, so the watch runs at a rate a
        /// person can type at rather than at the editor's own rate - the preview is at worst a frame of a
        /// drag behind the brush, which is not a lag anyone can see.</summary>
        private const double WatchInterval = 1.0 / 30.0;

        private static GameObject _root;
        private static readonly List<MeshView> _views = new List<MeshView>();

        /// <summary>The map being previewed, or null when the preview is off. Everything else below is
        /// derived from it, which is why nothing else has to be cleared on the way in.</summary>
        private static MapAsset _asset;

        /// <summary>The world the views are pointed at, and the digest of the map it was built from. A
        /// null world means "nothing is built yet", so the two are one state.</summary>
        private static SimWorld _world;
        private static int _digest;

        /// <summary>A <see cref="SimWorld"/> reads its frame from one of these, exactly as it does in
        /// game - the preview just writes it itself instead of waiting for a driver.</summary>
        private static FrameClock _frames;

        /// <summary>When the digest was last taken, against <see cref="WatchInterval"/>.</summary>
        private static double _checkedAt;

        private static Palette _palette;

        /// <summary>Whether <see cref="_palette"/> is one this class made and therefore has to free. A
        /// driver's palette is the asset the game draws with and is never ours to destroy.</summary>
        private static bool _ownPalette;

        /// <summary>
        /// Ask for this map to be drawn in the Scene view, and keep asking: calling this with the map
        /// already showing changes nothing. A null map is the off switch, so the caller states what it
        /// wants shown rather than keeping a flag in step with this one.
        /// </summary>
        public static void Show(MapAsset asset)
        {
            if (asset == null)
            {
                Hide();
                return;
            }

            if (_asset == asset) return;

            Hide();

            // Whatever a previous domain left drawing - see SweepOrphans - is cleared before this
            // preview's own root is built beside it.
            SweepOrphans();

            _asset = asset;
            _frames = new FrameClock();

            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.update += OnEditorTick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Nothing is built yet: the world waits for the first repaint, which is also the first
            // moment there is a Scene view camera to build it to.
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Take the preview down and leave nothing behind. Safe to call when it is already down, which is
        /// what lets every caller state its wish rather than track this class's state.
        /// </summary>
        public static void Hide()
        {
            if (_asset == null && _root == null) return;

            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.update -= OnEditorTick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            _asset = null;
            _world = null;
            _digest = 0;

            StandDown();

            if (_ownPalette && _palette != null) Object.DestroyImmediate(_palette);
            _palette = null;
            _ownPalette = false;

            SceneView.RepaintAll();
        }

        /// <summary>
        /// Put the Scene view on the map. A preview draws at the map's own coordinates - tile coordinates
        /// are world coordinates here, in the Editor as much as in game - which is nowhere near wherever
        /// the camera was left, and a preview nobody can find reads as a preview that did not work.
        /// </summary>
        public static void Focus()
        {
            if (_asset == null) return;

            // The last active Scene view, or the first one there is: Focus is reachable from the painting
            // window, which may be the window that was last touched, and a focus button that does nothing
            // because of which window has focus would be a button that does nothing.
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null && SceneView.sceneViews.Count > 0) view = SceneView.sceneViews[0] as SceneView;
            if (view == null) return;

            // A map is flat and the preview measures its screen-space lines from an orthographic camera,
            // so focusing is also where the Scene view is put into the one mode the map can be read in.
            view.orthographic = true;
            view.Frame(new Bounds(new Vector3(_asset.Width * 0.5f, _asset.Height * 0.5f, 0f),
                new Vector3(_asset.Width, _asset.Height, 0f)), true);
        }

        /// <summary>
        /// The watch between repaints. A map edited in the Inspector has no code to call this, and undo
        /// writes serialized fields straight back, so the preview asks the data rather than its writers:
        /// when the digest of what is drawn has moved, the Scene view is asked to repaint, and the repaint
        /// is what rebuilds.
        /// </summary>
        private static void OnEditorTick()
        {
            // Nothing is drawn while the game runs, so nothing can have changed either - and the digest
            // is only ever advanced by a repaint, so watching here would ask for one every tick.
            if (_asset == null || Application.isPlaying) return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _checkedAt < WatchInterval) return;
            _checkedAt = now;

            if (_world != null && _asset.ContentHash() == _digest) return;

            SceneView.RepaintAll();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Taken down from the moment Play is entered, not once it has started: the driver builds its own
            // map in its Awake, and a frame drawn by both would flicker.
            bool playing = change == PlayModeStateChange.ExitingEditMode
                || change == PlayModeStateChange.EnteredPlayMode;

            if (playing)
            {
                StandDown();
                return;
            }

            // Coming back needs a frame, and no repaint is guaranteed on the way out of Play mode. The next
            // repaint finds nothing built and builds it again, from the map it is asked to show.
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Put the objects away and forget what was drawn from them, keeping the map and the watchers.
        ///
        /// This is a take-down and not a hide because a hidden preview only stops drawing for as long as
        /// Unity agrees it is hidden: these objects are <c>DontSave</c>, so a Play-mode transition with
        /// "Enter Play Mode Options" leaving the domain alone carries them into the game still switched on,
        /// and a preview drawing a frame while its own meshes have been unloaded underneath it is one exception
        /// per view. Destroying them costs one rebuild on the way back, which is the same cost as opening the
        /// preview on a map for the first time.
        /// </summary>
        private static void StandDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);

            _root = null;
            _views.Clear();
            _world = null;
            _digest = 0;
        }

        /// <summary>
        /// Take the preview down before the scripts are reloaded.
        ///
        /// A reload drops every static in this class - including the one that names the preview's own
        /// root - and it does not take the objects with them: they are <c>DontSave</c>, so the Scene view
        /// keeps rendering the meshes they were last built with. What is left is a preview nobody can name:
        /// hidden from the hierarchy, so it cannot be deleted by hand, and drawing a frozen picture of the
        /// map as it was before the recompile, under the preview of the map being edited now. Standing down
        /// first is the cheaper half of the answer, because it leaves nothing to find; the expensive half is
        /// <see cref="SweepOrphans"/>, for a domain that went away without this hook - one this class has
        /// only run since, and an Editor session that began before it.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void HookReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StandDown;
            AssemblyReloadEvents.beforeAssemblyReload += StandDown;
        }

        /// <summary>
        /// Destroy every root carrying this class's name that it does not own, so a preview that outlived
        /// its statics stops drawing instead of being drawn under the next one. Asked of
        /// <see cref="Resources.FindObjectsOfTypeAll{T}"/>, which is the one lookup that sees an object
        /// hidden from the hierarchy and not saved into any scene - the two things that make an orphan
        /// unfindable by name, by selection or by hand.
        /// </summary>
        private static void SweepOrphans()
        {
            foreach (GameObject found in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (found == _root || found.name != RootName) continue;
                if ((found.hideFlags & HideFlags.DontSave) == 0) continue;   // not ours: a scene's own object

                Object.DestroyImmediate(found);
            }
        }

        private static void OnSceneGui(SceneView view)
        {
            // Unity draws a Scene view many times over for one picture - a layout pass, every mouse move -
            // and only the repaint pass shows anything, so only that one is answered.
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            // During Play mode there is nothing to draw: the preview took its objects down on the way in and
            // the game's own views are drawing this map.
            if (Application.isPlaying) return;

            if (!Synchronize()) return;

            DrawFrame(view.camera);
        }

        /// <summary>
        /// Bring what is drawn up to the asset, and say whether there is anything to draw. The world is
        /// built on the first call and rebuilt whenever the map's data has moved since - the same
        /// construction the game makes, from the same conversion, so a map that previews one way cannot
        /// load another.
        /// </summary>
        private static bool Synchronize()
        {
            if (_asset == null) return false;

            // One digest of exactly what draws, asked of the asset's canonical walk - the same walk
            // the painting window paints through, so the picture and this comparison cannot disagree
            // about a cell. It is asked before the conversion but they no longer trade side effects:
            // ToCore and ContentHash are both reads, and a half-edited asset converts honestly.
            int digest = _asset.ContentHash();
            if (_world != null && digest == _digest) return true;

            MapDefinition map = _asset.ToCore();
            _digest = digest;
            _world = new SimWorld(map, new SimConfig(), ContentDatabase.Default);

            if (_root == null) Build();
            else for (int i = 0; i < _views.Count; i++) _views[i].Rebind(_world);

            return true;
        }

        /// <summary>
        /// The views a map's own data can put something in, and no others: the ground, the map's rock and
        /// water with the rim where they meet other ground, the veins, the Core, and the wave entry points.
        /// Belts, machines, items, shots and enemies are all empty in a world nobody has played, and the
        /// cursor belongs to the Scene view's mouse rather than to the map.
        /// </summary>
        private static void Build()
        {
            _root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
            _views.Clear();

            Add<GridRenderer>("Grid");
            Add<TerrainRenderer>("Terrain");
            Add<OreRenderer>("Ore");
            Add<CoreView>("Core");
            Add<EnemyRenderer>("Enemies");
        }

        private static void Add<T>(string viewName) where T : MeshView
        {
            var go = new GameObject(viewName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_root.transform, false);

            T view = go.AddComponent<T>();
            view.Initialize(_world, _frames, Palette());
            _views.Add(view);
        }

        /// <summary>
        /// One frame per view, measured in the Scene view's own pixels. The game derives that number from
        /// the Game view's height and this cannot, so the honest reading is the camera being drawn
        /// through: every screen-space constant in the preview - the outline, the rim, the grid line - is
        /// the width it would be at this zoom. The Game view draws these same objects in edit mode and
        /// therefore shows them at the Scene view's pixel size, which is the one place the preview is not
        /// exactly the game.
        ///
        /// Nothing is drawn through a perspective camera. A flat map seen from an angle has no single
        /// pixel size across it, and drawing at a guessed one would show rims of the wrong width - which
        /// is exactly the thing the preview exists to get right.
        /// </summary>
        private static void DrawFrame(Camera camera)
        {
            if (camera == null || !camera.orthographic || camera.pixelHeight <= 0) return;

            _frames.Frame = new ViewFrame(1f, 2f * camera.orthographicSize / camera.pixelHeight,
                new Int2(-1, -1));

            for (int i = 0; i < _views.Count; i++) _views[i].Draw(_frames.Frame);
        }

        /// <summary>
        /// The palette the preview draws with: the one the scene's driver will draw with, because a
        /// preview in other colours would be a preview of another game. The code defaults stand in when
        /// there is no driver to ask - the same fallback the driver itself makes.
        /// </summary>
        private static Palette Palette()
        {
            if (_palette != null) return _palette;

            SimulationDriver driver = Object.FindFirstObjectByType<SimulationDriver>();
            if (driver != null && driver.Colors != null) return _palette = driver.Colors;

            _ownPalette = true;
            return _palette = ScriptableObject.CreateInstance<Palette>();
        }
    }
}
