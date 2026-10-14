using System;

namespace Facet.Core
{
    /// <summary>
    /// The twist, in code. Every tick, for every turret: a wrong-shape delivery at any input jams that
    /// belt cell, regardless of whether the turret wanted to shoot; a right-shape delivery arms it, and
    /// the item stays on the belt (so an idle turret banks ammo behind it and its line backs up); and if
    /// armed, in range and off cooldown it eats the item and fires. Fire rate is min(spec rate, delivered
    /// rate): damage is a throughput property of the layout, not a purchase.
    /// </summary>
    internal sealed class TurretBehavior : IMachineBehavior
    {
        public void Step(ref MachineState turret, Int2 cell, in MachineTickContext ctx)
        {
            TurretDef spec = ctx.Content.Turret(turret.Build);
            turret.Cooldown = MathF.Max(0f, turret.Cooldown - ctx.Dt);

            Vec2 centre = ctx.Grid.CellCenter(cell);
            Aim(ref turret, in ctx, centre, spec.Range, spec.Ammo);

            if (MachineDelivery.TryFindWrongDelivery(ctx.Belts, cell, spec.Ammo, out Int2 wrong))
            {
                ctx.Belts.TryJam(wrong);
                turret.Armed = false;
                return;
            }

            bool armed = MachineDelivery.TryFindDelivery(ctx.Belts, cell, spec.Ammo, requireShape: true,
                out Int2 delivery, out _);
            turret.Armed = armed;

            if (!armed || !turret.HasTarget || turret.Cooldown > 0f) return;

            ctx.Belts.TryTakeItem(delivery, out _);
            turret.Cooldown = spec.FireInterval;
            ctx.Telemetry.ShotsFired++;
            ctx.Projectiles.Spawn(centre + turret.Aim * 0.42f, turret.TargetId, turret.TargetPosition, spec);
            ctx.Events.ShotFired(cell, spec.Ammo, spec.Damage);
        }

        /// <summary>The enemy this turret's ammo is worth the most against - the nearest weak to it, or
        /// the nearest in range - with the barrel turned toward it. Chosen by the field, not by walking slots.</summary>
        private static void Aim(ref MachineState turret, in MachineTickContext ctx, Vec2 centre,
            float range, ShapeType ammo)
        {
            bool found = ctx.Enemies.TryFindPreferred(centre, range, ammo, out int targetId, out Vec2 targetPosition);
            turret.HasTarget = found;
            turret.TargetId = found ? targetId : 0;
            if (!found) return;

            turret.TargetPosition = targetPosition;
            Vec2 direction = targetPosition - centre;
            float length = direction.Magnitude;
            turret.Aim = length > 1e-4f ? direction / length : turret.Direction.ToVec();
        }
    }
}
