using System;

namespace Facet.Core
{
    /// <summary>All the build-mode input state: the selection (or none - the inspect cursor), the ghost's
    /// facing, and the press/drag state machine that decides *where* things land. It lives in the simulation
    /// because placement is a rule (a drill only on a patch, a belt needs a direction); the view only reads
    /// <see cref="Selected"/> and <see cref="PlacementDirection"/> to draw the ghost.
    ///
    /// A belt drag lays nothing on the press - the path grows a tile at a time and only cells this drag owns
    /// may be re-pointed, so sweeping an existing run cannot rewire it - while a machine drag merely *aims*
    /// and lands on the press cell. Both answer "which way did this drag go?" with
    /// <see cref="DominantDirection"/>.
    ///
    /// A refused placement (dry stockpile, taken tile, wrong ground) reports on <see cref="SimEventBuffer"/>
    /// rather than returning false into the void, because a click that quietly does nothing reads as broken;
    /// a belt drag's swept cells stay silent, since a run that stops growing is visible. What is being built
    /// is fixed by the press until the button comes up.</summary>
    public sealed class BuildController
    {
        private static readonly Int2 NoCell = new Int2(int.MinValue, int.MinValue);

        private readonly TileGrid _grid;
        private readonly BeltField _belts;
        private readonly MachineField _machines;
        private readonly EconomyState _economy;

        /// <summary>The content table: what a building costs, and which operator it needs unlocked.</summary>
        private readonly ContentDatabase _content;

        /// <summary>The ground and the map itself: the things the read cursor can name that are not
        /// buildings - a vein, a rock, a pond, and a wave's doorway.</summary>
        private readonly TerrainField _terrain;
        private readonly MapDefinition _map;

        /// <summary>The stream placements and removals are reported on. Derived telemetry: the
        /// controller's decisions are unchanged by it.</summary>
        private readonly SimEventBuffer _events;

        private bool _dragging;
        private bool _machinePress;
        private Int2 _pressCell;
        private Int2 _dragCell;
        private bool _dragCellIsOurs;
        private Int2 _lastRemovedCell;

        /// <summary>Whether the gesture in progress has put a belt down anywhere. Read at the release: a
        /// gesture that built nothing owes the player an answer, because there is no belt to explain the
        /// silence.</summary>
        private bool _gestureLaid;

        /// <summary>Has the player placed anything at all this run? A rule's input, not telemetry: the
        /// opening's countdown starts with the first building (<see cref="WaveDirector.ArmFirstWave"/>), so a
        /// run where the player is still reading the map has no clock. Belts count - waiting for a machine
        /// would leave the clock stopped while they laid the line that machine would feed.</summary>
        public bool HasBuilt { get; private set; }

        public BuildKind? Selected { get; private set; }

        /// <summary>The tile the player last read while nothing was selected - the cell the HUD's info card
        /// describes. <see cref="NoCell"/> when nothing is pinned, which is also what bare ground pins: a
        /// click either describes something or clears the card. View state, kept beside the selection because
        /// it is the same kind of thing - the pointer's state, needed by two views, changing no tick.</summary>
        public Int2 InspectedCell { get; private set; }

        public Dir GhostDirection { get; private set; }

        /// <summary>The facing the cursor ghost should draw, which is not always
        /// <see cref="GhostDirection"/>: while a belt run is dragged the run carries its own direction, so the
        /// chevron shows the way the belt is being laid rather than the way Q/E last left the ghost.</summary>
        public Dir PlacementDirection
            => _dragging && Selected == BuildKind.Belt && _dragCell != _pressCell
                ? DominantDirection(_pressCell, _dragCell)
                : GhostDirection;

        public BuildController(TileGrid grid, TerrainField terrain, MapDefinition map, BeltField belts,
            MachineField machines, EconomyState economy, ContentDatabase content, SimEventBuffer events)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _events = events ?? throw new ArgumentNullException(nameof(events));
            Reset();
        }

        public void Reset()
        {
            // A run opens with nothing selected: the cursor reads the map until the player picks a building.
            // The default is a state, not a building.
            Selected = null;
            InspectedCell = NoCell;
            GhostDirection = Dir.East;
            _dragging = false;
            _machinePress = false;
            _pressCell = NoCell;
            _dragCell = NoCell;
            _dragCellIsOurs = false;
            _lastRemovedCell = NoCell;
            _gestureLaid = false;
            HasBuilt = false;
        }

