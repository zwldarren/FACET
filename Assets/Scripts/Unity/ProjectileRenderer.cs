using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every shot in flight as one mesh, rebuilt each frame because shots never stop moving.
    /// A kill should be visibly a shape that left a belt and flew to its target, so each shot is
    /// drawn as its ammunition shape at a small radius. Positions are interpolated between the
    /// last two simulation ticks with the frame's Alpha, so flight is smooth at any frame rate
    /// instead of stepping at 30 Hz.
    ///
    /// The in-flight shape follows the same item-shape override as the belts, so a re-skinned
    /// resource stays recognisable all the way to the impact.
    ///
    /// These shapes carry no outline by default, unlike the items on belts: at projectile size the
    /// outline would be more pixels than the shape itself, and the colour alone is the message here.
    /// </summary>
    [DefaultExecutionOrder(114)]
    public sealed class ProjectileRenderer : MeshView
    {
        private readonly List<ProjectileSnapshot> _shots = new List<ProjectileSnapshot>();
        private ShapeIconSet _icons;

        protected override Palette.Layer Layer => Colors.ProjectileLayer;

        protected override void OnInitialized()
        {
            // Same shape override the belts use, at the projectile radius, with no outline.
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
