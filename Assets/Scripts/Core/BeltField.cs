using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>State of one belt cell. A cell holds at most one item, which makes throughput equal to
    /// <see cref="SimConfig.BeltSpeed"/> and a jam a single locatable cell.</summary>
    public struct BeltState
    {
        /// <summary>Which way items leave this cell.</summary>
        public Dir Direction;

        /// <summary>Shape riding this cell, or nothing.</summary>
        public Shape Item;

        /// <summary>0 at the cell's entry edge, 1 at its exit edge. Sits at 1 while queued.</summary>
        public float Progress;

        /// <summary>Where the item was at the end of the previous tick, for view interpolation.</summary>
        public Vec2 PreviousWorldPosition;

        /// <summary>A jammed cell freezes its item and refuses anything from upstream. Only a machine whose
        /// diet refuses an item jams a belt - see <see cref="MachineDef.Diet"/> - so a jam is always the
        /// answer to "what did I feed that thing".</summary>
        public bool Jammed;

        public bool HasItem => !Item.IsEmpty;
    }

    /// <summary>One item, flattened for the view layer.</summary>
    public struct ItemSnapshot
    {
        public Shape Shape;
        public Vec2 PreviousPosition;
        public Vec2 Position;
    }

    /// <summary>One belt cell, flattened for the view layer.</summary>
    public struct BeltSnapshot
    {
        public Int2 Cell;
        public Dir Direction;
        public bool Jammed;
    }

    /// <summary>The belt layer: one <see cref="BeltState"/> per tile, indexed exactly like
    /// <see cref="TileGrid"/>. Whether a tile <em>is</em> a belt stays <see cref="TileGrid"/>'s business
    /// (<see cref="TileKind.Belt"/>); this stores only what is riding it. Where a belt may be laid is
    /// <see cref="TerrainField.CanLayBelt"/>: ground and ore take transport, so a run can cross a vein
    /// while rock, water and void take nothing.
    ///
    /// Determinism: every field is a fixed-length array walked by linear index, so no update depends on
    /// dictionary order. The one order-sensitive decision - which of two belts feeding a cell gets it -
    /// is settled by a fixed rule (<see cref="ResolveHandoffs"/>).</summary>
    public sealed class BeltField
    {
        /// <summary>Progress is snapped to exactly 1 once it lands this close, so an item can never be
        /// left a few float ulps below the hand-off threshold and queue forever. 1e-4 of a tile is far
        /// below one screen pixel.</summary>
        private const float ProgressEpsilon = 1e-4f;

        private const byte MemoTrue = 1;
        private const byte MemoFalse = 2;

        private readonly TileGrid _grid;

        /// <summary>The ground, for the one thing a removal owes it: handing the tile back exactly as the
        /// map drew it - a belt crossing a vein leaves the vein, one over bare ground leaves bare ground,
        /// without this layer remembering which was which.</summary>
        private readonly TerrainField _terrain;

        /// <summary>The stream jams and fixes are reported on. Derived telemetry: nothing here reads
        /// it back, so the belt layer's determinism does not depend on it.</summary>
        private readonly SimEventBuffer _events;

        private readonly BeltState[] _state;
        private readonly byte[] _memo;
        private readonly bool[] _visiting;
        private readonly bool[] _claimed;

        /// <summary>Commit list for the current tick, in post-order: successors before predecessors.</summary>
        private readonly List<int> _handoffOrder = new List<int>();

        /// <summary>Bumped by every change a renderer would have to redraw. Items moving deliberately do
        /// not bump it - they are drawn every frame, while the belt bed only changes when it is built.</summary>
        public int Revision { get; private set; }

        /// <summary>How many belt cells are blocked right now. Maintained on mutation, because the HUD
        /// reads it every frame and the twist's whole failure mode is "how many segments are red".</summary>
        public int JamCount { get; private set; }

        public BeltField(TileGrid grid, TerrainField terrain, SimEventBuffer events)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _events = events ?? throw new ArgumentNullException(nameof(events));

            int cells = grid.Width * grid.Height;
            _state = new BeltState[cells];
            _memo = new byte[cells];
            _visiting = new bool[cells];
            _claimed = new bool[cells];
        }

        public bool Has(Int2 cell) => _grid.Get(cell) == TileKind.Belt;

        public bool TryGet(Int2 cell, out BeltState state)
        {
            if (!Has(cell))
            {
                state = default;
                return false;
            }
            state = _state[_grid.Index(cell)];
            return true;
        }

        public bool HasItemAt(Int2 cell) => Has(cell) && _state[_grid.Index(cell)].HasItem;

        public bool IsJammed(Int2 cell) => Has(cell) && _state[_grid.Index(cell)].Jammed;

        /// <summary>Lay a new belt. Fails out of bounds, on a tile that already holds something, on
        /// ground that takes no transport, or on an existing belt. Ground and ore both take one
        /// (<see cref="TerrainField.CanLayBelt"/>), so a run can cross a vein.</summary>
        public bool TryPlace(Int2 cell, Dir direction)
        {
            if (!_terrain.CanLayBelt(cell)) return false;

            _grid.Set(cell, TileKind.Belt);
            int i = _grid.Index(cell);
            _state[i] = default;
            _state[i].Direction = direction;
            Revision++;
            return true;
        }

        /// <summary>Re-point an existing belt. Used by the drag while it still owns the cell it laid, so
        /// dragging never silently rotates a belt the player placed earlier.</summary>
        public bool TrySetDirection(Int2 cell, Dir direction)
        {
            if (!Has(cell)) return false;
            _state[_grid.Index(cell)].Direction = direction;
            Revision++;
            return true;
        }

        /// <summary>Remove a belt and destroy whatever it carried. The ground comes back: a belt crossing
        /// a vein leaves the vein, not a hole.</summary>
        public bool TryRemove(Int2 cell)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            if (_state[i].Jammed) JamCount--;
            _state[i] = default;
            _terrain.Release(cell);
            Revision++;
            return true;
        }

        /// <summary>Block a cell. The item on it freezes where it stands rather than snapping to a
        /// canonical position, so a jam reads as "stuck here", and everything upstream backs up behind
        /// it.</summary>
        public bool TryJam(Int2 cell)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            if (_state[i].Jammed) return true;
            _state[i].Jammed = true;
            JamCount++;
            Revision++;

            // The item that caused the jam, reported with it: a sound or a log line wants to say
            // *what* arrived wrong, not just that something did.
            _events.Jammed(cell, _state[i].Item);
            return true;
        }

        /// <summary>Clear a jam and destroy the item that caused it. Destroying the item is not
        /// optional: leave it in place and the very next tick re-jams the same cell.</summary>
        public bool TryClearJam(Int2 cell)
        {
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            if (_state[i].Jammed) JamCount--;
            _state[i].Jammed = false;
            _state[i].Item = Shape.None;
            _state[i].Progress = 0f;
            Revision++;
            _events.JamCleared(cell);
            return true;
        }

        /// <summary>Take the item off a cell and keep the belt - how a turret eats and an operator takes
        /// its delivery: the machine consumes the delivery and the line carries on. The caller decides
        /// whether the shape was right; this only moves it.</summary>
        public bool TryTakeItem(Int2 cell, out Shape shape)
        {
            shape = Shape.None;
            if (!Has(cell)) return false;

            int i = _grid.Index(cell);
            if (!_state[i].HasItem || _state[i].Jammed) return false;

            shape = _state[i].Item;
            _state[i].Item = Shape.None;
            _state[i].Progress = 0f;
            return true;
        }

        /// <summary>Drop an item onto an empty, unjammed belt cell. Drills use this; so do the tests,
        /// which is why it is not gated behind anything.</summary>
        public bool TrySpawnItem(Int2 cell, Shape shape)
        {
            if (shape.IsEmpty || !Has(cell)) return false;

            int i = _grid.Index(cell);
            if (_state[i].Jammed || _state[i].HasItem) return false;

            _state[i].Item = shape;
            _state[i].Progress = 0f;
            _state[i].PreviousWorldPosition = WorldPositionOf(cell, 0f, _state[i].Direction);
            return true;
        }

        /// <summary>Advance every item by one fixed step. Call once per simulation tick.</summary>
        public void Step(float dt, float beltSpeed)
        {
            SnapshotPreviousPositions();
            AdvanceProgress(dt, beltSpeed);
            ResolveHandoffs();
        }

        /// <summary>Remember where every item is now, so the view can interpolate from here next frame.</summary>
        private void SnapshotPreviousPositions()
        {
            for (int i = 0; i < _state.Length; i++)
            {
                if (!_state[i].HasItem) continue;
                _state[i].PreviousWorldPosition = WorldPositionOf(_grid.CellOf(i), _state[i].Progress, _state[i].Direction);
            }
        }

        /// <summary>Pass 1. An item advances to the end of its own cell and no further - it need not know
        /// whether the next cell is free, because progress 1 <em>is</em> the boundary it queues on.</summary>
        private void AdvanceProgress(float dt, float beltSpeed)
        {
            float step = beltSpeed * dt;

            for (int i = 0; i < _state.Length; i++)
            {
                if (!_state[i].HasItem || _state[i].Jammed || _state[i].Progress >= 1f) continue;

                float p = _state[i].Progress + step;
                _state[i].Progress = p >= 1f - ProgressEpsilon ? 1f : p;
            }
        }

        /// <summary>Passes 2 and 3. Decide who may move, then commit.
        ///
        /// This split is the whole reason the file exists. When a belt is packed, every item reaches
        /// progress 1 on the same tick and every successor is "occupied", so moving in place during one
        /// sweep collapses throughput and testing the raw snapshot deadlocks outright. Resolving "may I
        /// hand off" as a fixpoint first (a successor vacating this tick counts as free) makes a packed
        /// belt shift one cell as a train, at zero world-space motion.</summary>
        private void ResolveHandoffs()
        {
            // Pass 2: evaluate against one frozen snapshot and record the commit order.
            Array.Clear(_memo, 0, _memo.Length);
            Array.Clear(_visiting, 0, _visiting.Length);
            _handoffOrder.Clear();
            for (int i = 0; i < _memo.Length; i++) CanHandoff(i);

            // Pass 3: commit in post-order, so a cell vacating this tick is emptied before the cell
            // behind it fills it. Writing in plain index order would overwrite the item of a cell that
            // had not moved yet and then move the wrong item.
            Array.Clear(_claimed, 0, _claimed.Length);
            for (int k = 0; k < _handoffOrder.Count; k++)
            {
                int i = _handoffOrder[k];

                int n = _grid.Index(_grid.CellOf(i) + _state[i].Direction.Offset());
                if (_claimed[n]) continue;   // merge: the first claimant in commit order wins

                _claimed[n] = true;
                _state[n].Item = _state[i].Item;
                _state[n].Progress = 0f;
                _state[n].PreviousWorldPosition = _state[i].PreviousWorldPosition;

                _state[i].Item = Shape.None;
                _state[i].Progress = 0f;
            }
        }

        /// <summary>May the item on <paramref name="index"/> move on this tick? Memoised, and appended to
        /// <see cref="_handoffOrder"/> on the way out so predecessors are committed after successors. A
        /// cycle - a full loop of belts - is the one shape that genuinely deadlocks, so it evaluates
        /// false.</summary>
        private bool CanHandoff(int index)
        {
            byte memo = _memo[index];
            if (memo == MemoTrue) return true;
            if (memo == MemoFalse) return false;
            if (_visiting[index]) return false;

            _visiting[index] = true;
            bool result = ComputeCanHandoff(index);
            _visiting[index] = false;

            _memo[index] = result ? MemoTrue : MemoFalse;
            if (result) _handoffOrder.Add(index);
            return result;
        }

        private bool ComputeCanHandoff(int index)
        {
            BeltState s = _state[index];
            if (!s.HasItem || s.Jammed || s.Progress < 1f) return false;

            Int2 next = _grid.CellOf(index) + s.Direction.Offset();
            if (!_grid.InBounds(next) || _grid.Get(next) != TileKind.Belt) return false;  // end of the line

            int n = _grid.Index(next);
            if (_state[n].Jammed) return false;
            if (!_state[n].HasItem) return true;

            return _state[n].Progress >= 1f && CanHandoff(n);
        }

        /// <summary>Wipe every cell. Only used by a restart, which clears the tile grid itself first.</summary>
        public void Clear()
        {
            Array.Clear(_state, 0, _state.Length);
            JamCount = 0;
            Revision++;
        }

        /// <summary>Every belt cell, in stable linear-index order.</summary>
        public void GetBelts(List<BeltSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _state.Length; i++)
            {
                Int2 cell = _grid.CellOf(i);
                if (_grid.Get(cell) != TileKind.Belt) continue;

                into.Add(new BeltSnapshot
                {
                    Cell = cell,
                    Direction = _state[i].Direction,
                    Jammed = _state[i].Jammed,
                });
            }
        }

        /// <summary>Every jammed cell, in the same stable linear-index order as <see cref="GetBelts"/>.
        /// <see cref="JamCount"/> answers "how many"; this answers "which ones", which a player needs the
        /// moment the segment to right-click is off the screen. A dedicated walk rather than a filtered
        /// <see cref="GetBelts"/>: a jam is the one state a view has to go and find.</summary>
        public void GetJammed(List<Int2> into)
        {
            into.Clear();
            if (JamCount == 0) return;

            for (int i = 0; i < _state.Length; i++)
            {
                if (!_state[i].Jammed) continue;
                into.Add(_grid.CellOf(i));
            }
        }

        /// <summary>Every item, with the world positions the view interpolates between.</summary>
        public void GetItems(List<ItemSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < _state.Length; i++)
            {
                ref readonly BeltState s = ref _state[i];
                if (!s.HasItem) continue;

                Int2 cell = _grid.CellOf(i);
                into.Add(new ItemSnapshot
                {
                    Shape = s.Item,
                    PreviousPosition = s.PreviousWorldPosition,
                    Position = WorldPositionOf(cell, s.Progress, s.Direction),
                });
            }
        }

        /// <summary>World position of an item at a given progress. Progress 0 sits half a tile before the
        /// cell centre and 1 half a tile past it, so progress 1 of one cell and progress 0 of its successor
        /// are the same point - a hand-off never visibly jumps.</summary>
        private Vec2 WorldPositionOf(Int2 cell, float progress, Dir direction)
        {
            return _grid.CellCenter(cell) + direction.ToVec() * (progress - 0.5f);
        }
    }
}
