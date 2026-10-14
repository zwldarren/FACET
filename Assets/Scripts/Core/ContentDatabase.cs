using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>What drives a machine's per-tick work. A closed set - deliberately not a plugin system: a
    /// new machine is a new row in the definition table pointing at one of these. <see cref="None"/> is the
    /// belt, which lives on its own layer (<see cref="BeltField"/>) and is never stepped by
    /// <see cref="MachineSystem"/>.</summary>
    public enum BehaviorKind : byte
    {
        None = 0,

        /// <summary>Mines the patch under it and pushes the shape onto the belt it faces.</summary>
        Drill = 1,

        /// <summary>Runs a <see cref="RecipeDef"/>: eats its input, waits out the interval, pushes its
        /// output through any wired outlet. The decomposer and cutter are the same behaviour with two
        /// recipes.</summary>
        Converter = 2,

        /// <summary>Carries one item across a tile and drops it two cells ahead.</summary>
        Pipe = 3,

        /// <summary>One hub that balances whatever its surrounding belts deliver.</summary>
        Splitter = 4,

        /// <summary>Eats its diet and fires it at whatever is in range.</summary>
        Turret = 5,

        /// <summary>Routes by shape: the shape its definition filters for goes out the side it faces,
        /// everything else out the other wired outlets. Like a splitter it reads its ports off the belts, so
        /// it is the same hub on a different job.</summary>
        Sorter = 6,
    }

    /// <summary>A stable, human-readable identity for a piece of content. Strings, not enum ordinals: an
    /// ordinal shifts the moment a value is inserted - exactly the bug that had <c>Balance.Turret</c> doing
    /// arithmetic on an enum.</summary>
    public readonly struct ContentId : IEquatable<ContentId>
    {
        public readonly string Value;

        public ContentId(string value) => Value = value ?? string.Empty;

        public bool IsNone => string.IsNullOrEmpty(Value);

        public bool Equals(ContentId other)
            => string.Equals(Value ?? string.Empty, other.Value ?? string.Empty, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        /// <summary>No identity: what a machine that references no recipe carries. It is the empty string, so
        /// <c>default(ContentId)</c>, <see cref="None"/> and <c>new ContentId("")</c> are one identity -
        /// "nothing" must not have several spellings that fail to compare equal.</summary>
        public static readonly ContentId None = new ContentId(string.Empty);
    }

    /// <summary>One shape as content: its identity in the simulation, and how a UI spells it.
    /// <see cref="Glyph"/> is that spelling - the word a sentence uses for the shape. It is a word rather
    /// than a symbol because the font a built player draws IMGUI text with has no symbols for these shapes:
    /// every "○" the HUD quoted came out as a gap. Where a shape is a <em>mark</em> the HUD draws the
    /// silhouette from the same Palette (see <c>Facet.Game.HudIcons</c>), so neither the sentence nor the
    /// picture is a second source for what a shape looks like.</summary>
    public sealed class ShapeDef
    {
        public readonly ShapeType Shape;
        public readonly ContentId Id;
        public readonly string Name;
        public readonly string Glyph;

        public ShapeDef(ShapeType shape, string id, string name, string glyph)
        {
            Shape = shape;
            Id = new ContentId(id);
            Name = name;
            Glyph = glyph;
        }
    }

    /// <summary>One transformation a <see cref="BehaviorKind.Converter"/> performs per cycle. The decomposer
    /// is the shipped example - one circle in, two half-circles out per 1.5 s - and a later furnace or
    /// assembler is another row, not another behaviour.</summary>
    public sealed class RecipeDef
    {
        public readonly ContentId Id;

        /// <summary>The shape this recipe consumes.</summary>
        public readonly ShapeType Input;

        /// <summary>The shape it produces.</summary>
        public readonly ShapeType Output;

        /// <summary>Inputs consumed per cycle. One, for every recipe shipped so far.</summary>
        public readonly int InputCount;

        /// <summary>Outputs produced per cycle: two, for the decomposer, and the reason its output
        /// outruns a single belt.</summary>
        public readonly int OutputCount;

        /// <summary>Seconds one cycle takes.</summary>
        public readonly float Interval;

        /// <summary>How many outputs the machine may hold while its outlets are blocked, so a cycle
        /// is never lost to a full belt.</summary>
        public readonly int Buffer;

        public RecipeDef(ContentId id, ShapeType input, ShapeType output, int inputCount,
            int outputCount, float interval, int buffer)
        {
            Id = id;
            Input = input;
            Output = output;
            InputCount = inputCount;
            OutputCount = outputCount;
            Interval = interval;
            Buffer = buffer;
        }
    }

    /// <summary>One buildable thing: what it costs, what tile it leaves, which behaviour drives it, and (for
    /// a converter) which recipe it runs. The row that used to be spread across <c>BuildCatalog.TileFor</c>,
    /// <c>Balance.Cost</c> and the HUD's name switch.</summary>
    public sealed class MachineDef
    {
        /// <summary>Stable identity: what this building <em>is</em>, independently of the ordinal
        /// <see cref="BuildKind"/> happens to hold. An Editor asset stores content this way, because a value
        /// inserted into the enum shifts every later ordinal and an asset keyed by ordinal would silently
        /// retune the wrong building.</summary>
        public readonly ContentId Id;

        public readonly BuildKind Build;
        public readonly TileKind Tile;
        public readonly BehaviorKind Behavior;
        public readonly int Cost;
        public readonly string Name;

        /// <summary>What the building does, without its numbers - the HUD appends those from the
        /// definition, so a retuned stat changes what the HUD claims.</summary>
        public readonly string Description;

        /// <summary>The recipe a <see cref="BehaviorKind.Converter"/> runs; <see cref="ContentId.None"/>
        /// for everything else.</summary>
        public readonly ContentId RecipeId;

        /// <summary>The machine's own cycle time where it has one: a drill's mine interval, a pipe's
        /// transit time. 0 when the machine has no single interval (splitter, sorter, turret).</summary>
        public readonly float Interval;

        /// <summary>The shape a <see cref="BehaviorKind.Sorter"/> sends out through its facing side; every
        /// other shape leaves by the other wired outlets. <see cref="ShapeType.None"/> for every other
        /// behaviour, which routes nothing.</summary>
        public readonly ShapeType Filter;

        /// <summary>Health an enemy must chew through to demolish this building. <b>0 means
        /// indestructible</b>: never targeted and never damaged, which is how belts and custom tables that
        /// predate HP opt out.</summary>
        public readonly float MaxHp;

        /// <summary>Does this machine's own facing mean anything? A drill pushes onto the cell it faces, a
        /// pipe carries across in its facing, a sorter sends its filtered shape out the side it faces, and a
        /// turret's barrel rests where it faces - all four are placed by aiming them, and a view can draw that
        /// facing. False for the hubs that read their ports off the belts around them (a converter, a
        /// splitter): their stored facing is never read, so an arrow would promise what turning cannot set.
        /// Asked by the ghost that draws the arrow and the HUD that names the side.</summary>
        public bool UsesFacing
            => Tile == TileKind.Belt
               || (Behavior != BehaviorKind.None
                   && Behavior != BehaviorKind.Converter
                   && Behavior != BehaviorKind.Splitter);

        public MachineDef(ContentId id, BuildKind build, TileKind tile, BehaviorKind behavior, int cost,
            string name, string description, ContentId recipeId, float interval,
            ShapeType filter = ShapeType.None, float maxHp = 0f)
        {
            Id = id;
            Build = build;
            Tile = tile;
            Behavior = behavior;
            Cost = cost;
            Name = name;
            Description = description;
            RecipeId = recipeId;
            Interval = interval;
            Filter = filter;
            MaxHp = maxHp;
        }
    }

    /// <summary>One turret's numbers. A turret is a machine that also fires, so it carries its
    /// <see cref="BuildKind"/> for the definition table rather than the whole <see cref="MachineDef"/> - and
    /// no <see cref="ContentId"/> of its own, because a turret's identity is its machine's.</summary>
    public sealed class TurretDef
    {
        public readonly BuildKind Build;
        public readonly string Name;
        public readonly ShapeType Ammo;
        public readonly float FireInterval;
        public readonly float Damage;
        public readonly float Range;
        public readonly float ProjectileSpeed;

        public TurretDef(BuildKind build, string name, ShapeType ammo, float fireInterval,
            float damage, float range, float projectileSpeed)
        {
            Build = build;
            Name = name;
            Ammo = ammo;
            FireInterval = fireInterval;
            Damage = damage;
            Range = range;
            ProjectileSpeed = projectileSpeed;
        }

        /// <summary>Shots per second at an unlimited supply. Whether it ever reaches this is the
        /// belt's business: one belt delivers one item per second, so this is a ceiling, not a rate.</summary>
        public float ShotsPerSecond => FireInterval <= 0f ? 0f : 1f / FireInterval;
    }

    /// <summary>One enemy's numbers, including what it is weak to.</summary>
    public sealed class EnemyDef
    {
        /// <summary>Stable identity, for the same reason <see cref="MachineDef.Id"/> is: a value inserted
        /// into <see cref="EnemyKind"/> shifts every later ordinal, and a wave or asset that named an enemy by
        /// ordinal would silently name a different one.</summary>
        public readonly ContentId Id;

        public readonly EnemyKind Kind;
        public readonly string Name;
        public readonly float Hp;
        public readonly float Speed;

        /// <summary>Damage per hit, whether the target is a building or the Core.</summary>
        public readonly float Damage;
        public readonly float AttackInterval;
        public readonly ShapeType Weakness;

        /// <summary>How close an attackable machine's cell centre must be for the enemy to stop and attack it
        /// instead of walking on.</summary>
        public readonly float AggroRange;

        /// <summary>How far away an attackable machine is <em>noticed</em>: outside <see cref="AggroRange"/>
        /// but within this, the enemy leaves its path and charges the machine, then attacks once in reach.
        /// Without it a building two tiles off the lane is furniture, which reads as an enemy that cannot see.
        /// A value at or below <see cref="AggroRange"/> means enemies attack only what they walk into - the
        /// honest way to opt a kind out of charging.</summary>
        public readonly float DetectionRange;

        public EnemyDef(ContentId id, EnemyKind kind, string name, float hp, float speed, float damage,
            float attackInterval, ShapeType weakness, float aggroRange = 1.6f,
            float detectionRange = 2.5f)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Hp = hp;
            Speed = speed;
            Damage = damage;
            AttackInterval = attackInterval;
            Weakness = weakness;
            AggroRange = aggroRange;
            DetectionRange = detectionRange;
        }
    }

    /// <summary>The one table of "what exists": shapes, recipes, machines, turrets and enemies, looked up by
    /// their identity. Plain C# on purpose - the single source of truth the simulation, the view and the
    /// Editor asset all read - and engine-free, so <c>FACET.Core</c> keeps its <c>noEngineReferences</c>
    /// contract. The Editor's carrier is <c>ContentDatabaseAsset</c>, which converts at the boundary.
    ///
    /// Passed to <see cref="SimWorld"/> explicitly rather than read from a static, so a test (or a replay)
    /// can run against a different table and get a different, reproducible game. The one exception is
    /// <see cref="Default"/>, which <see cref="Balance"/> - the shipped numbers, kept as a facade for call
    /// sites that predate this table - reads through.</summary>
    public sealed class ContentDatabase
    {
        /// <summary>The shipped decomposer recipe's id: circle in, half-circles out.</summary>
        public static readonly ContentId DecomposeRecipeId = new ContentId("decompose");

        /// <summary>The shipped cutter recipe's id: square in, half-squares out.</summary>
        public static readonly ContentId BisectRecipeId = new ContentId("bisect");

        private readonly MachineDef[] _machines;
        private readonly TurretDef[] _turrets;
        private readonly EnemyDef[] _enemies;
        private readonly ShapeDef[] _shapes;
        private readonly RecipeDef[] _recipes;

        public ContentDatabase(MachineDef[] machines, TurretDef[] turrets, EnemyDef[] enemies,
            ShapeDef[] shapes, RecipeDef[] recipes)
        {
            if (machines == null) throw new ArgumentNullException(nameof(machines));
            if (turrets == null) throw new ArgumentNullException(nameof(turrets));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));

            _machines = IndexByEnum<BuildKind, MachineDef>(machines, m => m.Build);
            _turrets = IndexByEnum<BuildKind, TurretDef>(turrets, t => t.Build);
            _enemies = IndexByEnum<EnemyKind, EnemyDef>(enemies, e => e.Kind);
            _shapes = IndexByEnum<ShapeType, ShapeDef>(shapes, s => s.Shape);
            _recipes = recipes;

            BuildKinds = (BuildKind[])Enum.GetValues(typeof(BuildKind));
            EnemyKinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));

            ValidateRows();
        }

        /// <summary>A row that names something the table does not hold is refused here, at construction,
        /// rather than surfacing as silence at runtime: a converter whose recipe is missing would run nothing,
        /// and a turret with no gun definition would never fire - neither failure throws on its own. A partial
        /// table is legitimate (only reachable rows need be present); an inconsistent one is not.</summary>
        private void ValidateRows()
        {
            for (int i = 0; i < _machines.Length; i++)
            {
                MachineDef def = _machines[i];
                if (def == null) continue;

                if (def.Behavior == BehaviorKind.Converter)
                {
                    if (def.RecipeId.IsNone || !HasRecipe(def.RecipeId))
                        throw new ArgumentException(
                            "converter row \"" + def.Name + "\" (" + def.Id + ") " +
                            (def.RecipeId.IsNone ? "names no recipe"
                                : "names recipe \"" + def.RecipeId.Value + "\", which this table does not hold"),
                            "recipes");
                }

                if (def.Behavior == BehaviorKind.Turret && _turrets[i] == null)
                    throw new ArgumentException(
                        "turret row \"" + def.Name + "\" (" + def.Id + ") has no turret definition",
                        "turrets");
            }
        }

        private bool HasRecipe(ContentId id)
        {
            for (int i = 0; i < _recipes.Length; i++)
                if (_recipes[i] != null && _recipes[i].Id.Equals(id)) return true;

            return false;
        }

        /// <summary>Every buildable kind, in ordinal (hotkey) order.</summary>
        public BuildKind[] BuildKinds { get; }

        /// <summary>Every enemy kind, for a UI that prints the roster instead of hardcoding a count.</summary>
        public EnemyKind[] EnemyKinds { get; }

        /// <summary>Every recipe, so a tool (or the Editor asset that composes a table) can list them
        /// without knowing the ids in advance.</summary>
        public IReadOnlyList<RecipeDef> Recipes => _recipes;

        /// <summary>The machine rows, indexed by <see cref="BuildKind"/> ordinal; a slot is null when this
        /// table defines nothing for that kind. A partial table is legitimate - a world only needs rows for
        /// what it can reach - so a consumer walking content (like <see cref="MachineSystem"/>'s start-up
        /// check) walks the rows that are there, not the enum.</summary>
        public IReadOnlyList<MachineDef> Machines => _machines;

        public MachineDef Machine(BuildKind kind)
        {
            MachineDef def = At(_machines, (int)kind);
            if (def == null) throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such machine");
            return def;
        }

        public bool IsTurret(BuildKind kind) => At(_turrets, (int)kind) != null;

        public TurretDef Turret(BuildKind kind)
        {
            TurretDef def = At(_turrets, (int)kind);
            if (def == null) throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a turret");
            return def;
        }

        public EnemyDef Enemy(EnemyKind kind)
        {
            EnemyDef def = At(_enemies, (int)kind);
            if (def == null) throw new ArgumentOutOfRangeException(nameof(kind), kind, "no such enemy");
            return def;
        }

        /// <summary>The same lookup by <em>identity</em> rather than enum ordinal, for callers holding a
        /// content id - an Editor row, a saved table, a test - and the reason a building keeps its meaning
        /// when a value is inserted into the enum above it.
        ///
        /// Shapes are deliberately not here: the simulation uses <see cref="ShapeType"/> everywhere, so a
        /// second way to name a shape would be a second thing to keep in step.</summary>
        public MachineDef Machine(ContentId id)
        {
            int slot = MachineSlot(id);
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(id), id.Value, "no such machine");
            return _machines[slot];
        }

        /// <summary>The ordinal slot a machine id occupies, or -1 when nothing carries it.</summary>
        public int MachineSlot(ContentId id) => SlotOf(_machines, id, def => def.Id);

        public EnemyDef Enemy(ContentId id)
        {
            int slot = EnemySlot(id);
            if (slot < 0) throw new ArgumentOutOfRangeException(nameof(id), id.Value, "no such enemy");
            return _enemies[slot];
        }

        /// <summary>The ordinal slot an enemy id occupies, or -1 when nothing carries it.</summary>
        public int EnemySlot(ContentId id) => SlotOf(_enemies, id, def => def.Id);

        public ShapeDef Shape(ShapeType shape)
        {
            ShapeDef def = At(_shapes, (int)shape);
            if (def == null) throw new ArgumentOutOfRangeException(nameof(shape), shape, "no such shape");
            return def;
        }

        public RecipeDef Recipe(ContentId id)
        {
            for (int i = 0; i < _recipes.Length; i++)
                if (_recipes[i].Id.Equals(id)) return _recipes[i];

            throw new ArgumentOutOfRangeException(nameof(id), id.Value, "no such recipe");
        }

        /// <summary>The shipped table. Every number is the design's, with its reasoning next to it, and every
        /// value the simulation reads at runtime comes through here.</summary>
        public static ContentDatabase Default { get; } = BuildDefault();

        private static ContentDatabase BuildDefault()
        {
            var shapes = new[]
            {
                // The word a shape is spelled with in a sentence. It used to be one character - "○", "◠" -
                // and every place the HUD quoted one printed a gap, because the font a built player draws
                // IMGUI text with does not carry them. The map draws shapes, not characters, and so does the
                // HUD wherever a shape is a mark (see Facet.Game.HudIcons). In a sentence the name is the
                // honest spelling, and every player's font has it.
                new ShapeDef(ShapeType.None, string.Empty, "Nothing", "nothing"),
                new ShapeDef(ShapeType.Circle, "circle", "Circle", "circle"),
                new ShapeDef(ShapeType.HalfCircle, "half-circle", "Half-circle", "half-circle"),
                new ShapeDef(ShapeType.Square, "square", "Square", "square"),
                new ShapeDef(ShapeType.HalfSquare, "half-square", "Half-square", "half-square"),
            };

            // One circle in, two halves out, 1.5 s per split. A drill feeds circles at 1/s but the split
            // consumes them at 0.67/s, so circles back up behind the decomposer - it, not the drill, is the
            // stage that starves the line, and a second decomposer is the fix. Two halves per cycle is why one
            // belt pointing away caps it at 1 half/s; a second outlet runs at its own 1.33 halves/s.
            var recipes = new[]
            {
                new RecipeDef(DecomposeRecipeId, ShapeType.Circle, ShapeType.HalfCircle,
                    inputCount: 1, outputCount: 2, interval: 1.5f, buffer: 4),

                // The same recipe shape on the other mineral, so the cutter's numbers are the decomposer's:
                // the second chain is a second chain, not a rebalance of the first. What differs between the
                // two lines is the guns at the end, not the splitting.
                new RecipeDef(BisectRecipeId, ShapeType.Square, ShapeType.HalfSquare,
                    inputCount: 1, outputCount: 2, interval: 1.5f, buffer: 4),
            };

            var machines = new[]
            {
                // A belt is cheap per cell but a long run is a real purchase, which makes the map's distances
                // matter. HP 0 - inert: a belt is not a machine, so nothing can target or damage it.
                new MachineDef(new ContentId("belt"), BuildKind.Belt, TileKind.Belt, BehaviorKind.None,
                    cost: 1, name: "Belt",
                    description: "carries one shape per tile · drag to lay a run",
                    recipeId: ContentId.None, interval: 0f, maxHp: 0f),

                // Matched to one belt, so a drill exactly saturates a line: one item every 30 ticks at
                // 30 Hz, never 31.
                new MachineDef(new ContentId("drill"), BuildKind.Drill, TileKind.Drill, BehaviorKind.Drill,
                    cost: 10, name: "Drill",
                    description: "on a shape patch · mines the belt it faces",
                    recipeId: ContentId.None, interval: 1f, maxHp: 40f),

                new MachineDef(new ContentId("decomposer"), BuildKind.Decomposer, TileKind.Decomposer,
                    BehaviorKind.Converter, cost: 15, name: "Decomposer",
                    description: "splits one shape into its parts",
                    recipeId: DecomposeRecipeId, interval: 0f, maxHp: 60f),

                // Well under a belt's one second per item, so a pipe never throttles the line it carries.
                new MachineDef(new ContentId("pipe"), BuildKind.Pipe, TileKind.Pipe, BehaviorKind.Pipe,
                    cost: 6, name: "Pipe",
                    description: "jumps one tile - the crossing piece · never jams",
                    recipeId: ContentId.None, interval: 0.25f, maxHp: 30f),

                new MachineDef(new ContentId("splitter"), BuildKind.Splitter, TileKind.Splitter,
                    BehaviorKind.Splitter, cost: 10, name: "Splitter",
                    description: "ports read off the belts around it · in/out by their direction",
                    recipeId: ContentId.None, interval: 0f, maxHp: 40f),

                // Mid-rate, mid-range, eats half-circles. Two shots kill a Spike, so a single fed cannon
                // holds a trickle; a packed wave wants two lines or a split feed.
                new MachineDef(new ContentId("cannon"), BuildKind.Cannon, TileKind.Turret,
                    BehaviorKind.Turret, cost: 20, name: "Cannon", description: string.Empty,
                    recipeId: ContentId.None, interval: 0f, maxHp: 80f),

                // Ships filtering for circles, which the split map wants: face the Core and the money banks
                // while the half-circles carry on. Priced under a cannon so the routing piece is the cheap half
                // of "keep money and ammo apart".
                new MachineDef(new ContentId("sorter"), BuildKind.Sorter, TileKind.Sorter,
                    BehaviorKind.Sorter, cost: 12, name: "Sorter",
                    description: "routes by shape · one shape leaves by the side it faces",
                    recipeId: ContentId.None, interval: 0f, filter: ShapeType.Circle, maxHp: 45f),

                // The second chain's converter. Identical to the decomposer but for which recipe it names -
                // the point of the behaviour seam: a new production stage is a row here, and nothing that steps
                // machines has to know it exists.
                new MachineDef(new ContentId("cutter"), BuildKind.Cutter, TileKind.Cutter,
                    BehaviorKind.Converter, cost: 15, name: "Cutter",
                    description: "splits one shape into its parts",
                    recipeId: BisectRecipeId, interval: 0f, maxHp: 60f),

                // The second gun. Deliberately not a straight upgrade: the mortar buys damage with a slower
                // cycle and a longer line, so the two guns answer different waves.
                new MachineDef(new ContentId("mortar"), BuildKind.Mortar, TileKind.Mortar,
                    BehaviorKind.Turret, cost: 30, name: "Mortar", description: string.Empty,
                    recipeId: ContentId.None, interval: 0f, maxHp: 100f),

                // The maze tool: cheap per cell, three times a drill's health, and nothing to do. It exists so
                // an enemy's walk can be shaped - and so a sealed Core is a decision the player can make and
                // an enemy can undo, in the twenty seconds a Spike needs at it, or the fifteen a Bulwark does.
                new MachineDef(new ContentId("wall"), BuildKind.Wall, TileKind.Wall, BehaviorKind.None,
                    cost: 2, name: "Wall", description: "blocks enemies · soaks their attacks",
                    recipeId: ContentId.None, interval: 0f, maxHp: 120f),
            };

            var turrets = new[]
            {
                new TurretDef(BuildKind.Cannon, "Cannon", ShapeType.HalfCircle,
                    fireInterval: 1f, damage: 3f, range: 7f, projectileSpeed: 14f),

                // Twice the damage on a cycle half again as long - 1.5 s against the cannon's 1 s, so
                // two-thirds the shot rate, not half. What the second gun really buys is the second ammunition,
                // which keeps it from being an upgrade: half-squares are resisted by a Spike, so a mortar needs
                // four shells where a cannon needs two shots, and they match a Bulwark. Each gun answers one
                // enemy.
                new TurretDef(BuildKind.Mortar, "Mortar", ShapeType.HalfSquare,
                    fireInterval: 1.5f, damage: 6f, range: 9f, projectileSpeed: 12f),
            };

            var enemies = new[]
            {
                // Fast, light, weak to half-circles. Two cannon shots bring one down, so the question is how
                // fast the line behind the cannon can feed it. Its 1.6-tile attack reach and 2.5-tile notice
                // range are the two machine radii: it hits what it walks into, and charges what it sees from a
                // tile and a half further out - which is why the map's guns sit three tiles off the lanes.
                new EnemyDef(new ContentId("spike"), EnemyKind.Spike, "Spike",
                    hp: 6f, speed: 1.5f, damage: 6f, attackInterval: 1f,
                    weakness: ShapeType.HalfCircle, aggroRange: 1.6f, detectionRange: 2.5f),

                // Slower but more dangerous, and the mirror of the Spike: weak to half-squares, so the mortar
                // brings one down in a single shell while a cannon barely marks it. The same six health, but it
                // walks at two-thirds the speed and hits the Core for ten - a wave of these asks whether the
                // second ammunition route exists, not whether it runs fast. It notices defences further out than
                // a Spike (3.2 against 2.5), so what stands between it and the Core has to hurt it, not just be
                // in the way.
                new EnemyDef(new ContentId("bulwark"), EnemyKind.Bulwark, "Bulwark",
                    hp: 6f, speed: 1f, damage: 10f, attackInterval: 1.25f,
                    weakness: ShapeType.HalfSquare, detectionRange: 3.2f),
            };
            return new ContentDatabase(machines, turrets, enemies, shapes, recipes);
        }

        /// <summary>Spread a definition list into a slot per enum ordinal, so a lookup is an array
        /// index and a kind with no definition reads back as null instead of a silent default.</summary>
        private static TDef[] IndexByEnum<TEnum, TDef>(TDef[] definitions, Func<TDef, TEnum> key)
            where TEnum : struct, Enum
            where TDef : class
        {
            var slots = new TDef[Enum.GetValues(typeof(TEnum)).Length];
            for (int i = 0; i < definitions.Length; i++)
            {
                // A sparse array is a natural way to build a table - only the kinds you care about,
                // addressed by ordinal - so an empty slot is simply a kind with no definition.
                if (definitions[i] == null) continue;
                slots[Convert.ToInt32(key(definitions[i]))] = definitions[i];
            }

            return slots;
        }

        private static T At<T>(T[] slots, int index) where T : class
            => index >= 0 && index < slots.Length ? slots[index] : null;

        /// <summary>The slot a definition with <paramref name="id"/> occupies, or -1 when nothing carries it.
        /// Linear scan: the table is a handful of rows, and an index would be more state to keep true than it
        /// saves.</summary>
        private static int SlotOf<TDef>(TDef[] slots, ContentId id, Func<TDef, ContentId> keyOf)
            where TDef : class
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && keyOf(slots[i]).Equals(id)) return i;

            return -1;
        }
    }
}
