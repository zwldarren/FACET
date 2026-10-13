using System;
using System.Text;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The player's whole operating surface: a status card top-left (the map and its wave, the Core's
    /// health, the defence's two diagnostics and the last second's news), the **stockpile** floating
    /// beside it as a shape and a number with no panel of its own, a small controls card top-right, a
    /// clickable **build bar** along the bottom, and an info card just above it that describes whichever
    /// building is hovered, selected - or, with nothing selected, read (see
    /// <see cref="DrawInspectInfo"/>). That last state is the one a run opens in, and the one Escape goes
    /// back to.
    ///
    /// What a click gets is a **toast**: a placement the simulation refused - the stockpile is dry, the
    /// tile is taken, the ground is wrong - arrives on the event stream as
    /// <see cref="SimEventKind.PlacementRejected"/> with the reason, and a panel sized to that sentence
    /// floats under the top cards for a few seconds of simulation time, fading out as it goes. The
    /// stockpile's own number turns amber while the selected building cannot be afforded and flashes
    /// warning-coloured while the toast is live, so what is wrong is visible before it is read.
    ///
    /// The build bar is the point of this view. Every building is a real button carrying its hotkey,
    /// its name, its role and its cost; a tile is tinted when the stockpile cannot cover it, outlined
    /// when it is selected, and highlighted under the pointer. Clicking one selects it exactly the way
    /// a number key does (through the input latch, so the "the press decides what is built" rule is
    /// untouched), which is what lets the game be played without memorising the keyboard - on WebGL,
    /// or on a trackpad, or by someone who has never seen it.
    ///
    /// Two sentences in the design doc land here. "The intermission reads like a bill of materials":
    /// the preview names each enemy with the shape it is weak to, so "what do I need for this wave?"
    /// is answered as a shopping list. And "every building is paid for in circles banked at the
    /// Core": the stockpile is the economy's one number, and every tile carries its cost.
    ///
    /// Drawn with IMGUI on purpose: the whole game is built in code, with no prefabs and no scene
    /// authoring, and a hand-authored uGUI canvas would be the one asset that has to be edited in the
    /// Editor to be changed.
    ///
    /// The pieces' geometry lives in <see cref="HudLayout"/>, because the camera rig needs the same
    /// numbers.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class HudView : MonoBehaviour
    {
        /// <summary>Rebuild the styles once the screen has scaled this far from the built size.</summary>
        private const float StyleScaleTolerance = 0.05f;

        /// <summary>The window the "last second" line reports over: one second, in ticks, so the
        /// numbers a player steers by are a rate rather than a running total.</summary>
        private static readonly int RecentTicks = (int)SimConfig.TickRate;

        /// <summary>How long a refused placement stays on the resource band, in ticks: long enough to
        /// read the answer after the click, short enough that it describes the click just made.</summary>
        private static readonly int RefusalTicks = (int)(SimConfig.TickRate * 3f);

        /// <summary>The fraction of the refusal's window spent fading out rather than holding.</summary>
        private const float RefusalFade = 0.35f;

        private SimWorld _world;
        private Palette _palette;
        private CampaignState _campaign;

        /// <summary>The progression, for the one question the HUD asks of it: which map is next. Set by
        /// the driver, which builds it from the scene's map set; a HUD the driver did not wire (a test's
        /// single-map HUD) has none.</summary>
        private MapCatalog _catalog;

        /// <summary>The build bar's two ways back into the input latch: select a kind (or none, which is
        /// the inspect cursor), and turn the placement ghost. Handed in by the driver rather than
        /// reached for, so a scene with no driver still draws.</summary>
        private Action<BuildKind?> _selectKind;
        private Action<int> _rotate;

        private Texture2D _pixel;
        private GUIStyle _title;
        private GUIStyle _labelCenter;
        private GUIStyle _small;
        private GUIStyle _smallRight;
        private GUIStyle _costRight;
        private GUIStyle _tiny;
        private GUIStyle _tinyRight;
        private GUIStyle _tinyCenter;
        private GUIStyle _wrap;
        private GUIStyle _button;
        private GUIStyle _banner;
        private GUIStyle _resource;
        private GUIStyle _alert;

        private float _stylesScale = -1f;

        /// <summary>Which build tile the pointer is over this frame, or -1. Read by the info card, which
        /// describes the hovered building in preference to the selected or read one.</summary>
        private int _hoveredTile = -1;

        /// <summary>The mouse is over a HUD panel right now, so a click belongs to the UI and not to the
        /// world underneath. Read by the driver and handed to <see cref="BuildInput.Sample"/>.</summary>
        public bool PointerOverHud { get; private set; }

        /// <summary>Is there a wave to start early, and is the world in a state to start it? The button
        /// is drawn only when this is true, so it never promises something the director would ignore.
        /// The clock half of the question is <see cref="WaveDirector.HasWaveToStart"/>; the paused and
        /// over halves are the world's.</summary>
        private bool CanStartWaveNow =>
            _world.Status == GameStatus.Playing && !_world.Paused && _world.Waves.HasWaveToStart;

        /// <summary>
        /// Point the HUD at a world, its palette, and where the player is in the campaign. Called
        /// again on every map change, which is why the pixel texture is created once and kept: the
        /// styles and the one white pixel outlive every world the HUD has drawn.
        ///
        /// <paramref name="campaign"/> is optional so a scene (or a test) with one map can leave it
        /// out and get exactly the single-map HUD. The selection and rotation callbacks are optional
        /// for the same reason: without them the bar still draws, it just cannot be clicked.
        /// </summary>
        public void Initialize(SimWorld world, Palette palette, CampaignState campaign = null,
            Action<BuildKind?> selectKind = null, Action<int> rotate = null, MapCatalog catalog = null)
        {
            _world = world;
            _palette = palette;
            _campaign = campaign;
            _catalog = catalog;
            if (selectKind != null) _selectKind = selectKind;
            if (rotate != null) _rotate = rotate;

            if (_pixel != null) return;

            // One white pixel, tinted per draw: a texture per panel colour would be four textures whose
            // only difference is a multiply.
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "FACET/HudPixel", hideFlags = HideFlags.DontSave };
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        /// <summary>Is the "how to play" card up? The one predicate the card's drawing, its claim on
        /// the pointer and the toast's clearance all read - and the same one the driver hands the
        /// camera rig - so no two pieces of the frame can disagree about whether the card is there.</summary>
        private bool ShowTutorial => _campaign != null && _campaign.IsFirstMap;

        private void OnGUI()
        {
            if (_world == null || _palette == null) return;

            EnsureStyles();

            float s = _stylesScale;

            Rect status = HudLayout.Status(s);
            Rect controls = HudLayout.Controls(Screen.width, s);
            Rect bar = HudLayout.Bar(Screen.width, Screen.height, s);
            Rect info = HudLayout.Info(bar, s);

            // The tutorial explains the first map's chain - mine, carry, spend - so it is up only
            // while the run is on the map that asks the player to do it for the first time. The same
            // predicate the camera rig is given, so the card and the map's bound cannot disagree.
            bool showTutorial = ShowTutorial;
            Rect tutorial = HudLayout.Tutorial(s);

            // The HUD owns its panels: the pointer being over one is reported to the input source, so a
            // click on a button cannot also build under it. OnGUI runs after the driver's Update, so
            // this describes the panel under the pointer as of the previous frame - a frame of lag on a
            // pointer that has to travel to the button anyway. The stockpile readout and the toast are
            // not panels: they are read, never clicked, so a click goes through them to the map.
            PointerOverHud = PointerOver(status) || PointerOver(controls) ||
                             PointerOver(bar) || PointerOver(info) ||
                             (showTutorial && PointerOver(tutorial));

            DrawStockpile(HudLayout.Stockpile(Screen.width, s), s);
            DrawStatus(status, s);

            if (showTutorial) DrawTutorial(tutorial, s);

            DrawControls(controls, s);

            // The bar runs first so it can record which tile is under the pointer; the info card reads
            // that decision and describes the hovered building in preference to the selected one.
            DrawBuildBar(bar, s);
            DrawInfo(info, s);

            DrawBanner(s);
            DrawToast(s);
        }

        // ------------------------------------------------------------------ stockpile and toast

        /// <summary>
        /// The stockpile, floating beside the status card in the largest type on screen: the shape the
        /// run banks, in that shape's own colour, and how many of it there are. No panel behind it and no
        /// label - it is the economy's one number, and the card it sits against already says what the
        /// game is about.
        ///
        /// It reads rather than shouts. The number turns amber while the selected building cannot be
        /// afforded and warning-coloured while a refusal is on screen, and the toast below it says what
        /// went wrong; the readout itself stays two pieces of information wide.
        /// </summary>
        private void DrawStockpile(Rect panel, float s)
        {
            BuildKind? selected = _world.SelectedKind;
            Color numberColour = _palette.Outline;
            if (selected.HasValue && !_world.Economy.CanAfford(selected.Value))
                numberColour = _palette.CursorNoFunds;
            if (TryLastRefusal(out SimEvent refusal))
                numberColour = WithAlpha(_palette.HudWarn, RefusalFadeOf(_world.TickCount - refusal.Tick));

            string count = _world.Economy.Circles.ToString();
            Label(panel, count, numberColour, _resource);

            // The shape follows the number rather than sitting in a column of its own: the readout grows
            // to the right as the stockpile grows, and stays glued to the card either way.
            float used = _resource.CalcSize(new GUIContent(count)).x;
            float glyphX = panel.x + used + 6f * s;
            if (glyphX >= panel.xMax) return;

            Label(new Rect(glyphX, panel.y, panel.xMax - glyphX, panel.height),
                Glyph(ShapeType.Circle), _palette.ShapeColor(ShapeType.Circle), _resource);
        }

        /// <summary>
        /// The last refused placement, as a toast: what the player tried to build, what it costs, and
        /// what is in the way - printed beside the stockpile it is about, over the map, for as long as the
        /// click is worth explaining.
        ///
        /// It has no state of its own. The refusal is read off the event stream by
        /// <see cref="TryLastRefusal"/> and faded by its own tick, so there is no timer to reset, a
        /// restart leaves nothing behind, and a paused run simply holds the message until the clock moves
        /// again.
        /// </summary>
        private void DrawToast(float s)
        {
            if (!TryLastRefusal(out SimEvent refusal)) return;

            float fade = RefusalFadeOf(_world.TickCount - refusal.Tick);
            string text = RefusalText(refusal);

            float padding = 14f * s;
            Vector2 textSize = _alert.CalcSize(new GUIContent(text));
            float width = Mathf.Min(textSize.x + 2f * padding, Screen.width - 2f * HudLayout.Margin * s);
            float height = textSize.y + 16f * s;

            Rect toast = HudLayout.Toast(Screen.width, s, width, height, ShowTutorial);
            Fill(toast, WithAlpha(_palette.HudPanel, fade));
            Border(toast, WithAlpha(_palette.HudWarn, fade), 2f * s);
            Label(toast, text, WithAlpha(_palette.HudWarn, fade), _alert);
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
                    return "NOT ENOUGH " + Glyph(ShapeType.Circle) + " — " + def.Name + " costs " +
                           Mathf.RoundToInt(refusal.Amount) + ", you have " + _world.Economy.Circles;
                case RejectionReason.Occupied:
                    return "BLOCKED — something already stands on " + refusal.Cell;
                case RejectionReason.BadGround:
                    return def.Tile == TileKind.Drill
                        ? "NO ORE — a drill mines only the patch it stands on"
                        : "ON ORE — only a drill may stand on a patch";
                case RejectionReason.Doorway:
                    return "A DOORWAY — waves walk in at " + refusal.Cell +
                           ", and nothing solid may stand there";
                default:
                    return "OFF THE MAP — nothing can be built on " + refusal.Cell;
            }
        }

        // ------------------------------------------------------------------ tutorial

        /// <summary>
        /// The one piece of the HUD that explains the game rather than reporting it: the first map's
        /// chain in the order the player has to do it - mine, carry, spend - and the two rules that
        /// watching one run does not make obvious: the jam, and the weakness. The weakness rule decides
        /// nothing on this map (every enemy here is a Spike), so it is taught here, one line early,
        /// rather than on the next map in the middle of the wave that first punishes ignoring it.
        ///
        /// A card rather than a dismissable popup: it sits in the corner the player is already reading,
        /// so it costs no click and leaves no state behind when the run moves on to the next map.
        /// </summary>
        private void DrawTutorial(Rect panel, float s)
        {
            Panel(panel);
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), _palette.HudAccent);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 10f * s, panel.width - 28f * s, 22f * s);
            Label(line, "HOW TO PLAY", _palette.HudAccent, _title);

            line.y += 28f * s;
            line.height = 92f * s;
            Label(line,
                "1. Mine circle ore and carry it back to the Core using conveyor belts.\n" +
                "2. Ore can only be spent after it reaches the Core.\n" +
                "3. The wrong shape will jam a line. Right-click the jammed segment to clear it.\n" +
                "4. Match ammo to the enemy's weakness icon - anything else deals a quarter.",
                _palette.HudText, _wrap);
        }

        // ------------------------------------------------------------------ status

        private void DrawStatus(Rect panel, float s)
        {
            // The card's two diagnostics are read before it is drawn, because the stripe down its left
            // edge reports them: a card with a jam in it, or a defence that cannot shoot, stops being
            // accent-coloured and turns warning-coloured - the one thing on the HUD that says "read me"
            // without being read.
            _world.Machines.CountTurrets(out int total, out int armed);
            bool starved = armed < total;
            int jams = _world.Belts.JamCount;
            bool hurting = _world.Core.HealthFraction <= 0.4f;

            Panel(panel);
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height),
                jams > 0 || starved || hurting ? _palette.HudWarn : _palette.HudAccent);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 10f * s, panel.width - 28f * s, 22f * s);
            Label(line, TitleLine(), _palette.HudAccent, _title);

            line.y += 28f * s;
            DrawHealthBar(new Rect(line.x, line.y, line.width, 16f * s), s);
            line.y += 24f * s;

            // The stockpile used to be the first line here. It lives in the resource band above now -
            // it is the economy's one number and the thing a refusal is about, so it is too important
            // to be the third line of a card.
            Label(line, "turrets armed " + armed + " / " + total + (starved ? "   (a starved turret's icon is grey)" : ""),
                starved ? _palette.HudWarn : _palette.HudGood, _small);
            line.y += 20f * s;

            Label(line, jams == 0
                    ? "no jammed segments"
                    : jams + " jammed segment" + (jams == 1 ? "" : "s") + "   [RMB clears one]",
                jams == 0 ? _palette.HudText : _palette.HudWarn, _small);
            line.y += 20f * s;

            Label(line, RecentLine(), _palette.HudText, _tiny);
            line.y += 19f * s;

            Label(new Rect(line.x, line.y, line.width, 40f * s), WaveLines(), _palette.HudText, _tiny);
            line.y += 41f * s;

            if (CanStartWaveNow)
            {
                var button = new Rect(line.x, line.y, 250f * s, 26f * s);
                if (Button(button, "▶  start wave " + _world.Waves.NextWave + " now",
                        _palette.HudAccent, _palette.HudPanel, s))
                    _world.RequestNextWave();
            }
        }

        /// <summary>
        /// What the simulation reported in the last second, read straight off its event stream: the
        /// economy's heartbeat (circles banked), the defence's work (shots, kills) and the one thing the
        /// player has to act on (a jam, and where it is).
        ///
        /// A window rather than a log, deliberately: a scrolling list of one-second-old news is
        /// something to read instead of play.
        /// </summary>
        private string RecentLine()
        {
            SimEventBuffer events = _world.Events;
            int since = _world.TickCount - RecentTicks;

            int banked = events.CountSince(SimEventKind.Banked, since);
            int shots = events.CountSince(SimEventKind.ShotFired, since);
            int kills = events.CountSince(SimEventKind.EnemyKilled, since);
            int lost = events.CountSince(SimEventKind.BuildingDestroyed, since);
            int jams = events.CountSince(SimEventKind.Jammed, since);

            var text = new StringBuilder();

            void Add(string part)
            {
                if (text.Length > 0) text.Append("  ·  ");
                text.Append(part);
            }

            if (banked > 0) Add(banked + " " + Glyph(ShapeType.Circle) + " banked");
            if (shots > 0) Add(shots + (shots == 1 ? " shot" : " shots"));
            if (kills > 0) Add(kills + (kills == 1 ? " kill" : " kills"));
            if (lost > 0) Add(lost + " lost");
            if (jams > 0 && events.TryLastWithin(SimEventKind.Jammed, since, out SimEvent jam))
                Add(jams + " jammed at (" + jam.Cell.X + "," + jam.Cell.Y + ")");

            // The ring is fixed and shared, so a loud second can overwrite events before this reader
            // saw them. When the stream fell behind inside the window being counted, say so: a count
            // that is quietly low is worse reported honestly than silently wrong - and the buffer
            // answers that for the window, not the reader.
            if (events.DroppedSince(since)) Add("stream full, " + events.Dropped + " dropped");

            return "last 1s:  " + (text.Length == 0 ? "quiet" : text.ToString());
        }

        private static bool PointerOver(Rect rect) => rect.Contains(Event.current.mousePosition);

        private void DrawHealthBar(Rect bar, float s)
        {
            Fill(bar, _palette.HudPanel);
            float fraction = _world.Core.HealthFraction;
            Color colour = fraction > 0.4f ? _palette.HudGood : _palette.HudWarn;
            Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), colour);
            Border(bar, Blend(_palette.HudText, _palette.HudPanel, 0.5f), 1f * s);

            Label(new Rect(bar.x + 6f * s, bar.y - 1f * s, bar.width, bar.height),
                "core " + Mathf.CeilToInt(_world.Core.Hp) + " / " + Mathf.CeilToInt(_world.Core.MaxHp),
                _palette.Outline, _tiny);
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

            WaveDirector waves = _world.Waves;
            switch (waves.Phase)
            {
                case WavePhase.Held:
                    // The opening's clock has not been armed: it starts with the player's first building, so
                    // its slot says "ready" rather than counting a number down - the number is waiting in the
                    // table, and the wave line underneath says what starts it. The button below is the other
                    // way to begin, for a player who wants the wave before they have built anything.
                    return head + "  ·  wave " + waves.NextWave + " of " + waveCount + "  ·  ready";
                case WavePhase.Intermission:
                    return head + "  ·  wave " + waves.NextWave + " of " + waveCount +
                           " in " + Mathf.CeilToInt(waves.IntermissionRemaining) + "s";
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

        /// <summary>The wave line and, while a wave waits for its clock, the composition the player is
        /// building for.</summary>
        private string WaveLines()
        {
            if (_world.Status != GameStatus.Playing) return "press R to restart";

            WaveDirector waves = _world.Waves;
            switch (waves.Phase)
            {
                case WavePhase.Over:
                    return "no waves left";

                case WavePhase.Held:
                case WavePhase.Intermission:
                {
                    string incoming = "incoming: " + Composition(waves.NextWaveDefinition);
                    // The opening begins its countdown with the player's first building; every countdown after
                    // it is already running. Either way the button below - or N - starts the wave now, while
                    // the run is unpaused; paused, neither the key nor the button does anything.
                    if (_world.Paused) return incoming;
                    return waves.Phase == WavePhase.Held
                        ? incoming + "   ·   N starts it now, or build to start the countdown"
                        : incoming + "   ·   press N to start it early";
                }

                case WavePhase.Running:
                {
                    WaveDefinition current = _world.Map.Waves[waves.CurrentWave - 1];
                    int left = _world.Enemies.AliveCount + waves.PendingInWave;
                    return "left in this wave: " + left + " of " + current.Total + " (" + Composition(current) + ")";
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(waves), waves.Phase,
                        "a new wave phase must name its wave line");
            }
        }

        /// <summary>"16 Spike (◠-weak)" - the doc's bill of materials, over whatever enemies exist.</summary>
        private string Composition(WaveDefinition wave)
        {
            ContentDatabase content = _world.Content;
            var text = new StringBuilder();
            for (int i = 0; i < content.EnemyKinds.Length; i++)
            {
                EnemyKind kind = content.EnemyKinds[i];
                int count = wave.CountOf(kind);
                if (count == 0) continue;

                EnemyDef spec = content.Enemy(kind);
                if (text.Length > 0) text.Append(" · ");
                text.Append(count).Append(' ').Append(spec.Name).Append(" (").Append(Glyph(spec.Weakness)).Append("-weak)");
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------ controls card

        /// <summary>
        /// The three buttons that are about the run rather than about a building: pause, restart, and
        /// (when there is one) the start-wave button living in the status card. Kept out of the build
        /// bar so a misplaced click can never restart the run.
        /// </summary>
        private void DrawControls(Rect panel, float s)
        {
            Panel(panel);

            Rect line = new Rect(panel.x + 12f * s, panel.y + 9f * s, panel.width - 24f * s, 14f * s);
            Label(line, "CONTROLS", _palette.HudAccent, _tiny);
            line.y += 19f * s;

            if (Button(new Rect(line.x, line.y, line.width, 26f * s), _world.Paused ? "▶  resume" : "II  pause",
                    ButtonFill, _palette.HudText, s))
                _world.Paused = !_world.Paused;
            line.y += 29f * s;

            if (Button(new Rect(line.x, line.y, line.width, 26f * s), "R  restart  run",
                    ButtonFill, _palette.HudText, s))
                _world.RequestRestart();
        }

        // ------------------------------------------------------------------ build bar

        private void DrawBuildBar(Rect panel, float s)
        {
            Panel(panel);
            Fill(new Rect(panel.x, panel.y, panel.width, 3f * s), _palette.HudAccent);
            _hoveredTile = -1;

            Rect header = new Rect(panel.x + 12f * s, panel.y + 9f * s, panel.width - 24f * s, 15f * s);
            Label(header, "BUILD", _palette.HudAccent, _tiny);
            Label(header,
                "click a tile or press " + BuildInput.KeyRangeText() +
                "   ·   again to drop it   ·   Q / E turn the ghost   ·   LMB place (drag to lay belts)   ·   RMB delete or clear a jam   ·   Esc inspects",
                _palette.HudText, _tinyRight);

            BuildKind[] kinds = _world.Content.BuildKinds;
            int count = kinds.Length;
            float pad = 10f * s;
            float gap = 6f * s;
            float tileWidth = (panel.width - 2f * pad - (count - 1) * gap) / count;
            float tileY = panel.y + 29f * s;
            float tileHeight = panel.height - 39f * s;

            for (int i = 0; i < count; i++)
            {
                var tile = new Rect(panel.x + pad + i * (tileWidth + gap), tileY, tileWidth, tileHeight);
                bool hovered = PointerOver(tile);
                if (hovered) _hoveredTile = i;

                DrawBuildTile(tile, kinds[i], i, hovered, s);
            }
        }

        /// <summary>
        /// One building as a button: hotkey, cost, name and role over a stripe tinted by that role.
        /// Selected is an accent outline and a tinted fill; unaffordable greys the name and turns the
        /// cost amber; the pointer lightens the whole tile. A click selects the kind through the input
        /// latch - it never places anything, so a stray click on the palette can only ever change what
        /// is selected.
        /// </summary>
        private void DrawBuildTile(Rect rect, BuildKind kind, int index, bool hovered, float s)
        {
            MachineDef def = _world.Content.Machine(kind);
            bool selected = kind == _world.SelectedKind;
            bool affordable = _world.Economy.CanAfford(kind);
            Color category = CategoryColor(kind);

            Color fill = _palette.HudPanel;
            if (selected) fill = Blend(_palette.HudPanel, category, 0.18f);
            else if (hovered) fill = Blend(_palette.HudPanel, _palette.HudText, 0.10f);
            else if (!affordable) fill = Blend(_palette.HudPanel, _palette.CursorNoFunds, 0.12f);
            Fill(rect, fill);

            Fill(new Rect(rect.x, rect.y, rect.width, 3f * s),
                selected ? category : Blend(category, _palette.HudPanel, 0.4f));

            if (selected) Border(rect, _palette.HudAccent, 2f * s);
            else if (hovered) Border(rect, Blend(_palette.HudText, _palette.HudPanel, 0.35f), 1f * s);

            Label(new Rect(rect.x + 8f * s, rect.y + 9f * s, 26f * s, 15f * s), BuildInput.HotkeyLabel(index),
                selected ? _palette.HudAccent : _palette.MachineIdle, _small);

            // The cost is the line that answers "can I afford this", so it is the line that turns amber
            // and bold when the answer is no - the same colour the cursor ghost and the resource band
            // use for the same fact.
            Label(new Rect(rect.x, rect.y + 9f * s, rect.width - 8f * s, 15f * s),
                def.Cost + " " + Glyph(ShapeType.Circle),
                affordable ? (selected ? _palette.HudText : _palette.MachineIdle) : _palette.CursorNoFunds,
                affordable ? _smallRight : _costRight);

            Label(new Rect(rect.x + 4f * s, rect.y + 27f * s, rect.width - 8f * s, 20f * s), def.Name,
                affordable ? _palette.HudText : _palette.MachineIdle, _labelCenter);

            Label(new Rect(rect.x + 4f * s, rect.y + rect.height - 20f * s, rect.width - 8f * s, 14f * s),
                RoleOf(kind), Blend(category, _palette.HudText, 0.15f), _tinyCenter);

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                _selectKind?.Invoke(kind);
            }
        }

        // ------------------------------------------------------------------ info card

        /// <summary>
        /// What the hovered (or, when nothing is hovered, the selected) building actually does: its
        /// cost and role in the header, a sentence of what it is for, the numbers behind it, and the
        /// facing its ghost will be built with - plus the two rotate buttons, which are the one way to
        /// turn a building without a keyboard.
        ///
        /// With nothing selected and nothing hovered the card describes what the player *read* instead -
        /// see <see cref="DrawInspectInfo"/>.
        /// </summary>
        private void DrawInfo(Rect panel, float s)
        {
            BuildKind[] kinds = _world.Content.BuildKinds;
            BuildKind? kind = _hoveredTile >= 0 && _hoveredTile < kinds.Length
                ? kinds[_hoveredTile]
                : _world.SelectedKind;

            if (kind.HasValue) DrawBuildInfo(panel, kind.Value, s);
            else DrawInspectInfo(panel, s);
        }

        private void DrawBuildInfo(Rect panel, BuildKind kind, float s)
        {
            MachineDef def = _world.Content.Machine(kind);
            Color category = CategoryColor(kind);

            Panel(panel);
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), category);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 9f * s, panel.width - 28f * s, 18f * s);
            Label(line, def.Name, _palette.HudText, _title);
            Label(line, def.Cost + " " + Glyph(ShapeType.Circle) + "   " + RoleOf(kind),
                _palette.MachineIdle, _smallRight);
            line.y += 24f * s;

            Label(new Rect(line.x, line.y, line.width, 32f * s), Blurb(kind), _palette.HudText, _wrap);
            line.y += 34f * s;

            string stats = Stats(kind);
            if (!string.IsNullOrEmpty(stats))
            {
                Label(new Rect(line.x, line.y, line.width, 15f * s), stats, category, _small);
                line.y += 18f * s;
            }

            Label(new Rect(line.x, line.y, 130f * s, 22f * s),
                "facing " + MachineReading.DirName(_world.PlacementDirection), _palette.MachineIdle, _small);

            float buttonWidth = 54f * s;
            var left = new Rect(panel.xMax - 14f * s - buttonWidth * 2f - 6f * s, line.y, buttonWidth, 22f * s);
            var right = new Rect(panel.xMax - 14f * s - buttonWidth, line.y, buttonWidth, 22f * s);

            if (Button(left, "◀ turn", ButtonFill, _palette.HudText, s)) _rotate?.Invoke(-1);
            if (Button(right, "turn ▶", ButtonFill, _palette.HudText, s)) _rotate?.Invoke(+1);

            // With something selected the card also carries the keyboard-free way back to reading the
            // map: the key is Escape, the button is the same door.
            if (!_world.SelectedKind.HasValue) return;

            var inspect = new Rect(panel.xMax - 14f * s - buttonWidth * 3f - 18f * s, line.y,
                74f * s, 22f * s);
            if (Button(inspect, "inspect", ButtonFill, _palette.HudText, s)) _selectKind?.Invoke(null);
        }

        /// <summary>
        /// The card with nothing selected: what the player read, not what they are about to build.
        ///
        /// Its lines are the building's *state*, which is the one thing the build card cannot show - the
        /// ports its belts are actually wired to, what it is doing this second, whether it is starved or
        /// jammed - and every one is read from the same state the tick runs on. The blurb and the cost
        /// are left out on purpose: those are the catalogue's business.
        ///
        /// Nothing pinned - or a pin whose building has since been removed - is not an empty card: it
        /// says what the state is for and how to leave it.
        /// </summary>
        private void DrawInspectInfo(Rect panel, float s)
        {
            Panel(panel);

            Int2 cell = _world.InspectedCell;

            if (_world.Machines.TryGetSnapshot(cell, out MachineSnapshot machine))
            {
                DrawMachineCard(panel, s, cell, machine);
                return;
            }

            if (_world.Belts.TryGet(cell, out BeltState belt))
            {
                DrawBeltCard(panel, s, cell, belt);
                return;
            }

            TileKind tile = _world.TileGrid.Get(cell);
            if (tile == TileKind.Core)
            {
                DrawCoreCard(panel, s);
                return;
            }

            // The ground is read off the terrain layer rather than the tile kind, so an ore card always
            // has a shape to name and a rock card always has its kind: the tile kind says what is
            // standing here, and none of these three is a building. Whether the cell is worth reading at
            // all, though, is the simulation's one answer (<see cref="SimWorld.CanInspect"/> - the same
            // rule the click and the cursor's marker read), not a copy restated here: restating it would
            // let a clause added to the one rule miss this card.
            if (_world.CanInspect(cell))
            {
                DrawGroundCard(panel, s, cell, _world.Map.SpawnIndexOf(cell));
                return;
            }

            DrawInspectHint(panel, s);
        }

        private void DrawMachineCard(Rect panel, float s, Int2 cell, in MachineSnapshot machine)
        {
            MachineDef def = _world.Content.Machine(machine.Build);
            Color category = CategoryColor(machine.Build);

            float y = InspectHeader(panel, s, def.Name, category, RoleOf(machine.Build) + "   " + cell);

            InspectLine(panel, s, ref y, MachineReading.Ports(machine), _palette.MachineIdle);

            ReadingNote note = MachineReading.LiveNote(_world, cell, machine);
            InspectLine(panel, s, ref y, note.Text, NoteColour(note.Severity));

            string stats = Stats(machine.Build);
            if (!string.IsNullOrEmpty(stats)) InspectLine(panel, s, ref y, stats, category);

            if (machine.MaxHp > 0f)
            {
                bool hurting = machine.HealthFraction < 0.4f;
                InspectLine(panel, s, ref y, "holding " + Mathf.CeilToInt(machine.Hp) + " / " +
                    Mathf.CeilToInt(machine.MaxHp) + " health",
                    hurting ? _palette.HudWarn : _palette.HudGood);
            }

            InspectLine(panel, s, ref y, MachineReading.FacingNote(def, machine, _world.Content), _palette.MachineIdle);
        }

        private void DrawBeltCard(Rect panel, float s, Int2 cell, in BeltState belt)
        {
            MachineDef def = _world.Content.Machine(BuildKind.Belt);

            float y = InspectHeader(panel, s, def.Name, CategoryColor(BuildKind.Belt),
                RoleOf(BuildKind.Belt) + "   " + cell);

            if (belt.Jammed)
            {
                // A jam arrives with the shape that caused it on the belt; name that shape when it is
                // there rather than asking the content table about "nothing", which has no glyph.
                string culprit = belt.Item == ShapeType.None ? string.Empty : " by " + Glyph(belt.Item);
                InspectLine(panel, s, ref y,
                    "JAMMED" + culprit + " — the wrong shape for whatever this segment feeds",
                    _palette.HudWarn);
            }
            else if (belt.HasItem)
                InspectLine(panel, s, ref y,
                    "carrying " + Glyph(belt.Item) + " · " + Mathf.RoundToInt(belt.Progress * 100f) +
                    "% across", _palette.HudText);
            else
                InspectLine(panel, s, ref y, "empty — nothing riding it right now", _palette.HudText);

            InspectLine(panel, s, ref y, "carries its item to the " + MachineReading.DirName(belt.Direction) +
                " · one item per cell, so this is also its rate", _palette.MachineIdle);
        }

        private void DrawCoreCard(Rect panel, float s)
        {
            float y = InspectHeader(panel, s, "Core", _palette.Core, "the bank");

            bool hurting = _world.Core.HealthFraction < 0.4f;
            InspectLine(panel, s, ref y, "holding " + Mathf.CeilToInt(_world.Core.Hp) + " / " +
                Mathf.CeilToInt(_world.Core.MaxHp) + " health", hurting ? _palette.HudWarn : _palette.HudGood);
            InspectLine(panel, s, ref y, "circles belted into it become spendable", _palette.MachineIdle);
            InspectLine(panel, s, ref y, "losing it loses the run", _palette.MachineIdle);
        }

        /// <summary>
        /// The ground: the map's own obstacle, a vein of ore, an entry point the waves walk in at, or
        /// two of them at once - a map may open a door over ore, so the card says what is there rather
        /// than choosing between them. None of them is a building, which is exactly why it is worth
        /// reading.
        /// </summary>
        private void DrawGroundCard(Rect panel, float s, Int2 cell, int doorway)
        {
            TerrainKind kind = _world.Terrain.KindAt(cell);

            // The map's own obstacle comes first: rock, water and the void are what a wave's route is
            // chosen around, and a player looking at one is asking "is this mine or the map's?".
            if (kind.IsObstacle())
            {
                DrawObstacleCard(panel, s, cell, kind);
                return;
            }

            bool ore = _world.Terrain.HasOre(cell);
            ShapeType shape = ore ? _world.Terrain.OreAt(cell) : ShapeType.None;
            Color oreColour = _palette.ShapeColor(shape);

            string title = doorway < 0 ? "Ore patch" : ore ? "Entry point · ore" : "Entry point";
            float y = InspectHeader(panel, s, title, doorway < 0 ? oreColour : _palette.SpawnMarker,
                cell.ToString());

            if (doorway >= 0)
            {
                InspectLine(panel, s, ref y, WavesAt(doorway), _palette.SpawnMarker);
                InspectLine(panel, s, ref y, "enemies enter on this tile and walk at the Core",
                    _palette.MachineIdle);
            }

            if (!ore) return;

            InspectLine(panel, s, ref y, "yields " + Glyph(shape) + " — only a drill may be built on it",
                oreColour);

            if (TryVeinAt(cell, out ShapePatch vein))
                InspectLine(panel, s, ref y, vein.Width + " × " + vein.Height + " vein in the ground",
                    _palette.MachineIdle);
        }

        /// <summary>
        /// A tile of the ground a map draws as an obstacle. Drawn in the same colours the terrain view
        /// draws it in, so the card and the map agree about which tile is being described - and the line
        /// under it says the one thing worth knowing, which is that this wall is the map's and cannot
        /// fall.
        /// </summary>
        private void DrawObstacleCard(Rect panel, float s, Int2 cell, TerrainKind kind)
        {
            Color colour = TerrainRenderer.ColorOf(kind, _palette);
            float y = InspectHeader(panel, s, kind.ToString(), colour, cell.ToString());

            InspectLine(panel, s, ref y, ObstacleNote(kind), colour);
            InspectLine(panel, s, ref y, "map ground — nothing may be built on it, and it cannot fall",
                _palette.MachineIdle);
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

        /// <summary>Which waves enter the map at this door, read off the map's own wave table - the
        /// question a player looking at a doorway is asking, and the reason a door no wave uses is worth
        /// saying out loud instead of leaving blank.</summary>
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

        /// <summary>The vein rectangle covering this cell, for its size: the map's own patch table, so
        /// the number the card prints is the ground the map laid, not a guess from the cursor. False for
        /// ore a test stamped straight into the field, which has no rectangle to report.</summary>
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

        private void DrawInspectHint(Rect panel, float s)
        {
            float y = InspectHeader(panel, s, "INSPECT", _palette.HudAccent, "nothing selected");

            Label(new Rect(panel.x + 14f * s, y, panel.width - 28f * s, 32f * s),
                "click a building, a vein or a doorway to read it: what it is, what it is doing, and " +
                "how its belts wire it up", _palette.HudText, _wrap);
            y += 34f * s;

            InspectLine(panel, s, ref y,
                "click a build tile or press 1–9 to build one · the same one again to read instead",
                _palette.MachineIdle);
        }

        /// <summary>The card's title row and the accent stripe, and the y the first line of body text
        /// goes at: every card opens the same way, with the building's own category colour.</summary>
        private float InspectHeader(Rect panel, float s, string name, Color accent, string right)
        {
            Fill(new Rect(panel.x, panel.y, 3f * s, panel.height), accent);

            Rect line = new Rect(panel.x + 14f * s, panel.y + 9f * s, panel.width - 28f * s, 18f * s);
            Label(line, name, _palette.HudText, _title);
            Label(line, right, _palette.MachineIdle, _smallRight);

            return line.y + 22f * s;
        }

        private void InspectLine(Rect panel, float s, ref float y, string text, Color colour)
        {
            Label(new Rect(panel.x + 14f * s, y, panel.width - 28f * s, 15f * s), text, colour, _small);
            y += 17f * s;
        }

        /// <summary>The colour a live note is said in - which colour means "the player should look" is
        /// the palette's question; what the note *means* is <see cref="MachineReading"/>'s, and the
        /// EditMode tests cross that interface to check it. An uncoloured severity is an error, not a
        /// quiet fallback to plain text.</summary>
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

        // The catalogue's words and numbers all live in MachineReading now - the one behaviour
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

        // ------------------------------------------------------------------ banner

        /// <summary>The one thing that has to be unmissable: the run is over, or the game is paused.
        /// An ended run also reports itself - shots fired, circles banked - so "do better" has numbers.</summary>
        private void DrawBanner(float s)
        {
            string text;
            Color colour;

            switch (_world.Status)
            {
                case GameStatus.Lost:
                    text = "CORE DESTROYED\n" + StatsLine() + "\nR to restart";
                    colour = _palette.HudWarn;
                    break;
                case GameStatus.Won:
                    // Naming the map that is coming needs the catalog, which the driver always wires but
                    // a test may not: a HUD with no campaign to walk still says the map was cleared, it
                    // just cannot promise what is next - and "campaign complete" is the honest reading
                    // of that, not a guess. That "no campaign" case is defended once, on the index, and
                    // the same defended index goes to both calls - the gauge is the catalog's own
                    // question (<see cref="MapCatalog.HasNext"/>) over exactly the index NextAfter is
                    // named, so the banner and the guard cannot disagree about it the way two
                    // hand-written comparisons could.
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
                    break;
                default:
                    if (!_world.Paused) return;
                    text = "PAUSED\nSpace to resume · R to restart";
                    colour = _palette.HudAccent;
                    break;
            }

            float width = 460f * s;
            float height = 150f * s;
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.42f, width, height);

            Panel(panel);
            Label(new Rect(panel.x, panel.y + 22f * s, panel.width, panel.height - 30f * s), text, colour, _banner);
        }

        /// <summary>The run in one line: how much shooting the defence did, and how much economy
        /// the player built - the two axes "a better run" moves on.</summary>
        private string StatsLine()
        {
            return _world.ShotsFired + " shots fired · " + _world.Economy.TotalBanked + " " +
                Glyph(ShapeType.Circle) + " banked";
        }

        // ------------------------------------------------------------------ drawing helpers

        private Color ButtonFill => Blend(_palette.HudPanel, _palette.HudText, 0.09f);

        private void EnsureStyles()
        {
            float s = HudLayout.Scale(Screen.height);
            if (_title != null && Mathf.Abs(s - _stylesScale) < StyleScaleTolerance) return;

            _stylesScale = s;

            _title = MakeStyle(15, s, FontStyle.Bold, TextAnchor.UpperLeft, wrap: false);
            _labelCenter = MakeStyle(13, s, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _small = MakeStyle(12, s, FontStyle.Normal, TextAnchor.UpperLeft, wrap: false);
            _smallRight = MakeStyle(12, s, FontStyle.Normal, TextAnchor.UpperRight, wrap: false);
            _costRight = MakeStyle(12, s, FontStyle.Bold, TextAnchor.UpperRight, wrap: false);
            _tiny = MakeStyle(11, s, FontStyle.Normal, TextAnchor.UpperLeft, wrap: false);
            _tinyRight = MakeStyle(11, s, FontStyle.Normal, TextAnchor.UpperRight, wrap: false);
            _tinyCenter = MakeStyle(10, s, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _wrap = MakeStyle(11, s, FontStyle.Normal, TextAnchor.UpperLeft, wrap: true);
            _button = MakeStyle(13, s, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
            _banner = MakeStyle(22, s, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: true);

            // The stockpile is the number the game is played against, so it is the largest thing the
            // HUD draws; a toast is the loudest thing it says, in bold, on its own panel.
            _resource = MakeStyle(26, s, FontStyle.Bold, TextAnchor.MiddleLeft, wrap: false);
            _alert = MakeStyle(15, s, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: false);
        }

        /// <summary>Text colour is applied per label through GUI.contentColor, so every style's own
        /// colour is white and no per-frame style clones are needed. The font scales with the panels -
        /// one <see cref="HudLayout.Scale"/> for both, or a 4K screen would be a big box of small
        /// text.</summary>
        private static GUIStyle MakeStyle(int size, float s, FontStyle font, TextAnchor anchor, bool wrap)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size * s)),
                fontStyle = font,
                alignment = anchor,
                wordWrap = wrap,
                richText = false,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 0, 0, 0),
            };
        }

        /// <summary>Panel backdrop, with the thin edge that keeps a dark card from bleeding into the map.</summary>
        private void Panel(Rect rect)
        {
            Fill(rect, _palette.HudPanel);
            Border(rect, new Color(_palette.HudText.r, _palette.HudText.g, _palette.HudText.b, 0.12f), 1f);
        }

        private void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = previous;
        }

        /// <summary>An inset border drawn as four filled edges, all scaled together.</summary>
        private void Border(Rect rect, Color colour, float thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            Fill(new Rect(rect.x, rect.y + thickness, thickness, rect.height - 2f * thickness), colour);
            Fill(new Rect(rect.xMax - thickness, rect.y + thickness, thickness, rect.height - 2f * thickness), colour);
        }

        private void Label(Rect rect, string text, Color colour, GUIStyle style)
        {
            Color previous = GUI.contentColor;
            GUI.contentColor = colour;
            GUI.Label(rect, text, style);
            GUI.contentColor = previous;
        }

        /// <summary>
        /// A HUD button drawn from the palette rather than the skin, so it matches the cards. It fires
        /// on the press, which is what makes the build bar feel immediate; the world never sees the
        /// click because <see cref="PointerOverHud"/> already claimed it for the UI.
        /// </summary>
        private bool Button(Rect rect, string text, Color fill, Color textColour, float s)
        {
            bool hovered = PointerOver(rect);
            Fill(rect, hovered ? Blend(fill, _palette.HudText, 0.16f) : fill);
            Border(rect, hovered ? _palette.HudAccent : Blend(_palette.HudText, _palette.HudPanel, 0.35f), 1f * s);
            Label(rect, text, textColour, _button);

            if (hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }

            return false;
        }

        private static Color Blend(Color from, Color to, float t) => Color.Lerp(from, to, t);

        /// <summary>The same colour, faded: what a refusal and the number it is about are drawn with
        /// while they age out.</summary>
        private static Color WithAlpha(Color colour, float alpha)
            => new Color(colour.r, colour.g, colour.b, colour.a * alpha);
    }
}