        /// <summary>Would a placement of this kind be allowed here, aimed this way? Structural only - whether
        /// the stockpile can cover it is <see cref="EconomyState.CanAfford"/>, and the cursor ghost reads both.
        /// The facing is part of the question rather than a detail of it, because a cutter's footprint is
        /// turned by it: the machine stands on two tiles, so the tile the ghost is over is not always the only
        /// one that has to be free.</summary>
        public bool CanPlace(BuildKind kind, Int2 cell, Dir direction)
            => IsUnlocked(kind)
               && (kind == BuildKind.Belt ? _terrain.CanLayBelt(cell) : _machines.CanPlace(kind, cell, direction));

        /// <summary>Has this map unlocked the operator this building needs? The campaign's progression is
        /// the operator set (<see cref="MapDefinition.Unlocked"/>), so map 1 refuses a rotator outright
        /// rather than merely making it expensive - a locked machine is not a thing the player should be
        /// able to buy and then find useless.
        ///
        /// A kind the run's own table does not define reads as locked, not as an error: the build bar offers
        /// the <em>shipped</em> kinds, a table may legitimately carry fewer rows than the enum has values,
        /// and a machine nothing defines could not be placed anyway.</summary>
        public bool IsUnlocked(BuildKind kind)
            => _content.TryMachine(kind, out MachineDef machine)
               && (machine.Requires & ~_map.Unlocked) == 0;

        /// <summary>Place one building and pay for it. A drag lays belts cell by cell through this, so a
        /// line simply stops growing when the stockpile runs dry - skipped like a blocked tile, not an
        /// error.</summary>
        public bool TryPlace(BuildKind kind, Int2 cell, Dir direction)
        {
            if (!CanPlace(kind, cell, direction)) return false;
            if (!_economy.TrySpend(kind)) return false;

            bool placed = kind == BuildKind.Belt
                ? _belts.TryPlace(cell, direction)
                : _machines.TryPlace(cell, kind, direction);

            // Unreachable in practice - CanPlace just answered the same question - but a spend with
            // no placement must never stand.
            if (!placed)
            {
                _economy.Refund(kind);
                return false;
            }

            _events.Built(cell, _economy.CostOf(kind));
            HasBuilt = true;
            return true;
        }

        /// <summary>Place one building at a point the player named - a click, or the press cell a drag
        /// started from - and when it is refused, say so on the stream with the reason: the HUD reads the
        /// refusal and puts it on screen. Only deliberate placements come through here; the cells a belt drag
        /// merely sweeps over go through <see cref="TryPlace"/> silently, because a run that stops growing at
        /// a dry stockpile is visible on the map and a drag across an existing build must not shout once per
        /// cell.</summary>
        private void PlaceOrReport(BuildKind kind, Int2 cell, Dir direction)
        {
            if (TryPlace(kind, cell, direction)) return;

            _events.PlacementRejected(cell, kind, ReasonFor(kind, cell, direction), _economy.CostOf(kind));
        }

        /// <summary>Why a placement was refused, read from the same rules that refused it and in the same
        /// order - <see cref="CanPlace"/> first, the stockpile second - so the toast names the rule that
        /// actually bit. The doorway is a machine's rule, so belts are exempt: a belt is walkable and may
        /// cross a door, and a belt refused on one is refused for the tile it is on.
        ///
        /// A machine that stands on two tiles is refused for the tile that refused it, which is the second
        /// one whenever the ghost sits on a clear tile and the lane beside it does not - so "there is already
        /// something in the lane this cutter would need" is what the player is told.</summary>
        private RejectionReason ReasonFor(BuildKind kind, Int2 cell, Dir direction)
        {
            if (CanPlace(kind, cell, direction)) return RejectionReason.NoFunds;
            if (!IsUnlocked(kind)) return RejectionReason.Locked;
            if (!_grid.InBounds(cell)) return RejectionReason.OffMap;
            if (kind != BuildKind.Belt && _map.SpawnIndexOf(cell) >= 0) return RejectionReason.Doorway;
            if (_grid.IsOccupied(cell)) return RejectionReason.Occupied;

            if (kind != BuildKind.Belt && _content.TryMachine(kind, out MachineDef machine) &&
                MachineFootprint.TrySecond(machine.Behavior, cell, direction, out Int2 second))
            {
                if (!_grid.InBounds(second)) return RejectionReason.OffMap;
                if (_map.SpawnIndexOf(second) >= 0) return RejectionReason.Doorway;
                if (_grid.IsOccupied(second)) return RejectionReason.Occupied;
            }

            return RejectionReason.BadGround;
        }

