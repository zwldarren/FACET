using System;
using Facet.Core;
using UnityEngine;

// Core's shape-filter mode collides with UnityEngine.FilterMode (the texture filter), and this file names it
// once, in the diet round-trip below. Same alias as ShapeField, for the same reason.
using FilterMode = Facet.Core.FilterMode;

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
    /// The rework deleted three row types with the mechanics behind them: the shape table (the item space is the
    /// algebra's 80, fixed by the quadrant encoding rather than by content), the recipe table (processing is an
    /// operator now) and the ammunition table (the chamber is universal). What is left is machines, guns and
    /// enemies - and the two places a shape is still content: what a machine eats, and what a sorter routes for.
    ///
    /// The row types below own the whole mapping to and from the engine-free definitions: each converts itself with
    /// <c>ToDef</c> and builds itself back with <c>FromDef</c>, so adding a stat is one edit beside the row's own
    /// fields rather than the same field written out again in a merge method and again in the asset creator.</summary>
    [CreateAssetMenu(fileName = "ContentDatabase", menuName = "FACET/Content Database", order = 1)]
    public sealed class ContentDatabaseAsset : ScriptableObject
    {
        [Serializable]
        public sealed class MachineRow
        {
            [Tooltip("Stable id for this building - what the row means. Leave it empty to key the row by " +
                "the Build dropdown alone; filled in, it wins over the dropdown, which is what lets a row " +
                "survive a value being inserted into the enum above it.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public BuildKind Build = BuildKind.Turret;
            [Tooltip("The tile a placement occupies. A Belt row is not needed - belts are their own layer.")]
            public TileKind Tile = TileKind.Turret;
            [Tooltip("Which behaviour drives it: Drill, Cutter, Rotator, Stacker, Pipe, Splitter, Sorter or " +
                "Turret. Which operator a machine is also decides whether a map must unlock it.")]
            public BehaviorKind Behavior = BehaviorKind.Turret;
            public int Cost = 20;
            public string Name = "Turret";
            [Tooltip("What it does, without numbers - the HUD appends those from the definition.")]
            public string Description = "";
            [Tooltip("The machine's cycle time where it has one: a drill's mine interval, a cutter's split, " +
                "a rotator's turn, a stacker's fuse, a pipe's transit.")]
            public float Interval;
            [Tooltip("A Sorter's shipped filter: what leaves by the side it faces, everything else by the " +
                "other wired outlets. Ignored by every other behaviour. The player may re-route a standing " +
                "sorter from the picker, so this is where it starts rather than where it stays.")]
            public ShapeFilterField Filter;
            [Tooltip("Health enemies must chew through to demolish this building. 0 keeps the shipped value.")]
            public float MaxHp = 0f;
            [Tooltip("What this machine eats: a delivery it refuses jams the belt that landed it. Leave " +
                "unticked to keep the shipped diet, which for every shipped machine is 'any shape'; tick it " +
                "and set the mode to None to author a machine that eats nothing at all.")]
            public bool OverrideDiet;
            public ShapeFilterField Diet;

            /// <summary>This row as the engine-free definition the simulation runs on. It takes the id, the
            /// <paramref name="build"/> its slot carries and the two values the merge resolved against that slot - the effective
            /// <paramref name="maxHp"/> and the effective <paramref name="diet"/> - rather than reading its own fields, so a row
            /// resolved by identity still describes the building its id names, and a field the row left unset keeps the shipped
            /// slot's value instead of this row's own default. That is this asset's purely-additive rule, and the reason a row
            /// that retunes one stat cannot silently reset another.</summary>
            public MachineDef ToDef(ContentId id, BuildKind build, float maxHp, ShapeFilter diet)
                => new MachineDef(id, build, Tile, Behavior, Cost, Name, Description, Interval,
                    Filter.ToCore(), maxHp, diet);

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
                Interval = def.Interval,
                Filter = ShapeFilterField.FromCore(def.Filter),
                MaxHp = def.MaxHp,

                // Ticked exactly when the definition's diet is not the one the merge would otherwise resolve -
                // the shipped machine's, which for every shipped machine is the one the constructor chooses
                // from the behaviour: "any shape" for a machine that eats, "nothing" for a belt or a wall. So
                // an unauthored diet keeps inheriting and an authored one survives, including the "eats
                // nothing" a narrower rule would read as "unset" and silently invert on reload.
                OverrideDiet = def.Diet.Mode != DefaultDietMode(def.Behavior),
                Diet = ShapeFilterField.FromCore(def.Diet),
            };

            /// <summary>The filter mode a machine's diet falls back to when a row states none: a machine with
            /// a behaviour eats everything, a belt or a wall eats nothing. What <see cref="FromDef"/> inverts,
            /// so a row written from a definition ticked its override exactly when the definition says
            /// something the slot would not have said on its own.</summary>
            private static FilterMode DefaultDietMode(BehaviorKind behavior)
                => behavior == BehaviorKind.None ? FilterMode.None : FilterMode.Any;
        }

        [Serializable]
        public sealed class TurretRow
        {
            [Tooltip("The id of the machine this gun is - a turret's identity is its machine's, so " +
                "there is no second id here. Leave it empty to key the row by the Build dropdown alone.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public BuildKind Build = BuildKind.Turret;
            public string Name = "Turret";
            [Tooltip("Damage one shot does to an enemy whose weakness it matches. A shot fired at anything " +
                "else does a half of it. 0 keeps the shipped value.")]
            public float Damage;
            [Tooltip("Seconds between shots. 0 keeps the shipped value.")]
            public float FireInterval;
            [Tooltip("Reach in tiles. 0 keeps the shipped value.")]
            public float Range;
            [Tooltip("Speed of the shot. 0 keeps the shipped value.")]
            public float ProjectileSpeed;

            /// <summary>This row as the engine-free definition the simulation runs on. It takes the numbers the merge resolved
            /// against the slot rather than reading its own fields, for the same additive reason <see cref="MachineRow.ToDef"/>
            /// takes its health and diet: a row that retunes the fire rate must not reset the gun's damage and reach to this
            /// row's own defaults, and a later retune of the shipped numbers must not be pinned by an asset that never named
            /// them.</summary>
            public TurretDef ToDef(BuildKind build, float damage, float fireInterval, float range,
                float projectileSpeed)
                => new TurretDef(build, Name, damage, fireInterval, range, projectileSpeed);

            public static TurretRow FromDef(ContentId id, TurretDef def) => new TurretRow
            {
                Id = id.Value,
                Build = def.Build,
                Name = def.Name,
                Damage = def.Damage,
                FireInterval = def.FireInterval,
                Range = def.Range,
                ProjectileSpeed = def.ProjectileSpeed,
            };
        }

        [Serializable]
        public sealed class EnemyRow
        {
            [Tooltip("Stable id for this enemy - what the row means. Leave it empty to key the row by " +
                "the Kind dropdown alone; filled in, it wins over the dropdown, which is what lets a row " +
                "survive a value being inserted into the enum above it.")]
            public string Id = "";
            [Tooltip("Which slot this row fills. Ignored when Id names something already shipped.")]
            public EnemyKind Kind = EnemyKind.Runner;
            public string Name = "Runner";
            public float Hp = 6f;
            public float Speed = 1.5f;
            [Tooltip("Damage per hit, whether the target is a building or the Core.")]
            public float Damage = 6f;
            public float AttackInterval = 1f;
            [Tooltip("How close an attackable machine must be for it to stop and attack. 0 keeps the " +
                "shipped value.")]
            public float AggroRange = 0f;
            [Tooltip("How far away an attackable machine is noticed and charged from. At or below the " +
                "attack reach it only attacks what it walks into. 0 keeps the shipped value.")]
            public float DetectionRange = 0f;

            // The archetype's weakness used to be a column here. It is the wave's business now - each group
            // in a wave names its own demand shape - so the same Runner asks for a different round every
            // wave, and there is nothing to retune here.

            /// <summary>This row as the engine-free definition the simulation runs on. It takes the effective radii the merge
            /// resolved rather than reading its own fields, for the same additive reason <see cref="MachineRow.ToDef"/> takes
            /// <c>maxHp</c>.</summary>
            public EnemyDef ToDef(ContentId id, EnemyKind kind, float aggroRange, float detectionRange)
                => new EnemyDef(id, kind, Name, Hp, Speed, Damage, AttackInterval, aggroRange, detectionRange);

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
                AggroRange = def.AggroRange,
                DetectionRange = def.DetectionRange,
            };
        }

        [Header("Content (a kind with no row keeps the shipped value)")]
        public MachineRow[] Machines;
        public TurretRow[] Turrets;
        public EnemyRow[] Enemies;

        /// <summary>Build the engine-free table this asset describes, starting from the shipped one and applying every row.
        /// Null row arrays are fine; so are individual null entries (the gap the Inspector leaves when a row is deleted).</summary>
        public ContentDatabase ToCore()
        {
            ContentDatabase shipped = ContentDatabase.Default;
            return new ContentDatabase(MergeMachines(shipped), MergeTurrets(shipped), MergeEnemies(shipped));
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

                // The diet, resolved against the slot rather than against the row's own Behaviour dropdown: a row
                // that states no diet keeps the shipped machine's, so an id-keyed row whose dropdown disagrees
                // with the slot it fills cannot arm a wall or starve a line as a side effect of a field it never
                // meant to set - and the "eats nothing" override survives, because OverrideDiet is what says a
                // row means it.
                ShapeFilter diet = row.OverrideDiet ? row.Diet.ToCore() : shippedDef.Diet;

                // The same rule for the filter: an untouched filter is the shipped one, so a row that only
                // retunes a cost cannot silently stop a sorter routing. Only rebuilt when the shipped slot
                // really has one - for every other kind the fallback would rebuild the definition it was
                // handed, so a sorter authored into a non-sorter slot still fails loudly in the table's own
                // validation, named by its row.
                MachineDef def = row.ToDef(row.IdOr(shippedDef.Id), shipped.BuildKinds[slot], maxHp, diet);
                if (def.Filter.IsNone && !shippedDef.Filter.IsNone)
                    def = WithFilter(def, shippedDef.Filter);
                defs[slot] = def;
            }

            return defs;
        }

        /// <summary>A machine definition with a different routing filter, which has no setter because a
        /// definition is meant to be replaced rather than mutated under a live machine.</summary>
        private static MachineDef WithFilter(MachineDef def, ShapeFilter filter)
            => new MachineDef(def.Id, def.Build, def.Tile, def.Behavior, def.Cost, def.Name, def.Description,
                def.Interval, filter, def.MaxHp, def.Diet);

        /// <summary>Which slot a row fills. An id the shipped table carries wins over the row's own enum, and that is why
        /// the rows may carry one: an enum value inserted above a building moves every later ordinal, so a row keyed by
        /// ordinal alone would retune its new neighbour instead. A row with no id (the default) is keyed by its dropdown,
        /// exactly as before ids existed - but an id that names nothing is <b>refused rather than fallen back on</b>,
        /// because a retired or misspelled id falling back to the ordinal would silently retune whatever now occupies
        /// that slot (a legacy "assembler" row, Build 9, would become the turret).</summary>
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

                // A row pointing at a machine that is not a turret would otherwise surface as a gun that never
                // fires, which blames the gun rather than the row.
                if (!shipped.IsTurret(shipped.BuildKinds[slot]))
                    throw new ArgumentException(
                        "turret row \"" + row.Name + "\" points at \"" + shipped.BuildKinds[slot] +
                        "\", which has no gun definition", "Turrets");

                // Each number the row leaves at 0 keeps the shipped gun's, the rule MaxHp and the enemy radii
                // already follow. Without it a row that retunes one stat resets the rest to the defaults above,
                // and an asset that never named a number would pin it against a later retune of the shipped
                // table - exactly the silent drift the FromDef/ToDef round-trip test exists to catch.
                TurretDef shippedTurret = shipped.Turret(shipped.BuildKinds[slot]);
                defs[slot] = row.ToDef(shipped.BuildKinds[slot],
                    row.Damage > 0f ? row.Damage : shippedTurret.Damage,
                    row.FireInterval > 0f ? row.FireInterval : shippedTurret.FireInterval,
                    row.Range > 0f ? row.Range : shippedTurret.Range,
                    row.ProjectileSpeed > 0f ? row.ProjectileSpeed : shippedTurret.ProjectileSpeed);
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
    }
}
