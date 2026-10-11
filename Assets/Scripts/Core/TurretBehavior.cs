using System;

namespace Facet.Core
{
    /// <summary>
    /// The twist, in code. Every tick, for every turret, in map order:
    ///
    ///   1. A delivery of the wrong shape at any of its inputs **jams that belt cell**. It does not
    ///      matter whether the turret wanted to shoot or was cooling down - the doc's rule is about the
    ///      delivery, and the jam is what silences everything downstream of that segment.
    ///   2. A delivery of the right shape arms the turret. The item stays on the belt, not in the turret:
    ///      a turret with nothing in range banks its ammo on the belt behind it and its line backs up.
    ///   3. If it is armed, has a target in range, and its cooldown has expired, it eats that item and
    ///      fires. So the fire rate is min(spec rate, what the line actually delivers): one belt is one
    ///      item per second, so a turret fed by one belt fires one shot per second.
    ///
    /// Nothing here can fire without a shape arriving, which is the entire mechanic: damage is a
    /// throughput property of the layout, not a purchase.
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

        /// <summary>The enemy this turret's ammunition is worth the most against - the nearest one
        /// weak to it, or the nearest in range at all - and the barrel turned toward it. Which enemy
        /// that is comes from the field itself, so the turret does not walk its slots.</summary>
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
