using System;
using System.Collections.Generic;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Core's shape-filter mode collides with UnityEngine.FilterMode (the texture filter).
using FilterMode = Facet.Core.FilterMode;

namespace Facet.Game
{
    /// <summary>The player's whole operating surface: status rail, stockpile, controls cluster, build bar and
    /// info card. Drawn with uGUI at runtime and event-driven - a label is written when its words change, a bar
    /// when its length does. Layout, type scale and ink levels are <see cref="HudLayout"/>'s, <see cref="HudType"/>'s
    /// and <see cref="HudInk"/>'s.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class HudView : MonoBehaviour
    {
        /// <summary>A scale change this large is a rebuild, not a matrix: the scale lives in the fonts' point size.</summary>
        private const float RebuildFraction = 0.02f;

        /// <summary>How long a refusal toast is shown.</summary>
        private static readonly int RefusalTicks = (int)(SimConfig.TickRate * 3f);

        private const float RefusalFade = 0.35f;

        private SimWorld _world;
        private Palette _palette;
        private CampaignState _campaign;

        /// <summary>Drives the one question the HUD asks of it, which map is next; null for a single-map HUD.</summary>
        private MapCatalog _catalog;

        /// <summary>The build bar's way back into the input latch; null in a HUD the driver did not wire.</summary>
        private Action<BuildKind?> _selectKind;
        private Action<int> _rotate;

        /// <summary>The driver's pause-menu door; null in an unwired HUD, where the button draws and does nothing.</summary>
        private Action _openMenu;

        /// <summary>The picker's way back into the input latch; null in an unwired HUD, whose card is read-only.</summary>
        private Action<Int2, ShapeFilter> _setFilter;

        /// <summary>The shapes as pictures, from the same Palette the map draws with: a mark drawn as a character
        /// disappears in a font that lacks it.</summary>
        private HudIcons _icons;

        /// <summary>Which mineral's mark the HUD is wearing right now; repainted when a map change moves it.</summary>
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

        private int _hoveredTile = -1;

        /// <summary>The camera whose view decides what is out of it - the same one <see cref="ThreatRadarView"/>
        /// draws for.</summary>
        private Camera _camera;

        /// <summary>Locator scratch, reused every frame so the status sentence and the map's arrows are one answer.</summary>
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

        /// <summary>The refusal's own fade this frame, 0 when the strip carries a notice: a notice from the
        /// lesson is not about the bank, so it must not repaint it.</summary>
        private float _refusalAlpha;

        private bool _toastTutorialUp;

        /// <summary>The lesson's own sentence in that strip; set by <see cref="Notice"/>, never by a refusal.</summary>
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

        /// <summary>The driver's speed gear; null in a scene with no driver, where the button stays at 1x.</summary>
        private TickClock _clock;

        private bool _infoBuildShown;
        private BuildKind? _infoKind;
        private bool _infoDirty = true;
        private int _infoFacing = -1;

        private bool _inspectDirty = true;
        private Int2 _inspectCell = new Int2(-1, -1);
        private int _inspectTick = -1;
        private int _rowCount;

        private float _rowDrop;
        private float _infoExtra;

        /// <summary>Is the info card wearing its taller sorter-picker band? Relayed to the locator through
        /// <see cref="InfoGrown"/>: a panel the arrows do not know about is a place an arrow is drawn under.</summary>
        private bool _pickerUp;

        /// <summary>The picker's working state, seeded from the machine's own filter - never the definition
        /// row's, because the machine is what routes.</summary>
        private Shape _filterShape;
        private bool _filterMineral;
        private Quad _filterMineralPicked;

        /// <summary>The filter the working state was seeded from, so a change under the picker re-seeds it
        /// instead of being overwritten by stale paint. Compared field by field: <see cref="ShapeFilter"/> has
        /// no equality of its own.</summary>
        private ShapeFilter _filterSeen;
        private Int2 _filterCell = new Int2(-1, -1);

        private bool _tutorialUp;

        /// <summary>The last line the task card printed, and whether that was the completion line. Rebuilt on
        /// the tick a line is ticked and on nothing else; whether the card is up is asked of the panel itself.</summary>
        private int _seenTaskStep = -1;
        private bool _seenTaskComplete;

        /// <summary>When the lesson finished, in unscaled seconds, or -1 while it is still being taught.</summary>
        private float _tutorialDoneAt = -1f;

        /// <summary>The world the lesson notices below belong to. A map change is a new world and a restart
        /// rewinds this one, so both read as "a lesson with nothing to report yet".</summary>
        private SimWorld _noticeWorld;
        private int _noticedStep = -1;

        /// <summary>When the current line became current, in the world's own ticks. Being the world's clock, it
        /// stands still while the run is paused: held time is not stuck time.</summary>
        private int _stepTick = -1;

        private int _hintStep = -1;

        /// <summary>Is the current line's hint pinned into the task card? The toast is the announcement, the pin
        /// keeps the answer where the line lives.</summary>
        private bool _hintPinned;

        private bool _seenHintPinned;

        private string _hideWord;

        /// <summary>The player's own say over the "how to play" card; null means the campaign decides.</summary>
        private bool? _tutorialChoice;

        /// <summary>The tile under the pointer, pushed by the driver each frame; (-1,-1) hides the tooltip.</summary>
        private Int2 _cursor = new Int2(-1, -1);
        private Int2 _tooltipCell = new Int2(-2, -2);
        private bool _tooltipDirty = true;
        private int _tooltipTick = -1;
        private int _tooltipRows;

        /// <summary>Did the last fill find anything to say about <see cref="_tooltipCell"/>? False for plain
        /// ground.</summary>
        private bool _tooltipFilled;
        private float _tooltipWidth;
        private float _tooltipHeight;

        private bool _helpOpen;
        private string _compKey;

        /// <summary>The bar's kind set from before the current rebuild, so a rebuild that grew the bar can light
        /// the tiles it added. Empty is also the first build's answer: nothing is news.</summary>
        private readonly HashSet<BuildKind> _barBefore = new HashSet<BuildKind>();

        /// <summary>Set when a rebuild comes from a world swap: a new map's bar is all new and none of it is news.</summary>
        private bool _barSuppressPulse;

        /// <summary>What the player cannot see right now, as the locator's own summary. The map's arrows are made
        /// from the same call: one rule about what counts as out of view, said in two registers.</summary>
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

            // One list because the locator has one answer - a place to go - and the same three the arrows
            // are made from, so the sentence and the marks cannot name different sets.
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

        /// <summary>Read by the driver and handed to <see cref="BuildInput.Sample"/>, so a click on the UI never
        /// also builds under it.</summary>
        public bool PointerOverHud { get; private set; }

        private bool CanStartWaveNow =>
            _world.Status == GameStatus.Playing && !_world.Paused &&
            (_world.Waves.HasWaveToStart || _world.Tutorial.WaitsForWaveCall);

        /// <summary>Relayed to the locator and the guide through <see cref="ThreatRadarView.SetInfoGrown"/>: their
        /// panel boxes are the boxes the HUD draws.</summary>
        public bool InfoGrown => _pickerUp;

        /// <summary>How much taller than its base size the read card's wrapped rows have made it, in design units.</summary>
        public float InfoExtra => _infoExtra;

        /// <summary>The run's number of keys, not the table's: a tutorial's bar grows a key at a time. Relayed to
        /// the locator and the guide, whose panel boxes are the boxes the HUD draws.</summary>
        public int BarTiles => _bar != null ? _bar.Tiles.Count : HudLayout.BarTiles;

        /// <summary>The operators this map hands over that the map before it did not. A later map that adds a
        /// machine says what it is for, so a new operator is never dropped in without a word.</summary>
        private Operators NewOperators
        {
            get
            {
                if (_campaign == null || _campaign.IsFirstMap || _world == null) return Operators.None;
                MapDefinition previous = _catalog != null && _campaign.MapIndex > 0
                    ? _catalog.At(_campaign.MapIndex - 1)
                    : null;
                return previous == null ? Operators.None : MachineReading.NewOperators(_world.Map, previous);
            }
        }

        private bool HasIntroduction => NewOperators != Operators.None;

        /// <summary>Is the "how to play" card up? Up by default on the campaign's first map and on any later map
        /// that hands over a machine - unless that map brings a lesson, which owns the corner while it is taught.
        /// Once the player has hidden or shown the card themselves, their choice wins on every map.</summary>
        private bool ShowTutorial
        {
            get
            {
                if (_campaign == null) return false;
                if (_world != null && _world.Tutorial.Coached) return false;
                if (_tutorialChoice.HasValue) return _tutorialChoice.Value;

                // A later map that hands over a machine opens with the card up too, unless it brings a lesson
                // of its own, which then owns the corner the same way.
                return (_campaign.IsFirstMap && (!HasTutorial || LessonHandedOver)) ||
                       (HasIntroduction && (!HasTutorial || LessonHandedOver));
            }
        }

        /// <summary>Has the lesson finished and given the corner back? The two cards are complements: one corner,
        /// one card.</summary>
        private bool LessonHandedOver => HasTutorial && !TaskListWanted;

        private bool HasTutorial => _world != null && _world.Tutorial.Active;

        /// <summary>Is the task list on screen this frame? Yielded to the reference card while the player has
        /// asked for it with <c>T</c>: the corner belongs to whichever card they wanted to read.</summary>
        private bool TaskListUp => TaskListWanted && !ShowTutorial;

        private bool TaskListWanted
        {
            get
            {
                if (_world == null) return false;

                TutorialState tutorial = _world.Tutorial;
                if (!tutorial.Active) return false;
                if (tutorial.Coached) return false;
                if (!tutorial.Complete) return true;

                return _tutorialDoneAt >= 0f && Time.unscaledTime - _tutorialDoneAt < TaskDoneHold;
            }
        }

        /// <summary>How long "tutorial complete" stays up: the same order as a toast, the HUD's other piece that
        /// answers a moment and then leaves.</summary>
        private const float TaskDoneHold = 3f;

        /// <summary>How many task lines the card prints at once; five is the body's budget, so the corner never grows.</summary>
        private const int MaxTaskLines = 5;

        /// <summary>How long a line has to stand unsolved before its hint is offered, in run ticks - the world's
        /// clock, not the wall's.</summary>
        private static readonly int HintDelayTicks = (int)(SimConfig.TickRate * 30f);

        /// <summary>How long a newly unlocked bar tile wears the accent outline.</summary>
        private const float FreshSeconds = 2.5f;

        /// <summary>How long a lesson's own sentence stays in the strip a refusal uses, and the fraction of that
        /// which is the fade out.</summary>
        private const float NoticeSeconds = 4f;
        private const float HintSeconds = 7f;
        private const float NoticeFade = 0.3f;

        /// <summary>Is either card on screen? The one predicate the cards' claim on the pointer, the toast's
        /// clearance and the driver's camera rig all read.</summary>
        public bool TutorialUp => ShowTutorial || TaskListUp;

        /// <summary>The coin this run is paid in: the <em>complete</em> shape of the map's own mineral, since any
        /// full shape banks (<see cref="Shape.IsFull"/>).</summary>
        private Shape MoneyShape => Shape.Full(_world.Map.CurrencyMineral);

        /// <summary>Point the HUD at a world, its palette and its campaign. Called on every map change, which is why
        /// the canvas is built once and kept: a change rebinds the world and lets the next update redraw every card.
        /// The campaign and the callbacks are optional, so a scene or a test gets a HUD that draws but cannot be clicked.</summary>
        public void Initialize(SimWorld world, Palette palette, CampaignState campaign = null,
            Action<BuildKind?> selectKind = null, Action<int> rotate = null, MapCatalog catalog = null,
            Action<Int2, ShapeFilter> setFilter = null, TickClock clock = null, Action openMenu = null)
        {
            _world = world;
            if (clock != null) _clock = clock;
            _palette = palette;
            _campaign = campaign;
            _catalog = catalog;
            // A map that hands over a new machine opens with its card up even if the player hid "how to play"
            // earlier: that choice was about a card they had read, and this one is new.
            if (HasIntroduction) _tutorialChoice = null;
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

            // The hierarchy stands; a map change is a rebinding. Every dirty flag goes up, so the next
            // update pass redraws the cards against the new world.
            FillTutorial(_tutorial);
            MarkEverythingDirty();
        }

        private void Update()
        {
            if (_world == null || _palette == null || _canvasRoot == null) return;

            RefreshScale();

            // A lesson hands buildings over mid-run, so the bar's tile set is a run's rather than the table's.
            // Rebuilt rather than patched - the same answer a resize gets - because this happens once a line.
            if (!BarMatchesContent())
            {
                RebuildPanels();
                return;
            }

            UpdateMoneyMarks();

            // A click on a panel must not also build under it. This update runs after the driver's, so the
            // pointer is described as of the previous frame - a frame of lag on a pointer that has to travel to
            // the button anyway. The stockpile and the toast are read, never clicked, so clicks pass through.
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
            ApplyCoachConcealment();
        }

        private void OnDestroy()
        {
            // The shape icons and the shared sprite are the HUD's own runtime textures, held by nothing else, so
            // they go with the HUD that made them - a scene reload in the Editor would otherwise leak one set per play.
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

        /// <summary>Tear the panels down and build them again at the current screen scale. The one heavy pass
        /// the HUD has, asked for only by a resize.</summary>
        private void RebuildPanels()
        {
            _scale = HudLayout.Scale(Screen.height);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;

            // The bar's set before the teardown, so the rebuild can tell a tile the lesson just handed over
            // from one that was always there.
            _barBefore.Clear();
            if (_bar != null)
                for (int i = 0; i < _bar.Tiles.Count; i++)
                    _barBefore.Add(_bar.Tiles[i].Kind);

            bool pulse = !_barSuppressPulse;
            _barSuppressPulse = false;

            // Immediate, because the rebuild happens inside the same update that noticed the resize: a deferred
            // Destroy would leave both HUDs on screen for a frame.
            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_canvasRoot.GetChild(i).gameObject);

            BuildPanels();
            MarkEverythingDirty();

            if (pulse) MarkFreshTiles();
        }

        /// <summary>Light the tiles a rebuild just added to the bar - the thread between the toast at the top and
        /// the bar at the bottom. Only growth lights anything.</summary>
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
            _refusalAlpha = 0f;
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
            _rowDrop = 0f;
            _infoExtra = 0f;
            _hoveredTile = -1;

            // The picker's paint belongs to the machine it was seeded from, and a new world has none.
            _filterSeen = ShapeFilter.None;
            _filterCell = new Int2(-1, -1);
            _helpOpen = false;
            _compKey = null;
            _tooltipCell = new Int2(-2, -2);
            _tooltipDirty = true;
            _tooltipTick = -1;
            _tooltipRows = 0;
            _tutorialUp = !ShowTutorial;   // forces the tutorial's own update to decide again

            // The card is built at its base height, and a picker left standing on the old hierarchy would be
            // a band nobody re-placed.
            SetPickerUp(false);
        }

