using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>What drives a machine's per-tick work. A closed set - deliberately not a plugin system: a
    /// new machine is a new row in the definition table pointing at one of these. <see cref="None"/> is the
    /// belt, which lives on its own layer (<see cref="BeltField"/>) and is never stepped by
    /// <see cref="MachineSystem"/>.
    ///
    /// The rework replaced the two <em>recipe</em> behaviours (converter, assembler) with three
    /// <em>operator</em> behaviours (cutter, rotator, stacker): processing is an operator now, so the
    /// recipe table disappeared with them. Ordinals are authored data - a retired value keeps its number
    /// dead, exactly as in <see cref="TileKind"/>.</summary>
    public enum BehaviorKind : byte
    {
        None = 0,

        /// <summary>Mines the patch under it and pushes a full shape onto the belt it faces.</summary>
        Drill = 1,

        // 2 was Converter. Retired and not to be reused: the recipe table it read is gone, and splitting
        // is the cutter's behaviour.

        /// <summary>Carries one item across a tile and drops it two cells ahead.</summary>
        Pipe = 3,

        /// <summary>One hub that balances whatever its surrounding belts deliver.</summary>
        Splitter = 4,

        /// <summary>Eats any shape and fires it at whatever is in range.</summary>
        Turret = 5,

        /// <summary>Routes by shape: what its filter matches goes out the side it faces, everything else
        /// out the other wired outlets. Like a splitter it reads its ports off the belts, so it is the
        /// same hub on a different job.</summary>
        Sorter = 6,

        // 7 was Assembler. Retired and not to be reused: combining is the stacker's behaviour.

        /// <summary>Splits one item along the line its facing selects, out of the front of each of its two
        /// tiles: one piece runs straight on, the other leaves a lane over.</summary>
        Cutter = 8,

        /// <summary>Turns one item a quarter clockwise, out the side it faces.</summary>
        Rotator = 9,

        /// <summary>Unions two disjoint items into one, out through any wired outlet.</summary>
        Stacker = 10,

        /// <summary>Eats anything delivered into it and destroys it. The one behaviour with no outlet and
        /// with no state that can hold, so it is the one machine that cannot back up - which is the whole of
        /// what it is for.</summary>
        Sink = 11,
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

        /// <summary>No identity: what a machine that references nothing carries. It is the empty string, so
        /// <c>default(ContentId)</c>, <see cref="None"/> and <c>new ContentId("")</c> are one identity -
        /// "nothing" must not have several spellings that fail to compare equal.</summary>
        public static readonly ContentId None = new ContentId(string.Empty);
    }

    /// <summary>Which of the filter's rules a <see cref="ShapeFilter"/> states. The mode is explicit
    /// rather than inferred from an empty payload, because "routes nothing" and "routes everything" are
    /// opposite rules that an empty shape would otherwise spell the same way.</summary>
    public enum FilterMode : byte
    {
        /// <summary>Matches nothing: a machine with no filter at all.</summary>
        None = 0,

        /// <summary>Matches every non-empty item.</summary>
        Any = 1,

        /// <summary>Matches any item whose structure contains <see cref="ShapeFilter.Mineral"/>.</summary>
        Mineral = 2,

        /// <summary>Matches one exact item, orientation included.</summary>
        Exact = 3,
    }

    /// <summary>
    /// A rule over shapes, in the modes the design asks for: <b>anything</b> (a universal chamber, a
    /// cutter), <b>contains a mineral</b> ("anything with square in it", which is one rule for fifty mixed
    /// shapes) or <b>an exact item</b> (one of the 80, orientation included). It is both things a machine
    /// needs to know about an item - what a sorter <em>routes</em> for, and what a machine <em>eats</em> -
    /// which is why there is one type rather than two.
    /// </summary>
    public readonly struct ShapeFilter
    {
        public readonly FilterMode Mode;

        /// <summary>The exact item, in exact mode; <see cref="Shape.None"/> otherwise.</summary>
        public readonly Shape Exact;

        /// <summary>The mineral any filled quadrant may carry, in mineral mode;
        /// <see cref="Quad.Empty"/> otherwise.</summary>
        public readonly Quad Mineral;

        private ShapeFilter(FilterMode mode, Shape exact, Quad mineral)
        {
            Mode = mode;
            Exact = exact;
            Mineral = mineral;
        }

        /// <summary>No filter at all: what every machine that routes nothing carries.</summary>
        public static readonly ShapeFilter None = default;

        /// <summary>Everything a belt can carry: the universal chamber's diet, and every operator's.</summary>
        public static readonly ShapeFilter Any = new ShapeFilter(FilterMode.Any, Shape.None, Quad.Empty);

        /// <summary>An exact item, orientation included - the picker's other mode. An empty item is not
        /// one: an exact rule over nothing admits nothing, which is the silence the content table refuses
        /// everywhere else, so the refusal happens here with a cause rather than at a belt nobody can
        /// unblock.</summary>
        public static ShapeFilter Is(Shape shape)
        {
            if (shape.IsEmpty)
                throw new ArgumentException("an exact filter over an empty item would match nothing", nameof(shape));

            return new ShapeFilter(FilterMode.Exact, shape, Quad.Empty);
        }

        /// <summary>Any item whose structure contains this mineral. Empty is not a mineral - it is the
        /// quadrant enum's default, so accepting it here would turn "the author forgot to pick one" into a
        /// rule that matches every item with a hole in it and describes itself as "anything with nothing".
        /// </summary>
        public static ShapeFilter Has(Quad mineral)
        {
            if (mineral == Quad.Empty)
                throw new ArgumentException("a contains-mineral filter needs a mineral", nameof(mineral));

            return new ShapeFilter(FilterMode.Mineral, Shape.None, mineral);
        }

        public bool IsNone => Mode == FilterMode.None;

        public bool IsExact => Mode == FilterMode.Exact;

        /// <summary>Does this rule admit that item? Nothing is admitted by the no-filter rule, and an
        /// empty item is admitted by nobody: the empty side of a cut is not a delivery.</summary>
        public bool Matches(Shape shape)
        {
            if (shape.IsEmpty) return false;

            switch (Mode)
            {
                case FilterMode.Any: return true;
                case FilterMode.Mineral: return shape.Contains(Mineral);
                case FilterMode.Exact: return shape == Exact;
                default: return false;
            }
        }

        /// <summary>How a sentence says it: "any shape", the exact item's own words, or "anything with
        /// square".</summary>
        public string Describe()
        {
            switch (Mode)
            {
                case FilterMode.Any: return "any shape";
                case FilterMode.Exact: return Exact.Describe();
                case FilterMode.Mineral: return "anything with " + Shape.MineralName(Mineral);
                default: return "nothing";
            }
        }

        public override string ToString() => Describe();
    }

    /// <summary>One buildable thing: what it costs, what tile it leaves, which behaviour drives it, and (for
    /// a sorter) what it filters for. The row that used to be spread across <c>BuildCatalog.TileFor</c>,
    /// <c>Balance.Cost</c> and the HUD's name switch.
    ///
    /// There is no recipe field any more: processing <em>is</em> an operator, so a cutter needs no table to
    /// know what it does.</summary>
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

        /// <summary>The machine's own cycle time where it has one: a drill's mine interval, a cutter's
        /// split, a rotator's turn, a stacker's fuse, a pipe's transit. 0 when the machine has no single
        /// interval (splitter, sorter, turret).</summary>
        public readonly float Interval;

        /// <summary>What a <see cref="BehaviorKind.Sorter"/> routes for; <see cref="ShapeFilter.None"/> for
        /// every other behaviour, which routes nothing.</summary>
        public readonly ShapeFilter Filter;

        /// <summary>
        /// What this machine eats: a delivery the diet refuses jams the belt that landed it, which is the
        /// one rule that ties routing to shapes. <see cref="ShapeFilter.Any"/> is the shipped answer for
        /// every eating machine - the rework put the pressure on matching what a wave demands rather than on
        /// never landing the wrong item, so a wrong shape is paid for in damage, not in a dead line. The
        /// rule stays a content field because a table may still declare a narrower machine, and because
        /// "what may a machine take" has to have exactly one answer per machine.
        /// </summary>
        public readonly ShapeFilter Diet;

        /// <summary>Health an enemy must chew through to demolish this building. <b>0 means
        /// indestructible</b>: never targeted and never damaged, which is how belts and custom tables that
        /// predate HP opt out.</summary>
        public readonly float MaxHp;

        /// <summary>
        /// The operator this machine <em>is</em>, and therefore the one a map must have unlocked before it
        /// can be built: the cutter, the rotator and the stacker <b>are</b> their operators, so the unlock
        /// ladder (§7) needs no separate table of "which machine does this map allow" - the campaign hands
        /// over Cutter on map 1, Rotator on map 2 and Stacker on map 3, and the build bar follows.
        /// Everything else is scaffolding that needs no unlocking.
        ///
        /// Computed once from the behaviour, like the diet: a definition is replaced rather than mutated, so
        /// a property that re-ran a switch on every placement check would be the one field of this type not
        /// following the type's own discipline.
        /// </summary>
        public readonly Operators Requires;

        /// <summary>Does this machine's own facing mean anything? A drill pushes onto the cell it faces, a
        /// pipe carries across in its facing, a cutter's cut line is selected by it, a rotator's outlet is
        /// the side it faces, a sorter sends its filtered shape out that side, and a turret's barrel rests
        /// where it faces - all six are placed by aiming them, and a view can draw that facing. False for
        /// the hubs that read their ports off the belts around them (a stacker, a splitter): their stored
        /// facing is never read, so an arrow would promise what turning cannot set. False for a sink too,
        /// which eats from every side at once and has no outlet an arrow could point down.
        /// Asked by the ghost that draws the arrow and the HUD that names the side.</summary>
        public bool UsesFacing
            => Tile == TileKind.Belt
               || (Behavior != BehaviorKind.None
                   && Behavior != BehaviorKind.Stacker
                   && Behavior != BehaviorKind.Splitter
                   && Behavior != BehaviorKind.Sink);

        public MachineDef(ContentId id, BuildKind build, TileKind tile, BehaviorKind behavior, int cost,
            string name, string description, float interval,
            ShapeFilter filter = default, float maxHp = 0f, ShapeFilter? diet = null)
        {
            Id = id;
            Build = build;
            Tile = tile;
            Behavior = behavior;
            Cost = cost;
            Name = name;
            Description = description;
            Interval = interval;
            Filter = filter;
            MaxHp = maxHp;

            // A machine with a behaviour eats, so its default diet is everything; a belt or a wall has no
            // behaviour and eats nothing at all. Stated here rather than at every row, so a new machine of
            // an eating kind cannot be born starving by omission.
            Diet = diet ?? (behavior == BehaviorKind.None ? ShapeFilter.None : ShapeFilter.Any);

            // The other field that comes from the behaviour rather than from the row: which operator the map
            // has to have handed over before this machine may be built at all.
            Requires = behavior switch
            {
                BehaviorKind.Cutter => Operators.Cutter,
                BehaviorKind.Rotator => Operators.Rotator,
                BehaviorKind.Stacker => Operators.Stacker,
                _ => Operators.None,
            };
        }
    }

    /// <summary>One turret's numbers: how hard a shot is, how often it fires and how far it reaches.
    ///
    /// There is no diet here any more. The chamber is universal - it eats any non-empty shape, one round
    /// per shot - so a turret's <em>damage</em> is not a property of the gun but of what the factory fed
    /// it: a round matching the target's weakness lands full damage, anything else a quarter. That is the
    /// whole reason the gun has no ammo table and the answer to "which shape is good" lives in the wave
    /// briefing instead.</summary>
    public sealed class TurretDef
    {
        public readonly BuildKind Build;
        public readonly string Name;

        /// <summary>Damage one shot does to an enemy whose weakness it matches. A shot fired at anything
        /// else does <see cref="Balance.ResistantDamageMultiplier"/> of it.</summary>
        public readonly float Damage;

        public readonly float FireInterval;
        public readonly float Range;
        public readonly float ProjectileSpeed;

        public TurretDef(BuildKind build, string name, float damage, float fireInterval, float range,
            float projectileSpeed)
        {
            Build = build;
            Name = name;
            Damage = damage;
            FireInterval = fireInterval;
            Range = range;
            ProjectileSpeed = projectileSpeed;
        }

        /// <summary>Shots per second at an unlimited supply. Whether it ever reaches this is the
        /// belt's business: one belt delivers one item per second, so this is a ceiling, not a rate.</summary>
        public float ShotsPerSecond => FireInterval <= 0f ? 0f : 1f / FireInterval;
    }

    /// <summary>One enemy <em>archetype</em>: its movement and attack profile, and nothing about shapes.
    ///
    /// What an enemy is weak to used to live here, one shape per kind, which made every wave of that kind
    /// the same puzzle. The wave generator assigns the weakness per group now, so the same archetype comes
    /// at the player demanding a different round each time - and the two fields are genuinely independent:
    /// the archetype says how it walks and hits, the demand says what kills it.</summary>
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
            float attackInterval, float aggroRange = 1.6f, float detectionRange = 2.5f)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Hp = hp;
            Speed = speed;
            Damage = damage;
            AttackInterval = attackInterval;
            AggroRange = aggroRange;
            DetectionRange = detectionRange;
        }
    }

    /// <summary>The one table of "what exists": machines, turrets and enemies, looked up by their identity.
    /// Plain C# on purpose - the single source of truth the simulation, the view and the Editor asset all
    /// read - and engine-free, so <c>FACET.Core</c> keeps its <c>noEngineReferences</c> contract. The
    /// Editor's carrier is <c>ContentDatabaseAsset</c>, which converts at the boundary.
    ///
    /// Passed to <see cref="SimWorld"/> explicitly rather than read from a static, so a test (or a replay)
    /// can run against a different table and get a different, reproducible game. The one exception is
    /// <see cref="Default"/>, which the shipped numbers come from - a facade for call sites that predate
    /// this table (and for the HUD, which prints the shipped numbers it is playing against).
    ///
    /// Shapes have no rows: the item space is the algebra's 80 (<see cref="ShapePool.All"/>), fixed by the
    /// quadrant encoding rather than by content, so a table of shape rows would be a second, driftable
    /// statement of it. The rework deleted exactly that table.</summary>
    public sealed class ContentDatabase
    {
        private readonly MachineDef[] _machines;
        private readonly TurretDef[] _turrets;
        private readonly EnemyDef[] _enemies;

        public ContentDatabase(MachineDef[] machines, TurretDef[] turrets, EnemyDef[] enemies)
        {
            if (machines == null) throw new ArgumentNullException(nameof(machines));
            if (turrets == null) throw new ArgumentNullException(nameof(turrets));
            if (enemies == null) throw new ArgumentNullException(nameof(enemies));

            _machines = IndexByEnum<BuildKind, MachineDef>(machines, m => m.Build);
            _turrets = IndexByEnum<BuildKind, TurretDef>(turrets, t => t.Build);
            _enemies = IndexByEnum<EnemyKind, EnemyDef>(enemies, e => e.Kind);

            BuildKinds = (BuildKind[])Enum.GetValues(typeof(BuildKind));
            EnemyKinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));

            ValidateRows();
        }

        /// <summary>A row that names something the table does not hold is refused here, at construction,
        /// rather than surfacing as silence at runtime: a turret with no gun definition would never fire,
        /// and a sorter with no filter would route by nothing - neither failure throws on its own. A
        /// partial table is legitimate (only reachable rows need be present); an inconsistent one is
        /// not.</summary>
        private void ValidateRows()
        {
            for (int i = 0; i < _machines.Length; i++)
            {
                MachineDef def = _machines[i];
                if (def == null) continue;

                if (def.Behavior == BehaviorKind.Turret && _turrets[i] == null)
                    throw new ArgumentException(
                        "turret row \"" + def.Name + "\" (" + def.Id + ") has no turret definition",
                        "turrets");

                if (def.Behavior == BehaviorKind.Sorter && def.Filter.IsNone)
                    throw new ArgumentException(
                        "sorter row \"" + def.Name + "\" (" + def.Id + ") filters for nothing, so it " +
                        "would route by no rule at all", "machines");
            }
        }

        /// <summary>Every buildable kind, in ordinal (hotkey) order.</summary>
        public BuildKind[] BuildKinds { get; }

        /// <summary>Every enemy kind, for a UI that prints the roster instead of hardcoding a count.</summary>
        public EnemyKind[] EnemyKinds { get; }

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

        /// <summary>
        /// The same lookup asked the other way: false instead of a throw when this table defines nothing for
        /// that kind. A partial table is legitimate (see <see cref="Machines"/>), so a caller asking "may
        /// this be built here" needs a plain "no" for a kind the run's own table does not carry - the
        /// Default table's kinds are what the build bar offers, and a run may be played on a narrower one.
        /// </summary>
        public bool TryMachine(BuildKind kind, out MachineDef def)
        {
            def = At(_machines, (int)kind);
            return def != null;
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
        /// when a value is inserted into the enum above it.</summary>
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

        /// <summary>The shipped table. Every number is the design's, with its reasoning next to it, and every
        /// value the simulation reads at runtime comes through here.</summary>
        public static ContentDatabase Default { get; } = BuildDefault();

        private static ContentDatabase BuildDefault()
        {
            var machines = new[]
            {
                // A belt is cheap per cell but a long run is a real purchase, which makes the map's distances
                // matter. HP 0 - inert: a belt is not a machine, so nothing can target or damage it.
                new MachineDef(new ContentId("belt"), BuildKind.Belt, TileKind.Belt, BehaviorKind.None,
                    cost: 1, name: "Belt",
                    description: "carries one shape per tile · drag to lay a run",
                    interval: 0f, maxHp: 0f),

                // Matched to one belt, so a drill exactly saturates a line: one item every 30 ticks at
                // 30 Hz, never 31. It mines the full shape, which is the root of every chain on the map.
                new MachineDef(new ContentId("drill"), BuildKind.Drill, TileKind.Drill, BehaviorKind.Drill,
                    cost: 10, name: "Drill",
                    description: "on a shape patch · mines a full shape onto the belt it faces",
                    interval: 1f, maxHp: 40f),

                // The first operator, and the one the campaign teaches first: 1.5 s per pair, both pieces
                // out at once, one per lane. A quarter in yields the quarter plus nothing, so cutting
                // twice is a corollary rather than a rule. The description names the two tiles, because a
                // machine that turns a belt is a machine a player has to build a corner for - and this one
                // forks the belt it is already standing in.
                new MachineDef(new ContentId("cutter"), BuildKind.Cutter, TileKind.Cutter,
                    BehaviorKind.Cutter, cost: 15, name: "Cutter",
                    description: "splits one shape in two · two tiles wide: one piece runs straight on, " +
                                 "the other leaves a lane over",
                    interval: 1.5f, maxHp: 60f),

                // Well under a belt's one second per item, so the rotator never throttles a line: the
                // cost of orientation repair is the machine and its footprint, not its cycle time.
                new MachineDef(new ContentId("rotator"), BuildKind.Rotator, TileKind.Rotator,
                    BehaviorKind.Rotator, cost: 10, name: "Rotator",
                    description: "turns a shape a quarter clockwise · the orientation fix",
                    interval: 0.75f, maxHp: 45f),

                // The expensive operator, and the one that costs time: 2 s per fuse is the premium on
                // mixed rounds, and the reason a two-mineral line is a commitment.
                new MachineDef(new ContentId("stacker"), BuildKind.Stacker, TileKind.Stacker,
                    BehaviorKind.Stacker, cost: 25, name: "Stacker",
                    description: "unions two shapes that do not overlap · an overlapping pair waits",
                    interval: 2f, maxHp: 60f),

                // Well under a belt's one second per item, so a pipe never throttles the line it carries.
                new MachineDef(new ContentId("pipe"), BuildKind.Pipe, TileKind.Pipe, BehaviorKind.Pipe,
                    cost: 6, name: "Pipe",
                    description: "jumps one tile - the crossing piece · never jams",
                    interval: 0.25f, maxHp: 30f),

                new MachineDef(new ContentId("splitter"), BuildKind.Splitter, TileKind.Splitter,
                    BehaviorKind.Splitter, cost: 10, name: "Splitter",
                    description: "ports read off the belts around it · in/out by their direction",
                    interval: 0f, maxHp: 40f),

                // Ships filtering for circles, which the money line wants: face the Core and the full
                // circles bank while the ammo shapes carry on. Priced under a turret so the routing piece
                // is the cheap half of "keep money and ammo apart".
                new MachineDef(new ContentId("sorter"), BuildKind.Sorter, TileKind.Sorter,
                    BehaviorKind.Sorter, cost: 12, name: "Sorter",
                    description: "routes by shape · what its filter matches leaves by the side it faces",
                    interval: 0f, filter: ShapeFilter.Has(Quad.Circle), maxHp: 45f),

                // The maze tool: cheap per cell, three times a drill's health, and nothing to do. It exists
                // so an enemy's walk can be shaped - and so a sealed Core is a decision the player can make
                // and an enemy can undo.
                new MachineDef(new ContentId("wall"), BuildKind.Wall, TileKind.Wall, BehaviorKind.None,
                    cost: 2, name: "Wall", description: "blocks enemies · soaks their attacks",
                    interval: 0f, maxHp: 120f),

                // The one gun. Its numbers live on its TurretDef; what it fires is whatever the factory
                // fed it, which is why it has no diet and no splash.
                new MachineDef(new ContentId("turret"), BuildKind.Turret, TileKind.Turret,
                    BehaviorKind.Turret, cost: 20, name: "Turret",
                    description: "eats any shape · what it fires is what the line delivered",
                    interval: 0f, maxHp: 80f),

                // The drain, and the ending every fork needs: a cutter makes two pieces and a layout usually
                // wants one, so without somewhere for the surplus to go its lane fills and the machine that
                // made it stalls. Cheaper than the cutter it rescues, because a fix the player cannot afford
                // is the same as no fix - and free of any unlock, since the cutter's first lesson is also the
                // first way to get stuck.
                new MachineDef(new ContentId("sink"), BuildKind.Sink, TileKind.Sink, BehaviorKind.Sink,
                    cost: 4, name: "Sink",
                    description: "eats anything delivered into it · nothing comes out",
                    interval: 0f, maxHp: 30f),
            };

            var turrets = new[]
            {
                // 4 damage at 1 shot/s and 7 tiles: two matched shots bring a Runner down, and a resisted
                // shot barely marks it - so the gun's real rate is what its lines deliver.
                new TurretDef(BuildKind.Turret, "Turret", damage: 4f, fireInterval: 1f, range: 7f,
                    projectileSpeed: 14f),
            };

            var enemies = new[]
            {
                // Fast, light, cheap: the archetype a shallow demand comes with. Its 1.6-tile attack reach
                // and 2.5-tile notice range are the two machine radii - it hits what it walks into, and
                // charges what it sees from a tile and a half further out, which is why the map's guns sit
                // three tiles off the lanes.
                new EnemyDef(new ContentId("runner"), EnemyKind.Runner, "Runner",
                    hp: 6f, speed: 1.5f, damage: 6f, attackInterval: 1f,
                    aggroRange: 1.6f, detectionRange: 2.5f),

                // Slow, heavy, and the archetype a deep demand comes with: ten health to the Runner's
                // six, so it takes three matched shots where the Runner takes two - and ten resisted
                // ones, where the Runner folds after six. It walks at two-thirds the speed and hits the
                // Core for ten. It notices defences further out than a Runner (3.2 against 2.5), so what
                // stands between it and the Core has to hurt it, not just be in the way.
                new EnemyDef(new ContentId("tank"), EnemyKind.Tank, "Tank",
                    hp: 10f, speed: 1f, damage: 10f, attackInterval: 1.25f, detectionRange: 3.2f),
            };

            return new ContentDatabase(machines, turrets, enemies);
        }

        /// <summary>Spread a definition list into a slot per enum ordinal, so a lookup is an array
        /// index and a kind with no definition reads back as null instead of a silent default.
        ///
        /// Sized past the highest ordinal rather than by the member count (<see cref="EnumTable"/>): a closed
        /// enum here keeps its retired values' numbers dead, so the two are different numbers - and a table
        /// sized by the count is short by exactly the holes, which throws on the highest-valued row.</summary>
        private static TDef[] IndexByEnum<TEnum, TDef>(TDef[] definitions, Func<TDef, TEnum> key)
            where TEnum : struct, Enum
            where TDef : class
        {
            var slots = EnumTable.Create<TEnum, TDef>();
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
