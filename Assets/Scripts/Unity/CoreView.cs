using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The defended object: one flat block covering its whole <see cref="CoreState.Size"/> x
    /// <see cref="CoreState.Size"/> footprint, with a dark emblem in the middle that tints toward the
    /// damage colour as HP falls - one shape carrying both "this is the thing you lose on" and "how
    /// close you are to losing", which is the second half of the design doc's art rule.
    ///
    /// The Core is the one view whose rebuild reason is a watched value rather than a revision or the
    /// clock: it redraws when its cell or its health fraction changes, and not otherwise. The
    /// procedural geometry, which is static, is built once in <see cref="OnInitialized"/>.
    ///
    /// A Sprite override replaces the block and emblem with one image, tinted toward the damage
    /// colour as the Core takes hits, so the health readout survives the re-skin.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CoreView : MeshView
    {
        private Vector2[] _block;
        private Vector2[] _emblem;
        private VisualStyle _core;

        private Int2 _seenCell = new Int2(int.MinValue, int.MinValue);
        private float _seenFraction = -1f;

        protected override Palette.Layer Layer => Colors.CoreLayer;

        protected override void OnInitialized()
        {
            _core = Colors.Visuals.Core;

            // Counter-clockwise, which ProcMesh requires of every polygon.
            float size = CoreState.Size;
            float inset = Colors.CoreBlock.Inset;
            _block = new[]
            {
                new Vector2(inset, inset),
                new Vector2(size - inset, inset),
                new Vector2(size - inset, size - inset),
                new Vector2(inset, size - inset),
            };

            float emblemTiles = Colors.CoreBlock.EmblemTiles;
            float min = size * 0.5f - emblemTiles * 0.5f;
            float max = size * 0.5f + emblemTiles * 0.5f;
            _emblem = new[]
            {
                new Vector2(min, min),
                new Vector2(max, min),
                new Vector2(max, max),
                new Vector2(min, max),
            };
        }

        protected override void Observe(in ViewFrame frame)
        {
            // The Core is static, so this view rebuilds only when it moves or its health moves the
            // fraction it draws - which is also why a map change has to reset what it last saw: the
            // next map's Core can sit on the same cell at the same health.
            CoreState core = World.Core;
            if (core.Cell == _seenCell && Mathf.Abs(core.HealthFraction - _seenFraction) < 0.001f) return;

            _seenCell = core.Cell;
            _seenFraction = core.HealthFraction;
            MarkDirty();
        }

        protected override void OnWorldRebound()
        {
            _seenCell = new Int2(int.MinValue, int.MinValue);
            _seenFraction = -1f;
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            CoreState core = World.Core;
            var origin = new Vector2(core.Cell.X, core.Cell.Y);
            BeginSprites();

            if (_core.Override && _core.Source == VisualSource.Sprite)
            {
                var centre = new Vector2(core.Cell.X + CoreState.Size * 0.5f, core.Cell.Y + CoreState.Size * 0.5f);
                Color tint = Color.Lerp(Colors.CoreDamage, Color.white, core.HealthFraction);
                DrawSprite(_core, centre, tint);
                EndSprites();
                return;
            }

            // A procedural override may recolour the block's healthy end and its outline; the health
            // tint and the emblem are still the palette's, so the damage readout is never lost.
            Color healthy = _core.Override ? _core.Fill : Colors.Core;
            Color outline = _core.Override ? _core.Outline : Colors.Outline;
            float outlineWidth = _core.Override ? OutlineWidthFor(_core) : OutlineWidth;

            Color fill = Color.Lerp(Colors.CoreDamage, healthy, core.HealthFraction);
            Color emblem = Color.Lerp(Colors.CoreDamage, Colors.Outline, core.HealthFraction);

            AppendPolygon(_block, origin, fill, outline, outlineWidth);

            // No outline of its own: it sits inside the block, and a second outline there would read
            // as a seam in the building.
            AppendPolygon(_emblem, origin, emblem, emblem, 0f);

            EndSprites();
        }
    }
}
