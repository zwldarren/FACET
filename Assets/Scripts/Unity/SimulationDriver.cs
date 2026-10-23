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

        /// <summary>The speed gear and the tick backlog. Owned by the driver, not the world, so the gear
        /// survives a map change or a restart and the simulation never sees it.</summary>
        public TickClock Clock { get; } = new TickClock();

        private BuildInput _input;
        private Camera _camera;

        /// <summary>Every mesh view, gathered as built rather than looked up by name so a view added
        /// later cannot be forgotten when a map change points them at the new world.</summary>
        private readonly List<MeshView> _views = new List<MeshView>();

        /// <summary>The camera rig, whose pan bounds are the map's size and so have to follow it.</summary>
        private CameraRig _rig;

        /// <summary>The out-of-view locator, which needs the camera only the rig's owner knows; re-handed
        /// on every map change with the rig.</summary>
        private ThreatRadarView _radar;

        /// <summary>The tutorial's guide, which needs the same camera the locator does - it folds its marker
        /// onto the open edge when the layout a task draws is off screen.</summary>
        private TutorialGuideView _guide;

        /// <summary>The lesson's camera: glides to the tile a line asks for and to a wave that has just
        /// arrived. Null until the views are built, and reset on every map because each map teaches its own
        /// lesson.</summary>
        private TutorialCamera _tutorialCamera;

        /// <summary>The HUD, for its panels' claim on the mouse. Null until the views are built (and
        /// for a driver that never built any).</summary>
        private HudView _hud;

        /// <summary>The shell: the title screen (the main menu), the in-run pause menu with the level list
        /// and the help card behind it. Null for a driver that built no views; it asks the driver for
        /// every transition rather than touching the world itself.</summary>
        private ShellView _shell;

        /// <summary>The cleared count last written to the store. The world reports its win every frame
        /// after it is won, so the write has to be an edge rather than a level.</summary>
        private int _writtenCleared;

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

            // Progress outlives the session: the store's cleared count is what the level list is built
            // from, and the campaign opens on its frontier unless the inspector names a map.
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

            // The shell owns the screen while it stands: the devices are the menu's, the run is held and
            // nothing ticks behind it - a menu that let the factory run would spend the player's time, and
            // a key aimed at it must not also reach the run. The held frame still describes the world, so
            // the views keep drawing it under the shell's own backdrop.
            if (_shell != null && _shell.Open)
            {
                Frames.Frame = new ViewFrame(1f, WorldPerPixel, CursorCell, Time.unscaledTime);
                return;
            }

            // Esc and M open the pause menu - the one in-run hub, the same door the HUD's menu button
            // asks for. Read here rather than in the shell, because the driver is what holds the run, and
            // read before the devices are sampled so the press cannot also build. Esc still drops a
            // selected building first: the key that reads the map should not also stop the run, so the
            // menu only opens when there is no placement on the cursor to put down.
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

            // [ and ] change gear. The gear only changes how many ticks this host feeds per frame.
            int gearSteps = _input.ConsumeGearSteps();
            for (; gearSteps > 0; gearSteps--) Clock.Up();
            for (; gearSteps < 0; gearSteps++) Clock.Down();

            // Sample the devices once, then hand the same latched command to every tick this frame, so a
            // click between two ticks applies once, not once per catch-up tick. The HUD has first claim on
            // the mouse, so pressing a panel button cannot also drop a belt under it.
            _input.Sample(_camera, World, _hud != null && _hud.PointerOverHud);

            // A digit the lesson keeps dead gets a word rather than silence: the bar the kind is missing
            // from is the lesson's doing, so the lesson says so.
            if (_hud != null && _input.LockedPressed.HasValue)
                _hud.NoticeLocked(_input.LockedPressed.Value);

            // The info card grows while a sorter's filter picker is on it, and both arrow views have to know:
            // the panel boxes they fold arrows past are the boxes the HUD draws, so a card the arrows do not
            // know about is a place an enemy can stand unseen. One frame of lag, like the pointer's.
            if (_hud != null)
            {
                if (_radar != null) _radar.SetInfoGrown(_hud.InfoGrown);
                if (_guide != null) _guide.SetInfoGrown(_hud.InfoGrown);
            }

            // The "how to play" card is the player's to hide and bring back (its button, or T), so the strip
            // it wears is relayed the same way: the rig's pan bound and the locator's arrows follow the card.
            // The bar's own length goes with it - a tutorial hands buildings over a key at a time, and an arrow
            // folded past the full shelf would be dodging a strip that is not drawn.
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
            // world drops a post-run start-wave request, so this is the only reader here - and the
            // one-shot is consumed either way, so the new run does not spend the same press twice.
            if (World.Status == GameStatus.Won && _input.StartWaveRequested)
            {
                _input.ConsumeOneShots();
                if (Campaign.EnterNext()) LoadMap();
                else EnterEndless();
            }

            InputCommand cmd = _input.Snapshot();
            CursorCell = cmd.CursorCell;
            // Handed to the HUD for the cursor tooltip: the input latch owns the devices, so the HUD
            // cannot sample the tile itself, and reading is not building.
            if (_hud != null) _hud.SetCursor(CursorCell);

            // The gear scales the real time poured in, never TickDt: at 3x a frame runs three times the
            // ticks, each the same 30 Hz step, so a run is the same tick sequence at every gear.
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
            // the view is the player's to inspect - the tutorial should not move it.
            if (_tutorialCamera != null && !World.Paused)
            {
                _tutorialCamera.Tick(World, _hud != null && _hud.TutorialUp,
                    _hud != null && _hud.InfoGrown, _hud != null ? _hud.BarTiles : HudLayout.BarTiles,
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

        /// <summary>Which map the campaign opens on: the inspector's own answer when it names one, and the
        /// saved progress' frontier otherwise. The frontier is the first map not yet cleared, clamped to the
        /// campaign - a store can outlive an edit to the map set, and a map that no longer exists is not an
        /// index to open on.</summary>
        private int StartIndexFromProgress(int cleared)
            => startMapIndex > 0 ? startMapIndex : Mathf.Min(cleared, Catalog.Count - 1);

        /// <summary>Write the campaign's cleared count through to the store when it has grown. Idempotent
        /// and edge-triggered: the world reports a win every frame, and a flush per frame would be a
        /// per-frame hitch for a count that can only change on the frame it changed.</summary>
        private void PersistProgress()
        {
            int cleared = Campaign.ClearedCount;
            if (cleared == _writtenCleared) return;

            CampaignProgress.Write(cleared);
            _writtenCleared = cleared;
        }

        /// <summary>Open the pause menu over the run the player is in. The shell draws over it and the
        /// driver holds it, so nothing the menu reads can also be a key the run answers. Public because
        /// its three callers are the driver's own Esc/M reading, the HUD's menu button and a PlayMode
        /// test, and they all mean the same door.</summary>
        public void OpenMenu()
        {
            if (_shell == null) return;

            _shell.ShowPause();
            SetRunHeld(true);
        }

        /// <summary>Hold the run behind the shell, or hand it back. The HUD and the camera rig read the
        /// keyboard in their own updates, so stopping the driver's frame is not enough on its own: both are
        /// stood down while the shell stands, and neither is destroyed by it.</summary>
        private void SetRunHeld(bool held)
        {
            if (_hud != null) _hud.enabled = !held;
            if (_rig != null) _rig.enabled = !held;
        }

        /// <summary>The shell's "play" door: open the map it asked for as a fresh run. A run is one run, so
        /// a map is always built again - the world behind a menu is never carried past it. Endless is the
        /// same map with its wave table unbounded, which is the only thing the flag changes.
        ///
        /// Public because it is also the door out of the boot title: a caller that starts a run this way
        /// gets exactly what the title's play button gets, devices and clock handed back included.</summary>
        public void PlayMap(int index, bool endless)
        {
            SetRunHeld(false);
            if (_shell != null) _shell.Close();

            Campaign.StartMap(index);
            if (endless) LoadMap(Catalog.At(Campaign.MapIndex).WithEndless());
            else LoadMap();

            PersistProgress();
        }

        /// <summary>The pause menu's back door: the run behind it carries on exactly where it was held,
        /// pause state and clock gear included - the menu never touched either.</summary>
        public void ResumeRun()
        {
            if (_shell == null) return;

            _shell.Close();
            SetRunHeld(false);
        }

        /// <summary>The pause menu's restart door: start this map's run over. The request goes through the
        /// world and lands on the next tick, exactly as the R key's does - a click happens between ticks
        /// and the world is the one that knows the order. Closing the shell first is what lets that tick
        /// happen; the world's restart resets its own pause and status.</summary>
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

        /// <summary>The pause menu's far door: leave the run for the title, the main menu. The run behind
        /// stays built but held - picking a map from there builds that map's own world, so nothing is
        /// carried across - and the hold is set here rather than assumed, so the door leaves the same
        /// state whichever screen asked for it.</summary>
        public void ReturnToTitle()
        {
            if (_shell == null) return;

            _shell.ShowTitle();
            SetRunHeld(true);
        }

        /// <summary>Swap in the map the campaign is now on, without reloading the scene.
        ///
        /// The world is <em>replaced</em> rather than reset: a world is one run, sized and fixed by its
        /// map, so a new map is a new world built from its own data with the same content table. The
        /// views rebind rather than rebuild, their meshes and sprites being the same objects either way.</summary>
        public void LoadMap() => LoadMap(Catalog.At(Campaign.MapIndex));

        /// <summary>The same swap, for a map the catalog does not hand out: the endless re-entry of the
        /// campaign's last map, whose world is built from a derived copy rather than the authored data.</summary>
        private void LoadMap(MapDefinition map)
        {
            World = NewWorld(map);
            ReportMapProblems(World.Map);

            // Every entry builds a fresh run of a map, so every entry teaches that map's lesson - picking a
            // cleared map from the list is a replay of the level, not a resumption of the run that cleared
            // it. Skipping the lesson because the campaign had already cleared the map made the tutorial
            // unreachable the moment the frontier moved past it, and lost it entirely for a save cleared
            // before the lesson was authored. A caller that really wants a lessonless world can still say
            // Tutorial.Skip() before the first tick; the driver itself does not.

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
            // The locator follows the rig: its Core marker points at the same focus point, and the tutorial
            // card is up on this map or it is not.
            if (_radar != null) _radar.InitializeLocator(_camera, Campaign.IsFirstMap);
            if (_guide != null) _guide.InitializeGuide(_camera);
            if (_tutorialCamera != null) _tutorialCamera.Reset();
            if (_hud != null)
                _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog,
                    _input.RequestFilter, Clock, OpenMenu);

            Debug.Log("FACET: loaded map " + (Campaign.MapIndex + 1) + " of " + Campaign.MapCount +
                " - " + World.Map.Name + (World.Map.GeneratedWaves < 0 ? " (endless)" : string.Empty), this);
        }

        /// <summary>The campaign has no map after this one, and the player asked to go on: the same map
        /// again, played endless (§7's entry). A new world from a copy of the map's own data with the
        /// generator unbounded - the exam keeps setting questions forever, on a seed of its own.</summary>
        private void EnterEndless()
        {
            LoadMap(Catalog.At(Campaign.MapIndex).WithEndless());
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
            var config = new SimConfig
            {
                BeltSpeed = beltSpeedTilesPerSecond,
                CoreMaxHp = coreMaxHp,
            };

            // §5: one seed per run, shown in the HUD. A map that names no seed - the default - gets a
            // fresh one here, at the engine boundary, because Core never draws a number of its own; so
            // two runs of one map play out differently. A map that pins a seed keeps every run of it
            // reproducible to that number instead.
            if (map.Seed == 0) config.FreshSeed = NewRunSeed;

            return new SimWorld(map, config, Content);
        }

        /// <summary>A fresh run seed, from Unity's random - the one place the game reaches for entropy,
        /// and the reason the HUD prints a seed at all: note the number, and the run can be replayed
        /// from it by pinning it on the map.</summary>
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
            // about to move, and draws over everything the map can put on screen.
            _radar = AddMeshView<ThreatRadarView>("ThreatRadar");
            _radar.InitializeLocator(_camera, Campaign.IsFirstMap);

            // The tutorial's guide under the cursor's ghost: an annotation of a tile, so it goes over the map
            // and under the one thing that says what the next click would do.
            _guide = AddMeshView<TutorialGuideView>("TutorialGuide");
            _guide.InitializeGuide(_camera);

            _hud = AddView<HudView>("Hud");
            _hud.Initialize(World, palette, Campaign, _input.RequestSelect, _input.RequestRotate, Catalog,
                _input.RequestFilter, Clock, OpenMenu);

            _rig = _camera.GetComponent<CameraRig>();
            if (_rig == null) _rig = _camera.gameObject.AddComponent<CameraRig>();
            _rig.Initialize(World.TileGrid, World.Core.Center, Campaign.IsFirstMap);

            // The lesson's camera rides the rig the driver just made: the tutorial's targets are the places
            // a player would otherwise have to pan to find, and the wave's arrival is the one event a lesson
            // cannot narrate from a fixed view.
            _tutorialCamera = new TutorialCamera(_rig, _camera);

            // The shell last: it draws over the run and asks the driver for every transition, so it is
            // handed the doors only once the HUD and the rig it will hold while it is open exist.
            _shell = AddView<ShellView>("Shell");
            _shell.Initialize(palette, Catalog, Campaign, PlayMap, ResumeRun, RestartRun, ReturnToTitle);

            // The title opens the game: the run is built behind it and held, so the first frame a player
            // sees is the wordmark rather than a map already under way. It is the same hold the pause menu
            // uses - devices, clock and HUD all stood down - and the play button is the one door out.
            // A scene that names a start map is the test door and boots straight into it instead, which is
            // what keeps Start Map Index doing what it says.
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
