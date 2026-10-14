using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>The bridge between Unity and the simulation: owns one <see cref="SimWorld"/> and
    /// advances it at a fixed <see cref="SimConfig.TickRate"/> Hz regardless of frame rate. The ONLY
    /// component a scene needs - it builds the views itself.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationDriver : MonoBehaviour
    {
        [Header("Simulation")]
        [Tooltip("Belt travel in tiles per second. A cell holds one item, so this is also items per second.")]
        [SerializeField] private float beltSpeedTilesPerSecond = 1f;
        [SerializeField] private float coreMaxHp = 100f;

        [Header("Look")]
        [Tooltip("Colours and screen-space thicknesses. Create one with Assets > Create > FACET > Palette.")]
        [SerializeField] private Palette palette;

        [Header("Content")]
        [Tooltip("Costs, turret/enemy stats and recipes. Create one with Assets > Create > FACET > Content " +
            "Database. Empty uses the shipped table (ContentDatabase.Default) unchanged.")]
        [SerializeField] private ContentDatabaseAsset content = null;

        [Header("Maps")]
        [Tooltip("The campaign this run walks: a Map Set asset naming the maps, in the order they are " +
            "played. Create one with Assets > Create > FACET > Map Set, and paint its maps with " +
            "FACET > Edit Map in Scene. The game cannot start without one.")]
        [SerializeField] private MapSet campaign = null;

        [Header("Progress")]
        [Tooltip("Which map of the campaign to start on. The rest of the campaign follows it: clear a map, " +
            "press N, and the next one is built without reloading the scene.")]
        [SerializeField] private int startMapIndex = 0;

        [Header("Views")]
        [Tooltip("Build the terrain / machine / belt / item / enemy / shot / core / cursor / HUD views at runtime.")]
        [SerializeField] private bool autoCreateViews = true;

        /// <summary>Hard cap on catch-up ticks per frame, so a long hitch cannot spiral.</summary>
        private const int MaxTicksPerFrame = 5;

        /// <summary>The simulation being played - one run, replaced when the campaign moves on.</summary>
        public SimWorld World { get; private set; }

        public Palette Colors => palette;

        /// <summary>Where the player is in the campaign; it outlives every world it builds, which is
        /// why it is not a field on <see cref="SimWorld"/>.</summary>
        public CampaignState Campaign { get; private set; }

        /// <summary>The progression this run walks: the maps the scene's map set names, in order, so
        /// which map is next is one answer for the driver, the campaign and the HUD.</summary>
        public MapCatalog Catalog { get; private set; }

        /// <summary>The content table this run was built with, from the asset or the shipped defaults;
        /// the HUD reads it so it prints the same table the tick runs on.</summary>
        public ContentDatabase Content { get; private set; }

        /// <summary>How far the renderer is between the previous and the current tick, 0..1.</summary>
        public float Alpha { get; private set; }

        /// <summary>Size of one screen pixel in world units, at the current resolution and zoom.</summary>
        public float WorldPerPixel { get; private set; }

        /// <summary>Tile under the cursor as of the last poll, or (-1,-1) when there is no cursor.</summary>
        public Int2 CursorCell { get; private set; }

        /// <summary>The frame every view is drawing right now. The driver is the only writer.</summary>
        public FrameClock Frames { get; private set; }

        private BuildInput _input;
        private Camera _camera;
        private float _accumulator;

        /// <summary>Every mesh view, gathered as built rather than looked up by name so a view added
        /// later cannot be forgotten when a map change points them at the new world.</summary>
        private readonly List<MeshView> _views = new List<MeshView>();

        /// <summary>The camera rig, whose pan bounds are the map's size and so have to follow it.</summary>
        private CameraRig _rig;

        /// <summary>The out-of-view locator, which needs the camera only the rig's owner knows; re-handed
        /// on every map change with the rig.</summary>
        private ThreatRadarView _radar;

        /// <summary>The HUD, for its panels' claim on the mouse. Null until the views are built (and
        /// for a driver that never built any).</summary>
        private HudView _hud;

        private void Awake()
        {
            // Tile coordinates ARE world coordinates: the views build in tile space and the cursor turns
            // a screen point straight into a tile, so this object must sit at the origin or everything
            // drawn drifts from everything clicked. Pinned in code because the symptom is a silent offset.
            transform.position = Vector3.zero;

            if (palette == null)
            {
                // Keeps an unwired scene runnable, and says so rather than drawing a null reference's colour.
                Debug.LogWarning("FACET: no Palette assigned - using code defaults. " +
                    "Assign one (Assets > Create > FACET > Palette) to tune colours and line widths.", this);
                palette = ScriptableObject.CreateInstance<Palette>();
                palette.hideFlags = HideFlags.DontSave;
            }

            // The campaign and the content every map is played with: the content table is converted once
            // here, so a map change costs one SimWorld. The catalog is built once from the wired map set;
            // a campaign with no maps is said out loud rather than started.
            Catalog = MapCatalog.From(campaign);
            if (Catalog == null)
            {
                Debug.LogError("FACET: no campaign - assign a Map Set (Assets > Create > FACET > Map Set) " +
                    "with at least one map in it. There is nothing to play without one.", this);
                enabled = false;
                return;
            }

            Campaign = new CampaignState(Catalog.Count, startMapIndex);
            Content = content != null ? content.ToCore() : ContentDatabase.Default;
            World = NewWorld(Catalog.At(Campaign.MapIndex));
            ReportMapProblems(World.Map);

            _input = new BuildInput();
            CursorCell = new Int2(-1, -1);

            _camera = Camera.main;
            if (_camera == null)
            {
                Debug.LogError("FACET: no Main Camera in the scene - the views cannot be positioned.", this);
                enabled = false;
                return;
            }

            if (!_camera.orthographic)
            {
                Debug.LogWarning("FACET: Main Camera must be orthographic for a 2D grid game - switching it.", this);
                _camera.orthographic = true;
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = palette.Background;

            UpdateWorldPerPixel();
            Frames = new FrameClock { Frame = new ViewFrame(1f, WorldPerPixel, new Int2(-1, -1)) };

            if (autoCreateViews) BuildViews();
        }

        private void Update()
        {
            // Unity can call Update on an instance whose Awake never completed: a script reload during
            // play restores the scene without re-awaking it. The driver stands down rather than throwing
            // once per frame, as its Awake's own no-camera bail already does.
            if (_input == null)
            {
                enabled = false;
                return;
            }

            UpdateWorldPerPixel();

            if (_input.ConsumePauseToggle())
            {
                World.Paused = !World.Paused;
                Debug.Log("FACET: " + (World.Paused ? "paused" : "resumed") + " at tick " + World.TickCount, this);
            }

            // Sample the devices once, then hand the same latched command to every tick this frame, so a
            // click between two ticks applies once, not once per catch-up tick. The HUD has first claim on
            // the mouse, so pressing a panel button cannot also drop a belt under it.
            _input.Sample(_camera, World, _hud != null && _hud.PointerOverHud);

            // N means "go on" in both run states: start the next wave mid-map, and carry on to the next map
            // once won. The world drops a post-run start-wave request, so this is the only reader here - and
            // the one-shot is consumed either way, so the new map does not spend the same press twice.
            if (World.Status == GameStatus.Won && _input.StartWaveRequested)
            {
                _input.ConsumeOneShots();
                if (Campaign.EnterNext()) LoadMap();
            }

            InputCommand cmd = _input.Snapshot();
            CursorCell = cmd.CursorCell;
            // Handed to the HUD for the cursor tooltip: the input latch owns the devices, so the HUD
            // cannot sample the tile itself, and reading is not building.
            if (_hud != null) _hud.SetCursor(CursorCell);

            _accumulator += Time.deltaTime;

            int ticks = 0;
            while (_accumulator >= SimConfig.TickDt && ticks < MaxTicksPerFrame)
            {
                _accumulator -= SimConfig.TickDt;

                World.Tick(cmd);
                _input.ConsumeOneShots();
                cmd = _input.Snapshot();
                ticks++;
            }

            // Drop the leftover backlog rather than trying to catch up forever.
            if (ticks >= MaxTicksPerFrame) _accumulator = 0f;

            // Recorded as it happens rather than on a keypress, so the count is right whatever got the
            // player here; idempotent, so polling every frame is the honest way to read an unreported edge.
            if (World.Status == GameStatus.Won) Campaign.MarkCleared();

            Alpha = World.Paused || World.Status != GameStatus.Playing
                ? 1f
                : Mathf.Clamp01(_accumulator / SimConfig.TickDt);

            // Publish the frame views draw in, written once after the tick loop and the WorldPerPixel
            // refresh, so every view reads the same value.
            Frames.Frame = new ViewFrame(Alpha, WorldPerPixel, CursorCell);
        }

        private void UpdateWorldPerPixel()
        {
            WorldPerPixel = Screen.height > 0
                ? (2f * _camera.orthographicSize) / Screen.height
                : 0.02f;
        }

        /// <summary>Swap in the map the campaign is now on, without reloading the scene.
        ///
        /// The world is <em>replaced</em> rather than reset: a world is one run, sized and fixed by its
        /// map, so a new map is a new world built from its own data with the same content table. The
        /// views rebind rather than rebuild, their meshes and sprites being the same objects either way.</summary>
        public void LoadMap()
        {
            World = NewWorld(Catalog.At(Campaign.MapIndex));
            ReportMapProblems(World.Map);

            // The frame the views are about to draw must describe the new world: no leftover tick backlog,
            // no cursor on a tile that may not exist.
            _accumulator = 0f;
            Alpha = 1f;
            CursorCell = new Int2(-1, -1);
            Frames.Frame = new ViewFrame(1f, WorldPerPixel, CursorCell);

            for (int i = 0; i < _views.Count; i++) _views[i].Rebind(World);
            // The run opens on the Core: the map can be taller than the screen, and the Core is what the
            // player defends and must find first.
            if (_rig != null) _rig.Initialize(World.TileGrid, World.Core.Center, Campaign.IsFirstMap);
            // The locator follows the rig: its Core marker points at the same focus point, and the tutorial
            // card is up on this map or it is not.
            if (_radar != null) _radar.InitializeLocator(_camera, Campaign.IsFirstMap);
            if (_hud != null)
                _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog);

            Debug.Log("FACET: loaded map " + (Campaign.MapIndex + 1) + " of " + Campaign.MapCount +
                " - " + World.Map.Name, this);
        }

        /// <summary>Say what is wrong with a map's own data, once, when it is loaded.
        ///
        /// A map is data, and data's mistakes do not throw: a ridge across a lane leaves a wave standing
        /// at its door, reading as a hung game. The rules are <see cref="MapValidation"/>'s, so this
        /// reports what the painting window shows and what the test suite refuses to ship.</summary>
        private void ReportMapProblems(MapDefinition map)
        {
            List<MapProblem> problems = MapValidation.Validate(map);
            if (problems.Count == 0) return;

            var report = new System.Text.StringBuilder();
            report.Append("FACET: ").Append(map.Name).Append(" has ").Append(problems.Count)
                .Append(problems.Count == 1 ? " map problem:" : " map problems:");

            for (int i = 0; i < problems.Count; i++) report.Append("\n  - ").Append(problems[i].Message);

            Debug.LogWarning(report.ToString(), this);
        }

        private SimWorld NewWorld(MapDefinition map)
        {
            return new SimWorld(map, new SimConfig
            {
                BeltSpeed = beltSpeedTilesPerSecond,
                CoreMaxHp = coreMaxHp,
            }, Content);
        }

        private void BuildViews()
        {
            AddMeshView<GridRenderer>("Grid");
            AddMeshView<TerrainRenderer>("Terrain");
            AddMeshView<OreRenderer>("Ore");
            AddMeshView<BeltRenderer>("Belts");
            AddMeshView<CoreView>("Core");
            AddMeshView<MachineRenderer>("Machines");
            AddMeshView<ItemRenderer>("Items");
            AddMeshView<EnemyRenderer>("Enemies");
            AddMeshView<ProjectileRenderer>("Shots");
            AddMeshView<CursorView>("Cursor");

            // The locator last but one: it reads the same frame every view reads and the camera the rig is
            // about to move, and draws over everything the map can put on screen.
            _radar = AddMeshView<ThreatRadarView>("ThreatRadar");
            _radar.InitializeLocator(_camera, Campaign.IsFirstMap);

            _hud = AddView<HudView>("Hud");
            _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog);

            _rig = _camera.GetComponent<CameraRig>();
            if (_rig == null) _rig = _camera.gameObject.AddComponent<CameraRig>();
            _rig.Initialize(World.TileGrid, World.Core.Center, Campaign.IsFirstMap);
        }

        private T AddView<T>(string viewName) where T : Component
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>Build a view and point it at the world, remembering it for the next map change -
        /// which is what <see cref="LoadMap"/> needs, and why views are not looked up by name.</summary>
        private T AddMeshView<T>(string viewName) where T : MeshView
        {
            T view = AddView<T>(viewName);
            view.Initialize(World, Frames, palette);
            _views.Add(view);
            return view;
        }

        private void OnDrawGizmosSelected()
        {
            // Edit mode has no catalog yet (it is built in Awake from the same field), so the gizmo asks
            // the map set directly rather than drawing the wrong map's rectangle, and draws nothing when
            // there is no campaign to ask.
            MapCatalog catalog = Catalog ?? MapCatalog.From(campaign);
            if (catalog == null) return;

            int index = Application.isPlaying && Campaign != null ? Campaign.MapIndex : startMapIndex;
            MapDefinition map = catalog.At(Mathf.Clamp(index, 0, catalog.Count - 1));

            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(new Vector3(map.Width * 0.5f, map.Height * 0.5f, 0f),
                new Vector3(map.Width, map.Height, 0f));
        }
    }
}
