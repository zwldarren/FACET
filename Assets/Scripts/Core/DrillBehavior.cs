namespace Facet.Core
{
    /// <summary>
    /// Mines the shape patch under the machine and pushes it onto the belt it faces. Its interval (one
    /// second) matches the belt's one item per second, so it saturates a line rather than outrunning it;
    /// a drill facing no belt with room waits rather than mining into nowhere.
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
