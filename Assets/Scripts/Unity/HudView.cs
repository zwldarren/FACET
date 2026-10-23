using System;
using System.Collections.Generic;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Core's shape-filter mode collides with UnityEngine.FilterMode (the texture filter), and this file means
// the routing rule, so the alias is made once here rather than qualified at every case of the picker's
// switches.
using FilterMode = Facet.Core.FilterMode;

namespace Facet.Game
{
    /// <summary>
    /// The player's whole operating surface: a status rail top-left (the map and its wave, the Core's
    /// health and the wave's clock as two bars, the wave's bill of materials as chips, and two diagnostic
    /// lines), the stockpile floating beside it as a shape, a number and the run's seed with no panel of its
    /// own, a controls cluster top-right (the wave, pause, speed gear, menu and help, with the keyboard and
    /// restart behind the help card), a clickable build bar centred along the bottom, and an info card above
    /// it describing whichever building is hovered, selected - or, with nothing selected, read (see
    /// <see cref="RefreshInspect"/>).
    ///
    /// The card reports, in the order the questions come: is my bank holding, how long do I have, what is
    /// coming, can my defence stop it, and where am I not looking. The last is <see cref="ThreatRadar"/>'s
    /// answer - the same one the arrows on the map's edge are drawn from, so a sentence and a mark cannot
    /// disagree. A refused placement arrives on the event stream as <see cref="SimEventKind.PlacementRejected"/>
    /// and becomes a toast under the top cards; the stockpile's number turns amber while the selected
    /// building cannot be afforded and flashes warning-coloured while the toast is live.
    ///
    /// The build bar is the point of this view: every building is a real button carrying its hotkey, name
    /// and cost on one row, and a tile is tinted when unaffordable, outlined when selected and highlighted
    /// under the pointer. There is no role tab above it - eleven kinds do not need seven shelves - and the
    /// roles survive as the info card's metadata. Clicking a tile selects it exactly the way a number key
    /// does (through the input latch, so "the press decides what is built" is untouched), which is what lets
    /// the game be played without memorising the keyboard.
    ///
    /// The whole surface is budgeted: see <see cref="HudLayout"/> for the 22 % the four permanent panels may
    /// spend, <see cref="HudType"/> for the one four-step scale, and <see cref="HudInk"/> for the five ink
    /// levels and the three semantic hues the panels are actually allowed to use.
    ///
    /// Drawn with uGUI on purpose - no prefabs, no scene authoring - over <see cref="HudUi"/>'s widgets,
    /// with the pieces where <see cref="HudLayout"/>'s table says, through the same scale the fonts use.
    /// The canvas is event-driven where IMGUI repainted: a label is written when its words change, a bar
    /// when its length does, so a frame in which the world stood still costs a handful of comparisons -
    /// which is also the answer to WebGL's garbage collector.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HudView : MonoBehaviour
    {
        /// <summary>Rebuild the hierarchy once the screen has scaled this far from the built size: the
        /// fonts need the scale inside their point size to stay crisp, so a scale change is a rebuild rather
        /// than a matrix. A window resize is the only thing that gets here.</summary>
        private const float RebuildFraction = 0.02f;

        /// <summary>How long a refusal toast is shown. The "last second" event feed was dropped with it:
        /// a feed that repeats what the map already shows is something to read instead of play.</summary>
        private static readonly int RefusalTicks = (int)(SimConfig.TickRate * 3f);

        /// <summary>The fraction of the refusal's window spent fading out rather than holding.</summary>
        private const float RefusalFade = 0.35f;

        private SimWorld _world;
        private Palette _palette;
        private CampaignState _campaign;

        /// <summary>The progression, for the one question the HUD asks of it: which map is next. Set by the
        /// driver; a HUD it did not wire (a test's single-map HUD) has none.</summary>
        private MapCatalog _catalog;

        /// <summary>The build bar's two ways back into the input latch: select a kind (or none, which is the
        /// inspect cursor), and turn the placement ghost. Handed in by the driver, so a scene with no driver
        /// still draws.</summary>
        private Action<BuildKind?> _selectKind;
        private Action<int> _rotate;

        /// <summary>The cluster's way out of the run: the driver's pause-menu door, the same one Esc opens.
        /// Null for a HUD the driver did not wire, in which case the button draws and does nothing.</summary>
        private Action _openMenu;

        /// <summary>
        /// The sorter picker's way back into the input latch: re-route the sorter at this cell. It is a
        /// third door onto the same latch the keyboard and the bar use, so the simulation stays the one
        /// place a routing change is applied. Null for a HUD the driver did not wire - the picker then draws
        /// the machine's filter and sends nothing, which is the honest read-only card.
        /// </summary>
        private Action<Int2, ShapeFilter> _setFilter;

        /// <summary>The shapes as pictures, built once from the same Palette the map draws with: the
        /// stockpile and build tiles are marks, not sentences, and a mark drawn as a character disappears in
        /// a player whose font lacks it. See <see cref="HudIcons"/>.</summary>
        private HudIcons _icons;

        /// <summary>The money mark the HUD is wearing right now: the complete shape of this map's mineral.
        /// A map change moves it from one mineral to another, and the coin on the stockpile and on every
        /// cost tile is repainted when it moves. See <see cref="UpdateMoneyMarks"/>.</summary>
        private Shape _moneyDrawn = Shape.None;

        // ------------------------------------------------------------------ the canvas and its panels

        private RectTransform _canvasRoot;

        private StatusPanel _status;
        private ControlsPanel _controls;
        private HelpPanel _help;
        private BarPanel _bar;

        private InfoPanel _info;
        private StockpilePanel _stockpile;
        private ToastPanel _toast;
        private BannerPanel _banner;
        private TutorialPanel _tutorial;
        private TaskPanel _tasks;
        private TooltipPanel _tooltip;

        private float _scale = -1f;
        private int _screenWidth = -1;
        private int _screenHeight = -1;

        /// <summary>Which build tile the pointer is over this frame, or -1. Read by the info card, which
        /// describes the hovered building in preference to the selected or read one.</summary>
        private int _hoveredTile = -1;

        /// <summary>The camera whose view decides what is out of it - the same one <see cref="ThreatRadarView"/>
        /// draws its arrows for. Kept, because the HUD is built before the rig and a scene with no camera has
        /// nothing to be out of.</summary>
        private Camera _camera;

        /// <summary>The locator's own lists and marker scratch, reused every frame: the HUD asks the same
        /// question <see cref="ThreatRadarView"/> asks and reads the same answer, so the two cannot disagree
        /// about what is out of view.</summary>
        private readonly List<EnemySnapshot> _radarEnemies = new List<EnemySnapshot>();
        private readonly List<Int2> _radarStopped = new List<Int2>();
        private readonly List<Int2> _radarBlocked = new List<Int2>();
        private readonly List<Int2> _radarStalled = new List<Int2>();
        private ThreatRadar.Marker[] _radarMarkers = new ThreatRadar.Marker[0];
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        // ------------------------------------------------------------------ what was drawn last

        private bool _statusDirty = true;
        private int _statusTick = -1;
        private int _statusSeen = -1;
        private bool _statusPaused;
        private int _statusGear;

        private int _bannerTick = -1;
        private int _bannerSeen = -1;
        private bool _bannerPaused;
        private string _bannerText;
        private bool _bannerShown;

        private bool _toastShown;
        private string _toastText;
        private float _toastAlpha;
        private bool _toastTutorialUp;

        /// <summary>The lesson's own sentence in that strip, and the window it lives in: null when the
        /// lesson has nothing to say. Set by <see cref="Notice"/>, never by a refusal.</summary>
        private string _noticeText;
        private float _noticeFrom;
        private float _noticeFor;

        private int _stockpileCount = int.MinValue;
        private float _stockpileAlpha = -1f;

        private bool _controlsDirty = true;
        private bool _controlsPaused;
        private int _controlsGear = -1;
        private bool _controlsBehind;
        private bool _controlsCanWave;

        /// <summary>The driver's speed gear, read for the gear button's word and cycled by its press.
        /// Null in a scene (or a test) with no driver: the button then stays at 1x and does nothing.</summary>
        private TickClock _clock;

        private bool _infoBuildShown;
        private BuildKind? _infoKind;
        private bool _infoDirty = true;
        private int _infoFacing = -1;

        private bool _inspectDirty = true;
        private Int2 _inspectCell = new Int2(-1, -1);
        private int _inspectTick = -1;
        private int _rowCount;

        /// <summary>Is the info card wearing its taller sorter-picker band this frame? Read by the locator
        /// through <see cref="InfoGrown"/>, because a panel the arrows do not know about is a place an arrow
        /// is drawn under.</summary>
        private bool _pickerUp;

        /// <summary>
        /// The picker's working state, seeded from the machine's own filter (never the definition row's -
        /// the machine is what routes, and the player may have re-routed it). Two fields rather than one,
        /// because the two modes answer different questions: <see cref="_filterShape"/> is the exact item
        /// being painted, <see cref="_filterMineral"/> says the rule is about the item's mineral.
        /// </summary>
        private Shape _filterShape;
        private bool _filterMineral;
        private Quad _filterMineralPicked;

        /// <summary>The filter the working state was seeded from, so a filter the machine changed under the
        /// picker (a second click, or a load) re-seeds it instead of being overwritten by stale paint.
        /// Compared field by field: <see cref="ShapeFilter"/> is a plain struct with no equality of its
        /// own.</summary>
        private ShapeFilter _filterSeen;
        private Int2 _filterCell = new Int2(-1, -1);

        private bool _tutorialUp;

        /// <summary>The line the task card last printed, and whether that was the completion line. The panel's
        /// text is a string build, so it is rebuilt on the tick a line is ticked and on nothing else. Whether
        /// the card is on screen is asked of the panel itself - a cached flag went stale across the rebuild a
        /// finished lesson triggers (see <see cref="UpdateTasks"/>).</summary>
        private int _seenTaskStep = -1;
        private bool _seenTaskComplete;

        /// <summary>When the lesson finished, in unscaled seconds, or -1 while it is still being taught. The
        /// card says "tutorial complete" for <see cref="TaskDoneHold"/> and then gives the corner back - a
        /// list that stayed up would hold a permanent strip of the screen for the rest of the run.</summary>
        private float _tutorialDoneAt = -1f;

        /// <summary>The world the lesson notices below belong to, and the number of lines ticked in it. A map
        /// change is a new world and a restart rewinds this one, so both are read here as "a lesson with
        /// nothing to report yet" rather than as a line ticked on the frame they happened.</summary>
        private SimWorld _noticeWorld;
        private int _noticedStep = -1;

        /// <summary>When the current line became current, in the world's own ticks. The tick is what tells a
        /// restart apart - the same world object, wound back to its first line, would otherwise keep a timer
        /// that says this line has already been waited on - and being the world's clock, it stands still while
        /// the run is paused: held time is not stuck time, and a player coming back from a pause is not met by
        /// a hint that fired while they were away.</summary>
        private int _stepTick = -1;

        /// <summary>The step whose hint has been offered, so each line gets one offer and not one a
        /// second.</summary>
        private int _hintStep = -1;

        /// <summary>Is the current line's hint pinned into the task card? Set when the offer fires: the toast
        /// is the announcement, the pin is the answer staying where the line lives - a player who looked away
        /// from the strip, or had it talked over by refusals, still has the help.</summary>
        private bool _hintPinned;

        /// <summary>What the pin state was the last time the task card's text was built - the card rebuilds
        /// its body when the pin lands or lifts, not only when a line ticks.</summary>
        private bool _seenHintPinned;

        /// <summary>The word on the "how to play" card's hide button as last written - while a lesson is live
        /// the button says where it goes back to, and the word only changes on the lesson's edge.</summary>
        private string _hideWord;

        /// <summary>The player's own say over the "how to play" card: null until they press its hide button
        /// or <c>T</c>, after which their choice holds for the rest of the session - across restarts and map
        /// changes - instead of the campaign's first-map default. See <see cref="ShowTutorial"/>.</summary>
        private bool? _tutorialChoice;

        /// <summary>The tile under the pointer, pushed by the driver each frame: the cursor tooltip's
        /// one input. (-1,-1) is the driver's own "no cursor" answer and hides the tooltip.</summary>
        private Int2 _cursor = new Int2(-1, -1);
        private Int2 _tooltipCell = new Int2(-2, -2);
        private bool _tooltipDirty = true;
        private int _tooltipTick = -1;
        private int _tooltipRows;

        /// <summary>Did the last fill find anything to say about <see cref="_tooltipCell"/>? False for plain
        /// ground. Read on every frame, refilled only when the cell, the tick or the HUD changes.</summary>
        private bool _tooltipFilled;
        private float _tooltipWidth;
        private float _tooltipHeight;

        private bool _helpOpen;
        private string _compKey;

        /// <summary>The bar's kind set from before the current rebuild, so a rebuild that grew the bar can
        /// light the tiles it added (a tutorial's hand-over is the one that happens mid-run). Empty is also
        /// the first build's answer: no previous bar, nothing is news.</summary>
        private readonly HashSet<BuildKind> _barBefore = new HashSet<BuildKind>();

        /// <summary>Set when a rebuild comes from a world swap (<see cref="Initialize"/>): a new map's bar is
        /// all new and none of it is news. Read once by the rebuild and cleared.</summary>
        private bool _barSuppressPulse;

        /// <summary>What the player cannot see right now, as the locator's own summary. The status card's
        /// threat line and stripe read this, and the map's arrows are made from the same call: one rule about
        /// what counts as out of view, said in two registers.</summary>
        private ThreatRadar.Summary ReadOutOfView()
        {
            Palette.RadarLook look = _palette.Radar;
            if (_world == null || look == null) return ThreatRadar.None;
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return ThreatRadar.None;

            if (_radarMarkers.Length != look.MaxMarkers)
                _radarMarkers = new ThreatRadar.Marker[Mathf.Max(1, look.MaxMarkers)];

            var view = _screen.Measure(_camera, TutorialUp, look.EdgeInsetPixels, _pickerUp, BarTiles);

            _world.Enemies.GetEnemies(_radarEnemies);

            // Everything that has stopped, wherever it stopped: a jammed segment, a segment whose line ends in
            // the air, and a machine holding a piece it cannot pass on. One list because the locator has one
            // answer - a place to go - and the same three the arrows are made from, so the sentence and the
            // marks cannot name different sets.
            _world.Belts.GetJammed(_radarStopped);
            _world.Belts.GetBlocked(_radarBlocked);
            _radarStopped.AddRange(_radarBlocked);
            _world.Machines.GetStalled(_radarStalled);
            _radarStopped.AddRange(_radarStalled);

            float pixels = Screen.height > 0 ? Screen.height : 1f;
            ThreatRadar.Collect(view, _radarEnemies, _radarStopped, ThreatRadar.CoreCentre(_world),
                look.MergePixels * (2f * _camera.orthographicSize / pixels), _radarMarkers,
                out ThreatRadar.Summary summary);

            return summary;
        }

        /// <summary>The mouse is over a HUD panel right now, so a click belongs to the UI and not to the
        /// world underneath. Read by the driver and handed to <see cref="BuildInput.Sample"/>.</summary>
        public bool PointerOverHud { get; private set; }

        /// <summary>Is there a wave to start early, and is the world in a state to start it? The button is
        /// drawn only when this is true, so it never promises something the director would ignore. The clock
        /// half is <see cref="WaveDirector.HasWaveToStart"/>; the paused and over halves are the world's.</summary>
        private bool CanStartWaveNow =>
            _world.Status == GameStatus.Playing && !_world.Paused && _world.Waves.HasWaveToStart;

        /// <summary>Is the info card taller than its base height right now, because a sorter's filter picker
        /// is on it? The driver relays this to the locator and the tutorial's guide, whose panel boxes are the
        /// same boxes the HUD draws - see <see cref="ThreatRadarView.SetInfoGrown"/>.</summary>
        public bool InfoGrown => _pickerUp;

        /// <summary>How many keys the build bar is carrying this frame. Relayed to the locator and the guide,
        /// whose panel boxes have to be the boxes the HUD draws - a tutorial's bar grows a key at a time, so
        /// this is the run's number rather than the table's. An empty bar is a bar of no keys and not a full
        /// shelf: the fallback is for a HUD that has not built its panels yet, where the full row is the one
        /// honest guess.</summary>
        public int BarTiles => _bar != null ? _bar.Tiles.Count : HudLayout.BarTiles;

        /// <summary>Is the "how to play" card up? Up by default on the campaign's first map - unless that map
        /// brings a lesson, whose task list owns this corner for as long as it is being taught. Once the lesson
        /// has had its moment the reference card comes back, so the five general rules are the last thing the
        /// map says rather than the first: a player who has built the opening is the one ready to read about
        /// the rotator and the weakness rule, and the last wave asks for exactly that. Once the player has
        /// hidden or shown the card themselves, their choice wins on every map.</summary>
        private bool ShowTutorial
        {
            get
            {
                if (_campaign == null) return false;
                if (_tutorialChoice.HasValue) return _tutorialChoice.Value;

                return _campaign.IsFirstMap && (!HasTutorial || LessonHandedOver);
            }
        }

        /// <summary>Has the lesson finished and given the corner back? True once the task card's own completion
        /// hold has expired, so the two cards are complements: one corner, one card, and it is the lesson's
        /// until the lesson has nothing left to say.</summary>
        private bool LessonHandedOver => HasTutorial && !TaskListWanted;

        /// <summary>Does this run have a lesson to show? The one condition that puts the task list in the
        /// corner the reference card otherwise wears.</summary>
        private bool HasTutorial => _world != null && _world.Tutorial.Active;

        /// <summary>Is the task list on screen this frame? Up while the lesson is being taught, and for
        /// <see cref="TaskDoneHold"/> after its last line is ticked - so the player sees the final tick before
        /// the corner goes quiet. Yielded to the reference card while the player has asked for it with
        /// <c>T</c>: the two cards share one place under the status rail, and the corner belongs to whichever
        /// one the player wanted to read.</summary>
        private bool TaskListUp => TaskListWanted && !ShowTutorial;

        private bool TaskListWanted
        {
            get
            {
                if (_world == null) return false;

                TutorialState tutorial = _world.Tutorial;
                if (!tutorial.Active) return false;
                if (!tutorial.Complete) return true;

                return _tutorialDoneAt >= 0f && Time.unscaledTime - _tutorialDoneAt < TaskDoneHold;
            }
        }

        /// <summary>How long "tutorial complete" stays up. Long enough to be read after the last line flips,
        /// short enough not to be a panel the player has to live with: the same order as a toast, which is the
        /// HUD's other piece that answers a moment and then leaves.</summary>
        private const float TaskDoneHold = 3f;

        /// <summary>How many task lines the card prints at once, the oldest dropping off as the list grows. The
        /// card is <see cref="HudLayout.TutorialHeight"/> tall and its body holds five micro lines - the same
        /// budget the reference card's five steps spend, so the corner never grows.</summary>
        private const int MaxTaskLines = 5;

        /// <summary>How long a line has to stand unsolved before its hint is offered, in run ticks - thirty
        /// seconds of the world's clock, not the wall's. Long enough that a player working through the step is
        /// never talked over - laying the ten belts a line into the Core takes the better part of this - and
        /// short enough that a player who is stuck reads the answer before they give up on a screen where
        /// nothing is coming.</summary>
        private static readonly int HintDelayTicks = (int)(SimConfig.TickRate * 30f);

        /// <summary>How long a newly unlocked bar tile wears the accent outline. About one glance: long
        /// enough to be seen wherever the eye is, short enough that the outline is gone before it reads as
        /// a state the tile is in rather than a moment it had.</summary>
        private const float FreshSeconds = 2.5f;

        /// <summary>How long a lesson's own sentence - a line ticked, a hint - stays in the strip a refusal
        /// uses, and the fraction of that which is the fade out. A tick is read in a glance, so it lives about
        /// as long as a refusal does; a hint is a sentence, so it gets longer.</summary>
        private const float NoticeSeconds = 4f;
        private const float HintSeconds = 7f;
        private const float NoticeFade = 0.3f;

        /// <summary>Is either card - the task list or the reference card - on screen this frame? The one
        /// predicate the cards' claim on the pointer, the toast's clearance and the driver's camera rig all
        /// read, so no two pieces of the frame can disagree about whether that corner is taken.</summary>
        public bool TutorialUp => ShowTutorial || TaskListUp;

        /// <summary>
        /// The coin this run is paid in, as a mark: the <em>complete</em> shape of the map's own mineral.
        /// The rule is any complete shape - a full square banks exactly what a full circle banks
        /// (<see cref="Shape.IsFull"/>) - and this is the one of them the HUD draws, because the mineral
        /// under the player's own drills is the money they will see arriving.
        /// </summary>
        private Shape MoneyShape => Shape.Full(_world.Map.CurrencyMineral);

        /// <summary>Point the HUD at a world, its palette, and where the player is in the campaign. Called
        /// on every map change, which is why the canvas is built once and kept: a map change rebinds the
        /// world and lets the next update pass redraw every card, and only a changed content table rebuilds
        /// the build bar's tiles.
        ///
        /// <paramref name="campaign"/> is optional so a scene (or a test) with one map gets exactly the
        /// single-map HUD; the selection, rotation and filter callbacks are optional so the bar and the
        /// sorter's picker draw without them, they just cannot be clicked.</summary>
        public void Initialize(SimWorld world, Palette palette, CampaignState campaign = null,
            Action<BuildKind?> selectKind = null, Action<int> rotate = null, MapCatalog catalog = null,
            Action<Int2, ShapeFilter> setFilter = null, TickClock clock = null, Action openMenu = null)
        {
            _world = world;
            if (clock != null) _clock = clock;
            _palette = palette;
            _campaign = campaign;
            _catalog = catalog;
            if (selectKind != null) _selectKind = selectKind;
            if (rotate != null) _rotate = rotate;
            if (setFilter != null) _setFilter = setFilter;
            if (openMenu != null) _openMenu = openMenu;

            if (_icons == null) _icons = new HudIcons(palette);

            if (_canvasRoot == null) BuildCanvas();

            if (!BarMatchesContent())
            {
                // A world swap's rebuild lights nothing: the new map's bar is all new and none of it is news.
                _barSuppressPulse = true;
                RebuildPanels();
                return;
            }

            // The hierarchy stands; a map change is a rebinding. Every dirty flag goes up, so the
            // next update pass redraws the cards against the new world rather than trusting what the
            // old one had said.
            MarkEverythingDirty();
        }

        private void Update()
        {
            if (_world == null || _palette == null || _canvasRoot == null) return;

            RefreshScale();

            // A tutorial hands buildings over mid-run, so the bar's own tile set is a run's rather than the
            // table's and can change between frames. The panels are rebuilt rather than patched - the same
            // answer a resize gets - because this happens once a line, not once a frame.
            if (!BarMatchesContent())
            {
                RebuildPanels();
                return;
            }

            UpdateMoneyMarks();

            // The HUD owns its panels: the pointer being over one is reported to the input source, so a
            // click on a button cannot also build under it. This update runs after the driver's, so it
            // describes the pointer as of the previous frame - a frame of lag on a pointer that has to travel
            // to the button anyway. The stockpile readout and the toast are not panels: they are read, never
            // clicked, so a click goes through them to the map.
            PointerOverHud = ComputePointerOver();

            UpdateTutorial();
            UpdateTasks();
            UpdateToast();
            UpdateBanner();
            UpdateStockpile();
            UpdateStatus();
            UpdateControls();
            UpdateBuildBar();
            UpdateInfo();
            UpdateTooltip();
            UpdateButtons();
        }

        private void OnDestroy()
        {
            // The shape icons and the one white sprite are the HUD's own runtime textures, held by nothing
            // else, so they go with the HUD that made them. A scene reload in the Editor would otherwise leak
            // one set per play; a built player never reloads a scene.
            _icons?.Dispose();
            _icons = null;
            HudUi.ReleaseShared();
        }

        // ------------------------------------------------------------------ building the canvas

        private void BuildCanvas()
        {
            HudUi.EnsureEventSystem(transform);

            var canvasGo = new GameObject("HudCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            _canvasRoot = (RectTransform)canvasGo.transform;
        }

        /// <summary>Tear the panels down and build them again against the current screen. The one heavy
        /// pass the HUD has, asked for only by a resize: the fonts need the scale inside their point size to
        /// stay crisp, so a new scale is a rebuild, not a matrix.</summary>
        private void RebuildPanels()
        {
            _scale = HudLayout.Scale(Screen.height);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;

            // The bar's set before the teardown, so the rebuild can tell a tile the lesson just handed over
            // from one that was always there. Read once on the far side, then the run moves on.
            _barBefore.Clear();
            if (_bar != null)
                for (int i = 0; i < _bar.Tiles.Count; i++)
                    _barBefore.Add(_bar.Tiles[i].Kind);

            bool pulse = !_barSuppressPulse;
            _barSuppressPulse = false;

            // Immediate, because the rebuild happens inside the same update that noticed the resize:
            // a deferred Destroy would leave both HUDs on screen for a frame, and the panels are pure
            // runtime objects with nothing an asset could lose.
            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_canvasRoot.GetChild(i).gameObject);

            BuildPanels();
            MarkEverythingDirty();

            if (pulse) MarkFreshTiles();
        }

        /// <summary>Light the tiles a rebuild just added to the bar. The toast that names the unlock is at the
        /// top of the screen and the bar is at the bottom; the pulse is the thread between them. Only growth
        /// lights anything: a restart's shrink or a resize's same-set rebuild is not news.</summary>
        private void MarkFreshTiles()
        {
            if (_barBefore.Count == 0 || _bar.Tiles.Count <= _barBefore.Count) return;

            for (int i = 0; i < _bar.Tiles.Count; i++)
                if (!_barBefore.Contains(_bar.Tiles[i].Kind))
                    _bar.Tiles[i].FreshUntil = Time.unscaledTime + FreshSeconds;
        }

        private void BuildPanels()
        {
            _status = BuildStatus();
            _tutorial = BuildTutorial();
            _tasks = BuildTasks();
            _controls = BuildControls();
            _help = BuildHelp();
            _stockpile = BuildStockpile();
            _bar = BuildBar();
            _info = BuildInfo();
            _banner = BuildBanner();
            _toast = BuildToast();
            _tooltip = BuildTooltip();
        }

        private void MarkEverythingDirty()
        {
            _statusDirty = true;
            _controlsDirty = true;
            _infoDirty = true;
            _toastShown = false;
            _toastText = null;
            _toastAlpha = 0f;
            _noticeText = null;
            _bannerText = null;
            _bannerShown = false;
            _seenTaskStep = -1;
            _stockpileCount = int.MinValue;
            _stockpileAlpha = -1f;
            _infoBuildShown = false;
            _infoKind = null;
            _infoFacing = -1;
            _inspectDirty = true;
            _inspectCell = new Int2(-1, -1);
            _inspectTick = -1;
            _hoveredTile = -1;

            // The picker's paint belongs to the machine it was seeded from, and a new world has none: the
            // next card seeds itself from whatever sorter is inspected there.
            _filterSeen = ShapeFilter.None;
            _filterCell = new Int2(-1, -1);
            _helpOpen = false;
            _compKey = null;
            _tooltipCell = new Int2(-2, -2);
            _tooltipDirty = true;
            _tooltipTick = -1;
            _tooltipRows = 0;
            _tutorialUp = !ShowTutorial;   // forces the tutorial's own update to decide again

            // The card is built at its base height, and a picker left standing on the old hierarchy would
            // be a band nobody re-placed: the next card that describes a sorter raises it again.
            SetPickerUp(false);
        }

        // ------------------------------------------------------------------ the panels' geometry

        /// <summary>A card: a rounded ink surface with a milled edge, no stripe and no square corners. The
        /// edge is the quietest level of the ramp - a card is a surface, not an outline - and the radius is
        /// the same 10 design units on every card, so the HUD reads as one set of objects. The shadow is the
        /// world's own light continued onto the HUD: a card is an object on the map's table, not a hole cut
        /// out of it.</summary>
        private RectTransform BuildCard(string name, Rect rect, PlaceCorner corner)
        {
            RectTransform root = HudUi.Node(_canvasRoot, name);
            Place(root, rect, corner);

            HudUi.Shadow(root, name + ".shadow", HudRadius, _palette.HudShadow);
            HudBox back = HudUi.Round(root, name + ".back", _palette.HudPanel, HudRadius);
            HudUi.StretchFull(back.Rect);
            HudUi.Border(root, name + ".frame", HudInk.Edge(_palette), HudRadius, 1f);
            return root;
        }

        /// <summary>The one corner radius the cards share, in design units.</summary>
        private const float HudRadius = 10f;

        /// <summary>The smaller radius the bar's tiles and buttons share.</summary>
        private const float ControlRadius = 6f;

        /// <summary>The card's inner margin, in design units: one value for every card so their text
        /// starts on the same vertical line whether the card is 300 or 880 wide.</summary>
        private const float CardInset = 14f;

        private Color PanelBorder() => HudInk.Edge(_palette);

        private Color ButtonFill => HudInk.Raised(_palette);

        /// <summary>A design-unit font size through the current scale. Rounded up rather than down: at
        /// 960x600 the scale is 0.83, and rounding 10 down to 8 is the difference between a micro line and
        /// a squint. The fonts stay crisp because the scale is in the point size.</summary>
        private int FontSize(float design) => Mathf.Max(8, Mathf.CeilToInt(design * _scale));

        /// <summary>Let a dynamic sentence drop below its step rather than run past the card: the map's
        /// name, a wave's count and a locator sentence all vary in length, and a step is a design decision
        /// while an overrun is a defect. It shrinks no further than <paramref name="minDesign"/>.</summary>
        private void AutoFit(HudText text, float minDesign)
        {
            text.Text.enableAutoSizing = true;
            text.Text.fontSizeMin = FontSize(minDesign);
            text.Text.fontSizeMax = text.Text.fontSize;
        }

        private HudText Label(RectTransform parent, string name, float design, FontStyle style,
            TextAnchor anchor, bool wrap = false, bool tracked = false, bool rich = false)
            => HudUi.Text(parent, name, FontSize(design), style, anchor, wrap, tracked, rich);

        /// <summary>
        /// The left rail, read top to bottom in the order the questions come: whose run this is, how much
        /// Core is left, how long the clock has, what the next two waves are made of, and - only when the
        /// answer is bad - what is stuck and what is out of view.
        ///
        /// The hierarchy is carried by weight and ink, not by colour: the map's name and the numbers are
        /// paper, the words that label them are faint, and the one thing that changes colour is the number
        /// with something to say (a hit, a jam, an enemy off screen). No stripe down the side, no role hue.
        /// </summary>
        private StatusPanel BuildStatus()
        {
            Rect rect = HudLayout.Status(_scale);
            var panel = new StatusPanel { Root = BuildCard("status", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            const float w = HudLayout.StatusWidth;
            const float inner = w - CardInset * 2f;

            // Identity on the left, the wave on the right: one line, two ends, so neither crowds the other
            // when a map is called something long.
            panel.Title = Label(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(panel.Title.Rect, CardInset, 4f, inner - 96f, 22f);
            AutoFit(panel.Title, HudType.Micro);

            panel.WaveTag = Label(root, "waveTag", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleRight,
                tracked: true);
            AtRight(panel.WaveTag.Rect, CardInset, 8f, 130f, 14f);

            // The Core: a caption, the number at the far end, and a metre under both. The number is the one
            // thing on this card allowed to turn warning-coloured, so a hit is read where the eye already is.
            panel.CoreCap = Label(root, "coreCap", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(panel.CoreCap.Rect, CardInset, 29f, 60f, 12f);
            panel.CoreCap.Set("CORE", HudInk.Faint(_palette));

            panel.HealthValue = Label(root, "coreValue", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleRight);
            AtRight(panel.HealthValue.Rect, CardInset, 29f, 220f, 12f);

            panel.HealthTrack = HudUi.Round(root, "health.back", HudInk.Raised(_palette), 3f);
            At(panel.HealthTrack.Rect, CardInset, 43f, inner, 7f);
            panel.Health = HudUi.BarFill(panel.HealthTrack.Rect, "fill", 0f, 0f, inner * _scale,
                7f * _scale, 3f);

            HudBox clockBack = HudUi.Round(root, "clock.back", HudInk.Raised(_palette), 2f);
            At(clockBack.Rect, CardInset, 53f, inner, 3f);
            panel.Clock = HudUi.BarFill(clockBack.Rect, "fill", 0f, 0f, inner * _scale, 3f * _scale, 2f);

            panel.Rule = HudUi.Box(root, "rule", HudInk.Rule(_palette));
            At(panel.Rule.Rect, CardInset, 62f, inner, 1f);

            panel.News = Label(root, "news", HudType.Label, FontStyle.Normal, TextAnchor.MiddleLeft);
            At(panel.News.Rect, CardInset, 66f, inner, 16f);
            AutoFit(panel.News, 9f);

            // The bill of materials, two rows: the wave being counted to and the one after it.
            panel.NextLabel = Label(root, "waveNext", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(panel.NextLabel.Rect, CardInset, ChipsTop + (ChipHeight - 14f) * 0.5f, ChipLabelWidth, 14f);
            panel.AfterLabel = Label(root, "waveAfter", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(panel.AfterLabel.Rect, CardInset, ChipsAfterTop + (ChipHeight - 14f) * 0.5f, ChipLabelWidth, 14f);

            panel.Chips = BuildChips(root, "chip");
            panel.ChipsAfter = BuildChips(root, "after");

            // A hairline over the card's footer: the band below is either the two diagnostics or, when the
            // run is quiet, the two stable facts a player still wants at hand - the seed and the gear.
            HudBox footerRule = HudUi.Box(root, "footerRule", HudInk.Rule(_palette));
            At(footerRule.Rect, CardInset, FooterTop, inner, 1f);

            panel.Seed = Label(root, "seed", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleLeft);
            At(panel.Seed.Rect, CardInset, FooterTop + 3f, inner * 0.62f, 13f);

            panel.Speed = Label(root, "speed", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleRight);
            AtRight(panel.Speed.Rect, CardInset, FooterTop + 3f, inner, 13f);

            // Two lines that only speak when something needs them: quiet is the default and silence is the
            // good news - a line that only says "all clear" trains the player to stop reading.
            panel.Production = Label(root, "production", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleLeft);
            At(panel.Production.Rect, CardInset, FooterTop + 3f, inner, 13f);
            AutoFit(panel.Production, 8f);
            panel.Production.Show(false);

            panel.Threat = Label(root, "threat", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleLeft);
            At(panel.Threat.Rect, CardInset, FooterTop + 16f, inner, 13f);
            AutoFit(panel.Threat, 8f);
            panel.Threat.Show(false);

            return panel;
        }

        /// <summary>One row of chips: the count, the archetype's name and the demanded round's figure, all
        /// hidden until a wave is briefed into them.</summary>
        private Chip[] BuildChips(RectTransform root, string prefix)
        {
            var chips = new Chip[ChipCapacity];

            for (int i = 0; i < ChipCapacity; i++)
            {
                var chip = new Chip
                {
                    Back = HudUi.Round(root, prefix + i + ".back", HudInk.Raised(_palette), 4f),
                    Count = Label(root, prefix + i + ".count", HudType.Micro, FontStyle.Bold,
                        TextAnchor.MiddleLeft, tracked: true),
                    Name = Label(root, prefix + i + ".name", HudType.Micro, FontStyle.Normal,
                        TextAnchor.MiddleLeft),
                };
                RectTransform iconRect = HudUi.Node(root, prefix + i + ".icon");
                var icon = iconRect.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                chip.Icon = icon;

                chip.Back.Show(false);
                chip.Count.Show(false);
                chip.Name.Show(false);
                icon.gameObject.SetActive(false);
                chips[i] = chip;
            }

            return chips;
        }

        private TutorialPanel BuildTutorial()
        {
            Rect rect = HudLayout.Tutorial(_scale);
            var panel = new TutorialPanel { Root = BuildCard("tutorial", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            const float w = HudLayout.StatusWidth;
            const float inner = w - CardInset * 2f;

            HudText title = Label(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(title.Rect, CardInset, 4f, 250f, 22f);
            title.Set("HOW TO PLAY", _palette.HudText);

            // The player's way out: the card is a first-map default, not a rule, and the key on the button
            // is the way back once it is gone (the help card names it too).
            panel.Hide = HudUi.Button(root, "hide", 58f * _scale, 18f * _scale, "HIDE  T",
                FontSize(HudType.Micro), HudInk.Muted(_palette), () => SetTutorialShown(false));
            HudUi.TopRight(panel.Hide.Root, CardInset * _scale, 5f * _scale, 58f * _scale, 18f * _scale);

            HudBox rule = HudUi.Box(root, "rule", HudInk.Rule(_palette));
            At(rule.Rect, CardInset, 27f, inner, 1f);

            // Five steps, one line each: the numerals are the one accent, because they are the lesson's
            // rhythm rather than decoration. Rich text is on for this one label only.
            HudText body = Label(root, "body", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft,
                wrap: false, rich: true);
            At(body.Rect, CardInset, 30f, inner, 66f);

            string accent = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudAccent);
            body.Set(
                "<color=" + accent + ">1</color>  Drill (2) on ore, then drag Belt (1) to the Core.\n" +
                "<color=" + accent + ">2</color>  A full shape banked earns one coin.\n" +
                "<color=" + accent + ">3</color>  Each wave is weak to the shape printed above it.\n" +
                "<color=" + accent + ">4</color>  Cutter (3) splits, Rotator (4) turns  ·  Q/E turns a ghost.\n" +
                "<color=" + accent + ">5</color>  Belt it to a Turret (0): the exact shape does full damage.",
                HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.18f));

            panel.Root.gameObject.SetActive(ShowTutorial);
            return panel;
        }

        /// <summary>
        /// The lesson's card, in the corner the reference card wears: a heading, the position in the list, a
        /// hairline, then the lines themselves. Same rect, same width and same height as the reference card, so
        /// whichever one the player is reading hangs off the status rail in the same place and the layout's
        /// top strip does not have to know which it is.
        ///
        /// No close button: the card is not a thing the player opens, so it is not a thing they dismiss - it goes
        /// when the lesson does. <c>T</c> still swaps in the reference card, and its own hide button swaps
        /// back.
        /// </summary>
        private TaskPanel BuildTasks()
        {
            Rect rect = HudLayout.Tutorial(_scale);
            var panel = new TaskPanel { Root = BuildCard("tasks", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            const float w = HudLayout.StatusWidth;
            const float inner = w - CardInset * 2f;

            panel.Title = Label(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            At(panel.Title.Rect, CardInset, 4f, 190f, 22f);
            panel.Title.Set("TUTORIAL", _palette.HudText);

            panel.Count = Label(root, "count", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleRight);
            At(panel.Count.Rect, CardInset + inner - 44f, 8f, 44f, 14f);
            panel.Count.Set(string.Empty, HudInk.Muted(_palette));

            HudBox rule = HudUi.Box(root, "rule", HudInk.Rule(_palette));
            At(rule.Rect, CardInset, 27f, inner, 1f);

            // Five micro lines, the same budget the reference card's five steps spend: the lines are short by
            // construction (the map authors them) and the card does not grow with the list. The AutoFit is the
            // safety net under the asset's own length rule (MapProblemKind.TutorialLineTooLong): a line that
            // slips past validation shrinks to its floor rather than spilling past the card onto the map.
            panel.Body = Label(root, "body", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft,
                wrap: false, rich: true);
            At(panel.Body.Rect, CardInset, 30f, inner, 74f);
            AutoFit(panel.Body, 8f);

            // The stuck-line hint, pinned under the list once the current line has stood unanswered for a
            // while (UpdateLessonNotice): one label of its own, so its shrink never touches the task lines.
            // While it is up the list cedes it a row (see TaskLines), so the card still never grows.
            panel.Hint = Label(root, "hint", HudType.Micro, FontStyle.Italic, TextAnchor.UpperLeft);
            At(panel.Hint.Rect, CardInset, 90f, inner, 14f);
            AutoFit(panel.Hint, 8f);
            panel.Hint.Show(false);

            panel.Root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>The controls cluster: wave, pause, gear, menu and help, five small controls in the
        /// screen's top-right corner. The keyboard list is reference material and lives behind the help
        /// button, in <see cref="BuildHelp"/>. Pause stays out here - pausing is a mid-play act - while
        /// restart stays inside the card, one deliberate click further from a misfire. The wave button moved
        /// here from the status rail's last row: it is a time control like pause and the gear, and the rail
        /// needed the row back for its diagnostics. The menu button is the clickable form of Esc: it opens
        /// the shell's pause menu, so a player who never learns a key still has a way back to the title.
        /// </summary>
        private ControlsPanel BuildControls()
        {
            Rect rect = HudLayout.Controls(Screen.width, _scale);
            var panel = new ControlsPanel { Root = HudUi.Node(_canvasRoot, "controls") };
            Place(panel.Root, rect, PlaceCorner.TopRight);

            float side = HudLayout.ControlsHeight * _scale;
            float wave = HudLayout.WaveButtonWidth * _scale;
            float gap = HudLayout.ControlGap * _scale;

            // The wave is the cluster's one filled control: the only action that starts something, so it is
            // the only one allowed to wear the accent as a surface.
            panel.Wave = HudUi.Button(panel.Root, "wave", wave, side, "WAVE",
                FontSize(HudType.Micro), _palette.HudPanel, PressWave);
            HudUi.TopLeft(panel.Wave.Root, 0f, 0f, wave, side);
            panel.Wave.Root.gameObject.SetActive(false);

            panel.Pause = HudUi.Button(panel.Root, "pause", side, side, string.Empty,
                FontSize(HudType.Body), _palette.HudText, PressPause);
            HudUi.TopLeft(panel.Pause.Root, wave + gap, 0f, side, side);

            // Two bars rather than the word "II": a transport glyph is drawn, not typed, so it cannot turn
            // into a missing-character box in a font that lacks the character.
            panel.PauseBars = HudUi.Node(panel.Pause.Root, "bars");
            HudUi.TopLeft(panel.PauseBars, (HudLayout.ControlsHeight - 10f) * 0.5f * _scale,
                (HudLayout.ControlsHeight - 12f) * 0.5f * _scale, 10f * _scale, 12f * _scale);
            panel.PauseBarA = HudUi.Round(panel.PauseBars, "a", _palette.HudText, 1f);
            HudUi.TopLeft(panel.PauseBarA.Rect, 0f, 0f, 3f * _scale, 12f * _scale);
            panel.PauseBarB = HudUi.Round(panel.PauseBars, "b", _palette.HudText, 1f);
            HudUi.TopLeft(panel.PauseBarB.Rect, 7f * _scale, 0f, 3f * _scale, 12f * _scale);

            panel.Gear = HudUi.Button(panel.Root, "gear", side, side, GearWord(),
                FontSize(HudType.Label), _palette.HudText, PressGear);
            HudUi.TopLeft(panel.Gear.Root,
                (HudLayout.WaveButtonWidth + HudLayout.ControlsHeight + 2f * HudLayout.ControlGap) * _scale,
                0f, side, side);

            panel.Help = HudUi.Button(panel.Root, "help", side, side, "?",
                FontSize(HudType.Body), _palette.HudText, () => SetHelpOpen(!_helpOpen));
            HudUi.TopLeft(panel.Help.Root,
                (HudLayout.ControlsWidth - HudLayout.ControlsHeight) * _scale, 0f, side, side);

            // The way out sits between the gear and the help: the cluster reads left to right as
            // "what the run is doing" and then "what I can do about it", with the door to the menu
            // one word away from the run's own controls rather than hidden behind a glyph.
            panel.Menu = HudUi.Button(panel.Root, "menu", HudLayout.MenuButtonWidth * _scale, side, "MENU",
                FontSize(HudType.Micro), _palette.HudText, PressMenu);
            HudUi.TopLeft(panel.Menu.Root,
                (HudLayout.WaveButtonWidth + 2f * HudLayout.ControlsHeight + 3f * HudLayout.ControlGap) * _scale,
                0f, HudLayout.MenuButtonWidth * _scale, side);

            return panel;
        }

        /// <summary>The menu button's press: the driver's pause-menu door. Read through the driver rather
        /// than toggled here because the driver owns the run's hold - the same separation the pause and
        /// wave buttons keep.</summary>
        private void PressMenu() => _openMenu?.Invoke();

        /// <summary>The help card: the keyboard, one pair of ideas per line, and the restart button - here
        /// rather than on the cluster because a misplaced click must never restart the run, and opening a card
        /// first guarantees deliberateness. It overlays the map while open, the way a toast does. Its
        /// geometry is <see cref="HudLayout"/>'s, which the shell's copy of this card reads too.</summary>
        private HelpPanel BuildHelp()
        {
            string[] lines = KeyLines();
            Rect rect = HudLayout.Help(Screen.width, _scale, lines.Length);
            var panel = new HelpPanel { Root = BuildCard("help", rect, PlaceCorner.TopRight) };
            RectTransform root = panel.Root;

            HudText header = Label(root, "header", HudLayout.HelpLineSize, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            HudUi.TopLeft(header.Rect, HudLayout.HelpInset * _scale, HudLayout.HelpHeaderTop * _scale,
                rect.width - 60f * _scale, HudLayout.HelpLineHeight * _scale);
            header.Set("CONTROLS", _palette.HudText);

            HudBox rule = HudUi.Box(root, "rule", HudInk.Rule(_palette));
            HudUi.TopLeft(rule.Rect, HudLayout.HelpInset * _scale, 23f * _scale,
                rect.width - 2f * HudLayout.HelpInset * _scale, 1f);

            float y = HudLayout.HelpLineTop;
            foreach (string key in lines)
            {
                HudText line = Label(root, "key" + y, HudLayout.HelpLineSize, FontStyle.Normal,
                    TextAnchor.MiddleLeft);
                HudUi.TopLeft(line.Rect, HudLayout.HelpInset * _scale, y * _scale,
                    rect.width - 2f * HudLayout.HelpInset * _scale, HudLayout.HelpLineHeight * _scale);
                AutoFit(line, 8f);
                line.Set(key, HudInk.Muted(_palette));
                y += HudLayout.HelpLineStep;
            }

            panel.Restart = HudUi.Button(root, "restart", rect.width - 2f * HudLayout.HelpInset * _scale,
                HudLayout.HelpButtonHeight * _scale, "R  restart  run", FontSize(HudType.Label),
                _palette.HudText, PressRestart);
            // Under the last key line, so a line added to KeyLines pushes the button down with it.
            HudUi.TopLeft(panel.Restart.Root, HudLayout.HelpInset * _scale,
                (y + HudLayout.HelpButtonGap) * _scale, rect.width - 2f * HudLayout.HelpInset * _scale,
                HudLayout.HelpButtonHeight * _scale);

            panel.Close = HudUi.Button(root, "close", 20f * _scale, 20f * _scale, "×",
                FontSize(HudType.Label), _palette.HudText, () => SetHelpOpen(false));
            HudUi.TopRight(panel.Close.Root, 6f * _scale, 6f * _scale, 20f * _scale, 20f * _scale);

            root.gameObject.SetActive(false);
            return panel;
        }

        private StockpilePanel BuildStockpile()
        {
            Rect rect = HudLayout.Stockpile(Screen.width, _scale);

            var panel = new StockpilePanel { Root = HudUi.Node(_canvasRoot, "stockpile") };
            Place(panel.Root, rect, PlaceCorner.TopLeft);

            // The bank wears the same ink as the cards rather than floating as loose type over the map: it
            // is part of the HUD, and one surface language is what keeps the top row reading as one row.
            HudUi.Shadow(panel.Root, "shadow", 10f, _palette.HudShadow);
            HudBox back = HudUi.Round(panel.Root, "back", _palette.HudPanel, 10f);
            HudUi.StretchFull(back.Rect);
            HudUi.Border(panel.Root, "frame", HudInk.Edge(_palette), 10f, 1f);

            float w = rect.width / _scale;

            // The mark first, then the number: a bank is read as "this much of this", and a mark pinned to
            // the left never collides with a number that grows to the right.
            // The coin is drawn at the demand figure's size, like every other "this shape" mark on the HUD
            // (issue #12), centred on the pill's height.
            const float coin = HudLayout.DemandFigure;
            panel.Icon = HudUi.Icon(panel.Root, "icon", _icons.Mark(MoneyShape));
            At(panel.Icon.rectTransform, 12f, (HudLayout.StockpileHeight - coin) * 0.5f, coin, coin);
            panel.Icon.gameObject.SetActive(false);

            panel.Number = Label(panel.Root, "number", HudType.Display, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            At(panel.Number.Rect, 12f + coin + 6f, 2f, w - (12f + coin + 12f), 40f);

            return panel;
        }

        /// <summary>
        /// The bar: every building this run's table carries, on one row, in the table's own order - which is
        /// also the hotkey order, so the tenth key is the tile under the 0 and the eleventh under the X.
        ///
        /// One row, not a role tab over a shelf of two or three. The table has eleven buildings: seven roles
        /// would have been seven words of chrome to reach a bar that never held more than three tiles, four
        /// of the roles holding one building each. A player building a factory should not pay a click to
        /// look at a catalogue they can see at a glance, and the hotkeys are global anyway - the tab was
        /// only a second answer to "where is the cutter". The roles still exist, but as the info card's
        /// metadata, where a word is worth reading and costs the bar nothing.
        ///
        /// <b>A tutorial hides rather than greys.</b> A run that opens with one building shows one key: the
        /// ten it has not been taught are not on the bar at all (<see cref="VisibleKinds"/>), because a row of
        /// grey LOCKED keys is the first thing a new player would read, and what it says is "there are eleven
        /// things you are not allowed to do". The bar then grows a key at a time, which is the lesson's own
        /// progress made visible without a line of text. The strip is narrowed to fit those keys
        /// (<see cref="HudLayout.BarWidthFor"/>), so a one-key bar is a keyboard of one rather than a shelf with
        /// an empty eleven-tenths.
        ///
        /// <b>No tile on this bar is ever locked.</b> Hiding replaced greying, so the set drawn here is the set
        /// the run offers: a building the map has not handed over - a tutorial still teaching it, or a map
        /// whose operator set stops short of it - is not a key the player cannot press, it is no key at all.
        /// That is why there is no LOCKED state to dress, and why the level list keeps naming what clearing a
        /// map grants: the shelf fills as the lesson hands things over, and the ladder between maps is the
        /// shell's sentence rather than a row of grey keys.
        ///
        /// <b>The current lesson line points here too.</b> Every kind its ghost blueprint names wears a
        /// breathing ring of its own for as long as the line is current (<see cref="BuildTile.Lesson"/>),
        /// which is the thread from the card's sentence and the map's picture to the key that builds it. The
        /// ring is deliberately several times the weight of the tile's hairline and pulses to the brightest
        /// ink, because a pulse that rode the same hairline the selected tile wears made the two states look
        /// alike. A line with no blueprint, a finished lesson and the player's own selected or hovered mark
        /// all leave the ring off.
        ///
        /// Eleven tiles at the strip's 808.25 design units is 66.75 apiece, so a tile is a key rather than a
        /// row: name, then hotkey and price, then the afford line.
        /// </summary>
        private BarPanel BuildBar()
        {
            BuildKind[] kinds = VisibleKinds();
            int count = kinds.Length;

            Rect rect = HudLayout.Bar(Screen.width, Screen.height, _scale, count);
            var panel = new BarPanel { Root = BuildCard("bar", rect, PlaceCorner.BottomLeft) };
            RectTransform root = panel.Root;

            const float pad = HudLayout.BarPad;
            const float gap = HudLayout.BarGap;
            const float tileHeight = 40f;
            float tileWidth = HudLayout.BarTileWidth;

            // Derived from the width the card actually got, because HudLayout.Bar clamps it to the screen: a
            // narrow viewport would otherwise lay the trailing keys over the card's edge. BarTileWidth is the
            // ceiling, so the unclamped path reproduces the design number exactly.
            if (count > 0)
            {
                float fit = (rect.width / _scale - 2f * pad - (count - 1) * gap) / count;
                if (fit < tileWidth) tileWidth = fit;
            }

            panel.TileWidth = tileWidth * _scale;
            panel.TileHeight = tileHeight * _scale;
            panel.Pad = pad * _scale;
            panel.Gap = gap * _scale;
            panel.TilesTop = 6f * _scale;

            for (int i = 0; i < count; i++)
            {
                BuildKind kind = kinds[i];
                var tile = new BuildTile { Kind = kind };

                tile.Root = HudUi.Node(root, "tile" + i);
                HudUi.TopLeft(tile.Root, panel.Pad + i * (panel.TileWidth + panel.Gap), panel.TilesTop,
                    panel.TileWidth, panel.TileHeight);

                tile.Button = tile.Root.gameObject.AddComponent<HudButton>();
                BuildKind captured = kind;
                // The press selects through the same door the hotkey uses, which is what refuses a kind the
                // run has not handed over. No tile here carries one - the bar is the run's own set - but the
                // rule stays in one place rather than being restated per key.
                tile.Button.Pressed = () => SelectKind(captured);

                tile.Button.Fill = HudUi.Round(tile.Root, "back", HudInk.Raised(_palette), ControlRadius);
                HudUi.StretchFull(tile.Button.Fill.Rect);
                tile.Button.Fill.Image.raycastTarget = true;

                // Name across the top, then the hotkey and the price on one line: a key you can read from
                // its shape, its key and its cost without the pointer having to arrive first.
                //
                // The one place the HUD drops its tracking: a key is 66.75 units wide, and the widest name
                // (SPLITTER, ROTATOR) only fits because the +9 % the card labels wear is left off here -
                // spending it would cost the name a point of size, which reads as one key printed smaller
                // rather than as air.
                tile.Name = Label(tile.Root, "name", HudType.Label, FontStyle.Bold,
                    TextAnchor.MiddleLeft);
                At(tile.Name.Rect, 6f, 3f, tileWidth - 12f, 16f);
                tile.Name.Set(_world.Content.Machine(kind).Name, _palette.HudText);
                AutoFit(tile.Name, 9f);

                // The keycap is drawn as a little key so the digit reads as "press this" rather than as a
                // label - the one glyph on the tile that is a control rather than a fact.
                tile.Keycap = HudUi.Round(tile.Root, "keycap", HudInk.Raised(_palette), 4f);
                At(tile.Keycap.Rect, 6f, 21f, 16f, 14f);

                tile.Hotkey = Label(tile.Root, "hotkey", HudType.Micro, FontStyle.Bold,
                    TextAnchor.MiddleCenter);
                At(tile.Hotkey.Rect, 6f, 21f, 16f, 14f);
                // The key is the kind's own ordinal, not this loop's position: a tutorial's bar skips the
                // kinds it has not handed over, and a keycap that renumbered itself as the bar grew would be
                // a label for a key that presses something else.
                tile.Hotkey.Set(BuildInput.HotkeyLabel((int)kind), HudInk.Faint(_palette));

                // The price is a number, not a word, so it carries no tracking either.
                tile.Cost = Label(tile.Root, "cost", HudType.Micro, FontStyle.Bold,
                    TextAnchor.MiddleRight);
                At(tile.Cost.Rect, 24f, 21f, tileWidth - 30f, 14f);
                tile.Cost.Set(_world.Content.Machine(kind).Cost.ToString(), HudInk.Muted(_palette));
                AutoFit(tile.Cost, 7f);

                tile.CostIcon = HudUi.Icon(tile.Root, "costIcon", _icons.Mark(MoneyShape));
                At(tile.CostIcon.rectTransform, tileWidth - 22f, 23.5f, 9f, 9f);
                tile.CostIcon.gameObject.SetActive(false);

                // The fraction of the price the bank covers, along the tile's floor: nine tenths of the
                // way is a belt to lay now, a tenth is a plan for later.
                tile.Afford = HudUi.BarFill(tile.Root, "afford", 6f * _scale, 2f * _scale,
                    (tileWidth - 12f) * _scale, 2f * _scale, 1f);

                tile.Border = HudUi.Border(tile.Root, "frame", _palette.HudAccent, ControlRadius, 1f);
                tile.Border.Show(false);

                // The lesson's own ring, three units thick against the hairline above: "this key is the
                // answer to the current line" has to be a different *weight* of mark, not a brighter shade
                // of the same outline the selected tile wears - the two states were one glance apart.
                tile.Cue = HudUi.Border(tile.Root, "cue", _palette.HudAccent, ControlRadius, 3f);
                tile.Cue.Show(false);

                tile.Selected = kind == _world.SelectedKind;
                tile.Affordable = _world.Economy.CanAfford(kind);

                // Dress the first frame here: the update pass only re-dresses a tile whose state *changed*,
                // so a tile built with the panel's own colour would stay invisible until the player
                // happened to select or hover it.
                DressBuildTile(tile);

                panel.Tiles.Add(tile);
            }

            return panel;
        }

        private InfoPanel BuildInfo()
        {
            Rect rect = HudLayout.Info(Screen.width, Screen.height, _scale);

            var panel = new InfoPanel { Root = BuildCard("info", rect, PlaceCorner.BottomRight) };
            RectTransform root = panel.Root;

            const float w = HudLayout.InfoWidth;
            const float inner = w - CardInset * 2f;

            // ----- the build card: a title, a hairline, then the catalogue's sentences
            panel.BuildRoot = HudUi.Node(root, "build");
            HudUi.StretchFull(panel.BuildRoot);

            panel.BuildTitle = Label(panel.BuildRoot, "title", HudType.Body, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            At(panel.BuildTitle.Rect, CardInset, 4f, inner - 130f, 20f);
            AutoFit(panel.BuildTitle, HudType.Micro);

            panel.BuildRight = Label(panel.BuildRoot, "right", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleRight);
            At(panel.BuildRight.Rect, CardInset, 6f, inner, 16f);

            HudBox buildRule = HudUi.Box(panel.BuildRoot, "rule", HudInk.Rule(_palette));
            At(buildRule.Rect, CardInset, 27f, inner, 1f);

            panel.Blurb = Label(panel.BuildRoot, "blurb", HudType.Label, FontStyle.Normal,
                TextAnchor.UpperLeft, wrap: true);
            At(panel.Blurb.Rect, CardInset, 31f, inner, 48f);

            panel.Stats = Label(panel.BuildRoot, "stats", HudType.Micro, FontStyle.Normal,
                TextAnchor.UpperLeft, wrap: true);
            At(panel.Stats.Rect, CardInset, 82f, inner, 22f);

            panel.Facing = Label(panel.BuildRoot, "facing", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            At(panel.Facing.Rect, CardInset, 108f, 120f, 14f);

            panel.TurnLeft = HudUi.Button(panel.BuildRoot, "turnL", 24f * _scale, 20f * _scale, "<",
                FontSize(HudType.Label), _palette.HudText, () => _rotate?.Invoke(-1));
            panel.TurnRight = HudUi.Button(panel.BuildRoot, "turnR", 24f * _scale, 20f * _scale, ">",
                FontSize(HudType.Label), _palette.HudText, () => _rotate?.Invoke(+1));
            panel.Inspect = HudUi.Button(panel.BuildRoot, "inspect", 52f * _scale, 20f * _scale, "READ",
                FontSize(HudType.Micro), _palette.HudText, PressInspect);

            // ----- the inspect card: the same header grammar, then one row per fact
            panel.InspectRoot = HudUi.Node(root, "inspect");
            HudUi.StretchFull(panel.InspectRoot);

            panel.InspectTitle = Label(panel.InspectRoot, "title", HudType.Body, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            At(panel.InspectTitle.Rect, CardInset, 4f, inner - 130f, 20f);
            AutoFit(panel.InspectTitle, HudType.Micro);

            panel.InspectRight = Label(panel.InspectRoot, "right", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleRight);
            At(panel.InspectRight.Rect, CardInset, 6f, inner, 16f);

            HudBox inspectRule = HudUi.Box(panel.InspectRoot, "rule", HudInk.Rule(_palette));
            At(inspectRule.Rect, CardInset, 27f, inner, 1f);

            panel.Rows = new HudText[InspectRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = Label(panel.InspectRoot, "row" + i, HudType.Label, FontStyle.Normal,
                    TextAnchor.MiddleLeft);
                At(panel.Rows[i].Rect, CardInset, 33f + 15f * i, inner, 14f);
                panel.Rows[i].Show(false);
            }

            // ----- the sorter's filter picker, the one band of the card that is not always there
            //
            // The canvas is the item's own square, quartered - the same figure the machine's badge and the
            // belt items wear - and each quarter is a button. What the buttons paint is a routing rule, so
            // the band carries the mode and the rule in words under it: a player choosing what a machine
            // eats should not have to infer it from a picture.
            panel.Picker = BuildFilterPicker(panel.InspectRoot, rect);

            panel.BuildRoot.gameObject.SetActive(false);
            panel.InspectRoot.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>
        /// The sorter picker as widgets: a quartered canvas of ghost cells and one figure, a mode toggle and
        /// the sentence saying what the picker would route for.
        ///
        /// The ghosts are the empty quadrants and nothing else is: an empty quadrant is drawn as nothing on
        /// the map, and only here - where the player is choosing quadrants - is it a faint cell to click.
        /// The figure itself is a rasterised picture (the HUD has no meshes), so the quadrants' colours come
        /// from the same per-mineral override the map draws with.
        /// </summary>
        private FilterPicker BuildFilterPicker(RectTransform root, Rect card)
        {
            var picker = new FilterPicker();

            picker.Root = HudUi.Node(root, "picker");
            HudUi.TopLeft(picker.Root, 0f, PickerTop * _scale, card.width, HudLayout.InfoPickHeight * _scale);

            picker.Title = Label(picker.Root, "title", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            HudUi.TopLeft(picker.Title.Rect, 12f * _scale, 0f, 120f * _scale, 12f * _scale);
            picker.Title.Set("ROUTES", HudInk.Faint(_palette));

            RectTransform canvas = HudUi.Node(picker.Root, "canvas");
            HudUi.TopLeft(canvas, 12f * _scale, 13f * _scale, CanvasSize * _scale, CanvasSize * _scale);

            // Draw order inside the canvas is the read order: the ghost cells, then the figure over them,
            // then the buttons that claim the clicks.
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Vector2 at = CellOffset(quadrant);

                HudBox ghost = HudUi.Round(canvas, "ghost" + quadrant, GhostCell, 2f);
                HudUi.TopLeft(ghost.Rect, at.x * _scale, at.y * _scale, CellSize * _scale, CellSize * _scale);
                picker.Ghosts[quadrant] = ghost;
            }

            var figure = HudUi.Node(canvas, "figure");
            HudUi.StretchFull(figure);
            picker.Figure = figure.gameObject.AddComponent<RawImage>();
            picker.Figure.raycastTarget = false;

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Vector2 at = CellOffset(quadrant);

                // One button per quadrant, its own label hidden: the picture underneath is the content, and
                // a click on a quarter is a click on that quadrant of the item.
                int captured = quadrant;
                HudButton button = HudUi.Button(canvas, "pick" + quadrant, CellSize * _scale, CellSize * _scale,
                    string.Empty, FontSize(HudType.Micro), _palette.HudText, () => PressFilterQuadrant(captured));
                button.Label.Show(false);
                HudUi.TopLeft(button.Root, at.x * _scale, at.y * _scale, CellSize * _scale, CellSize * _scale);
                picker.Quadrants[quadrant] = button;
            }

            HudUi.Border(canvas, "frame", PanelBorder(), 3f, 1f);

            picker.Mode = HudUi.Button(picker.Root, "mode", ModeWidth * _scale, 18f * _scale, string.Empty,
                FontSize(HudType.Micro), _palette.HudText, PressFilterMode);
            HudUi.TopLeft(picker.Mode.Root, ModeLeft * _scale, 13f * _scale, ModeWidth * _scale, 18f * _scale);

            picker.Caption = Label(picker.Root, "caption", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleLeft);
            HudUi.TopLeft(picker.Caption.Rect, ModeLeft * _scale, 34f * _scale,
                card.width - (ModeLeft + 12f) * _scale, 12f * _scale);

            picker.Root.gameObject.SetActive(false);
            return picker;
        }

        /// <summary>Where one quadrant's cell sits inside the canvas, in design units: north is up, so a
        /// quadrant's own compass corner is its corner of the square.</summary>
        private static Vector2 CellOffset(int quadrant)
        {
            float x = quadrant == Shape.NW || quadrant == Shape.SW ? 0f : CellSize;
            float y = quadrant == Shape.NW || quadrant == Shape.NE ? 0f : CellSize;

            return new Vector2(x, y);
        }

        private BannerPanel BuildBanner()
        {
            var panel = new BannerPanel { Root = HudUi.Node(_canvasRoot, "banner") };
            RectTransform root = panel.Root;

            // The banner's own rectangle, in the layout's y-down screen space: centred, its top edge
            // at 42% of the leftover height - and placed by that top edge, the way every panel is. It is a
            // transient overlay, so it is not on the 22 % budget; 560 wide is what the run's one-line
            // report needs at the display size.
            Rect rect = new Rect((Screen.width - 560f * _scale) * 0.5f, (Screen.height - 170f * _scale) * 0.42f,
                560f * _scale, 170f * _scale);
            HudUi.TopCentre(root, rect.y, rect.width, rect.height);

            HudUi.Shadow(root, "shadow", 12f, _palette.HudShadow);
            HudBox back = HudUi.Round(root, "back", _palette.HudPanel, 12f);
            HudUi.StretchFull(back.Rect);
            HudUi.Border(root, "frame", PanelBorder(), 12f, 1f);

            panel.Label = Label(root, "label", HudType.Display, FontStyle.Bold, TextAnchor.MiddleCenter,
                wrap: true, tracked: true);
            HudUi.Stretch(panel.Label.Rect, 0f, 18f * _scale, 0f, 136f * _scale);

            root.gameObject.SetActive(false);
            return panel;
        }

        private ToastPanel BuildToast()
        {
            var panel = new ToastPanel { Root = HudUi.Node(_canvasRoot, "toast") };
            RectTransform root = panel.Root;

            panel.Group = root.gameObject.AddComponent<CanvasGroup>();
            panel.Group.blocksRaycasts = false;
            panel.Group.interactable = false;
            panel.Group.alpha = 0f;

            HudUi.Shadow(root, "shadow", 8f, _palette.HudShadow);
            panel.Back = HudUi.Round(root, "back", _palette.HudPanel, 8f);
            HudUi.StretchFull(panel.Back.Rect);
            panel.Frame = HudUi.Border(root, "frame", _palette.HudWarn, 8f, 2f);
            panel.Label = Label(root, "label", HudType.Label, FontStyle.Bold, TextAnchor.MiddleCenter,
                tracked: true);
            HudUi.TopLeft(panel.Label.Rect, 14f * _scale, 8f * _scale, 100f, 22f * _scale);

            root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>The cursor tooltip: what is under the pointer, in two or three lines, beside the pointer
        /// itself. It is the answer to the old HUD's worst interaction - with a building selected, reading the
        /// map meant dropping the selection, pressing Escape, clicking and selecting again. Now the map is
        /// always readable at the pointer; the info card keeps its role for the buildings and the pinned tile.
        ///
        /// It takes no clicks and claims no pointer - it is news, not a panel - so a click passes through it
        /// to the tile it describes.</summary>
        private TooltipPanel BuildTooltip()
        {
            var panel = new TooltipPanel { Root = HudUi.Node(_canvasRoot, "tooltip") };
            RectTransform root = panel.Root;

            HudUi.Shadow(root, "shadow", 8f, _palette.HudShadow);
            panel.Back = HudUi.Round(root, "back", _palette.HudPanel, 8f);
            HudUi.StretchFull(panel.Back.Rect);
            panel.Frame = HudUi.Border(root, "frame", PanelBorder(), 8f, 1f);

            panel.Title = Label(root, "title", HudType.Label, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            HudUi.TopLeft(panel.Title.Rect, 10f * _scale, 5f * _scale, 200f, 16f * _scale);

            panel.Rows = new HudText[TooltipRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = Label(root, "row" + i, HudType.Micro, FontStyle.Normal,
                    TextAnchor.UpperLeft);
                HudUi.TopLeft(panel.Rows[i].Rect, 10f * _scale, (23f + 14f * i) * _scale, 200f, 13f * _scale);
                panel.Rows[i].Show(false);
            }

            root.gameObject.SetActive(false);
            return panel;
        }

        // ------------------------------------------------------------------ placing helpers

        private enum PlaceCorner { TopLeft, TopRight, BottomLeft, BottomRight }

        /// <summary>Put a panel root where <see cref="HudLayout"/> says it goes, corner for corner.
        ///
        /// The two systems measure the screen in opposite directions: HudLayout speaks IMGUI's y-down screen
        /// space; the canvas speaks uGUI's y-up. A top-hung piece keeps its own top edge, a bottom-hung piece
        /// its bottom edge (the rect's yMax, converted) - getting one backwards mirrors the whole HUD about
        /// the screen's middle.</summary>
        private void Place(RectTransform rt, Rect rect, PlaceCorner corner)
        {
            switch (corner)
            {
                case PlaceCorner.TopLeft:
                    HudUi.TopLeft(rt, rect.x, rect.y, rect.width, rect.height);
                    break;
                case PlaceCorner.TopRight:
                    HudUi.TopRight(rt, Screen.width - rect.xMax, rect.y, rect.width, rect.height);
                    break;
                case PlaceCorner.BottomLeft:
                    HudUi.BottomLeft(rt, rect.x, Screen.height - rect.yMax, rect.width, rect.height);
                    break;
                case PlaceCorner.BottomRight:
                    HudUi.BottomRight(rt, Screen.width - rect.xMax, Screen.height - rect.yMax, rect.width, rect.height);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corner), corner, "a panel sits in a corner");
            }
        }

        /// <summary>Hang a piece inside its card at the design-unit offset the layout names, through
        /// the one scale the fonts and the panels share.</summary>
        private void At(RectTransform rt, float x, float yFromTop, float width, float height)
            => HudUi.TopLeft(rt, x * _scale, yFromTop * _scale, width * _scale, height * _scale);

        /// <summary>The same, hung off its card's top-right: xFromRight grows leftwards.</summary>
        private void AtRight(RectTransform rt, float xFromRight, float yFromTop, float width, float height)
            => HudUi.TopRight(rt, xFromRight * _scale, yFromTop * _scale, width * _scale, height * _scale);

        // ------------------------------------------------------------------ pointer

        private bool ComputePointerOver()
        {
            if (_status == null) return false;

            Vector2 pointer = Pointer();
            if (pointer.x < 0f) return false;

            if (Over(_status.Root, pointer) || Over(_controls.Root, pointer) ||
                Over(_bar.Root, pointer) || Over(_info.Root, pointer))
                return true;

            if (_helpOpen && Over(_help.Root, pointer)) return true;

            if (ShowTutorial && _tutorial != null && _tutorial.Root.gameObject.activeSelf &&
                Over(_tutorial.Root, pointer))
                return true;

            return _tasks != null && _tasks.Root.gameObject.activeSelf && Over(_tasks.Root, pointer);
        }

        /// <summary>The pointer in screen pixels, or (-1, -1) where there is no mouse to ask: a touch
        /// or a headless runner has no panel to be over.</summary>
        private static Vector2 Pointer()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : new Vector2(-1f, -1f);
        }

        private static bool Over(RectTransform rt, Vector2 screenPoint)
            => RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);

        // ------------------------------------------------------------------ the tutorial card

        /// <summary>The one piece of the HUD that explains the game rather than reporting it: up only while
        /// the run is on a map that asks for it - the same predicate the camera rig is given, so the card and
        /// the map's bound cannot disagree. On a map that teaches, this is the reference card behind <c>T</c>;
        /// the lesson itself is <see cref="UpdateTasks"/>.</summary>
        private void UpdateTutorial()
        {
            bool up = ShowTutorial;
            if (up != _tutorialUp)
            {
                _tutorialUp = up;
                _tutorial.Root.gameObject.SetActive(up);
            }

            // While the lesson is still teaching, this card is a detour the player may not have meant to take
            // (T swaps the cards), so the button says where the same key goes back to. Once the corner is the
            // reference card's own again, the word is the plain one.
            string hide = HasTutorial && !_world.Tutorial.Complete ? "TASKS  T" : "HIDE  T";
            if (hide != _hideWord)
            {
                _hideWord = hide;
                _tutorial.Hide.Label.Set(hide, HudInk.Muted(_palette));
            }
        }

        // ------------------------------------------------------------------ the task list

        /// <summary>
        /// The lesson's card: the line being taught, under the ones already ticked, in the corner the reference
        /// card otherwise wears.
        ///
        /// It prints the <b>history and the present</b> and nothing else - the lines still to come are not
        /// listed, so what the player reads is what they have been taught, not a syllabus. The ticked lines stay
        /// up (muted) because they are the record of what the bar has already grown by: a line vanishing the
        /// moment it was satisfied would take the answer to "why do I have a belt now" with it.
        ///
        /// The timestamp that retires the card is written here rather than in the tutorial state, because "how
        /// long a card stays on screen" is the HUD's business and not a rule of the run - the simulation has no
        /// opinion about a fade-out.
        /// </summary>
        private void UpdateTasks()
        {
            if (_tasks == null || _world == null) return;

            TutorialState tutorial = _world.Tutorial;

            if (tutorial.Complete)
            {
                if (_tutorialDoneAt < 0f) _tutorialDoneAt = Time.unscaledTime;
            }
            else
            {
                _tutorialDoneAt = -1f;
            }

            UpdateLessonNotice(tutorial);

            bool up = TaskListUp;

            // Asked of the panel rather than of a cached flag. The lesson's last line also changes the bar's
            // key set, so this frame's update went through RebuildPanels and returned - and the panel that
            // rebuild made is inactive, while the completion hold's timestamp is stamped here, on the frame
            // after. A flag forced during the rebuild is therefore already wrong by the time the card is
            // wanted; the object knows whether it is on screen.
            if (up != _tasks.Root.gameObject.activeSelf) _tasks.Root.gameObject.SetActive(up);

            if (!up) return;
            if (tutorial.StepsCompleted == _seenTaskStep && tutorial.Complete == _seenTaskComplete &&
                _hintPinned == _seenHintPinned) return;

            _seenTaskStep = tutorial.StepsCompleted;
            _seenTaskComplete = tutorial.Complete;
            _seenHintPinned = _hintPinned;

            _tasks.Title.Set(tutorial.Complete ? "TUTORIAL COMPLETE" : "TUTORIAL", _palette.HudText);
            _tasks.Count.Set(tutorial.StepsCompleted + " / " + tutorial.Count, HudInk.Muted(_palette));
            // With the lesson over there is no count left to print, and the heading has the row to itself.
            _tasks.Count.Show(!tutorial.Complete);
            _tasks.Body.Set(TaskLines(tutorial), HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.18f));

            // The pin is the card's own copy of what the toast said once: help that stays where the line
            // lives. It wears the card's second voice - italic, muted - so it reads as an answer offered,
            // not as another task.
            bool hint = _hintPinned && !tutorial.Complete && !string.IsNullOrEmpty(tutorial.Current.Hint);
            _tasks.Hint.Show(hint);
            if (hint) _tasks.Hint.Set(tutorial.Current.Hint, HudInk.Muted(_palette));
        }

        /// <summary>
        /// The lesson speaking for itself: a line ticked, the end of the lesson, and - for a line that has
        /// stood unsolved too long - what the rule is actually asking for. The first two are one sentence in
        /// the strip a refusal uses; the hint is said there once <em>and</em> pinned into the task card until
        /// the line ticks, because a stuck player is exactly the one the strip talks over - refusals outrank
        /// notices there, and a glance away costs a one-shot sentence. The pin survives both.
        ///
        /// The stuck timer runs on the world's own tick count rather than the wall clock: a paused or finished
        /// run does not tick, so time spent away is never counted as time spent stuck.
        /// </summary>
        private void UpdateLessonNotice(TutorialState tutorial)
        {
            int steps = tutorial.StepsCompleted;

            // A map change is a new world and a restart rewinds this one - the lesson has nothing to report on
            // either, and the idle timer belongs to the line, not to the clock.
            if (!ReferenceEquals(_noticeWorld, _world) || steps < _noticedStep || _world.TickCount < _stepTick)
            {
                _noticeWorld = _world;
                _noticedStep = steps;
                _stepTick = _world.TickCount;
                _hintStep = -1;
                _hintPinned = false;
                return;
            }

            if (steps > _noticedStep)
            {
                _noticedStep = steps;
                _stepTick = _world.TickCount;
                _hintStep = -1;
                _hintPinned = false;
                Notice(TaskDoneNotice(tutorial, steps - 1), NoticeSeconds);
                return;
            }

            if (tutorial.Complete || _hintStep == steps) return;
            if (_world.TickCount - _stepTick < HintDelayTicks) return;

            string hint = tutorial.Current.Hint;
            if (string.IsNullOrEmpty(hint)) return;

            _hintStep = steps;
            _hintPinned = true;
            Notice(hint, HintSeconds);
        }

        /// <summary>What a ticked line is worth saying out loud: its number, and the buildings it just put on
        /// the bar. The last line's reward is the whole bar, so that is what it says.</summary>
        private string TaskDoneNotice(TutorialState tutorial, int index)
        {
            if (tutorial.Complete) return "TUTORIAL COMPLETE - THE WHOLE BAR IS UNLOCKED";

            var opened = new StringBuilder();
            BuildKind[] hands = tutorial.TaskAt(index).Opens;
            for (int i = 0; i < hands.Length; i++)
            {
                if (!_world.Content.TryMachine(hands[i], out MachineDef def)) continue;
                if (opened.Length > 0) opened.Append(" + ");
                opened.Append(def.Name.ToUpperInvariant());
            }

            string head = "TASK " + (index + 1) + " DONE";
            return opened.Length == 0 ? head : head + " - " + opened + " UNLOCKED";
        }

        /// <summary>The card's body as rich text: one numbered line per ticked task and the current one, the
        /// current bright and the rest muted. The current line owns the one accent numeral, the same rhythm the
        /// reference card's five steps use - so the two cards, which share one corner, also share a voice - and a
        /// ticked line's numeral is muted with its text, because a number that still looks live is the one thing
        /// that would make the card read as a syllabus rather than as progress.
        ///
        /// Colour is set per run of text rather than by the label's own colour, because one label carries both
        /// states; the label's colour is the base the ticks sit over.</summary>
        private string TaskLines(TutorialState tutorial)
        {
            string accent = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudAccent);
            string done = "#" + ColorUtility.ToHtmlStringRGB(HudInk.Muted(_palette));
            string live = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudText);

            // A pinned hint takes one row of the budget - four tasks and the answer rather than five tasks -
            // because the corner's height is the invariant the hint is not allowed to move.
            int budget = _hintPinned ? MaxTaskLines - 1 : MaxTaskLines;
            int last = Mathf.Min(tutorial.StepsCompleted, tutorial.Count - 1);
            int first = Mathf.Max(0, last - (budget - 1));

            var lines = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                if (lines.Length > 0) lines.Append('\n');

                bool current = i == tutorial.StepsCompleted;
                lines.Append("<color=").Append(current ? accent : done).Append('>').Append(i + 1)
                    .Append("</color>  <color=").Append(current ? live : done).Append('>')
                    .Append(tutorial.TaskAt(i).Text).Append("</color>");
            }

            return lines.ToString();
        }

        // ------------------------------------------------------------------ toast

        /// <summary>
        /// The strip under the controls, in two voices. A <b>refusal</b> is the answer to the click the player
        /// just made, so it wins the strip and fades on the event's own window; a <b>notice</b> is the lesson
        /// talking - a line ticked, a hint after a long wait - and expires by itself. One widget, because both
        /// are a single sentence at the top of the screen for a few seconds, and two widgets would fight for
        /// the same place.
        /// </summary>
        private void UpdateToast()
        {
            bool refused = TryLastRefusal(out SimEvent refusal);
            bool noticed = !refused && NoticeLive;
            bool live = refused || noticed;

            if (live != _toastShown)
            {
                _toast.Root.gameObject.SetActive(live);
                _toastShown = live;
            }

            if (!live)
            {
                _toastAlpha = 0f;
                _toast.Group.alpha = 0f;
                return;
            }

            string text = refused ? RefusalText(refusal) : _noticeText;
            Color ink = refused ? _palette.HudWarn : _palette.HudAccent;
            float fade = refused ? RefusalFadeOf(_world.TickCount - refusal.Tick) : NoticeFadeOf();

            // Laid out when the sentence changes rather than every frame: its width comes from a measured
            // preferred height, which is this method's expensive call. The strip above it moves with the
            // task card, so that is the other thing a layout depends on.
            if (_toastText != text || _toastTutorialUp != TutorialUp)
            {
                _toastText = text;
                _toastTutorialUp = TutorialUp;

                float padding = 14f * _scale;
                float width = Mathf.Min(_toast.Label.PreferredWidth + 2f * padding,
                    Screen.width - 2f * HudLayout.Margin * _scale);
                float height = _toast.Label.PreferredHeight + 16f * _scale;

                Rect toast = HudLayout.Toast(Screen.width, _scale, width, height, TutorialUp);
                HudUi.TopCentre(_toast.Root, toast.y, toast.width, toast.height);
                HudUi.TopLeft(_toast.Label.Rect, padding, 8f * _scale, width - 2f * padding, height - 16f * _scale);
            }

            _toastAlpha = fade;
            _toast.Group.alpha = fade;
            _toast.Back.Set(WithAlpha(_palette.HudPanel, fade));
            _toast.Frame.Set(WithAlpha(ink, fade));
            _toast.Label.Set(_toastText, WithAlpha(ink, fade));
        }

        /// <summary>Say one sentence in the strip for a few seconds. Replaced by the next notice and by
        /// nothing else - except a refusal, which is the player's own click answered and outranks news the
        /// lesson volunteered.</summary>
        private void Notice(string text, float seconds)
        {
            _noticeText = text;
            _noticeFrom = Time.unscaledTime;
            _noticeFor = seconds;
        }

        /// <summary>The answer to a hotkey the lesson is keeping dead: the kind's name and whose move it is,
        /// one sentence in the strip. Called by the driver when the input latch refuses a digit - silence
        /// reads as a broken key, and the tile missing from the bar is the lesson's own doing, so the lesson
        /// says so. A kind the map itself refuses never gets here (<see cref="SimWorld.LockedByTutorial"/>).
        /// </summary>
        public void NoticeLocked(BuildKind kind)
        {
            if (_world == null || !_world.Content.TryMachine(kind, out MachineDef def)) return;

            Notice("NOT YET - the " + def.Name + " is the lesson's to hand over", NoticeSeconds);
        }

        /// <summary>Is a notice still inside the window it is shown for?</summary>
        private bool NoticeLive => _noticeText != null && Time.unscaledTime - _noticeFrom < _noticeFor;

        /// <summary>Full strength for most of the notice's life, then out: the sentence was not asked for, so
        /// it leaves rather than waiting to be dismissed.</summary>
        private float NoticeFadeOf()
        {
            float elapsed = Time.unscaledTime - _noticeFrom;
            float holding = _noticeFor * (1f - NoticeFade);
            return elapsed <= holding ? 1f : Mathf.Clamp01((_noticeFor - elapsed) / (_noticeFor * NoticeFade));
        }

        /// <summary>The last refused placement, if it is still inside the window it is shown for -
        /// the buffer's own window question, not a hand-composed age check.</summary>
        private bool TryLastRefusal(out SimEvent refusal)
            => _world.Events.TryLastWithin(SimEventKind.PlacementRejected, _world.TickCount - RefusalTicks, out refusal);

        /// <summary>Full strength for most of the window, then out: the message is read in the second
        /// after the click, and the fade is only there so it does not disappear between two blinks.</summary>
        private static float RefusalFadeOf(int age)
        {
            float holding = RefusalTicks * (1f - RefusalFade);
            return age <= holding ? 1f : Mathf.Clamp01((RefusalTicks - age) / (RefusalTicks * RefusalFade));
        }

        /// <summary>Why the placement was refused, in the terms the player used: what they tried to
        /// build, what it costs, and what is in the way.</summary>
        private string RefusalText(in SimEvent refusal)
        {
            MachineDef def = _world.Content.Machine(refusal.Building);

            switch (refusal.Reason)
            {
                case RejectionReason.NoFunds:
                    return "NOT ENOUGH " + Plural(Shape.MineralName(_world.Map.CurrencyMineral), 2)
                               .ToUpperInvariant() +
                           " - " + def.Name + " costs " + Mathf.RoundToInt(refusal.Amount) + ", you have " +
                           _world.Economy.Shapes;
                case RejectionReason.Occupied:
                    return "BLOCKED - something already stands on " + refusal.Cell;
                case RejectionReason.BadGround:
                    return def.Tile == TileKind.Drill
                        ? "NO ORE - a drill mines only the patch it stands on"
                        : "ON ORE - only a drill may stand on a patch";
                case RejectionReason.Doorway:
                    return "A DOORWAY - waves walk in at " + refusal.Cell +
                           ", and nothing solid may stand there";
                case RejectionReason.Locked:
                    // The run has not handed this building over yet - a map's operator set, or a tutorial
                    // line still to be taught. The bar does not carry it at all, so this is the keyboard's
                    // answer to a key the bar no longer offers.
                    return "NOT ON THIS MAP - " + def.Name + " is an operator this run has not unlocked";
                default:
                    return "OFF THE MAP - nothing can be built on " + refusal.Cell;
            }
        }

        // ------------------------------------------------------------------ stockpile

        /// <summary>
        /// Keep every money mark on the HUD on the run's own coin. Two marks read it - the stockpile's and
        /// each cost tile's - and both are repainted from here rather than at their own call sites, so a map
        /// change cannot leave the previous map's mineral on one of them. The comparison is the shape itself,
        /// which is a <see cref="uint"/>: a frame where the map has not changed costs one integer compare.
        /// </summary>
        private void UpdateMoneyMarks()
        {
            Shape money = MoneyShape;
            if (money == _moneyDrawn) return;

            _moneyDrawn = money;
            HudIcons.Icon coin = _icons.Mark(money);

            if (_stockpile != null && _stockpile.Icon != null) Paint(_stockpile.Icon, coin);
            if (_bar == null) return;

            for (int i = 0; i < _bar.Tiles.Count; i++)
                if (_bar.Tiles[i].CostIcon != null) Paint(_bar.Tiles[i].CostIcon, coin);
        }

        /// <summary>Point one mark at one rasterised item. An icon the palette cannot draw carries no
        /// texture, and a <see cref="RawImage"/> with none draws nothing rather than a white square - so a
        /// mineral with no shipped mark leaves the number bare instead of lying about it.</summary>
        private static void Paint(RawImage mark, HudIcons.Icon icon)
        {
            mark.texture = icon.Texture;
            mark.uvRect = icon.Uv;
        }

        private void UpdateStockpile()
        {
            BuildKind? selected = _world.SelectedKind;
            Color numberColour = HudInk.Paper(_palette);
            if (selected.HasValue && !_world.Economy.CanAfford(selected.Value))
                numberColour = _palette.CursorNoFunds;
            if (_toastAlpha > 0f)
                numberColour = WithAlpha(_palette.HudWarn, _toastAlpha);

            int shapes = _world.Economy.Shapes;
            if (shapes != _stockpileCount)
            {
                _stockpileCount = shapes;
                _stockpile.Number.Set(shapes.ToString(), numberColour);

                // The mark stands to the left of the number, so it never has to move as the number grows;
                // it is shown only when the palette could draw the map's mineral.
                if (_stockpile.Icon != null)
                    _stockpile.Icon.gameObject.SetActive(_stockpile.Icon.texture != null);
            }
            else _stockpile.Number.SetColour(numberColour);

            // The texture carries the shape's colour; the tint carries only what the number's own fade
            // asks for - a fading number fades its shape with it.
            if (_stockpile.Icon != null && !Mathf.Approximately(_stockpileAlpha, numberColour.a))
            {
                _stockpileAlpha = numberColour.a;
                Color tint = _stockpile.Icon.color;
                tint.a = numberColour.a;
                _stockpile.Icon.color = tint;
            }
        }

        // ------------------------------------------------------------------ status

        /// <summary>The status card, in the order the questions come: what the run is, whether the bank is
        /// holding, how much of the wait is left, what the wave is made of, whether the defence can shoot it,
        /// and where the player is not looking. Two of those are bars rather than sentences, because a length
        /// is read at a glance where a number has to be read twice.
        ///
        /// The diagnostics are read before the card is drawn, because the stripe down its left edge reports
        /// them: a jam, a defence that cannot shoot, or a Core being chewed on turns the stripe warning-
        /// coloured - the one thing on the HUD that says "read me" without being read.
        ///
        /// The card is rebuilt when the world ticks: a paused run holds what it said.</summary>
        private void UpdateStatus()
        {
            if (!_statusDirty &&
                _statusTick == _world.TickCount &&
                _statusSeen == (int)_world.Status &&
                _statusPaused == _world.Paused &&
                _statusGear == GearNow)
                return;

            _statusDirty = false;
            _statusTick = _world.TickCount;
            _statusSeen = (int)_world.Status;
            _statusPaused = _world.Paused;
            _statusGear = GearNow;

            _world.Machines.CountTurrets(out int total, out int armed);
            bool starved = armed < total;
            int jams = _world.Belts.JamCount;
            int blocked = _world.Belts.BlockedCount;
            _world.Machines.CountStalled(out int stalled);
            bool hurting = _world.Core.HealthFraction <= 0.4f;
            ThreatRadar.Summary outOfView = ReadOutOfView();
            bool coreHit = ThreatRadar.CoreIsBeingHit(_world, _palette.Radar.DangerTicks);

            _status.Title.Set(MapLine(), _palette.HudText);
            _status.WaveTag.Set(WaveTag(),
                CanStartWaveNow ? _palette.HudAccent : HudInk.Muted(_palette));

            // The clock as a length above the clock as a number: the bar is what a player reads without
            // taking their eyes off the map, and the line underneath says what the wait is for.
            DrawHealthBar();
            DrawWaveClock();

            _status.News.Set(WaveNews(), _palette.HudText);
            UpdateComposition();

            string production = ProductionLine(jams, blocked, stalled, starved, total);
            bool productionNews = production != null;
            _status.Production.Show(productionNews);
            if (productionNews) _status.Production.Set(production, ProductionColour(jams, blocked, stalled, starved));

            string threat = ThreatWarning(in outOfView);
            bool threatNews = threat != null;
            _status.Threat.Show(threatNews);
            if (threatNews) _status.Threat.Set(threat, ThreatColour(in outOfView));

            // The footer band: diagnostics when there are any, otherwise the two stable facts - the run's
            // seed and the gear. A reserved band that is empty most of the run reads as a broken card; one
            // that always says something useful reads as a footer.
            bool quiet = !productionNews && !threatNews;
            _status.Seed.Show(quiet);
            _status.Speed.Show(quiet);
            if (quiet)
            {
                _status.Seed.Set(SeedLine(), HudInk.Muted(_palette));
                _status.Speed.Set(SpeedFooter(), HudInk.Muted(_palette));
            }
        }

        /// <summary>
        /// The production line, said only when something is wrong: a segment that cannot pass its item on
        /// first - it stops the run, not just one machine - then a machine holding a piece it has nowhere to
        /// put, then a gun that cannot shoot, then, once a wave
        /// is walking in, the absence of anything to shoot it with. When the defence is fed and the belts
        /// are clear the line stays silent: "all clear" printed every second is a line nobody reads. Null
        /// means no news, and the line is hidden.
        ///
        /// Jammed and blocked are two words for one symptom, and both are counted here because they are the
        /// two ways a <em>belt</em> stops: a jam is an item a machine refused, a block is an item with nowhere
        /// to go at all. The block is the one that used to be silent - a line ending in the air reddened nothing,
        /// so a factory came to a standstill with nothing on the HUD to say why. The stalled machine is the
        /// third of the three and the last one to have been silent: a machine that has eaten its item and
        /// has no outlet for it is neither a jam nor a block, so without the count here the belts behind it
        /// queue and the card says nothing. A <em>full</em> outlet is not that - a line loaded behind a full
        /// consumer is a line doing its job - so the count is of machines with nowhere to put the piece, not of
        /// machines that are merely waiting for room.
        /// </summary>
        private string ProductionLine(int jams, int blocked, int stalled, bool starved, int total)
        {
            if (jams > 0 && blocked > 0)
                return jams + " jammed, " + blocked + " blocked   ·   right-click to clear";

            if (jams > 0)
                return jams + " jammed segment" + (jams == 1 ? "" : "s") + "   ·   right-click one to clear it";

            if (blocked > 0)
                return blocked + " blocked - a line ends in the air   ·   right-click to clear";

            if (stalled > 0)
                // The third way a line stops, and the one that had no words before: the machine has eaten its
                // item and has no outlet to pass it on to, so the belts behind it queue and nothing upstream
                // is ever "blocked". Only a <em>missing</em> outlet is said here - a full one is a loaded line,
                // and a gun waiting for the next wave is not a fault the player can go and fix.
                return stalled + " machine" + (stalled == 1 ? "" : "s") +
                       " stalled - an outlet is missing   ·   click one";

            if (starved)
                // The chamber is universal, so a starved gun is not missing the right shape - it is missing
                // every shape. The words are MachineReading's live note, said here about the fleet: an empty
                // chamber is a gun with nothing in hand, which is also why its badge is blank.
                return "a starved gun - an empty chamber, nothing waiting on its lines";

            WaveDirector waves = _world.Waves;
            if (total == 0 && (waves.Phase == WavePhase.Running ||
                    (waves.Phase == WavePhase.Intermission && waves.CurrentWave > 0)))
                return "no turrets standing - the wave walks in unopposed";

            return null;
        }

        private Color ProductionColour(int jams, int blocked, int stalled, bool starved)
            => jams > 0 || blocked > 0 || stalled > 0 || starved ? _palette.HudWarn : _palette.HudText;

        /// <summary>What is out of view, said in the register the arrows cannot: how many, how far, and
        /// whether it is hitting anything. The arrows are drawn from the same <see cref="ThreatRadar.Summary"/>,
        /// so a card claiming the map is quiet while an arrow is live is not possible. Null - and a hidden
        /// line - when there is nothing out of view: the locator's silence is the good news.</summary>
        private string ThreatWarning(in ThreatRadar.Summary outOfView)
        {
            if (_world.Status != GameStatus.Playing) return null;
            if (!outOfView.HasAnything) return null;

            var text = new StringBuilder();

            if (outOfView.HasThreats)
            {
                text.Append(outOfView.Threats)
                    .Append(outOfView.Threats == 1 ? " enemy" : " enemies")
                    .Append(" out of view");

                if (outOfView.Nearest < float.MaxValue)
                    text.Append("   ·   nearest ").Append(Mathf.RoundToInt(outOfView.Nearest));

                // The word that matters is the verb: an enemy out of view is news, an enemy out of view that
                // is hitting something is the reason the arrows pulse.
                if (outOfView.Urgent) text.Append("   ·   hitting");
            }

            if (outOfView.HasStops)
                AppendSeparator(text)
                    .Append(outOfView.Stops)
                    .Append(" stopped");

            if (outOfView.CoreIsOut)
                AppendSeparator(text).Append("Core out   ·   C goes back");

            return text.ToString();
        }

        private static StringBuilder AppendSeparator(StringBuilder text)
            => text.Length == 0 ? text : text.Append("   ·   ");

        /// <summary>How loud the threat line is said: warning when it is pointing at something doing damage,
        /// plain otherwise. The line only appears when the locator has something to point at (see
        /// <see cref="ThreatWarning"/>), so there is no quiet colour to fall back to.</summary>
        private Color ThreatColour(in ThreatRadar.Summary outOfView)
            => outOfView.Urgent ? _palette.HudWarn : _palette.HudText;

        /// <summary>
        /// The bill of materials as chips, in two rows: the wave the clock is counting to - the one being
        /// fought while a wave runs - and the wave after it, because "what do I need" is asked about both
        /// while there is still time to build a line for the second.
        ///
        /// Per <em>group</em>, because a group is what a wave is made of: the count, the archetype and the
        /// round that group demands. The demand is the group's own (<see cref="SpawnGroup.Weakness"/>), not
        /// the archetype's, which is what makes the same enemy a different puzzle each wave.
        ///
        /// The rows are laid out by measurement, once per change, at whatever widths the words ask for. A
        /// wave too varied to fit drops the names and keeps the numbers and figures - the count and the round
        /// are the decision, the name the courtesy - and both rows drop them together so the pair stays a
        /// pair.
        /// </summary>
        private void UpdateComposition()
        {
            WaveDirector waves = _world.Waves;

            // NextWave is the number a reader prints, in every phase: while a wave runs it is that wave's
            // number, and between waves the number counting down. Both definitions are null once the run is
            // out of waves, which is the whole of "there is nothing left to brief".
            WaveDefinition next = waves.NextWaveDefinition;
            WaveDefinition after = waves.WaveAfterNextDefinition;

            var key = new StringBuilder().Append(waves.NextWave).Append('/').Append((int)waves.Phase);
            AppendWaveKey(key, next);
            key.Append('|');
            AppendWaveKey(key, after);

            string signature = key.ToString();
            if (signature == _compKey) return;
            _compKey = signature;

            if (next == null)
            {
                HideChips();
                return;
            }

            ChipEntries(next, _nextEntries);
            ChipEntries(after, _afterEntries);

            // "NOW" and "THEN" rather than the wave numbers: the title tag already names the wave, and a
            // label that starts with a digit reads as one more count beside the chips.
            string nextLabel = "NOW";
            string afterLabel = "THEN";

            if (!LayoutChips(_status.Chips, _status.NextLabel, nextLabel, _nextEntries, ChipsTop, withNames: true) ||
                !LayoutChips(_status.ChipsAfter, _status.AfterLabel, afterLabel, _afterEntries, ChipsAfterTop, withNames: true))
            {
                if (!LayoutChips(_status.Chips, _status.NextLabel, nextLabel, _nextEntries, ChipsTop, withNames: false) ||
                    !LayoutChips(_status.ChipsAfter, _status.AfterLabel, afterLabel, _afterEntries, ChipsAfterTop, withNames: false))
                    HideChips();
            }
        }

        /// <summary>One wave's groups as chip entries, reused per frame so a briefing change allocates
        /// nothing. A group past the row's capacity is dropped: more kinds than chips in one wave is a
        /// content-table question, not a layout one.</summary>
        private void ChipEntries(WaveDefinition wave, List<ChipEntry> into)
        {
            into.Clear();
            if (wave == null) return;

            ContentDatabase content = _world.Content;
            for (int g = 0; g < wave.Groups.Length && into.Count < ChipCapacity; g++)
            {
                SpawnGroup group = wave.Groups[g];
                into.Add(new ChipEntry(group.Count, content.Enemy(group.Kind).Name, group.Weakness));
            }
        }

        /// <summary>What makes one briefing different from another, so the rows are laid out when the demand
        /// changes rather than every frame: the enemies, their counts and their demanded rounds.</summary>
        private static void AppendWaveKey(StringBuilder key, WaveDefinition wave)
        {
            if (wave == null) return;

            for (int g = 0; g < wave.Groups.Length; g++)
            {
                SpawnGroup group = wave.Groups[g];
                key.Append(':').Append((int)group.Kind).Append('x').Append(group.Count)
                    .Append('@').Append(group.Weakness.Code);
            }
        }

        /// <summary>Lay one briefing row left to right, after its own label. False when the row does not fit
        /// - the caller tries the pair again without the names, because a name dropped is a courtesy lost
        /// while a chip clipped is a count the player cannot read.</summary>
        private bool LayoutChips(Chip[] row, HudText label, string labelText, List<ChipEntry> entries,
            float top, bool withNames)
        {
            label.Set(labelText, HudInk.Muted(_palette));
            label.Show(entries.Count > 0);

            float x = CardInset * _scale;
            if (entries.Count > 0) x += (ChipLabelWidth + 4f) * _scale;

            for (int i = 0; i < ChipCapacity; i++)
            {
                Chip chip = row[i];
                if (i >= entries.Count)
                {
                    HideChip(chip);
                    continue;
                }

                ChipEntry entry = entries[i];

                chip.Count.Set(entry.Count.ToString());
                chip.Name.Set(entry.Name);
                chip.Name.Show(withNames);

                float countWidth = chip.Count.PreferredWidth;
                float nameWidth = withNames ? chip.Name.PreferredWidth : 0f;
                // Text, a gap, the figure, a hair of air: the figure is the demand, so it gets the chip's
                // full height rather than whatever room the words leave.
                float chipWidth = (withNames ? countWidth + 4f * _scale + nameWidth : countWidth) +
                                  (5f + 4f + HudLayout.DemandFigure + ChipIconPad) * _scale;
                float textTop = top + (ChipHeight - 12f) * 0.5f;

                if (x + chipWidth > _status.Root.rect.width - CardInset * _scale) return false;

                chip.Back.Show(true);
                chip.Count.Show(true);
                HudUi.TopLeft(chip.Back.Rect, x, top * _scale, chipWidth, ChipHeight * _scale);
                HudUi.TopLeft(chip.Count.Rect, x + 5f * _scale, textTop * _scale,
                    countWidth + 2f * _scale, 12f * _scale);
                chip.Count.SetColour(_palette.HudText);

                if (withNames)
                {
                    HudUi.TopLeft(chip.Name.Rect, x + 5f * _scale + countWidth + 4f * _scale,
                        textTop * _scale, nameWidth + 2f * _scale, 12f * _scale);
                    chip.Name.SetColour(HudInk.Muted(_palette));
                }

                if (chip.Icon != null)
                {
                    HudIcons.Icon icon = _icons.Mark(entry.Round);
                    if (icon.Valid)
                    {
                        chip.Icon.texture = icon.Texture;
                        chip.Icon.uvRect = icon.Uv;
                        HudUi.TopLeft(chip.Icon.rectTransform,
                            x + chipWidth - (HudLayout.DemandFigure + ChipIconPad) * _scale,
                            (top + ChipIconPad) * _scale,
                            HudLayout.DemandFigure * _scale, HudLayout.DemandFigure * _scale);
                        chip.Icon.gameObject.SetActive(true);
                    }
                    else chip.Icon.gameObject.SetActive(false);
                }

                x += chipWidth + 6f * _scale;
            }

            return true;
        }

        private void HideChips()
        {
            for (int i = 0; i < ChipCapacity; i++)
            {
                HideChip(_status.Chips[i]);
                HideChip(_status.ChipsAfter[i]);
            }

            _status.NextLabel.Show(false);
            _status.AfterLabel.Show(false);
        }

        private static void HideChip(Chip chip)
        {
            chip.Back.Show(false);
            chip.Count.Show(false);
            chip.Name.Show(false);
            if (chip.Icon != null) chip.Icon.gameObject.SetActive(false);
        }

        private void DrawHealthBar()
        {
            Palette.RadarLook look = _palette.Radar;
            bool hit = ThreatRadar.TryCoreHit(_world, look.DangerTicks, out float damage, out int age);
            float life = hit ? Mathf.Clamp01(1f - age / (float)Mathf.Max(1, look.DangerTicks)) : 0f;

            float fraction = _world.Core.HealthFraction;

            // Full Core reads as paper: the brightest thing in the rail, which is what "healthy" looks like
            // without spending a fourth hue on it. Only damage turns it warning-coloured.
            _status.Health.Set(fraction, fraction > 0.4f ? HudInk.Paper(_palette) : _palette.HudWarn);

            // The metre's track carries the hit: it goes warning-coloured for the window the Core was last
            // chewed, the same window the screen's danger frame fades over.
            _status.HealthTrack.Set(hit
                ? WithAlpha(_palette.HudWarn, 0.16f + 0.30f * life)
                : HudInk.Raised(_palette));

            // One number, said once: what is left, and - while a hit is fresh - what the last one cost.
            string value = Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp);
            if (hit) value += "   -" + DamageText(damage) + " hp";
            _status.HealthValue.Set(value, hit
                ? WithAlpha(_palette.HudWarn, 0.45f + 0.55f * life)
                : HudInk.Muted(_palette));
        }

        /// <summary>A hit on the Core, printed without a decimal tail when it does not have one.</summary>
        private static string DamageText(float damage)
            => damage == Mathf.RoundToInt(damage)
                ? Mathf.RoundToInt(damage).ToString()
                : damage.ToString("0.0");

        /// <summary>The wave clock as a length rather than a number: how much of the countdown is spent, or
        /// how much of the running wave is killed. One bar, two readings, because both answer "how long do I
        /// have" without a sentence.
        ///
        /// <see cref="WavePhase.Held"/> reads as an empty bar, which is the truth: the opening waits for the
        /// player's first building, not for time.</summary>
        private void DrawWaveClock()
        {
            float fraction = WaveClockFraction();

            // One hue however the clock reads: a wave being spent is good news, and good news is the
            // focus hue here - the HUD spends no second colour on it (HudInk.Good is HudAccent by design).
            Color fill = _palette.HudAccent;

            _status.Clock.Set(fraction, fill);
        }

        private float WaveClockFraction()
        {
            WaveDirector waves = _world.Waves;
            switch (waves.Phase)
            {
                case WavePhase.Held: return 0f;
                case WavePhase.Over: return 1f;

                case WavePhase.Intermission:
                    // Guarded because the fraction divides by it: a table row that promised 0 seconds starts
                    // its wave on the same tick, so there is no wait to have spent any of.
                    if (waves.IntermissionTotal <= 0f) return 1f;
                    return Mathf.Clamp01(1f - waves.IntermissionRemaining / waves.IntermissionTotal);

                case WavePhase.Running:
                    // Definition, not the map's authored table: past the table the director generates, and
                    // indexing the array with a generated wave's number is how this used to throw.
                    WaveDefinition current = waves.Definition(waves.CurrentWave);
                    if (current == null || current.Total <= 0) return 1f;
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return Mathf.Clamp01(1f - left / (float)current.Total);

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its clock bar");
            }
        }

        /// <summary>The run's identity, on the title line's left end: the map in the campaign and - when the
        /// last map re-enters as an endless run - the word for it. The wave used to share this line and
        /// pushed the map's name off the end whenever the two were long; it now has the right end.</summary>
        private string MapLine()
        {
            string at = _campaign != null && _campaign.MapCount > 1
                ? (_campaign.MapIndex + 1) + "/" + _campaign.MapCount + "  ·  "
                : string.Empty;

            return at + _world.Map.Name + (_world.Waves.Endless ? "  ·  endless" : string.Empty);
        }

        /// <summary>The title line's right end: which wave the clock is on or counting to, and whether the
        /// run has a last one to be counted against. A word rather than a sentence, because it shares its
        /// line with the map's name.</summary>
        private string WaveTag()
        {
            if (_world.Status == GameStatus.Won) return "CLEARED";
            if (_world.Status == GameStatus.Lost) return "LOST";

            WaveDirector waves = _world.Waves;
            string total = waves.TotalWaves > 0 ? " OF " + waves.TotalWaves : string.Empty;

            switch (waves.Phase)
            {
                case WavePhase.Held:
                case WavePhase.Intermission:
                    return "WAVE " + waves.NextWave + total + " NEXT";
                case WavePhase.Running:
                    return "WAVE " + waves.CurrentWave + total;
                case WavePhase.Over:
                    return "ALL WAVES";
                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its title tag");
            }
        }

        /// <summary>The card's footer: the seed a run is reproducible from, in the corner the start-wave
        /// button leaves empty. Dim and out of the way - the one number on this card nobody plays against.</summary>
        private string SeedLine() => "seed " + _world.Waves.Seed;

        /// <summary>What the wave clock is doing, in the sentence the bar above it is the length of.</summary>
        private string WaveNews()
        {
            if (_world.Status != GameStatus.Playing)
            {
                // Won with no map after this one, the next press of N is the endless entry; a loss, or a
                // win with a map still ahead, has no N to offer - the banner above already says which.
                return _world.Status == GameStatus.Won && _campaign != null && !_campaign.HasNext
                    ? "press N to keep going, endless  ·  R to restart"
                    : "press R to restart";
            }

            WaveDirector waves = _world.Waves;
            switch (waves.Phase)
            {
                case WavePhase.Over:
                    return "no waves left";

                case WavePhase.Held:
                    // Two waits look the same and answer to different people. On a teaching map every wait is
                    // the lesson's, so neither building nor N begins it - saying so would put this line in
                    // contradiction with the task card beside it. On an ordinary map only the opening is held,
                    // and the first building (or N) is exactly what fills it.
                    return waves.HoldEveryWait
                        ? "wave " + waves.NextWave + " arrives when the lesson lets it go   ·   N waits too"
                        : "wave " + waves.NextWave + " starts when you build   ·   N calls it now";

                case WavePhase.Intermission:
                    return "next wave in " + Mathf.CeilToInt(waves.IntermissionRemaining) + "s"
                        + (_world.Paused ? string.Empty : "   ·   N starts it now");

                case WavePhase.Running:
                {
                    // Definition, not the authored table: an endless or generated run has waves the table
                    // never held, and the count still has to be right.
                    WaveDefinition current = waves.Definition(waves.CurrentWave);
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return left + " of " + (current != null ? current.Total : left) + " enemies left in this wave"
                        + (_world.Paused ? "   ·   paused" : string.Empty);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its wave line");
            }
        }

        // ------------------------------------------------------------------ controls card

        /// <summary>The controls cluster's one moving part is the pause button's word; the help card is
        /// opened and closed by its button and by <c>H</c>, and the "how to play" card by its hide button and
        /// by <c>T</c>, both read by the HUD itself - a view concern, the way the camera rig owns <c>C</c>.
        /// Nothing here is simulation state.</summary>
        private void UpdateControls()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) SetHelpOpen(!_helpOpen);
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame) SetTutorialShown(!ShowTutorial);

            int gear = _clock != null ? _clock.Gear : 1;
            bool behind = _clock != null && _clock.FallingBehind && !_world.Paused;
            bool canWave = CanStartWaveNow;
            if (!_controlsDirty && _controlsPaused == _world.Paused && _controlsGear == gear &&
                _controlsBehind == behind && _controlsCanWave == canWave) return;
            _controlsDirty = false;
            _controlsPaused = _world.Paused;
            _controlsGear = gear;
            _controlsBehind = behind;
            _controlsCanWave = canWave;

            _controls.Pause.Label.Set(_world.Paused ? ">" : string.Empty, _palette.HudText);
            _controls.PauseBars.gameObject.SetActive(!_world.Paused);

            // The gear's word; amber when this browser cannot keep up with it, so 3x never quietly means 2x.
            _controls.Gear.Label.Set(GearWord(), behind ? _palette.HudWarn : _palette.HudText);

            // The wave button is the one control in the cluster that comes and goes: it is drawn only
            // when the director has a wave to start, so it never promises something it would ignore.
            if (_controls.Wave.Root.gameObject.activeSelf != canWave)
                _controls.Wave.Root.gameObject.SetActive(canWave);
            if (canWave) _controls.Wave.Label.Set("WAVE " + _world.Waves.NextWave);
        }

        private void SetHelpOpen(bool open)
        {
            if (_helpOpen == open) return;
            _helpOpen = open;
            _help.Root.gameObject.SetActive(open);
        }

        /// <summary>Record the player's choice for the "how to play" card. The card itself follows on this
        /// frame's <see cref="UpdateTutorial"/>, and the rig and locator on the driver's next relay of
        /// <see cref="TutorialUp"/>.</summary>
        private void SetTutorialShown(bool shown) => _tutorialChoice = shown;

        /// <summary>The tile under the pointer, pushed by the driver every frame - the tooltip reads
        /// what the click would read, and the driver is the one place that already knows the answer.</summary>
        public void SetCursor(Int2 cell) => _cursor = cell;

        /// <summary>The keyboard, one pair of ideas per line. Short lines are the point: this list was once a
        /// single right-aligned sentence across the build bar's header, and a narrow screen clips the start of
        /// its own sentence first. Every line prints whole at the smallest scale, and none uses a character a
        /// built player's font might lack.</summary>
        internal static string[] KeyLines()
        {
            return new[]
            {
                "WASD / arrows pan  ·  wheel zooms",
                BuildInput.KeyRangeText() + " or click builds",
                "the same one again drops it",
                "Q / E turn  ·  drag lays belts",
                "T how to play  ·  H this card",
                "RMB deletes, or clears a jam",
                "Esc / M menu  ·  C the Core",
                "N the wave  ·  Space  ·  R restart",
                "[ / ] speed 1x - 3x",
            };
        }

        // ------------------------------------------------------------------ build bar

        private void UpdateBuildBar()
        {
            BuildKind? selected = _world.SelectedKind;
            _hoveredTile = -1;

            for (int i = 0; i < _bar.Tiles.Count; i++)
            {
                BuildTile tile = _bar.Tiles[i];

                // Every tile is on the bar all the time, so the pointer's answer is the button's own; the
                // object test is kept because a hierarchy torn down under a pointer can leave a flag set.
                bool hovered = tile.Root.gameObject.activeSelf && tile.Button.Hovered;
                if (hovered) _hoveredTile = i;

                bool selectedTile = tile.Kind == selected;
                bool affordable = _world.Economy.CanAfford(tile.Kind);
                bool lesson = LessonWants(tile.Kind);

                if (tile.Selected != selectedTile || tile.Hovered != hovered ||
                    tile.Affordable != affordable || tile.Lesson != lesson)
                {
                    tile.Selected = selectedTile;
                    tile.Hovered = hovered;
                    tile.Affordable = affordable;
                    tile.Lesson = lesson;

                    DressBuildTile(tile);
                }

                // The tile the lesson just handed over keeps the accent outline for a glance. The player's
                // own marks win outright - selected or hovered, the tile has been seen - and the fade runs
                // from the everyday hairline (DressBuildTile's quiet case) so the outline dissolves rather
                // than popping off.
                if (tile.FreshUntil > 0f)
                {
                    float fresh = tile.FreshUntil - Time.unscaledTime;
                    if (fresh <= 0f || tile.Selected || tile.Hovered)
                    {
                        tile.FreshUntil = 0f;
                        DressBuildTile(tile);
                    }
                    else
                    {
                        tile.Border.Set(HudInk.Blend(HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.18f),
                            _palette.HudAccent, Mathf.Clamp01(fresh / FreshSeconds)));
                    }
                }

                // The lesson's own key wears a ring three times the hairline's weight that breathes from the
                // accent toward the brightest ink, and the keycap behind its digit lights with it. Weight,
                // not shade, is the point: the selected tile's outline and this ring are no longer two
                // brightnesses of the same line, so "the line wants this key" cannot be read as "this key is
                // selected" - which is what a pulse that only rode the same hairline did. The player's own
                // marks still win: selected or hovered has been seen.
                if (tile.Lesson && !tile.Selected && !tile.Hovered)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f);
                    Color cue = HudInk.Blend(_palette.HudAccent, _palette.HudText, 0.35f + 0.65f * pulse);

                    tile.Cue.Show(true);
                    tile.Cue.Set(cue);
                    tile.Keycap.Set(HudInk.Blend(_palette.HudPanel, cue, 0.28f + 0.34f * pulse));
                    tile.Hotkey.SetColour(cue);
                }
                else
                {
                    // The keycap and the digit were put back by DressBuildTile on the frame this state
                    // changed; only the ring is this block's to clear.
                    tile.Cue.Show(false);
                }

                // The line along the tile's bottom says how far from affordable the bank is: nine tenths of
                // the way is a belt to lay now, a tenth of the way is a plan for later.
                if (!tile.Affordable && _world.Content.Machine(tile.Kind).Cost > 0)
                    tile.Afford.Set(Mathf.Clamp01(_world.Economy.Shapes / (float)_world.Content.Machine(tile.Kind).Cost),
                        _palette.CursorNoFunds);
                else tile.Afford.Set(0f, Color.clear);
            }

            // The cost's shape sits glued to the number's left edge, and the number's width is known
            // only once the text has laid itself out - so the first frames place it as they learn it.
            for (int i = 0; i < _bar.Tiles.Count; i++) PlaceCostIcon(_bar.Tiles[i]);
        }

        /// <summary>
        /// Does the current lesson line's blueprint use this building? The bar highlights every kind the
        /// ghost plan names, which is the one thread from the map's picture to the key that puts it there.
        /// A line with no blueprint highlights nothing; a finished lesson highlights nothing even on its
        /// last frame, because there is no next thing to press.
        /// </summary>
        private bool LessonWants(BuildKind kind)
        {
            TutorialState tutorial = _world.Tutorial;
            if (!tutorial.Active || tutorial.Complete) return false;

            TutorialGhost[] ghost = tutorial.Current.Ghost;
            for (int i = 0; i < ghost.Length; i++)
                if (ghost[i].Build == kind) return true;

            return false;
        }

        /// <summary>
        /// One building as a button: hotkey, cost, name and role over a stripe tinted by that role.
        /// Selected is an accent outline and tinted fill, unaffordable greys the name and turns the cost
        /// amber, and the pointer lightens the tile. A click selects the kind through the input latch - it
        /// never places anything, so a stray click can only change what is selected.
        ///
        /// Its set is the run's, not the map's: a kind the run has not handed over is not on the bar at all
        /// (<see cref="VisibleKinds"/>), so there is no locked state to dress. A press for one still has a
        /// door - the hotkeys outlive the bar - and <see cref="SelectKind"/> is where that is refused.
        /// </summary>
        private void DressBuildTile(BuildTile tile)
        {
            // The tile wears one of the ink levels plus the one focus colour; roles are words on the card,
            // not hues on the keys, so a bar of eleven tiles does not look like a paint chart.
            Color fill = HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.11f);
            if (tile.Selected) fill = HudInk.Blend(_palette.HudPanel, _palette.HudAccent, 0.22f);
            else if (tile.Hovered) fill = HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.19f);
            else if (!tile.Affordable) fill = HudInk.Blend(_palette.HudPanel, _palette.CursorNoFunds, 0.08f);
            tile.Button.Fill.Set(fill);

            tile.Keycap.Set(tile.Selected
                ? HudInk.Blend(_palette.HudPanel, _palette.HudAccent, 0.40f)
                : HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.18f));

            // Every tile keeps a hairline: eleven surfaces need an edge to read as eleven keys rather than
            // as one dark strip with floating words on it.
            tile.Border.Show(true);
            tile.Border.Set(tile.Selected
                ? _palette.HudAccent
                : tile.Hovered
                    ? HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.38f)
                    : HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.18f));

            tile.Hotkey.SetColour(tile.Selected ? _palette.HudText : HudInk.Muted(_palette));

            // The cost is the line that answers "can I afford this", so it is the line that turns amber
            // when the answer is no - the same colour the cursor ghost and the resource band use for the
            // same fact.
            tile.Cost.SetColour(tile.Affordable
                ? (tile.Selected ? _palette.HudText : HudInk.Muted(_palette))
                : _palette.CursorNoFunds);

            tile.Name.SetColour(tile.Affordable ? _palette.HudText : HudInk.Muted(_palette));
        }

