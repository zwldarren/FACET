using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every enemy plus the map's wave entry points as one mesh, rebuilt each frame because
    /// enemies are always moving. Positions are interpolated between the last two simulation ticks
    /// with the frame's Alpha, so a wave walks smoothly at any frame rate instead of stepping.
    ///
    /// Two of the design doc's art rules land here. "Colour says which side it is on, polygon count
    /// says how damaged it is": the body is a 3..6-sided polygon that loses a side for each chunk of
    /// health, and the four possible bodies are cached once instead of regenerated per enemy per
    /// frame. And every enemy wears its own weakness as an icon, in the exact colour that ammo rides
    /// the belts in, so the counter-pick is one glance rather than a trip through the codex.
    ///
    /// The body and the weakness icon may each be overridden from the Palette's
    /// <see cref="Palette.Visuals"/>: an enemy body override replaces the health-shaped polygon for
    /// that kind, and the weakness icon follows the same shape override the belt items use, so the
    /// two keep reading as one shape.
    ///
    /// It is also the first consumer of the simulation's event stream: a shot that lands flashes the
    /// enemy it hit, and a kill leaves an expanding ring where the enemy died. Both come straight off
    /// <see cref="SimWorld.Events"/> - the simulation says what happened, the view decides what that
    /// looks like - so no reaction here can change the fight it is reporting on.
    /// </summary>
    [DefaultExecutionOrder(112)]
    public sealed class EnemyRenderer : MeshView
    {
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>();

        /// <summary>Enemies hit in the last few ticks, as a fixed ring of (id, expiry tick) - so a
        /// wave of hits costs no allocation and never needs pruning, since a stale slot simply stops
        /// matching on its expiry tick.</summary>
        private const int MaxFlashes = 32;
        private readonly int[] _flashId = new int[MaxFlashes];
        private readonly int[] _flashUntilTick = new int[MaxFlashes];
        private int _flashCursor;

        /// <summary>Bodies indexed by side count minus three: [0]=3 sides (dead-ish) .. [3]=6 sides (full health).</summary>
        private readonly Vector2[][] _bodies = new Vector2[4][];

        /// <summary>Each kind's override look, resolved once and indexed by (int)EnemyKind: an
        /// unticked style draws the health polygon below, a ticked one replaces it - per kind, so a
        /// re-skinned Spike says nothing about the Bulwark.</summary>
        private VisualStyle[] _styles;
        private Vector2[][] _points;

        private ShapeIconSet _weakness;

        /// <summary>Reused per frame: which spawn points the NEXT wave walks in from. Sized to the
        /// map at initialization, since the map is what owns its entry points.</summary>
        private bool[] _nextWaveSpawns;

        protected override Palette.Layer Layer => Colors.EnemyLayer;

        protected override void OnInitialized()
        {
            // First vertex at 90 degrees puts the "missing" side of a triangle at the bottom, so a
            // damaged enemy reads as worn down from below - and a 6-sided body reads as a hexagon
            // pointing up, distinct from a 4-sided one at any rotation.
            for (int sides = 3; sides <= 6; sides++)
                _bodies[sides - 3] = ProcMesh.RegularPolygon(sides, Colors.Enemies.BodyRadius, 90f);

            // One style per kind, not one for the field: the Bulwark's override must not leak onto
            // the Spike, and an unticked entry still costs one cached point array and nothing else.
            var kinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));
            _styles = new VisualStyle[kinds.Length];
            _points = new Vector2[kinds.Length][];
            foreach (EnemyKind kind in kinds)
            {
                _styles[(int)kind] = Colors.Visuals.Enemies.For(kind);
                _points[(int)kind] = VisualShapes.Points(_styles[(int)kind], Colors.CircleSides);
            }

            // Same shape override as the belt items, so a weakness icon and the ammo that counters it
            // are recognisably the same shape; no outline - an outlined shape inside an outlined body
            // reads as noise.
            _weakness = Colors.ShapeIcons(Colors.Enemies.WeaknessRadius, 0f);

            _nextWaveSpawns = new bool[World.Map.SpawnPoints.Length];
        }

        /// <summary>A new map has its own entry points, and its own enemies: the markers sized for the
        /// old map, and the hit flashes pointing at enemies that no longer exist, both have to go.</summary>
        protected override void OnWorldRebound()
        {
            _nextWaveSpawns = new bool[World.Map.SpawnPoints.Length];
            for (int i = 0; i < _flashId.Length; i++)
            {
                _flashId[i] = 0;
                _flashUntilTick[i] = 0;
            }
        }

        /// <summary>Enemies are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            SampleHits();

            World.Enemies.GetEnemies(_enemies);
            float outlineWidth = OutlineWidth;
            BeginSprites();

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemySnapshot enemy = _enemies[i];
                Vec2 p = Vec2.Lerp(enemy.PreviousPosition, enemy.Position, frame.Alpha);
                var at = new Vector2(p.X, p.Y);

                // Red outline while it is actually hurting something - the Core or a machine it
                // stopped to chew. Attacking means damage in progress, not damage taken.
                Color outline = enemy.Attacking ? Colors.Jam : Colors.Outline;
                AppendEnemyBody(enemy, at, outline, outlineWidth);

                AppendIcon(_weakness, enemy.Weakness, at, null);
            }

            AppendSpawnMarkers();
            AppendKillBursts();
            EndSprites();
        }

        /// <summary>
        /// Read the tick's event stream and note which enemies a shot landed on. This is the whole of
        /// the view's hit bookkeeping: the simulation already decided what happened, so there is no
        /// health comparison or edge detection here, and a dropped event costs one frame of flash and
        /// nothing else.
        /// </summary>
        private void SampleHits()
        {
            SimEventBuffer events = World.Events;
            int until = World.TickCount + Colors.Enemies.HitFlashTicks;

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Kind != SimEventKind.EnemyDamaged) continue;

                _flashId[_flashCursor] = e.EnemyId;
                _flashUntilTick[_flashCursor] = until;
                _flashCursor = (_flashCursor + 1) % MaxFlashes;
            }
        }

        /// <summary>Is this enemy inside its flash window? False once the window has passed, which is
        /// what expires a slot without anyone having to sweep the ring.</summary>
        private bool IsFlashing(int id)
        {
            int now = World.TickCount;
            for (int i = 0; i < MaxFlashes; i++)
                if (_flashId[i] == id && _flashUntilTick[i] > now) return true;

            return false;
        }

        /// <summary>
        /// The kill burst: an expanding, fading ring where each enemy recently died. The simulation
        /// said "killed here", and this is the view's answer - the one piece of feedback the design
        /// asks a kill to produce, hung off the event stream rather than off a health edge this view
        /// would have had to track itself.
        /// </summary>
        private void AppendKillBursts()
        {
            SimEventBuffer events = World.Events;
            Palette.EnemyLook look = Colors.Enemies;
            int now = World.TickCount;
            int segments = Mathf.Max(8, look.KillRingSegments);
            float pixel = WorldPerPixel;

            for (int i = 0; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (e.Kind != SimEventKind.EnemyKilled) continue;

                float age = (now - e.Tick) / (float)look.KillRingTicks;
                if (age < 0f || age > 1f) continue;

                var centre = new Vector2(e.Position.X, e.Position.Y);
                float radius = Mathf.Lerp(0.12f, look.KillRingRadius, age);
                float half = Mathf.Max(look.KillRingPixels * pixel * (1f - age), 0.004f) * 0.5f;
                Color colour = look.HitFlash;
                colour.a = 1f - age;

                for (int s = 0; s < segments; s++)
                {
                    float a0 = Mathf.PI * 2f * s / segments;
                    float a1 = Mathf.PI * 2f * (s + 1) / segments;
                    Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                    Vector2 d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                    AppendQuad(
                        centre + d0 * (radius - half), centre + d0 * (radius + half),
                        centre + d1 * (radius + half), centre + d1 * (radius - half), colour);
                }
            }
        }

        /// <summary>The body: the health-shaped polygon unless this kind's override replaces it.</summary>
        private void AppendEnemyBody(in EnemySnapshot enemy, Vector2 at, Color outline, float outlineWidth)
        {
            VisualStyle style = _styles[(int)enemy.Kind];

            if (style.Override && style.Source == VisualSource.Sprite)
            {
                DrawSprite(style, at, Color.white);
                return;
            }

            if (style.Override)
            {
                AppendPolygon(_points[(int)enemy.Kind], at + style.Offset, style.Fill, style.Outline,
                    OutlineWidthFor(style));
                return;
            }

            // A body that was just hit goes white-hot for a few ticks, so landing a shot is something
            // the player sees rather than infers from a shrinking polygon.
            Color body = IsFlashing(enemy.Id) ? Colors.Enemies.HitFlash : Colors.EnemyColor(enemy.Kind);
            int sides = Mathf.Clamp(3 + Mathf.RoundToInt(3f * enemy.HealthFraction), 3, 6);
            AppendPolygon(_bodies[sides - 3], at, body, outline, outlineWidth);
        }

        /// <summary>
        /// The wave entry points, each as a hollow frame built from one alpha value this frame.
        /// While a wave runs the frames sit at a steady faint alpha; during an intermission they pulse
        /// with the countdown so the map itself says "something is coming". The opening's countdown
        /// pulses like the rest once the player's first building arms it. The points the NEXT wave
        /// uses additionally get a solid inner quad, which is the wave preview drawn where the
        /// player is already looking.
        /// </summary>
        private void AppendSpawnMarkers()
        {
            WaveDirector waves = World.Waves;

            Palette.EnemyLook look = Colors.Enemies;
            float frameAlpha;
            if (waves.Phase == WavePhase.Intermission && waves.IntermissionTotal > 0f)
            {
                // The phase is the semantic answer - a countdown is running - and WavePhase says why
                // reading it off a number once was a bug ("CurrentWave alone is 0 both before the
                // first wave and right after one clears"). The opening is Held, not counting, so its
                // frames sit steady instead of pulsing from a countdown that has not been armed. The
                // total stays guarded because the fraction divides by it: a table row that promised 0
                // seconds is data, and data does not get to divide the renderer by zero - a 0-second
                // intermission starts its wave on the same tick, so there is nothing to pulse for.
                float phase = 1f - waves.IntermissionRemaining / waves.IntermissionTotal;
                frameAlpha = look.SpawnPulseCenter + look.SpawnPulseAmplitude * Mathf.Sin(phase * Mathf.PI * 2f * look.SpawnPulseCycles);
            }
            else
            {
                frameAlpha = look.SpawnWaveRunningAlpha;
            }

            for (int i = 0; i < _nextWaveSpawns.Length; i++) _nextWaveSpawns[i] = false;
            WaveDefinition next = waves.NextWaveDefinition;
            if (next != null)
                for (int g = 0; g < next.Groups.Length; g++)
                {
                    // The map resolves a table's door index onto its own entry points, so the marker
                    // that lights up and the tile the enemies actually enter at are the same answer.
                    int at = World.Map.ResolveSpawn(next.Groups[g].SpawnPoint);
                    if (at >= 0) _nextWaveSpawns[at] = true;
                }

            Color frame = Colors.SpawnMarker;
            frame.a = frameAlpha;

            Color preview = Colors.SpawnMarker;
            preview.a = look.SpawnPreviewAlpha;

            for (int i = 0; i < World.Map.SpawnPoints.Length; i++)
            {
                Int2 cell = World.Map.SpawnPoints[i];
                float minX = cell.X + look.SpawnFrameInset;
                float minY = cell.Y + look.SpawnFrameInset;
                float maxX = cell.X + 1f - look.SpawnFrameInset;
                float maxY = cell.Y + 1f - look.SpawnFrameInset;
                float thickness = look.SpawnFrameThickness;

                AppendRect(minX, minY, maxX, minY + thickness, frame);              // bottom
                AppendRect(minX, maxY - thickness, maxX, maxY, frame);              // top
                AppendRect(minX, minY + thickness, minX + thickness, maxY - thickness, frame); // left
                AppendRect(maxX - thickness, minY + thickness, maxX, maxY - thickness, frame); // right

                if (_nextWaveSpawns[i])
                    AppendRect(cell.X + look.SpawnInnerMarkerInset, cell.Y + look.SpawnInnerMarkerInset,
                        cell.X + 1f - look.SpawnInnerMarkerInset, cell.Y + 1f - look.SpawnInnerMarkerInset, preview);
            }
        }
    }
}
