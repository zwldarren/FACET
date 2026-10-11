using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every item riding a belt as one mesh, rebuilt each frame because items are always
    /// moving. Positions are interpolated between the last two simulation ticks with the frame's
    /// Alpha, so transport is smooth at any frame rate instead of stepping at 30 Hz.
    ///
    /// The shape is the one thing on screen the player has to identify at a glance, so its
    /// appearance comes from the Palette's <see cref="Palette.Visuals"/> override for that shape:
    /// unticked it is the built-in circle / half-disc in the shape's colour, ticked it can be a
    /// different silhouette, colour or a Sprite. The icon set is built once, so a busy belt still
    /// rebuilds without per-item allocation.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class ItemRenderer : MeshView
    {
        private readonly List<ItemSnapshot> _items = new List<ItemSnapshot>();
        private ShapeIconSet _icons;

        protected override Palette.Layer Layer => Colors.ItemLayer;

        protected override void OnInitialized()
        {
            _icons = Colors.ShapeIcons(Colors.Items.Radius, Colors.OutlinePixels);
        }

        /// <summary>Items are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Belts.GetItems(_items);
            BeginSprites();

            for (int i = 0; i < _items.Count; i++)
            {
                ItemSnapshot item = _items[i];
                Vec2 p = Vec2.Lerp(item.PreviousPosition, item.Position, frame.Alpha);
                AppendIcon(_icons, item.Shape, new Vector2(p.X, p.Y), null);
            }

            EndSprites();
        }
    }
}
