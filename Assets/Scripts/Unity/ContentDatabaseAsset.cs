using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace Facet.Game
{
    /// <summary>The Editor's carrier for the content table. A ScriptableObject so the numbers can be edited in the
    /// Inspector and live in an asset, but it is <em>not</em> the simulation's table: <see cref="ToCore"/> converts it
    /// to the engine-free <see cref="ContentDatabase"/>, which is what <see cref="SimWorld"/> is handed. That boundary
    /// is the whole reason <c>FACET.Core</c> can keep its <c>noEngineReferences</c> contract while the content is
    /// authored in the Editor.
    ///
    /// Every row is an <b>override</b>, not a full definition: a kind with no row keeps the shipped value, so this
    /// asset is purely additive - an empty one behaves exactly like the shipped game - and you only fill in what you
    /// are changing or adding. An existing kind's numbers can be retuned without recompiling, and a new kind is a row
    /// plus the enum value that names it.
    ///
    /// The row types below own the whole mapping to and from the engine-free definitions: each converts itself with
    /// <c>ToDef</c> and builds itself back with <c>FromDef</c>, so adding a stat is one edit beside the row's own
    /// fields rather than the same field written out again in a merge method and again in the asset creator.</summary>
    [CreateAssetMenu(fileName = "ContentDatabase", menuName = "FACET/Content Database", order = 1)]
    public sealed class ContentDatabaseAsset : ScriptableObject
    {
        [Serializable]
        public sealed class ShapeRow
        {
            [Tooltip("Which shape this row describes.")]
            public ShapeType Shape = ShapeType.Circle;
            public string Name = "Circle";
            [Tooltip("The word a sentence spells this shape with - \"no half-circle waiting on its lines\". " +
                "Where a shape is a mark rather than a word (beside the stockpile, on a build tile) the HUD " +
                "draws the silhouette the map draws, from the Palette, so this field never has to be a " +
                "character the player's font happens to carry.")]
            public string Glyph = "circle";

            [Tooltip("Tick for ammunition no enemy resists - the composite's trick.")]
            public bool MatchesAnyWeakness = false;

            /// <summary>This row as a definition. It keeps the shipped id: that is the simulation's identity for the shape,
            /// not a display name, so a row may rename what a shape is called without changing what it is.</summary>
            public ShapeDef ToDef(ContentId shippedId) => new ShapeDef(Shape, shippedId.Value, Name, Glyph,
                MatchesAnyWeakness);

            public static ShapeRow FromDef(ShapeDef def) => new ShapeRow
            {
                Shape = def.Shape,
                Name = def.Name,
                Glyph = def.Glyph,
                MatchesAnyWeakness = def.MatchesAnyWeakness,
            };
        }

        [Serializable]
        public sealed class RecipeRow
        {
            [Tooltip("Stable id, referenced by a Converter machine's Recipe Id.")]
            public string Id = "decompose";
            [Tooltip("The shape consumed per cycle.")]
            public ShapeType Input = ShapeType.Circle;
            [Tooltip("The second shape a two-input (assembler) recipe consumes; None for a split.")]
            public ShapeType Input2 = ShapeType.None;
            [Tooltip("The shape produced per cycle.")]
            public ShapeType Output = ShapeType.HalfCircle;
            [Min(1)] public int InputCount = 1;
            [Min(1)] public int OutputCount = 2;
            [Tooltip("Seconds one cycle takes.")]
            public float Interval = 1.5f;
            [Tooltip("How many outputs the machine may hold while its outlets are blocked.")]
            public int Buffer = 4;

            public RecipeDef ToDef() => new RecipeDef(new ContentId(Id), Input, Output, InputCount,
                OutputCount, Interval, Buffer, Input2);

            public static RecipeRow FromDef(RecipeDef def) => new RecipeRow
            {
                Id = def.Id.Value,
                Input = def.Input,
                Input2 = def.Input2,
                Output = def.Output,
                InputCount = def.InputCount,
                OutputCount = def.OutputCount,
                Interval = def.Interval,
                Buffer = def.Buffer,
            };
        }

        [Serializable]
        public sealed class MachineRow
        {
            [Tooltip("Stable id for this building - what the row means. Leave it empty to key the row by " +
                "the Build dropdown alone; filled in, it wins over the dropdown, which is what lets a row " +
                "survive a value being inserted into the enum above it.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public BuildKind Build = BuildKind.Cannon;
            [Tooltip("The tile a placement occupies. A Belt row is not needed - belts are their own layer.")]
            public TileKind Tile = TileKind.Turret;
            [Tooltip("Which behaviour drives it: Drill, Converter (needs a Recipe Id), Pipe, Splitter, " +
                "Sorter or Turret.")]
            public BehaviorKind Behavior = BehaviorKind.Turret;
            public int Cost = 20;
            public string Name = "Cannon";
            [Tooltip("What it does, without numbers - the HUD appends those from the definition.")]
            public string Description = "";
            [Tooltip("A Converter's recipe id, empty otherwise.")]
            public string RecipeId = "";
            [Tooltip("The machine's cycle time where it has one: a drill's mine interval, a pipe's transit.")]
            public float Interval;
            [Tooltip("A Sorter's filter: the shape that leaves by the side it faces, everything else by " +
                "the other wired outlets. Ignored by every other behaviour.")]
            public ShapeType Filter = ShapeType.Circle;
            [Tooltip("Health enemies must chew through to demolish this building. 0 keeps the shipped value.")]
            public float MaxHp = 0f;

            /// <summary>This row as the engine-free definition the simulation runs on. It takes the id, the
            /// <paramref name="build"/> its slot carries and the effective <paramref name="maxHp"/> rather than reading its own
            /// fields, so a row resolved by identity still describes the building its id names - and a row that leaves MaxHp at
            /// 0 still comes out with the shipped slot's health, mirroring this asset's purely-additive rule.</summary>
            public MachineDef ToDef(ContentId id, BuildKind build, float maxHp) => new MachineDef(id, build, Tile,
                Behavior, Cost, Name, Description, new ContentId(RecipeId), Interval, Filter, maxHp);

            /// <summary>The identity this row ends up carrying: its own, or the slot's shipped one when the row names none - so
            /// a table built from an asset never holds a definition that cannot be looked up by identity.</summary>
            public ContentId IdOr(ContentId shippedId) => RowId(Id, shippedId);

            /// <summary>The inverse. Every field of a definition is written down here and in <see cref="ToDef"/> and nowhere
            /// else, so a new stat is one edit in one class.</summary>
            public static MachineRow FromDef(MachineDef def) => new MachineRow
            {
                Id = def.Id.Value,
                Build = def.Build,
                Tile = def.Tile,
                Behavior = def.Behavior,
                Cost = def.Cost,
                Name = def.Name,
                Description = def.Description,
                RecipeId = def.RecipeId.Value,
                Interval = def.Interval,
                Filter = def.Filter,
                MaxHp = def.MaxHp,
            };
        }

        [Serializable]
        public sealed class AmmoEntry
        {
            [Tooltip("The shape the gun eats.")]
            public ShapeType Shape = ShapeType.HalfCircle;
            [Tooltip("What a shot of it does.")]
            public float Damage = 3f;

            public AmmoDef ToDef() => new AmmoDef(Shape, Damage);

            public static AmmoEntry FromDef(AmmoDef def) => new AmmoEntry { Shape = def.Shape, Damage = def.Damage };
        }

        [Serializable]
        public sealed class TurretRow
        {
            [Tooltip("The id of the machine this gun is - a turret's identity is its machine's, so " +
                "there is no second id here. Leave it empty to key the row by the Build dropdown alone.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public BuildKind Build = BuildKind.Cannon;
            public string Name = "Cannon";
            [Tooltip("The gun's whole diet: every shape it eats and what a shot of it does. An empty " +
                "list keeps the shipped diet, so a row can retune the gun without restating its ammunition.")]
            public AmmoEntry[] AmmoTypes;

            // The gun's diet before it became a list: one shape and one damage number. Kept - renamed,
            // hidden, and read only by MergeTurrets - so an asset authored as (Ammo, Damage) still loads
            // its retune instead of silently reverting to the shipped ammunition, which is what Unity's
            // dropping of the old fields would otherwise do. A row authored today leaves these at None/0,
            // which is exactly how the merge tells a legacy row from an untouched one.
            [HideInInspector, FormerlySerializedAs("Ammo")] public ShapeType LegacyAmmo = ShapeType.None;
            [HideInInspector, FormerlySerializedAs("Damage")] public float LegacyDamage = 0f;

            public float FireInterval = 1f;
            public float Range = 7f;
            public float ProjectileSpeed = 14f;
            [Tooltip("The shell's burst radius in tiles: every enemy that close to the impact takes the " +
                "hit. Negative keeps the shipped value, so 0 can mean what it says - a single-target gun.")]
            public float SplashRadius = -1f;

            public TurretDef ToDef(BuildKind build, AmmoDef[] ammoTypes, float splashRadius)
                => new TurretDef(build, Name, ammoTypes, FireInterval, Range, ProjectileSpeed, splashRadius);

            public static TurretRow FromDef(ContentId id, TurretDef def)
            {
                var ammo = new AmmoEntry[def.AmmoTypes.Length];
                for (int i = 0; i < ammo.Length; i++) ammo[i] = AmmoEntry.FromDef(def.AmmoTypes[i]);

                return new TurretRow
                {
                    Id = id.Value,
                    Build = def.Build,
                    Name = def.Name,
                    AmmoTypes = ammo,
                    FireInterval = def.FireInterval,
                    Range = def.Range,
                    ProjectileSpeed = def.ProjectileSpeed,
                    SplashRadius = def.SplashRadius,
                };
            }
        }

        [Serializable]
        public sealed class EnemyRow
        {
            [Tooltip("Stable id for this enemy - what the row means. Leave it empty to key the row by " +
                "the Kind dropdown alone; filled in, it wins over the dropdown, which is what lets a row " +
                "survive a value being inserted into the enum above it.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public EnemyKind Kind = EnemyKind.Spike;
            public string Name = "Spike";
            public float Hp = 6f;
            public float Speed = 1.5f;
            [Tooltip("Damage per hit, whether the target is a building or the Core.")]
            public float Damage = 6f;
            public float AttackInterval = 1f;
            [Tooltip("The shape it is weak to; also what the HUD names in the wave preview.")]
            public ShapeType Weakness = ShapeType.HalfCircle;
            [Tooltip("How close an attackable machine must be for it to stop and attack. 0 keeps the " +
                "shipped value.")]
            public float AggroRange = 0f;
            [Tooltip("How far away an attackable machine is noticed and charged from. At or below the " +
                "attack reach it only attacks what it walks into. 0 keeps the shipped value.")]
            public float DetectionRange = 0f;

            /// <summary>This row as the engine-free definition the simulation runs on. It takes the effective radii the merge
            /// resolved rather than reading its own fields, for the same additive reason <see cref="MachineRow.ToDef"/> takes
            /// <c>maxHp</c>.</summary>
            public EnemyDef ToDef(ContentId id, EnemyKind kind, float aggroRange, float detectionRange)
                => new EnemyDef(id, kind, Name, Hp, Speed, Damage, AttackInterval, Weakness, aggroRange,
                    detectionRange);

            /// <summary>The identity this row ends up carrying, or the slot's shipped one when it names none - see
            /// <see cref="MachineRow.IdOr"/>.</summary>
            public ContentId IdOr(ContentId shippedId) => RowId(Id, shippedId);

            public static EnemyRow FromDef(EnemyDef def) => new EnemyRow
            {
                Id = def.Id.Value,
                Kind = def.Kind,
                Name = def.Name,
                Hp = def.Hp,
                Speed = def.Speed,
                Damage = def.Damage,
                AttackInterval = def.AttackInterval,
                Weakness = def.Weakness,
                AggroRange = def.AggroRange,
                DetectionRange = def.DetectionRange,
            };
        }

        [Header("Content (a kind with no row keeps the shipped value)")]
        public ShapeRow[] Shapes;
        public RecipeRow[] Recipes;
        public MachineRow[] Machines;
        public TurretRow[] Turrets;
        public EnemyRow[] Enemies;

        /// <summary>Build the engine-free table this asset describes, starting from the shipped one and applying every row.
        /// Null row arrays are fine; so are individual null entries (the gap the Inspector leaves when a row is deleted).</summary>
        public ContentDatabase ToCore()
        {
            ContentDatabase shipped = ContentDatabase.Default;

            return new ContentDatabase(
                MergeMachines(shipped), MergeTurrets(shipped), MergeEnemies(shipped),
                MergeShapes(shipped), MergeRecipes(shipped));
        }

        /// <summary>The identity a row ends up carrying: the id it names, or the shipped one for the slot it fills. Shared
        /// by the machine and enemy rows so "an empty id means the slot's" is one rule, not one copy per row type.</summary>
        private static ContentId RowId(string rowId, ContentId shippedId)
        {
            var id = new ContentId(rowId);
            return id.IsNone ? shippedId : id;
        }

        private MachineDef[] MergeMachines(ContentDatabase shipped)
        {
            var defs = new MachineDef[shipped.BuildKinds.Length];
            for (int i = 0; i < defs.Length; i++) defs[i] = shipped.Machine(shipped.BuildKinds[i]);

            if (Machines == null) return defs;
            foreach (MachineRow row in Machines)
            {
                if (row == null) continue;

                int slot = SlotFor(new ContentId(row.Id), shipped.MachineSlot(new ContentId(row.Id)),
                    (int)row.Build, "Machines");
                MachineDef shippedDef = shipped.Machine(shipped.BuildKinds[slot]);

                // 0 on the row means "unset", so a row (or a whole asset) authored before HP existed keeps the shipped value -
                // the same purely-additive rule this asset follows everywhere.
                float maxHp = row.MaxHp > 0f ? row.MaxHp : shippedDef.MaxHp;
                defs[slot] = row.ToDef(row.IdOr(shippedDef.Id), shipped.BuildKinds[slot], maxHp);
            }

            return defs;
        }

        /// <summary>Which slot a row fills. An id the shipped table carries wins over the row's own enum, and that is why
        /// the rows may carry one: an enum value inserted above a building moves every later ordinal, so a row keyed by
        /// ordinal alone would retune its new neighbour instead. A row with no id (the default) is keyed by its dropdown,
        /// exactly as before ids existed - but an id that names nothing is <b>refused rather than fallen back on</b>,
        /// because a retired or misspelled id falling back to the ordinal would silently retune whatever now occupies
        /// that slot (a legacy "cutter" row, Build 7, would become the mortar).</summary>
        private static int SlotFor(ContentId id, int byId, int byOrdinal, string rows)
        {
            if (id.IsNone) return byOrdinal;
            if (byId < 0)
                throw new ArgumentException(
                    rows + " row names \"" + id.Value + "\", which the shipped table does not hold. " +
                    "Falling back to the row's own ordinal would silently retune whatever now occupies slot " +
                    byOrdinal + " - correct the id, or clear it to key the row by its dropdown.", rows);

            return byId;
        }

        private TurretDef[] MergeTurrets(ContentDatabase shipped)
        {
            var defs = new TurretDef[shipped.BuildKinds.Length];
            for (int i = 0; i < defs.Length; i++)
                if (shipped.IsTurret(shipped.BuildKinds[i])) defs[i] = shipped.Turret(shipped.BuildKinds[i]);

            if (Turrets == null) return defs;
            foreach (TurretRow row in Turrets)
            {
                if (row == null) continue;

                // A turret row names its machine, since that is where a turret's identity lives.
                var turretId = new ContentId(row.Id);
                int slot = SlotFor(turretId, shipped.MachineSlot(turretId), (int)row.Build, "Turrets");

                // A row pointing at a machine with no gun definition would otherwise surface as "empty
                // diet", which blames the diet rather than the row.
                if (!shipped.IsTurret(shipped.BuildKinds[slot]))
                    throw new ArgumentException(
                        "turret row \"" + row.Name + "\" points at \"" + shipped.BuildKinds[slot] +
                        "\", which has no gun definition", "Turrets");

                // Empty means "unset", the purely-additive rule the machine and enemy rows follow for
                // their own optional numbers: a row may retune the gun without restating its diet.
                AmmoDef[] ammoTypes = null;
                if (row.AmmoTypes != null && row.AmmoTypes.Length > 0)
                {
                    ammoTypes = new AmmoDef[row.AmmoTypes.Length];
                    for (int i = 0; i < ammoTypes.Length; i++) ammoTypes[i] = row.AmmoTypes[i].ToDef();
                }
                else if (row.LegacyAmmo != ShapeType.None)
                {
                    // An asset authored before the diet became a list: its one (Ammo, Damage) pair is the
                    // gun's whole diet. Honoured rather than dropped, so a retuned legacy gun keeps its
                    // retune instead of quietly returning to the shipped ammunition.
                    ammoTypes = new[] { new AmmoDef(row.LegacyAmmo, row.LegacyDamage) };
                }
                else
                {
                    ammoTypes = shipped.Turret(shipped.BuildKinds[slot]).AmmoTypes;
                }

                // Negative means "unset" here, unlike the counts above: 0 is a real radius - the
                // single-target gun - so it must stay authorable.
                float splashRadius = row.SplashRadius >= 0f
                    ? row.SplashRadius
                    : shipped.Turret(shipped.BuildKinds[slot]).SplashRadius;
                defs[slot] = row.ToDef(shipped.BuildKinds[slot], ammoTypes, splashRadius);
            }

            return defs;
        }

        private EnemyDef[] MergeEnemies(ContentDatabase shipped)
        {
            var defs = new EnemyDef[shipped.EnemyKinds.Length];
            for (int i = 0; i < defs.Length; i++) defs[i] = shipped.Enemy(shipped.EnemyKinds[i]);

            if (Enemies == null) return defs;
            foreach (EnemyRow row in Enemies)
            {
                if (row == null) continue;

                int slot = SlotFor(new ContentId(row.Id), shipped.EnemySlot(new ContentId(row.Id)),
                    (int)row.Kind, "Enemies");
                EnemyDef shippedDef = shipped.Enemy(shipped.EnemyKinds[slot]);
                float aggroRange = row.AggroRange > 0f ? row.AggroRange : shippedDef.AggroRange;
                float detectionRange = row.DetectionRange > 0f ? row.DetectionRange : shippedDef.DetectionRange;
                defs[slot] = row.ToDef(row.IdOr(shippedDef.Id), shipped.EnemyKinds[slot], aggroRange,
                    detectionRange);
            }

            return defs;
        }

        private ShapeDef[] MergeShapes(ContentDatabase shipped)
        {
            var defs = new ShapeDef[Enum.GetValues(typeof(ShapeType)).Length];
            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
                defs[(int)shape] = shipped.Shape(shape);

            if (Shapes == null) return defs;
            foreach (ShapeRow row in Shapes)
            {
                if (row == null) continue;
                defs[(int)row.Shape] = row.ToDef(shipped.Shape(row.Shape).Id);
            }

            return defs;
        }

        private RecipeDef[] MergeRecipes(ContentDatabase shipped)
        {
            var defs = new List<RecipeDef>(shipped.Recipes);

            if (Recipes == null) return defs.ToArray();
            foreach (RecipeRow row in Recipes)
            {
                if (row == null) continue;

                RecipeDef def = row.ToDef();

                int at = defs.FindIndex(r => r.Id.Equals(def.Id));
                if (at >= 0) defs[at] = def;   // retune the shipped recipe
                else defs.Add(def);            // or add a new one
            }

            return defs.ToArray();
        }
    }
}