        /// <summary>The bar's one way to change what is selected, and the one place a locked kind is
        /// refused: the tile's click and the digit key are the same door, so the rule has one statement.
        /// Asking for the kind already in hand drops it, which is the read cursor.</summary>
        private void SelectKind(BuildKind kind)
        {
            if (_world == null || !_world.IsUnlocked(kind)) return;

            _selectKind?.Invoke(kind);
        }

        // ------------------------------------------------------------------ the bar's keys

        /// <summary>The cost's shape, once the cost's own width is known: the picture just outside the
        /// number's left edge, where a cost is a mark rather than a sentence.</summary>
        private void PlaceCostIcon(BuildTile tile)
        {
            if (tile.CostIcon == null) return;

            if (tile.CostPlaced) return;
            float textWidth = tile.Cost.PreferredWidth;
            if (textWidth <= 0f) return;

            tile.CostPlaced = true;
            float size = 9f * _scale;
            float x = _bar.TileWidth - 6f * _scale - textWidth - 4f * _scale - size;
            if (x < 24f * _scale) return;   // never under the keycap

            HudUi.TopLeft(tile.CostIcon.rectTransform, x, 23.5f * _scale, size, size);
            tile.CostIcon.gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------ info card

        /// <summary>What the hovered (or, with nothing hovered, the selected) building does: its cost and role
        /// in the header, a sentence of what it is for, its numbers, and the facing its ghost will be built
        /// with - plus the two rotate buttons, the one way to turn a building without a keyboard.
        ///
        /// With nothing selected and nothing hovered the card describes what the player <em>read</em> instead -
        /// see <see cref="RefreshInspect"/>.</summary>
        private void UpdateInfo()
        {
            // The hover index is a position on the bar, and the bar is the run's row of tiles - not the whole
            // content table: a tutorial hides the buildings it has not handed over yet, so the two arrays
            // stop agreeing the moment one is hidden. Read the tile's own kind, the mapping the bar and the
            // hotkeys already use.
            bool hovered = _bar != null && _hoveredTile >= 0 && _hoveredTile < _bar.Tiles.Count;

            if (hovered || _world.SelectedKind.HasValue)
            {
                BuildKind kind = hovered ? _bar.Tiles[_hoveredTile].Kind : _world.SelectedKind.Value;

                if (!_infoBuildShown || _infoKind != kind || _infoDirty)
                {
                    _infoBuildShown = true;
                    _infoKind = kind;
                    _infoDirty = false;

                    // The build card has no picker: a sorter left inspected behind it would otherwise leave
                    // the card wearing its taller band with nothing drawn in it.
                    SetPickerUp(false);
                    RefreshBuildInfo(kind);
                }

                // The facing is the ghost's, and Q and E move it: the one line on the build card that
                // answers to the keyboard rather than to the catalogue. A cutter's line names the cut
                // instead of the arrow - "cuts top from bottom" flipping to "cuts left from right" as
                // the ghost turns is the lesson, watched live where the turn buttons sit.
                int facing = (int)_world.PlacementDirection;
                if (facing != _infoFacing)
                {
                    _infoFacing = facing;
                    _info.Facing.Set(GhostFacing(kind), HudInk.Muted(_palette));
                }

                // The READ button is the card's door into the inspect mode, so it follows the selection
                // every frame rather than the card's words: hovering a building the player has just
                // selected changes nothing on the card, and a button written only when the words changed
                // would stay hidden in exactly that case.
                bool canRead = _world.SelectedKind.HasValue;
                if (_info.Inspect.Root.gameObject.activeSelf != canRead)
                    _info.Inspect.Root.gameObject.SetActive(canRead);

                return;
            }

            if (_infoBuildShown || _infoDirty)
            {
                _infoBuildShown = false;
                _infoDirty = false;
                _info.BuildRoot.gameObject.SetActive(false);
                _info.InspectRoot.gameObject.SetActive(true);
            }

            RefreshInspect();
        }

        private void RefreshBuildInfo(BuildKind kind)
        {
            _info.BuildRoot.gameObject.SetActive(true);
            _info.InspectRoot.gameObject.SetActive(false);

            MachineDef def = _world.Content.Machine(kind);
            bool locked = !_world.IsUnlocked(kind);

            // No role hue on the stripe: the card is about one building, and the header says whether that
            // building is available to place (a live title) or still locked (a muted one), not which family
            // it belongs to.
            _info.BuildTitle.Set(def.Name, locked ? HudInk.Muted(_palette) : _palette.HudText);
            _info.BuildRight.Set(
                def.Cost + " " + Plural(Shape.MineralName(_world.Map.CurrencyMineral), def.Cost) + "   ·   " +
                (locked ? "LOCKED" : RoleOf(kind)),
                HudInk.Muted(_palette));

            // A locked kind is described on the card like any other - the catalogue's words are not a
            // secret - with the one thing the player needs to know about it said first.
            _info.Blurb.Set(locked
                ? "not unlocked on this map · " + Blurb(kind)
                : Blurb(kind), _palette.HudText);

            string stats = Stats(kind);
            bool hasStats = !string.IsNullOrEmpty(stats);
            _info.Stats.Show(hasStats);
            if (hasStats) _info.Stats.Set(stats, HudInk.Muted(_palette));

            // The facing line sits under the stats, and the three buttons under it: the card's height is
            // fixed, so a building with no stats leaves a gap rather than moving the buttons under the
            // pointer.
            At(_info.Facing.Rect, CardInset, 108f, 120f, 14f);
            AtRight(_info.Inspect.Root, CardInset, 112f, 52f, 20f);
            AtRight(_info.TurnRight.Root, 70f, 112f, 24f, 20f);
            AtRight(_info.TurnLeft.Root, 98f, 112f, 24f, 20f);

            // With something selected the card also carries the keyboard-free way back to reading the
            // map: the key is Escape, the button is the same door. Its visible state is the update pass's
            // to keep true (see UpdateInfo), because selecting the building the card already describes is
            // not a change of words.
            _info.Facing.Set(GhostFacing(kind), HudInk.Muted(_palette));
        }

        /// <summary>The build card's facing line, for the placement the card is describing: what that
        /// kind's facing would mean when placed now, read from the same note the built card prints - a
        /// cutter says which cut it is aimed at, everything else says the side it faces.</summary>
        private string GhostFacing(BuildKind? kind)
        {
            if (!kind.HasValue) return string.Empty;

            return MachineReading.GhostFacingNote(_world.Content.Machine(kind.Value),
                _world.PlacementDirection);
        }

        /// <summary>The card with nothing selected: what the player read, not what they are about to build.
        ///
        /// Its lines are the building's <em>state</em> - the ports its belts are wired to, what it is doing
        /// this second, whether it is starved or jammed - all read from the same state the tick runs on. The
        /// blurb and the cost are left out on purpose: those are the catalogue's business.
        ///
        /// Nothing pinned - or a pin whose building has since been removed - is not an empty card: it says
        /// what the state is for and how to leave it.</summary>
        private void RefreshInspect()
        {
            if (!_inspectDirty && _inspectCell.X == _world.InspectedCell.X &&
                _inspectCell.Y == _world.InspectedCell.Y && _inspectTick == _world.TickCount)
                return;

            _inspectDirty = false;
            _inspectCell = _world.InspectedCell;
            _inspectTick = _world.TickCount;
            _rowCount = 0;

            Int2 cell = _world.InspectedCell;
            bool machineHere = _world.Machines.TryGetSnapshot(cell, out MachineSnapshot machine);

            // The one machine whose card grows: a sorter's filter is a setting the player owns, and the
            // picker is where it is set. It is read from the machine's snapshot - not the definition row -
            // because a sorter that has been re-routed is the machine that is routing.
            bool picker = machineHere && machine.Behavior == BehaviorKind.Sorter;
            SetPickerUp(picker);
            if (picker) RefreshFilterPicker(cell, machine);

            if (machineHere)
            {
                MachineDef def = _world.Content.Machine(machine.Build);

                InspectHeader(def.Name, _palette.HudAccent, RoleOf(machine.Build) + "   " + cell);
                InspectLine(PortsLine(machine), HudInk.Muted(_palette));

                ReadingNote note = MachineReading.LiveNote(_world, cell, machine);
                InspectLine(note.Text, NoteColour(note.Severity));

                string stats = Stats(machine.Build);
                if (!string.IsNullOrEmpty(stats)) InspectLine(stats, HudInk.Muted(_palette));

                if (machine.MaxHp > 0f)
                {
                    bool hurting = machine.HealthFraction < 0.4f;
                    InspectLine("holding " + Mathf.CeilToInt(machine.Hp) + " / " +
                        Mathf.CeilToInt(machine.MaxHp) + " health",
                        hurting ? _palette.HudWarn : HudInk.Good(_palette));
                }

                InspectLine(MachineReading.FacingNote(def, machine), HudInk.Muted(_palette));
                FinishInspect();
                return;
            }

            if (_world.Belts.TryGet(cell, out BeltState belt))
            {
                MachineDef def = _world.Content.Machine(BuildKind.Belt);

                InspectHeader(def.Name, _palette.HudAccent, RoleOf(BuildKind.Belt) + "   " + cell);

                if (belt.Jammed)
                {
                    // A jam arrives with the item that caused it on the belt; name that item when it is
                    // there, because a jam is always "this machine refuses this round" and the round is the
                    // half of it the player can act on.
                    string culprit = belt.Item.IsEmpty ? string.Empty : " by " + belt.Item.Describe();
                    InspectLine("JAMMED" + culprit + " - the machine it feeds will not take it",
                        _palette.HudWarn);
                }
                else if (belt.HasItem)
                    InspectLine("carrying " + belt.Item.Describe() + " · " +
                        Mathf.RoundToInt(belt.Progress * 100f) + "% across", _palette.HudText);
                else
                    InspectLine("empty - nothing riding it right now", _palette.HudText);

                InspectLine("runs " + MachineReading.DirName(belt.Direction) +
                    " · one item per cell, so this is its rate", HudInk.Muted(_palette));
                FinishInspect();
                return;
            }

            TileKind tileKind = _world.TileGrid.Get(cell);
            if (tileKind == TileKind.Core)
            {
                InspectHeader("Core", _palette.Core, "the bank");

                bool hurting = _world.Core.HealthFraction < 0.4f;
                InspectLine("holding " + Mathf.CeilToInt(_world.Core.Hp) + " / " +
                    Mathf.CeilToInt(_world.Core.MaxHp) + " health",
                    hurting ? _palette.HudWarn : HudInk.Good(_palette));
                InspectLine("only a complete shape banks - anything less is lost",
                    HudInk.Muted(_palette));
                InspectLine("losing it loses the run", HudInk.Muted(_palette));
                FinishInspect();
                return;
            }

            // The ground is read off the terrain layer rather than the tile kind, so an ore card has a
            // mineral and a rock card its kind. Whether the cell is worth reading at all is the simulation's
            // one answer (<see cref="SimWorld.CanInspect"/> - the same rule the click and the cursor's marker
            // read), not a copy restated here.
            if (_world.CanInspect(cell))
            {
                DrawGroundCard(cell, _world.Map.SpawnIndexOf(cell));
                FinishInspect();
                return;
            }

            InspectHeader("Inspect", _palette.HudAccent, "nothing selected");

            // Nothing to read is not an error: the message sits in the middle of the card's body rather
            // than pinned under the header, so an empty card looks deliberate instead of truncated.
            PlaceInspectRows(58);
            InspectLine("click a building, vein or doorway to read it", _palette.HudText);
            InspectLine("or pick a tile to place it", HudInk.Muted(_palette));
            FinishInspect();
        }

        /// <summary>
        /// Grow or shrink the info card's picker band, and put the pieces that depend on it back where the
        /// grown card wants them. The card is bottom-anchored, so growing means the header stays at the top
        /// of a taller card and the rows move down by the band's height.
        /// </summary>
        private void SetPickerUp(bool up)
        {
            if (_pickerUp == up) return;

            _pickerUp = up;
            _info.Picker.Root.gameObject.SetActive(up);
            Place(_info.Root, HudLayout.Info(Screen.width, Screen.height, _scale, up),
                PlaceCorner.BottomRight);
            PlaceInspectRows(33 + (up ? HudLayout.InfoPickHeight : 0));
        }

        /// <summary>Put the inspect rows back on their base grid, in design units from the card's top. The
        /// header calls this for every real card, because the empty state's two lines sit lower in the card
        /// (there is nothing under them, and a message pinned to the header looks like a clipped card).</summary>
        private void PlaceInspectRows(float top)
        {
            for (int i = 0; i < _info.Rows.Length; i++)
                At(_info.Rows[i].Rect, CardInset, top + 15f * i,
                    HudLayout.InfoWidth - 2f * CardInset, 14f);
        }

        /// <summary>
        /// Paint the picker from the machine's own filter: the item's figure on the canvas, a ghost cell
        /// where a quadrant is empty, and the sentence the mode button and the caption carry.
        ///
        /// The working state is re-seeded whenever the machine's filter is not the one it was seeded from,
        /// so a filter changed by an earlier click (or by a different machine being inspected) is read back
        /// rather than painted over - the card always shows what routes.
        /// </summary>
        private void RefreshFilterPicker(Int2 cell, in MachineSnapshot machine)
        {
            if (cell != _filterCell || !SameFilter(machine.Filter, _filterSeen))
            {
                _filterCell = cell;
                _filterSeen = machine.Filter;
                SeedFilter(machine.Filter);
            }

            Shape painted = PaintedFilter();
            HudIcons.Icon icon = _icons.Canvas(painted);
            if (icon.Valid)
            {
                _info.Picker.Figure.texture = icon.Texture;
                _info.Picker.Figure.uvRect = icon.Uv;
            }
            _info.Picker.Figure.gameObject.SetActive(icon.Valid);

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                _info.Picker.Ghosts[quadrant].Show(!painted.Has(quadrant));

            _info.Picker.Mode.Label.Set(_filterMineral ? "mode: contains a mineral" : "mode: exactly this item",
                _palette.HudText);

            // The caption is what a click would send. While the canvas is still empty it says what the
            // machine routes <em>now</em> instead, so the card never leaves the player guessing: a sorter
            // whose row shipped "any shape" has a rule worth reading even though it has no quadrants to paint.
            // A card built without a way to send a rule - the honest read-only one - says what the machine
            // routes without inviting a click that could never take effect.
            string caption = PickerCaption();
            Color captionColour = HudInk.Muted(_palette);
            if (!TryPendingFilter(out _))
            {
                if (_setFilter == null)
                {
                    caption = "routing " + machine.Filter.Describe() + " now   ·   read-only";
                    captionColour = HudInk.Muted(_palette);
                }
                else
                {
                    caption = machine.Filter.IsNone
                        ? caption
                        : "routing " + machine.Filter.Describe() + " now   ·   click a quadrant to change it";
                    captionColour = machine.Filter.IsNone ? _palette.HudWarn : HudInk.Muted(_palette);
                }
            }

            _info.Picker.Caption.Set(caption, captionColour);
        }

        /// <summary>What the machine's filter says the picker should start painting.</summary>
        private void SeedFilter(in ShapeFilter filter)
        {
            switch (filter.Mode)
            {
                case FilterMode.Mineral:
                    _filterMineral = true;
                    _filterMineralPicked = filter.Mineral != Quad.Empty ? filter.Mineral : Quad.Circle;
                    _filterShape = Shape.Full(_filterMineralPicked);
                    return;

                case FilterMode.Exact:
                    _filterMineral = false;
                    _filterShape = filter.Exact;
                    return;

                case FilterMode.Any:
                case FilterMode.None:
                    // "Routes everything" and "routes nothing" have no quadrants to paint: the canvas starts
                    // empty and the first click on a quarter is what gives it a rule. The mineral is seeded
                    // anyway, so the mode toggle has somewhere to go from an empty canvas.
                    _filterMineral = false;
                    _filterShape = Shape.None;
                    _filterMineralPicked = Quad.Circle;
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(filter), filter.Mode,
                        "a new filter mode must say how the picker paints it");
            }
        }

