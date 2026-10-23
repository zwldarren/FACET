using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>The free camera the design doc's controls call for: WASD or arrows pan, the wheel zooms, and
    /// the pan is bounded by the map - by as much of the screen as the HUD leaves, so an edge tile can be
    /// brought out from under the panels, plus a little slack. The bound itself is <see cref="PanLimits"/>;
    /// the rig only feeds it the screen, the zoom and the slack.
    ///
    /// <c>C</c> puts the Core back in the middle of the screen: a map can be taller than a window, and once
    /// the locator has told the player their Core is off view, the next thing they need is the way back. The
    /// Core is one keypress from anywhere.
    ///
    /// It reads input itself rather than going through the simulation: panning is a view concern, nothing
    /// replays it, and routing it through the tick would make the camera step at 30 Hz for no reason.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CameraRig : MonoBehaviour
    {
        [Tooltip("How many screenfuls the camera crosses per second. Zoom-independent by design.")]
        [SerializeField] private float panScreensPerSecond = 0.9f;

        [Tooltip("Zoom multiplier applied per wheel notch.")]
        [SerializeField] private float zoomStepPerNotch = 0.88f;

        [SerializeField] private float minZoom = 4f;
        [SerializeField] private float maxZoom = 18f;
        [SerializeField] private float smoothTime = 0.08f;

        [Tooltip("Smoothing for an automatic glide (the tutorial's camera). Slower than a player's pan on " +
            "purpose: a long move the player did not ask for should read as the view travelling, not as a cut.")]
        [SerializeField] private float glideSmoothTime = 0.45f;

        [Tooltip("The key that centres the view on the run's one losing object.")]
        [SerializeField] private Key focusCoreKey = Key.C;

        [Tooltip("How far past the map's edge the camera may still pan, as a fraction of the screen. " +
            "This is the margin an edge row is panned out into instead of stopping against the panel " +
            "that was hiding it; 0 presses the map against that panel instead.")]
        [SerializeField] private float edgeSlackScreens = PanLimits.DefaultEdgeSlack;

        private Camera _camera;
        private TileGrid _grid;
        private Vector3 _target;
        private float _targetZoom;
        private Vector3 _panVelocity;
        private float _zoomVelocity;
        private bool _tutorialUp;

        /// <summary>Is the view currently travelling to a point nobody asked for? Only then does the longer
        /// <see cref="glideSmoothTime"/> apply; a player's pan stays immediate, and a pan or zoom ends the
        /// glide at once.</summary>
        private bool _autoGlide;

        /// <summary>When the player last panned or zoomed, in unscaled seconds. The tutorial's camera reads
        /// it to stand aside for a while: a player who has just moved the view is looking at something, and
        /// an automatic pan at that moment would take the map away from under them.</summary>
        public float LastManualInputTime { get; private set; } = float.NegativeInfinity;

        /// <summary>The point a map is played around - the Core's centre, which <see cref="Initialize"/> is handed
        /// and <see cref="focusCoreKey"/> returns to.</summary>
        private Vector2 _home;

        /// <summary><paramref name="tutorialUp"/> is whether the HUD's "how to play" card is on screen - a solid
        /// panel on the first map, so the map has to get out from under it. Passed in rather than read here: the
        /// rig knows the map, not the campaign.
        /// <paramref name="focus"/> is the point each map opens looking at - the Core in the driver's call, which
        /// runs again on every map swap, not just the first. It used to be the map's own middle; on a map small
        /// enough to fit the screen the two are the same view, but on one that is not, the middle is empty
        /// corridor. The campaign's first map is a strip three screens tall with its Core at the south end, and
        /// the thing a player must be watching on the first frame is that Core.</summary>
        public void Initialize(TileGrid grid, Vec2 focus, bool tutorialUp = false)
        {
            _camera = GetComponent<Camera>();
            _grid = grid;
            _tutorialUp = tutorialUp;

            _targetZoom = Mathf.Clamp(_camera.orthographicSize, minZoom, maxZoom);

            _home = new Vector2(focus.X, focus.Y);

            // Start on the focus point rather than wherever the scene happened to author the camera.
            _target = Clamp(new Vector3(focus.X, focus.Y, transform.position.z), _targetZoom);
            transform.position = _target;
            _camera.orthographicSize = _targetZoom;
            _autoGlide = false;
        }

        private void Update()
        {
            if (_camera == null || _grid == null) return;

            Pan();
            Zoom();
            Focus();
        }

        private void LateUpdate()
        {
            if (_camera == null || _grid == null) return;

            _camera.orthographicSize = Mathf.SmoothDamp(
                _camera.orthographicSize, _targetZoom, ref _zoomVelocity, smoothTime);

            // Clamp against the size we actually have this frame, not the one we are heading for,
            // or the view would overshoot the map edge while a zoom settles.
            float smooth = _autoGlide ? glideSmoothTime : smoothTime;
            transform.position = Vector3.SmoothDamp(
                transform.position, Clamp(_target, _camera.orthographicSize), ref _panVelocity, smooth);

            // The glide is over once the view has arrived (or the player has taken it, which clears the
            // flag in Pan/Zoom/Focus); the next player pan then gets its immediate smoothing back.
            if (_autoGlide && ((Vector2)(_target - transform.position)).sqrMagnitude < 0.01f) _autoGlide = false;
        }

        /// <summary>Put <paramref name="point"/> in the middle of the view, through the rig's own smoothing
        /// and pan bound. The tutorial's camera is the caller: a lesson whose next tile is off screen is
        /// asking the player to pan before they can even see what it means, and this is the one view
        /// operation the rig already knows how to do. A player's own pan still wins - this sets the target
        /// once rather than holding it.</summary>
        public void GlideTo(Vector2 point)
        {
            if (_camera == null || _grid == null) return;

            _target = Clamp(new Vector3(point.x, point.y, transform.position.z), _targetZoom);
            _autoGlide = true;
        }

        /// <summary><c>C</c>: put the Core in the middle of the view. The pan bound is applied to the destination,
        /// so the key cannot shove the map off the open screen any more than a drag can, and the glide uses the
        /// rig's smoothing rather than a cut - a hard cut leaves the player no idea which way they went.</summary>
        private void Focus()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!keyboard[focusCoreKey].wasPressedThisFrame) return;

            LastManualInputTime = Time.unscaledTime;
            _autoGlide = false;
            _target = Clamp(new Vector3(_home.x, _home.y, transform.position.z), _targetZoom);
        }

        private void Pan()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = 0f;
            float y = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            if (x == 0f && y == 0f) return;

            LastManualInputTime = Time.unscaledTime;
            _autoGlide = false;
            var dir = new Vector2(x, y).normalized;   // so a diagonal pan is not faster than a straight one
            float height = _camera.orthographicSize;
            float width = height * _camera.aspect;
            float step = panScreensPerSecond * Time.deltaTime;

            _target += new Vector3(dir.x * width * step, dir.y * height * step, 0f);
            _target = Clamp(_target, _targetZoom);
        }

        private void Zoom()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            LastManualInputTime = Time.unscaledTime;
            _autoGlide = false;
            _targetZoom = Mathf.Clamp(
                _targetZoom * Mathf.Pow(zoomStepPerNotch, WheelNotches(scroll)), minZoom, maxZoom);
            _target = Clamp(_target, _targetZoom);
        }

        /// <summary>Wheel deltas arrive as 120 per notch on Windows and as 1 on some other backends, so a raw
        /// division would make the wheel dead on one of them. Take whichever reading fits.</summary>
        private static float WheelNotches(float scroll)
        {
            float notches = Mathf.Abs(scroll) <= 1.5f ? scroll : scroll / 120f;
            return Mathf.Clamp(notches, -3f, 3f);
        }

        /// <summary>Say whether the HUD's "how to play" card is up. The player can hide or show it mid-map, so
        /// the driver relays it every frame; the pan bound follows on the next smoothing step, which clamps
        /// against it anew.</summary>
        public void SetTutorialUp(bool up) => _tutorialUp = up;

        /// <summary>The pan bound for a given zoom; the rule is <see cref="PanLimits"/>. The rig only hands it the
        /// screen, the zoom and <see cref="edgeSlackScreens"/>.</summary>
        private Vector3 Clamp(Vector3 position, float zoom)
        {
            return PanLimits.Clamp(position, zoom, _camera.aspect, Screen.width, Screen.height,
                _grid.Width, _grid.Height,
                HudLayout.Insets(HudLayout.Scale(Screen.height), _tutorialUp),
                edgeSlackScreens);
        }
    }

    /// <summary>
    /// Moves the camera for a lesson: to the tile a line is asking for, and to the wave when one arrives.
    ///
    /// <b>Why it exists.</b> A tutorial narrows the map to one answer, but a map can be several screens
    /// tall: the drill a line names is off to the right, the cutter at the corridor mouth, and wave 1 walks
    /// in from a door at the far end. The guide's edge chevron says <em>which way</em>, but the player still
    /// has to pan before the sentence and the ghost make sense. The camera is the cheapest way to put the
    /// lesson where the player is looking - one glide on the frame the line changes, not a cut, so the view
    /// keeps its bearings.
    ///
    /// <b>What moves it.</b> Three edges, and no continuous following:
    /// a line changes (after <see cref="TaskSettleSeconds"/>, so the card is read first), a wave starts
    /// running (to the most advanced enemy, after <see cref="WaveSettleSeconds"/>), and the Core is first
    /// hit in a streak (to the Core, so the one losing object is on screen). A new line always pulls the
    /// view to its layout - the tour is the lesson - while a wave or a Core hit only moves when it is not
    /// already visible: the picture is what the player needs, not the pan. A wave's look is a <em>glance</em>:
    /// after <see cref="WaveGlanceSeconds"/> the view drifts back to the one the player had, unless they
    /// have taken the camera themselves in the meantime.
    ///
    /// <b>What it never does.</b> It does not fight the player: panning or zooming is remembered by
    /// <see cref="CameraRig.LastManualInputTime"/>, and a scheduled glide inside
    /// <see cref="ManualQuietSeconds"/> of that input is dropped rather than queued. It also only runs on a
    /// map that teaches (<see cref="TutorialState.Active"/>), so a normal level's camera is exactly the free
    /// camera it always was.
    ///
    /// It is a plain class rather than a view: it reads the world's facts and asks the rig to glide, and the
    /// driver calls it once per frame after the tick loop - the same order the HUD and the guide see.
    /// </summary>
    public sealed class TutorialCamera
    {
        /// <summary>How long a new line is left alone before the camera moves. One sentence of reading time,
        /// and long enough that a drag of belts which ticked the last line is finished.</summary>
        private const float TaskSettleSeconds = 1.1f;

        /// <summary>How long a wave is left alone before the camera looks at it: enough for the first
        /// walker to be on the field rather than a spawn point with nothing on it yet.</summary>
        private const float WaveSettleSeconds = 0.5f;

        /// <summary>The Core is the urgent one: a shorter settle than the other two, because the health bar
        /// moving is the news.</summary>
        private const float CoreSettleSeconds = 0.15f;

        /// <summary>How long a pan or zoom stands the automatic camera down. Longer than a glance - the
        /// player who just moved the view is mid-thought.</summary>
        private const float ManualQuietSeconds = 2.5f;

        /// <summary>A scheduled move older than this is dropped: the moment it belonged to has passed.</summary>
        private const float PendingLifetime = 8f;

        /// <summary>How long after a wave starts to wait for its first enemy before giving up.</summary>
        private const float WaveSearchSeconds = 6f;

        /// <summary>How long the camera lingers on a wave before drifting back to the view the player had.
        /// "Go and look" is a glance, not a new home: the player's own view is where the building is.</summary>
        private const float WaveGlanceSeconds = 5f;

        /// <summary>At most one Core focus per this many seconds, so a Core under sustained fire does not
        /// hold the camera hostage.</summary>
        private const float CoreRepeatSeconds = 12f;

        /// <summary>How far inside the screen a point counts as visible. The same arithmetic the locator
        /// uses, so a tile the arrows call visible is a tile the camera will not move away from.</summary>
        private const float VisibleMarginPixels = 10f;

        private readonly CameraRig _rig;
        private readonly Camera _camera;

        /// <summary>The locator's own viewport reader, reused so "is it on screen" means the same thing
        /// here as it does for the edge arrows - panels included.</summary>
        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

        /// <summary>Scratch for the enemy walk; owned here so a wave focus asks its question without
        /// allocating every frame.</summary>
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>(64);

        private int _seenStep = -1;
        private WavePhase _seenPhase = WavePhase.Held;

        /// <summary>Is a wave waiting for its first enemy? A wave's phase changes before its first group's
        /// delay has run, so the focus has to wait for something to look at.</summary>
        private bool _waveSearch;
        private float _waveSearchUntil;

        private bool _hasPending;
        private Vector2 _pending;
        private Vector2 _pendingMin;
        private Vector2 _pendingMax;
        private bool _pendingForced;
        private bool _pendingWave;
        private float _pendingAt;
        private float _pendingUntil;

        /// <summary>The glance back: where the camera was when it went to look at a wave, and when it may
        /// drift home. Armed only by a wave focus and disarmed by anything else the player or the lesson
        /// does, so it can never fight a newer request.</summary>
        private bool _returnArmed;
        private float _returnAt;
        private Vector2 _returnPoint;
        private float _returnManualAt;

        private bool _coreHitSeen;
        private float _lastCoreFocus = float.NegativeInfinity;

        public TutorialCamera(CameraRig rig, Camera camera)
        {
            _rig = rig;
            _camera = camera;
        }

        /// <summary>Forget the run the director was watching - a map swap is a new lesson, and a line from
        /// the old one must not be mistaken for the new one's.</summary>
        public void Reset()
        {
            _seenStep = -1;
            _seenPhase = WavePhase.Held;
            _waveSearch = false;
            _hasPending = false;
            _returnArmed = false;
            _coreHitSeen = false;
            _lastCoreFocus = float.NegativeInfinity;
        }

        /// <summary>One frame of looking. Called after the tick loop, so a line or a wave that changed this
        /// frame is already visible in the world; <paramref name="tutorialUp"/>, <paramref name="infoGrown"/>
        /// and <paramref name="barTiles"/> are the HUD's own panel facts, which decide whether a point is
        /// hidden under a card. <paramref name="interacting"/> is the mouse being down on the map: a glide
        /// under a drag would move the tile the player is aiming at, so it waits for the hand to come up.</summary>
        public void Tick(SimWorld world, bool tutorialUp, bool infoGrown, int barTiles, bool interacting, float now)
        {
            if (world == null || _rig == null || _camera == null) return;

            TutorialState tutorial = world.Tutorial;
            if (!tutorial.Active) return;

            TrackTask(tutorial, now);
            TrackWave(world, now);
            TrackCoreHit(world, now);

            // A placement the player just made is the same kind of signal as a held button: they are working
            // where the view is. The glide waits for a second and a half of quiet rather than moving the map
            // under their next click.
            bool busy = interacting ||
                        world.Events.TryLastWithin(SimEventKind.Built,
                            world.TickCount - (int)(1.5f * SimConfig.TickRate), out _);

            Resolve(tutorialUp, infoGrown, barTiles, busy, now);
            UpdateReturn(now, busy);
        }

        /// <summary>A line is a place on the map: when the lesson advances, frame the layout it draws - the
        /// whole run of belts, not just the one tile the card points at, because the player has to build all
        /// of it. A line with no one place - "bank a shape" - moves nothing.</summary>
        private void TrackTask(TutorialState tutorial, float now)
        {
            int step = tutorial.Complete ? -1 : tutorial.StepsCompleted;
            if (step == _seenStep) return;

            _seenStep = step;
            if (step < 0) return;

            TutorialTask task = tutorial.Current;
            if (!TryFocusOf(in task, out Vector2 at, out Vector2 min, out Vector2 max)) return;

            Schedule(at, min, max, now + TaskSettleSeconds, now, force: true);
        }

        /// <summary>What a line is asking the camera to show: the centre of its blueprint, with the
        /// rectangle that has to be visible for the picture to be complete. A line with no blueprint falls
        /// back to its one target tile; a line with neither has nothing to show.</summary>
        private static bool TryFocusOf(in TutorialTask task, out Vector2 at, out Vector2 min, out Vector2 max)
        {
            if (task.Ghost.Length > 0)
            {
                float loX = float.MaxValue, loY = float.MaxValue;
                float hiX = float.MinValue, hiY = float.MinValue;
                for (int i = 0; i < task.Ghost.Length; i++)
                {
                    Int2 cell = task.Ghost[i].Cell;
                    if (cell.X < loX) loX = cell.X;
                    if (cell.Y < loY) loY = cell.Y;
                    if (cell.X > hiX) hiX = cell.X;
                    if (cell.Y > hiY) hiY = cell.Y;
                }

                min = new Vector2(loX, loY);
                max = new Vector2(hiX + 1f, hiY + 1f);
                at = (min + max) * 0.5f;
                return true;
            }

            if (!task.Target.HasValue)
            {
                at = default;
                min = default;
                max = default;
                return false;
            }

            Int2 target = task.Target.Value;
            at = new Vector2(target.X + 0.5f, target.Y + 0.5f);
            min = max = at;
            return true;
        }

        /// <summary>A wave is an event: when the phase turns to <see cref="WavePhase.Running"/>, look at the
        /// walker closest to the Core. The first frame of a wave can be before the first walker exists, so
        /// the search retries for <see cref="WaveSearchSeconds"/> rather than aiming at an empty doorway.</summary>
        private void TrackWave(SimWorld world, float now)
        {
            WavePhase phase = world.Waves.Phase;
            if (phase == WavePhase.Running && _seenPhase != WavePhase.Running && world.Waves.CurrentWave > 0)
            {
                _waveSearch = true;
                _waveSearchUntil = now + WaveSearchSeconds;
            }

            _seenPhase = phase;

            if (!_waveSearch) return;
            if (now > _waveSearchUntil)
            {
                _waveSearch = false;
                return;
            }

            if (!TryLeadingEnemy(world, out Vector2 at)) return;

            _waveSearch = false;
            Schedule(at, at, at, now + WaveSettleSeconds, now, wave: true);
        }

        /// <summary>The Core being hit is the one piece of news the player must not miss. It is shown once
        /// per hit streak, and at most once per <see cref="CoreRepeatSeconds"/>, so sustained fire cannot make
        /// the camera a leash.</summary>
        private void TrackCoreHit(SimWorld world, float now)
        {
            bool hit = ThreatRadar.CoreIsBeingHit(world, 2);
            if (!hit)
            {
                _coreHitSeen = false;
                return;
            }

            if (_coreHitSeen) return;
            _coreHitSeen = true;

            if (now - _lastCoreFocus < CoreRepeatSeconds) return;
            _lastCoreFocus = now;

            Vector2 core = ThreatRadar.CoreCentre(world);
            Schedule(core, core, core, now + CoreSettleSeconds, now);
        }

        /// <summary>The enemy nearest the Core: the one whose arrival is the news, and the one a player
        /// watching the lane would be looking at.</summary>
        private bool TryLeadingEnemy(SimWorld world, out Vector2 at)
        {
            world.Enemies.GetEnemies(_enemies);
            if (_enemies.Count == 0)
            {
                at = default;
                return false;
            }

            Vec2 core = world.Core.Center;
            float best = float.MaxValue;
            at = default;

            for (int i = 0; i < _enemies.Count; i++)
            {
                Vec2 p = _enemies[i].Position;
                float dx = p.X - core.X;
                float dy = p.Y - core.Y;
                float sq = dx * dx + dy * dy;
                if (sq >= best) continue;

                best = sq;
                at = new Vector2(p.X, p.Y);
            }

            return true;
        }

        private void Schedule(Vector2 at, Vector2 min, Vector2 max, float when, float now,
            bool force = false, bool wave = false)
        {
            // Anything newer than the glance overrides it; a stale return would otherwise move the view a
            // second time after the next line has already asked for its own tile.
            _returnArmed = false;

            _hasPending = true;
            _pending = at;
            _pendingMin = min;
            _pendingMax = max;
            _pendingForced = force;
            _pendingWave = wave;
            _pendingAt = when;
            _pendingUntil = now + PendingLifetime;
        }

        /// <summary>Run the one scheduled move when it is due, unless the player has taken the camera, the
        /// moment has gone, they are still building, or the point is already in front of them.</summary>
        private void Resolve(bool tutorialUp, bool infoGrown, int barTiles, bool busy, float now)
        {
            if (!_hasPending) return;
            if (now > _pendingUntil)
            {
                _hasPending = false;
                return;
            }

            if (now < _pendingAt) return;
            if (busy) return;
            _hasPending = false;
            if (now - _rig.LastManualInputTime < ManualQuietSeconds) return;

            // A line is the lesson's own beat, so it moves the view even when the layout happens to be on
            // screen: the tour is the point, and the player's next move is the ghost in the middle. A wave
            // or a Core hit only asks for a look when the player cannot already see it.
            if (!_pendingForced && IsVisible(tutorialUp, infoGrown, barTiles, _pendingMin, _pendingMax)) return;

            _rig.GlideTo(_pending);

            if (!_pendingWave) return;

            _returnArmed = true;
            _returnPoint = _camera.transform.position;
            _returnManualAt = _rig.LastManualInputTime;
            _returnAt = now + WaveGlanceSeconds;
        }

        /// <summary>After the glance at a wave, drift back to the view the player had - unless they have
        /// taken the camera themselves in the meantime, or a newer lesson beat has something to show.</summary>
        private void UpdateReturn(float now, bool busy)
        {
            if (!_returnArmed || now < _returnAt) return;

            _returnArmed = false;
            if (_hasPending || busy) return;
            if (_rig.LastManualInputTime > _returnManualAt) return;

            _rig.GlideTo(_returnPoint);
        }

        /// <summary>Is the whole rectangle on the open part of the screen? The locator's question, asked the
        /// same way, so the camera and the edge arrows cannot disagree about what the player can see. A
        /// blueprint that is partly off screen pulls the view to its middle; one already whole does not.</summary>
        private bool IsVisible(bool tutorialUp, bool infoGrown, int barTiles, Vector2 min, Vector2 max)
        {
            ThreatRadar.Viewport view =
                _screen.Measure(_camera, tutorialUp, VisibleMarginPixels, infoGrown, barTiles);
            return view.IsOpen(min) && view.IsOpen(max);
        }
    }
}
