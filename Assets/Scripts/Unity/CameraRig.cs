using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>The free camera: WASD or arrows pan, the wheel zooms, <c>C</c> recentres the Core, and the
    /// pan is bounded by <see cref="PanLimits"/> so an edge tile can be dragged out from under the HUD. It
    /// reads input itself - panning is a view concern and nothing replays it.</summary>
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

        /// <summary>Only an automatic move glides slowly; the player's own pan stays immediate, and a pan or
        /// a zoom ends the glide at once.</summary>
        private bool _autoGlide;

        /// <summary>When the player last panned or zoomed, in unscaled seconds. The tutorial's camera stands
        /// aside for a while after it: they are looking at something.</summary>
        public float LastManualInputTime { get; private set; } = float.NegativeInfinity;

        /// <summary>The Core's centre, which <see cref="Initialize"/> is handed and <c>C</c> returns to.</summary>
        private Vector2 _home;

        /// <summary><paramref name="tutorialUp"/> is the HUD's card being up: a solid panel on the first map,
        /// so the map has to get out from under it. <paramref name="focus"/> is the point each map opens
        /// looking at - the Core, not the map's middle, which on a tall map is empty corridor. Both are
        /// passed in because the rig knows the map, not the campaign.</summary>
        public void Initialize(TileGrid grid, Vec2 focus, bool tutorialUp = false)
        {
            _camera = GetComponent<Camera>();
            _grid = grid;
            _tutorialUp = tutorialUp;

            _targetZoom = Mathf.Clamp(_camera.orthographicSize, minZoom, maxZoom);

            _home = new Vector2(focus.X, focus.Y);

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

            // The glide is over once the view has arrived; a player's pan or zoom clears the flag in
            // Pan/Zoom/Focus and the next pan gets its immediate smoothing back.
            if (_autoGlide && ((Vector2)(_target - transform.position)).sqrMagnitude < 0.01f) _autoGlide = false;
        }

        /// <summary>Put <paramref name="point"/> in the middle of the view through the rig's own smoothing and
        /// pan bound. Sets the target once rather than holding it, so a player's own pan still wins.</summary>
        public void GlideTo(Vector2 point)
        {
            if (_camera == null || _grid == null) return;

            _target = Clamp(new Vector3(point.x, point.y, transform.position.z), _targetZoom);
            _autoGlide = true;
        }

        /// <summary><c>C</c>: put the Core in the middle of the view, through the pan bound so the key cannot
        /// shove the map off the open screen, and gliding rather than cutting so the player keeps their
        /// bearings.</summary>
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

        /// <summary>The player can hide or show the card mid-map, so the driver relays it every frame; the pan
        /// bound clamps against the new value on the next smoothing step.</summary>
        public void SetTutorialUp(bool up) => _tutorialUp = up;

        /// <summary>The pan bound for a given zoom; the rule is <see cref="PanLimits"/>.</summary>
        private Vector3 Clamp(Vector3 position, float zoom)
        {
            return PanLimits.Clamp(position, zoom, _camera.aspect, Screen.width, Screen.height,
                _grid.Width, _grid.Height,
                HudLayout.Insets(HudLayout.Scale(Screen.height), _tutorialUp),
                edgeSlackScreens);
        }
    }

    /// <summary>Moves the camera for a lesson: to a line's layout, and to a wave or a Core hit. Three edges,
    /// no continuous following, and a glide rather than a cut so the view keeps its bearings. A line always
    /// moves the view; a wave or a Core hit only when the thing is off the open screen, and a wave's look is a
    /// glance that drifts back. It never fights the player: a scheduled glide within
    /// <see cref="ManualQuietSeconds"/> of a pan or zoom is dropped rather than queued. Runs only on a map that
    /// teaches (<see cref="TutorialState.Active"/>), so a normal level's camera is the free camera.</summary>
    public sealed class TutorialCamera
    {
        /// <summary>One sentence of reading time, and long enough for a drag of belts that tipped the last line
        /// to finish.</summary>
        private const float TaskSettleSeconds = 1.1f;

        /// <summary>Enough for the first walker to be on the field.</summary>
        private const float WaveSettleSeconds = 0.5f;

        /// <summary>Shorter than the other two: the health bar moving is the news.</summary>
        private const float CoreSettleSeconds = 0.15f;

        /// <summary>Longer than a glance: a player who just moved the view is mid-thought.</summary>
        private const float ManualQuietSeconds = 2.5f;

        /// <summary>A scheduled move older than this is dropped: the moment it belonged to has passed.</summary>
        private const float PendingLifetime = 8f;

        /// <summary>How long after a wave starts to wait for its first enemy.</summary>
        private const float WaveSearchSeconds = 6f;

        /// <summary>"Go and look" is a glance, not a new home: the player's own view is where the building
        /// is.</summary>
        private const float WaveGlanceSeconds = 5f;

        /// <summary>At most one Core focus per this many seconds, so sustained fire cannot make the camera a
        /// leash.</summary>
        private const float CoreRepeatSeconds = 12f;

        /// <summary>How far inside the screen a point counts as visible - the locator's arithmetic, so a tile
        /// the arrows call visible is one the camera will not move away from.</summary>
        private const float VisibleMarginPixels = 10f;

        private readonly CameraRig _rig;
        private readonly Camera _camera;

        private readonly ThreatRadar.ScreenReader _screen = new ThreatRadar.ScreenReader();

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

        /// <summary>Where the camera was when it went to look at a wave, and when it may drift home. Armed
        /// only by a wave focus and disarmed by anything else, so it cannot fight a newer request.</summary>
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

        /// <summary>A map swap is a new lesson: a line from the old one must not be mistaken for the new
        /// one's.</summary>
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

        public void Tick(SimWorld world, bool tutorialUp, bool infoGrown, float infoExtra, int barTiles,
            bool interacting, float now)
        {
            if (world == null || _rig == null || _camera == null) return;

            TutorialState tutorial = world.Tutorial;
            if (!tutorial.Active) return;

            TrackTask(tutorial, now);
            TrackWave(world, now);
            TrackCoreHit(world, now);

            // A placement is the same signal as a held button: the player is working where the view is. Wait
            // for a second and a half of quiet rather than moving the map under their next click.
            bool busy = interacting ||
                        world.Events.TryLastWithin(SimEventKind.Built,
                            world.TickCount - (int)(1.5f * SimConfig.TickRate), out _);

            Resolve(tutorialUp, infoGrown, infoExtra, barTiles, busy, now);
            UpdateReturn(now, busy);
        }

        /// <summary>A line is a place on the map: frame the whole layout it draws, not just the tile the card
        /// points at, because the player has to build all of it. A line with no one place - "bank a shape" -
        /// moves nothing.</summary>
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

        /// <summary>What a line is asking the camera to show: the centre of its blueprint and the rectangle
        /// that has to be visible. A line with no blueprint falls back to its one target tile.</summary>
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
        /// walker closest to the Core. The first frame can be before the first walker exists, so the search
        /// retries rather than aiming at an empty doorway.</summary>
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

        /// <summary>Shown once per hit streak, and at most once per <see cref="CoreRepeatSeconds"/>.</summary>
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

        /// <summary>The enemy nearest the Core: the one whose arrival is the news.</summary>
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
        private void Resolve(bool tutorialUp, bool infoGrown, float infoExtra, int barTiles, bool busy,
            float now)
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
            // screen; a wave or a Core hit only asks for a look when the player cannot already see it.
            if (!_pendingForced && IsVisible(tutorialUp, infoGrown, infoExtra, barTiles, _pendingMin,
                    _pendingMax)) return;

            _rig.GlideTo(_pending);

            if (!_pendingWave) return;

            _returnArmed = true;
            _returnPoint = _camera.transform.position;
            _returnManualAt = _rig.LastManualInputTime;
            _returnAt = now + WaveGlanceSeconds;
        }

        /// <summary>After the glance at a wave, drift back to where the player was - unless they took the
        /// camera themselves or a newer beat has something to show.</summary>
        private void UpdateReturn(float now, bool busy)
        {
            if (!_returnArmed || now < _returnAt) return;

            _returnArmed = false;
            if (_hasPending || busy) return;
            if (_rig.LastManualInputTime > _returnManualAt) return;

            _rig.GlideTo(_returnPoint);
        }

        /// <summary>Is the whole rectangle on the open part of the screen? The locator's question, asked the
        /// same way, so the camera and the edge arrows cannot disagree. A blueprint partly off screen pulls
        /// the view to its middle; one already whole does not.</summary>
        private bool IsVisible(bool tutorialUp, bool infoGrown, float infoExtra, int barTiles, Vector2 min,
            Vector2 max)
        {
            ThreatRadar.Viewport view =
                _screen.Measure(_camera, tutorialUp, VisibleMarginPixels, infoGrown, barTiles, infoExtra);
            return view.IsOpen(min) && view.IsOpen(max);
        }
    }
}
