using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The route preview: one semi-transparent red arrow that sweeps from each gate the coming wave uses to the
    /// Core, its tail fading behind it, a few seconds before the wave sets out and for a moment after - "they
    /// are coming, and this is the way they will walk".
    ///
    /// The route is <see cref="SimWorld.TraceEnemyRoute"/>, which makes the walkers' own choices on the
    /// fields they walk, so a route bent around the player's guns is drawn bent: the preview never promises a
    /// lane the wave will not take. It is asked fresh every frame it shows, so a building placed during the
    /// countdown re-draws the way at once.
    ///
    /// When it shows: the last <see cref="LeadSeconds"/> of a countdown; a tutorial's held wave while its line
    /// is waiting for the player to call it; and the first <see cref="TrailSeconds"/> of every wave, which
    /// covers a wave called early or started without a countdown. Read-only: it changes nothing in the run.
    /// </summary>
    [DefaultExecutionOrder(115)]
    public sealed class EnemyRouteView : MeshView
    {
        /// <summary>How long before a counted-down wave the route appears.</summary>
        private const float LeadSeconds = 4f;

        /// <summary>How long into a wave the route stays, fading over the last second.</summary>
        private const float TrailSeconds = 1.5f;

        /// <summary>Fade-in at the start of the lead window, in seconds.</summary>
        private const float FadeIn = 0.3f;

        /// <summary>One sweep: the arrow runs gate to Core in <see cref="SweepSeconds"/>, its tail drains into
        /// the Core, then a short rest before the next.</summary>
        private const float SweepSeconds = 1.5f;
        private const float RestSeconds = 0.35f;

        /// <summary>How long the fading tail behind the arrowhead is, in tiles.</summary>
        private const float TailTiles = 20f;

        /// <summary>The line at its most solid, right behind the head: semi-transparent.</summary>
        private const float MaxAlpha = 0.6f;

        /// <summary>The arrowhead's length and half-width, in tiles.</summary>
        private const float HeadLength = 0.55f;
        private const float HeadHalfWidth = 0.4f;

        /// <summary>The wave's red: the same family as the danger the HUD and the locator wear.</summary>
        private static readonly Color RouteRed = new Color(1f, 0.22f, 0.18f, 1f);

        private readonly List<Int2> _route = new List<Int2>();
        private readonly List<Vector2> _corners = new List<Vector2>();
        private readonly List<int> _spawns = new List<int>();

        private int _seenWave = -1;
        private int _waveStartTick;

        protected override Palette.Layer Layer => Colors.GuideMarkerLayer;

        /// <summary>Animated while it shows, so every frame is a new one.</summary>
        protected override void Observe(in ViewFrame frame)
        {
            SimWorld world = World;
            if (world == null) return;

            // Note when a wave sets out, in run ticks, so the trail window pauses with the run.
            if (world.Waves.CurrentWave != _seenWave)
            {
                _seenWave = world.Waves.CurrentWave;
                _waveStartTick = world.TickCount;
            }

            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            SimWorld world = World;
            if (world == null || world.Status != GameStatus.Playing) return;

            float pixel = frame.WorldPerPixel;
            if (pixel <= 0f) return;

            if (!TryWindow(world, out WaveDefinition wave, out float alpha) || wave == null || alpha <= 0.01f)
                return;

            _spawns.Clear();
            for (int g = 0; g < wave.Groups.Length; g++)
                if (!_spawns.Contains(wave.Groups[g].SpawnPoint)) _spawns.Add(wave.Groups[g].SpawnPoint);

            for (int s = 0; s < _spawns.Count; s++)
            {
                if (world.TraceEnemyRoute(_spawns[s], _route) < 2) continue;
                DrawRoute(world.TileGrid, alpha, pixel);
            }
        }

        /// <summary>Is a route due now, for which wave, and how strongly?</summary>
        private bool TryWindow(SimWorld world, out WaveDefinition wave, out float alpha)
        {
            WaveDirector waves = world.Waves;
            wave = null;
            alpha = 0f;

            switch (waves.Phase)
            {
                case WavePhase.Intermission:
                    if (waves.IntermissionRemaining > LeadSeconds) return false;
                    wave = waves.NextWaveDefinition;
                    alpha = Mathf.Clamp01((LeadSeconds - waves.IntermissionRemaining) / FadeIn);
                    return true;

                case WavePhase.Held:
                    // A lesson holding its wave for the player to call: show where it will come from.
                    if (!world.Tutorial.WaitsForWaveCall) return false;
                    wave = waves.NextWaveDefinition;
                    alpha = 1f;
                    return true;

                case WavePhase.Running:
                    float elapsed = (world.TickCount - _waveStartTick) * SimConfig.TickDt;
                    if (elapsed > TrailSeconds || waves.CurrentWave <= 0) return false;
                    wave = waves.Definition(waves.CurrentWave);
                    alpha = Mathf.Clamp01(TrailSeconds - elapsed);
                    return true;

                default:
                    return false;
            }
        }

        private void DrawRoute(TileGrid grid, float alpha, float pixel)
        {
            // The walk as its turning points, cell centres, so a straight run is one stroke not twenty.
            _corners.Clear();
            for (int i = 0; i < _route.Count; i++)
            {
                bool turn = i == 0 || i == _route.Count - 1 ||
                    (_route[i].X - _route[i - 1].X) != (_route[i + 1].X - _route[i].X) ||
                    (_route[i].Y - _route[i - 1].Y) != (_route[i + 1].Y - _route[i].Y);
                if (turn) _corners.Add(Centre(grid, _route[i]));
            }

            float total = 0f;
            for (int i = 0; i + 1 < _corners.Count; i++) total += Vector2.Distance(_corners[i], _corners[i + 1]);
            if (total <= 0f) return;

            // Where the head is in this sweep. It runs on past the Core by a tail's length, so the tail drains
            // into the Core instead of vanishing at once.
            float tail = Mathf.Min(TailTiles, total);
            float cycle = SweepSeconds + RestSeconds;
            float t = Mathf.Repeat(Time.unscaledTime, cycle);
            if (t > SweepSeconds) return;
            float head = t / SweepSeconds * (total + tail);

            float from = Mathf.Max(0f, head - tail);
            float to = Mathf.Min(total, head);
            float half = Mathf.Max(0.06f, 3f * pixel);

            // The tail as short pieces, each a little more solid toward the head.
            const float piece = 0.2f;
            for (float d = from; d < to; d += piece)
            {
                float e = Mathf.Min(to, d + piece);
                if (!PointAt(d, out Vector2 a, out _) || !PointAt(e, out Vector2 b, out _)) continue;
                float strength = Mathf.Clamp01((e - (head - tail)) / tail);
                AppendLane(a, b, half, WithAlpha(RouteRed, MaxAlpha * alpha * strength * strength));
            }

            // The arrowhead, while it is still on its way.
            if (head <= total && PointAt(head, out Vector2 tip, out Dir dir))
                AppendChevron(tip, dir, HeadLength * 0.5f, HeadHalfWidth, HeadLength * 0.5f,
                    WithAlpha(RouteRed, MaxAlpha * alpha));
        }

        /// <summary>The point <paramref name="distance"/> tiles along the corners, and the way it faces.</summary>
        private bool PointAt(float distance, out Vector2 at, out Dir dir)
        {
            for (int i = 0; i + 1 < _corners.Count; i++)
            {
                Vector2 a = _corners[i], b = _corners[i + 1];
                float len = Vector2.Distance(a, b);
                if (distance <= len && len > 0f)
                {
                    at = Vector2.Lerp(a, b, distance / len);
                    Vector2 step = b - a;
                    dir = Mathf.Abs(step.x) >= Mathf.Abs(step.y)
                        ? (step.x >= 0f ? Dir.East : Dir.West)
                        : (step.y >= 0f ? Dir.North : Dir.South);
                    return true;
                }
                distance -= len;
            }

            at = default;
            dir = Dir.North;
            return false;
        }

        private static Vector2 Centre(TileGrid grid, Int2 cell)
        {
            Vec2 c = grid.CellCenter(cell);
            return new Vector2(c.X, c.Y);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
    }
}
