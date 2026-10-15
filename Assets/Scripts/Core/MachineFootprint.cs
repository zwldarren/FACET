namespace Facet.Core
{
    /// <summary>
    /// Where a machine stands: the one tile most of them occupy, and the second tile a cutter forks across.
    ///
    /// <b>One rule, in one place, because six callers ask it</b> and any two of them disagreeing is a machine
    /// you can build half of: placement checks and marks both tiles, removal and enemy damage clear both, the
    /// tick's intake scans both, the port masks are read per tile, and the view draws one body over the pair.
    /// It lives on the behaviour rather than on the building, the same way <see cref="MachineDelivery"/>'s
    /// port rule does, so a second two-tile machine is a case here and nothing else.
    ///
    /// <b>The cutter is 1x2 across the flow.</b> Its facing is the way the belt runs through it: the belt
    /// feeds the tile it stands on, that tile's front runs one piece straight on, and the tile one lane over
    /// to the flow's right runs the other piece out beside it. So the two pieces leave next to each other,
    /// both heading the way the line already went - the belt is forked rather than turned, which is what lets
    /// a cutter sit <em>in</em> a run instead of at the corner of one.
    /// </summary>
    public static class MachineFootprint
    {
        /// <summary>How many tiles a machine of this behaviour stands on: two for the cutter, one for
        /// everything else.</summary>
        public static int Tiles(BehaviorKind behavior) => behavior == BehaviorKind.Cutter ? 2 : 1;

        /// <summary>
        /// The tile beside the one a machine stands on, for a behaviour that stands on two. False for
        /// everything else, so a caller can ask without knowing which behaviours those are.
        /// </summary>
        public static bool TrySecond(BehaviorKind behavior, Int2 cell, Dir facing, out Int2 second)
        {
            if (Tiles(behavior) == 1)
            {
                second = default;
                return false;
            }

            second = cell + LaneOf(facing).Offset();
            return true;
        }

        /// <summary>
        /// The direction the second tile lies in: the machine's right-hand side, which is its facing turned a
        /// quarter clockwise - north's right is east. The displaced piece is the one on that side of the cut,
        /// so it leaves by that lane.
        /// </summary>
        public static Dir LaneOf(Dir facing) => (Dir)(((int)facing + 1) & 3);
    }
}
