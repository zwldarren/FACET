namespace Facet.Core
{
    /// <summary>
    /// Mines the shape patch under the machine and pushes it onto the belt it faces. A vein yields a
    /// <em>full</em> shape - the root every chain is built from - and its interval (one second) matches the
    /// belt's one item per second, so a drill saturates a line rather than outrunning it; a drill facing no
    /// outlet belt with room waits rather than mining into nowhere. The outlet is any belt in front that does
    /// not point back in (<see cref="MachineDelivery.IsOutput"/>), so one free cell is enough to turn.
    /// </summary>
    internal sealed class DrillBehavior : IMachineBehavior
    {
        public void Step(ref MachineState drill, Int2 cell, in MachineTickContext ctx)
        {
            if (!MachineClock.Ticked(ref drill.Cooldown, ctx.Dt)) return;

            Quad mineral = ctx.Terrain.OreAt(cell);
            if (mineral == Quad.Empty) return;

            // The same outlet rule as every other machine: any belt in front that does not point back into
            // the drill, entered from the drill's side so a belt turning there takes it round the corner.
            // TrySpawnItem is then the "does it have room" test.
            if (MachineDelivery.TryPushOutSide(ctx.Belts, cell, drill.Direction, Shape.Full(mineral)))
                drill.Cooldown = ctx.Content.Machine(drill.Build).Interval;
        }
    }
}