        /// <summary>Take whatever is on this tile away. A jammed belt is un-jammed instead of deleted: that
        /// is the doc's "clears the jam" action, it destroys the wrong-shape item that caused the jam (leaving
        /// it would re-jam the same cell next tick), and it keeps the line's shape. Anything actually removed
        /// refunds its full cost.</summary>
        public bool Remove(Int2 cell)
        {
            if (_belts.IsJammed(cell)) return _belts.TryClearJam(cell);
            if (_belts.Has(cell))
            {
                if (!_belts.TryRemove(cell)) return false;
                _economy.Refund(BuildKind.Belt);
                _events.Removed(cell, _economy.CostOf(BuildKind.Belt));
                ForgetInspected(cell);
                return true;
            }
            if (_machines.TryGet(cell, out MachineState machine) && _machines.TryRemove(cell))
            {
                _economy.Refund(machine.Build);
                _events.Removed(cell, _economy.CostOf(machine.Build));
                ForgetInspected(cell);
                return true;
            }
            return false;
        }

        /// <summary>Removing what the info card describes drops the pin: the card would otherwise describe
        /// a hole in the ground. Clearing a jam keeps the belt, so it deliberately does not forget.</summary>
        private void ForgetInspected(Int2 cell)
        {
            if (cell == InspectedCell) InspectedCell = NoCell;
        }

        /// <summary>
        /// Re-route a standing sorter: the picker's verb. Only a sorter has a filter to set, so a command
        /// aimed at anything else is ignored rather than treated as an error - the picker is drawn from the
        /// machine under the cursor, and a cursor that moved between the click and the tick must not turn a
        /// routing change into a refusal.
        ///
        /// A filter that routes nothing is refused: <see cref="ShapeFilter.None"/> would leave the machine
        /// taking items it can never send anywhere, which stalls a line with no explanation.
        /// </summary>
        public bool SetFilter(Int2 cell, ShapeFilter filter)
        {
            if (filter.IsNone) return false;
            if (!_machines.TryGet(cell, out MachineState machine)) return false;
            if (_content.Machine(machine.Build).Behavior != BehaviorKind.Sorter) return false;

            _machines.RefAt(cell).Filter = filter;
            return true;
        }

        /// <summary>Change the selection outside the press/release gesture. The HUD's build bar and a number
        /// key come through this same door - the next tick carries the change in
        /// <see cref="InputCommand.Selected"/> - so there is still exactly one owner of the state. A gesture
        /// already in progress keeps its own selection, so a palette click can never re-purpose the drag
        /// halfway through.
        ///
        /// <see langword="null"/> asks for the inspect cursor - Escape, or the info card's button. It builds
        /// nothing and drops the inspected marker too, since the pin belongs to the read cursor and a
        /// highlight left behind in build mode would read as a second selection.</summary>
        public void Select(BuildKind? kind)
        {
            if (_dragging) return;
            Selected = kind;
            if (kind != null) InspectedCell = NoCell;
        }

        /// <summary>Read the tile under the cursor: what the HUD describes while nothing is selected. Bare
        /// ground unpins instead of pinning nothing, so a click either describes something or clears the
        /// card.</summary>
        public void Inspect(Int2 cell) => InspectedCell = CanInspect(cell) ? cell : NoCell;

        /// <summary>Is there something at this cell worth reading? The rule behind the pin, in one place so
        /// the cursor that marks the tile and the card that describes it read the same answer: a building
        /// (the Core included - it is a tile with state), the map's own ground wherever it is more than plain
        /// (a vein, a rock ridge, a pond, the void), or a wave's doorway. Bare ground is not, and a cell off
        /// the map is not a cell: without the bounds test an off-map cursor would read as
        /// <see cref="TerrainKind.Void"/> and pin the void itself.</summary>
        public bool CanInspect(Int2 cell)
            => _grid.InBounds(cell)
               && (_grid.Get(cell).IsBuilding() || _terrain.HasOre(cell) || _map.SpawnIndexOf(cell) >= 0
                   || _terrain.KindAt(cell).IsObstacle());

