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
    /// The player's whole operating surface: a status card top-left (the map and its wave, the Core's
    /// health and the wave's clock as two bars, the wave's bill of materials as chips, and two diagnostic
    /// lines), the stockpile floating beside it as a shape and a number with no panel of its own, a
    /// controls cluster top-right (pause, speed gear and help, with the keyboard and restart behind the help card), a
    /// clickable build bar along the bottom, and an info card above it describing whichever building is
    /// hovered, selected - or, with nothing selected, read (see <see cref="RefreshInspect"/>).
    ///
    /// The card reports, in the order the questions come: is my bank holding, how long do I have, what is
    /// coming, can my defence stop it, and where am I not looking. The last is <see cref="ThreatRadar"/>'s
    /// answer - the same one the arrows on the map's edge are drawn from, so a sentence and a mark cannot
    /// disagree. A refused placement arrives on the event stream as <see cref="SimEventKind.PlacementRejected"/>
    /// and becomes a toast under the top cards; the stockpile's number turns amber while the selected
    /// building cannot be afforded and flashes warning-coloured while the toast is live.
    ///
    /// The build bar is the point of this view: every building is a real button carrying its hotkey, name,
    /// role and cost, and a tile is tinted when unaffordable, outlined when selected and highlighted under
    /// the pointer. Clicking one selects it exactly the way a number key does (through the input latch, so
    /// "the press decides what is built" is untouched), which is what lets the game be played without
    /// memorising the keyboard.
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

        /// <summary>Have the tabs' words been measured yet? They are laid out again every frame until uGUI
        /// has answered, which it does within the first frame or two.</summary>
        private bool _tabsMeasured;

        /// <summary>The selection the bar's open tab was last brought into line with, so a tab the player
        /// opened by hand survives the frames that follow.</summary>
        private BuildKind? _seenSelected;
        private InfoPanel _info;
        private StockpilePanel _stockpile;
        private ToastPanel _toast;
        private BannerPanel _banner;
        private TutorialPanel _tutorial;
        private TooltipPanel _tooltip;

        private Rect _stockpileRect;

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
        private int _toastTick = -1;
        private string _toastText;
        private float _toastAlpha;
        private bool _toastTutorialUp;

        private int _stockpileCount = int.MinValue;
        private float _stockpileAlpha = -1f;

        private bool _controlsDirty = true;
        private bool _controlsPaused;
        private int _controlsGear = -1;
        private bool _controlsBehind;

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
        private float _tooltipWidth;
        private float _tooltipHeight;

        private bool _helpOpen;
        private string _compKey;

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

            var view = _screen.Measure(_camera, ShowTutorial, look.EdgeInsetPixels, _pickerUp);

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
        /// is on it? The driver relays this to the locator, whose panel boxes are the same boxes the HUD
        /// draws - see <see cref="ThreatRadarView.SetInfoGrown"/>.</summary>
        public bool InfoGrown => _pickerUp;

        /// <summary>Is the "how to play" card up? The one predicate the card's claim on the pointer, the
        /// toast's clearance and the driver's camera rig all read, so no two pieces of the frame can disagree
        /// about whether the card is there. Up by default on the campaign's first map; once the player has
        /// hidden or shown it themselves, their choice wins on every map.</summary>
        private bool ShowTutorial => _campaign != null && (_tutorialChoice ?? _campaign.IsFirstMap);

        /// <summary>Is the "how to play" card on screen this frame? The driver relays it to the camera rig
        /// and the locator, whose bounds give up the strip the card wears - the way it relays
        /// <see cref="InfoGrown"/>.</summary>
        public bool TutorialUp => ShowTutorial;

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
            Action<Int2, ShapeFilter> setFilter = null, TickClock clock = null)
        {
            _world = world;
            if (clock != null) _clock = clock;
            _palette = palette;
            _campaign = campaign;
            _catalog = catalog;
            if (selectKind != null) _selectKind = selectKind;
            if (rotate != null) _rotate = rotate;
            if (setFilter != null) _setFilter = setFilter;

            if (_icons == null) _icons = new HudIcons(palette);

            if (_canvasRoot == null) BuildCanvas();

            if (!BarMatchesContent())
            {
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
            UpdateMoneyMarks();

            // The HUD owns its panels: the pointer being over one is reported to the input source, so a
            // click on a button cannot also build under it. This update runs after the driver's, so it
            // describes the pointer as of the previous frame - a frame of lag on a pointer that has to travel
            // to the button anyway. The stockpile readout and the toast are not panels: they are read, never
            // clicked, so a click goes through them to the map.
            PointerOverHud = ComputePointerOver();

            UpdateTutorial();
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

            // Immediate, because the rebuild happens inside the same update that noticed the resize:
            // a deferred Destroy would leave both HUDs on screen for a frame, and the panels are pure
            // runtime objects with nothing an asset could lose.
            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_canvasRoot.GetChild(i).gameObject);

            BuildPanels();
            MarkEverythingDirty();
        }

        private void BuildPanels()
        {
            // Fresh tabs are built at 0x0 and only positioned once uGUI's text measurement lands - which
            // only happens while _tabsMeasured is false. A rebuild that left it true (the fullscreen
            // resize path) would strand every tab unpositioned, their Overflow labels superimposed in
            // one unreadable pile. A new hierarchy always re-measures.
            _tabsMeasured = false;

            _status = BuildStatus();
            _tutorial = BuildTutorial();
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
            _toastTick = -1;
            _toastAlpha = 0f;
            _bannerText = null;
            _bannerShown = false;
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

        /// <summary>A card: the panel backdrop in the palette's panel colour, with the thin edge that keeps a
        /// dark card from bleeding into the map.</summary>
        private RectTransform BuildCard(string name, Rect rect, PlaceCorner corner)
        {
            RectTransform root = HudUi.Node(_canvasRoot, name);
            Place(root, rect, corner);

            HudBox back = HudUi.Box(root, name + ".back", _palette.HudPanel);
            HudUi.StretchFull(back.Rect);
            HudUi.Frame(root, name + ".frame", PanelBorder(), 1f * _scale);
            return root;
        }

        private Color PanelBorder()
            => new Color(_palette.HudText.r, _palette.HudText.g, _palette.HudText.b, 0.12f);

        private Color ButtonFill => Blend(_palette.HudPanel, _palette.HudText, 0.09f);

        /// <summary>A design-unit font size through the current scale: the fonts stay crisp because the scale
        /// is in the point size, and the layout keeps the same fraction of the screen because everything else
        /// goes through the same number.</summary>
        private int FontSize(float design) => Mathf.Max(8, Mathf.RoundToInt(design * _scale));

        private HudText Label(RectTransform parent, string name, float design, FontStyle style,
            TextAnchor anchor, bool wrap = false)
            => HudUi.Text(parent, name, FontSize(design), style, anchor, wrap);

        /// <summary>A card's accent stripe: full height whatever the card's own is, because the info card can
        /// grow while a sorter's picker is on it and a stripe built at the base height would stop short of the
        /// card it is drawn on. Placed by its own insets rather than through the corner helpers, which set
        /// coincident anchors: this one stretches down the card's height, and an inset is the one way to say
        /// "the card's full height, whatever that turns out to be" without also having to reason about where
        /// the pivot sits on a stretched axis.</summary>
        private void Stripe(HudBox stripe, float width)
        {
            stripe.Rect.anchorMin = Vector2.zero;
            stripe.Rect.anchorMax = Vector2.up;
            stripe.Rect.offsetMin = Vector2.zero;
            stripe.Rect.offsetMax = new Vector2(width, 0f);
        }

        private StatusPanel BuildStatus()
        {
            Rect rect = HudLayout.Status(_scale);
            var panel = new StatusPanel { Root = BuildCard("status", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            panel.Stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            Stripe(panel.Stripe, 3f * _scale);

            panel.Title = Label(root, "title", 15f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Title.Rect, 14f * _scale, 10f * _scale, rect.width - 28f * _scale, 22f * _scale);

            // The stockpile lives in the resource band beside the card, not as a line here: it is the
            // economy's one number and the thing a refusal is about, too important to be a card's third line.
            HudBox healthBack = HudUi.Box(root, "health.back", _palette.HudPanel);
            HudUi.TopLeft(healthBack.Rect, 14f * _scale, 38f * _scale, rect.width - 28f * _scale, 16f * _scale);
            panel.Health = HudUi.BarFill(healthBack.Rect, "fill", 0f, 0f, rect.width - 28f * _scale, 16f * _scale);
            panel.HealthFrame = HudUi.Frame(healthBack.Rect, "frame", _palette.HudText, 1f * _scale);
            panel.HealthLabel = Label(healthBack.Rect, "label", 11f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.HealthLabel.Rect, 6f * _scale, -1f * _scale, rect.width - 28f * _scale, 16f * _scale);
            panel.HealthDamage = Label(healthBack.Rect, "damage", 11f, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(panel.HealthDamage.Rect, 0f, -1f * _scale, rect.width - 34f * _scale, 16f * _scale);
            panel.HealthDamage.Show(false);

            HudBox clockBack = HudUi.Box(root, "clock.back", Blend(_palette.HudPanel, _palette.HudText, 0.14f));
            HudUi.TopLeft(clockBack.Rect, 14f * _scale, 60f * _scale, rect.width - 28f * _scale, 6f * _scale);
            panel.Clock = HudUi.BarFill(clockBack.Rect, "fill", 0f, 0f, rect.width - 28f * _scale, 6f * _scale);
            HudUi.Frame(clockBack.Rect, "frame", Blend(_palette.HudText, _palette.HudPanel, 0.5f), 1f * _scale);

            panel.News = Label(root, "news", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.News.Rect, 14f * _scale, 72f * _scale, rect.width - 28f * _scale, 19f * _scale);

            // The bill of materials as chips, in two rows: the wave the clock is counting to and the one
            // after it, because "what do I need" is asked about both while there is still time to build for
            // the second. Each chip is the count, the archetype and the round its group demands, drawn as a
            // quadrant figure - the character the old content table spelled a weakness with is not in every
            // font a player has.
            // Derived from the two rows' own constants rather than repeating their numbers, so moving a row
            // moves its label with it - the placement below and LayoutChips read the same two values.
            panel.NextLabel = Label(root, "waveNext", 11f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.NextLabel.Rect, 14f * _scale, (ChipsTop + 4f) * _scale, 80f * _scale,
                15f * _scale);
            panel.AfterLabel = Label(root, "waveAfter", 11f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.AfterLabel.Rect, 14f * _scale, (ChipsAfterTop + 4f) * _scale, 80f * _scale,
                15f * _scale);

            panel.Chips = BuildChips(root, "chip");
            panel.ChipsAfter = BuildChips(root, "after");

            // Two lines that only speak when something needs them: a jam or a starved gun, and what is out
            // of view. Quiet is the default and silence is the good news - a line that only says "all clear"
            // trains the player to stop reading.
            panel.Production = Label(root, "production", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Production.Rect, 14f * _scale, 145f * _scale, rect.width - 28f * _scale, 19f * _scale);
            panel.Production.Show(false);

            panel.Threat = Label(root, "threat", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Threat.Rect, 14f * _scale, 164f * _scale, rect.width - 28f * _scale, 19f * _scale);
            panel.Threat.Show(false);

            panel.WaveButton = HudUi.Button(root, "wave", 250f * _scale, 26f * _scale, string.Empty,
                FontSize(13f), _palette.HudPanel, PressWave);
            HudUi.TopLeft(panel.WaveButton.Root, 14f * _scale, 183f * _scale, 250f * _scale, 26f * _scale);
            panel.WaveButton.Root.gameObject.SetActive(false);

            // The run's seed, in the corner the button leaves beside it: on the title's line it pushed the
            // wave number off the card, and it is not what anyone reads the card for.
            panel.Seed = Label(root, "seed", 11f, FontStyle.Normal, TextAnchor.LowerRight);
            HudUi.TopLeft(panel.Seed.Rect, 272f * _scale, 186f * _scale, rect.width - 286f * _scale,
                20f * _scale);

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
                    Back = HudUi.Box(root, prefix + i + ".back", Blend(_palette.HudPanel, _palette.HudText, 0.07f)),
                    Count = Label(root, prefix + i + ".count", 12f, FontStyle.Bold, TextAnchor.UpperLeft),
                    Name = Label(root, prefix + i + ".name", 11f, FontStyle.Normal, TextAnchor.UpperLeft),
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

            HudBox stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            HudUi.TopLeft(stripe.Rect, 0f, 0f, 3f * _scale, rect.height);

            HudText title = Label(root, "title", 17f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(title.Rect, 14f * _scale, 10f * _scale, rect.width - 100f * _scale, 24f * _scale);
            title.Set("HOW TO PLAY", _palette.HudAccent);

            // The player's way out: the card is a first-map default, not a rule, and the key on the button
            // is the way back once it is gone (the help card names it too).
            panel.Hide = HudUi.Button(root, "hide", 70f * _scale, 22f * _scale, "hide  (T)", FontSize(11f),
                _palette.HudText, () => SetTutorialShown(false));
            HudUi.TopRight(panel.Hide.Root, 10f * _scale, 9f * _scale, 70f * _scale, 22f * _scale);

            // The card's one job is being read. Each step is one plain instruction with no hand-made line
            // breaks - wrapping is the text's job, and breaks written for one width are ragged at every other.
            // Seven lines at this size fill well under the body's room (HudLayout.TutorialHeight less the
            // footer), so the last step never runs off the bottom. Only what the first map unlocks is named:
            // the stacker is not on it.
            HudText body = Label(root, "body", 13f, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            HudUi.TopLeft(body.Rect, 14f * _scale, 40f * _scale, rect.width - 28f * _scale, 134f * _scale);
            body.Set(
                "1. Put a Drill (2) on ore and drag Belts (1) to the Core.\n" +
                "2. Each full shape that reaches the Core earns 1 coin.\n" +
                "3. Each wave is weak to the shape shown next to it above.\n" +
                "4. Make that shape: a Cutter (3) splits it in half, a Rotator (4) turns a piece. " +
                "Press Q/E to turn a machine before you place it.\n" +
                "5. Belt that shape into a Turret (0). The exact shape does full damage, anything else half.",
                _palette.HudText);

            HudText footer = Label(root, "footer", 11f, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            HudUi.TopLeft(footer.Rect, 14f * _scale, rect.height - 28f * _scale, rect.width - 28f * _scale,
                20f * _scale);
            footer.Set("Line stuck? Right-click the red belt, or end a spare lane in a Sink (X).",
                _palette.MachineIdle);

            panel.Root.gameObject.SetActive(ShowTutorial);
            return panel;
        }

        /// <summary>The controls cluster: pause and help, two small buttons in the screen's top-right
        /// corner. The keyboard list is reference material and lives behind the help button, in
        /// <see cref="BuildHelp"/>. Pause stays out here - pausing is a mid-play act - while restart stays
        /// inside the card, one deliberate click further from a misfire.</summary>
        private ControlsPanel BuildControls()
        {
            Rect rect = HudLayout.Controls(Screen.width, _scale);
            var panel = new ControlsPanel { Root = HudUi.Node(_canvasRoot, "controls") };
            Place(panel.Root, rect, PlaceCorner.TopRight);

            float side = HudLayout.ControlsHeight * _scale;
            panel.Pause = HudUi.Button(panel.Root, "pause", side, side,
                _world.Paused ? ">" : "II", FontSize(15f), _palette.HudText, PressPause);
            HudUi.TopLeft(panel.Pause.Root, 0f, 0f, side, side);

            panel.Gear = HudUi.Button(panel.Root, "gear", side, side, GearWord(), FontSize(12f), _palette.HudText,
                PressGear);
            HudUi.TopLeft(panel.Gear.Root, (HudLayout.ControlsHeight + HudLayout.ControlGap) * _scale, 0f,
                side, side);

            panel.Help = HudUi.Button(panel.Root, "help", side, side, "?", FontSize(15f), _palette.HudText,
                () => SetHelpOpen(!_helpOpen));
            HudUi.TopLeft(panel.Help.Root, (HudLayout.ControlsWidth - HudLayout.ControlsHeight) * _scale, 0f,
                side, side);

            return panel;
        }

        /// <summary>The help card: the keyboard, one pair of ideas per line, and the restart button - here
        /// rather than on the cluster because a misplaced click must never restart the run, and opening a card
        /// first guarantees deliberateness. It overlays the map while open, the way a toast does.</summary>
        private HelpPanel BuildHelp()
        {
            Rect rect = HudLayout.Help(Screen.width, _scale);
            var panel = new HelpPanel { Root = BuildCard("help", rect, PlaceCorner.TopRight) };
            RectTransform root = panel.Root;

            HudText header = Label(root, "header", 11f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(header.Rect, 12f * _scale, 9f * _scale, rect.width - 60f * _scale, 14f * _scale);
            header.Set("CONTROLS", _palette.HudAccent);

            float y = 29f;
            foreach (string key in KeyLines())
            {
                HudText line = Label(root, "key" + y, 11f, FontStyle.Normal, TextAnchor.UpperLeft);
                HudUi.TopLeft(line.Rect, 12f * _scale, y * _scale, rect.width - 24f * _scale, 14f * _scale);
                line.Set(key, _palette.MachineIdle);
                y += 15f;
            }

            panel.Restart = HudUi.Button(root, "restart", rect.width - 24f * _scale, 26f * _scale,
                "R  restart  run", FontSize(13f), _palette.HudText, PressRestart);
            // Under the last key line, so a line added to KeyLines pushes the button down with it.
            HudUi.TopLeft(panel.Restart.Root, 12f * _scale, (y + 5f) * _scale, rect.width - 24f * _scale, 26f * _scale);

            panel.Close = HudUi.Button(root, "close", 22f * _scale, 22f * _scale, "×", FontSize(13f),
                _palette.HudText, () => SetHelpOpen(false));
            HudUi.TopRight(panel.Close.Root, 8f * _scale, 7f * _scale, 22f * _scale, 22f * _scale);

            root.gameObject.SetActive(false);
            return panel;
        }

        private StockpilePanel BuildStockpile()
        {
            Rect rect = HudLayout.Stockpile(Screen.width, _scale);
            _stockpileRect = rect;

            var panel = new StockpilePanel { Root = HudUi.Node(_canvasRoot, "stockpile") };
            Place(panel.Root, rect, PlaceCorner.TopLeft);

            panel.Number = Label(panel.Root, "number", 26f, FontStyle.Bold, TextAnchor.MiddleLeft);
            HudUi.TopLeft(panel.Number.Rect, 0f, 0f, rect.width, rect.height);

            HudIcons.Icon coin = _icons.Mark(MoneyShape);
            panel.Icon = HudUi.Icon(panel.Root, "icon", coin);
            float side = Mathf.Min(28f * _scale, rect.height - 4f * _scale);
            HudUi.TopLeft(panel.Icon.rectTransform, rect.width, (rect.height - side) * 0.5f, side, side);
            panel.Icon.gameObject.SetActive(false);

            return panel;
        }

        private BarPanel BuildBar()
        {
            Rect rect = HudLayout.Bar(Screen.width, Screen.height, _scale);
            var panel = new BarPanel { Root = BuildCard("bar", rect, PlaceCorner.BottomLeft) };
            RectTransform root = panel.Root;

            HudBox stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            HudUi.TopLeft(stripe.Rect, 0f, 0f, rect.width, 3f * _scale);

            // The hint, right-aligned along the tab row. There is no "BUILD" label any more: the tabs are
            // what the panel is called, and a word naming the panel beside seven words naming its contents
            // is the one line the two-level bar does not need.
            HudText hint = Label(root, "hint", 11f, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(hint.Rect, 12f * _scale, 8f * _scale, rect.width - 24f * _scale, 15f * _scale);
            hint.Set("click a tile or press " + BuildInput.KeyRangeText() +
                "   ·   again drops it   ·   drag lays a run",
                _palette.HudText);

            BuildKind[] kinds = _world.Content.BuildKinds;
            int count = kinds.Length;
            float pad = 10f * _scale;
            float gap = 6f * _scale;
            float tileWidth = (rect.width - 2f * pad - (count - 1) * gap) / count;
            float tileHeight = rect.height - 39f * _scale;
            panel.TileWidth = tileWidth;
            panel.TileHeight = tileHeight;
            panel.Pad = pad;
            panel.Gap = gap;
            panel.TilesTop = 29f * _scale;

            for (int i = 0; i < count; i++)
            {
                BuildKind kind = kinds[i];
                var tile = new BuildTile { Kind = kind };

                tile.Root = HudUi.Node(root, "tile" + i);
                HudUi.TopLeft(tile.Root, pad + i * (tileWidth + gap), 29f * _scale, tileWidth, tileHeight);

                tile.Button = tile.Root.gameObject.AddComponent<HudButton>();
                BuildKind captured = kind;
                // A locked tile is refused here rather than by the simulation's rejection toast: the map
                // has not handed that operator over (§7), so a ghost promising it would be a lie and a
                // click that produced "NOT ON THIS MAP" would be a worse answer than no selection at all.
                tile.Button.Pressed = () => SelectKind(captured);

                tile.Button.Fill = HudUi.Box(tile.Root, "back", _palette.HudPanel);
                HudUi.StretchFull(tile.Button.Fill.Rect);
                tile.Button.Fill.Image.raycastTarget = true;

                tile.Stripe = HudUi.Box(tile.Root, "stripe", _palette.HudPanel);
                HudUi.TopLeft(tile.Stripe.Rect, 0f, 0f, tileWidth, 3f * _scale);

                tile.Hotkey = Label(tile.Root, "hotkey", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
                HudUi.TopLeft(tile.Hotkey.Rect, 8f * _scale, 9f * _scale, 26f * _scale, 15f * _scale);
                tile.Hotkey.Set(BuildInput.HotkeyLabel(i), _palette.MachineIdle);

                tile.Cost = Label(tile.Root, "cost", 12f, FontStyle.Bold, TextAnchor.UpperRight);
                HudUi.TopLeft(tile.Cost.Rect, 0f, 9f * _scale, tileWidth - 8f * _scale, 15f * _scale);
                tile.Cost.Set(_world.Content.Machine(kind).Cost.ToString(), _palette.MachineIdle);

                tile.CostIcon = HudUi.Icon(tile.Root, "costIcon", _icons.Mark(MoneyShape));
                HudUi.TopLeft(tile.CostIcon.rectTransform, tileWidth, 10f * _scale, 13f * _scale,
                    13f * _scale);
                tile.CostIcon.gameObject.SetActive(false);

                tile.Name = Label(tile.Root, "name", 13f, FontStyle.Bold, TextAnchor.MiddleCenter);
                HudUi.TopLeft(tile.Name.Rect, 4f * _scale, 27f * _scale, tileWidth - 8f * _scale, 20f * _scale);
                tile.Name.Set(_world.Content.Machine(kind).Name, _palette.HudText);

                tile.Locked = !_world.IsUnlocked(kind);

                tile.Role = Label(tile.Root, "role", 10f, FontStyle.Bold, TextAnchor.MiddleCenter);
                HudUi.TopLeft(tile.Role.Rect, 4f * _scale, tileHeight - 20f * _scale,
                    tileWidth - 8f * _scale, 14f * _scale);
                tile.Role.Set(tile.Locked ? "LOCKED" : RoleOf(kind),
                    Blend(CategoryColor(kind), _palette.HudText, 0.15f));

                tile.Afford = HudUi.BarFill(tile.Root, "afford", 1f * _scale, 1f * _scale,
                    tileWidth - 2f * _scale, 2f * _scale);

                tile.Border = HudUi.Frame(tile.Root, "frame", _palette.HudAccent, 1f * _scale);
                tile.Border.Show(false);

                tile.Selected = kind == _world.SelectedKind;
                tile.Affordable = _world.Economy.CanAfford(kind);

                panel.Tiles.Add(tile);
            }

            BuildRoleTabs(panel, root, kinds);

            // The first tab is open before the first frame: a bar that opened empty would read as a bar with
            // nothing in it, one frame before the player's first glance.
            panel.OpenRole = panel.Tabs.Count > 0 ? panel.Tabs[0].Role : (MachineRole?)null;
            LayoutBarTiles(panel);

            return panel;
        }

        /// <summary>
        /// The bar's first level: one tab per role this run's table actually carries, in the order each
        /// role's first building appears in it. A reading of the content rather than a second table to keep
        /// in step with the first - a new building lands in its role's tab by saying what role it has, and a
        /// narrower table simply has fewer tabs.
        ///
        /// Locked buildings stay inside their tab: the ladder is worth seeing, and the tile says LOCKED
        /// itself, so a tab that vanished until a map handed its operator over would hide the progression.
        /// </summary>
        private void BuildRoleTabs(BarPanel panel, RectTransform root, BuildKind[] kinds)
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                MachineRole role = MachineReading.Role(_world.Content.Machine(kinds[i]));
                if (panel.HasTab(role)) continue;

                var tab = new RoleTab { Role = role };
                MachineRole captured = role;
                tab.Button = HudUi.Button(root, "tab" + panel.Tabs.Count, 0f, 0f,
                    MachineReading.RoleLabel(role), FontSize(11f), _palette.HudText,
                    () => OpenRoleTab(captured));
                DressRoleTab(tab);
                panel.Tabs.Add(tab);
            }
        }

        private InfoPanel BuildInfo()
        {
            Rect rect = HudLayout.Info(HudLayout.Bar(Screen.width, Screen.height, _scale), _scale);

            var panel = new InfoPanel { Root = BuildCard("info", rect, PlaceCorner.BottomRight) };
            RectTransform root = panel.Root;

            // ----- the build card
            panel.BuildRoot = HudUi.Node(root, "build");
            HudUi.StretchFull(panel.BuildRoot);

            panel.BuildStripe = HudUi.Box(panel.BuildRoot, "stripe", _palette.HudAccent);
            Stripe(panel.BuildStripe, 3f * _scale);

            panel.BuildTitle = Label(panel.BuildRoot, "title", 15f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.BuildTitle.Rect, 14f * _scale, 9f * _scale, rect.width - 28f * _scale, 18f * _scale);

            panel.BuildRight = Label(panel.BuildRoot, "right", 12f, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(panel.BuildRight.Rect, 14f * _scale, 9f * _scale, rect.width - 28f * _scale, 18f * _scale);

            panel.Blurb = Label(panel.BuildRoot, "blurb", 11f, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            HudUi.TopLeft(panel.Blurb.Rect, 14f * _scale, 33f * _scale, rect.width - 28f * _scale, 32f * _scale);

            panel.Stats = Label(panel.BuildRoot, "stats", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Stats.Rect, 14f * _scale, 67f * _scale, rect.width - 28f * _scale, 15f * _scale);

            panel.Facing = Label(panel.BuildRoot, "facing", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Facing.Rect, 14f * _scale, 85f * _scale, 130f * _scale, 22f * _scale);

            panel.TurnLeft = HudUi.Button(panel.BuildRoot, "turnL", 54f * _scale, 22f * _scale, "< turn",
                FontSize(13f), _palette.HudText, () => _rotate?.Invoke(-1));
            panel.TurnRight = HudUi.Button(panel.BuildRoot, "turnR", 54f * _scale, 22f * _scale, "turn >",
                FontSize(13f), _palette.HudText, () => _rotate?.Invoke(+1));
            panel.Inspect = HudUi.Button(panel.BuildRoot, "inspect", 74f * _scale, 22f * _scale, "inspect",
                FontSize(13f), _palette.HudText, PressInspect);

            // ----- the inspect card
            panel.InspectRoot = HudUi.Node(root, "inspect");
            HudUi.StretchFull(panel.InspectRoot);

            panel.InspectStripe = HudUi.Box(panel.InspectRoot, "stripe", _palette.HudAccent);
            Stripe(panel.InspectStripe, 3f * _scale);

            panel.InspectTitle = Label(panel.InspectRoot, "title", 15f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.InspectTitle.Rect, 14f * _scale, 9f * _scale, rect.width - 28f * _scale, 18f * _scale);

            panel.InspectRight = Label(panel.InspectRoot, "right", 12f, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(panel.InspectRight.Rect, 14f * _scale, 9f * _scale, rect.width - 28f * _scale, 18f * _scale);

            panel.Rows = new HudText[InspectRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = Label(panel.InspectRoot, "row" + i, 12f, FontStyle.Normal, TextAnchor.UpperLeft);
                HudUi.TopLeft(panel.Rows[i].Rect, 14f * _scale, (31f + 17f * i) * _scale,
                    rect.width - 28f * _scale, 15f * _scale);
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

            picker.Title = Label(picker.Root, "title", 11f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(picker.Title.Rect, 14f * _scale, 0f, 120f * _scale, 14f * _scale);
            picker.Title.Set("ROUTES", _palette.HudAccent);

            RectTransform canvas = HudUi.Node(picker.Root, "canvas");
            HudUi.TopLeft(canvas, 14f * _scale, 15f * _scale, CanvasSize * _scale, CanvasSize * _scale);

            // Draw order inside the canvas is the read order: the ghost cells, then the figure over them,
            // then the buttons that claim the clicks.
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Vector2 at = CellOffset(quadrant);

                HudBox ghost = HudUi.Box(canvas, "ghost" + quadrant, GhostCell);
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
                    string.Empty, FontSize(11f), _palette.HudText, () => PressFilterQuadrant(captured));
                button.Label.Show(false);
                HudUi.TopLeft(button.Root, at.x * _scale, at.y * _scale, CellSize * _scale, CellSize * _scale);
                picker.Quadrants[quadrant] = button;
            }

            HudUi.Frame(canvas, "frame", PanelBorder(), 1f * _scale);

            picker.Mode = HudUi.Button(picker.Root, "mode", ModeWidth * _scale, 20f * _scale, string.Empty,
                FontSize(12f), _palette.HudText, PressFilterMode);
            HudUi.TopLeft(picker.Mode.Root, ModeLeft * _scale, 15f * _scale, ModeWidth * _scale, 20f * _scale);

            picker.Caption = Label(picker.Root, "caption", 11f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(picker.Caption.Rect, ModeLeft * _scale, 38f * _scale,
                card.width - (ModeLeft + 14f) * _scale, 14f * _scale);

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
            // at 42% of the leftover height - and placed by that top edge, the way every panel is.
            Rect rect = new Rect((Screen.width - 460f * _scale) * 0.5f, (Screen.height - 150f * _scale) * 0.42f,
                460f * _scale, 150f * _scale);
            HudUi.TopCentre(root, rect.y, rect.width, rect.height);

            HudBox back = HudUi.Box(root, "back", _palette.HudPanel);
            HudUi.StretchFull(back.Rect);
            HudUi.Frame(root, "frame", PanelBorder(), 1f * _scale);

            panel.Label = Label(root, "label", 22f, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: true);
            HudUi.Stretch(panel.Label.Rect, 0f, 22f * _scale, 0f, 120f * _scale);

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

            panel.Back = HudUi.Box(root, "back", _palette.HudPanel);
            HudUi.StretchFull(panel.Back.Rect);
            panel.Frame = HudUi.Frame(root, "frame", _palette.HudWarn, 2f * _scale);

            panel.Label = Label(root, "label", 15f, FontStyle.Bold, TextAnchor.MiddleCenter);
            HudUi.TopLeft(panel.Label.Rect, 14f * _scale, 8f * _scale, 100f, 30f);

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

            panel.Back = HudUi.Box(root, "back", _palette.HudPanel);
            HudUi.StretchFull(panel.Back.Rect);
            panel.Frame = HudUi.Frame(root, "frame", PanelBorder(), 1f * _scale);

            panel.Title = Label(root, "title", 13f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Title.Rect, 12f * _scale, 7f * _scale, 200f, 18f * _scale);

            panel.Rows = new HudText[TooltipRowCapacity];
            for (int i = 0; i < panel.Rows.Length; i++)
            {
                panel.Rows[i] = Label(root, "row" + i, 11f, FontStyle.Normal, TextAnchor.UpperLeft);
                HudUi.TopLeft(panel.Rows[i].Rect, 12f * _scale, (27f + 16f * i) * _scale, 200f, 15f * _scale);
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

            return ShowTutorial && _tutorial != null && _tutorial.Root.gameObject.activeSelf &&
                Over(_tutorial.Root, pointer);
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
        /// the run is on the map that first asks for the chain - the same predicate the camera rig is given,
        /// so the card and the map's bound cannot disagree.</summary>
        private void UpdateTutorial()
        {
            bool up = ShowTutorial;
            if (up == _tutorialUp) return;

            _tutorialUp = up;
            _tutorial.Root.gameObject.SetActive(up);
        }

        // ------------------------------------------------------------------ toast

        private void UpdateToast()
        {
            bool live = TryLastRefusal(out SimEvent refusal);
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

            if (_toastTick != refusal.Tick || _toastTutorialUp != ShowTutorial)
            {
                _toastTick = refusal.Tick;
                _toastTutorialUp = ShowTutorial;
                _toastText = RefusalText(refusal);

                float padding = 14f * _scale;
                float width = Mathf.Min(_toast.Label.PreferredWidth + 2f * padding,
                    Screen.width - 2f * HudLayout.Margin * _scale);
                float height = _toast.Label.PreferredHeight + 16f * _scale;

                Rect toast = HudLayout.Toast(Screen.width, _scale, width, height, ShowTutorial);
                HudUi.TopCentre(_toast.Root, toast.y, toast.width, toast.height);
                HudUi.TopLeft(_toast.Label.Rect, padding, 8f * _scale, width - 2f * padding, height - 16f * _scale);
            }

            float fade = RefusalFadeOf(_world.TickCount - refusal.Tick);
            _toastAlpha = fade;
            _toast.Group.alpha = fade;
            _toast.Back.Set(WithAlpha(_palette.HudPanel, fade));
            _toast.Frame.Set(WithAlpha(_palette.HudWarn, fade), 2f * _scale);
            _toast.Label.Set(_toastText, WithAlpha(_palette.HudWarn, fade));
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
                    // The map has not handed this operator over yet (§7's unlock ladder). The build bar
                    // greys the tile out, so this is the keyboard's version of the same answer.
                    return "NOT ON THIS MAP - " + def.Name + " is an operator this map has not unlocked";
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
            Color numberColour = _palette.Outline;
            if (selected.HasValue && !_world.Economy.CanAfford(selected.Value))
                numberColour = _palette.CursorNoFunds;
            if (_toastAlpha > 0f)
                numberColour = WithAlpha(_palette.HudWarn, _toastAlpha);

            int shapes = _world.Economy.Shapes;
            if (shapes != _stockpileCount)
            {
                _stockpileCount = shapes;
                _stockpile.Number.Set(shapes.ToString(), numberColour);

                // The shape follows the number rather than sitting in a column of its own: the readout grows
                // rightwards and stays glued to the card. Drawn, not typed - the character for a mineral is
                // not in every font a player has, and this mark must never be a gap.
                if (_stockpile.Icon != null)
                {
                    float used = _stockpile.Number.PreferredWidth;
                    float side = Mathf.Min(28f * _scale, _stockpileRect.height - 4f * _scale);
                    float iconX = used + 6f * _scale;
                    bool fits = iconX + side <= _stockpileRect.width;
                    _stockpile.Icon.gameObject.SetActive(fits);
                    if (fits)
                        HudUi.TopLeft(_stockpile.Icon.rectTransform, iconX,
                            (_stockpileRect.height - side) * 0.5f, side, side);
                }
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

            _status.Stripe.Set(
                jams > 0 || blocked > 0 || stalled > 0 || starved || hurting || coreHit || outOfView.Urgent
                    ? _palette.HudWarn : _palette.HudAccent);

            _status.Title.Set(TitleLine(), _palette.HudAccent);
            _status.Seed.Set(SeedLine(), Blend(_palette.HudText, _palette.HudPanel, 0.45f));

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

            bool button = CanStartWaveNow;
            if (_status.WaveButton.Root.gameObject.activeSelf != button)
                _status.WaveButton.Root.gameObject.SetActive(button);
            if (button)
                _status.WaveButton.Label.Set(">  start wave " + _world.Waves.NextWave + " now");
        }

        /// <summary>
        /// The production line, said only when something is wrong: a segment that cannot pass its item on
        /// first - it stops the run, not just one machine - then a gun that cannot shoot, then, once a wave
        /// is walking in, the absence of anything to shoot it with. When the defence is fed and the belts
        /// are clear the line stays silent: "all clear" printed every second is a line nobody reads. Null
        /// means no news, and the line is hidden.
        ///
        /// Jammed and blocked are two words for one symptom, and both are counted here because they are the
        /// two ways a line stops: a jam is an item a machine refused, a block is an item with nowhere to go
        /// at all. The block is the one that used to be silent - a line ending in the air reddened nothing,
        /// so a factory came to a standstill with nothing on the HUD to say why.
        /// </summary>
        /// <summary>
        /// The production line, said only when something is wrong: a segment that cannot pass its item on
        /// first - it stops the run, not just one machine - then a machine holding a piece its outlets will not
        /// take, then a gun that cannot shoot, then, once a wave
        /// is walking in, the absence of anything to shoot it with. When the defence is fed and the belts
        /// are clear the line stays silent: "all clear" printed every second is a line nobody reads. Null
        /// means no news, and the line is hidden.
        ///
        /// Jammed and blocked are two words for one symptom, and both are counted here because they are the
        /// two ways a <em>belt</em> stops: a jam is an item a machine refused, a block is an item with nowhere
        /// to go at all. The block is the one that used to be silent - a line ending in the air reddened nothing,
        /// so a factory came to a standstill with nothing on the HUD to say why. The stalled machine is the
        /// third of the three and the last one to have been silent: a machine that has eaten its item and
        /// cannot pass it on is neither a jam nor a block, so without the count here the belts behind it queue
        /// and the card says nothing.
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
                // item and cannot pass it on, so the belts behind it queue and nothing upstream is ever
                // "blocked". Both causes are one sentence - a lane that was never wired and a lane whose belt
                // is full are the same fact to the machine - and clicking the one the locator points at is
                // where the two are told apart.
                return stalled + " machine" + (stalled == 1 ? "" : "s") +
                       " stalled - an outlet is full or missing   ·   click one to read it";

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
                    text.Append("   ·   nearest ").Append(Mathf.RoundToInt(outOfView.Nearest)).Append(" tiles");

                // The word that matters is the verb: an enemy out of view is news, an enemy out of view that
                // is hitting something is the reason the arrows pulse.
                if (outOfView.Urgent) text.Append("   ·   hitting something");
            }

            if (outOfView.HasStops)
                AppendSeparator(text)
                    .Append(outOfView.Stops)
                    .Append(" stopped out of view");

            if (outOfView.CoreIsOut)
                AppendSeparator(text).Append("the Core is out of view   ·   C goes back to it");

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

            string nextLabel = "WAVE " + waves.NextWave;
            string afterLabel = "THEN " + (waves.NextWave + 1);

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
            label.Set(labelText, _palette.HudAccent);
            label.Show(entries.Count > 0);

            float x = 14f * _scale;
            if (entries.Count > 0) x += label.PreferredWidth + 6f * _scale;

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
                float chipWidth = countWidth + (withNames ? 3f * _scale + nameWidth + 5f * _scale : 0f) + 13f * _scale;

                if (x + chipWidth > _status.Root.rect.width - 14f * _scale) return false;

                chip.Back.Show(true);
                chip.Count.Show(true);
                HudUi.TopLeft(chip.Back.Rect, x - 5f * _scale, top * _scale, chipWidth + 10f * _scale, 24f * _scale);
                HudUi.TopLeft(chip.Count.Rect, x, (top + 4f) * _scale, countWidth + 2f * _scale, 18f * _scale);
                chip.Count.SetColour(_palette.HudText);

                if (withNames)
                {
                    HudUi.TopLeft(chip.Name.Rect, x + countWidth + 3f * _scale, (top + 4f) * _scale,
                        nameWidth + 2f * _scale, 18f * _scale);
                    chip.Name.SetColour(_palette.MachineIdle);
                }

                if (chip.Icon != null)
                {
                    HudIcons.Icon icon = _icons.Mark(entry.Round);
                    if (icon.Valid)
                    {
                        chip.Icon.texture = icon.Texture;
                        chip.Icon.uvRect = icon.Uv;
                        HudUi.TopLeft(chip.Icon.rectTransform, x + chipWidth - 13f * _scale,
                            (top + 5f) * _scale, 13f * _scale, 13f * _scale);
                        chip.Icon.gameObject.SetActive(true);
                    }
                    else chip.Icon.gameObject.SetActive(false);
                }

                x += chipWidth + 12f * _scale;
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
            Color colour = fraction > 0.4f ? _palette.HudGood : _palette.HudWarn;

            _status.Health.Set(fraction, colour);

            // The edge carries the hit: a bar that only shrinks says the same thing twice, while one that
            // goes red-rimmed for the window the Core was last chewed says it is happening now. The same
            // window the screen's danger frame fades over, so the card and the frame pulse together.
            _status.HealthFrame.Set(hit
                ? WithAlpha(_palette.HudWarn, 0.45f + 0.55f * life)
                : Blend(_palette.HudText, _palette.HudPanel, 0.5f));

            _status.HealthLabel.Set(
                "core " + Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp),
                _palette.Outline);

            // What the last hit cost, printed in the bar's right-hand margin and fading with it: the
            // difference between "87" and "87, and four of those just went".
            _status.HealthDamage.Show(hit);
            if (hit)
                _status.HealthDamage.Set("-" + DamageText(damage) + " hp", WithAlpha(_palette.HudWarn, life));
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
            WaveDirector waves = _world.Waves;
            float fraction = WaveClockFraction();

            // A wave being spent is good news and a countdown running is not, so the bar wears the colour of
            // whichever it is - the same two colours the diagnostics below it are said in.
            Color fill = waves.Phase == WavePhase.Running || waves.Phase == WavePhase.Over
                ? _palette.HudGood : _palette.HudAccent;

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

        /// <summary>
        /// The card's title: the run's identity - the map in the campaign and where the clock is. The seed
        /// the wave generator drew from is the card's footer (<see cref="SeedLine"/>): it belongs with the
        /// run, but it is twelve digits nobody reads twice, and on the title's line it pushed the wave the
        /// player is actually waiting for off the end of the card.
        ///
        /// The total is <see cref="WaveDirector.TotalWaves"/>, and an endless run has none: it prints the
        /// wave number alone rather than "wave 7 of 0", and no line here ever claims a final wave, because
        /// an endless run does not have one.
        /// </summary>
        private string TitleLine()
        {
            string map = _world.Map.Name;
            string at = _campaign != null && _campaign.MapCount > 1
                ? "map " + (_campaign.MapIndex + 1) + "/" + _campaign.MapCount + "  ·  "
                : string.Empty;

            // The endless re-entry of the last map is the same map on the card, so the card says so -
            // a run with no total to print is otherwise indistinguishable from a scripted one.
            string endless = _world.Waves.Endless ? "  ·  endless" : string.Empty;

            string head = "FACET  ·  " + at + map + endless;

            switch (_world.Status)
            {
                case GameStatus.Won:
                    return head + (_campaign != null && _campaign.HasNext ? "  ·  map cleared" : "  ·  campaign cleared");
                case GameStatus.Lost: return head + "  ·  the core is gone";
            }

            // The title names the wave and nothing else: the bar under it is the clock, and the line under
            // that has the number when the number is what matters.
            WaveDirector waves = _world.Waves;
            switch (waves.Phase)
            {
                case WavePhase.Held:
                case WavePhase.Intermission:
                    return head + "  ·  wave " + waves.NextWave + OfTotal(waves) + " next";
                case WavePhase.Running:
                    return head + "  ·  wave " + waves.CurrentWave + OfTotal(waves);
                case WavePhase.Over:
                    // The tick that finishes the table also ends the run, so a playing map never shows
                    // this; the clock still owes the title an answer rather than a default.
                    return head + "  ·  every wave fought";
                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its title line");
            }
        }

        /// <summary>" of 6" while the run has a bound, and nothing at all when it does not: an endless run
        /// counts toward no final wave, so a total would be a number it cannot reach.</summary>
        private static string OfTotal(WaveDirector waves)
            => waves.TotalWaves > 0 ? " of " + waves.TotalWaves : string.Empty;

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
                    // The opening's clock is the player's to start: it begins on the tick their first building
                    // lands, so its bar is empty and this line says what fills it. The button below, or N, is
                    // the other way to begin.
                    return "wave " + waves.NextWave + " starts when you build   ·   N calls it now";

                case WavePhase.Intermission:
                    return "next wave in " + Mathf.CeilToInt(waves.IntermissionRemaining) + "s"
                        + (_world.Paused ? string.Empty : "   ·   N starts it now") + SpeedNote();

                case WavePhase.Running:
                {
                    // Definition, not the authored table: an endless or generated run has waves the table
                    // never held, and the count still has to be right.
                    WaveDefinition current = waves.Definition(waves.CurrentWave);
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return left + " of " + (current != null ? current.Total : left) + " enemies left in this wave"
                        + (_world.Paused ? "   ·   paused" : string.Empty) + SpeedNote();
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
            if (!_controlsDirty && _controlsPaused == _world.Paused && _controlsGear == gear &&
                _controlsBehind == behind) return;
            _controlsDirty = false;
            _controlsPaused = _world.Paused;
            _controlsGear = gear;
            _controlsBehind = behind;

            _controls.Pause.Label.Set(_world.Paused ? ">" : "II");
            // The gear's word; amber when this browser cannot keep up with it, so 3x never quietly means 2x.
            _controls.Gear.Label.Set(GearWord(), behind ? _palette.HudWarn : _palette.HudText);
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
        private static IEnumerable<string> KeyLines()
        {
            yield return "WASD / arrows pan  ·  wheel zooms";
            yield return BuildInput.KeyRangeText() + " or click builds";
            yield return "the same one again drops it";
            yield return "Q / E turn  ·  drag lays belts";
            yield return "T how to play  ·  H this card";
            yield return "RMB deletes, or clears a jam";
            yield return "Esc reads  ·  C goes to the Core";
            yield return "N the wave  ·  Space  ·  R restart";
            yield return "[ / ] speed 1x - 3x";
        }

        // ------------------------------------------------------------------ build bar

        private void UpdateBuildBar()
        {
            if (!_tabsMeasured) LayoutRoleTabs();

            BuildKind? selected = _world.SelectedKind;

            // The hotkeys are global - a digit picks its building wherever it is filed - so a selection made
            // from the keyboard opens whichever tab that building lives in, and the bar follows the keys
            // rather than hiding what they just picked. Only a *change* of selection moves the bar: browsing
            // to another tab while a belt is in hand must not be undone the next frame.
            if (selected.HasValue && selected != _seenSelected)
                OpenRoleTab(MachineReading.Role(_world.Content.Machine(selected.Value)));
            _seenSelected = selected;

            for (int i = 0; i < _bar.Tabs.Count; i++)
            {
                RoleTab tab = _bar.Tabs[i];
                bool open = _bar.OpenRole.HasValue && _bar.OpenRole.Value == tab.Role;
                bool hovered = tab.Button.Hovered;
                if (tab.Open == open && tab.Hovered == hovered) continue;

                tab.Open = open;
                tab.Hovered = hovered;
                DressRoleTab(tab);
            }

            _hoveredTile = -1;

            for (int i = 0; i < _bar.Tiles.Count; i++)
            {
                BuildTile tile = _bar.Tiles[i];

                // A closed tile's button is inactive, so the pointer cannot leave it: its hover flag is frozen
                // at whatever it was when the tab closed. Asking the object rather than the flag is what keeps
                // the info card from describing a building that is off the bar.
                bool hovered = tile.Root.gameObject.activeSelf && tile.Button.Hovered;
                if (hovered) _hoveredTile = i;

                bool selectedTile = tile.Kind == selected;
                bool affordable = _world.Economy.CanAfford(tile.Kind);
                bool locked = !_world.IsUnlocked(tile.Kind);

                if (tile.Selected != selectedTile || tile.Hovered != hovered ||
                    tile.Affordable != affordable || tile.Locked != locked)
                {
                    bool wasLocked = tile.Locked;
                    tile.Selected = selectedTile;
                    tile.Hovered = hovered;
                    tile.Affordable = affordable;
                    tile.Locked = locked;

                    // The two labels the lock owns: put the role back the moment the map hands the operator
                    // over, which is what a campaign's map change does. The cost mark is placed from the
                    // cost's measured width, so a lock change makes it place itself again.
                    if (wasLocked != locked)
                    {
                        tile.Role.Set(locked ? "LOCKED" : RoleOf(tile.Kind));
                        tile.CostPlaced = false;
                    }

                    DressBuildTile(tile);
                }

                // The line along the tile's bottom says how far from affordable the bank is: nine tenths of
                // the way is a belt to lay now, a tenth of the way is a plan for later. A locked tile has no
                // cost to save toward, so its line stays empty.
                if (!tile.Locked && !tile.Affordable && _world.Content.Machine(tile.Kind).Cost > 0)
                    tile.Afford.Set(Mathf.Clamp01(_world.Economy.Shapes / (float)_world.Content.Machine(tile.Kind).Cost),
                        _palette.CursorNoFunds);
                else tile.Afford.Set(0f, Color.clear);
            }

            // The cost's shape sits glued to the number's left edge, and the number's width is known
            // only once the text has laid itself out - so the first frames place it as they learn it.
            for (int i = 0; i < _bar.Tiles.Count; i++) PlaceCostIcon(_bar.Tiles[i]);
        }

        /// <summary>
        /// One building as a button: hotkey, cost, name and role over a stripe tinted by that role.
        /// Selected is an accent outline and tinted fill, unaffordable greys the name and turns the cost
        /// amber, and the pointer lightens the tile. A click selects the kind through the input latch - it
        /// never places anything, so a stray click can only change what is selected.
        ///
        /// A kind this map has not unlocked is drawn as a locked tile: grey, its role replaced by LOCKED,
        /// and no cost. It stays on the bar rather than disappearing because the ladder is worth seeing -
        /// "the rotator exists and some map hands it over" is the progression - but it cannot
        /// be selected, and <see cref="SelectKind"/> is where that is enforced for the click as well as the
        /// key.
        /// </summary>
        private void DressBuildTile(BuildTile tile)
        {
            Color category = CategoryColor(tile.Kind);

            if (tile.Locked)
            {
                tile.Button.Fill.Set(Blend(_palette.HudPanel, _palette.Outline, 0.25f));
                tile.Stripe.Set(Blend(category, _palette.HudPanel, 0.8f));
                tile.Border.Show(false);
                tile.Hotkey.SetColour(Blend(_palette.MachineIdle, _palette.HudPanel, 0.5f));
                tile.Cost.SetColour(Blend(_palette.MachineIdle, _palette.HudPanel, 0.5f));
                tile.Name.SetColour(Blend(_palette.HudText, _palette.HudPanel, 0.55f));
                tile.Role.SetColour(Blend(_palette.MachineIdle, _palette.HudPanel, 0.6f));
                return;
            }

            Color fill = _palette.HudPanel;
            if (tile.Selected) fill = Blend(_palette.HudPanel, category, 0.18f);
            else if (tile.Hovered) fill = Blend(_palette.HudPanel, _palette.HudText, 0.10f);
            else if (!tile.Affordable) fill = Blend(_palette.HudPanel, _palette.CursorNoFunds, 0.12f);
            tile.Button.Fill.Set(fill);

            tile.Stripe.Set(tile.Selected ? category : Blend(category, _palette.HudPanel, 0.4f));

            if (tile.Selected)
            {
                tile.Border.Show(true);
                tile.Border.Set(_palette.HudAccent, 2f * _scale);
            }
            else if (tile.Hovered)
            {
                tile.Border.Show(true);
                tile.Border.Set(Blend(_palette.HudText, _palette.HudPanel, 0.35f), 1f * _scale);
            }
            else tile.Border.Show(false);

            tile.Hotkey.SetColour(tile.Selected ? _palette.HudAccent : _palette.MachineIdle);

            // The cost is the line that answers "can I afford this", so it is the line that turns amber
            // when the answer is no - the same colour the cursor ghost and the resource band use for the
            // same fact.
            tile.Cost.SetColour(tile.Affordable
                ? (tile.Selected ? _palette.HudText : _palette.MachineIdle)
                : _palette.CursorNoFunds);

            tile.Name.SetColour(tile.Affordable ? _palette.HudText : _palette.MachineIdle);
        }

        /// <summary>The bar's one way to change what is selected, and the one place a locked kind is
        /// refused: the tile's click and the digit key are the same door, so the rule has one statement.
        /// Asking for the kind already in hand drops it, which is the read cursor.</summary>
        private void SelectKind(BuildKind kind)
        {
            if (_world == null || !_world.IsUnlocked(kind)) return;

            _selectKind?.Invoke(kind);
        }

        // ------------------------------------------------------------------ the bar's two levels

        /// <summary>Open a role's tab. Browsing is not selecting: the tab decides what the bar shows and
        /// nothing else, so a player can look at what a later map will hand them without dropping the belt in
        /// hand - and the selection, when it changes, opens its own tab in
        /// <see cref="UpdateBuildBar"/>.</summary>
        private void OpenRoleTab(MachineRole role)
        {
            if (_bar.OpenRole.HasValue && _bar.OpenRole.Value == role) return;

            _bar.OpenRole = role;
            LayoutBarTiles(_bar);
        }

        /// <summary>
        /// Show the open role's buildings and slide them up to the left edge, in the table's own order; the
        /// rest go away. A hidden tile is not merely invisible - its button stops taking clicks and its
        /// tooltip stops firing - which is what "the bar is showing this role" has to mean for the one panel
        /// that answers the pointer.
        ///
        /// Tile sizes are set once, when the bar is built, from the whole table's count. A tile that grew and
        /// shrank as the player browsed would move its own cost, name and afford line under the pointer that
        /// was about to click it. The cost icon is placed from the tile's width and so survives a move.
        /// </summary>
        private void LayoutBarTiles(BarPanel panel)
        {
            float x = panel.Pad;

            for (int i = 0; i < panel.Tiles.Count; i++)
            {
                BuildTile tile = panel.Tiles[i];
                bool open = !panel.OpenRole.HasValue
                    || MachineReading.Role(_world.Content.Machine(tile.Kind)) == panel.OpenRole.Value;

                if (tile.Root.gameObject.activeSelf != open) tile.Root.gameObject.SetActive(open);
                if (!open) continue;

                HudUi.TopLeft(tile.Root, x, panel.TilesTop, panel.TileWidth, panel.TileHeight);
                x += panel.TileWidth + panel.Gap;
            }

            // The pointer may well be standing on the tile that just left; a hover flag nobody can clear
            // would keep the info card describing a building that is no longer on the bar.
            _hoveredTile = -1;
        }

        /// <summary>
        /// Lay the tabs out along the header row, each as wide as its own word plus padding. Measured rather
        /// than guessed: the words are the roles' own labels, and a tab one character too narrow is a clipped
        /// PROCESSING. uGUI measures text on its own schedule, so this runs again until every width has
        /// arrived.
        /// </summary>
        private void LayoutRoleTabs()
        {
            float x = _bar.Pad;
            float y = 5f * _scale;
            float height = 22f * _scale;
            bool measured = true;

            for (int i = 0; i < _bar.Tabs.Count; i++)
            {
                RoleTab tab = _bar.Tabs[i];
                float width = tab.Button.Label.PreferredWidth + 18f * _scale;
                if (tab.Button.Label.PreferredWidth <= 0f) measured = false;

                HudUi.TopLeft(tab.Button.Root, x, y, width, height);
                x += width + _bar.Gap;
            }

            _tabsMeasured = measured;
        }

        /// <summary>
        /// A tab's three states: the open one wears its role's colour, the one under the pointer lifts out of
        /// the panel, and the rest sit flat and grey. The word on it is the role's own label - the same word
        /// the tiles underneath print - so the bar's two levels are one taxonomy read twice, and a building
        /// is filed where its own tile says it is.
        /// </summary>
        private void DressRoleTab(RoleTab tab)
        {
            Color category = CategoryColour(tab.Role);

            tab.Button.Fill.Set(tab.Open
                ? Blend(_palette.HudPanel, category, 0.22f)
                : tab.Hovered
                    ? Blend(_palette.HudPanel, _palette.HudText, 0.10f)
                    : Blend(_palette.HudPanel, _palette.Outline, 0.30f));

            tab.Button.Label.SetColour(tab.Open
                ? _palette.HudText
                : Blend(category, _palette.HudText, 0.4f));

            tab.Button.Border.Set(tab.Open ? category : Blend(category, _palette.HudPanel, 0.55f),
                tab.Open ? 2f * _scale : 1f * _scale);
        }

        /// <summary>The cost's shape, once the cost's own width is known: the picture just outside the
        /// number's left edge, where a cost is a mark rather than a sentence. A locked tile has no price to
        /// pay yet, so it draws no cost mark.</summary>
        private void PlaceCostIcon(BuildTile tile)
        {
            if (tile.CostIcon == null) return;

            if (tile.Locked)
            {
                tile.CostIcon.gameObject.SetActive(false);
                return;
            }

            if (tile.CostPlaced) return;
            float textWidth = tile.Cost.PreferredWidth;
            if (textWidth <= 0f) return;

            tile.CostPlaced = true;
            float x = _bar.TileWidth - 8f * _scale - textWidth - 13f * _scale - 3f * _scale;
            if (x < 28f * _scale) return;   // never under the hotkey

            HudUi.TopLeft(tile.CostIcon.rectTransform, x, 10f * _scale, 13f * _scale, 13f * _scale);
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
            BuildKind[] kinds = _world.Content.BuildKinds;
            bool hovered = _hoveredTile >= 0 && _hoveredTile < kinds.Length;

            if (hovered || _world.SelectedKind.HasValue)
            {
                BuildKind kind = hovered ? kinds[_hoveredTile] : _world.SelectedKind.Value;

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
                    _info.Facing.Set(GhostFacing(kind), _palette.MachineIdle);
                }

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
            Color category = CategoryColor(kind);
            bool locked = !_world.IsUnlocked(kind);

            _info.BuildStripe.Set(category);
            _info.BuildTitle.Set(def.Name, _palette.HudText);
            _info.BuildRight.Set(
                def.Cost + " " + Plural(Shape.MineralName(_world.Map.CurrencyMineral), def.Cost) + "   " +
                (locked ? "LOCKED" : RoleOf(kind)),
                _palette.MachineIdle);

            // A locked kind is described on the card like any other - the catalogue's words are not a
            // secret - with the one thing the player needs to know about it said first.
            _info.Blurb.Set(locked
                ? "not unlocked on this map · " + Blurb(kind)
                : Blurb(kind), _palette.HudText);

            string stats = Stats(kind);
            bool hasStats = !string.IsNullOrEmpty(stats);
            _info.Stats.Show(hasStats);
            if (hasStats) _info.Stats.Set(stats, category);

            // The facing line and the three buttons sit on the first line after whatever came above:
            // a building with numbers to show pushes them down, one without keeps them up.
            float y = hasStats ? 85f : 67f;
            At(_info.Facing.Rect, 14f, y, 130f, 22f);
            AtRight(_info.TurnLeft.Root, 74f, y, 54f, 22f);
            AtRight(_info.TurnRight.Root, 14f, y, 54f, 22f);
            AtRight(_info.Inspect.Root, 180f, y, 74f, 22f);

            // With something selected the card also carries the keyboard-free way back to reading the
            // map: the key is Escape, the button is the same door.
            _info.Inspect.Root.gameObject.SetActive(_world.SelectedKind.HasValue);

            _info.Facing.Set(GhostFacing(kind), _palette.MachineIdle);
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
                Color category = CategoryColor(machine.Build);

                InspectHeader(def.Name, category, RoleOf(machine.Build) + "   " + cell);
                InspectLine(MachineReading.Ports(machine), _palette.MachineIdle);

                ReadingNote note = MachineReading.LiveNote(_world, cell, machine);
                InspectLine(note.Text, NoteColour(note.Severity));

                string stats = Stats(machine.Build);
                if (!string.IsNullOrEmpty(stats)) InspectLine(stats, category);

                if (machine.MaxHp > 0f)
                {
                    bool hurting = machine.HealthFraction < 0.4f;
                    InspectLine("holding " + Mathf.CeilToInt(machine.Hp) + " / " +
                        Mathf.CeilToInt(machine.MaxHp) + " health",
                        hurting ? _palette.HudWarn : _palette.HudGood);
                }

                InspectLine(MachineReading.FacingNote(def, machine), _palette.MachineIdle);
                FinishInspect();
                return;
            }

            if (_world.Belts.TryGet(cell, out BeltState belt))
            {
                MachineDef def = _world.Content.Machine(BuildKind.Belt);

                InspectHeader(def.Name, CategoryColor(BuildKind.Belt),
                    RoleOf(BuildKind.Belt) + "   " + cell);

                if (belt.Jammed)
                {
                    // A jam arrives with the item that caused it on the belt; name that item when it is
                    // there, because a jam is always "this machine refuses this round" and the round is the
                    // half of it the player can act on.
                    string culprit = belt.Item.IsEmpty ? string.Empty : " by " + belt.Item.Describe();
                    InspectLine("JAMMED" + culprit + " - the machine this segment feeds will not take it",
                        _palette.HudWarn);
                }
                else if (belt.HasItem)
                    InspectLine("carrying " + belt.Item.Describe() + " · " +
                        Mathf.RoundToInt(belt.Progress * 100f) + "% across", _palette.HudText);
                else
                    InspectLine("empty - nothing riding it right now", _palette.HudText);

                InspectLine("carries its item to the " + MachineReading.DirName(belt.Direction) +
                    " · one item per cell, so this is also its rate", _palette.MachineIdle);
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
                    hurting ? _palette.HudWarn : _palette.HudGood);
                InspectLine("a complete shape belted into it becomes spendable - anything less is destroyed",
                    _palette.MachineIdle);
                InspectLine("losing it loses the run", _palette.MachineIdle);
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

            InspectHeader("INSPECT", _palette.HudAccent, "nothing selected");
            InspectLine("click a building, a vein or a doorway to read it: what it is, what it is doing, and " +
                "how its belts wire it up", _palette.HudText);
            InspectLine("click a build tile or press 1-9 to build one · the same one again to read instead",
                _palette.MachineIdle);
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
            Place(_info.Root, HudLayout.Info(HudLayout.Bar(Screen.width, Screen.height, _scale), _scale, up),
                PlaceCorner.BottomRight);

            for (int i = 0; i < _info.Rows.Length; i++)
                HudUi.TopLeft(_info.Rows[i].Rect, 14f * _scale,
                    (31f + (up ? HudLayout.InfoPickHeight : 0f) + 17f * i) * _scale,
                    (HudLayout.InfoWidth - 28f) * _scale, 15f * _scale);
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
            Color captionColour = _palette.MachineIdle;
            if (!TryPendingFilter(out _))
            {
                if (_setFilter == null)
                {
                    caption = "routing " + machine.Filter.Describe() + " now   ·   read-only";
                    captionColour = _palette.MachineIdle;
                }
                else
                {
                    caption = machine.Filter.IsNone
                        ? caption
                        : "routing " + machine.Filter.Describe() + " now   ·   click a quadrant to change it";
                    captionColour = machine.Filter.IsNone ? _palette.HudWarn : _palette.MachineIdle;
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

        /// <summary>The card's title row and the accent stripe: every card opens the same way, with the
        /// building's own category colour.</summary>
        private void InspectHeader(string name, Color accent, string right)
        {
            _info.InspectStripe.Set(accent);
            _info.InspectTitle.Set(name, _palette.HudText);
            _info.InspectRight.Set(right, _palette.MachineIdle);
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
                case NoteSeverity.Good: return _palette.HudGood;
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
                InspectLine("enemies enter on this tile and walk at the Core", _palette.MachineIdle);
            }

            if (!ore) return;

            InspectLine("mines as " + Shape.Full(mineral).Describe() +
                " - only a drill may be built on it", oreColour);

            if (TryVeinAt(cell, out ShapePatch vein))
                InspectLine(vein.Width + " × " + vein.Height + " vein in the ground", _palette.MachineIdle);
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

            InspectLine(ObstacleNote(kind), Blend(colour, _palette.HudText, 0.6f));
            InspectLine("map ground - nothing may be built on it, and it cannot fall", _palette.MachineIdle);
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
                    show = FillTooltip(_cursor);
                }
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
                TooltipHeader(def.Name, CategoryColor(machine.Build));
                TooltipLine(MachineReading.Ports(machine), _palette.MachineIdle);

                ReadingNote note = MachineReading.LiveNote(_world, cell, machine);
                TooltipLine(note.Text, NoteColour(note.Severity));
                FinishTooltip();
                return true;
            }

            if (_world.Belts.TryGet(cell, out BeltState belt))
            {
                TooltipHeader(_world.Content.Machine(BuildKind.Belt).Name, CategoryColor(BuildKind.Belt));

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
                    _world.Core.HealthFraction < 0.4f ? _palette.HudWarn : _palette.HudGood);
                FinishTooltip();
                return true;
            }

            if (!_world.CanInspect(cell)) return false;

            TerrainKind kind = _world.Terrain.KindAt(cell);
            if (kind.IsObstacle())
            {
                TooltipHeader(kind.ToString(), TerrainRenderer.ColorOf(kind, _palette));
                TooltipLine(ObstacleNote(kind), Blend(TerrainRenderer.ColorOf(kind, _palette), _palette.HudText, 0.6f));
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
                TooltipLine(vein.Width + " × " + vein.Height + " vein in the ground", _palette.MachineIdle);

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

            _tooltipWidth = Mathf.Min(width + 24f * _scale, Screen.width - 16f);
            _tooltipHeight = (27f + 16f * _tooltipRows + 6f) * _scale;
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

                    colour = _palette.HudGood;
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
            Dress(_controls.Help, ButtonFill);
            Dress(_help.Close, ButtonFill);
            Dress(_help.Restart, ButtonFill);
            if (_tutorial.Root.gameObject.activeSelf) Dress(_tutorial.Hide, ButtonFill);
            Dress(_info.TurnLeft, ButtonFill);
            Dress(_info.TurnRight, ButtonFill);
            if (_info.Inspect.Root.gameObject.activeSelf) Dress(_info.Inspect, ButtonFill);

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
            if (_status.WaveButton.Root.gameObject.activeSelf)
            {
                float phase = (Time.time % 1.2f) / 1.2f;
                float pulse = _world.Paused ? 1f : 0.8f + 0.2f * (0.5f + 0.5f * Mathf.Sin(phase * (2f * Mathf.PI)));
                Dress(_status.WaveButton, WithAlpha(_palette.HudAccent, pulse));
            }
        }

        private void Dress(HudButton button, Color fill)
        {
            bool hovered = button.Hovered;
            button.Fill.Set(hovered ? Blend(fill, _palette.HudText, 0.16f) : fill);
            button.Border.Set(hovered ? _palette.HudAccent : Blend(_palette.HudText, _palette.HudPanel, 0.35f),
                1f * _scale);
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

        /// <summary>The wave line's tail naming the gear and its keys, so the status card says how to speed up
        /// the wait it is describing. Empty while paused: a stopped run has no pace to talk about.</summary>
        private string SpeedNote()
        {
            if (_world.Paused) return string.Empty;
            return GearNow > 1 ? "   ·   " + GearWord() + "  [ ] speed" : "   ·   ] speeds up";
        }

        /// <summary>The gear button's word: "1x", "2x" or "3x".</summary>
        private string GearWord() => _clock != null ? _clock.Label : "1x";

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

        private Color CategoryColor(BuildKind kind) =>
            CategoryColour(MachineReading.Role(_world.Content.Machine(kind)));

        /// <summary>Which colour files which role under. The two mineral colours are the palette's warm
        /// pair, and the roles take them the way the chains do: the mining and processing machines wear the
        /// circle's gold, the routing piece the HUD's own accent, and transport and walls the quiet machine
        /// grey. A role nobody has coloured is an error, not a quiet slide into "transport" - the discipline
        /// the badge and draw dispatches keep.</summary>
        private Color CategoryColour(MachineRole role)
        {
            switch (role)
            {
                case MachineRole.Defence: return _palette.HudWarn;
                case MachineRole.Mining:
                case MachineRole.Processing: return _palette.CircleMineral;
                case MachineRole.Routing: return _palette.HudAccent;
                case MachineRole.Transport: return _palette.MachineIdle;   // belts and pipes
                case MachineRole.Wall: return _palette.MachineIdle;
                // The drain wears the same quiet grey as the plumbing: it is a hole in the ground, not a
                // machine with a state worth watching, and the one thing it must not look like is a warning.
                case MachineRole.Waste: return _palette.MachineIdle;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role,
                        "a new role needs a colour in the build bar");
            }
        }

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

        private static Color Blend(Color from, Color to, float t) => Color.Lerp(from, to, t);

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

        private bool BarMatchesContent()
        {
            if (_bar == null) return false;
            BuildKind[] kinds = _world.Content.BuildKinds;
            if (_bar.Tiles.Count != kinds.Length) return false;

            for (int i = 0; i < kinds.Length; i++)
                if (_bar.Tiles[i].Kind != kinds[i]) return false;

            return true;
        }

        // ------------------------------------------------------------------ the panels as widgets

        private sealed class StatusPanel
        {
            public RectTransform Root;
            public HudBox Stripe;
            public HudText Title;
            public HudBar Health;
            public HudFrame HealthFrame;
            public HudText HealthLabel;
            public HudText HealthDamage;
            public HudBar Clock;
            public HudText News;
            public HudText NextLabel;
            public HudText AfterLabel;
            public Chip[] Chips;
            public Chip[] ChipsAfter;
            public HudText Production;
            public HudText Threat;
            public HudButton WaveButton;
            public HudText Seed;
        }

        private sealed class ControlsPanel
        {
            public RectTransform Root;
            public HudButton Pause;
            public HudButton Gear;
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
            public HudFrame Frame;
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
            public HudFrame Frame;
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

        private sealed class BarPanel
        {
            public RectTransform Root;
            public readonly List<BuildTile> Tiles = new List<BuildTile>();

            /// <summary>The bar's first level: one tab per role this table carries, in table order.</summary>
            public readonly List<RoleTab> Tabs = new List<RoleTab>();

            /// <summary>The role whose buildings the second level is showing, or null for "all of them" -
            /// which only a table with no roles at all can be.</summary>
            public MachineRole? OpenRole;

            public float TileWidth;
            public float TileHeight;
            public float Pad;
            public float Gap;

            /// <summary>Where the top of the tile row sits, under the tab row.</summary>
            public float TilesTop;

            public bool HasTab(MachineRole role)
            {
                for (int i = 0; i < Tabs.Count; i++)
                    if (Tabs[i].Role == role) return true;

                return false;
            }
        }

        /// <summary>One tab: the role it opens, the button that opens it, and the two states its dressing
        /// is written from - so a tab is only re-dressed when one of them changes.</summary>
        private sealed class RoleTab
        {
            public MachineRole Role;
            public HudButton Button;
            public bool Open;
            public bool Hovered;
        }

        private sealed class BuildTile
        {
            public RectTransform Root;
            public HudButton Button;
            public HudBox Stripe;
            public HudText Hotkey;
            public HudText Cost;
            public RawImage CostIcon;
            public HudText Name;
            public HudText Role;
            public HudBar Afford;
            public HudFrame Border;
            public BuildKind Kind;
            public bool Selected;
            public bool Hovered;
            public bool Affordable;
            public bool Locked;
            public bool CostPlaced;
        }

        private sealed class InfoPanel
        {
            public RectTransform Root;

            public RectTransform BuildRoot;
            public HudBox BuildStripe;
            public HudText BuildTitle;
            public HudText BuildRight;
            public HudText Blurb;
            public HudText Stats;
            public HudText Facing;
            public HudButton TurnLeft;
            public HudButton TurnRight;
            public HudButton Inspect;

            public RectTransform InspectRoot;
            public HudBox InspectStripe;
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

        /// <summary>Where the two briefing rows' chip backs sit, in design units from the card's top: the
        /// wave being counted to, then the one after it. The labels above each row are placed with them, in
        /// <see cref="BuildStatus"/>.</summary>
        private const float ChipsTop = 90f;
        private const float ChipsAfterTop = 114f;

        /// <summary>
        /// The sorter picker's geometry, in design units: a quartered canvas of four cells and the mode
        /// toggle beside it. Big enough that four quadrants read apart at the smallest scale, and small
        /// enough that the band it costs the info card is one line of the layout
        /// (<see cref="HudLayout.InfoPickHeight"/>).
        /// </summary>
        private const float CellSize = 22f;
        private const float CanvasSize = CellSize * 2f;

        /// <summary>Where the picker's band starts inside the card, under the header: the header runs
        /// 9..27, and the band's own widgets hang from here. The card's rows move down by
        /// <see cref="HudLayout.InfoPickHeight"/> while it is up, so this is the whole of the overlap
        /// rule.</summary>
        private const float PickerTop = 30f;

        /// <summary>The mode toggle's column, clear of the canvas.</summary>
        private const float ModeLeft = 14f + CanvasSize + 6f;
        private const float ModeWidth = 250f;

        /// <summary>The faint cell an empty quadrant wears in the picker, and nowhere else: on the map an
        /// empty quadrant is nothing, and a ghost there would draw an item the simulation does not have.
        /// Faint enough to be a target rather than content.</summary>
        private Color GhostCell => WithAlpha(_palette.HudText, 0.09f);
    }
}
