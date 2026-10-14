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
            transform.position = Vector3.SmoothDamp(
                transform.position, Clamp(_target, _camera.orthographicSize), ref _panVelocity, smoothTime);
        }

        /// <summary><c>C</c>: put the Core in the middle of the view. The pan bound is applied to the destination,
        /// so the key cannot shove the map off the open screen any more than a drag can, and the glide uses the
        /// rig's smoothing rather than a cut - a hard cut leaves the player no idea which way they went.</summary>
        private void Focus()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!keyboard[focusCoreKey].wasPressedThisFrame) return;

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
}