        /// <summary>Apply the input state that changes no rule: which building is selected, which tile
        /// the player is reading, and which way the ghost faces. One method because it is one rule - the
        /// pointer's state follows the player in every state, paused and over included, while everything
        /// <see cref="Apply"/> does to the world waits for a playing tick.
        ///
        /// The ghost's facing is pointer state too, not a world change: Q/E is how a cutter's cut line
        /// and a rotator's outlet are aimed, and re-aiming a line is exactly what a paused player is
        /// doing. It rode the playing branch once and a player who paused to plan could not turn the
        /// ghost at all - the ghost showed a facing no key could then change.
        ///
        /// A sorter's filter rides here rather than on <see cref="Apply"/> for the same reason, though it
        /// is the one member that does rewrite machine state: the picker is a card click, not a gesture,
        /// so it must never be readable as a press on the map, and a player re-routing a line while the
        /// run is held expects the setting to stick rather than be swallowed by the pause.</summary>
        public void ApplySelection(InputCommand cmd)
        {
            Select(cmd.Selected);
            if (cmd.InspectPressed) Inspect(cmd.CursorCell);
            if (cmd.RotateSteps != 0) GhostDirection = Rotate(GhostDirection, cmd.RotateSteps);
            if (cmd.FilterSet) SetFilter(cmd.FilterCell, cmd.SetFilter);
        }

        public void Apply(InputCommand cmd)
        {
            // The press decides what is being built; the selection waits for the next gesture. Updating it
            // mid-drag would re-purpose the gesture: aiming a drill, tapping the belt key and letting go
            // would lay a belt run instead of placing the drill. The ghost shows what the release will
            // build.
            ApplySelection(cmd);

            if (cmd.RemoveHeld)
            {
                // Right-drag erases: each new cell under the cursor is removed once.
                _dragging = false;
                _machinePress = false;
                if (cmd.CursorCell != _lastRemovedCell)
                {
                    _lastRemovedCell = cmd.CursorCell;
                    Remove(cmd.CursorCell);
                }
                return;
            }

            _lastRemovedCell = NoCell;

            // Nothing selected: the left button is the read cursor, so the gesture machine is never
            // entered. Right-drag delete above still works - it never needed a selection.
            if (!Selected.HasValue) return;

            bool isMachine = Selected.Value != BuildKind.Belt;

            // A press starts the drag. An input source that only reports "held" (a test, a replay)
            // still works: the press is simply taken to be wherever the cursor already is.
            if (!_dragging && cmd.BuildHeld)
            {
                _dragging = true;
                _machinePress = isMachine;
                _pressCell = cmd.CursorCell;
                _dragCell = cmd.CursorCell;
                _dragCellIsOurs = _belts.Has(cmd.CursorCell);
                _gestureLaid = false;
            }

            if (_dragging && cmd.BuildHeld && cmd.CursorCell != _dragCell)
            {
                if (isMachine)
                {
                    // Aim: the building still lands on the press cell, but it will face the way the
                    // drag went, and the ghost shows that while the button is down.
                    GhostDirection = DominantDirection(_pressCell, cmd.CursorCell);
                    _dragCell = cmd.CursorCell;
                }
                else
                {
                    // The first step leaves the press cell, so it decides which side of a machine under the
                    // press the run leaves from: point that machine at the run before laying it, or the run
                    // and the machine disagree.
                    if (_dragCell == _pressCell) PointMachineAtTheRun(cmd.CursorCell);
                    LayPath(cmd.CursorCell);
                }
            }

            bool released = cmd.PrimaryReleased || (_dragging && !cmd.BuildHeld);
            if (!released) return;

            bool wasDragging = _dragging;
            _dragging = false;

            if (_machinePress)
            {
                _machinePress = false;

                // A press only ever starts with a selection (the early return above) and Select refuses a
                // change while the gesture runs, so this is the kind the press was taken with. The machine
                // lands where the press was, and a refusal is reported rather than swallowed.
                PlaceOrReport(Selected.Value, _pressCell, GhostDirection);
                return;
            }

            // A belt gesture places as it drags. A click that never walked - or a drag whose every cell was
            // refused - still owes the player the belt it promised at the press cell, and the answer when it
            // cannot have one: the click that would otherwise vanish now says why.
            if (wasDragging && !_gestureLaid && !_belts.Has(_pressCell))
                PlaceOrReport(BuildKind.Belt, _pressCell, GhostDirection);
        }

