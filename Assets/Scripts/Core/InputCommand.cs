namespace Facet.Core
{
    /// <summary>
    /// Everything an input source may ask the simulation to do in one tick.
    /// Note there is no movement field: the player has no avatar in the world,
    /// the camera pans freely and only the cursor interacts with the simulation.
    ///
    /// The one-shot fields (<see cref="InspectPressed"/>, <see cref="PrimaryReleased"/>,
    /// <see cref="RotateSteps"/>, <see cref="StartWavePressed"/>, <see cref="RestartPressed"/>) are
    /// latched by the input source and cleared only once a tick has consumed them, so a click that
    /// lands between two ticks - or during a frame that runs several catch-up ticks - is neither lost
    /// nor applied twice.
    /// </summary>
    public readonly struct InputCommand
    {
        /// <summary>Left mouse button held - drags a belt run, or aims a machine.</summary>
        public readonly bool BuildHeld;

        /// <summary>Right mouse button held - clears a jam, or removes whatever is under the cursor.</summary>
        public readonly bool RemoveHeld;

        /// <summary>The left button came up since the last consumed tick. There is no "pressed" twin:
        /// a gesture starts on <see cref="BuildHeld"/> and ends on this, so the edge of the press is
        /// never the thing the simulation acts on.</summary>
        public readonly bool PrimaryReleased;

        /// <summary>Tile under the mouse cursor. (-1,-1) when there is no cursor.</summary>
        public readonly Int2 CursorCell;

        /// <summary>Which building the player has selected. Sent every tick: the selection is the input
        /// source's state, and the simulation only needs to know what it currently is.
        /// <see langword="null"/> is a state of its own - nothing selected, so the cursor reads the map
        /// instead of building on it (see <see cref="InspectPressed"/>).</summary>
        public readonly BuildKind? Selected;

        /// <summary>The left button went down on the map while nothing was selected: read the tile under
        /// the cursor rather than build on it. The input source decides that - it is the one that knows
        /// what is selected - and the tick applies it, so the click lands on a tick boundary.</summary>
        public readonly bool InspectPressed;

        /// <summary>Quarter turns requested since the last consumed tick. Positive is clockwise.</summary>
        public readonly int RotateSteps;

        /// <summary>The player asked for the next wave now (the N key). The HUD's button calls
        /// <see cref="SimWorld.RequestNextWave"/> instead, because a click happens between ticks.</summary>
        public readonly bool StartWavePressed;

        public readonly bool RestartPressed;

        public InputCommand(bool buildHeld = false, bool removeHeld = false, Int2 cursorCell = default,
            bool primaryReleased = false, BuildKind? selected = null,
            int rotateSteps = 0, bool startWavePressed = false, bool restartPressed = false,
            bool inspectPressed = false)
        {
            BuildHeld = buildHeld;
            RemoveHeld = removeHeld;
            PrimaryReleased = primaryReleased;
            CursorCell = cursorCell;
            Selected = selected;
            InspectPressed = inspectPressed;
            RotateSteps = rotateSteps;
            StartWavePressed = startWavePressed;
            RestartPressed = restartPressed;
        }

        /// <summary>No cursor, nothing held, nothing selected, nothing requested.</summary>
        public static readonly InputCommand None = new InputCommand(cursorCell: new Int2(-1, -1));
    }
}