        /// <summary>The item the canvas is showing: the painted shape in exact mode, and the whole of the
        /// mineral in mineral mode - the rule is about the mineral, so the swatch that stands for it is all
        /// four quadrants of it.</summary>
        private Shape PaintedFilter() => _filterMineral ? Shape.Full(_filterMineralPicked) : _filterShape;

        /// <summary>
        /// What a click would send, or false when it would send nothing. A rule that routes nothing is
        /// never sent: <see cref="ShapeFilter.None"/> is refused by the world, and an exact match on an
        /// empty item would be a filter that admits nothing at all.
        /// </summary>
        private bool TryPendingFilter(out ShapeFilter filter)
        {
            if (_filterMineral)
            {
                filter = _filterMineralPicked == Quad.Empty
                    ? ShapeFilter.None
                    : ShapeFilter.Has(_filterMineralPicked);
                return !filter.IsNone;
            }

            filter = _filterShape.IsEmpty ? ShapeFilter.None : ShapeFilter.Is(_filterShape);
            return !filter.IsNone;
        }

        /// <summary>The rule a click would send, in words - or the instruction, while the canvas is still
        /// empty and a click is what gives it a rule.</summary>
        private string PickerCaption()
            => TryPendingFilter(out ShapeFilter filter) ? filter.Describe() : "click a quadrant to route on";