        // ------------------------------------------------------------------ the panels' geometry

        private const float HudRadius = 10f;

        private const float ControlRadius = 6f;

        /// <summary>The banner's card in design units: a transient overlay, off the screen's 22 % budget.</summary>
        private const float BannerWidth = 560f;
        private const float BannerHeight = 170f;
        private const float BannerRadius = 12f;

        private const float ToastRadius = 8f;
        private const float TooltipRadius = 8f;

        /// <summary>The card's inner margin in design units: one value for every card, so their text starts on the
        /// same vertical line whatever the card's width.</summary>
        private const float CardInset = 14f;

        private Color PanelBorder() => HudInk.Edge(_palette);

        private Color ButtonFill => HudInk.Raised(_palette);

        /// <summary>A design-unit font size through the current scale, rounded up: at 960x600 rounding a micro
        /// line down is the difference between a line and a squint.</summary>
        private int FontSize(float design) => Mathf.Max(8, Mathf.CeilToInt(design * _scale));

        /// <summary>Let a dynamic sentence drop below its step rather than run past the card: a step is a design
        /// decision while an overrun is a defect.</summary>
        private void AutoFit(HudText text, float minDesign)
        {
            text.Text.enableAutoSizing = true;
            text.Text.fontSizeMin = FontSize(minDesign);
            text.Text.fontSizeMax = text.Text.fontSize;
        }

