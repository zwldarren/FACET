using System;
using System.Collections.Generic;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>
    /// The player's whole operating surface: a status card top-left (the map and its wave, the Core's
    /// health and the wave's clock as two bars, the wave's bill of materials as chips, and two diagnostic
    /// lines), the stockpile floating beside it as a shape and a number with no panel of its own, a
    /// controls cluster top-right (pause and help, with the keyboard and restart behind the help card), a
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

        /// <summary>The shapes as pictures, built once from the same Palette the map draws with: the
        /// stockpile and build tiles are marks, not sentences, and a mark drawn as a character disappears in
        /// a player whose font lacks it. See <see cref="HudIcons"/>.</summary>
        private HudIcons _icons;

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
        private readonly List<Int2> _radarJams = new List<Int2>();
        private ThreatRadar.Marker[] _radarMarkers = new ThreatRadar.Marker[0];
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        // ------------------------------------------------------------------ what was drawn last

        private bool _statusDirty = true;
        private int _statusTick = -1;
        private int _statusSeen = -1;
        private bool _statusPaused;

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

        private bool _infoBuildShown;
        private BuildKind? _infoKind;
        private bool _infoDirty = true;
        private int _infoFacing = -1;

        private bool _inspectDirty = true;
        private Int2 _inspectCell = new Int2(-1, -1);
        private int _inspectTick = -1;
        private int _rowCount;

        private bool _tutorialUp;

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

            var view = _screen.Measure(_camera, ShowTutorial, look.EdgeInsetPixels);

            _world.Enemies.GetEnemies(_radarEnemies);
            _world.Belts.GetJammed(_radarJams);

            float pixels = Screen.height > 0 ? Screen.height : 1f;
            ThreatRadar.Collect(view, _radarEnemies, _radarJams, ThreatRadar.CoreCentre(_world),
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

        /// <summary>Is the "how to play" card up? The one predicate the card's claim on the pointer, the
        /// toast's clearance and the driver's camera rig all read, so no two pieces of the frame can disagree
        /// about whether the card is there.</summary>
        private bool ShowTutorial => _campaign != null && _campaign.IsFirstMap;

        /// <summary>Point the HUD at a world, its palette, and where the player is in the campaign. Called
        /// on every map change, which is why the canvas is built once and kept: a map change rebinds the
        /// world and lets the next update pass redraw every card, and only a changed content table rebuilds
        /// the build bar's tiles.
        ///
        /// <paramref name="campaign"/> is optional so a scene (or a test) with one map gets exactly the
        /// single-map HUD; the selection and rotation callbacks are optional so the bar draws without them,
        /// it just cannot be clicked.</summary>
        public void Initialize(SimWorld world, Palette palette, CampaignState campaign = null,
            Action<BuildKind?> selectKind = null, Action<int> rotate = null, MapCatalog catalog = null)
        {
            _world = world;
            _palette = palette;
            _campaign = campaign;
            _catalog = catalog;
            if (selectKind != null) _selectKind = selectKind;
            if (rotate != null) _rotate = rotate;

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
            _helpOpen = false;
            _compKey = null;
            _tooltipCell = new Int2(-2, -2);
            _tooltipDirty = true;
            _tooltipTick = -1;
            _tooltipRows = 0;
            _tutorialUp = !ShowTutorial;   // forces the tutorial's own update to decide again
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

        private StatusPanel BuildStatus()
        {
            Rect rect = HudLayout.Status(_scale);
            var panel = new StatusPanel { Root = BuildCard("status", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            panel.Stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            HudUi.Stretch(panel.Stripe.Rect, 0f, 0f, rect.width - 3f * _scale, rect.height);

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

            // The bill of materials as chips: count, enemy, and the shape it is weak to drawn as a picture -
            // the character the content table spells a weakness with is not in every font a player has.
            panel.Chips = new Chip[ChipCapacity];
            for (int i = 0; i < ChipCapacity; i++)
            {
                var chip = new Chip
                {
                    Back = HudUi.Box(root, "chip" + i + ".back", Blend(_palette.HudPanel, _palette.HudText, 0.07f)),
                    Count = Label(root, "chip" + i + ".count", 12f, FontStyle.Bold, TextAnchor.UpperLeft),
                    Name = Label(root, "chip" + i + ".name", 11f, FontStyle.Normal, TextAnchor.UpperLeft),
                };
                RectTransform iconRect = HudUi.Node(root, "chip" + i + ".icon");
                var icon = iconRect.gameObject.AddComponent<RawImage>();
                icon.raycastTarget = false;
                chip.Icon = icon;

                chip.Back.Show(false);
                chip.Count.Show(false);
                chip.Name.Show(false);
                icon.gameObject.SetActive(false);
                panel.Chips[i] = chip;
            }

            // Two lines that only speak when something needs them: a jam or a starved gun, and what is out
            // of view. Quiet is the default and silence is the good news - a line that only says "all clear"
            // trains the player to stop reading.
            panel.Production = Label(root, "production", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Production.Rect, 14f * _scale, 125f * _scale, rect.width - 28f * _scale, 19f * _scale);
            panel.Production.Show(false);

            panel.Threat = Label(root, "threat", 12f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(panel.Threat.Rect, 14f * _scale, 144f * _scale, rect.width - 28f * _scale, 19f * _scale);
            panel.Threat.Show(false);

            panel.WaveButton = HudUi.Button(root, "wave", 250f * _scale, 26f * _scale, string.Empty,
                FontSize(13f), _palette.HudPanel, PressWave);
            HudUi.TopLeft(panel.WaveButton.Root, 14f * _scale, 163f * _scale, 250f * _scale, 26f * _scale);
            panel.WaveButton.Root.gameObject.SetActive(false);

            return panel;
        }

        private TutorialPanel BuildTutorial()
        {
            Rect rect = HudLayout.Tutorial(_scale);
            var panel = new TutorialPanel { Root = BuildCard("tutorial", rect, PlaceCorner.TopLeft) };
            RectTransform root = panel.Root;

            HudBox stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            HudUi.TopLeft(stripe.Rect, 0f, 0f, 3f * _scale, rect.height);

            HudText title = Label(root, "title", 15f, FontStyle.Bold, TextAnchor.UpperLeft);
            HudUi.TopLeft(title.Rect, 14f * _scale, 10f * _scale, rect.width - 28f * _scale, 22f * _scale);
            title.Set("HOW TO PLAY", _palette.HudAccent);

            HudText body = Label(root, "body", 11f, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            HudUi.TopLeft(body.Rect, 14f * _scale, 38f * _scale, rect.width - 28f * _scale, 92f * _scale);
            body.Set(
                "1. Mine circle ore and carry it back to the Core using conveyor belts.\n" +
                "2. Ore can only be spent after it reaches the Core.\n" +
                "3. The wrong shape will jam a line. Right-click the jammed segment to clear it.\n" +
                "4. Match ammo to the enemy's weakness icon - anything else deals a quarter.",
                _palette.HudText);

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
            HudUi.TopLeft(panel.Restart.Root, 12f * _scale, 140f * _scale, rect.width - 24f * _scale, 26f * _scale);

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

            HudIcons.Icon icon = _icons.Shape(ShapeType.Circle);
            if (icon.Valid)
            {
                panel.Icon = HudUi.Icon(panel.Root, "icon", icon);
                float side = Mathf.Min(28f * _scale, rect.height - 4f * _scale);
                HudUi.TopLeft(panel.Icon.rectTransform, rect.width, (rect.height - side) * 0.5f, side, side);
                panel.Icon.gameObject.SetActive(false);
            }

            return panel;
        }

        private BarPanel BuildBar()
        {
            Rect rect = HudLayout.Bar(Screen.width, Screen.height, _scale);
            var panel = new BarPanel { Root = BuildCard("bar", rect, PlaceCorner.BottomLeft) };
            RectTransform root = panel.Root;

            HudBox stripe = HudUi.Box(root, "stripe", _palette.HudAccent);
            HudUi.TopLeft(stripe.Rect, 0f, 0f, rect.width, 3f * _scale);

            HudText header = Label(root, "header", 11f, FontStyle.Normal, TextAnchor.UpperLeft);
            HudUi.TopLeft(header.Rect, 12f * _scale, 9f * _scale, rect.width - 24f * _scale, 15f * _scale);
            header.Set("BUILD", _palette.HudAccent);

            HudText hint = Label(root, "hint", 11f, FontStyle.Normal, TextAnchor.UpperRight);
            HudUi.TopLeft(hint.Rect, 12f * _scale, 9f * _scale, rect.width - 24f * _scale, 15f * _scale);
            hint.Set("click a tile or press " + BuildInput.KeyRangeText() +
                "   ·   again to drop it   ·   drag to lay a run   ·   RMB deletes, or clears a jam   ·   Esc reads",
                _palette.HudText);

            BuildKind[] kinds = _world.Content.BuildKinds;
            int count = kinds.Length;
            float pad = 10f * _scale;
            float gap = 6f * _scale;
            float tileWidth = (rect.width - 2f * pad - (count - 1) * gap) / count;
            float tileHeight = rect.height - 39f * _scale;
            panel.TileWidth = tileWidth;

            for (int i = 0; i < count; i++)
            {
                BuildKind kind = kinds[i];
                var tile = new BuildTile { Kind = kind };

                tile.Root = HudUi.Node(root, "tile" + i);
                HudUi.TopLeft(tile.Root, pad + i * (tileWidth + gap), 29f * _scale, tileWidth, tileHeight);

                tile.Button = tile.Root.gameObject.AddComponent<HudButton>();
                BuildKind captured = kind;
                tile.Button.Pressed = () => _selectKind?.Invoke(captured);

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

                HudIcons.Icon icon = _icons.Shape(ShapeType.Circle);
                if (icon.Valid)
                {
                    tile.CostIcon = HudUi.Icon(tile.Root, "costIcon", icon);
                    HudUi.TopLeft(tile.CostIcon.rectTransform, tileWidth, 10f * _scale, 13f * _scale,
                        13f * _scale);
                    tile.CostIcon.gameObject.SetActive(false);
                }

                tile.Name = Label(tile.Root, "name", 13f, FontStyle.Bold, TextAnchor.MiddleCenter);
                HudUi.TopLeft(tile.Name.Rect, 4f * _scale, 27f * _scale, tileWidth - 8f * _scale, 20f * _scale);
                tile.Name.Set(_world.Content.Machine(kind).Name, _palette.HudText);

                tile.Role = Label(tile.Root, "role", 10f, FontStyle.Bold, TextAnchor.MiddleCenter);
                HudUi.TopLeft(tile.Role.Rect, 4f * _scale, tileHeight - 20f * _scale,
                    tileWidth - 8f * _scale, 14f * _scale);
                tile.Role.Set(RoleOf(kind), Blend(CategoryColor(kind), _palette.HudText, 0.15f));

                tile.Afford = HudUi.BarFill(tile.Root, "afford", 1f * _scale, 1f * _scale,
                    tileWidth - 2f * _scale, 2f * _scale);

                tile.Border = HudUi.Frame(tile.Root, "frame", _palette.HudAccent, 1f * _scale);
                tile.Border.Show(false);

                tile.Selected = kind == _world.SelectedKind;
                tile.Affordable = _world.Economy.CanAfford(kind);

                panel.Tiles.Add(tile);
            }

            return panel;
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
            HudUi.TopLeft(panel.BuildStripe.Rect, 0f, 0f, 3f * _scale, rect.height);

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
            HudUi.TopLeft(panel.InspectStripe.Rect, 0f, 0f, 3f * _scale, rect.height);

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

            panel.BuildRoot.gameObject.SetActive(false);
            panel.InspectRoot.gameObject.SetActive(false);
            return panel;
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
                    return "NOT ENOUGH " + Plural(ShapeType.Circle, 2).ToUpperInvariant() + " - " +
                           def.Name + " costs " + Mathf.RoundToInt(refusal.Amount) + ", you have " +
                           _world.Economy.Circles;
                case RejectionReason.Occupied:
                    return "BLOCKED - something already stands on " + refusal.Cell;
                case RejectionReason.BadGround:
                    return def.Tile == TileKind.Drill
                        ? "NO ORE - a drill mines only the patch it stands on"
                        : "ON ORE - only a drill may stand on a patch";
                case RejectionReason.Doorway:
                    return "A DOORWAY - waves walk in at " + refusal.Cell +
                           ", and nothing solid may stand there";
                default:
                    return "OFF THE MAP - nothing can be built on " + refusal.Cell;
            }
        }

        // ------------------------------------------------------------------ stockpile

        private void UpdateStockpile()
        {
            BuildKind? selected = _world.SelectedKind;
            Color numberColour = _palette.Outline;
            if (selected.HasValue && !_world.Economy.CanAfford(selected.Value))
                numberColour = _palette.CursorNoFunds;
            if (_toastAlpha > 0f)
                numberColour = WithAlpha(_palette.HudWarn, _toastAlpha);

            int circles = _world.Economy.Circles;
            if (circles != _stockpileCount)
            {
                _stockpileCount = circles;
                _stockpile.Number.Set(circles.ToString(), numberColour);

                // The shape follows the number rather than sitting in a column of its own: the readout grows
                // rightwards and stays glued to the card. Drawn, not typed - the circle character is not in
                // every font a player has, and this mark must never be a gap.
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
                _statusPaused == _world.Paused)
                return;

            _statusDirty = false;
            _statusTick = _world.TickCount;
            _statusSeen = (int)_world.Status;
            _statusPaused = _world.Paused;

            _world.Machines.CountTurrets(out int total, out int armed);
            bool starved = armed < total;
            int jams = _world.Belts.JamCount;
            bool hurting = _world.Core.HealthFraction <= 0.4f;
            ThreatRadar.Summary outOfView = ReadOutOfView();
            bool coreHit = ThreatRadar.CoreIsBeingHit(_world, _palette.Radar.DangerTicks);

            _status.Stripe.Set(
                jams > 0 || starved || hurting || coreHit || outOfView.Urgent
                    ? _palette.HudWarn : _palette.HudAccent);

            _status.Title.Set(TitleLine(), _palette.HudAccent);

            // The clock as a length above the clock as a number: the bar is what a player reads without
            // taking their eyes off the map, and the line underneath says what the wait is for.
            DrawHealthBar();
            DrawWaveClock();

            _status.News.Set(WaveNews(), _palette.HudText);
            UpdateComposition();

            string production = ProductionLine(jams, starved, total);
            bool productionNews = production != null;
            _status.Production.Show(productionNews);
            if (productionNews) _status.Production.Set(production, ProductionColour(jams, starved));

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

        /// <summary>The production line, said only when something is wrong: a jam first - it stops the run,
        /// not just one machine - then a gun that cannot shoot, then, once a wave is walking in, the absence
        /// of anything to shoot it with. When the defence is fed and the belts are clear the line stays
        /// silent: "all clear" printed every second is a line nobody reads. Null means no news, and the line
        /// is hidden.</summary>
        private string ProductionLine(int jams, bool starved, int total)
        {
            if (jams > 0)
                return jams + " jammed segment" + (jams == 1 ? "" : "s") + "   ·   right-click one to clear it";
            if (starved)
                return "a starved gun's ammo icon is grey   ·   its line is not feeding it";

            WaveDirector waves = _world.Waves;
            if (total == 0 && (waves.Phase == WavePhase.Running ||
                    (waves.Phase == WavePhase.Intermission && waves.CurrentWave > 0)))
                return "no turrets standing - the wave walks in unopposed";

            return null;
        }

        private Color ProductionColour(int jams, bool starved)
            => jams > 0 || starved ? _palette.HudWarn : _palette.HudText;

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

            if (outOfView.HasJams)
                AppendSeparator(text)
                    .Append(outOfView.Jams)
                    .Append(outOfView.Jams == 1 ? " jam" : " jams")
                    .Append(" out of view");

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

        /// <summary>The bill of materials as chips: which enemies the wave being built for is made of, and -
        /// drawn, not typed - the shape each is weak to. The next wave while one is waited on, the one being
        /// fought while a wave runs; "what do I need" is the same question, only the answer differs.
        ///
        /// The row is laid out by measurement, once per change, at whatever widths the words ask for. A wave
        /// too varied to fit drops the names and keeps the numbers and marks - the count and shape are the
        /// decision, the name the courtesy.</summary>
        private void UpdateComposition()
        {
            WaveDirector waves = _world.Waves;
            WaveDefinition wave = waves.Phase == WavePhase.Running
                ? _world.Map.Waves[waves.CurrentWave - 1]
                : waves.Phase == WavePhase.Held || waves.Phase == WavePhase.Intermission
                    ? waves.NextWaveDefinition
                    : null;

            var key = new StringBuilder().Append((int)waves.Phase).Append(':').Append(waves.NextWave);
            if (wave != null)
                for (int i = 0; i < _world.Content.EnemyKinds.Length; i++)
                    key.Append(':').Append(wave.CountOf(_world.Content.EnemyKinds[i]));

            string signature = key.ToString();
            if (signature == _compKey) return;
            _compKey = signature;

            if (wave == null)
            {
                HideChips();
                return;
            }

            var entries = new List<ChipEntry>();
            ContentDatabase content = _world.Content;
            for (int i = 0; i < content.EnemyKinds.Length; i++)
            {
                EnemyKind kind = content.EnemyKinds[i];
                int count = wave.CountOf(kind);
                if (count == 0) continue;
                entries.Add(new ChipEntry(count, content.Enemy(kind).Name, content.Enemy(kind).Weakness));
            }

            if (entries.Count == 0 || !LayoutChips(entries, withNames: true))
                LayoutChips(entries, withNames: false);
        }

        /// <summary>Lay the chips left to right across the card's row. False when they do not fit -
        /// the caller tries again without the names.</summary>
        private bool LayoutChips(List<ChipEntry> entries, bool withNames)
        {
            if (entries.Count > ChipCapacity) entries = entries.GetRange(0, ChipCapacity);

            float x = 14f * _scale;

            for (int i = 0; i < ChipCapacity; i++)
            {
                Chip chip = _status.Chips[i];
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

                if (x + chipWidth > _status.Root.rect.width - 14f * _scale)
                {
                    if (!withNames)
                    {
                        HideChips();
                        return false;   // even the numbers do not fit: the row stays empty rather than clipped
                    }
                    return false;       // too wide with names - the caller lays the compact row
                }

                chip.Back.Show(true);
                chip.Count.Show(true);
                HudUi.TopLeft(chip.Back.Rect, x - 5f * _scale, 92f * _scale, chipWidth + 10f * _scale, 24f * _scale);
                HudUi.TopLeft(chip.Count.Rect, x, 96f * _scale, countWidth + 2f * _scale, 18f * _scale);
                chip.Count.SetColour(_palette.HudText);

                if (withNames)
                {
                    HudUi.TopLeft(chip.Name.Rect, x + countWidth + 3f * _scale, 96f * _scale,
                        nameWidth + 2f * _scale, 18f * _scale);
                    chip.Name.SetColour(_palette.MachineIdle);
                }

                if (chip.Icon != null)
                {
                    HudIcons.Icon icon = _icons.Shape(entry.Weakness);
                    if (icon.Valid)
                    {
                        chip.Icon.texture = icon.Texture;
                        chip.Icon.uvRect = icon.Uv;
                        HudUi.TopLeft(chip.Icon.rectTransform, x + chipWidth - 13f * _scale,
                            97f * _scale, 13f * _scale, 13f * _scale);
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
            for (int i = 0; i < ChipCapacity; i++) HideChip(_status.Chips[i]);
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
                    WaveDefinition current = _world.Map.Waves[waves.CurrentWave - 1];
                    if (current.Total <= 0) return 1f;
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return Mathf.Clamp01(1f - left / (float)current.Total);

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its clock bar");
            }
        }

        private string TitleLine()
        {
            int waveCount = _world.Map.Waves.Length;
            string map = _world.Map.Name;
            string at = _campaign != null && _campaign.MapCount > 1
                ? "map " + (_campaign.MapIndex + 1) + "/" + _campaign.MapCount + "  ·  "
                : string.Empty;

            string head = "FACET  ·  " + at + map;

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
                    return head + "  ·  wave " + waves.NextWave + " of " + waveCount + " next";
                case WavePhase.Running:
                    return head + "  ·  wave " + waves.CurrentWave + " of " + waveCount;
                case WavePhase.Over:
                    // The tick that finishes the table also ends the run, so a playing map never shows
                    // this; the clock still owes the title an answer rather than a default.
                    return head + "  ·  every wave fought";
                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its title line");
            }
        }

        /// <summary>What the wave clock is doing, in the sentence the bar above it is the length of.</summary>
        private string WaveNews()
        {
            if (_world.Status != GameStatus.Playing) return "press R to restart";

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
                        + (_world.Paused ? string.Empty : "   ·   N starts it now");

                case WavePhase.Running:
                {
                    WaveDefinition current = _world.Map.Waves[waves.CurrentWave - 1];
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return left + " of " + current.Total + " enemies left in this wave"
                        + (_world.Paused ? "   ·   paused" : string.Empty);
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its wave line");
            }
        }

        // ------------------------------------------------------------------ controls card

        /// <summary>The controls cluster's one moving part is the pause button's word; the help card is
        /// opened and closed by its button and by <c>H</c>, which the HUD reads itself - a view concern, the
        /// way the camera rig owns <c>C</c>. Nothing here is simulation state.</summary>
        private void UpdateControls()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.hKey.wasPressedThisFrame) SetHelpOpen(!_helpOpen);

            if (!_controlsDirty && _controlsPaused == _world.Paused) return;
            _controlsDirty = false;
            _controlsPaused = _world.Paused;

            _controls.Pause.Label.Set(_world.Paused ? ">" : "II");
        }

        private void SetHelpOpen(bool open)
        {
            if (_helpOpen == open) return;
            _helpOpen = open;
            _help.Root.gameObject.SetActive(open);
        }

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
            yield return "RMB deletes, or clears a jam";
            yield return "Esc reads  ·  C goes to the Core";
            yield return "N the wave  ·  Space  ·  R restart";
        }

        // ------------------------------------------------------------------ build bar

        private void UpdateBuildBar()
        {
            _hoveredTile = -1;

            BuildKind? selected = _world.SelectedKind;

            for (int i = 0; i < _bar.Tiles.Count; i++)
            {
                BuildTile tile = _bar.Tiles[i];
                bool hovered = tile.Button.Hovered;
                if (hovered) _hoveredTile = i;

                bool selectedTile = tile.Kind == selected;
                bool affordable = _world.Economy.CanAfford(tile.Kind);

                if (tile.Selected != selectedTile || tile.Hovered != hovered ||
                    tile.Affordable != affordable)
                {
                    tile.Selected = selectedTile;
                    tile.Hovered = hovered;
                    tile.Affordable = affordable;
                    DressBuildTile(tile);
                }

                // The line along the tile's bottom says how far from affordable the bank is: nine tenths of
                // the way is a belt to lay now, a tenth of the way is a plan for later.
                if (!tile.Affordable && _world.Content.Machine(tile.Kind).Cost > 0)
                    tile.Afford.Set(Mathf.Clamp01(_world.Economy.Circles / (float)_world.Content.Machine(tile.Kind).Cost),
                        _palette.CursorNoFunds);
                else tile.Afford.Set(0f, Color.clear);
            }

            // The cost's shape sits glued to the number's left edge, and the number's width is known
            // only once the text has laid itself out - so the first frames place it as they learn it.
            for (int i = 0; i < _bar.Tiles.Count; i++) PlaceCostIcon(_bar.Tiles[i]);
        }

        /// <summary>One building as a button: hotkey, cost, name and role over a stripe tinted by that role.
        /// Selected is an accent outline and tinted fill, unaffordable greys the name and turns the cost
        /// amber, and the pointer lightens the tile. A click selects the kind through the input latch - it
        /// never places anything, so a stray click can only change what is selected.</summary>
        private void DressBuildTile(BuildTile tile)
        {
            Color category = CategoryColor(tile.Kind);

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

        /// <summary>The cost's shape, once the cost's own width is known: the picture just outside the
        /// number's left edge, where a cost is a mark rather than a sentence.</summary>
        private void PlaceCostIcon(BuildTile tile)
        {
            if (tile.CostIcon == null || tile.CostPlaced) return;
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
                    RefreshBuildInfo(kind);
                }

                // The facing is the ghost's, and Q and E move it: the one line on the build card that
                // answers to the keyboard rather than to the catalogue.
                int facing = (int)_world.PlacementDirection;
                if (facing != _infoFacing)
                {
                    _infoFacing = facing;
                    _info.Facing.Set("facing " + MachineReading.DirName(_world.PlacementDirection),
                        _palette.MachineIdle);
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

            _info.BuildStripe.Set(category);
            _info.BuildTitle.Set(def.Name, _palette.HudText);
            _info.BuildRight.Set(
                def.Cost + " " + Plural(Glyph(ShapeType.Circle), def.Cost) + "   " + RoleOf(kind),
                _palette.MachineIdle);

            _info.Blurb.Set(Blurb(kind), _palette.HudText);

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

            _info.Facing.Set("facing " + MachineReading.DirName(_world.PlacementDirection),
                _palette.MachineIdle);
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

            if (_world.Machines.TryGetSnapshot(cell, out MachineSnapshot machine))
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

                InspectLine(MachineReading.FacingNote(def, machine, _world.Content), _palette.MachineIdle);
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
                    // A jam arrives with the shape that caused it on the belt; name that shape when it is
                    // there rather than asking the content table about "nothing", which has no glyph.
                    string culprit = belt.Item == ShapeType.None ? string.Empty : " by " + Glyph(belt.Item);
                    InspectLine("JAMMED" + culprit + " - the wrong shape for whatever this segment feeds",
                        _palette.HudWarn);
                }
                else if (belt.HasItem)
                    InspectLine("carrying " + Glyph(belt.Item) + " · " + Mathf.RoundToInt(belt.Progress * 100f) +
                        "% across", _palette.HudText);
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
                InspectLine("circles belted into it become spendable", _palette.MachineIdle);
                InspectLine("losing it loses the run", _palette.MachineIdle);
                FinishInspect();
                return;
            }

            // The ground is read off the terrain layer rather than the tile kind, so an ore card has a shape
            // and a rock card its kind. Whether the cell is worth reading at all is the simulation's one answer
            // (<see cref="SimWorld.CanInspect"/> - the same rule the click and the cursor's marker read), not
            // a copy restated here.
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
            ShapeType shape = ore ? _world.Terrain.OreAt(cell) : ShapeType.None;
            Color oreColour = _palette.ShapeColor(shape);

            string title = doorway < 0 ? "Ore patch" : ore ? "Entry point · ore" : "Entry point";
            InspectHeader(title, doorway < 0 ? oreColour : _palette.SpawnMarker, cell.ToString());

            if (doorway >= 0)
            {
                InspectLine(WavesAt(doorway), _palette.SpawnMarker);
                InspectLine("enemies enter on this tile and walk at the Core", _palette.MachineIdle);
            }

            if (!ore) return;

            InspectLine("yields " + Glyph(shape) + " - only a drill may be built on it", oreColour);

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
                    ? "JAMMED" + (belt.Item == ShapeType.None ? "" : " by " + Glyph(belt.Item)) +
                      " - right-click clears it"
                    : belt.HasItem
                        ? "carrying " + Glyph(belt.Item) + " · " + Mathf.RoundToInt(belt.Progress * 100f) + "% across"
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
                ShapeType shape = _world.Terrain.OreAt(cell);
                TooltipHeader("Ore patch", _palette.ShapeColor(shape));
                TooltipLine("yields " + Glyph(shape) + " - a drill mines it", _palette.ShapeColor(shape));
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
        /// An ended run also reports itself - shots fired, circles banked - so "do better" has numbers.</summary>
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
                        text = _world.Map.Name.ToUpperInvariant() + " CLEARED\n" + StatsLine() +
                               (_campaign != null && _campaign.MapCount > 1 ? "\ncampaign complete · R to play it again" : "\nR to restart");
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
                Plural(ShapeType.Circle, _world.Economy.TotalBanked) + " banked";
        }

        // ------------------------------------------------------------------ the buttons' dressing

        /// <summary>The buttons whose look is the generic one: a filled rectangle that lightens under the
        /// pointer and takes an accent edge while hovered, drawn from the palette rather than a skin so it
        /// matches the cards. The world never sees the click because <see cref="PointerOverHud"/> claimed it
        /// for the UI.</summary>
        private void UpdateButtons()
        {
            Dress(_controls.Pause, ButtonFill);
            Dress(_controls.Help, ButtonFill);
            Dress(_help.Close, ButtonFill);
            Dress(_help.Restart, ButtonFill);
            Dress(_info.TurnLeft, ButtonFill);
            Dress(_info.TurnRight, ButtonFill);
            if (_info.Inspect.Root.gameObject.activeSelf) Dress(_info.Inspect, ButtonFill);

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

        /// <summary>Which colour files which role under. A role nobody has coloured is an error, not
        /// a quiet slide into "transport" - the discipline the badge and draw dispatches keep.</summary>
        private Color CategoryColour(MachineRole role)
        {
            switch (role)
            {
                case MachineRole.Defence: return _palette.HudWarn;
                case MachineRole.Mining:
                case MachineRole.Processing: return _palette.CircleShape;
                case MachineRole.Routing: return _palette.HudAccent;
                case MachineRole.Transport: return _palette.HalfCircleShape;   // belts and pipes
                case MachineRole.Wall: return _palette.HalfCircleShape;
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

        private string Glyph(ShapeType shape) => _world.Content.Shape(shape).Glyph;

        /// <summary>The shape's word, pluralised the way the sentence reading it needs: "3 circle banked" is
        /// the HUD's mistake to avoid, not the table's.</summary>
        private string Plural(ShapeType shape, int count) => Plural(Glyph(shape), count);

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
            public Chip[] Chips;
            public HudText Production;
            public HudText Threat;
            public HudButton WaveButton;
        }

        private sealed class ControlsPanel
        {
            public RectTransform Root;
            public HudButton Pause;
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

        /// <summary>One entry of the wave's bill of materials: how many, of what, weak to what.</summary>
        private readonly struct ChipEntry
        {
            public readonly int Count;
            public readonly string Name;
            public readonly ShapeType Weakness;

            public ChipEntry(int count, string name, ShapeType weakness)
            {
                Count = count;
                Name = name;
                Weakness = weakness;
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
    }
}
