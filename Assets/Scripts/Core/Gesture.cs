namespace Facet.Core
{
    /// <summary>
    /// The game's gestures, spelled once as <see cref="InputCommand"/>s.
    ///
    /// <see cref="InputCommand"/> is the protocol and <c>BuildInput</c> is its one production adapter,
    /// but "what a click is" used to have as many spellings as callers: every test hand-composed the
    /// field triplets the adapter latches, some with the release edge and some without it, and the
    /// comment at each site claimed they matched. This module is the shared spelling - the adapter and
    /// the tests quote the same gesture, so the spelling cannot drift from the device it imitates.
    /// </summary>
    public static class Gesture
    {
        /// <summary>The tick a left press lands on while nothing is selected: read the tile under the
        /// cursor. The input source decides that the press is a read - the selection is its state -
        /// and the tick applies it (see <see cref="InputCommand.InspectPressed"/>).</summary>
        public static InputCommand Click(Int2 cell)
            => new InputCommand(buildHeld: true, cursorCell: cell, inspectPressed: true);

        /// <summary>The tick a left button is held over a cell. With a kind selected this is a drag's
        /// stroke; with none it is the read cursor moving - the press alone builds nothing.</summary>
        public static InputCommand Aim(Int2 cell, BuildKind? kind = null)
            => new InputCommand(buildHeld: true, cursorCell: cell, selected: kind);

        /// <summary>The tick the left button came up over a cell: the edge that commits whatever the
        /// gesture carries. <see cref="BuildController"/> also closes on the hold falling, so a
        /// gesture written without this still works - and still spells the protocol differently than
        /// the mouse does.</summary>
        public static InputCommand Release(Int2 cell, BuildKind? kind = null)
            => new InputCommand(cursorCell: cell, primaryReleased: true, selected: kind);

        /// <summary>The tick a right button is held over a cell: erase what stands there. One removal
        /// per stroke per cell, which is the controller's rule, not this one.</summary>
        public static InputCommand Removal(Int2 cell)
            => new InputCommand(removeHeld: true, cursorCell: cell);

        /// <summary>The belt drag: press, carry, lift. The three ticks a hand on the mouse produces,
        /// as the sequence a driver feeds to successive <see cref="SimWorld.Tick"/>s.</summary>
        public static InputCommand[] Drag(Int2 from, Int2 to)
            => new[] { Aim(from, BuildKind.Belt), Aim(to, BuildKind.Belt), Release(to, BuildKind.Belt) };
    }
}