        /// <summary>
        /// The left rail, read top to bottom in the order the questions come: whose run this is, how much
        /// Core is left, how long the clock has, what the next two waves are made of, and - only when the
        /// answer is bad - what is stuck and what is out of view.
        ///
        /// The hierarchy is carried by weight and ink, not colour: the map's name and the numbers are paper,
        /// the words that label them faint, and the only thing that changes colour is the number with something
        /// to say.
        /// </summary>
        private StatusPanel BuildStatus()
        {
            RectTransform root = BindPanel(StatusPrefab, "status", HudLayout.Status(_scale),
                PlaceCorner.TopLeft);
            var panel = new StatusPanel { Root = root };

            const float w = HudLayout.StatusWidth;
            const float inner = w - CardInset * 2f;

            AdoptCard(root, "status");

            panel.Title = BindLabel(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            AutoFit(panel.Title, HudType.Micro);

            panel.WaveTag = BindLabel(root, "waveTag", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleRight, tracked: true);

            panel.CoreCap = BindLabel(root, "coreCap", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            panel.CoreCap.Set("CORE", HudInk.Faint(_palette));

            panel.HealthValue = BindLabel(root, "coreValue", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleRight);

            panel.HealthTrack = HudUi.AdoptRound(HudUi.Child(root, "health.back"),
                HudInk.Raised(_palette), 3f);
            panel.Health = HudUi.AdoptBarFill(panel.HealthTrack.Rect, "fill", inner * _scale, 3f);

            HudBox clockBack = HudUi.AdoptRound(HudUi.Child(root, "clock.back"), HudInk.Raised(_palette), 2f);
            panel.Clock = HudUi.AdoptBarFill(clockBack.Rect, "fill", inner * _scale, 2f);

            panel.Rule = HudUi.AdoptBox(HudUi.Child(root, "rule"), HudInk.Rule(_palette));

            panel.News = BindLabel(root, "news", HudType.Label, FontStyle.Normal, TextAnchor.MiddleLeft);
            AutoFit(panel.News, 9f);

            panel.NextLabel = BindLabel(root, "waveNext", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            panel.AfterLabel = BindLabel(root, "waveAfter", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);

            panel.Chips = BindChips(root, "chip");
            panel.ChipsAfter = BindChips(root, "after");

            HudUi.AdoptBox(HudUi.Child(root, "footerRule"), HudInk.Rule(_palette));

            panel.Seed = BindLabel(root, "seed", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleLeft);
            panel.Speed = BindLabel(root, "speed", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleRight);

            // Two lines that only speak when something needs them: silence is the default and the good news.
            panel.Production = BindLabel(root, "production", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleLeft);
            AutoFit(panel.Production, 8f);
            panel.Production.Show(false);

            panel.Threat = BindLabel(root, "threat", HudType.Micro, FontStyle.Normal, TextAnchor.MiddleLeft);
            AutoFit(panel.Threat, 8f);
            panel.Threat.Show(false);

            return panel;
        }

        // panels from assets

        /// <summary>The panels that come from an asset under any <c>Resources</c> folder: a path rather than a
        /// serialized reference, because the driver makes the HUD in code and a test has no scene to wire one in.
        /// One per panel, so a diff touches the panel it changed.</summary>
        private const string StatusPrefab = "Hud/HudStatus";
        private const string TutorialPrefab = "Hud/HudTutorial";
        private const string TasksPrefab = "Hud/HudTasks";
        private const string ControlsPrefab = "Hud/HudControls";
        private const string HelpPrefab = "Hud/HudHelp";
        private const string BarPrefab = "Hud/HudBar";
        private const string BarTilePrefab = "Hud/HudBarTile";
        private const string InfoPrefab = "Hud/HudInfo";
        private const string StockpilePrefab = "Hud/HudStockpile";
        private const string BannerPrefab = "Hud/HudBanner";
        private const string ToastPrefab = "Hud/HudToast";
        private const string TooltipPrefab = "Hud/HudTooltip";

        private RectTransform BindPanel(string resourcePath, string name, Rect rect, PlaceCorner corner)
            => BindPanel(_canvasRoot, resourcePath, name, rect, corner);

        /// <summary>A panel from its prefab, under <paramref name="parent"/>: the asset is authored in design units
        /// and <see cref="ScaleDesignUnits"/> turns them into this screen's pixels once per rebuild.
        /// <paramref name="rect"/> is already in pixels; the parent is a parameter because the bar is one asset
        /// repeated, so a key is a child of the bar's card.</summary>
        private RectTransform BindPanel(Transform parent, string resourcePath, string name, Rect rect,
            PlaceCorner corner)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "HUD panel prefab 'Assets/Resources/" + resourcePath + ".prefab' is missing");

            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = name;
            var root = (RectTransform)instance.transform;
            Place(root, rect, corner);
            ScaleDesignUnits(root);
            return root;
        }

        private void ScaleDesignUnits(RectTransform root) => HudUi.ScaleDesignUnits(root, _scale);

        /// <summary>A label the prefab places, adopted at the size its design unit names, by the rule <see cref="Label"/>'s
        /// - so an authored label and a built one cannot disagree.</summary>
        private HudText BindLabel(RectTransform root, string name, float design, FontStyle style,
            TextAnchor anchor, bool wrap = false, bool tracked = false, bool rich = false)
            => HudUi.AdoptText(HudUi.Child(root, name), FontSize(design), style, anchor, wrap, tracked, rich);

        /// <summary>A button the prefab places: the size and the words are the design's, the surface and the hover
        /// are the view's - <see cref="UpdateButtons"/> dresses every one of them from the palette.</summary>
        private HudButton BindButton(RectTransform root, string name, float design, Color labelColour,
            string label, Action pressed)
            => HudUi.AdoptButton(HudUi.Child(root, name), label, FontSize(design), labelColour, pressed);

        /// <summary>A card's three surfaces, adopted: the cast shadow, the fill and the milled edge. The card's name is
        /// spelled once here, so an asset's node names and this builder agree by convention (<c>status.shadow</c>,
        /// <c>status.back</c>, <c>status.frame</c>).</summary>
        private void AdoptCard(RectTransform root, string name, float radius = HudRadius)
        {
            HudUi.AdoptShadow(HudUi.Child(root, name + ".shadow"), radius, _palette.HudShadow);
            HudUi.AdoptRound(HudUi.Child(root, name + ".back"), _palette.HudPanel, radius);
            HudUi.AdoptBorder(HudUi.Child(root, name + ".frame"), HudInk.Edge(_palette), radius);
        }

        /// <summary>One row of chips, hidden until a wave is briefed into them; the prefab carries the six slots and
        /// <see cref="LayoutChips"/> places and shows them.</summary>
        private Chip[] BindChips(RectTransform root, string prefix)
        {
            var chips = new Chip[ChipCapacity];

            for (int i = 0; i < ChipCapacity; i++)
            {
                var chip = new Chip
                {
                    Back = HudUi.AdoptRound(HudUi.Child(root, prefix + i + ".back"),
                        HudInk.Raised(_palette), 4f),
                    Count = BindLabel(root, prefix + i + ".count", HudType.Micro, FontStyle.Bold,
                        TextAnchor.MiddleLeft, tracked: true),
                    Name = BindLabel(root, prefix + i + ".name", HudType.Micro, FontStyle.Normal,
                        TextAnchor.MiddleLeft),
                };

                RawImage icon = HudUi.AdoptIcon(HudUi.Child(root, prefix + i + ".icon"));
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
            RectTransform root = BindPanel(TutorialPrefab, "tutorial", HudLayout.Tutorial(_scale),
                PlaceCorner.TopLeft);
            var panel = new TutorialPanel { Root = root };

            AdoptCard(root, "tutorial");
            panel.Title = BindLabel(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);
            panel.Hide = BindButton(root, "hide", HudType.Micro, HudInk.Muted(_palette), "HIDE  T",
                () => SetTutorialShown(false));
            HudUi.AdoptBox(HudUi.Child(root, "rule"), HudInk.Rule(_palette));

            panel.Body = BindLabel(root, "body", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft,
                wrap: false, rich: true);
            AutoFit(panel.Body, 8f);

            FillTutorial(panel);
            root.gameObject.SetActive(ShowTutorial);
            return panel;
        }

        /// <summary>
        /// The lesson's card, in the corner the reference card wears: same rect, same width and height, so the top
        /// strip does not have to know which card is up and the asset carries only what the two do not share.
        ///
        /// No close button: the card is not a thing the player opens, so it is not a thing they dismiss.
        /// </summary>
        private TaskPanel BuildTasks()
        {
            RectTransform root = BindPanel(TasksPrefab, "tasks", HudLayout.Tutorial(_scale),
                PlaceCorner.TopLeft);
            var panel = new TaskPanel { Root = root };

            AdoptCard(root, "tasks");
            panel.Title = BindLabel(root, "title", HudType.Body, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true);

            panel.Count = BindLabel(root, "count", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleRight);

            HudUi.AdoptBox(HudUi.Child(root, "rule"), HudInk.Rule(_palette));

            panel.Body = BindLabel(root, "body", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft,
                wrap: false, rich: true);
            AutoFit(panel.Body, 8f);

            panel.Hint = BindLabel(root, "hint", HudType.Micro, FontStyle.Italic, TextAnchor.UpperLeft);
            AutoFit(panel.Hint, 8f);
            panel.Hint.Show(false);

            root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>The card's five lines for the map being played, re-said on every map change because the card is
        /// built once and kept. Five lines of Micro type, one numeral each, no wrapping.</summary>
        private void FillTutorial(TutorialPanel panel)
        {
            if (panel == null || panel.Title == null) return;

            string accent = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudAccent);
            Color ink = HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.18f);

            Operators fresh = NewOperators;
            string[] lines;
            if (fresh == Operators.None)
            {
                panel.Title.Set("HOW TO PLAY", _palette.HudText);
                lines = new[]
                {
                    "Drag Belt (1) from ore into the Core - a belt on ore mines it.",
                    "A full shape banked earns one coin.",
                    "Each wave is weak to the shape printed above it.",
                    "Cutter (3) splits, Rotator (4) turns  \u00B7  Q/E turns a ghost.",
                    "Belt it to a Turret (0): the exact shape does full damage.",
                };
            }
            else if ((fresh & Operators.Stacker) != 0)
            {
                // The map that first asks for a mixed round: what the machine is for, then the recipe.
                panel.Title.Set("NEW: STACKER (" + HotkeyFor(Operators.Stacker) + ")", _palette.HudText);
                lines = new[]
                {
                    MachineReading.Introduction(Operators.Stacker),
                    "Mine both ores, and cut a half of each with a Cutter (" + HotkeyFor(Operators.Cutter) + ").",
                    "Belt the two halves into two sides of the Stacker.",
                    "Halves on the same side never join - it waits for the other.",
                    "A two-colour figure above is a round only it can make.",
                };
            }
            else
            {
                panel.Title.Set("NEW ON THIS MAP", _palette.HudText);
                var list = new List<string>();
                foreach (Operators op in new[] { Operators.Cutter, Operators.Rotator })
                    if ((fresh & op) != 0)
                        list.Add(MachineReading.Introduction(op) + " (" + HotkeyFor(op) + ")");
                lines = list.ToArray();
            }

            var body = new StringBuilder();
            for (int i = 0; i < lines.Length && i < 5; i++)
            {
                if (i > 0) body.Append('\n');
                body.Append("<color=").Append(accent).Append('>').Append(i + 1).Append("</color>  ").Append(lines[i]);
            }

            panel.Body.Set(body.ToString(), ink);
        }

        private static string HotkeyFor(Operators op)
        {
            BuildKind kind = op == Operators.Cutter ? BuildKind.Cutter
                : op == Operators.Rotator ? BuildKind.Rotator
                : BuildKind.Stacker;
            return BuildInput.HotkeyLabel((int)kind);
        }

        /// <summary>The controls cluster: wave, pause, gear, menu and help, top-right. The keyboard list lives behind
        /// the help button, and restart lives inside that card - one deliberate click further from a misfire.</summary>
        private ControlsPanel BuildControls()
        {
            RectTransform root = BindPanel(ControlsPrefab, "controls",
                HudLayout.Controls(Screen.width, _scale), PlaceCorner.TopRight);
            var panel = new ControlsPanel { Root = root };

            panel.Wave = BindButton(root, "wave", HudType.Micro, _palette.HudPanel, "WAVE", PressWave);
            panel.Wave.Root.gameObject.SetActive(false);

            panel.Pause = BindButton(root, "pause", HudType.Body, _palette.HudText, string.Empty, PressPause);

            // Two bars rather than the word "II": a transport glyph is drawn, not typed, so it cannot turn
            // into a missing-character box.
            panel.PauseBars = HudUi.Child(panel.Pause.Root, "bars");
            panel.PauseBarA = HudUi.AdoptRound(HudUi.Child(panel.PauseBars, "a"), _palette.HudText, 1f);
            panel.PauseBarB = HudUi.AdoptRound(HudUi.Child(panel.PauseBars, "b"), _palette.HudText, 1f);

            panel.Gear = BindButton(root, "gear", HudType.Label, _palette.HudText, GearWord(), PressGear);
            panel.Help = BindButton(root, "help", HudType.Body, _palette.HudText, "?",
                () => SetHelpOpen(!_helpOpen));

            panel.Menu = BindButton(root, "menu", HudType.Micro, _palette.HudText, "MENU", PressMenu);

            return panel;
        }

        /// <summary>The menu button's press. Read through the driver rather than toggled here, because the driver
        /// owns the run's hold - the separation the pause and wave buttons keep.</summary>
        private void PressMenu() => _openMenu?.Invoke();

        /// <summary>The help card: the keyboard, one pair of ideas per line, and the restart button - here rather
        /// than on the cluster, so a misplaced click can never restart the run. It overlays the map while open.</summary>
        private HelpPanel BuildHelp()
        {
            string[] lines = KeyLines();
            RectTransform root = BindPanel(HelpPrefab, "help",
                HudLayout.Help(Screen.width, _scale, lines.Length), PlaceCorner.TopRight);
            var panel = new HelpPanel { Root = root };

            AdoptCard(root, "help");

            BindLabel(root, "header", HudLayout.HelpLineSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true).Set("CONTROLS", _palette.HudText);
            HudUi.AdoptBox(HudUi.Child(root, "rule"), HudInk.Rule(_palette));

            for (int i = 0; i < lines.Length; i++)
            {
                HudText line = BindLabel(root, "key" + i, HudLayout.HelpLineSize, FontStyle.Normal,
                    TextAnchor.MiddleLeft);
                AutoFit(line, 8f);
                line.Set(lines[i], HudInk.Muted(_palette));
            }

            panel.Restart = BindButton(root, "restart", HudType.Label, _palette.HudText,
                "R  restart  run", PressRestart);
            panel.Close = BindButton(root, "close", HudType.Label, _palette.HudText, "×",
                () => SetHelpOpen(false));

            root.gameObject.SetActive(false);
            return panel;
        }

        private StockpilePanel BuildStockpile()
        {
            RectTransform root = BindPanel(StockpilePrefab, "stockpile",
                HudLayout.Stockpile(Screen.width, _scale), PlaceCorner.TopLeft);
            var panel = new StockpilePanel { Root = root };

            AdoptCard(root, "stockpile");
            panel.Number = BindLabel(root, "number", HudType.Display, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);

            panel.Icon = HudUi.AdoptIcon(HudUi.Child(root, "icon"));
            Paint(panel.Icon, _icons.Mark(MoneyShape));
            panel.Icon.gameObject.SetActive(panel.Icon.texture != null);

            return panel;
        }

        private BarPanel BuildBar()
        {
            BuildKind[] kinds = VisibleKinds();
            int count = kinds.Length;

            Rect rect = HudLayout.Bar(Screen.width, Screen.height, _scale, count);
            RectTransform root = BindPanel(BarPrefab, "bar", rect, PlaceCorner.BottomLeft);
            var panel = new BarPanel { Root = root };

            AdoptCard(root, "bar");

            // Where a key starts inside the card is the asset's answer; how many keys follow is the run's.
            RectTransform row = HudUi.Child(root, "tiles");

            const float gap = HudLayout.BarGap;
            const float tileHeight = 40f;
            float tileWidth = HudLayout.BarTileWidth;

            // Derived from the width the card actually got, because HudLayout.Bar clamps it to the screen: a
            // narrow viewport would otherwise lay the trailing keys over the card's edge.
            if (count > 0)
            {
                float fit = (rect.width / _scale - 2f * HudLayout.BarPad - (count - 1) * gap) / count;
                if (fit < tileWidth) tileWidth = fit;
            }

            panel.TileWidth = tileWidth * _scale;

            float height = tileHeight * _scale;
            float spacing = (tileWidth + gap) * _scale;

            for (int i = 0; i < count; i++)
                panel.Tiles.Add(BindTile(row, kinds[i], i,
                    new Rect(i * spacing, 0f, panel.TileWidth, height), tileWidth));

            return panel;
        }

        /// <summary>
        /// One key: the asset's nine pieces, the run's words and wiring. <paramref name="rect"/> is in pixels;
        /// <paramref name="tileWidth"/> is the design width the bar settled on for this screen.
        ///
        /// A key's hairline is the tile's state, not a button's, which is why this adopts a HudButton rather than
        /// a whole one.
        /// </summary>
        private BuildTile BindTile(RectTransform row, BuildKind kind, int index, Rect rect, float tileWidth)
        {
            RectTransform root = BindPanel(row, BarTilePrefab, "tile" + index, rect, PlaceCorner.TopLeft);
            var tile = new BuildTile { Kind = kind, Root = root };

            tile.Button = root.gameObject.AddComponent<HudButton>();
            tile.Button.Fill = HudUi.AdoptRound(HudUi.Child(root, "back"), HudInk.Raised(_palette),
                ControlRadius);
            tile.Button.Fill.Image.raycastTarget = true;
            tile.Button.Pressed = () => SelectKind(kind);

            float narrow = Mathf.Max(0f, (HudLayout.BarTileWidth - tileWidth) * _scale);
            var cut = new Vector2(narrow, 0f);

            tile.Name = BindLabel(root, "name", HudType.Label, FontStyle.Bold, TextAnchor.MiddleLeft);
            tile.Name.Set(_world.Content.Machine(kind).Name, _palette.HudText);
            AutoFit(tile.Name, 9f);
            tile.Name.Rect.sizeDelta -= cut;

            tile.Keycap = HudUi.AdoptRound(HudUi.Child(root, "keycap"), HudInk.Raised(_palette), 4f);

            tile.Hotkey = BindLabel(root, "hotkey", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleCenter);
            tile.Hotkey.Set(BuildInput.HotkeyLabel((int)kind), HudInk.Faint(_palette));

            tile.Cost = BindLabel(root, "cost", HudType.Micro, FontStyle.Bold, TextAnchor.MiddleRight);
            tile.Cost.Set(_world.Content.Machine(kind).Cost.ToString(), HudInk.Muted(_palette));
            AutoFit(tile.Cost, 7f);
            tile.Cost.Rect.sizeDelta -= cut;

            tile.CostIcon = HudUi.AdoptIcon(HudUi.Child(root, "costIcon"));
            tile.CostIcon.rectTransform.anchoredPosition -= cut;
            tile.CostIcon.gameObject.SetActive(false);

            tile.Afford = HudUi.AdoptBar(HudUi.Child(root, "afford"), tile.Name.Rect.sizeDelta.x, 1f);
            tile.Afford.Rect.sizeDelta -= cut;

            tile.Border = HudUi.AdoptBorder(HudUi.Child(root, "frame"), _palette.HudAccent, ControlRadius, 1f);
            tile.Border.Show(false);

            tile.Cue = HudUi.AdoptBorder(HudUi.Child(root, "cue"), _palette.HudAccent, ControlRadius, 3f);
            tile.Cue.Show(false);

            tile.Selected = kind == _world.SelectedKind;
            tile.Affordable = _world.Economy.CanAfford(kind);

            DressBuildTile(tile);
            return tile;
        }

        /// <summary>
        /// The info card: two faces on one surface. With a building hovered or selected it is the catalogue's
        /// card; with nothing selected it is what the player <em>read</em> instead, one row per fact. Both faces
        /// are <see cref="InfoPrefab"/> - <see cref="HudLayout"/> sizes the card, and what stays here is what it
        /// says and the palette it says it in.
        /// </summary>
        private InfoPanel BuildInfo()
        {
            RectTransform root = BindPanel(InfoPrefab, "info",
                HudLayout.Info(Screen.width, Screen.height, _scale), PlaceCorner.BottomRight);
            var panel = new InfoPanel { Root = root };

            AdoptCard(root, "info");

            // ----- the build card: a title, a hairline, then the catalogue's sentences
            panel.BuildRoot = HudUi.Child(root, "build");

            panel.BuildTitle = BindLabel(panel.BuildRoot, "title", HudType.Body, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            AutoFit(panel.BuildTitle, HudType.Micro);

            panel.BuildRight = BindLabel(panel.BuildRoot, "right", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleRight);

            HudUi.AdoptBox(HudUi.Child(panel.BuildRoot, "rule"), HudInk.Rule(_palette));

            panel.Blurb = BindLabel(panel.BuildRoot, "blurb", HudType.Label, FontStyle.Normal,
                TextAnchor.UpperLeft, wrap: true);
            AutoFit(panel.Blurb, 8f);

            panel.Stats = BindLabel(panel.BuildRoot, "stats", HudType.Micro, FontStyle.Normal,
                TextAnchor.UpperLeft, wrap: true);
            AutoFit(panel.Stats, 8f);

            panel.Facing = BindLabel(panel.BuildRoot, "facing", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            AutoFit(panel.Facing, 8f);

            // The card's three quiet actions along its bottom edge: the two that turn the ghost, and the door to
            // the face that reads the map. The asset places them off the right edge, where the arithmetic used to be.
            panel.TurnLeft = BindButton(panel.BuildRoot, "turnL", HudType.Label, _palette.HudText, "<",
                () => _rotate?.Invoke(-1));
            panel.TurnRight = BindButton(panel.BuildRoot, "turnR", HudType.Label, _palette.HudText, ">",
                () => _rotate?.Invoke(+1));
            panel.Inspect = BindButton(panel.BuildRoot, "inspect", HudType.Micro, _palette.HudText, "READ",
                PressInspect);

            // ----- the inspect card: the same header grammar, then one row per fact
            panel.InspectRoot = HudUi.Child(root, "inspect");

            panel.InspectTitle = BindLabel(panel.InspectRoot, "title", HudType.Body, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            AutoFit(panel.InspectTitle, HudType.Micro);

            panel.InspectRight = BindLabel(panel.InspectRoot, "right", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleRight);

            HudUi.AdoptBox(HudUi.Child(panel.InspectRoot, "rule"), HudInk.Rule(_palette));

            // Six lines: the card prints the ones the machine has and hides the rest. A sentence longer than the
            // card wraps rather than printing past its edge, and the stack grows the card upward.
            panel.Rows = new HudText[InspectRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = BindLabel(panel.InspectRoot, "row" + i, HudType.Label, FontStyle.Normal,
                    TextAnchor.MiddleLeft, wrap: true);
                panel.Rows[i].Show(false);
            }

            // The sorter's filter picker, the one band of the card that is not always there.
            panel.Picker = BindFilterPicker(panel.InspectRoot);

            panel.BuildRoot.gameObject.SetActive(false);
            panel.InspectRoot.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>
        /// The sorter picker as widgets: a quartered canvas of ghost cells and one figure, a mode toggle and the
        /// sentence saying what the picker would route for. The ghosts are empty quadrants and nothing else is - on
        /// the map an empty quadrant is drawn as nothing, and only here, where it is being chosen, is it a cell.
        /// </summary>
        private FilterPicker BindFilterPicker(RectTransform inspectRoot)
        {
            var picker = new FilterPicker { Root = HudUi.Child(inspectRoot, "picker") };

            picker.Title = BindLabel(picker.Root, "title", HudType.Micro, FontStyle.Bold,
                TextAnchor.MiddleLeft, tracked: true);
            picker.Title.Set("ROUTES", HudInk.Faint(_palette));

            RectTransform canvas = HudUi.Child(picker.Root, "canvas");

            // Draw order inside the canvas is the read order: the ghost cells, then the figure over them, then
            // the buttons that claim the clicks. The asset carries it in exactly that order.
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                picker.Ghosts[quadrant] = HudUi.AdoptRound(HudUi.Child(canvas, "ghost" + quadrant),
                    GhostCell, 2f);

            // The item's picture is the HUD's own runtime atlas, so the asset holds the node and the update pass
            // paints the shape onto it.
            picker.Figure = HudUi.AdoptIcon(HudUi.Child(canvas, "figure"));

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                // One button per quadrant, its own label hidden: the picture underneath is the content, and
                // a click on a quarter is a click on that quadrant of the item.
                int captured = quadrant;
                HudButton button = BindButton(canvas, "pick" + quadrant, HudType.Micro, _palette.HudText,
                    string.Empty, () => PressFilterQuadrant(captured));
                button.Label.Show(false);
                picker.Quadrants[quadrant] = button;
            }

            HudUi.AdoptBorder(HudUi.Child(canvas, "frame"), PanelBorder(), 3f, 1f);

            picker.Mode = BindButton(picker.Root, "mode", HudType.Micro, _palette.HudText, string.Empty,
                PressFilterMode);

            picker.Caption = BindLabel(picker.Root, "caption", HudType.Micro, FontStyle.Normal,
                TextAnchor.MiddleLeft);
            AutoFit(picker.Caption, 8f);

            picker.Root.gameObject.SetActive(false);
            return picker;
        }

        /// <summary>
        /// The one thing that has to be unmissable: the run is over, or the game is paused. A card the layout does
        /// not size - a transient overlay, off the screen's 22 % budget - centred with its top edge at 42 % of the
        /// leftover height.
        /// </summary>
        private BannerPanel BuildBanner()
        {
            Rect rect = new Rect((Screen.width - BannerWidth * _scale) * 0.5f,
                (Screen.height - BannerHeight * _scale) * 0.42f,
                BannerWidth * _scale, BannerHeight * _scale);

            RectTransform root = BindPanel(BannerPrefab, "banner", rect, PlaceCorner.TopCentre);
            var panel = new BannerPanel { Root = root };

            AdoptCard(root, "banner", BannerRadius);
            panel.Label = BindLabel(root, "label", HudType.Display, FontStyle.Bold,
                TextAnchor.MiddleCenter, wrap: true, tracked: true);

            // Nothing to say until the world says it: an overlay that starts hidden cannot flash an empty card
            // in the frame between a rebuild and the update that decides it.
            root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>
        /// The strip of news under the top cards. Its one line is an inset rather than a placed rectangle, because
        /// the strip is sized to its own sentence by <see cref="UpdateToast"/>; its <see cref="CanvasGroup"/> is
        /// the asset's too, so the fade is one alpha, and a strip that takes no clicks lets a refusal be read over
        /// a live map.
        /// </summary>
        private ToastPanel BuildToast()
        {
            RectTransform root = BindPanel(ToastPrefab, "toast", new Rect(0f, 0f, 200f, 30f),
                PlaceCorner.TopLeft);
            var panel = new ToastPanel { Root = root };

            panel.Group = root.GetComponent<CanvasGroup>();
            panel.Group.blocksRaycasts = false;
            panel.Group.interactable = false;
            panel.Group.alpha = 0f;

            HudUi.AdoptShadow(HudUi.Child(root, "toast.shadow"), ToastRadius, _palette.HudShadow);
            panel.Back = HudUi.AdoptRound(HudUi.Child(root, "toast.back"), _palette.HudPanel, ToastRadius);
            // The frame is the ink the strip is speaking in - a refusal's warning, a notice's accent - and it is
            // the one surface on the HUD drawn two units thick.
            panel.Frame = HudUi.AdoptBorder(HudUi.Child(root, "toast.frame"), _palette.HudWarn, ToastRadius, 2f);

            panel.Label = BindLabel(root, "label", HudType.Label, FontStyle.Bold,
                TextAnchor.MiddleCenter, tracked: true);

            root.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>The cursor tooltip: what is under the pointer, in two or three lines, beside the pointer itself.
        /// It takes no clicks and claims no pointer - it is news, not a panel - so a click passes through to the tile
        /// it describes, and its box is sized to its own words.</summary>
        private TooltipPanel BuildTooltip()
        {
            RectTransform root = BindPanel(TooltipPrefab, "tooltip", new Rect(0f, 0f, 200f, 68f),
                PlaceCorner.TopLeft);
            var panel = new TooltipPanel { Root = root };

            AdoptCard(root, "tooltip", TooltipRadius);

            panel.Title = BindLabel(root, "title", HudType.Label, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);

            panel.Rows = new HudText[TooltipRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = BindLabel(root, "row" + i, HudType.Micro, FontStyle.Normal,
                    TextAnchor.UpperLeft);
                panel.Rows[i].Show(false);
            }

            root.gameObject.SetActive(false);
            return panel;
        }

        // ------------------------------------------------------------------ placing helpers

        private enum PlaceCorner { TopLeft, TopRight, BottomLeft, BottomRight, TopCentre }

        /// <summary>Put a panel root where <see cref="HudLayout"/> says it goes, corner for corner.
        ///
        /// The two systems measure the screen in opposite directions: HudLayout speaks IMGUI's y-down screen
        /// space, the canvas uGUI's y-up. A top-hung piece keeps its own top edge, a bottom-hung piece its bottom
        /// edge - getting one backwards mirrors the whole HUD about the screen's middle.</summary>
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
                case PlaceCorner.TopCentre:
                    HudUi.TopCentre(rt, rect.y, rect.width, rect.height);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(corner), corner, "a panel sits in a corner");
            }
        }

        // ------------------------------------------------------------------ pointer

        private bool ComputePointerOver()
        {
            if (_status == null) return false;

            Vector2 pointer = Pointer();
            if (pointer.x < 0f) return false;

            if (OverShown(_status.Root, pointer) || OverShown(_controls.Root, pointer) ||
                OverShown(_bar.Root, pointer) || OverShown(_info.Root, pointer))
                return true;

            for (int i = 0; i < _pointerClaims.Count; i++)
            {
                RectTransform claim = _pointerClaims[i];
                if (claim != null && claim.gameObject.activeInHierarchy && Over(claim, pointer)) return true;
            }

            if (_helpOpen && Over(_help.Root, pointer)) return true;

            if (ShowTutorial && _tutorial != null && _tutorial.Root.gameObject.activeSelf &&
                Over(_tutorial.Root, pointer))
                return true;

            return _tasks != null && _tasks.Root.gameObject.activeSelf && Over(_tasks.Root, pointer);
        }

        /// <summary>The pointer in screen pixels, or (-1, -1) where there is no mouse to ask.</summary>
        private static Vector2 Pointer()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : new Vector2(-1f, -1f);
        }

        private static bool Over(RectTransform rt, Vector2 screenPoint)
            => RectTransformUtility.RectangleContainsScreenPoint(rt, screenPoint, null);

        /// <summary>Over a panel the coach has not hidden: a concealed card is not there to click.</summary>
        private static bool OverShown(RectTransform rt, Vector2 screenPoint) => Shown(rt) && Over(rt, screenPoint);

        // ------------------------------------------------------------------ the coached lesson

        /// <summary>Other views' boxes that take the pointer the way a card does - the coach's popup - so a
        /// click on its button does not also build on the tile under it.</summary>
        private readonly List<RectTransform> _pointerClaims = new List<RectTransform>();

        /// <summary>Let another view's box claim the pointer like a HUD card. Claimed for good; a destroyed or
        /// hidden box claims nothing.</summary>
        public void ClaimPointer(RectTransform box)
        {
            if (box != null && !_pointerClaims.Contains(box)) _pointerClaims.Add(box);
        }

        /// <summary>A coached map opens with an empty HUD and each line reveals the pieces it talks about
        /// (<see cref="TutorialState.Revealed"/>). Hidden with a <see cref="CanvasGroup"/> rather than by
        /// switching the panel off, so every panel keeps drawing itself and is correct the frame it appears -
        /// and a hidden panel takes no clicks. Every other map reveals everything, which is a no-op.</summary>
        private void ApplyCoachConcealment()
        {
            TutorialHud shown = _world.Tutorial.Revealed;

            SetShown(_status?.Root, (shown & TutorialHud.Status) != 0);
            SetShown(_stockpile?.Root, (shown & TutorialHud.Stockpile) != 0);
            SetShown(_bar?.Root, (shown & TutorialHud.Bar) != 0);
            SetShown(_controls?.Root, (shown & TutorialHud.Controls) != 0);
            SetShown(_info?.Root, (shown & TutorialHud.Info) != 0);
        }

        private static void SetShown(RectTransform root, bool shown)
        {
            if (root == null) return;

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                if (shown) return;   // never hidden, nothing to undo
                group = root.gameObject.AddComponent<CanvasGroup>();
            }

            float alpha = shown ? 1f : 0f;
            if (Mathf.Approximately(group.alpha, alpha)) return;

            group.alpha = alpha;
            group.blocksRaycasts = shown;
            group.interactable = shown;
        }

        private static bool Shown(RectTransform root)
        {
            if (root == null) return false;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            return group == null || group.alpha > 0f;
        }

        /// <summary>
        /// Where a piece of the HUD is on screen, in screen pixels (origin bottom-left), for the coach to box. The
        /// HUD measures its own pieces so a box can never drift from what it points at. False for a piece that is
        /// not on screen - not built yet, hidden, or a bar key the run has not been handed.
        /// <paramref name="building"/> says which bar key <see cref="TutorialFocus.BarTile"/> means.
        /// </summary>
        public bool TryFocusRect(TutorialFocus focus, BuildKind building, out Rect screen)
        {
            screen = default;
            if (_status == null) return false;

            var union = new FocusUnion();
            switch (focus)
            {
                case TutorialFocus.Stockpile:
                    if (Shown(_stockpile?.Root)) union.Add(_stockpile.Root);
                    break;

                case TutorialFocus.StatusCard:
                    if (Shown(_status.Root)) union.Add(_status.Root);
                    break;

                case TutorialFocus.CoreHealth:
                    if (!Shown(_status.Root)) break;
                    union.Add(_status.CoreCap.Rect);
                    union.Add(_status.HealthValue.Rect);
                    union.Add(_status.HealthTrack.Rect);
                    break;

                case TutorialFocus.WaveClock:
                    if (!Shown(_status.Root)) break;
                    union.Add(_status.WaveTag.Rect);
                    union.Add(_status.News.Rect);
                    break;

                case TutorialFocus.WaveChips:
                    if (!Shown(_status.Root)) break;
                    union.Add(_status.NextLabel.Rect);
                    for (int i = 0; i < _status.Chips.Length; i++)
                        if (_status.Chips[i].Back.Rect.gameObject.activeInHierarchy) union.Add(_status.Chips[i].Back.Rect);
                    break;

                case TutorialFocus.WaveButton:
                    if (Shown(_controls?.Root) && _controls.Wave != null) union.Add(_controls.Wave.Root);
                    break;

                case TutorialFocus.BuildBar:
                    if (!Shown(_bar?.Root)) break;
                    for (int i = 0; i < _bar.Tiles.Count; i++)
                        if (_bar.Tiles[i].Root.gameObject.activeInHierarchy) union.Add(_bar.Tiles[i].Root);
                    break;

                case TutorialFocus.BarTile:
                    if (!Shown(_bar?.Root)) break;
                    for (int i = 0; i < _bar.Tiles.Count; i++)
                        if (_bar.Tiles[i].Kind == building) union.Add(_bar.Tiles[i].Root);
                    break;
            }

            return union.TryGet(out screen);
        }

        /// <summary>The smallest screen rectangle holding a set of HUD nodes: on a screen-space overlay canvas a
        /// node's world corners are screen pixels.</summary>
        private struct FocusUnion
        {
            private bool _any;
            private Vector2 _min;
            private Vector2 _max;

            public void Add(RectTransform rt)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) return;

                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    Vector2 c = corners[i];
                    if (!_any)
                    {
                        _min = _max = c;
                        _any = true;
                        continue;
                    }

                    _min = Vector2.Min(_min, c);
                    _max = Vector2.Max(_max, c);
                }
            }

            public bool TryGet(out Rect rect)
            {
                rect = _any ? Rect.MinMaxRect(_min.x, _min.y, _max.x, _max.y) : default;
                return _any && rect.width > 0f && rect.height > 0f;
            }
        }

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
        /// card otherwise wears. It prints the history and the present only, and the ticked lines stay up (muted)
        /// because they are the record of what the bar has already grown by.
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

            // Asked of the panel rather than of a cached flag: the lesson's last line also changes the bar's key
            // set, so this frame's update went through RebuildPanels and returned, and the panel that rebuild made
            // is inactive while the completion hold's timestamp is stamped on the frame after.
            if (up != _tasks.Root.gameObject.activeSelf) _tasks.Root.gameObject.SetActive(up);

            if (!up) return;
            if (tutorial.StepsCompleted == _seenTaskStep && tutorial.Complete == _seenTaskComplete &&
                _hintPinned == _seenHintPinned) return;

            _seenTaskStep = tutorial.StepsCompleted;
            _seenTaskComplete = tutorial.Complete;
            _seenHintPinned = _hintPinned;

            _tasks.Title.Set(tutorial.Complete ? "TUTORIAL COMPLETE" : "TUTORIAL", _palette.HudText);
            _tasks.Count.Set(tutorial.StepsCompleted + " / " + tutorial.Count, HudInk.Muted(_palette));
            _tasks.Count.Show(!tutorial.Complete);
            _tasks.Body.Set(TaskLines(tutorial), HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.18f));

            // The pin is the card's own copy of what the toast said once, in the card's second voice - italic,
            // muted - so it reads as an answer offered, not as another task.
            bool hint = _hintPinned && !tutorial.Complete && !string.IsNullOrEmpty(tutorial.Current.Hint);
            _tasks.Hint.Show(hint);
            if (hint) _tasks.Hint.Set(tutorial.Current.Hint, HudInk.Muted(_palette));
        }

        /// <summary>
        /// The lesson speaking for itself: a line ticked, the end of the lesson, and - for a line that has stood
        /// unsolved too long - what the rule is asking for. The hint is said in the strip once <em>and</em> pinned
        /// into the task card until the line ticks, because refusals outrank notices in that strip.
        ///
        /// The stuck timer runs on the world's tick count, not the wall clock, so time spent away is never counted
        /// as time spent stuck.
        /// </summary>
        private void UpdateLessonNotice(TutorialState tutorial)
        {
            int steps = tutorial.StepsCompleted;

            // A map change is a new world and a restart rewinds this one - the lesson has nothing to report on
            // either.
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

        /// <summary>What a ticked line says: its number, and the buildings it just put on the bar. The last line's
        /// reward is the whole bar.</summary>
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

        /// <summary>The card's body as rich text: one numbered line per ticked task and the current one, the current
        /// bright and the rest muted. Colour is set per run of text rather than by the label's own colour, because one
        /// label carries both states.</summary>
        private string TaskLines(TutorialState tutorial)
        {
            string accent = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudAccent);
            string done = "#" + ColorUtility.ToHtmlStringRGB(HudInk.Muted(_palette));
            string live = "#" + ColorUtility.ToHtmlStringRGB(_palette.HudText);

            // A pinned hint takes one row of the budget: the corner's height is the invariant it cannot move.
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
        /// The strip under the controls, in two voices: a <b>refusal</b> is the answer to the click the player just
        /// made, so it wins the strip; a <b>notice</b> is the lesson talking, and expires by itself. One widget,
        /// because both are a single sentence at the top of the screen for a few seconds.
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
                _refusalAlpha = 0f;
                _toast.Group.alpha = 0f;
                return;
            }

            string text = refused ? RefusalText(refusal) : _noticeText;
            Color ink = refused ? _palette.HudWarn : _palette.HudAccent;
            float fade = refused ? RefusalFadeOf(_world.TickCount - refusal.Tick) : NoticeFadeOf();

            _refusalAlpha = refused ? fade : 0f;
            _toast.Group.alpha = fade;
            _toast.Back.Set(_palette.HudPanel);
            _toast.Frame.Set(ink);
            _toast.Label.Set(text, ink);

            // Laid out when the sentence changes rather than every frame: its width comes from a measured preferred
            // height, which is this method's expensive call. It also moves with the task card above it.
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
            }
        }

        /// <summary>Say one sentence in the strip for a few seconds; a refusal outranks it.</summary>
        private void Notice(string text, float seconds)
        {
            _noticeText = text;
            _noticeFrom = Time.unscaledTime;
            _noticeFor = seconds;
        }

        /// <summary>The answer to a hotkey the lesson is keeping dead. Called by the driver when the input latch refuses
        /// a digit: silence reads as a broken key, and the missing tile is the lesson's own doing.</summary>
        public void NoticeLocked(BuildKind kind)
        {
            if (_world == null || !_world.Content.TryMachine(kind, out MachineDef def)) return;

            Notice("NOT YET - the " + def.Name + " is the lesson's to hand over", NoticeSeconds);
        }

        /// <summary>Is a notice still inside its window?</summary>
        private bool NoticeLive => _noticeText != null && Time.unscaledTime - _noticeFrom < _noticeFor;

        /// <summary>Full strength for most of the notice's life, then out: the sentence was not asked for.</summary>
        private float NoticeFadeOf()
        {
            float elapsed = Time.unscaledTime - _noticeFrom;
            float holding = _noticeFor * (1f - NoticeFade);
            return elapsed <= holding ? 1f : Mathf.Clamp01((_noticeFor - elapsed) / (_noticeFor * NoticeFade));
        }

        private bool TryLastRefusal(out SimEvent refusal)
            => _world.Events.TryLastWithin(SimEventKind.PlacementRejected, _world.TickCount - RefusalTicks, out refusal);

        private static float RefusalFadeOf(int age)
        {
            float holding = RefusalTicks * (1f - RefusalFade);
            return age <= holding ? 1f : Mathf.Clamp01((RefusalTicks - age) / (RefusalTicks * RefusalFade));
        }

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
                        : "ON ORE - only a belt may cross a patch, and it mines it";
                case RejectionReason.Doorway:
                    return "A DOORWAY - waves walk in at " + refusal.Cell +
                           ", and nothing solid may stand there";
                case RejectionReason.Locked:
                    // The bar does not carry it at all, so this is the keyboard's answer to a key the bar no
                    // longer offers - a map's operator set, or a tutorial line still to be taught.
                    return "NOT ON THIS MAP - " + def.Name + " is an operator this run has not unlocked";
                default:
                    return "OFF THE MAP - nothing can be built on " + refusal.Cell;
            }
        }

        // ------------------------------------------------------------------ stockpile

        /// <summary>
        /// Keep every money mark on the HUD on the run's own coin. Both are repainted from here rather than at
        /// their own call sites, so a map change cannot leave the previous map's mineral on one of them.
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

        /// <summary>Point one mark at one rasterised item. An icon the palette cannot draw carries no texture, and a
        /// <see cref="RawImage"/> with none draws nothing rather than a white square.</summary>
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
            if (_refusalAlpha > 0f)
                numberColour = WithAlpha(_palette.HudWarn, _refusalAlpha);

            int shapes = _world.Economy.Shapes;
            if (shapes != _stockpileCount)
            {
                _stockpileCount = shapes;
                _stockpile.Number.Set(shapes.ToString(), numberColour);

                // The mark stands to the left of the number, so it never has to move as the number grows.
                if (_stockpile.Icon != null)
                    _stockpile.Icon.gameObject.SetActive(_stockpile.Icon.texture != null);
            }
            else _stockpile.Number.SetColour(numberColour);

            // The texture carries the shape's colour; the tint carries only the fade, so a fading number fades
            // its shape with it.
            if (_stockpile.Icon != null && !Mathf.Approximately(_stockpileAlpha, numberColour.a))
            {
                _stockpileAlpha = numberColour.a;
                Color tint = _stockpile.Icon.color;
                tint.a = numberColour.a;
                _stockpile.Icon.color = tint;
            }
        }

        // ------------------------------------------------------------------ status

        /// <summary>The status card, in the order the questions come, rebuilt when the world ticks: a paused run holds
        /// what it said. The diagnostics are read before the card is drawn, because its one warning colour depends on
        /// them.</summary>
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

            DrawHealthBar();
            DrawWaveClock();

            _status.News.Set(WaveNews(), _palette.HudText);
            UpdateComposition();

            string production = ProductionLine(jams, blocked, stalled, starved, total);
            bool productionNews = production != null;
            Color productionColour = productionNews ? ProductionColour(jams, blocked, stalled, starved) : _palette.HudText;

            // A quiet production line is the briefing's to use: say under the chips why this wave's figure is
            // two colours. A stopped line still wins the slot.
            if (!productionNews && (production = MixedDemandNote()) != null)
            {
                productionNews = true;
                productionColour = _palette.HudAccent;
            }

            _status.Production.Show(productionNews);
            if (productionNews) _status.Production.Set(production, productionColour);

            string threat = ThreatWarning(in outOfView);
            bool threatNews = threat != null;
            _status.Threat.Show(threatNews);
            if (threatNews) _status.Threat.Set(threat, ThreatColour(in outOfView));

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
        /// The production line, said only when something is wrong, and silent when the belts are clear - "all
        /// clear" printed every second is a line nobody reads. Null means no news.
        ///
        /// Jammed and blocked are the two ways a <em>belt</em> stops: a jam is an item a machine refused, a block is
        /// an item with nowhere to go at all. A stalled machine is the third: it has eaten its item and has no outlet
        /// for it. A <em>full</em> outlet is not that - a line loaded behind a full consumer is doing its job.
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
                // Only a missing outlet is said here: a full one is a loaded line, and a gun waiting for the
                // next wave is not a fault the player can go and fix.
                return stalled + " machine" + (stalled == 1 ? "" : "s") +
                       " stalled - an outlet is missing   ·   click one";

            if (starved)
                // The chamber is universal, so a starved gun is missing every shape, not the right one.
                return "a starved gun - an empty chamber, nothing waiting on its lines";

            WaveDirector waves = _world.Waves;
            if (total == 0 && (waves.Phase == WavePhase.Running ||
                    (waves.Phase == WavePhase.Intermission && waves.CurrentWave > 0)))
                return "no turrets standing - the wave walks in unopposed";

            return null;
        }

        private Color ProductionColour(int jams, int blocked, int stalled, bool starved)
            => jams > 0 || blocked > 0 || stalled > 0 || starved ? _palette.HudWarn : _palette.HudText;

        /// <summary>What is out of view, said in the register the arrows cannot. Both are drawn from the same
        /// <see cref="ThreatRadar.Summary"/>, so a card claiming the map is quiet while an arrow is live is not possible.</summary>
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

        private Color ThreatColour(in ThreatRadar.Summary outOfView)
            => outOfView.Urgent ? _palette.HudWarn : _palette.HudText;

        /// <summary>
        /// The bill of materials as chips, in two rows: the wave being fought and the wave after it, since "what do
        /// I need" is asked about both while there is still time to build a line for the second.
        ///
        /// Per <em>group</em>, because the demand is the group's own (<see cref="SpawnGroup.Weakness"/>), not the
        /// archetype's - which is what makes the same enemy a different puzzle each wave. Laid out by measurement, once
        /// per change; a wave too varied to fit drops the names and keeps the numbers and figures, both rows together.
        /// </summary>
        private void UpdateComposition()
        {
            WaveDirector waves = _world.Waves;

            // NextWave is the number a reader prints in every phase; both definitions are null once the run is out
            // of waves, which is the whole of "there is nothing left to brief".
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

        private string MixedDemandNote()
        {
            if (_world.Status != GameStatus.Playing) return null;

            WaveDefinition next = _world.Waves.NextWaveDefinition;
            if (next == null) return null;

            for (int g = 0; g < next.Groups.Length; g++)
            {
                string note = MachineReading.DemandNote(next.Groups[g].Weakness);
                if (note != null) return note;
            }

            return null;
        }

        /// <summary>One wave's groups as chip entries, reused per frame so a briefing change allocates nothing.</summary>
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

        /// <summary>Lay one briefing row left to right, after its own label. False when it does not fit: a name dropped
        /// is a courtesy lost, while a chip clipped is a count the player cannot read.</summary>
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
                // The figure is the demand, so it gets the chip's full height rather than whatever room the words leave.
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

            // Full Core reads as paper, the brightest ink in the rail: only damage turns it warning-coloured.
            _status.Health.Set(fraction, fraction > 0.4f ? HudInk.Paper(_palette) : _palette.HudWarn);

            // The metre's track carries the hit for the window the Core was last chewed, the same window the
            // screen's danger frame fades over.
            _status.HealthTrack.Set(hit
                ? WithAlpha(_palette.HudWarn, 0.16f + 0.30f * life)
                : HudInk.Raised(_palette));

            // One number: what is left, and - while a hit is fresh - what the last one cost.
            string value = Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp);
            if (hit) value += "   -" + DamageText(damage) + " hp";
            _status.HealthValue.Set(value, hit
                ? WithAlpha(_palette.HudWarn, 0.45f + 0.55f * life)
                : HudInk.Muted(_palette));
        }

        private static string DamageText(float damage)
            => damage == Mathf.RoundToInt(damage)
                ? Mathf.RoundToInt(damage).ToString()
                : damage.ToString("0.0");

        /// <summary>The wave clock as a length: how much of the countdown is spent, or how much of the running wave
        /// is killed. One bar, two readings. <see cref="WavePhase.Held"/> reads as an empty bar, which is the truth -
        /// the opening waits for the player's first building, not for time.</summary>
        private void DrawWaveClock()
        {
            float fraction = WaveClockFraction();

            // One hue however the clock reads: a wave being spent is good news, and HudInk.Good is HudAccent by design.
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
                    // Guarded because the fraction divides by it: a 0-second intermission starts its wave on the
                    // same tick, so there is no wait to have spent any of.
                    if (waves.IntermissionTotal <= 0f) return 1f;
                    return Mathf.Clamp01(1f - waves.IntermissionRemaining / waves.IntermissionTotal);

                case WavePhase.Running:
                    // Definition, not the map's authored table: past the table the director generates waves, and
                    // indexing the authored array with a generated wave's number is how this used to throw.
                    WaveDefinition current = waves.Definition(waves.CurrentWave);
                    if (current == null || current.Total <= 0) return 1f;
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return Mathf.Clamp01(1f - left / (float)current.Total);

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its clock bar");
            }
        }

        /// <summary>The title line's left end: the map in the campaign, and the endless word when the last map
        /// re-enters as an endless run.</summary>
        private string MapLine()
        {
            string at = _campaign != null && _campaign.MapCount > 1
                ? (_campaign.MapIndex + 1) + "/" + _campaign.MapCount + "  ·  "
                : string.Empty;

            return at + _world.Map.Name + (_world.Waves.Endless ? "  ·  endless" : string.Empty);
        }

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

        private string SeedLine() => "seed " + _world.Waves.Seed;

        private string WaveNews()
        {
            if (_world.Status != GameStatus.Playing)
            {
                // Won with no map after this one, the next press of N is the endless entry; a loss, or a win
                // with a map still ahead, has no N to offer.
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
                    // the lesson's, so neither building nor N begins it - saying otherwise would contradict the
                    // task card beside it. On an ordinary map only the opening is held.
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

        /// <summary>The cluster's one moving part is the pause button's word. H and T are read by the HUD itself -
        /// a view concern, the way the camera rig owns C. Nothing here is simulation state.</summary>
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

        private void SetTutorialShown(bool shown) => _tutorialChoice = shown;

        /// <summary>The tile under the pointer, pushed by the driver every frame: the tooltip reads what the click
        /// would read, and the driver is the one place that already knows the answer.</summary>
        public void SetCursor(Int2 cell) => _cursor = cell;

        /// <summary>The keyboard, one pair of ideas per line. Every line prints whole at the smallest scale, and none
        /// uses a character a built player's font might lack.</summary>
        public static string[] KeyLines()
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

                // The object test is kept because a hierarchy torn down under a pointer can leave a flag set.
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

                // The freshly handed-over tile's outline dissolves from the everyday hairline; the player's own
                // marks - selected or hovered - end it early, because a tile in hand has been seen.
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

                // The lesson's ring is weight, not shade: the selected outline and this ring must not be two
                // brightnesses of one line, or "the line wants this key" reads as "this key is selected". The
                // player's own marks still win.
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
                    // DressBuildTile put the keycap and the digit back when this state changed; only the ring is
                    // this block's to clear.
                    tile.Cue.Show(false);
                }

                if (!tile.Affordable && _world.Content.Machine(tile.Kind).Cost > 0)
                    tile.Afford.Set(Mathf.Clamp01(_world.Economy.Shapes / (float)_world.Content.Machine(tile.Kind).Cost),
                        _palette.CursorNoFunds);
                else tile.Afford.Set(0f, Color.clear);
            }

            // The cost's shape needs the number's measured width, so the first frames place it as they learn it.
            for (int i = 0; i < _bar.Tiles.Count; i++) PlaceCostIcon(_bar.Tiles[i]);
        }

        /// <summary>Does the current lesson line's blueprint use this building? A line with no blueprint highlights
        /// nothing, and a finished lesson highlights nothing even on its last frame.</summary>
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
        /// One building as a button: hotkey, cost, name and role. A click selects the kind through the input latch -
        /// it never places anything, so a stray click can only change what is selected. A kind the run has not handed
        /// over is not on the bar at all, so there is no locked state to dress.
        /// </summary>
        private void DressBuildTile(BuildTile tile)
        {
            // The tile wears one ink level plus the one focus colour: roles are words on the card, not hues on the keys.
            Color fill = HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.11f);
            if (tile.Selected) fill = HudInk.Blend(_palette.HudPanel, _palette.HudAccent, 0.22f);
            else if (tile.Hovered) fill = HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.19f);
            else if (!tile.Affordable) fill = HudInk.Blend(_palette.HudPanel, _palette.CursorNoFunds, 0.08f);
            tile.Button.Fill.Set(fill);

            tile.Keycap.Set(tile.Selected
                ? HudInk.Blend(_palette.HudPanel, _palette.HudAccent, 0.40f)
                : HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.18f));

            // Every tile keeps a hairline, so eleven surfaces read as eleven keys rather than one dark strip.
            tile.Border.Show(true);
            tile.Border.Set(tile.Selected
                ? _palette.HudAccent
                : tile.Hovered
                    ? HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.38f)
                    : HudInk.Blend(_palette.HudPanel, _palette.HudText, 0.18f));

            tile.Hotkey.SetColour(tile.Selected ? _palette.HudText : HudInk.Muted(_palette));

            // The cost turns amber when the answer is no - the same colour the cursor ghost and the resource band
            // use for the same fact.
            tile.Cost.SetColour(tile.Affordable
                ? (tile.Selected ? _palette.HudText : HudInk.Muted(_palette))
                : _palette.CursorNoFunds);

            tile.Name.SetColour(tile.Affordable ? _palette.HudText : HudInk.Muted(_palette));
        }

        /// <summary>The bar's one way to change what is selected, and the one place a locked kind is refused: the
        /// tile's click and the digit key are the same door, so the rule has one statement.</summary>
        private void SelectKind(BuildKind kind)
        {
            if (_world == null || !_world.IsUnlocked(kind)) return;

            _selectKind?.Invoke(kind);
        }

        // ------------------------------------------------------------------ the bar's keys

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

        /// <summary>What the hovered, or with nothing hovered the selected, building does. With neither, the card
        /// describes what the player <em>read</em> instead - see <see cref="RefreshInspect"/>.</summary>
        private void UpdateInfo()
        {
            // The hover index is a position on the bar, not in the content table: a lesson hides buildings, so
            // the two arrays stop agreeing the moment one is hidden. Read the tile's own kind.
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

                // The facing is the ghost's and Q/E move it: the one line on the build card that answers to the
                // keyboard. A cutter names the cut instead of the arrow, and it flips as the ghost turns.
                int facing = (int)_world.PlacementDirection;
                if (facing != _infoFacing)
                {
                    _infoFacing = facing;
                    _info.Facing.Set(GhostFacing(kind), HudInk.Muted(_palette));
                }

                // The READ button follows the selection every frame rather than the card's words: hovering the
                // building already selected changes nothing on the card, and a button written only when the
                // words changed would stay hidden in exactly that case.
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

            // The build face has no read rows, so it is always the base card, whatever the rows under it last
            // measured.
            _infoExtra = 0f;
            PlaceInfoCard();

            MachineDef def = _world.Content.Machine(kind);
            bool locked = !_world.IsUnlocked(kind);

            // The title says whether the building is placeable (live) or still locked (muted), not which role
            // family it belongs to.
            _info.BuildTitle.Set(def.Name, locked ? HudInk.Muted(_palette) : _palette.HudText);
            _info.BuildRight.Set(
                def.Cost + " " + Plural(Shape.MineralName(_world.Map.CurrencyMineral), def.Cost) + "   ·   " +
                (locked ? "LOCKED" : RoleOf(kind)),
                HudInk.Muted(_palette));

            // A locked kind is described like any other, with the one thing the player needs said first.
            _info.Blurb.Set(locked
                ? "not unlocked on this map · " + Blurb(kind)
                : Blurb(kind), _palette.HudText);

            string stats = Stats(kind);
            bool hasStats = !string.IsNullOrEmpty(stats);
            _info.Stats.Show(hasStats);
            if (hasStats) _info.Stats.Set(stats, HudInk.Muted(_palette));

            // The card's height is fixed, so a building with no stats leaves a gap rather than moving the
            // buttons under the pointer. All five places are the asset's.
            _info.Facing.Set(GhostFacing(kind), HudInk.Muted(_palette));
        }

        /// <summary>The build card's facing line for the placement it describes, from the same note the built card
        /// prints.</summary>
        private string GhostFacing(BuildKind? kind)
        {
            if (!kind.HasValue) return string.Empty;

            return MachineReading.GhostFacingNote(_world.Content.Machine(kind.Value),
                _world.PlacementDirection);
        }

        /// <summary>The card with nothing selected: the building's <em>state</em> - its ports, what it is doing this
        /// second, whether it is starved or jammed - from the same state the tick runs on. The blurb and the cost are
        /// the catalogue's business and left out. Nothing pinned is not an empty card: it says what the state is for
        /// and how to leave it.</summary>
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

            // A sorter's card grows: its filter is read from the machine's snapshot - not the definition row -
            // because a re-routed sorter is the machine that is routing.
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
                    // A jam arrives with the item that caused it on the belt: a jam is always "this machine
                    // refuses this round", and the round is the half of it the player can act on.
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

            // The ground is read off the terrain layer rather than the tile kind, so an ore card has a mineral and
            // a rock card its kind. Whether the cell is worth reading at all is SimWorld.CanInspect's answer - the
            // same rule the click and the cursor's marker read - not a copy restated here.
            if (_world.CanInspect(cell))
            {
                DrawGroundCard(cell, _world.Map.SpawnIndexOf(cell));
                FinishInspect();
                return;
            }

            InspectHeader("Inspect", _palette.HudAccent, "nothing selected");

            _rowDrop = InspectEmptyDrop;
            InspectLine("click a building, vein or doorway to read it", _palette.HudText);
            InspectLine("or pick a tile to place it", HudInk.Muted(_palette));
            FinishInspect();
        }

        /// <summary>Grow or shrink the info card's picker band, and put the rows back where the grown card wants them.
        /// The card is bottom-anchored, so the rows move down by the band's height.</summary>
        private void SetPickerUp(bool up)
        {
            if (_pickerUp == up) return;

            _pickerUp = up;
            _info.Picker.Root.gameObject.SetActive(up);

            if (_info.InspectRoot.gameObject.activeSelf)
            {
                StackInspectRows(_rowDrop);
            }
            else
            {
                _infoExtra = 0f;
                PlaceInfoCard();
            }
        }

        /// <summary>
        /// Lay the read card's rows out from the asset's first line, measuring each at the width the card gives it: a
        /// sentence longer than the row wraps and the row is one whole step taller. The card grows upward by whatever
        /// the stack needs past its body, so a long sentence never prints past its bottom edge.
        ///
        /// <paramref name="drop"/> moves the whole stack down inside the card: 0 for a machine, the picker band's
        /// height while the band is up (the card grows by the same amount, so the rows stay put), or
        /// <see cref="InspectEmptyDrop"/> for the empty card.
        /// </summary>
        private void StackInspectRows(float drop)
        {
            float band = _pickerUp ? HudLayout.InfoPickHeight : 0f;
            float top = InfoRowTop + band + drop;
            float bottom = 0f;

            for (int i = 0; i < _info.Rows.Length; i++)
            {
                HudText row = _info.Rows[i];
                if (!row.Text.gameObject.activeSelf || string.IsNullOrEmpty(row.Text.text))
                {
                    row.Rect.anchoredPosition = new Vector2(row.Rect.anchoredPosition.x,
                        -(InfoRowTop + band + drop + i * InfoRowStep) * _scale);
                    row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, InfoRowHeight * _scale);
                    continue;
                }

                float measured = row.Text.GetPreferredValues(row.Text.text, row.Rect.rect.width, 0f).y / _scale;
                int lines = Mathf.Max(1,
                    1 + Mathf.CeilToInt((measured - InfoRowHeight - 0.01f) / InfoRowStep));
                float height = InfoRowHeight + (lines - 1) * InfoRowStep;

                row.Rect.anchoredPosition = new Vector2(row.Rect.anchoredPosition.x, -top * _scale);
                row.Rect.sizeDelta = new Vector2(row.Rect.sizeDelta.x, height * _scale);

                bottom = top + height;
                top = bottom + (InfoRowStep - InfoRowHeight);
            }

            float body = HudLayout.InfoHeight + band - InfoFoot;
            _infoExtra = Mathf.Max(0f, bottom - body);
            PlaceInfoCard();
        }

        private void PlaceInfoCard()
            => Place(_info.Root,
                HudLayout.Info(Screen.width, Screen.height, _scale, _pickerUp, _infoExtra),
                PlaceCorner.BottomRight);

        /// <summary>How much lower the empty card's two lines sit than the first row's line, in design units: a
        /// message pinned to the header reads as a clipped card, there being nothing under it.</summary>
        private const float InspectEmptyDrop = 25f;

        /// <summary>
        /// The read card's row grid in design units: the first row's line below the card's top, the height of a
        /// one-line row, the pitch a wrapped line adds, and the foot. <c>HudInfo.prefab</c> carries exactly these, and
        /// <see cref="StackInspectRows"/> turns them into a measured stack.
        /// </summary>
        private const float InfoRowTop = 33f;
        private const float InfoRowHeight = 14f;
        private const float InfoRowStep = 15f;
        private const float InfoFoot = 12f;

        /// <summary>
        /// Paint the picker from the machine's own filter. The working state is re-seeded whenever the machine's
        /// filter is not the one it was seeded from, so a filter changed by an earlier click is read back rather than
        /// painted over - the card always shows what routes.
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

            // The caption is what a click would send; while the canvas is empty it says what the machine routes
            // now. A card built without a way to send a rule - the honest read-only one - does not invite the click.
            string caption = PickerCaption();
            Color captionColour = HudInk.Muted(_palette);
            if (!TryPendingFilter(out _))
            {
                if (_setFilter == null)
                {
                    caption = "routing " + machine.Filter.Describe() + "   ·   read-only";
                    captionColour = HudInk.Muted(_palette);
                }
                else
                {
                    caption = machine.Filter.IsNone
                        ? caption
                        : "routing " + machine.Filter.Describe() + "   ·   click to change";
                    captionColour = machine.Filter.IsNone ? _palette.HudWarn : HudInk.Muted(_palette);
                }
            }

            _info.Picker.Caption.Set(caption, captionColour);
        }

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
                    // "Routes everything" and "routes nothing" have no quadrants to paint; the mineral is seeded
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

        /// <summary>The item the canvas is showing: in mineral mode the whole of the mineral, since the rule is about
        /// the mineral.</summary>
        private Shape PaintedFilter() => _filterMineral ? Shape.Full(_filterMineralPicked) : _filterShape;

        /// <summary>What a click would send, or false when it would send nothing: a rule that routes nothing is never
        /// sent, since <see cref="ShapeFilter.None"/> is refused by the world.</summary>
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

        private string PickerCaption()
            => TryPendingFilter(out ShapeFilter filter) ? filter.Describe() : "click a quadrant to route on";

        /// <summary>
        /// One quadrant clicked. In exact mode it cycles that quadrant's own mineral - which way up an item is
        /// matters, and a top half-circle is not a bottom one, so the canvas is a real item. In mineral mode it cycles
        /// the mineral, which is the only thing that rule is about.
        /// </summary>
        private void PressFilterQuadrant(int quadrant)
        {
            if (!_pickerUp) return;

            if (_filterMineral)
            {
                // In mineral mode the cycle has two stops: a mineral is never nothing, and a blank canvas would be
                // a rule the player cannot send.
                _filterMineralPicked = _filterMineralPicked == Quad.Circle ? Quad.Square : Quad.Circle;
            }
            else
            {
                _filterShape = WithQuadrant(_filterShape, quadrant, NextMineral(_filterShape.At(quadrant)));
            }

            SendFilter();
        }

        /// <summary>Switch between the two rules the picker states; neither switch is a dead end, so the toggle always
        /// sends a rule the world accepts. Guarded like the quadrant press beside it: both are the picker's own buttons.</summary>
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

        /// <summary>One quadrant of a painted item replaced, the other three kept: the canvas is an item, so painting
        /// one quadrant must not disturb its neighbours.</summary>
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

        private static Quad FirstMineral(Shape shape)
        {
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                if (shape.Has(quadrant)) return shape.At(quadrant);

            return Quad.Empty;
        }

        /// <summary>Same routing rule? <see cref="ShapeFilter"/> is a plain struct with no equality of its own, and
        /// its unused halves are not part of its identity.</summary>
        private static bool SameFilter(in ShapeFilter left, in ShapeFilter right)
            => left.Mode == right.Mode && left.Exact == right.Exact && left.Mineral == right.Mineral;

        /// <summary>The header grammar every card shares: a title left, the secondary words right, a hairline under
        /// both. When the header is about ground rather than a building, the title carries the ground's hue.</summary>
        private void InspectHeader(string name, Color accent, string right)
        {
            Color title = accent == _palette.HudAccent
                ? _palette.HudText
                : HudInk.Blend(accent, _palette.HudText, 0.35f);

            _info.InspectTitle.Set(name, title);
            _info.InspectRight.Set(right, HudInk.Muted(_palette));
            _rowDrop = 0f;
        }

        /// <summary>The ports line at the width the card can print: MachineReading's sentence explains the whole wiring
        /// rule, which is three lines too long for the row. The short form keeps the one fact a reader needs.</summary>
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

            StackInspectRows(_rowDrop);
        }

        /// <summary>The colour a live note is said in - which colour means "look" is the palette's question, what the
        /// note means is MachineReading's. An unknown severity throws: it is an error, not a quiet fallback.</summary>
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

        /// <summary>The ground: the map's obstacle, a vein of ore, an entry point the waves walk in at - or two of
        /// them at once, since a map may open a door over ore. The card says what is there rather than choosing.</summary>
        private void DrawGroundCard(Int2 cell, int doorway)
        {
            TerrainKind kind = _world.Terrain.KindAt(cell);

            // The map's own obstacle comes first: a player looking at rock is asking "is this mine or the map's?".
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

            InspectLine("mines as " + Shape.Full(mineral).Describe() + " · lay a belt on it to mine", oreColour);

            if (TryVeinAt(cell, out ShapePatch vein))
                InspectLine(vein.Width + " x " + vein.Height + " vein in the ground", HudInk.Muted(_palette));
        }

        /// <summary>A tile of ground the map draws as an obstacle, in the same colours the terrain view draws it in, so
        /// the card and the map agree about which tile is described. The note's colour is the terrain's, lifted toward
        /// the card's text, because a dark line on a dark panel is a line nobody can read.</summary>
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

        /// <summary>Which waves enter the map at this door, read off the map's own wave table; a door no wave uses is
        /// said out loud instead of left blank.</summary>
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

        /// <summary>The vein rectangle covering this cell, from the map's own patch table - the ground the map laid, not
        /// a guess. False for ore a test stamped, which has no rectangle.</summary>
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

        /// <summary>The tile under the pointer, said beside the pointer: the same state the pinned inspect card reads,
        /// condensed to a glance. A plain tile of ground says nothing.</summary>
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

                // The cell's answer is kept with the cell, so plain ground stays silent between ticks too.
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
                TooltipLine("mines as " + Shape.Full(mineral).Describe() + " - a belt laid on it mines it",
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
            // Counted before the rest are put away: a box sized to the pool leaves a blank line under every
            // two-line answer.
            int printed = _tooltipRows;

            for (; _tooltipRows < _tooltip.Rows.Length; _tooltipRows++)
                _tooltip.Rows[_tooltipRows].Show(false);

            float width = _tooltip.Title.PreferredWidth;
            for (int i = 0; i < printed; i++)
                width = Mathf.Max(width, _tooltip.Rows[i].PreferredWidth);

            _tooltipWidth = Mathf.Min(width + 20f * _scale, Screen.width - 16f);
            _tooltipHeight = (20f + 14f * printed + 6f) * _scale;
        }

        private void PositionTooltip(Vector2 pointer)
        {
            const float Edge = 8f;

            float x = pointer.x + 16f;
            if (x + _tooltipWidth > Screen.width - Edge) x = pointer.x - _tooltipWidth - 16f;
            x = Mathf.Clamp(x, Edge, Mathf.Max(Edge, Screen.width - Edge - _tooltipWidth));

            float fromTop = Screen.height - pointer.y - 16f;
            if (fromTop < Edge) fromTop = Screen.height - pointer.y + 24f;   // near the top: below the pointer
            if (fromTop + _tooltipHeight > Screen.height - Edge)            // near the bottom: above it
                fromTop = Screen.height - pointer.y - _tooltipHeight - 16f;
            fromTop = Mathf.Clamp(fromTop, Edge, Mathf.Max(Edge, Screen.height - Edge - _tooltipHeight));

            HudUi.TopLeft(_tooltip.Root, x, fromTop, _tooltipWidth, _tooltipHeight);
        }

        // ------------------------------------------------------------------ banner

        /// <summary>The one thing that has to be unmissable: the run is over, or the game is paused.</summary>
        private void UpdateBanner()
        {
            // The banner's gate: a paused world does not tick, so a paused banner holds for free; an ended run
            // may still tick the last coins into its stats line.
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
                    // A test may not wire a catalog: a HUD with no campaign says the map was cleared but cannot
                    // promise what is next. The index is defended once and goes to both calls, so the banner and
                    // the guard cannot disagree.
                    int index = _campaign != null ? _campaign.MapIndex : 0;
                    if (_catalog != null && _catalog.HasNext(index))
                    {
                        MapDefinition next = _catalog.NextAfter(index);
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               "\npress N for the next map: " + next.Name;
                    }
                    else
                    {
                        // No map after this one, and pressing N is the "go on" verb in every state: here it
                        // is the endless entry, the same map with the generator unbounded.
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

        private string StatsLine()
        {
            return _world.ShotsFired + " shots fired · " + _world.Economy.TotalBanked + " " +
                Plural(Shape.MineralName(_world.Map.CurrencyMineral), _world.Economy.TotalBanked) + " banked";
        }

        // ------------------------------------------------------------------ the buttons' dressing

        /// <summary>The buttons whose look is the generic one. The world never sees the click because
        /// <see cref="PointerOverHud"/> claimed it for the UI.</summary>
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

            // The picker's quarters keep a clear fill so the item underneath is the content; their border is
            // what says the cell is clickable, which is why every quadrant wears one, empty or not.
            if (_info.Picker.Root.gameObject.activeSelf)
            {
                Dress(_info.Picker.Mode, ButtonFill);
                for (int i = 0; i < _info.Picker.Quadrants.Length; i++)
                    Dress(_info.Picker.Quadrants[i], Color.clear);
            }

            // The start-wave button breathes while it waits; a paused world holds its breath.
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

        /// <summary>A control with a surface: a raised fill and a hairline that answers the pointer.</summary>
        private void Dress(HudButton button, Color fill)
        {
            bool hovered = button.Hovered;
            button.Fill.Set(hovered ? HudInk.Blend(fill, _palette.HudText, 0.14f) : fill);
            button.Border.Set(hovered ? _palette.HudAccent : PanelBorder());
        }

        /// <summary>A control without a surface: one step off the card, no edge until the pointer arrives.</summary>
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

        private int GearNow => _clock != null ? _clock.Gear : 1;

        /// <summary>The status card's footer, right end: the gear and the keys that change it.</summary>
        private string SpeedFooter()
            => _world.Paused ? "paused  ·  space resumes" : GearWord() + "  ·  [ ] speed";

        /// <summary>The gear button's word; the multiplication sign, not an x, because at 10 points the letter
        /// reads as a letter.</summary>
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

        // The catalogue's words and numbers live in MachineReading - the one behaviour dispatch the HUD merely
        // formats. What stays here is the palette question: which colour says which role.

        private string RoleOf(BuildKind kind) =>
            MachineReading.RoleLabel(MachineReading.Role(_world.Content.Machine(kind)));

        private string Blurb(BuildKind kind) =>
            MachineReading.Blurb(_world.Content.Machine(kind), _world.Content);

        private string Stats(BuildKind kind) =>
            MachineReading.Stats(_world.Content.Machine(kind), _world.Content, _world.Config);

        /// <summary>The item's own words, pluralised the way the sentence reading it needs.</summary>
        private static string Plural(string word, int count)
            => count == 1 ? word : word + "s";

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

        /// <summary>The buildings this run offers, in the table's (hotkey) order, filtered by
        /// <see cref="SimWorld.IsUnlocked"/> - the same rule the hotkeys and the click obey, so a hidden tile is a
        /// dead key. The table's own array comes back when nothing is hidden, so the ordinary path allocates nothing.</summary>
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

        /// <summary>Does the bar's row of tiles still match the buildings this run offers? A map's set is fixed for the
        /// run but a tutorial's grows with its lines, so this is asked every frame. Walking the table costs no array.</summary>
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
            public HudText Title;
            public HudText[] Rows;
        }

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
            public HudText Title;
            public HudText Body;
        }

        /// <summary>The lesson's card: heading, position in the list, the lines as one rich-text label, and the pinned
        /// hint under them. No button - the card goes when the lesson does.</summary>
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

            /// <summary>The lesson's cue ring: weight, not shade, so "the line wants this key" cannot read as "this
            /// key is selected".</summary>
            public HudBorder Cue;

            public BuildKind Kind;
            public bool Selected;
            public bool Hovered;
            public bool Affordable;
            public bool CostPlaced;

            public bool Lesson;

            /// <summary>Unscaled time the accent outline fades at; 0 is no pulse. The player's own marks end it early.</summary>
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

        /// <summary>The most lines any inspect card asks of the pool: ports, live note, stats, health, facing, plus one.</summary>
        private const int InspectRowCapacity = 6;

        /// <summary>Enemy kinds laid out before giving up on overflow; more than this in one wave is a content-table
        /// question, not a layout one.</summary>
        private const int ChipCapacity = 6;

        private const int TooltipRowCapacity = 3;

        /// <summary>The chip rows' own copy of the briefings, reused every pass so a wave change allocates nothing.</summary>
        private readonly List<ChipEntry> _nextEntries = new List<ChipEntry>();
        private readonly List<ChipEntry> _afterEntries = new List<ChipEntry>();

        /// <summary>Where the two briefing rows sit, in design units from the card's top; the labels and the chips
        /// share the row.</summary>
        private const float ChipsTop = 82f;
        private const float ChipsAfterTop = 103f;

        /// <summary>A chip is the demand figure plus two units of air above and below: the figure sets the row's
        /// height, not the count beside it.</summary>
        private const float ChipIconPad = 2f;
        private const float ChipHeight = HudLayout.DemandFigure + ChipIconPad * 2f;

        private const float FooterTop = ChipsAfterTop + ChipHeight + 1f;

        /// <summary>The column the NOW/THEN labels own, so both chip rows start on the same vertical line.</summary>
        private const float ChipLabelWidth = 36f;

        /// <summary>The faint cell an empty quadrant wears in the picker, and nowhere else: on the map an empty
        /// quadrant is nothing, and a ghost there would draw an item the simulation does not have.</summary>
        private Color GhostCell => WithAlpha(_palette.HudText, 0.09f);
    }
}
