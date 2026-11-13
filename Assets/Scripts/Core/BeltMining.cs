using System;

namespace Facet.Core
{
    /// <summary>
    /// A belt laid on ore mines it. Each belt tile standing on a vein puts one full shape of that mineral onto
    /// itself every <see cref="MachineDef.Interval"/> of the drill - the rate a drill on that tile had - and
    /// the belt carries it on along its arrow like any other item.
    ///
    /// This replaces the drill as the player's way to mine: "drag a belt from the ore into the Core" is one
    /// building and one gesture, where the drill was a second building with a facing to get right before the
    /// first coin. The drill itself stays in the simulation (<see cref="BuildCatalog.Retired"/>), so a map or
    /// a test that stands one still works.
    ///
    /// A tile only mines into an empty, unjammed belt (<see cref="BeltField.TrySpawnItem(Int2, Shape)"/>), so
    /// a belt already carrying something from upstream is not overfilled - the vein waits its turn, the way a
    /// drill waited for room in front of it. The cooldown restarts from zero when a belt is laid, so a run
    /// dragged across a patch starts mining on its first tick rather than on a stale timer.
    /// </summary>
    public sealed class BeltMining
    {
        private readonly TileGrid _grid;
        private readonly TerrainField _terrain;
        private readonly BeltField _belts;
        private readonly ContentDatabase _content;

        /// <summary>Seconds until each tile may mine again; 0 is ready.</summary>
        private readonly float[] _cooldown;

        /// <summary>Was there a belt on this tile last tick? A belt that has just been laid mines at once.</summary>
        private readonly bool[] _hadBelt;

        /// <summary>The fallback when a content table has no drill to read the rate from.</summary>
        private const float DefaultInterval = 1f;

        public BeltMining(TileGrid grid, TerrainField terrain, BeltField belts, ContentDatabase content)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _content = content ?? throw new ArgumentNullException(nameof(content));

            _cooldown = new float[grid.Width * grid.Height];
            _hadBelt = new bool[grid.Width * grid.Height];
        }

        /// <summary>The rate a vein is mined at: the drill's, so the economy's numbers do not move.</summary>
        public float Interval
            => _content.TryMachine(BuildKind.Drill, out MachineDef drill) && drill.Interval > 0f
                ? drill.Interval
                : DefaultInterval;

        public void Step(float dt)
        {
            float interval = Interval;

            for (int y = 0; y < _grid.Height; y++)
            for (int x = 0; x < _grid.Width; x++)
            {
                var cell = new Int2(x, y);
                int i = _grid.Index(cell);

                if (!_belts.Has(cell))
                {
                    _hadBelt[i] = false;
                    continue;
                }

                if (!_hadBelt[i])
                {
                    _hadBelt[i] = true;
                    _cooldown[i] = 0f;
                }

                Quad mineral = _terrain.OreAt(cell);
                if (mineral == Quad.Empty) continue;

                if (_cooldown[i] > 0f)
                {
                    _cooldown[i] -= dt;
                    if (_cooldown[i] > 1e-5f) continue;
                }

                if (_belts.TrySpawnMined(cell, Shape.Full(mineral))) _cooldown[i] = interval;
            }
        }

        /// <summary>A restart is a new run: every vein is ready again.</summary>
        public void Reset()
        {
            Array.Clear(_cooldown, 0, _cooldown.Length);
            Array.Clear(_hadBelt, 0, _hadBelt.Length);
        }
    }
}
