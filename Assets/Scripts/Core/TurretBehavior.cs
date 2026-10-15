using System;

namespace Facet.Core
{
    /// <summary>
    /// The one gun, with a universal chamber. Every tick: a delivery is taken into the chamber as the round it
    /// is (any non-empty shape - the chamber has no diet) <em>as soon as one is there</em>, whether or not there
    /// is anything to shoot at; the barrel prefers the enemy that round actually hurts; and if the machine is
    /// loaded, aimed and off cooldown it fires <em>it</em> and the chamber is empty again.
    ///
    /// <b>The chamber holds one round, and loading it is not conditional on a target.</b> A gun that only ate
    /// when something was already in range could never be seen to load: its badge stayed empty, the line backed
    /// up at the muzzle, and the player read their own working factory as a broken one. Loading is the belt's
    /// business and firing is the target's, so the two are separate steps here - which is also what makes
    /// "what is my line feeding the gun" a question the screen answers at any moment, loaded round or not.
    ///
    /// Nothing jams here under the shipped diet, because there is nothing a belt can deliver that the
    /// chamber refuses: the rework moved the punishment for the wrong shape from the belt to the damage
    /// roll, where it belongs - a resisted round does a half damage, and the answer to a mismatched
    /// line is to build the shape the briefing asked for, not to clear a jam. That claim is the content
    /// table's, not this file's: a custom table that narrows the chamber's diet jams the wrong delivery
    /// at the door, by the one intake rule every machine shares - and the gun stands down rather than
    /// counting a delivery it will never eat as its round.
    ///
    /// Fire rate is min(the gun's rate, what its lines deliver): damage is a throughput property of the
    /// layout, and now a shape property of the round as well. A loaded chamber is what makes the first
    /// shot of an engagement immediate rather than one belt-length late.
    /// </summary>
    internal sealed class TurretBehavior : IMachineBehavior
    {
        public void Step(ref MachineState turret, Int2 cell, in MachineTickContext ctx)
        {
            MachineDef def = ctx.Content.Machine(turret.Build);
            TurretDef spec = ctx.Content.Turret(turret.Build);
            turret.Cooldown = MathF.Max(0f, turret.Cooldown - ctx.Dt);

            Vec2 centre = ctx.Grid.CellCenter(cell);

            if (MachineDelivery.TryJamWrongDelivery(ctx.Belts, cell, turret.Direction, in def))
            {
                // A delivery the diet refuses blocks its segment. That says nothing about what is already in
                // the chamber, so the gun keeps whatever it has loaded and stays aimed with it.
                Armed(ref turret);
                Aim(ref turret, in ctx, centre, spec.Range, turret.Carried);
                return;
            }

            // Load: only into an empty chamber, so a round already in hand is never overwritten - and not
            // gated on a target, which is the whole point of a chamber.
            if (turret.Carried.IsEmpty &&
                MachineDelivery.TryFindDietDelivery(ctx.Belts, cell, turret.Direction, in def, out Int2 delivery, out _) &&
                ctx.Belts.TryTakeItem(delivery, out Shape taken))
            {
                turret.Carried = taken;
            }

            Armed(ref turret);

            // The barrel aims with the round it is holding, so it points at the enemy that round is for.
            Aim(ref turret, in ctx, centre, spec.Range, turret.Carried);

            if (turret.Carried.IsEmpty || !turret.HasTarget || turret.Cooldown > 0f) return;

            // Fire: the round leaves the chamber, so what the badge shows afterwards is the next one loaded.
            // The shape is read out before it is cleared - it is what the shot in flight carries, and what the
            // enemy it lands on is measured against.
            Shape fired = turret.Carried;
            turret.Carried = Shape.None;
            turret.Cooldown = spec.FireInterval;
            Armed(ref turret);

            ctx.Telemetry.ShotsFired++;
            ctx.Projectiles.Spawn(centre + turret.Aim * 0.42f, turret.TargetId, turret.TargetPosition,
                spec, fired);
            ctx.Events.ShotFired(cell, fired, spec.Damage);
        }

        /// <summary>"There is a round in the chamber" is not a state of its own - it <em>is</em> the chamber's
        /// contents, read off it here so the flag the HUD warns on and the badge the view draws cannot disagree.
        /// </summary>
        private static void Armed(ref MachineState turret) => turret.Armed = !turret.Carried.IsEmpty;

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
