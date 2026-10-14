using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// Where a hand-painted map comes from: one folder, and one way to make one.
    ///
    /// Two surfaces ask for a new map - the painting window's New Map button and the Scene view's editor,
    /// whichever of its two ways in was used - and a second copy of the folder literal or of the creation
    /// sequence would be a second place that has to learn about a change to either. What a new map starts as
    /// lives here too, because it is a statement about the data and not about either surface: the size the fields
    /// default to, laid out, with no doors and no waves on purpose, so the validation panel says exactly what a
    /// playable map still needs - a better first lesson than a map that merely looks finished.
    /// </summary>
    internal static class MapAssets
    {
        /// <summary>Where a map made here lands - one folder, so a hand-painted map sits beside the shipped
        /// assets instead of wherever it was first saved.</summary>
        public const string Folder = "Assets/Data/Maps";

        private const string NewFileName = "NewMap.asset";

        /// <summary>
        /// Make a map from nothing, write it, and hand it back selected. Never over the top of an existing file:
        /// the asset is the work, so <c>GenerateUniqueAssetPath</c> is what makes this safe to press twice.
        ///
        /// Selecting and pinging it is part of the one job rather than something each caller repeats: both of
        /// them exist to point their own surface at what was just made, and a new asset that is nowhere on screen
        /// reads as a button that did nothing.
        /// </summary>
        public static MapAsset New()
        {
            EnsureFolder();

            var asset = ScriptableObject.CreateInstance<MapAsset>();

            // OnValidate has not run on an object built in code, and a fresh asset's terrain array is
            // empty: Resize is the call that lays the grid out, so the first repaint has a whole map to
            // draw rather than an array it would have to grow in the middle of drawing.
            asset.Resize(asset.Width, asset.Height);

            string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + NewFileName);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            return asset;
        }

        /// <summary>The folder a map asset lives in, made on demand: Assets/Data is where the palette and the
        /// content table already are, and Maps is the subfolder hand-painted maps own.</summary>
        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data", "Maps");
        }
    }
}
