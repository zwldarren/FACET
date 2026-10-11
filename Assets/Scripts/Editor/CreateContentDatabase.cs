using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using UnityEditor;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// Creates the tunable content asset from the shipped table, so a designer starts from every
    /// current value rather than from empty arrays. It reads <see cref="ContentDatabase.Default"/> -
    /// the same numbers the simulation runs on - so the asset can never start out disagreeing with the
    /// game, and there is exactly one place the shipped numbers are written down.
    ///
    /// Batchmode: <c>Unity -batchmode -quit -projectPath &lt;project&gt;
    /// -executeMethod Facet.EditorTools.CreateContentDatabase.CreateFilled</c>
    /// </summary>
    public static class CreateContentDatabase
    {
        private const string AssetPath = "Assets/Data/ContentDatabase.asset";

        [MenuItem("FACET/Create Content Database", priority = 20)]
        public static void Create()
        {
            ContentDatabaseAsset existing = AssetDatabase.LoadAssetAtPath<ContentDatabaseAsset>(AssetPath);
            if (existing != null)
            {
                // Never overwrite by hand: the asset IS the edit, and silently resetting it would
                // throw away whatever was tuned. Select it instead so editing is one click away.
                EditorUtility.DisplayDialog("FACET",
                    AssetPath + " already exists.\n\nSelect it in the Project window to edit the game's content.",
                    "OK");
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }

            CreateFilled();
        }

        /// <summary>Create the asset, fully populated from the shipped table.</summary>
        public static void CreateFilled()
        {
            var asset = ScriptableObject.CreateInstance<ContentDatabaseAsset>();
            Fill(asset);

            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log("FACET: created " + AssetPath + " from the shipped content table.");
        }

        /// <summary>
        /// Rebuild the existing asset's rows from the shipped table, <em>in place</em>.
        ///
        /// In place rather than by recreating the file, and this is the whole reason it is a separate
        /// entry point: Assets/Scenes/Main.unity references this asset by GUID, and replacing the object
        /// on disk would break that reference. "Start over from the shipped numbers" is also a
        /// destructive thing to do to a tuned asset, so it is a menu item with its own name rather than
        /// something that happens on its own.
        ///
        /// It is also how the asset learns about content added since it was made - a new mineral, a new
        /// recipe, a new building - without anyone hand-copying numbers.
        /// </summary>
        [MenuItem("FACET/Refresh Content Database from shipped table", priority = 21)]
        public static void Refresh()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ContentDatabaseAsset>(AssetPath);
            if (asset == null)
            {
                CreateFilled();
                return;
            }

            Fill(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log("FACET: refreshed " + AssetPath + " from the shipped content table.");
        }

        private static void Fill(ContentDatabaseAsset asset)
        {
            ContentDatabase shipped = ContentDatabase.Default;

            asset.Shapes = FillShapes(shipped);
            asset.Recipes = FillRecipes(shipped);
            asset.Machines = FillMachines(shipped);
            asset.Turrets = FillTurrets(shipped);
            asset.Enemies = FillEnemies(shipped);
        }

        private static ContentDatabaseAsset.ShapeRow[] FillShapes(ContentDatabase shipped)
        {
            var rows = new List<ContentDatabaseAsset.ShapeRow>();
            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
                rows.Add(ContentDatabaseAsset.ShapeRow.FromDef(shipped.Shape(shape)));

            return rows.ToArray();
        }

        private static ContentDatabaseAsset.RecipeRow[] FillRecipes(ContentDatabase shipped)
        {
            var rows = new List<ContentDatabaseAsset.RecipeRow>();
            foreach (RecipeDef recipe in shipped.Recipes)
                rows.Add(ContentDatabaseAsset.RecipeRow.FromDef(recipe));

            return rows.ToArray();
        }

        private static ContentDatabaseAsset.MachineRow[] FillMachines(ContentDatabase shipped)
        {
            var rows = new List<ContentDatabaseAsset.MachineRow>();
            foreach (BuildKind kind in shipped.BuildKinds)
                rows.Add(ContentDatabaseAsset.MachineRow.FromDef(shipped.Machine(kind)));

            return rows.ToArray();
        }

        private static ContentDatabaseAsset.TurretRow[] FillTurrets(ContentDatabase shipped)
        {
            var rows = new List<ContentDatabaseAsset.TurretRow>();
            foreach (BuildKind kind in shipped.BuildKinds)
            {
                if (!shipped.IsTurret(kind)) continue;

                // A turret row names its machine: that is where a turret's identity lives.
                rows.Add(ContentDatabaseAsset.TurretRow.FromDef(shipped.Machine(kind).Id,
                    shipped.Turret(kind)));
            }

            return rows.ToArray();
        }

        private static ContentDatabaseAsset.EnemyRow[] FillEnemies(ContentDatabase shipped)
        {
            var rows = new List<ContentDatabaseAsset.EnemyRow>();
            foreach (EnemyKind kind in shipped.EnemyKinds)
                rows.Add(ContentDatabaseAsset.EnemyRow.FromDef(shipped.Enemy(kind)));

            return rows.ToArray();
        }
    }
}