        /// <summary>Walk from the cell the drag last reached to the cursor, one tile at a time, laying belts.
        ///
        /// The walk is Manhattan, and the axis it takes first is the drag's own dominant axis - the same rule
        /// <see cref="DominantDirection"/> uses to aim a machine - so a run dragged up the screen starts *up*
        /// from the press cell. A blocked tile is skipped but the walk continues, and the direction link is
        /// deliberately not carried across the gap.
        ///
        /// Only cells this drag owns get re-pointed: the one it started from (if already a belt) and the ones
        /// it laid. A belt the cursor merely passes over keeps its direction, so dragging across an existing
        /// build cannot silently rewire it.</summary>
        private void LayPath(Int2 target)
        {
            Int2 cur = _dragCell;
            bool curIsOurs = _dragCellIsOurs;

            // One decision for the whole walk: deciding per step would turn a diagonal drag into a
            // staircase instead of the two straight legs the cursor swept. The walk rule itself is
            // BeltWalk's - the same statement the test harness asserts against, so it cannot drift.
            bool yFirst = BeltWalk.YFirst(_pressCell, target);

            while (cur != target)
            {
                Int2 next = BeltWalk.NextStep(cur, target, yFirst);
                Dir dir = BeltWalk.DirectionOf(cur, next);

                // The press cell becomes a belt on the first step, so the run starts under the cursor rather
                // than one tile along; afterwards the cell is re-pointed forward, turning a corner into a
                // turn instead of a dead end. Placement goes through TryPlace so every cell is paid for: a
                // dry stockpile stops the run growing like a blocked tile does.
                if (curIsOurs) _belts.TrySetDirection(cur, dir);
                else curIsOurs = TryPlace(BuildKind.Belt, cur, dir);

                bool placed = TryPlace(BuildKind.Belt, next, dir);

                if (curIsOurs || placed) _gestureLaid = true;

                cur = next;
                curIsOurs = placed;
            }

            _dragCell = cur;
            _dragCellIsOurs = curIsOurs;
        }

        /// <summary>Point the machine standing on the press cell at the run just drawn out of it. A run
        /// leaving a machine's own cell can only be that machine's output - a belt that *feeds* a machine
        /// points into it, so it was drawn from the other end - so the machine turns to face the run whatever
        /// way the player aimed it.
        ///
        /// Without this a drill placed by a plain click keeps the ghost's default facing (east) and mines
        /// nothing into a run drawn north or south, which reads as "belts only work left and right". Drills
        /// and pipes only: their facing *is* their output. A turret eats from all four sides.</summary>
        private void PointMachineAtTheRun(Int2 cursorCell)
        {
            if (!_machines.TryGet(_pressCell, out MachineState machine)) return;
            if (machine.Kind != TileKind.Drill && machine.Kind != TileKind.Pipe) return;

            _machines.RefAt(_pressCell).Direction = DominantDirection(_pressCell, cursorCell);
        }

        /// <summary>The way a drag points a single-tile building: whichever axis it ran further along,
        /// with X winning a tie. The staircase itself is <see cref="BeltWalk"/>'s rule.</summary>
        private static Dir DominantDirection(Int2 from, Int2 to)
        {
            int dx = to.X - from.X;
            int dy = to.Y - from.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) return dx >= 0 ? Dir.East : Dir.West;
            return dy >= 0 ? Dir.North : Dir.South;
        }

        /// <summary>Quarter turns, clockwise for positive steps. Dir's ordinals are in clockwise order.</summary>
        private static Dir Rotate(Dir direction, int steps)
        {
            int value = ((int)direction + steps) % 4;
            if (value < 0) value += 4;
            return (Dir)value;
        }
    }
}
