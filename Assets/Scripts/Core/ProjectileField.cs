using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>One shot in flight.</summary>
    public struct ProjectileState
    {
        /// <summary>The shape it was fired from, carried so the view can draw what is in the air.
        /// It is always the firing turret's own diet: a line of the wrong shape jams, it never fires.</summary>
        public ShapeType Ammo;
        public float Damage;
        public float Speed;

        /// <summary>The enemy it was fired at. It keeps homing on this id, so a shot fired at a moving
        /// target still lands on it - a miss reads as a bug, and the shape is the message here, not the aim.</summary>
        public int TargetId;

        /// <summary>Where the target was the last time it was seen, so a shot whose target dies on
        /// the way still lands where the enemy was rather than flying on forever.</summary>
        public Vec2 LastKnownTarget;

        public Vec2 Position;
        public Vec2 PreviousPosition;
        public float Age;
    }

    /// <summary>One shot, flattened for the view layer.</summary>
    public struct ProjectileSnapshot
    {
        public ShapeType Ammo;
        public Vec2 PreviousPosition;
        public Vec2 Position;
    }

    /// <summary>
    /// Shots in flight. They exist so that "every shot is a shape that was mined, split and carried" is
    /// visible right up to the impact: what kills an enemy is a triangle that left a belt, not a
    /// number subtracted at a turret.
    /// </summary>
    public sealed class ProjectileField
    {
        private ProjectileState[] _shots = new ProjectileState[32];
        private int _count;

        public int Count => _count;

        public void Clear()
        {
            Array.Clear(_shots, 0, _count);
            _count = 0;
        }

        public void Spawn(Vec2 from, int targetId, Vec2 targetPosition, TurretDef spec)
        {
            if (_count == _shots.Length) Array.Resize(ref _shots, _shots.Length * 2);

            _shots[_count] = new ProjectileState
            {
                Ammo = spec.Ammo,
                Damage = spec.Damage,
                Speed = spec.ProjectileSpeed,
                TargetId = targetId,
                LastKnownTarget = targetPosition,
                Position = from,
                PreviousPosition = from,
                Age = 0f,
            };

            _count++;
        }

        /// <summary>Fly every shot one step, and burst the ones that arrive.</summary>
        public void Step(float dt, EnemyField enemies)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                ref ProjectileState shot = ref _shots[i];
                shot.PreviousPosition = shot.Position;
                shot.Age += dt;

                if (enemies.TryGetPosition(shot.TargetId, out Vec2 target)) shot.LastKnownTarget = target;

                Vec2 delta = shot.LastKnownTarget - shot.Position;
                float distance = delta.Magnitude;
                float step = shot.Speed * dt;

                if (distance <= MathF.Max(step, Balance.ProjectileHitRadius))
                {
                    enemies.ApplyDamage(shot.TargetId, shot.Damage, shot.Ammo);
                    RemoveAt(i);
                    continue;
                }

                shot.Position += delta * (step / distance);

                if (shot.Age >= Balance.ProjectileMaxLifetime) RemoveAt(i);
            }
        }

        public void GetProjectiles(List<ProjectileSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _count; i++)
            {
                into.Add(new ProjectileSnapshot
                {
                    Ammo = _shots[i].Ammo,
                    PreviousPosition = _shots[i].PreviousPosition,
                    Position = _shots[i].Position,
                });
            }
        }

        private void RemoveAt(int index)
        {
            int last = _count - 1;
            if (index != last) _shots[index] = _shots[last];
            _shots[last] = default;
            _count--;
        }
    }
}
