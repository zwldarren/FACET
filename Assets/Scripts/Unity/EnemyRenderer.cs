using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every enemy plus the map's wave entry points as one mesh, rebuilt each frame because
    /// enemies always move. Positions interpolate between the last two ticks with the frame's Alpha, so
    /// a wave walks smoothly at any frame rate.
    ///
    /// Two art rules land here: "colour says which side it is on, polygon count says how damaged it is"
    /// - the body is a 3..6-sided polygon losing a side per health chunk, with the four bodies cached -
    /// and every enemy wears the round its own group demands as a quadrant figure in the mineral colours,
    /// so what the briefing asked for and what the walker wants are the same picture.
    ///
    /// The body and weakness figure may each be overridden from the Palette's <see cref="Palette.Visuals"/>:
    /// a body override replaces the health-shaped polygon for that kind, and the figure follows the same
    /// per-mineral override the belt items use, so the two keep reading as one item.
    ///
    /// It is also the first consumer of the simulation's event stream: a shot flashes the enemy it hit,
    /// and a kill leaves an expanding ring. Both come off <see cref="SimWorld.Events"/> - the simulation
    /// says what happened, the view decides what it looks like - so no reaction can change the fight.</summary>
    [DefaultExecutionOrder(112)]
    public sealed class EnemyRenderer : MeshView
    {
        private readonly List<EnemySnapshot> _enemies = new List<EnemySnapshot>();

        /// <summary>Enemies hit in the last few ticks, as a fixed ring of (id, expiry tick), so a wave
        /// of hits costs no allocation and never needs pruning.</summary>
        private const int MaxFlashes = 32;
        private readonly int[] _flashId = new int[MaxFlashes];
        private readonly int[] _flashUntilTick = new int[MaxFlashes];
        private int _flashCursor;

        /// <summary>Bodies indexed by side count minus three: [0]=3 sides (dead-ish) .. [3]=6 sides (full health).</summary>
        private readonly Vector2[][] _bodies = new Vector2[4][];

        /// <summary>Each kind's override look, resolved once and indexed by (int)EnemyKind: an unticked
        /// style draws the health polygon, a ticked one replaces it, per kind.</summary>
        private VisualStyle[] _styles;
        private Vector2[][] _points;

        private ShapeIconSet _weakness;

        /// <summary>Reused per frame: which spawn points the NEXT wave walks in from; sized to the map
        /// at initialization.</summary>
        private bool[] _nextWaveSpawns;

        protected override Palette.Layer Layer => Colors.EnemyLayer;

        protected override void OnInitialized()
        {
            // First vertex at 90 degrees puts a triangle's missing side at the bottom, so a damaged enemy
            // reads as worn down from below; a 6-sided body points up, distinct from a 4-sided one.
            for (int sides = 3; sides <= 6; sides++)
                _bodies[sides - 3] = ProcMesh.RegularPolygon(sides, Colors.Enemies.BodyRadius, 90f);

            // One style per kind, not one for the field: the Tank's override must not leak onto the Runner.
            var kinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));
            _styles = new VisualStyle[kinds.Length];
            _points = new Vector2[kinds.Length][];
            foreach (EnemyKind kind in kinds)
            {
                _styles[(int)kind] = Colors.Visuals.Enemies.For(kind);
                _points[(int)kind] = VisualShapes.Points(_styles[(int)kind], Colors.CircleSides);
            }

            // Same per-mineral override as the belt items, so a weakness figure and the round that
            // counters it read as the same item; no outline, since an outlined figure inside an outlined
            // body is noise.
            _weakness = Colors.ShapeIcons(Colors.Enemies.WeaknessRadius, 0f);

            _nextWaveSpawns = new bool[World.Map.SpawnPoints.Length];
        }

        /// <summary>A new map has its own entry points and enemies: the old map's sized markers and the
        /// hit flashes pointing at gone enemies both have to go.</summary>
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

                // Red outline while it is hurting something - the Core or a machine it stopped to chew;
                // attacking means damage in progress, not damage taken.
                Color outline = enemy.Attacking ? Colors.Jam : Colors.Outline;
                AppendEnemyBody(enemy, at, outline, outlineWidth);

                AppendIcon(_weakness, enemy.Weakness, at, null);
            }

            AppendSpawnMarkers();
            AppendKillBursts();
            EndSprites();
        }

        /// <summary>Read the tick's event stream and note which enemies a shot landed on. The simulation
        /// already decided what happened, so there is no health comparison or edge detection here, and a
        /// dropped event costs one frame of flash.</summary>
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

        /// <summary>Is this enemy inside its flash window? The window's passing expires a slot without
        /// anyone sweeping the ring.</summary>
        private bool IsFlashing(int id)
        {
            int now = World.TickCount;
            for (int i = 0; i < MaxFlashes; i++)
                if (_flashId[i] == id && _flashUntilTick[i] > now) return true;

            return false;
        }

        /// <summary>An expanding, fading ring where each enemy recently died - the view's answer to the
        /// simulation's "killed here", hung off the event stream rather than a tracked health edge.</summary>
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

            // A just-hit body goes white-hot for a few ticks, so a landed shot is seen, not inferred from
            // the shrinking polygon.
            Color body = IsFlashing(enemy.Id) ? Colors.Enemies.HitFlash : Colors.EnemyColor(enemy.Kind);
            int sides = Mathf.Clamp(3 + Mathf.RoundToInt(3f * enemy.HealthFraction), 3, 6);
            AppendPolygon(_bodies[sides - 3], at, body, outline, outlineWidth);
        }

        /// <summary>The wave entry points, each a hollow frame at one alpha this frame: steady and faint
        /// while a wave runs, pulsing with the countdown during an intermission so the map says "something
        /// is coming". The NEXT wave's points also get a solid inner quad, the preview where the player is
        /// already looking.</summary>
        private void AppendSpawnMarkers()
        {
            WaveDirector waves = World.Waves;

            Palette.EnemyLook look = Colors.Enemies;
            float frameAlpha;
            if (waves.Phase == WavePhase.Intermission && waves.IntermissionTotal > 0f)
            {
                // Intermission is the semantic answer ("a countdown is running"); the opening is Held, not
                // counting, so its frames sit steady. The total stays guarded because the fraction divides
                // by it: a 0-second intermission starts its wave the same tick, so there is nothing to pulse.
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
                    // The map resolves a table's door index onto its own entry points, so the lit marker and
                    // the tile enemies enter at are the same answer.
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