        /// <summary>
        /// One quadrant clicked. In exact mode it cycles that quadrant's own content - empty, circle,
        /// square, empty - because which way up an item is matters and a top half-circle is not a bottom
        /// one, so the canvas is a real item rather than a bag of minerals. In mineral mode it cycles the
        /// mineral, which is the only thing that rule is about.
        /// </summary>
        private void PressFilterQuadrant(int quadrant)
        {
            if (!_pickerUp) return;

            if (_filterMineral)
            {
                // In mineral mode a click chooses the mineral itself, so the cycle has two stops: a mineral
                // is never nothing, and a canvas that could go blank would be a rule the player cannot send.
                _filterMineralPicked = _filterMineralPicked == Quad.Circle ? Quad.Square : Quad.Circle;
            }
            else
            {
                _filterShape = WithQuadrant(_filterShape, quadrant, NextMineral(_filterShape.At(quadrant)));
            }

            SendFilter();
        }

        /// <summary>Switch between the two rules the picker states: anything containing a mineral, and one
        /// exact item. Neither switch is ever a dead end - each keeps a mineral or an item to route on - so
        /// the toggle always sends a rule the world accepts. Guarded like the quadrant press beside it: both
        /// are the picker's own buttons, and one handler defending against a state the other ignores would
        /// read as deliberate.</summary>
        private void PressFilterMode()
        {
            if (!_pickerUp) return;

            if (_filterMineral)
            {
                _filterMineral = false;
                _filterShape = Shape.Full(_filterMineralPicked);
            }
            else
            {
                Quad mineral = FirstMineral(_filterShape);
                _filterMineral = true;
                _filterMineralPicked = mineral != Quad.Empty ? mineral : _filterMineralPicked;
            }

            SendFilter();
        }

