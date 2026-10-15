using Facet.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Facet.Game
{
    /// <summary>
    /// Translates raw device state into one <see cref="InputCommand"/> per tick, plus the pause toggle the
    /// driver consumes itself. One-shots are latched here: the sim runs at 30 Hz while the screen refreshes
    /// faster and one frame can run several catch-up ticks, so a press is remembered until a tick calls
    /// <see cref="ConsumeOneShots"/>, while held state is read fresh every frame. Not a MonoBehaviour - the
    /// driver owns it, so there is no execution-order race. Only the new Input System is enabled here;
    /// never use UnityEngine.Input.
    /// </summary>
    public sealed class BuildInput
    {
        /// <summary>The digits that select a building, in <see cref="BuildCatalog.All"/> order: key 0 is
        /// the tenth kind, and X the eleventh - the drain is the one kind past the digits, because a
        /// keyboard has exactly ten of them and the table outgrew them. The loop stops at the table's
        /// length, so a shorter table's spare keys are inert; an overflowing table would need a test to
        /// catch it - see <see cref="ContentFormatTests.Hotkeys_ReachEveryBuildKind"/>.</summary>
        private static readonly Key[] BuildKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6,
            Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.X,
        };

        /// <summary>How many kinds the keyboard can reach, so the build bar's hints never go stale.</summary>
        public static int HotkeyCount => BuildKeys.Length;

        /// <summary>The key label for a kind's index: digits 1..9, then 0 for the tenth, then X.</summary>
        public static string HotkeyLabel(int index) => index <= 9 ? ((index + 1) % 10).ToString() : "X";

        /// <summary>The hint text for the keys the table actually reaches: "1-9 and 0" for a table of ten,
        /// and the letter named for the eleven the shipped content has. A hyphen, not an en dash: the built
        /// player's HUD font has no en dash.</summary>
        public static string KeyRangeText()
        {
            int count = System.Math.Min(BuildCatalog.Count, BuildKeys.Length);
            if (count <= 0) return "no keys";
            if (count < 10) return "1-" + count;
            return count < 11 ? "1-9 and 0" : "1-9, 0 and X";
        }

        private bool _pauseToggleRequested;
        private bool _startWaveRequested;
        private bool _restartRequested;
        private bool _primaryReleased;
        private int _rotateSteps;

        /// <summary>The sorter picker's one-shot, with the cell it is about: the cell travels in the
        /// command's own <see cref="InputCommand.FilterCell"/>, which is the field
        /// <see cref="BuildController.SetFilter"/> reads.</summary>
        private bool _filterSet;
        private Int2 _filterCell;
        private ShapeFilter _setFilter;

        private bool _buildHeld;
        private bool _removeHeld;
        private bool _wasBuildHeld;

        /// <summary>Has the "more kinds than keys" complaint been logged? Once is enough; every frame would
        /// bury everything else in the console.</summary>
        private static bool _hotkeyOverflowReported;

        /// <summary>What the player is building, or null for the inspect cursor - where a run starts, so
        /// the game opens with no ghost promising an unasked-for placement.</summary>
        private BuildKind? _selected;
        private bool _inspectPressed;
        private Int2 _cursor = new Int2(-1, -1);

        /// <summary>True exactly once after P was pressed. Cleared by the call.</summary>
        public bool ConsumePauseToggle()
        {
            bool value = _pauseToggleRequested;
            _pauseToggleRequested = false;
            return value;
        }

        /// <summary>Is N being asked for right now? Read by the driver as "go on" and by the world as
        /// "start the next wave". Non-destructive: both readers act on the same press in different states,
        /// and only the tick's consumer clears it.</summary>
        public bool StartWaveRequested => _startWaveRequested;

        /// <summary>Read every device once per frame: held state, latched one-shots, selection, cursor.
        /// <paramref name="pointerOverHud"/> is the HUD's "the mouse is mine right now" - while true the
        /// buttons do not reach the world.</summary>
        public void Sample(Camera camera, SimWorld world, bool pointerOverHud = false)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (!_hotkeyOverflowReported && BuildCatalog.Count > BuildKeys.Length)
                {
                    _hotkeyOverflowReported = true;
                    Debug.LogError("FACET: the content table has " + BuildCatalog.Count +
                        " build kinds but only " + BuildKeys.Length + " hotkeys - kinds after the tenth " +
                        "cannot be selected from the keyboard. Add keys or trim the table.");
                }

                // A locked kind is not selectable at all - the world would refuse to place it
                // (RejectionReason.Locked), so a ghost promising it would be a lie. The build bar's tiles
                // ask the same question, so the digit key and the click agree.
                for (int i = 0; i < BuildKeys.Length && i < BuildCatalog.Count; i++)
                    if (keyboard[BuildKeys[i]].wasPressedThisFrame && world.IsUnlocked(BuildCatalog.All[i]))
                        ToggleSelect(BuildCatalog.All[i]);

                // Q/E turn the ghost; rotation is latched, so two presses between ticks turn it twice.
                if (keyboard.qKey.wasPressedThisFrame) _rotateSteps--;
                if (keyboard.eKey.wasPressedThisFrame) _rotateSteps++;

                // Escape drops the selection to the inspect cursor, the way back to reading the map.
                if (keyboard.escapeKey.wasPressedThisFrame) _selected = null;

                // Space and P pause; starting a wave is N or the HUD button, so skipping the countdown
                // is always deliberate.
                if (keyboard.spaceKey.wasPressedThisFrame) _pauseToggleRequested = true;
                if (keyboard.pKey.wasPressedThisFrame) _pauseToggleRequested = true;
                if (keyboard.nKey.wasPressedThisFrame) _startWaveRequested = true;
                if (keyboard.rKey.wasPressedThisFrame) _restartRequested = true;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                bool held = mouse.leftButton.isPressed && !pointerOverHud;

                // A release is reported by the device or inferred from the button going up unannounced
                // (lost focus, a stalled WebGL canvas), so a placement is never left pending forever.
                if (mouse.leftButton.wasPressedThisFrame && !pointerOverHud && _selected == null)
                {
                    // With nothing selected the press is a read: selection is this type's state, so the
                    // simulation only hears "read this tile".
                    _inspectPressed = true;
                }
                if (mouse.leftButton.wasReleasedThisFrame || (_wasBuildHeld && !held)) _primaryReleased = true;

                _wasBuildHeld = held;
                _buildHeld = held;
                _removeHeld = mouse.rightButton.isPressed && !pointerOverHud;
            }

            _cursor = CursorCell(camera, world);
        }

        /// <summary>The command the next tick should apply. Named fields, not positional: the latches below
        /// are twelve of them, and a silent swap of two same-typed arguments would mis-wire the whole game
        /// (see <see cref="Gesture"/> for the single-gesture names the tests drive by).
        ///
        /// A latched filter command carries the sorter's own cell in <see cref="InputCommand.FilterCell"/> -
        /// its own field, not the cursor's. The picker is a click on a panel rather than on the map, so the
        /// cell it is about has to travel with the command, while the cursor stays where the mouse really is:
        /// everything else that reads it in the same tick (the tooltip, a keyboard inspect, the ghost) is
        /// about the map. The latch lasts exactly one command - <see cref="ConsumeOneShots"/> drops it.</summary>
        public InputCommand Snapshot()
            => new InputCommand(buildHeld: _buildHeld, removeHeld: _removeHeld, cursorCell: _cursor,
                primaryReleased: _primaryReleased, selected: _selected, rotateSteps: _rotateSteps,
                startWavePressed: _startWaveRequested, restartPressed: _restartRequested,
                inspectPressed: _inspectPressed, filterSet: _filterSet, setFilter: _setFilter,
                filterCell: _filterCell);

        /// <summary>The HUD's build bar asking for a selection: the kind its tile names, or none at all
        /// (the inspect cursor). It writes the same field the number keys write, so the next
        /// <see cref="Snapshot"/> carries it in <see cref="InputCommand.Selected"/> and the simulation
        /// stays the one place the selection is applied. The world applies it even while paused, so the
        /// highlighted tile follows the click.</summary>
        public void RequestSelect(BuildKind? kind)
        {
            if (kind.HasValue) ToggleSelect(kind.Value);
            else _selected = null;
        }

        /// <summary>Asking for the building already in hand drops it, which is how the player returns to
        /// the read cursor without a key of its own: the digit key and the bar's tile are two doors onto
        /// this one rule. Both doors ask <see cref="SimWorld.IsUnlocked"/> first, so a kind this map has not
        /// handed over is never selected at all.</summary>
        private void ToggleSelect(BuildKind kind) => _selected = _selected == kind ? null : kind;

        /// <summary>The HUD's rotate buttons, latched exactly like the Q/E keys.</summary>
        public void RequestRotate(int steps) => _rotateSteps += steps;

        /// <summary>
        /// The HUD's sorter picker asking for a new routing rule: the machine at
        /// <paramref name="cell"/> routes for <paramref name="filter"/> from the next applied tick. Latched
        /// rather than applied here for the same reason a placement is - a click happens between ticks, and
        /// the simulation is the one place state changes. The picker never asks for
        /// <see cref="ShapeFilter.None"/>, which the world refuses.
        /// </summary>
        public void RequestFilter(Int2 cell, ShapeFilter filter)
        {
            _filterSet = true;
            _filterCell = cell;
            _setFilter = filter;
        }

        /// <summary>Drop the one-shots, once a tick has actually applied them.</summary>
        public void ConsumeOneShots()
        {
            _primaryReleased = false;
            _inspectPressed = false;
            _rotateSteps = 0;
            _startWaveRequested = false;
            _restartRequested = false;
            _filterSet = false;
            _setFilter = ShapeFilter.None;
        }

        /// <summary>Tile under the mouse cursor, or (-1,-1) when there is no camera / no mouse.</summary>
        private static Int2 CursorCell(Camera camera, SimWorld world)
        {
            var mouse = Mouse.current;
            if (camera == null || mouse == null) return new Int2(-1, -1);

            Vector3 screen = mouse.position.ReadValue();
            screen.z = -camera.transform.position.z;
            Vector3 worldPos = camera.ScreenToWorldPoint(screen);
            return world.TileGrid.CellAt(new Vec2(worldPos.x, worldPos.y));
        }
    }
}
