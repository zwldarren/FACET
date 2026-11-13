using System;
using System.Collections.Generic;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Facet.Game
{
    /// <summary>The game's shell: the title screen, the pause menu that stands over a run, the level select and
    /// the help card. States of one runtime-built canvas rather than separate scenes, made in code from the same
    /// <see cref="HudUi"/> widgets the HUD uses, with the screens' card geometry authored as prefabs under
    /// <c>Resources/Hud</c>.
    ///
    /// The shell owns the screen while it is open: the driver stops sampling the devices and ticking the run for as
    /// long as <see cref="Open"/> stands, and the HUD and camera rig are stood down with it, so no click or key
    /// aimed at the menu reaches the simulation. The run behind is held, not rebuilt, and a map picked from the
    /// list is a fresh run.
    ///
    /// The pause menu is the in-run hub (Esc, M, and the HUD's menu button); the map list opened from it goes back
    /// to it. The two doors that throw a run away - the title and restart - are armed by one press and taken by the
    /// next, and Escape disarms before either leaves: the unsaved factory is what a misfire would cost, not progress,
    /// which is written when a map is cleared.
    ///
    /// No screen is torn down inside the press that asked for it: every change goes through
    /// <see cref="RequestRebuild"/> - see <see cref="HudButton.Dispatching"/> for why.</summary>
    [DefaultExecutionOrder(210)]   // after the HUD (200): the shell's keys are read with the run already held
    public sealed class ShellView : MonoBehaviour
    {
        private enum Mode { Title, Levels, Help, Pause }

        private Palette _palette;
        private MapCatalog _catalog;
        private CampaignState _campaign;

        /// <summary>The shell's four doors back into the driver. The driver owns every transition; the shell only
        /// asks.</summary>
        private Action<int, bool> _play;
        private Action _resume;
        private Action _restart;
        private Action _toTitle;

        private RectTransform _canvasRoot;

        private Mode _mode = Mode.Title;

        /// <summary>The frame the current mode was built in. The keys that opened the shell are still "pressed this
        /// frame" when its own update runs - the HUD's M opens the menu, and the shell's M would close it again the
        /// same frame - so a mode just built ignores its keys until the next one.</summary>
        private int _shownAtFrame = -1;

        /// <summary>Is a rebuild waiting for this frame's update? Set by a button's press, which may not tear the
        /// card down while uGUI is still walking it.</summary>
        private bool _rebuildPending;

        /// <summary>Is the shell on screen? While it stands the devices are the shell's, the run is held, and
        /// nothing ticks.</summary>
        public bool Open { get; private set; }

        /// <summary>Which face the shell is showing: the main menu (the title) answers true; the pause menu, its map
        /// list and its help card all answer false.</summary>
        public bool AtTitle => _mode == Mode.Title;

        /// <summary>Whether the shell was opened over a run rather than at boot or a return to the title. It decides
        /// what the back button does and says, and which state word a level row wears.</summary>
        private bool _inRun;

        /// <summary>Is the main-menu door armed? The first press asks ("abandon this run?"), the second takes it;
        /// anything else that changes the screen cancels. Every irreversible press on this card is therefore always
        /// two.</summary>
        private bool _confirmQuit;

        /// <summary>Is the restart door armed? Same discipline as the main-menu door, and for the same reason: a
        /// restart discards the run as completely as leaving does. Only one door can be armed at a time.</summary>
        private bool _confirmRestart;

        private float _scale = -1f;
        private int _screenWidth = -1;
        private int _screenHeight = -1;

        /// <summary>The buttons dressed for hover each open frame, gathered as built: the shell's buttons are the
        /// HUD's generic one, so one dressing pass covers every one of them.</summary>
        private readonly List<HudButton> _buttons = new List<HudButton>();

        /// <summary>Locked rows dress themselves dim and take no press; remembered apart so the hover pass can skip
        /// them without a flag on the row.</summary>
        private readonly List<HudButton> _locked = new List<HudButton>();

        /// <summary>The title's one filled action: a title with three identical buttons has no door at all.</summary>
        private readonly List<HudButton> _primary = new List<HudButton>();

        // ------------------------------------------------------------------ wiring and lifecycle

        /// <summary>Point the shell at the palette, the campaign and its catalog, and hand it the four doors back
        /// into the driver. The canvas is built once; every open rebuilds the content against the progress of the
        /// moment, which is what keeps a level list honest after a run has cleared something.</summary>
        public void Initialize(Palette palette, MapCatalog catalog, CampaignState campaign,
            Action<int, bool> play, Action resume, Action restart, Action toTitle)
        {
            _palette = palette;
            _catalog = catalog;
            _campaign = campaign;
            _play = play;
            _resume = resume;
            _restart = restart;
            _toTitle = toTitle;

            if (_canvasRoot == null) BuildCanvas();
        }

        private void Update()
        {
            if (_palette == null || _canvasRoot == null || !Open) return;

            // The screen a press asked for is built here, not inside the press: the event system has finished
            // walking the card that was clicked, so replacing it costs no exception and leaves no half-finished
            // press to land on the new screen's widgets.
            if (_rebuildPending) Rebuild();

            RefreshScale();
            ReadKeys();
            DressButtons();
        }

        private void OnDestroy()
        {
            // The shared font and pixel sprite are the HUD's lifecycle (see HudView.OnDestroy).
            _canvasRoot = null;

            _buttons.Clear();
            _locked.Clear();
            _primary.Clear();
        }

        // ------------------------------------------------------------------ the driver's doors

        /// <summary>Open on the title screen, rebuilt from the progress of this moment.</summary>
        public void ShowTitle()
        {
            _inRun = false;
            SetMode(Mode.Title);
        }

        /// <summary>Open the pause menu over a running world: resume, restart, the map list, how to play, and the two
        /// ways out. Esc, M and the HUD's menu button all ask for this door.</summary>
        public void ShowPause()
        {
            _inRun = true;
            SetMode(Mode.Pause);
        }

        /// <summary>
        /// The main-menu door, armed by one press and taken by the next: the two clicks that throw a run away are
        /// deliberately as far from the resume button as the card allows. Progress is not at stake - cleared maps
        /// were written when they were cleared.
        /// </summary>
        public void RequestMainMenu()
        {
            if (_mode != Mode.Pause) return;

            if (_confirmQuit)
            {
                _confirmQuit = false;
                _toTitle?.Invoke();
                return;
            }

            _confirmQuit = true;
            _confirmRestart = false;
            RequestRebuild();
        }

        /// <summary>
        /// The restart door, armed the same way and for the same reason: a restart is the world's own reset, so it
        /// discards the run as completely as the main-menu door does.
        /// </summary>
        public void RequestRestart()
        {
            if (_mode != Mode.Pause) return;

            if (_confirmRestart)
            {
                _confirmRestart = false;
                _restart?.Invoke();
                return;
            }

            _confirmRestart = true;
            _confirmQuit = false;
            RequestRebuild();
        }

        /// <summary>Close the shell. The run behind a pause menu is the driver's to hand back; the shell only goes
        /// away.</summary>
        public void Close()
        {
            Open = false;
            // A screen change still queued belongs to a shell that is no longer on screen.
            _rebuildPending = false;
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(false);
        }

        private void SetMode(Mode mode)
        {
            _mode = mode;
            _confirmQuit = false;
            _confirmRestart = false;
            RequestRebuild();
            Open = true;
            _shownAtFrame = Time.frameCount;
            if (_canvasRoot != null) _canvasRoot.gameObject.SetActive(true);
        }

        /// <summary>
        /// Build the screen of the moment, or queue it for this frame's update when the ask came from a press. A press
        /// is dispatched from inside the event system's walk of the card, and both uGUI and the input module still
        /// hold the widget it was delivered to when the callback returns: tearing it down there throws out of
        /// <c>ExecuteEvents.ExecuteHierarchy</c> and out of <c>InputSystemUIInputModule.ProcessPointer</c>, and the
        /// unfinished press is dispatched again next frame - at whatever the new screen put under the pointer.
        ///
        /// Deferring is free: this update runs after the event system's and before anything is drawn, so the new
        /// screen is the one on screen in the frame the button was pressed. A rebuild asked for anywhere else - the
        /// driver's doors, a key, a resize, a test - still happens immediately.
        /// </summary>
        private void RequestRebuild()
        {
            if (HudButton.Dispatching)
            {
                _rebuildPending = true;
                return;
            }

            Rebuild();
        }

        // ------------------------------------------------------------------ building the canvas

        private void BuildCanvas()
        {
            HudUi.EnsureEventSystem(transform);

            var canvasGo = new GameObject("ShellCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD's own canvas (100): the shell covers the screen even where a card of the
            // HUD's would reach, so a menu click can never land on a button that is not the menu's.
            canvas.sortingOrder = 200;

            _canvasRoot = (RectTransform)canvasGo.transform;
        }

        /// <summary>Tear the content down and build it again for the mode and progress of the moment. The one heavy
        /// pass the shell has - the fonts need the scale inside their point size to stay crisp.</summary>
        private void Rebuild()
        {
            _rebuildPending = false;
            _scale = HudLayout.Scale(Screen.height);
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;

            for (int i = _canvasRoot.childCount - 1; i >= 0; i--)
                DestroyImmediate(_canvasRoot.GetChild(i).gameObject);

            _buttons.Clear();
            _locked.Clear();
            _primary.Clear();

            BuildBackdrop();

            switch (_mode)
            {
                case Mode.Title: BuildTitle(); break;
                case Mode.Levels: BuildLevels(); break;
                case Mode.Help: BuildHelp(); break;
                case Mode.Pause: BuildPause(); break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Mode), _mode, "a shell mode must be built");
            }
        }

        private void BuildBackdrop()
        {
            // Opaque, not the panel's 96.5 %: at 3.5 % of a bright map the cards and the Core showed through the
            // title exactly where the eye was trying to read the wordmark.
            Color ink = _palette.HudPanel;
            ink.a = 1f;

            HudBox backdrop = HudUi.AdoptBox(InstantiatePanel(BackdropPrefab, "backdrop"), ink);
            // The one panel that does take clicks: a click that misses every button lands here rather than on the
            // map behind it.
            backdrop.Image.raycastTarget = true;
        }

        // ------------------------------------------------------------------ the title screen

        /// <summary>A title is an identity, not a line of HUD: the four-step scale stops at the stockpile's 28, and a
        /// title set at a data size was the smallest thing on its own screen.</summary>
        private const float WordmarkSize = 64f;

        /// <summary>
        /// The main menu: the game's name, its one figure, the doors and the footer. <see cref="TitlePrefab"/> owns
        /// the band, the column, the subtitle and the footer; what stays here is the words, the wiring and the figure.
        /// The band is a fraction of the screen's height, which an anchor can say and a design unit cannot
        /// (<see cref="TitleNameTop"/>).
        /// </summary>
        private void BuildTitle()
        {
            RectTransform root = InstantiatePanel(TitlePrefab, "title");
            HudUi.ScaleDesignUnits(root, _scale);

            BuildTitleMotif(root);

            HudText name = AdoptLabel(root, "name", WordmarkSize, FontStyle.Bold, TextAnchor.MiddleCenter);
            // The one place the HUD's tracking rule widens: a wordmark is drawn, not set, and 64-point type
            // at the labels' 9 % reads as a line of text that happens to be big.
            name.Text.characterSpacing = 16f;
            name.Set("FACET", _palette.HudText);

            HudText subtitle = AdoptLabel(root, "subtitle", HudType.Body, FontStyle.Normal,
                TextAnchor.UpperCenter, wrap: true);
            subtitle.Set("an enemy's weakness is a shape, and what your factory builds is what your guns fire",
                _palette.MachineIdle);

            BindMenuButton(root, "play", PlayLabel(), PressPlay, primary: true);
            BindMenuButton(root, "maps", "the maps  ·  M", ShowLevelScreen);

            // No "how to play" door any more: the first map is the tutorial, and it teaches by doing. Its slot
            // goes to the door below it, so the column has no gap.
            RectTransform howto = HudUi.Child(root, "howto");
            Vector2 howtoSlot = howto.anchoredPosition;
            howto.gameObject.SetActive(false);

#if !UNITY_WEBGL || UNITY_EDITOR
            // A quit is a host's luxury: a WebGL build has no tab to close from inside, so the button
            // is a desktop one and says nothing where it cannot mean it.
            BindMenuButton(root, "quit", "quit", Application.Quit);
            HudUi.Child(root, "quit").anchoredPosition = howtoSlot;
#else
            HudUi.Child(root, "quit").gameObject.SetActive(false);
#endif

            HudText footer = AdoptLabel(root, "footer", HudType.Micro, FontStyle.Normal, TextAnchor.LowerCenter);
            footer.Set("a factory-defence prototype  ·  github.com/zwldarren/FACET",
                HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.45f));
        }

        /// <summary>
        /// The title's ground: a square and the diamond inside it, drawn in the world's own ink at a watermark. It is
        /// the game's one figure and the game's own name - a facet is a square seen at the angle its cut makes - and
        /// it costs two generated outlines rather than an asset.
        ///
        /// It is the one thing on the title an asset cannot place: the square is 62 % of the screen's short side, a
        /// fraction of a fraction. <see cref="TitlePrefab"/> carries the two outlines and the diamond's rotation;
        /// the square it is drawn in is this call's.
        /// </summary>
        private void BuildTitleMotif(RectTransform root)
        {
            float side = Mathf.Min(Screen.width, Screen.height) * 0.62f;
            float y = (TitleMotifCentre - 0.5f) * Screen.height;
            Color ink = new Color(_palette.HudText.r, _palette.HudText.g, _palette.HudText.b, 0.05f);

            RectTransform square = HudUi.Child(root, "motif.square");
            HudUi.Place(square, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(side, side));
            HudUi.AdoptBorder(HudUi.Child(square, "line"), ink, 6f, 1f);

            RectTransform diamond = HudUi.Child(root, "motif.diamond");
            float inner = side * 0.70f;
            HudUi.Place(diamond, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, y), new Vector2(inner, inner));
            HudUi.AdoptBorder(HudUi.Child(diamond, "line"), ink, 4f, 1f);
        }

        /// <summary>As a fraction of the screen's height from its bottom edge: a little under the middle, so the
        /// wordmark above it and the column below it are both in its field.</summary>
        private const float TitleMotifCentre = 0.44f;

        /// <summary>From where the campaign stands: a fresh profile plays, a started campaign continues at its
        /// frontier map, and a finished one has nothing left but the last map played endless.</summary>
        private string PlayLabel()
        {
            if (_campaign.ClearedCount >= _campaign.MapCount)
                return "endless on " + _catalog.At(_campaign.MapCount - 1).Name + "  ·  Enter";
            if (_campaign.ClearedCount == 0) return "play  ·  Enter";

            return "continue, map " + (_campaign.ClearedCount + 1) + " of " + _campaign.MapCount + "  ·  Enter";
        }

        /// <summary>One of the title's doors: the asset's place, the run's words.</summary>
        private HudButton BindMenuButton(RectTransform parent, string name, string label, Action pressed,
            bool primary = false)
        {
            HudButton button = HudUi.AdoptButton(HudUi.Child(parent, name), label, FontSize(HudType.Label),
                primary ? _palette.HudPanel : _palette.HudText, pressed);
            _buttons.Add(button);
            if (primary) _primary.Add(button);
            return button;
        }

        private void PressPlay()
        {
            // The frontier is the first map the campaign has not cleared; with the whole campaign
            // cleared, the same button is the endless entry - the last map, unbounded.
            bool endless = _campaign.ClearedCount >= _campaign.MapCount;
            int index = Mathf.Min(_campaign.ClearedCount, _campaign.MapCount - 1);
            _play?.Invoke(index, endless);
        }

        private void ShowLevelScreen() => SetMode(Mode.Levels);

        private void OpenHelp() => SetMode(Mode.Help);

        // ------------------------------------------------------------------ the pause menu

        /// <summary>In design units: the width is the title's column plus the card's own insets, so both screens put
        /// their buttons where the eye already expects them.</summary>
        private const float PauseCardWidth = 380f;
        private const float PauseFirstButton = 66f;

        /// <summary>
        /// The in-run screen and the game's one hub: resume, restart, the map list, how to play, and the two ways out
        /// - plus, in a fullscreen web player, the way back to a window (see <see cref="PlayerPage"/>).
        /// <see cref="PausePrefab"/> is the card and its column of doors; this says which doors are up, what the
        /// armed ones say, and where the run is.
        ///
        /// The run behind it is held by the driver, not paused in the world: the shell never touches
        /// <c>World.Paused</c>, so a player who paused before opening the menu gets their pause back on resume.
        /// A window too short to carry the authored column compacts the step rather than clipping the two ways out.
        /// </summary>
        private void BuildPause()
        {
            RectTransform root = InstantiatePanel(PausePrefab, "pause");
            HudUi.ScaleDesignUnits(root, _scale);
            RectTransform card = HudUi.Child(root, "card");

#if !UNITY_WEBGL || UNITY_EDITOR
            const int fixedDoors = 5;   // resume, restart, maps, main menu, quit
#else
            const int fixedDoors = 4;   // a WebGL build has no tab to close from inside
#endif
            // ...plus the one row that exists on one screen only: the way out of the browser's fullscreen. The page
            // hands Escape to the game while the frame is fullscreen (see PlayerPage), so a player who cannot find
            // this row has no way back.
            bool fullscreen = PlayerPage.InFullscreen;
            int doors = fixedDoors + (fullscreen ? 1 : 0);

            float cardWidth = Mathf.Min(PauseCardWidth, Screen.width / _scale - 2f * HudLayout.Margin);

            // The step never goes below a door's own height, so the extreme case overlaps nothing a player can still
            // reach.
            float available = Mathf.Max(0f, Screen.height / _scale - 2f * HudLayout.Margin);
            float step = Mathf.Min(ButtonStep, Mathf.Max(34f, (available - PauseFirstButton - 8f) / doors));
            float cardHeight = Mathf.Min(PauseFirstButton + doors * step + 8f, available);

            HudUi.TopCentre(card, (Screen.height - cardHeight * _scale) * 0.5f, cardWidth * _scale,
                cardHeight * _scale);

            AdoptCard(card, "card", CardRadius);

            HudText header = AdoptLabel(card, "header", HudType.Body, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            header.Set("PAUSED", _palette.HudText);

            MapDefinition map = _catalog.At(_campaign.MapIndex);
            HudText where = AdoptLabel(card, "where", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft);
            where.Set("map " + (_campaign.MapIndex + 1) + " of " + _campaign.MapCount + "  ·  " + map.Name,
                HudInk.Muted(_palette));

            // Armed is a state of one button, not a second screen: the label says what the next press
            // would do, in the warning ink, and every other key or door on the card disarms it.
            BindCardButton(card, "resume", "resume  ·  Esc", () => _resume?.Invoke(), primary: true);
            BindCardButton(card, "restart",
                _confirmRestart ? "restart this run?  ·  click again" : "restart this run", RequestRestart,
                labelColour: _confirmRestart ? HudInk.Warning(_palette) : null);
            BindCardButton(card, "maps", "the maps", ShowLevelScreen);
            HudUi.Child(card, "howto").gameObject.SetActive(false);   // the tutorial map teaches; no help door
            BindCardButton(card, "mainmenu",
                _confirmQuit ? "abandon this run?  ·  click again" : "main menu", RequestMainMenu,
                labelColour: _confirmQuit ? HudInk.Warning(_palette) : null);

            if (fullscreen)
            {
                HudUi.Child(card, "windowed").gameObject.SetActive(true);
                BindCardButton(card, "windowed", "leave fullscreen", PlayerPage.LeaveFullscreen);
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            BindCardButton(card, "quit", "quit", Application.Quit);
#else
            HudUi.Child(card, "quit").gameObject.SetActive(false);
#endif

            // Always laid out here now: the authored column still has the help door's row in it, and only the
            // doors that are up are stepped.
            LayoutPauseColumn(card, cardWidth, step, fullscreen);
        }

        /// <summary>Laid out here for the two cases the asset cannot have authored: a compacted step and the
        /// fullscreen page's extra row. Only the doors that are up are stepped, so a WebGL build's missing quit leaves
        /// no gap in the column.</summary>
        private void LayoutPauseColumn(RectTransform card, float cardWidth, float step, bool fullscreen)
        {
            float width = (cardWidth - 32f) * _scale;
            float y = PauseFirstButton;
            string[] doors = fullscreen
                ? new[] { "resume", "restart", "maps", "howto", "mainmenu", "windowed", "quit" }
                : new[] { "resume", "restart", "maps", "howto", "mainmenu", "quit" };

            for (int i = 0; i < doors.Length; i++)
            {
                Transform door = card.Find(doors[i]);
                if (door == null || !door.gameObject.activeSelf) continue;

                HudUi.TopLeft((RectTransform)door, 16f * _scale, y * _scale, width, 34f * _scale);
                y += step;
            }
        }

        /// <summary>One door in a card's column: the asset's place and width, the run's words.</summary>
        private HudButton BindCardButton(RectTransform card, string name, string label, Action pressed,
            bool primary = false, Color? labelColour = null)
        {
            HudButton button = HudUi.AdoptButton(HudUi.Child(card, name), label, FontSize(HudType.Label),
                labelColour ?? (primary ? _palette.HudPanel : _palette.HudText), pressed);
            _buttons.Add(button);
            if (primary) _primary.Add(button);
            return button;
        }

        // ------------------------------------------------------------------ the level select

        private void BuildLevels()
        {
            RectTransform levels = InstantiatePanel(LevelsPrefab, "levels");
            HudUi.ScaleDesignUnits(levels, _scale);
            RectTransform card = HudUi.Child(levels, "card");

            // The rows must all fit: a campaign longer than the screen shrinks its rows rather than clipping the
            // tail off, because a map the list cannot show is a map the player cannot pick. Everything below is in
            // design units and scaled once, at the card - a pixel term that rode through the final * _scale would
            // make the card _scale times taller than the screen.
            float header = LevelsHeader;
            float foot = LevelsFoot;
            float available = Screen.height / _scale - 2f * HudLayout.Margin - header - foot;
            float rows = Mathf.Max(1, _catalog.Count);
            float fit = (available - (rows - 1) * LevelRowGap) / rows;
            float rowHeight = Mathf.Min(LevelRowHeight, Mathf.Max(LevelRowFloor, fit));

            // The gap shrinks with the rows once they reach the floor, so a campaign long enough to bind it still
            // fits the screen instead of overflowing the card.
            float rowGap = fit < LevelRowFloor && rows > 1
                ? Mathf.Max(0f, (available - rows * rowHeight) / (rows - 1))
                : LevelRowGap;

            float cardWidthDesign = Mathf.Min(LevelsWidth, Screen.width / _scale - 2f * HudLayout.Margin);
            float cardWidth = cardWidthDesign * _scale;
            float rowsBottom = header + _catalog.Count * rowHeight
                + Mathf.Max(0, _catalog.Count - 1) * rowGap;
            float cardHeight = (rowsBottom + foot) * _scale;

            HudUi.TopCentre(card, (Screen.height - cardHeight) * 0.5f, cardWidth, cardHeight);
            AdoptCard(card, "card", CardRadius);

            HudText title = AdoptLabel(card, "title", HudType.Body, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            title.Set("MAPS", _palette.HudText);

            HudText count = AdoptLabel(card, "count", HudType.Micro, FontStyle.Normal, TextAnchor.UpperRight);
            count.Set(_campaign.ClearedCount + " of " + _campaign.MapCount + " cleared",
                HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.45f));

            // The one way out, top-right where the help card's close button sits. Over a run it goes back to the
            // pause menu it was opened from; from the title's level list it goes back to the title. A run's screen
            // widens the button because the door there says where it goes.
            float backWidth = InRun ? 170f : 90f;
            var back = (RectTransform)card.Find("back");
            back.sizeDelta = new Vector2(backWidth * _scale, back.sizeDelta.y);
            count.Rect.sizeDelta = new Vector2(-(CardInset + (InRun ? 180f : 100f)) * _scale,
                count.Rect.sizeDelta.y);

            BindCardButton(card, "back", InRun ? "back to the menu  ·  Esc" : "title  ·  Esc",
                InRun ? (Action)(() => SetMode(Mode.Pause)) : () => SetMode(Mode.Title));

            float rowTop = header;
            for (int i = 0; i < _catalog.Count; i++)
            {
                BuildRow(card, i, rowTop, rowHeight, cardWidthDesign);
                rowTop += rowHeight + rowGap;
            }
        }

        /// <summary>
        /// One map as a row: its name and the facts that choose between maps on the left, its state on the right. An
        /// open row is a button that asks the driver for the map; a locked one is drawn dim and presses nothing.
        ///
        /// The row is <see cref="MapRowPrefab"/>, repeated: one asset for every map in the list, placed and sized by
        /// the card's own fit. Everything inside it is design units, scaled once at bind.
        /// </summary>
        private void BuildRow(RectTransform card, int index, float yFromTop, float rowHeight,
            float cardWidth)
        {
            MapDefinition map = _catalog.At(index);
            bool locked = index > _campaign.ClearedCount;

            // A shrunk row cannot carry the facts line and the reason under it as well: at less than a full row they
            // run into the next row's name. The name and the state word are what a picker has to read.
            bool roomy = rowHeight >= LevelRowHeight - 0.01f;

            RectTransform root = InstantiatePanel(card, MapRowPrefab, "map" + index);
            HudUi.ScaleDesignUnits(root, _scale);
            HudUi.TopLeft(root, CardInset * _scale, yFromTop * _scale,
                (cardWidth - 2f * CardInset) * _scale, rowHeight * _scale);

            HudButton button = root.gameObject.AddComponent<HudButton>();
            button.Fill = HudUi.AdoptRound(HudUi.Child(root, "back"), _palette.HudPanel, RowRadius);
            button.Fill.Image.raycastTarget = true;
            button.Border = HudUi.AdoptBorder(HudUi.Child(root, "frame"), Color.clear, RowRadius, 1f);

            // An open row asks for its map; a locked one presses nothing, and is dressed dim by the hover pass's
            // locked list.
            if (!locked)
            {
                int captured = index;
                button.Pressed = () => _play?.Invoke(captured, false);
                _buttons.Add(button);
            }
            else _locked.Add(button);

            HudText name = AdoptLabel(root, "name", HudType.Body, FontStyle.Bold, TextAnchor.UpperLeft,
                tracked: true);
            name.Set(map.Name, locked ? LockedText() : _palette.HudText);

            HudText facts = AdoptLabel(root, "facts", HudType.Micro, FontStyle.Normal, TextAnchor.UpperLeft);
            facts.Set(Facts(map), locked ? LockedText() : _palette.MachineIdle);
            facts.Show(roomy);

            HudText state = AdoptLabel(root, "state", HudType.Micro, FontStyle.Bold, TextAnchor.UpperRight,
                tracked: true);
            state.Set(StateWord(index), StateColour(index));

            // The reason, under the state word: the map standing in the way, and - when that map is the one that
            // teaches something new - the operator it is there to hand over. The unlock ladder said out loud, not a
            // bare ordinal.
            HudText reason = AdoptLabel(root, "reason", HudType.Micro, FontStyle.Normal, TextAnchor.UpperRight,
                wrap: true);
            reason.Set(LockedReason(), LockedText());
            reason.Show(locked && roomy);
        }

        /// <summary>In the priority the run itself would name: the map behind the pause menu is playing, a cleared
        /// map stays cleared for good, the frontier is the campaign's next step, and anything past it is
        /// locked.</summary>
        private string StateWord(int index)
        {
            if (index == _campaign.MapIndex && InRun) return "PLAYING";
            if (index < _campaign.ClearedCount) return "CLEARED";
            if (index == _campaign.ClearedCount) return "NEXT";
            return "LOCKED";
        }

        private Color StateColour(int index)
        {
            if (index == _campaign.MapIndex && InRun) return _palette.HudAccent;
            if (index < _campaign.ClearedCount) return HudInk.Good(_palette);
            if (index == _campaign.ClearedCount) return _palette.HudAccent;
            return LockedText();
        }

        /// <summary>Why a row past the frontier is closed: the same answer for every locked row, because the gate is
        /// the campaign's. The frontier map stands in the way, and when it hands the campaign an operator it has never
        /// held, the reason names it - the unlock ladder, not "map 3 of 6".</summary>
        private string LockedReason()
        {
            int frontier = Mathf.Min(_campaign.ClearedCount, _campaign.MapCount - 1);
            MapDefinition gate = _catalog.At(frontier);

            string fresh = FreshOperatorNames(frontier);
            return fresh.Length > 0
                ? "clear " + gate.Name + " - it hands over the " + fresh
                : "clear " + gate.Name;
        }

        /// <summary>The part of the unlock ladder clearing map <paramref name="index"/> buys, or the empty string when
        /// it teaches nothing new.</summary>
        private string FreshOperatorNames(int index)
        {
            Operators granted = Operators.None;
            for (int i = 0; i < index; i++) granted |= _catalog.At(i).Unlocked;

            return OperatorNames(_catalog.At(index).Unlocked & ~granted);
        }

        /// <summary>Every flag named: the dispatch the HUD's cards and the badge keep, so a fourth operator cannot slip
        /// into a map and ride out nameless.</summary>
        private static string OperatorNames(Operators unlocked)
        {
            var names = new StringBuilder();
            AppendOperator(names, unlocked, Operators.Cutter, "cutter");
            AppendOperator(names, unlocked, Operators.Rotator, "rotator");
            AppendOperator(names, unlocked, Operators.Stacker, "stacker");
            return names.ToString();
        }

        private static void AppendOperator(StringBuilder names, Operators unlocked, Operators flag,
            string name)
        {
            if ((unlocked & flag) == 0) return;
            if (names.Length > 0) names.Append(", ");
            names.Append(name);
        }

        /// <summary>A map's own facts, one line: the size a route is planned on, the budget it starts with, its wave
        /// count (or "endless"), the operators it hands over, and the seed only when the map pins one. Deliberately no
        /// wave table here: the demands are the run's surprises.</summary>
        private static string Facts(MapDefinition map)
        {
            var text = new StringBuilder();
            text.Append(map.Width).Append(" × ").Append(map.Height);
            text.Append("  ·  start ").Append(map.StartShapes);
            text.Append("  ·  ").Append(map.GeneratedWaves < 0 ? "endless" : map.Waves.Length + " waves");

            string operators = OperatorNames(map.Unlocked);
            if (operators.Length > 0) text.Append("  ·  ").Append(operators);
            if (map.GeneratedWaves >= 0) text.Append("  ·  endless on N");
            if (map.Seed != 0) text.Append("  ·  seed ").Append(map.Seed);

            return text.ToString();
        }

        private Color LockedText() => HudInk.Blend(_palette.HudText, _palette.HudPanel, 0.55f);

        // ------------------------------------------------------------------ the help card

        private const string HelpPrefab = "Hud/ShellHelp";

        /// <summary>
        /// The shell's help card: the keyboard, the same list the run's help card prints - one keyboard, one list, which
        /// is why <see cref="HudView.KeyLines"/> is shared. Built here because at boot there is no HUD yet to open
        /// its own.
        ///
        /// It takes the wider 300 (a card centred on an empty screen has room to breathe) and has no restart button,
        /// because a menu is not a run.
        /// </summary>
        private void BuildHelp()
        {
            RectTransform card = InstantiatePanel(HelpPrefab, "help");
            float width = 300f * _scale;
            string[] lines = HudView.KeyLines();
            float height = HudLayout.HelpHeightFor(lines.Length) * _scale;

            // The card is centred: the canvas root already spans the screen, so the card hangs off its top edge.
            HudUi.TopCentre(card, (Screen.height - height) * 0.5f, width, height);
            HudUi.ScaleDesignUnits(card, _scale);

            HudUi.AdoptShadow(HudUi.Child(card, "shadow"), 12f, _palette.HudShadow);
            HudUi.AdoptRound(HudUi.Child(card, "back"), _palette.HudPanel, 12f);
            HudUi.AdoptBorder(HudUi.Child(card, "frame"), PanelBorder(), 12f, 1f);

            AdoptLabel(card, "header", HudLayout.HelpLineSize, FontStyle.Bold, TextAnchor.MiddleLeft,
                tracked: true).Set("CONTROLS", _palette.HudText);
            HudUi.AdoptBox(HudUi.Child(card, "rule"), HudInk.Rule(_palette));

            for (int i = 0; i < lines.Length; i++)
            {
                HudText row = AdoptLabel(card, "key" + i, HudLayout.HelpLineSize, FontStyle.Normal,
                    TextAnchor.MiddleLeft);
                row.Set(lines[i], HudInk.Muted(_palette));
            }

            HudButton close = HudUi.AdoptButton(HudUi.Child(card, "close"), "×", FontSize(HudType.Label),
                _palette.HudText, () => SetMode(_inRun ? Mode.Pause : Mode.Title));
            _buttons.Add(close);
        }

        // ------------------------------------------------------------------ keys, scale, dressing

        /// <summary>The keys the shell itself answers, read only while it is open - the run is held and unsampled, so
        /// these cannot collide with a run key.
        ///
        /// Back is one step, whatever the screen: Escape or M closes the help card and the map list onto whichever
        /// screen opened them, and closes the pause menu onto the run. On the title Escape has no screen behind it - in
        /// a fullscreen page it leaves fullscreen instead, and in a window it does nothing (see
        /// <see cref="PlayerPage"/>). Inside the pause menu, Escape disarms an armed door rather than leaving.
        ///
        /// The branches are exclusive on purpose: one key takes one step per frame. A mode change reopens the shell
        /// and spends the frame's keys, so a frame that read two doors could start a run and then open the map list
        /// over it.</summary>
        private void ReadKeys()
        {
            if (_shownAtFrame == Time.frameCount) return;   // the keys that opened this mode already spent themselves

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool confirm = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            bool menu = keyboard.mKey.wasPressedThisFrame;
            bool back = keyboard.escapeKey.wasPressedThisFrame;

            switch (_mode)
            {
                case Mode.Title:
                    // M opens the map list here. Escape has no screen behind it, so it takes the one step that is behind
                    // the title itself: out of fullscreen, where the page is holding the key for the game (see
                    // PlayerPage). In a windowed page there is no step to take, which is why the title's map button
                    // advertises M and not Esc.
                    if (menu) SetMode(Mode.Levels);
                    else if (confirm) PressPlay();
                    else if (back && PlayerPage.InFullscreen) PlayerPage.LeaveFullscreen();
                    break;

                case Mode.Help:
                case Mode.Levels:
                    if (back || menu) SetMode(_inRun ? Mode.Pause : Mode.Title);
                    break;

                case Mode.Pause:
                    if (back || menu)
                    {
                        if (_confirmQuit || _confirmRestart) CancelArmed();
                        else _resume?.Invoke();
                    }
                    else if (confirm && !_confirmQuit && !_confirmRestart) _resume?.Invoke();
                    break;
            }
        }

        /// <summary>Disarm whichever door was armed and put the card back the way it was.</summary>
        private void CancelArmed()
        {
            _confirmQuit = false;
            _confirmRestart = false;
            RequestRebuild();
        }

        private void RefreshScale()
        {
            float s = HudLayout.Scale(Screen.height);
            if (_screenWidth == Screen.width && _screenHeight == Screen.height &&
                Math.Abs(s - _scale) <= _scale * RebuildFraction)
                return;

            Rebuild();
        }

        private void DressButtons()
        {
            for (int i = 0; i < _buttons.Count; i++) Dress(_buttons[i]);
            for (int i = 0; i < _locked.Count; i++) _locked[i].Border.Set(LockedText());
        }

        /// <summary>The HUD's generic button look, said once here as the HUD says it once: a menu that dressed
        /// differently would read as a different game. The title's play button is the one filled accent surface on the
        /// screen, because "start here" is the only thing a first-time player has to find.</summary>
        private void Dress(HudButton button)
        {
            bool hovered = button.Hovered;

            if (_primary.Contains(button))
            {
                button.Fill.Set(hovered ? HudInk.Blend(_palette.HudAccent, _palette.HudText, 0.16f) : _palette.HudAccent);
                button.Border.Set(hovered ? _palette.HudText : HudInk.Blend(_palette.HudAccent, _palette.HudPanel, 0.35f));
                return;
            }

            button.Fill.Set(hovered ? HudInk.Blend(ButtonFill, _palette.HudText, 0.14f) : ButtonFill);
            button.Border.Set(hovered ? _palette.HudAccent : PanelBorder());
        }

        /// <summary>The header's back button and the rows' PLAYING mark both read it; the boot's shell has no run
        /// behind it to go back to.</summary>
        private bool InRun => _inRun;

        private int FontSize(float design) => Mathf.Max(8, Mathf.CeilToInt(design * _scale));

        /// <summary>A screen from its prefab, under the shell's canvas and ready to be placed: authored in design units
        /// and scaled once with <see cref="HudUi.ScaleDesignUnits"/>, the same pass the HUD's panels take.</summary>
        private RectTransform InstantiatePanel(string resourcePath, string name)
            => InstantiatePanel(_canvasRoot, resourcePath, name);

        /// <summary>The shell's screens, one asset each: the shell is made in code by the driver and has no scene to
        /// wire a reference into. One per screen, so a diff touches the screen it changed; the map list's rows are one
        /// row asset repeated.</summary>
        private const string BackdropPrefab = "Hud/ShellBackdrop";
        private const string TitlePrefab = "Hud/ShellTitle";
        private const string PausePrefab = "Hud/ShellPause";
        private const string LevelsPrefab = "Hud/ShellLevels";
        private const string MapRowPrefab = "Hud/ShellMapRow";

        /// <summary>A screen's piece from its prefab, under <paramref name="parent"/>: a rebuild takes the level
        /// list's rows with the card.</summary>
        private RectTransform InstantiatePanel(Transform parent, string resourcePath, string name)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "shell prefab 'Assets/Resources/" + resourcePath + ".prefab' is missing");

            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = name;
            return (RectTransform)instance.transform;
        }

        /// <summary>A label the prefab places, at the size its design unit names: the rule <see cref="Label"/> gives
        /// one it made, so an authored label and a built one cannot disagree.</summary>
        private HudText AdoptLabel(RectTransform parent, string name, float design, FontStyle style,
            TextAnchor anchor, bool tracked = false, bool wrap = false)
            => HudUi.AdoptText(HudUi.Child(parent, name), FontSize(design), style, anchor, wrap, tracked);

        /// <summary>A card's three surfaces, adopted: the cast shadow, the fill and the milled edge. The card's name is
        /// spelled once here, the one thing an asset's node names and this builder agree on by convention
        /// (<c>card.back</c>). Every card in the shell is the same object at the same radius.</summary>
        private void AdoptCard(RectTransform root, string name, float radius)
        {
            HudUi.AdoptShadow(HudUi.Child(root, name + ".shadow"), radius, _palette.HudShadow);
            HudUi.AdoptRound(HudUi.Child(root, name + ".back"), _palette.HudPanel, radius);
            HudUi.AdoptBorder(HudUi.Child(root, name + ".frame"), PanelBorder(), radius, 1f);
        }

        private Color PanelBorder() => HudInk.Edge(_palette);

        private Color ButtonFill => HudInk.Raised(_palette);

        // ------------------------------------------------------------------ geometry

        /// <summary>The HUD's own rule: the fonts keep the scale inside their point size.</summary>
        private const float RebuildFraction = 0.02f;

        /// <summary>A screen's card takes the larger radius the HUD's overlay cards share: a menu is a place rather
        /// than a strip.</summary>
        private const float CardRadius = 12f;

        /// <summary>The one value the shell's cards put their words and their doors at, so the pause menu and the map
        /// list read as the same card.</summary>
        private const float CardInset = 16f;

        /// <summary>A row is a control inside a card, so it takes the smaller cut.</summary>
        private const float RowRadius = 8f;

        /// <summary>
        /// The one number on the title that is a fraction rather than a design unit, because a wordmark's band is a
        /// share of the screen. <see cref="TitlePrefab"/> says it with an anchor: the name's top edge sits at 86 %
        /// height.
        /// </summary>
        public const float TitleNameTop = 0.14f;


        /// <summary>Where the title's column starts, in design units below the name's band, and one door's height in
        /// design units. The column's step is <see cref="ButtonStep"/>, this height plus its gap, so a step below it
        /// would stack the doors on each other - <c>ShellLayoutTests</c> holds the asset to that.</summary>
        public const float TitleFirstButtonTop = 150f;
        public const float TitleButtonHeight = 36f;

        /// <summary>One door's height and the gap to the next, in design units. The pause menu is the one that
        /// compacts it, on a window too short to carry the column.</summary>
        public const float ButtonStep = 46f;

        /// <summary>The map list's card width and the header band above the rows and foot under them, in design units.
        /// The card's own height is the fit's rather than the asset's; these two bands are what the fit is measured
        /// against.</summary>
        private const float LevelsWidth = 660f;
        private const float LevelsHeader = 44f;
        private const float LevelsFoot = 16f;

        /// <summary>A level row's height and the gap between rows, before the fit-into-the-screen shrink; the floor is
        /// the shortest row that still carries a name and a state word legibly.</summary>
        private const float LevelRowHeight = 54f;
        private const float LevelRowGap = 8f;
        private const float LevelRowFloor = 30f;
    }
}