        /// <summary>Hand the picker's rule to the input latch, which carries it to the next tick. Nothing is
        /// sent while the canvas is empty (see <see cref="TryPendingFilter"/>).</summary>
        private void SendFilter()
        {
            if (_setFilter == null) return;
            if (!TryPendingFilter(out ShapeFilter filter)) return;

            _setFilter(_filterCell, filter);
        }

        /// <summary>The next mineral the picker's cycle shows.</summary>
        private static Quad NextMineral(Quad mineral)
        {
            switch (mineral)
            {
                case Quad.Empty: return Quad.Circle;
                case Quad.Circle: return Quad.Square;
                case Quad.Square: return Quad.Empty;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mineral), mineral,
                        "a new mineral needs a place in the picker's cycle");
            }
        }

        /// <summary>One quadrant of a painted item replaced, the other three kept: the canvas is an item, so
        /// painting a quadrant must not disturb its neighbours.</summary>
        private static Shape WithQuadrant(Shape shape, int quadrant, Quad mineral)
        {
            Quad nw = shape.At(Shape.NW), ne = shape.At(Shape.NE);
            Quad se = shape.At(Shape.SE), sw = shape.At(Shape.SW);

            switch (quadrant)
            {
                case Shape.NW: nw = mineral; break;
                case Shape.NE: ne = mineral; break;
                case Shape.SE: se = mineral; break;
                case Shape.SW: sw = mineral; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(quadrant), quadrant,
                        "an item has exactly four quadrants");
            }

            return Shape.Of(nw, ne, se, sw);
        }

        /// <summary>The mineral a painted item is made of: its first filled quadrant's. Empty for a canvas
        /// with nothing on it.</summary>
        private static Quad FirstMineral(Shape shape)
        {
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                if (shape.Has(quadrant)) return shape.At(quadrant);

            return Quad.Empty;
        }

        /// <summary>Is this the same routing rule? <see cref="ShapeFilter"/> is a plain struct with no
        /// equality of its own, and the two unused halves are not part of its identity.</summary>
        private static bool SameFilter(in ShapeFilter left, in ShapeFilter right)
            => left.Mode == right.Mode && left.Exact == right.Exact && left.Mineral == right.Mineral;

        /// <summary>The card's title row: the header grammar every card shares - a title on the left end of the
        /// line, the secondary words on the right, a hairline under both. The stripe the card used to wear is
        /// gone; when the header is about ground rather than a building, the title itself carries the ground's
        /// hue, because a mineral is worth a colour and a corner is not.</summary>
        private void InspectHeader(string name, Color accent, string right)
        {
            Color title = accent == _palette.HudAccent
                ? _palette.HudText
                : HudInk.Blend(accent, _palette.HudText, 0.35f);

            _info.InspectTitle.Set(name, title);
            _info.InspectRight.Set(right, HudInk.Muted(_palette));
            PlaceInspectRows(33 + (_pickerUp ? HudLayout.InfoPickHeight : 0));
        }

        /// <summary>The ports line, at the width the card can print: MachineReading's sentence explains
        /// the whole wiring rule, which is right for the catalogue and three lines too long for a 276-unit
        /// row. The short form keeps the one fact a reader needs - nothing is wired - without the essay.</summary>
        private static string PortsLine(in MachineSnapshot machine)
        {
            string ports = MachineReading.Ports(machine);
            return ports.StartsWith("no belts wired", StringComparison.Ordinal)
                ? "no belts wired · a belt in feeds it"
                : ports;
        }

        private void InspectLine(string text, Color colour)
        {
            if (_rowCount >= _info.Rows.Length) return;
            HudText row = _info.Rows[_rowCount++];
            row.Show(true);
            row.Set(text, colour);
        }

        private void FinishInspect()
        {
            for (; _rowCount < _info.Rows.Length; _rowCount++)
                _info.Rows[_rowCount].Show(false);
        }

        /// <summary>The colour a live note is said in - which colour means "look" is the palette's question;
        /// what the note means is <see cref="MachineReading"/>'s, and the EditMode tests cross that interface
        /// to check it. An uncoloured severity is an error, not a quiet fallback.</summary>
        private Color NoteColour(NoteSeverity severity)
        {
            switch (severity)
            {
                case NoteSeverity.Neutral: return _palette.HudText;
                case NoteSeverity.Good: return HudInk.Good(_palette);
                case NoteSeverity.Warn: return _palette.HudWarn;
                default:
                    throw new ArgumentOutOfRangeException(nameof(severity), severity,
                        "a new note severity needs a colour in the card");
            }
        }

        /// <summary>The ground: the map's own obstacle, a vein of ore, an entry point the waves walk in at,
        /// or two of them at once - a map may open a door over ore, so the card says what is there rather than
        /// choosing. None is a building, which is exactly why it is worth reading.</summary>
        private void DrawGroundCard(Int2 cell, int doorway)
        {
            TerrainKind kind = _world.Terrain.KindAt(cell);

            // The map's own obstacle comes first: rock, water and the void are what a wave's route is
            // chosen around, and a player looking at one is asking "is this mine or the map's?".
            if (kind.IsObstacle())
            {
                DrawObstacleCard(cell, kind);
                return;
            }

            bool ore = _world.Terrain.HasOre(cell);
            Quad mineral = ore ? _world.Terrain.OreAt(cell) : Quad.Empty;
            Color oreColour = mineral == Quad.Empty ? _palette.Outline : _palette.MineralColor(mineral);

            string title = doorway < 0 ? "Ore patch" : ore ? "Entry point · ore" : "Entry point";
            InspectHeader(title, doorway < 0 ? oreColour : _palette.SpawnMarker, cell.ToString());

            if (doorway >= 0)
            {
                InspectLine(WavesAt(doorway), _palette.SpawnMarker);
                InspectLine("enemies enter on this tile and walk at the Core", HudInk.Muted(_palette));
            }

            if (!ore) return;

            InspectLine("mines as " + Shape.Full(mineral).Describe() + " · a drill must stand here", oreColour);

            if (TryVeinAt(cell, out ShapePatch vein))
                InspectLine(vein.Width + " x " + vein.Height + " vein in the ground", HudInk.Muted(_palette));
        }

        /// <summary>A tile of ground the map draws as an obstacle, in the same colours the terrain view draws
        /// it in, so the card and the map agree about which tile is described - and the line under it says
        /// this wall is the map's and cannot fall.
        ///
        /// The note is the terrain's colour <em>lifted toward the card's text</em>, because a dark line on a
        /// dark panel is a line nobody can read.</summary>
        private void DrawObstacleCard(Int2 cell, TerrainKind kind)
        {
            Color colour = TerrainRenderer.ColorOf(kind, _palette);
            InspectHeader(kind.ToString(), colour, cell.ToString());

            InspectLine(ObstacleNote(kind), HudInk.Blend(colour, _palette.HudText, 0.6f));
            InspectLine("map ground - nothing may be built on it", HudInk.Muted(_palette));
        }

        private static string ObstacleNote(TerrainKind kind)
        {
            switch (kind)
            {
                case TerrainKind.Rock: return "rock: waves walk around what they cannot chew";
                case TerrainKind.Water: return "water: the other ground nothing crosses";
                default: return "void: outside the map, so there is no ground here at all";
            }
        }

        /// <summary>Which waves enter the map at this door, read off the map's own wave table - the question a
        /// player looking at a doorway is asking, and why a door no wave uses is said out loud instead of
        /// left blank.</summary>
        private string WavesAt(int doorway)
        {
            MapDefinition map = _world.Map;
            var numbers = new StringBuilder();
            int count = 0;

            for (int w = 0; w < map.Waves.Length; w++)
            {
                SpawnGroup[] groups = map.Waves[w].Groups;
                bool used = false;
                for (int g = 0; g < groups.Length && !used; g++)
                    used = map.ResolveSpawn(groups[g].SpawnPoint) == doorway;
                if (!used) continue;

                if (numbers.Length > 0) numbers.Append(", ");
                numbers.Append(w + 1);
                count++;
            }

            if (count == 0) return "no wave enters the map here";
            return (count == 1 ? "wave " : "waves ") + numbers + " walk in here";
        }

        /// <summary>The vein rectangle covering this cell, for its size, from the map's own patch table - the
        /// number printed is the ground the map laid, not a guess. False for ore a test stamped into the
        /// field, which has no rectangle.</summary>
        private bool TryVeinAt(Int2 cell, out ShapePatch vein)
        {
            ShapePatch[] patches = _world.Map.Patches;
            for (int i = 0; i < patches.Length; i++)
            {
                if (!patches[i].Contains(cell)) continue;
                vein = patches[i];
                return true;
            }

            vein = default;
            return false;
        }

        // ------------------------------------------------------------------ cursor tooltip

        /// <summary>The tile under the pointer, said beside the pointer: the same state the pinned inspect
        /// card reads, condensed to what a glance carries. It follows the pointer, sizes itself to its own
        /// words and gets out of the way of the screen's edges; a plain tile of ground says nothing.</summary>
        private void UpdateTooltip()
        {
            Vector2 pointer = Pointer();
            bool show = pointer.x >= 0f && !PointerOverHud &&
                _cursor.X >= 0 && _cursor.Y >= 0 && _world.Status == GameStatus.Playing;

            if (show)
            {
                if (_tooltipDirty || _tooltipCell.X != _cursor.X || _tooltipCell.Y != _cursor.Y ||
                    _tooltipTick != _world.TickCount)
                {
                    _tooltipDirty = false;
                    _tooltipCell = _cursor;
                    _tooltipTick = _world.TickCount;
                    _tooltipFilled = FillTooltip(_cursor);
                }

                // The cell's answer is kept with the cell: on the frames between two ticks nothing is
                // refilled, and plain ground must stay silent there too rather than show the last card's words.
                show = _tooltipFilled;
            }

            if (!show)
            {
                if (_tooltip.Root.gameObject.activeSelf) _tooltip.Root.gameObject.SetActive(false);
                return;
            }

            if (!_tooltip.Root.gameObject.activeSelf) _tooltip.Root.gameObject.SetActive(true);
            PositionTooltip(pointer);
        }

        /// <summary>Fill the tooltip from the cell. False - and nothing shown - when the cell is a
        /// plain tile of ground: the one thing under the pointer that has nothing to say.</summary>
        private bool FillTooltip(Int2 cell)
        {
            _tooltipRows = 0;

            if (_world.Machines.TryGetSnapshot(cell, out MachineSnapshot machine))
            {
                MachineDef def = _world.Content.Machine(machine.Build);
                TooltipHeader(def.Name, _palette.HudAccent);
                TooltipLine(PortsLine(machine), HudInk.Muted(_palette));

                ReadingNote note = MachineReading.LiveNote(_world, cell, machine);
                TooltipLine(note.Text, NoteColour(note.Severity));
                FinishTooltip();
                return true;
            }

            if (_world.Belts.TryGet(cell, out BeltState belt))
            {
                TooltipHeader(_world.Content.Machine(BuildKind.Belt).Name, _palette.HudAccent);

                string state = belt.Jammed
                    ? "JAMMED" + (belt.Item.IsEmpty ? "" : " by " + belt.Item.Describe()) +
                      " - right-click clears it"
                    : belt.HasItem
                        ? "carrying " + belt.Item.Describe() + " · " +
                          Mathf.RoundToInt(belt.Progress * 100f) + "% across"
                        : "empty";
                TooltipLine(state + " · to the " + MachineReading.DirName(belt.Direction),
                    belt.Jammed ? _palette.HudWarn : _palette.HudText);
                FinishTooltip();
                return true;
            }

            TileKind tile = _world.TileGrid.Get(cell);
            if (tile == TileKind.Core)
            {
                TooltipHeader("Core", _palette.Core);
                TooltipLine("holding " + Mathf.CeilToInt(_world.Core.Hp) + " / " +
                    Mathf.CeilToInt(_world.Core.MaxHp) + " health",
                    _world.Core.HealthFraction < 0.4f ? _palette.HudWarn : HudInk.Good(_palette));
                FinishTooltip();
                return true;
            }

            if (!_world.CanInspect(cell)) return false;

            TerrainKind kind = _world.Terrain.KindAt(cell);
            if (kind.IsObstacle())
            {
                TooltipHeader(kind.ToString(), TerrainRenderer.ColorOf(kind, _palette));
                TooltipLine(ObstacleNote(kind), HudInk.Blend(TerrainRenderer.ColorOf(kind, _palette), _palette.HudText, 0.6f));
                FinishTooltip();
                return true;
            }

            bool ore = _world.Terrain.HasOre(cell);
            int doorway = _world.Map.SpawnIndexOf(cell);
            if (!ore && doorway < 0) return false;   // plain ground: nothing is what it is

            if (doorway >= 0)
            {
                TooltipHeader("Entry point", _palette.SpawnMarker);
                TooltipLine(WavesAt(doorway), _palette.SpawnMarker);
            }
            else
            {
                Quad mineral = _world.Terrain.OreAt(cell);
                TooltipHeader("Ore patch", _palette.MineralColor(mineral));
                TooltipLine("mines as " + Shape.Full(mineral).Describe() + " - a drill mines it",
                    _palette.MineralColor(mineral));
            }

            if (ore && TryVeinAt(cell, out ShapePatch vein))
                TooltipLine(vein.Width + " x " + vein.Height + " vein in the ground", HudInk.Muted(_palette));

            FinishTooltip();
            return true;
        }

        private void TooltipHeader(string name, Color accent)
        {
            _tooltip.Title.Show(true);
            _tooltip.Title.Set(name, accent);
        }

        private void TooltipLine(string text, Color colour)
        {
            if (_tooltipRows >= _tooltip.Rows.Length) return;
            HudText row = _tooltip.Rows[_tooltipRows++];
            row.Show(true);
            row.Set(text, colour);
        }

        private void FinishTooltip()
        {
            for (; _tooltipRows < _tooltip.Rows.Length; _tooltipRows++)
                _tooltip.Rows[_tooltipRows].Show(false);

            // Sized to its own words: the widest line carries the box, the title and the rows together
            // carry the height.
            float width = _tooltip.Title.PreferredWidth;
            for (int i = 0; i < _tooltipRows; i++)
                width = Mathf.Max(width, _tooltip.Rows[i].PreferredWidth);

            _tooltipWidth = Mathf.Min(width + 20f * _scale, Screen.width - 16f);
            _tooltipHeight = (20f + 14f * _tooltipRows + 6f) * _scale;
        }

        /// <summary>Beside the pointer, over its right shoulder - flipped left near the screen's right edge
        /// and dropped below near the top, so the answer is never partly off screen and never under the
        /// hand.</summary>
        private void PositionTooltip(Vector2 pointer)
        {
            float x = pointer.x + 16f;
            float yFromTop = Screen.height - pointer.y - 16f;

            if (x + _tooltipWidth > Screen.width - 8f) x = pointer.x - _tooltipWidth - 16f;
            if (x < 8f) x = 8f;
            if (yFromTop < 8f) yFromTop = Screen.height - pointer.y + 24f;

            HudUi.TopLeft(_tooltip.Root, x, yFromTop, _tooltipWidth, _tooltipHeight);
        }

        // ------------------------------------------------------------------ banner

        /// <summary>The one thing that has to be unmissable: the run is over, or the game is paused.
        /// An ended run also reports itself - shots fired, shapes banked - so "do better" has numbers.</summary>
        private void UpdateBanner()
        {
            // The banner's own gate: a run's end and a pause are events, and the sentence they ask for does
            // not change until the world moves again. A paused world does not tick, so a paused banner holds
            // for free; an ended run may still tick the last coins into its stats line.
            bool changed = _bannerSeen != (int)_world.Status || _bannerPaused != _world.Paused ||
                _bannerTick != _world.TickCount;
            _bannerTick = _world.TickCount;
            if (!changed) return;

            _bannerSeen = (int)_world.Status;
            _bannerPaused = _world.Paused;

            string text;
            Color colour;
            bool show;

            switch (_world.Status)
            {
                case GameStatus.Lost:
                    text = "CORE DESTROYED\n" + StatsLine() + "\nR to restart";
                    colour = _palette.HudWarn;
                    show = true;
                    break;
                case GameStatus.Won:
                    // Naming the coming map needs the catalog, which the driver wires but a test may not: a HUD
                    // with no campaign still says the map was cleared, it just cannot promise what is next. The
                    // "no campaign" case is defended once, on the index, and the same index goes to both calls -
                    // the gauge is the catalog's own question (<see cref="MapCatalog.HasNext"/>) over exactly the
                    // index NextAfter is named, so the banner and the guard cannot disagree.
                    int index = _campaign != null ? _campaign.MapIndex : 0;
                    if (_catalog != null && _catalog.HasNext(index))
                    {
                        MapDefinition next = _catalog.NextAfter(index);
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               "\npress N for the next map: " + next.Name;
                    }
                    else
                    {
                        // The campaign has no map after this one, and pressing N is the "go on" verb in
                        // every state there is: here it is the endless entry (§7), the same map with the
                        // generator unbounded. The banner invites it; without a campaign there is only R.
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               (_campaign != null
                                    ? "\ncampaign complete · press N to keep going, endless"
                                    : "\nR to restart");
                    }

                    colour = HudInk.Good(_palette);
                    show = true;
                    break;
                default:
                    show = _world.Paused;
                    text = "PAUSED\nSpace to resume · R to restart";
                    colour = _palette.HudAccent;
                    break;
            }

            if (show == _bannerShown && text == _bannerText) return;

            _bannerShown = show;
            _bannerText = show ? text : null;
            _banner.Root.gameObject.SetActive(show);
            if (show) _banner.Label.Set(text, colour);
        }

        /// <summary>The run in one line: how much shooting the defence did, and how much economy
        /// the player built - the two axes "a better run" moves on.</summary>
        private string StatsLine()
        {
            return _world.ShotsFired + " shots fired · " + _world.Economy.TotalBanked + " " +
                Plural(Shape.MineralName(_world.Map.CurrencyMineral), _world.Economy.TotalBanked) + " banked";
        }

        // ------------------------------------------------------------------ the buttons' dressing

        /// <summary>The buttons whose look is the generic one: a filled rectangle that lightens under the
        /// pointer and takes an accent edge while hovered, drawn from the palette rather than a skin so it
        /// matches the cards. The world never sees the click because <see cref="PointerOverHud"/> claimed it
        /// for the UI.</summary>
        private void UpdateButtons()
        {
            Dress(_controls.Pause, ButtonFill);
            Dress(_controls.Gear, ButtonFill);
            Dress(_controls.Menu, ButtonFill);
            Dress(_controls.Help, ButtonFill);
            Dress(_help.Restart, ButtonFill);
            DressGhost(_help.Close);
            if (_tutorial.Root.gameObject.activeSelf) DressGhost(_tutorial.Hide);
            DressGhost(_info.TurnLeft);
            DressGhost(_info.TurnRight);
            if (_info.Inspect.Root.gameObject.activeSelf) DressGhost(_info.Inspect);

            // The picker's quarters are cells of the figure, not buttons with a colour of their own: their
            // fill stays clear so the item underneath is the content, and their border is what says the cell
            // is clickable - which is also why every quadrant wears one, empty or not.
            if (_info.Picker.Root.gameObject.activeSelf)
            {
                Dress(_info.Picker.Mode, ButtonFill);
                for (int i = 0; i < _info.Picker.Quadrants.Length; i++)
                    Dress(_info.Picker.Quadrants[i], Color.clear);
            }

            // The start-wave button breathes while it waits: the one motion the HUD spends on itself, on the
            // one action a player can take early and might not notice. A paused world holds its breath.
            if (_controls.Wave.Root.gameObject.activeSelf)
            {
                float phase = (Time.time % 1.2f) / 1.2f;
                float pulse = _world.Paused
                    ? 1f
                    : 0.86f + 0.14f * (0.5f + 0.5f * Mathf.Sin(phase * (2f * Mathf.PI)));
                Color fill = WithAlpha(_palette.HudAccent, pulse);
                _controls.Wave.Fill.Set(_controls.Wave.Hovered ? HudInk.Blend(fill, _palette.HudText, 0.16f) : fill);
                _controls.Wave.Border.Set(Color.clear);
            }
        }

        /// <summary>A control with a surface: a raised fill and a hairline that answers the pointer. The
        /// border is the quiet edge level, so a cluster of four controls does not draw four bright boxes.</summary>
        private void Dress(HudButton button, Color fill)
        {
            bool hovered = button.Hovered;
            button.Fill.Set(hovered ? HudInk.Blend(fill, _palette.HudText, 0.14f) : fill);
            button.Border.Set(hovered ? _palette.HudAccent : PanelBorder());
        }

        /// <summary>A control without a surface: one step off the card, no edge until the pointer arrives.
        /// The card's secondary actions - read, turn, hide, close - are quiet until asked for.</summary>
        private void DressGhost(HudButton button)
        {
            button.Fill.Set(button.Hovered
                ? HudInk.Raised(_palette)
                : HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.045f));
            button.Border.Set(button.Hovered
                ? HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.30f)
                : HudInk.Edge(_palette));
        }

        // ------------------------------------------------------------------ the button handlers

        private void PressWave()
        {
            if (_world != null && CanStartWaveNow) _world.RequestNextWave();
        }

        private void PressPause()
        {
            if (_world != null) _world.Paused = !_world.Paused;
        }

        private void PressGear() => _clock?.Cycle();

        /// <summary>The active gear, or 1 with no driver's clock.</summary>
        private int GearNow => _clock != null ? _clock.Gear : 1;

        /// <summary>The status card's footer, right end: the gear and the keys that change it. The card is
        /// where a player looks for "how fast is this going", so the answer lives there even while nothing
        /// is wrong.</summary>
        private string SpeedFooter()
            => _world.Paused ? "paused  ·  space resumes" : GearWord() + "  ·  [ ] speed";

        /// <summary>The gear button's word: "1×", "2×" or "3×". The multiplication sign, not an x, because
        /// at 10 points the letter reads as a letter.</summary>
        private string GearWord() => _clock != null ? _clock.Label.Replace('x', '×') : "1×";

        private void PressRestart()
        {
            if (_world != null) _world.RequestRestart();
        }

        private void PressInspect()
        {
            if (_world != null) _selectKind?.Invoke(null);
        }

        // ------------------------------------------------------------------ the catalogue's words

        // The catalogue's words and numbers all live in MachineReading - the one behaviour
        // dispatch the HUD merely formats, and the interface the EditMode tests cross to check what
        // a card would claim. What stays here is the palette question: which colour says which role.

        private string RoleOf(BuildKind kind) =>
            MachineReading.RoleLabel(MachineReading.Role(_world.Content.Machine(kind)));

        private string Blurb(BuildKind kind) =>
            MachineReading.Blurb(_world.Content.Machine(kind), _world.Content);

        private string Stats(BuildKind kind) =>
            MachineReading.Stats(_world.Content.Machine(kind), _world.Content, _world.Config);

        /// <summary>The item's own words, pluralised the way the sentence reading it needs: "3 banked
        /// circles" is the HUD's mistake to avoid, not the algebra's.</summary>
        private static string Plural(string word, int count)
            => count == 1 ? word : word + "s";

        /// <summary>The same colour, faded: what a refusal and the number it is about are drawn with
        /// while they age out.</summary>
        private static Color WithAlpha(Color colour, float alpha)
            => new Color(colour.r, colour.g, colour.b, colour.a * alpha);

        // ------------------------------------------------------------------ scale and rebinding

        private void RefreshScale()
        {
            float s = HudLayout.Scale(Screen.height);
            if (_screenWidth == Screen.width && _screenHeight == Screen.height &&
                Mathf.Abs(s - _scale) <= _scale * RebuildFraction)
                return;

            RebuildPanels();
        }

        /// <summary>The buildings this run offers, in the table's own (hotkey) order. Every kind on a map that
        /// has handed everything over, which is every map but a tutorial's opening: the filter is
        /// <see cref="SimWorld.IsUnlocked"/>, the same rule the hotkeys and the click both obey, so a hidden tile
        /// is a dead key.
        ///
        /// No allocation on the ordinary path - the table's own array is handed back when nothing is hidden,
        /// which is every frame of every run that is not being taught.
        ///
        /// A kind the table does not define is left out here rather than failing later: the bar asks the table
        /// for each tile's name and price, and a partial table is legal.
        ///
        /// <b>What this means for the ladder.</b> Because hiding replaced greying, a kind the run never offers
        /// is a kind the bar never shows at all - the stacker on a map whose veins yield one mineral and whose
        /// operator set therefore stops at the cutter. That is the trade the hidden bar makes, and it is why the
        /// level list keeps naming what clearing a map grants: the shelf fills as the lesson hands things over,
        /// and the ladder between maps is the shell's sentence rather than a row of grey keys.</summary>
        private BuildKind[] VisibleKinds()
        {
            BuildKind[] all = _world.Content.BuildKinds;

            int shown = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!_world.IsUnlocked(all[i])) continue;
                shown++;
            }

            if (shown == all.Length) return all;

            var visible = new BuildKind[shown];
            shown = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!_world.IsUnlocked(all[i])) continue;
                visible[shown++] = all[i];
            }

            return visible;
        }

        /// <summary>Does the bar's row of tiles still match the buildings this run offers? Asked every frame,
        /// not only on a rebind: a map's set is fixed for the run, but a tutorial's grows with its lines, so the
        /// bar's length is a run's state. A mismatch rebuilds the panels rather than re-laying twelve tiles - it
        /// happens once a lesson line, where a resize happens once a display change.
        ///
        /// Compared by walking the table and skipping what is hidden, so the answer costs no array.</summary>
        private bool BarMatchesContent()
        {
            if (_bar == null) return false;

            BuildKind[] kinds = _world.Content.BuildKinds;
            int shown = 0;

            for (int i = 0; i < kinds.Length; i++)
            {
                if (!_world.IsUnlocked(kinds[i])) continue;
                if (shown >= _bar.Tiles.Count || _bar.Tiles[shown].Kind != kinds[i]) return false;

                shown++;
            }

            return shown == _bar.Tiles.Count;
        }

        // ------------------------------------------------------------------ the panels as widgets

        private sealed class StatusPanel
        {
            public RectTransform Root;
            public HudText Title;
            public HudText WaveTag;
            public HudText CoreCap;
            public HudBox HealthTrack;
            public HudBar Health;
            public HudText HealthValue;
            public HudBar Clock;
            public HudBox Rule;
            public HudText News;
            public HudText NextLabel;
            public HudText AfterLabel;
            public Chip[] Chips;
            public Chip[] ChipsAfter;
            public HudText Production;
            public HudText Threat;
            public HudText Seed;
            public HudText Speed;
        }

        private sealed class ControlsPanel
        {
            public RectTransform Root;
            public HudButton Wave;
            public HudButton Pause;
            public RectTransform PauseBars;
            public HudBox PauseBarA;
            public HudBox PauseBarB;
            public HudButton Gear;
            public HudButton Menu;
            public HudButton Help;
        }

        private sealed class HelpPanel
        {
            public RectTransform Root;
            public HudButton Close;
            public HudButton Restart;
        }

        private sealed class TooltipPanel
        {
            public RectTransform Root;
            public HudBox Back;
            public HudBorder Frame;
            public HudText Title;
            public HudText[] Rows;
        }

        /// <summary>One entry of the wave's bill of materials: how many, of what, and the round that group
        /// demands.</summary>
        private readonly struct ChipEntry
        {
            public readonly int Count;
            public readonly string Name;
            public readonly Shape Round;

            public ChipEntry(int count, string name, Shape round)
            {
                Count = count;
                Name = name;
                Round = round;
            }
        }

        /// <summary>One chip of the bill of materials: the count, the enemy's name, the weakness mark.</summary>
        private sealed class Chip
        {
            public HudBox Back;
            public HudText Count;
            public HudText Name;
            public RawImage Icon;
        }

        private sealed class StockpilePanel
        {
            public RectTransform Root;
            public HudText Number;
            public RawImage Icon;
        }

        private sealed class ToastPanel
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public HudBox Back;
            public HudBorder Frame;
            public HudText Label;
        }

        private sealed class BannerPanel
        {
            public RectTransform Root;
            public HudText Label;
        }

        private sealed class TutorialPanel
        {
            public RectTransform Root;
            public HudButton Hide;
        }

        /// <summary>The lesson's card: a heading, the position in the list, the lines themselves as one
        /// rich-text label, and the stuck-line hint pinned under them. No button - there is nothing to dismiss,
        /// because the card goes when the lesson does.</summary>
        private sealed class TaskPanel
        {
            public RectTransform Root;
            public HudText Title;
            public HudText Count;
            public HudText Body;
            public HudText Hint;
        }

        private sealed class BarPanel
        {
            public RectTransform Root;
            public readonly List<BuildTile> Tiles = new List<BuildTile>();

            public float TileWidth;
            public float TileHeight;
            public float Pad;
            public float Gap;

            /// <summary>Where the top of the tile row sits, inside the bar's card.</summary>
            public float TilesTop;
        }

        private sealed class BuildTile
        {
            public RectTransform Root;
            public HudButton Button;
            public HudBox Keycap;
            public HudText Hotkey;
            public HudText Cost;
            public RawImage CostIcon;
            public HudText Name;
            public HudBar Afford;
            public HudBorder Border;

            /// <summary>The lesson's ring: the same tile, marked with a ring several times the hairline's
            /// weight and pulsing toward the brightest ink, so "the line wants this key" can never be read
            /// as "this key is selected". Shown only while the line wants the kind and the player has not
            /// marked the tile himself.</summary>
            public HudBorder Cue;

            public BuildKind Kind;
            public bool Selected;
            public bool Hovered;
            public bool Affordable;
            public bool CostPlaced;

            /// <summary>Is this kind part of the current lesson line's blueprint? The update pass lights its
            /// cue ring - a thick, breathing mark of its own - while the line is current, so the bar points at
            /// the key the map is asking for.</summary>
            public bool Lesson;

            /// <summary>A rebuild that grew the bar (a tutorial's hand-over) stamps the added tiles with the
            /// unscaled time their accent outline fades at; 0 is no pulse. The player's own marks - selected,
            /// hovered - end it early: a tile in hand has been seen.</summary>
            public float FreshUntil;
        }

        private sealed class InfoPanel
        {
            public RectTransform Root;

            public RectTransform BuildRoot;
            public HudText BuildTitle;
            public HudText BuildRight;
            public HudText Blurb;
            public HudText Stats;
            public HudText Facing;
            public HudButton TurnLeft;
            public HudButton TurnRight;
            public HudButton Inspect;

            public RectTransform InspectRoot;
            public HudText InspectTitle;
            public HudText InspectRight;
            public HudText[] Rows;
            public FilterPicker Picker;
        }

        /// <summary>The sorter's filter picker: the item's own square as four clickable quadrants, the mode
        /// toggle beside them and the sentence saying what a click would route for.</summary>
        private sealed class FilterPicker
        {
            public RectTransform Root;
            public HudText Title;

            /// <summary>The faint cells empty quadrants wear here, and only here.</summary>
            public readonly HudBox[] Ghosts = new HudBox[Shape.QuadrantCount];

            public RawImage Figure;
            public readonly HudButton[] Quadrants = new HudButton[Shape.QuadrantCount];
            public HudButton Mode;
            public HudText Caption;
        }

        /// <summary>The most lines any inspect card asks of the pool: a machine's ports, live note,
        /// stats, health and facing - and a vein of room above that.</summary>
        private const int InspectRowCapacity = 6;

        /// <summary>How many enemy kinds the bill of materials lays out before giving up on the overflow -
        /// more than this in one wave is a content-table question, not a layout one.</summary>
        private const int ChipCapacity = 6;

        /// <summary>The most lines the cursor tooltip asks of its pool: what it is, its ports, its
        /// live note - the glance, not the card.</summary>
        private const int TooltipRowCapacity = 3;

        /// <summary>The chip row's own copy of the briefings, reused every pass: a wave change is rare, but a
        /// wave change that allocated two lists per row would be a garbage-collector spike exactly when the
        /// player is reading the card.</summary>
        private readonly List<ChipEntry> _nextEntries = new List<ChipEntry>();
        private readonly List<ChipEntry> _afterEntries = new List<ChipEntry>();

        /// <summary>Where the two briefing rows sit, in design units from the card's top: the wave being
        /// counted to, then the one after it. The labels and the chips share the row.</summary>
        private const float ChipsTop = 82f;
        private const float ChipsAfterTop = 103f;

        /// <summary>A chip is the demand figure plus two units of air above and below it - the figure sets
        /// the row's height, not the 10-point count beside it (issue #12).</summary>
        private const float ChipIconPad = 2f;
        private const float ChipHeight = HudLayout.DemandFigure + ChipIconPad * 2f;

        /// <summary>The hairline over the card's footer: one unit under the second briefing row. The seed,
        /// the gear and the two diagnostics hang off it.</summary>
        private const float FooterTop = ChipsAfterTop + ChipHeight + 1f;

        /// <summary>The column the NOW/THEN labels own, so both chip rows start on the same vertical line.</summary>
        private const float ChipLabelWidth = 36f;

        /// <summary>
        /// The sorter picker's geometry, in design units: a quartered canvas of four cells and the mode
        /// toggle beside it. Big enough that four quadrants read apart at the smallest scale, and small
        /// enough that the band it costs the info card is one line of the layout
        /// (<see cref="HudLayout.InfoPickHeight"/>).
        /// </summary>
        private const float CellSize = 22f;
        private const float CanvasSize = CellSize * 2f;

        /// <summary>Where the picker's band starts inside the card, under the header and its hairline.</summary>
        private const float PickerTop = 32f;

        /// <summary>The mode toggle's column, clear of the canvas, and the width the card leaves it.</summary>
        private const float ModeLeft = 12f + CanvasSize + 8f;
        private const float ModeWidth = HudLayout.InfoWidth - ModeLeft - 12f;

        /// <summary>The faint cell an empty quadrant wears in the picker, and nowhere else: on the map an
        /// empty quadrant is nothing, and a ghost there would draw an item the simulation does not have.
        /// Faint enough to be a target rather than content.</summary>
        private Color GhostCell => WithAlpha(_palette.HudText, 0.09f);
    }
}
