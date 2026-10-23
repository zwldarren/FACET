using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>The defended object: one flat block covering its whole <see cref="CoreState.Size"/> x
    /// <see cref="CoreState.Size"/> footprint, with a dark emblem in the middle that tints toward the damage colour as
    /// HP falls - one shape carrying both "this is what you lose on" and "how close you are to losing".
    ///
    /// The Core is the one view whose rebuild reason is a watched value rather than a revision or the clock: it
    /// redraws when its cell or health changes and while a view-clock hit cue is active. Geometry is built
    /// once in <see cref="OnInitialized"/>.
    ///
    /// A Sprite override replaces the block and emblem with one image, tinted toward the damage colour as the Core
    /// takes hits, so the health readout survives the re-skin.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CoreView : MeshView
    {
        private readonly FeedbackEvents _feedback = new FeedbackEvents(16);
        private float _seconds;
        private bool _feedbackWasActive;
        private bool _wasAnimating;
        private Vector2[] _block;
        private Vector2[] _emblem;
        private VisualStyle _core;

        private Int2 _seenCell = new Int2(int.MinValue, int.MinValue);
        private float _seenFraction = -1f;

        protected override Palette.Layer Layer => Colors.CoreLayer;

        /// <summary>The Core is the largest solid thing on the map, and the one the eye has to find: it
        /// drops a shadow like every other body.</summary>
        protected override bool CastsShadows => true;

        protected override void OnInitialized()
        {
            _feedback.Reset(World.Events);
            _core = Colors.Visuals.Core;

            // Counter-clockwise, which ProcMesh requires of every polygon.
            float size = CoreState.Size;
            float inset = Colors.CoreBlock.Inset;
            _block = ProcMesh.Rounded(new[]
            {
                new Vector2(inset, inset),
                new Vector2(size - inset, inset),
                new Vector2(size - inset, size - inset),
                new Vector2(inset, size - inset),
            }, Colors.CoreBlock.CornerRadius);

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
            _seconds = frame.Seconds;
            if (_feedbackWasActive != Colors.Feedback.Active) MarkDirty();
            _feedbackWasActive = Colors.Feedback.Active;
            _feedback.Capture(World.Events, _seconds, 1 << (int)SimEventKind.CoreDamaged, Colors.Feedback.Active);
            // Keep rebuilding through the final clean frame so an expired/disabled flash cannot stick.
            CoreState core = World.Core;
            float age = ImpactAge();
            bool animating = age >= 0f && age < 1f;
            if (animating || _wasAnimating) MarkDirty();
            _wasAnimating = animating;
            if (core.Cell == _seenCell && Mathf.Abs(core.HealthFraction - _seenFraction) < 0.001f) return;

            _seenCell = core.Cell;
            _seenFraction = core.HealthFraction;
            MarkDirty();
        }

        protected override void OnWorldRebound()
        {
            _feedback.Reset(World.Events);
            _wasAnimating = false;
            _seenCell = new Int2(int.MinValue, int.MinValue);
            _seenFraction = -1f;
            MarkDirty();
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
                // A sprite replaces the block's mark, not its footprint: the shipped block is what stands
                // on the ground, so it is what drops the shadow under an authored body.
                AppendShadow(_block, origin + _core.Offset);
                DrawSprite(_core, centre, FlashTint(tint));
                AppendCoreImpact(origin);
                EndSprites();
                return;
            }

            // A procedural override may recolour the block's healthy end and outline; the health tint and emblem stay
            // the palette's, so the damage readout is never lost.
            Color healthy = _core.Override ? _core.Fill : Colors.Core;
            Color outline = _core.Override ? _core.Outline : Colors.Outline;
            float outlineWidth = _core.Override ? OutlineWidthFor(_core) : OutlineWidth;

            Color fill = Color.Lerp(Colors.CoreDamage, healthy, core.HealthFraction);
            Color emblem = Color.Lerp(Colors.CoreDamage, Colors.Outline, core.HealthFraction);

            AppendShadow(_block, origin);
            AppendPolygon(_block, origin, FlashTint(fill), outline, outlineWidth);

            // No outline of its own: it sits inside the block, where a second outline would read as a seam.
            AppendPolygon(_emblem, origin, emblem, emblem, 0f);

            AppendCoreImpact(origin);
            EndSprites();
        }

        private float ImpactAge()
        {
            if (_feedback.Count == 0) return 1f;
            return (_seconds - _feedback[0].Seconds) / Mathf.Max(0.01f, Colors.Feedback.CoreHitSeconds);
        }

        private Color FlashTint(Color body)
        {
            float t = ImpactAge();
            return t >= 0f && t < 1f ? Color.Lerp(body, Colors.Feedback.CoreHit, (1f - t) * 0.8f) : body;
        }

        private void AppendCoreImpact(Vector2 origin)
        {
            float t = ImpactAge();
            if (t < 0f || t >= 1f) return;
            Color colour = Colors.Feedback.CoreHit;
            colour.a *= 1f - t;
            float extra = 0.08f + ViewFeedback.EaseOutQuad(t) * 0.25f;
            float loX = origin.x - extra, loY = origin.y - extra;
            float hiX = origin.x + CoreState.Size + extra, hiY = origin.y + CoreState.Size + extra;
            float w = Mathf.Min(CoreState.Size * 0.2f, Mathf.Max(1f, Colors.Feedback.StrokePixels) * WorldPerPixel);
            AppendRect(loX, loY, hiX, loY + w, colour);
            AppendRect(loX, hiY - w, hiX, hiY, colour);
            AppendRect(loX, loY + w, loX + w, hiY - w, colour);
            AppendRect(hiX - w, loY + w, hiX, hiY - w, colour);
        }
    }
}
