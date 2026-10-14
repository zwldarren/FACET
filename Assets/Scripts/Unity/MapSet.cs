using UnityEngine;

namespace Facet.Game
{
    /// <summary>The campaign: the maps a run walks, in order.
    ///
    /// It exists because the maps stopped being code. While maps were an array in code, that array <em>was</em> the
    /// progression; once maps are assets, something has to name the set and its order. The alternative - an array on
    /// the driver - would hide the game's content in a scene file, where a search cannot find it; this is one asset,
    /// one file, one list that can be read, diffed and pointed at.
    ///
    /// It holds only the order. What a map <em>is</em> belongs to the map, which of them is playable data is
    /// <c>MapValidation</c>'s answer, and what "next" means is <see cref="MapCatalog"/>'s.</summary>
    [CreateAssetMenu(fileName = "Campaign", menuName = "FACET/Map Set", order = 3)]
    public sealed class MapSet : ScriptableObject
    {
        /// <summary>The maps, in the order they are played. An entry that names nothing is skipped rather than being a hole
        /// in the progression, so a set being filled in is a shorter campaign instead of a crash on the map after the gap.</summary>
        [Tooltip("Played in this order. An entry that names no map is skipped.")]
        public MapAsset[] Maps = new MapAsset[0];
    }
}
