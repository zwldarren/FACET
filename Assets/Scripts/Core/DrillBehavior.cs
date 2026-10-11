namespace Facet.Core
{
    /// <summary>
    /// Mines the shape patch under the machine and pushes it onto the belt it faces. Deliberately
    /// matched to one belt: the drill's definition interval (one second) is the belt's own one item
    /// per second, so a drill exactly saturates a line rather than outrunning it.
    ///
    /// A drill facing anything but a belt with room simply waits with its cooldown expired - it never
    /// mines into nowhere. Waiting is the honest signal for "this stage is the bottleneck".
    /// </summary>
    internal sealed class DrillBehavior : IMachineBehavior
    {
        public void Step(ref MachineState drill, Int2 cell, in MachineTickContext ctx)
        {
            if (!MachineClock.Ticked(ref drill.Cooldown, ctx.Dt)) return;

            ShapeType mined = ctx.Terrain.OreAt(cell);
            if (!mined.IsShape()) return;

            // TrySpawnItem is also the "is that cell a belt with room" test.
            if (ctx.Belts.TrySpawnItem(MachineReading.DrillLanding(cell, drill.Direction), mined))
                drill.Cooldown = ctx.Content.Machine(drill.Build).Interval;
        }
    }
}
