using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [Tooltip("Which map of the campaign to start on, when the scene wants a specific one. Left at 0, " +
            "the game opens on its title screen and the campaign's frontier - the first map the saved " +
            "progress has not cleared, and the first map alone on a fresh profile. A named map boots " +
            "straight into that map instead, skipping the title, which is the door a scene under test uses.")]
        [SerializeField] private int startMapIndex = 0;

        [Header("Views")]
        [Tooltip("Build the terrain / machine / belt / item / enemy / shot / core / cursor / HUD views at runtime.")]
        [SerializeField] private bool autoCreateViews = true;

        /// <summary>One run, replaced when the campaign moves on.</summary>
        public SimWorld World { get; private set; }

        public Palette Colors => palette;

        /// <summary>Outlives every world it builds, which is why it is not a field on
        /// <see cref="SimWorld"/>.</summary>
        public CampaignState Campaign { get; private set; }

        public MapCatalog Catalog { get; private set; }

        /// <summary>The HUD reads this so it prints the same table the tick runs on.</summary>
        public ContentDatabase Content { get; private set; }

        public float Alpha { get; private set; }

        public float WorldPerPixel { get; private set; }

        public Int2 CursorCell { get; private set; }

        /// <summary>The frame every view draws; the driver is the only writer.</summary>
        public FrameClock Frames { get; private set; }

        /// <summary>The speed gear and the tick backlog: owned here rather than by the world, so the gear
        /// survives a map change and the simulation never sees it.</summary>
        public TickClock Clock { get; } = new TickClock();

        private BuildInput _input;
        private Camera _camera;

        /// <summary>Gathered as built rather than looked up by name, so a view added later cannot be
        /// forgotten on a map change.</summary>
        private readonly List<MeshView> _views = new List<MeshView>();

        private CameraRig _rig;

        private ThreatRadarView _radar;

        private TutorialGuideView _guide;

        /// <summary>The coached lesson's popup and boxes (the tutorial level). Draws nothing on a map whose
        /// lesson is not coached.</summary>
        private TutorialCoachView _coach;

        /// <summary>Reset on every map, because each map teaches its own lesson.</summary>
        private TutorialCamera _tutorialCamera;

        /// <summary>Tiles of empty room the coached level's framing leaves right of the map, where the coach's
        /// popup sits, so the popup covers neither the map nor the HUD.</summary>
        private const float CoachRoomTiles = 10f;

        private HudView _hud;

        /// <summary>Asks the driver for every transition rather than touching the world itself.</summary>
        private ShellView _shell;

        /// <summary>Edge-triggered: the world reports its win every frame after it is won, so the write
        /// cannot be a level.</summary>
        private int _writtenCleared;

        private void Awake()
        {
            // Tile coordinates ARE world coordinates: the views build in tile space and the cursor turns a
            // screen point straight into a tile, so this object must sit at the origin. Pinned in code because
            // the symptom is a silent offset.
            transform.position = Vector3.zero;

            if (palette == null)
            {
                // Keeps an unwired scene runnable.
                Debug.LogWarning("FACET: no Palette assigned - using code defaults. " +
                    "Assign one (Assets > Create > FACET > Palette) to tune colours and line widths.", this);
                palette = ScriptableObject.CreateInstance<Palette>();
                palette.hideFlags = HideFlags.DontSave;
            }

            // The content table is converted once here, so a map change costs one SimWorld; a campaign with
            // no maps is said out loud rather than started.
            Catalog = MapCatalog.From(campaign);
            if (Catalog == null)
            {
                Debug.LogError("FACET: no campaign - assign a Map Set (Assets > Create > FACET > Map Set) " +
                    "with at least one map in it. There is nothing to play without one.", this);
                enabled = false;
                return;
            }

            // Progress outlives the session: the store's cleared count is what the level list is built from.
            int cleared = CampaignProgress.Read();
            Campaign = new CampaignState(Catalog.Count, StartIndexFromProgress(cleared), cleared);
            _writtenCleared = cleared;
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
            Frames = new FrameClock { Frame = new ViewFrame(1f, WorldPerPixel, new Int2(-1, -1), Time.unscaledTime) };

            if (autoCreateViews) BuildViews();

            // The matched-round analytics: a reader of the world, like the views, sending one row per wave.
            gameObject.AddComponent<AnalyticsReporter>().Bind(this);
        }

        private void Update()
        {
            // Unity can call Update on an instance whose Awake never completed (a script reload during play
            // restores the scene without re-awaking it), so the driver stands down rather than throwing once
            // per frame.
            if (_input == null)
            {
                enabled = false;
                return;
            }

            UpdateWorldPerPixel();

            // The shell owns the screen while it stands: the devices are the menu's and nothing ticks behind
            // it, so a key aimed at the menu cannot also reach the run. The held frame still describes the
            // world, so the views keep drawing it under the shell's backdrop.
            if (_shell != null && _shell.Open)
            {
                Frames.Frame = new ViewFrame(1f, WorldPerPixel, CursorCell, Time.unscaledTime);
                return;
            }

            // Esc and M open the pause menu - the same door the HUD's menu button asks for. Read here
            // because the driver is what holds the run, and before the devices are sampled so the press
            // cannot also build. Esc drops a selected building first: the menu only opens when there is no
            // placement on the cursor.
            if (_shell != null && Keyboard.current != null)
            {
                bool menu = Keyboard.current.mKey.wasPressedThisFrame;
                bool escape = Keyboard.current.escapeKey.wasPressedThisFrame && World.SelectedKind == null;
                if (menu || escape)
                {
                    OpenMenu();
                    return;
                }
            }

            if (_input.ConsumePauseToggle())
            {
                World.Paused = !World.Paused;
                Debug.Log("FACET: " + (World.Paused ? "paused" : "resumed") + " at tick " + World.TickCount, this);
            }

            // [ and ] change gear, which only changes how many ticks this host feeds per frame.
            int gearSteps = _input.ConsumeGearSteps();
            for (; gearSteps > 0; gearSteps--) Clock.Up();
            for (; gearSteps < 0; gearSteps++) Clock.Down();

            // Sample the devices once, then hand the same latched command to every tick this frame, so a
            // click between two ticks applies once. The HUD has first claim on the mouse, so pressing a panel
            // button cannot also drop a belt under it.
            _input.Sample(_camera, World, _hud != null && _hud.PointerOverHud);

            // A digit the lesson keeps dead gets a word rather than silence.
            if (_hud != null && _input.LockedPressed.HasValue)
                _hud.NoticeLocked(_input.LockedPressed.Value);

            // Both arrow views fold past the boxes the HUD drew, so they follow the card's two kinds of
            // growth. One frame of lag, like the pointer's: a card the arrows do not know about is a place an
            // enemy can stand unseen.
            if (_hud != null)
            {
                if (_radar != null)
                {
                    _radar.SetInfoGrown(_hud.InfoGrown);
                    _radar.SetInfoExtra(_hud.InfoExtra);
                }
                if (_guide != null)
                {
                    _guide.SetInfoGrown(_hud.InfoGrown);
                    _guide.SetInfoExtra(_hud.InfoExtra);
                }
            }

            // The "how to play" card is the player's to hide and bring back (its button, or T), so the strip
            // it wears is relayed the same way - and the bar's own length with it, since a tutorial hands
            // buildings over a key at a time.
            if (_hud != null)
            {
                if (_rig != null) _rig.SetTutorialUp(_hud.TutorialUp);
                if (_radar != null)
                {
                    _radar.SetTutorialUp(_hud.TutorialUp);
                    _radar.SetBarTiles(_hud.BarTiles);
                }
                if (_guide != null)
                {
                    _guide.SetTutorialUp(_hud.TutorialUp);
                    _guide.SetBarTiles(_hud.BarTiles);
                }
            }

            // N means "go on" in every run state there is: start the next wave mid-map, carry on to the
            // next map once won, and - the campaign spent - keep going on the last map, endless (§7). The
            // one-shot is consumed either way, so the new run does not spend the same press twice.
            if (World.Status == GameStatus.Won && _input.StartWaveRequested)
            {
                _input.ConsumeOneShots();
                if (Campaign.EnterNext()) LoadMap();
                else EnterEndless();
            }

            InputCommand cmd = _input.Snapshot();
            CursorCell = cmd.CursorCell;
            // For the cursor tooltip: the input latch owns the devices, and reading is not building.
            if (_hud != null) _hud.SetCursor(CursorCell);

            // The gear scales the real time poured in, never TickDt: at 3x a frame runs three 30 Hz ticks,
            // so a run is the same tick sequence at every gear.
            int ticks = Clock.Advance(Time.deltaTime, SimConfig.TickDt);
            for (int i = 0; i < ticks; i++)
            {
                int beforeTick = World.TickCount;
                World.Tick(cmd);
                if (cmd.RestartPressed || World.TickCount < beforeTick)
                    for (int view = 0; view < _views.Count; view++) _views[view].Rebind(World);
                _input.ConsumeOneShots();
                cmd = _input.Snapshot();
            }

            // Recorded as it happens rather than on a keypress, so the count is right whatever got the
            // player here; idempotent, so polling every frame is the honest way to read an unreported edge.
            if (World.Status == GameStatus.Won) Campaign.MarkCleared();

            // A clear is the one moment progress grows, so it is the one moment the store is written -
            // not every frame the won world keeps saying so.
            PersistProgress();

            Alpha = World.Paused || World.Status != GameStatus.Playing
                ? 1f
                : Clock.Alpha(SimConfig.TickDt);

            // The lesson's camera reads the frame that just ticked: a line that advanced or a wave that
            // started is already in World, and the glide must begin from the view the player has now. Paused,
            // the view is the player's to inspect - the tutorial should not move it. A coached level frames the
            // whole map once and holds it, so nothing the lesson boxes is ever off screen to glide to.
            if (_tutorialCamera != null && !World.Paused && !World.Tutorial.Coached)
            {
                _tutorialCamera.Tick(World, _hud != null && _hud.TutorialUp,
                    _hud != null && _hud.InfoGrown, _hud != null ? _hud.InfoExtra : 0f,
                    _hud != null ? _hud.BarTiles : HudLayout.BarTiles,
                    cmd.BuildHeld, Time.unscaledTime);
            }

            // Publish the frame views draw in, written once after the tick loop and the WorldPerPixel
            // refresh, so every view reads the same value.
            Frames.Frame = new ViewFrame(Alpha, WorldPerPixel, CursorCell, Time.unscaledTime);
        }

        private void UpdateWorldPerPixel()
        {
            WorldPerPixel = Screen.height > 0
                ? (2f * _camera.orthographicSize) / Screen.height
                : 0.02f;
        }

        /// <summary>Which map the campaign opens on: the inspector's answer when it names one, the saved
        /// frontier otherwise. The frontier is clamped to the campaign, since a store can outlive an edit to
        /// the map set.</summary>
        private int StartIndexFromProgress(int cleared)
            => startMapIndex > 0 ? startMapIndex : Mathf.Min(cleared, Catalog.Count - 1);

        /// <summary>Idempotent and edge-triggered: the world reports a win every frame, and a flush per frame
        /// would be a hitch for a count that changes once.</summary>
        private void PersistProgress()
        {
            int cleared = Campaign.ClearedCount;
            if (cleared == _writtenCleared) return;

            CampaignProgress.Write(cleared);
            _writtenCleared = cleared;
        }

        /// <summary>The shell draws over the run and the driver holds it, so nothing the menu reads can also
        /// be a key the run answers. Public because the driver's Esc/M reading, the HUD's menu button and a
        /// PlayMode test all mean the same door.</summary>
        public void OpenMenu()
        {
            if (_shell == null) return;

            _shell.ShowPause();
            SetRunHeld(true);
        }

        /// <summary>Stopping the driver's frame is not enough: the HUD and the camera rig read the keyboard
        /// in their own updates, so both are stood down while the shell stands, and neither is destroyed by
        /// it.</summary>
        private void SetRunHeld(bool held)
        {
            if (_hud != null) _hud.enabled = !held;
            if (_rig != null) _rig.enabled = !held;
            if (_coach != null) _coach.enabled = !held;
        }

        /// <summary>A run is one run, so the map is always built again - the world behind a menu is never
        /// carried past it. Endless is the same map with its wave table unbounded, which is the only thing
        /// the flag changes. Public because it is also the door out of the boot title.</summary>
        public void PlayMap(int index, bool endless)
        {
            SetRunHeld(false);
            if (_shell != null) _shell.Close();

            Campaign.StartMap(index);
            if (endless) LoadMap(Catalog.At(Campaign.MapIndex).WithEndless());
            else LoadMap();

            PersistProgress();
        }

        /// <summary>The run behind it carries on exactly where it was held, pause state and clock gear
        /// included - the menu never touched either.</summary>
        public void ResumeRun()
        {
            if (_shell == null) return;

            _shell.Close();
            SetRunHeld(false);
        }

        /// <summary>The request goes through the world and lands on the next tick, exactly as the R key's
        /// does: a click happens between ticks and the world is the one that knows the order. Closing the
        /// shell first is what lets that tick happen.</summary>
        public void RestartRun()
        {
            if (_shell == null) return;

            _shell.Close();
            SetRunHeld(false);
            World.RequestRestart();
            // A restart reopens the lesson from its first line, so the camera's memory of the last one goes
            // with it - otherwise the first target of the new run would look already visited.
            if (_tutorialCamera != null) _tutorialCamera.Reset();
        }

        /// <summary>The run behind stays built but held, and picking a map from the title builds that map's
        /// own world, so nothing is carried across.</summary>
        public void ReturnToTitle()
        {
            if (_shell == null) return;

            _shell.ShowTitle();
            SetRunHeld(true);
        }

        /// <summary>The world is <em>replaced</em> rather than reset: a world is one run, sized and fixed by
        /// its map, so a new map is a new world built from its own data with the same content table. The views
        /// rebind rather than rebuild, their meshes and sprites being the same objects either way.</summary>
        public void LoadMap() => LoadMap(Catalog.At(Campaign.MapIndex));

        /// <summary>The endless re-entry of the campaign's last map, whose world is built from a derived copy
        /// rather than the authored data.</summary>
        private void LoadMap(MapDefinition map)
        {
            World = NewWorld(map);
            ReportMapProblems(World.Map);

            // Every entry builds a fresh run, so every entry teaches that map's lesson: a cleared map picked
            // from the list is a replay of the level, not a resumption. A caller that really wants a lessonless
            // world can still say Tutorial.Skip() before the first tick.

            // The frame the views are about to draw must describe the new world: no leftover tick backlog,
            // no cursor on a tile that may not exist.
            Clock.ResetBacklog();
            Alpha = 1f;
            CursorCell = new Int2(-1, -1);
            Frames.Frame = new ViewFrame(1f, WorldPerPixel, CursorCell, Time.unscaledTime);

            for (int i = 0; i < _views.Count; i++) _views[i].Rebind(World);
            // The run opens on the Core: the map can be taller than the screen, and the Core is what the
            // player defends and must find first.
            if (_rig != null) _rig.Initialize(World.TileGrid, World.Core.Center, Campaign.IsFirstMap);
            if (_rig != null && World.Tutorial.Coached) _rig.FrameWholeMap(World.Map.Width, World.Map.Height, CoachRoomTiles);
            // The locator follows the rig - its Core marker points at the same focus point - and the tutorial
            // card is up on this map or it is not.
            if (_radar != null) _radar.InitializeLocator(_camera, Campaign.IsFirstMap);
            if (_guide != null) _guide.InitializeGuide(_camera);
            if (_tutorialCamera != null) _tutorialCamera.Reset();
            if (_hud != null)
                _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog,
                    _input.RequestFilter, Clock, OpenMenu);
            if (_coach != null) _coach.Initialize(World, _hud, _camera, palette);

            Debug.Log("FACET: loaded map " + (Campaign.MapIndex + 1) + " of " + Campaign.MapCount +
                " - " + World.Map.Name + (World.Map.GeneratedWaves < 0 ? " (endless)" : string.Empty), this);
        }

        /// <summary>A new world from a copy of the map's own data with the generator unbounded: the exam keeps
        /// setting questions forever, on a seed of its own.</summary>
        private void EnterEndless()
        {
            LoadMap(Catalog.At(Campaign.MapIndex).WithEndless());
        }

        /// <summary>A map is data, and data's mistakes do not throw: a ridge across a lane leaves a wave
        /// standing at its door, reading as a hung game. The rules are <see cref="MapValidation"/>'s.</summary>
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
            var config = new SimConfig
            {
                BeltSpeed = beltSpeedTilesPerSecond,
                CoreMaxHp = coreMaxHp,
            };

            // One seed per run, shown in the HUD. A map that names no seed gets a fresh one here, at the engine
            // boundary, because Core never draws a number of its own; a map that pins a seed keeps every run of it
            // reproducible.
            if (map.Seed == 0) config.FreshSeed = NewRunSeed;

            return new SimWorld(map, config, Content);
        }

        /// <summary>From Unity's random - the one place the game reaches for entropy.</summary>
        private static int NewRunSeed() => Random.Range(1, int.MaxValue);

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
            // about to move.
            _radar = AddMeshView<ThreatRadarView>("ThreatRadar");
            _radar.InitializeLocator(_camera, Campaign.IsFirstMap);

            // The tutorial's guide under the cursor's ghost: an annotation of a tile, so it goes over the map
            // and under the one thing that says what the next click would do.
            _guide = AddMeshView<TutorialGuideView>("TutorialGuide");
            _guide.InitializeGuide(_camera);

            _hud = AddView<HudView>("Hud");
            _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog,
                _input.RequestFilter, Clock, OpenMenu);

            // The coach over the HUD: it boxes the HUD's own pieces, so it is built after them.
            _coach = AddView<TutorialCoachView>("TutorialCoach");
            _coach.Initialize(World, _hud, _camera, palette);

            _rig = _camera.GetComponent<CameraRig>();
            if (_rig == null) _rig = _camera.gameObject.AddComponent<CameraRig>();
            _rig.Initialize(World.TileGrid, World.Core.Center, Campaign.IsFirstMap);
            if (World.Tutorial.Coached) _rig.FrameWholeMap(World.Map.Width, World.Map.Height, CoachRoomTiles);

            // The lesson's targets are the places a player would otherwise have to pan to find.
            _tutorialCamera = new TutorialCamera(_rig, _camera);

            // The shell's doors are handed over only once the HUD and the rig it will hold exist.
            _shell = AddView<ShellView>("Shell");
            _shell.Initialize(palette, Catalog, Campaign, PlayMap, ResumeRun, RestartRun, ReturnToTitle);

            // The run is built behind the title and held, so the first frame a player sees is the wordmark
            // rather than a map already under way. A scene that names a start map is the test door and boots
            // straight into it instead.
            if (startMapIndex <= 0)
            {
                _shell.ShowTitle();
                SetRunHeld(true);
            }
        }

        private T AddView<T>(string viewName) where T : Component
        {
            var go = new GameObject(viewName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>Build a view, point it at the world and remember it for the next map change - which is
        /// why views are not looked up by name.</summary>
        private T AddMeshView<T>(string viewName) where T : MeshView
        {
            T view = AddView<T>(viewName);
            view.Initialize(World, Frames, palette);
            _views.Add(view);
            return view;
        }

        private void OnDrawGizmosSelected()
        {
            // Edit mode has no catalog yet, so the gizmo asks the map set directly rather than drawing the
            // wrong map's rectangle, and draws nothing when there is no campaign to ask.
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
