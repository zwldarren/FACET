using System;

namespace Facet.Core
{
    /// <summary>
    /// The one gun, with a universal chamber. Every tick: a delivery is taken as the round it is (any
    /// non-empty shape - the chamber has no diet), the barrel prefers the enemy that round actually hurts,
    /// and if the machine is aimed, armed and off cooldown it eats the round and fires <em>it</em>.
    ///
    /// Nothing jams here under the shipped diet, because there is nothing a belt can deliver that the
    /// chamber refuses: the rework moved the punishment for the wrong shape from the belt to the damage
    /// roll, where it belongs - a resisted round does a quarter damage, and the answer to a mismatched
    /// line is to build the shape the briefing asked for, not to clear a jam. That claim is the content
    /// table's, not this file's: a custom table that narrows the chamber's diet jams the wrong delivery
    /// at the door, by the one intake rule every machine shares - and the gun stands down rather than
    /// counting a delivery it will never eat as its round.
    ///
    /// Fire rate is min(the gun's rate, what its lines deliver): damage is a throughput property of the
    /// layout, and now a shape property of the round as well.
    /// </summary>
    internal sealed class TurretBehavior : IMachineBehavior
    {
        public void Step(ref MachineState turret, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(turret.Build);
            TurretDef spec = ctx.Content.Turret(turret.Build);
            turret.Cooldown = MathF.Max(0f, turret.Cooldown - ctx.Dt);

            Vec2 centre = ctx.Grid.CellCenter(cell);

            if (MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, in def))
            {
                turret.Armed = false;
                Aim(ref turret, in ctx, centre, spec.Range, Shape.None);
                return;
            }

            bool armed = MachineDelivery.TryFindDietDelivery(ctx.Belts, cell, in def,
                out Int2 delivery, out Shape round);
            turret.Armed = armed;

            // Armed: the aim prefers the enemy this round is worth full damage against. Unarmed: nothing in
            // particular, so the barrel tracks the nearest enemy and the picture still reads as a gun.
            Aim(ref turret, in ctx, centre, spec.Range, armed ? round : Shape.None);

            if (!armed || !turret.HasTarget || turret.Cooldown > 0f) return;

            // Aiming was a read of what is being delivered; firing still has to confirm the take. A
            // jammed segment scans as a delivery but refuses to be emptied, and without this the gun
            // would fire a round that never left the belt, once per cooldown, forever.
            if (!ctx.Belts.TryTakeItem(delivery, out _))
            {
                turret.Armed = false;
                return;
            }

            turret.Cooldown = spec.FireInterval;

            // The chamber holds nothing between shots, so <see cref="MachineState.Carried"/> keeps the last
            // round fired instead: it is what the badge draws and what the card reports, which is the only
            // way the player can see which shape their line is actually feeding the gun.
            turret.Carried = round;
            ctx.Telemetry.ShotsFired++;
            ctx.Projectiles.Spawn(centre + turret.Aim * 0.42f, turret.TargetId, turret.TargetPosition,
                spec, round);
            ctx.Events.ShotFired(cell, round, spec.Damage);
        }

        /// <summary>The enemy this turret's round is worth the most against - the nearest weak to it,
        /// or the nearest in range - with the barrel turned toward it. Chosen by the field, not by walking
        /// slots. <see cref="Shape.None"/> prefers nobody: the barrel simply tracks the nearest.</summary>
        private static void Aim(ref MachineState turret, in MachineTickContext ctx, Vec2 centre,
            float range, Shape round)
        {
            bool found = ctx.Enemies.TryFindPreferred(centre, range, round, out int targetId, out Vec2 targetPosition);
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
