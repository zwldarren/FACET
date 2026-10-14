using System.Collections.Generic;
using Facet.Core;

namespace Facet.Game
{
    /// <summary>The whole progression a run walks, as one list: the maps a <see cref="MapSet"/> names, in its order,
    /// already converted to the engine-free form the simulation runs on.
    ///
    /// It exists because "which map is next" is asked in four places - the driver that builds the world, the campaign
    /// that counts the maps, the HUD that names the one after this, and the tests - and each would otherwise read the
    /// map set, convert the assets, and disagree about what a null entry means. One catalog, built once, read for the
    /// rest of the session.
    ///
    /// There is deliberately no way to build an empty one: a campaign with no maps is a game with nothing to play, and
    /// <see cref="From"/> answers <see langword="null"/> for it, so the one caller that can do something - the driver,
    /// in Awake - is the one that sees it. An empty catalog instead would put that check in every reader of
    /// <see cref="At"/>.</summary>
    public sealed class MapCatalog
    {
        private readonly MapDefinition[] _maps;

        private MapCatalog(MapDefinition[] maps) => _maps = maps;

        /// <summary>The campaign a map set names: exactly the maps in it, in its order, with an entry that names no map
        /// skipped rather than being a hole - so a set being filled in is a shorter campaign instead of a crash on the
        /// map after the gap.</summary>
        public static MapCatalog From(MapSet set)
        {
            if (set == null || set.Maps == null) return null;

            var maps = new List<MapDefinition>(set.Maps.Length);
            for (int i = 0; i < set.Maps.Length; i++)
            {
                if (set.Maps[i] == null) continue;
                maps.Add(set.Maps[i].ToCore());
            }

            return maps.Count == 0 ? null : new MapCatalog(maps.ToArray());
        }

        public int Count => _maps.Length;

        /// <summary>The maps themselves, in order, for callers that walk the whole campaign rather than follow it one
        /// map at a time.</summary>
        public IReadOnlyList<MapDefinition> Maps => _maps;

        /// <summary>The map at an index, clamped into range: the driver's start-map field is authored data, and a stale
        /// index has to land on a real map rather than throwing in Awake.</summary>
        public MapDefinition At(int index) => _maps[Clamp(index)];

        /// <summary>Is there a map after this one? What the HUD's "carry on" button promises.</summary>
        public bool HasNext(int index) => Clamp(index) + 1 < _maps.Length;

        /// <summary>The map after this one, or the last one when there is none - so a caller that only wants to name what
        /// is coming never has to check twice.</summary>
        public MapDefinition NextAfter(int index) => _maps[Clamp(Clamp(index) + 1)];

        private int Clamp(int index) => index < 0 ? 0 : index >= _maps.Length ? _maps.Length - 1 : index;
    }
}
