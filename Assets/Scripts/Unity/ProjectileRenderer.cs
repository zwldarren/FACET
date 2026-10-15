using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws every shot in flight as one mesh, rebuilt each frame because shots never stop moving. A kill
    /// should be visibly an item that left a belt and flew to its target, so each shot is the round it was fed - its
    /// own quadrant figure at a small radius, in the mineral colours. Positions interpolate between the last two ticks
    /// with the frame's Alpha, so flight is smooth instead of stepping at 30 Hz.
    ///
    /// The in-flight item follows the same per-mineral override as the belts, so a re-skinned mineral stays
    /// recognisable to the impact. These marks carry no outline by default, unlike belt items: at projectile size
    /// the outline would be more pixels than the figure itself, and the colour alone is the message.</summary>
    [DefaultExecutionOrder(114)]
    public sealed class ProjectileRenderer : MeshView
    {
        private readonly List<ProjectileSnapshot> _shots = new List<ProjectileSnapshot>();
        private ShapeIconSet _icons;

        protected override Palette.Layer Layer => Colors.ProjectileLayer;

        protected override void OnInitialized()
        {
            // Same per-mineral override the belts use, at the projectile radius, with no outline.
            _icons = Colors.ShapeIcons(Colors.Projectiles.Radius, 0f);
        }

        /// <summary>Shots are always moving, so this view rebuilds every frame.</summary>
        protected override void Observe(in ViewFrame frame) => MarkDirty();

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Projectiles.GetProjectiles(_shots);
            BeginSprites();

            for (int i = 0; i < _shots.Count; i++)
            {
                ProjectileSnapshot shot = _shots[i];
                Vec2 p = Vec2.Lerp(shot.PreviousPosition, shot.Position, frame.Alpha);
                AppendIcon(_icons, shot.Ammo, new Vector2(p.X, p.Y), null);
            }

            EndSprites();
        }
    }
}
