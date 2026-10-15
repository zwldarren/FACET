using System;
using System.Text;

namespace Facet.Core
{
    /// <summary>What a building is for, as the catalogue and the HUD both say it. The colour the HUD
    /// tints each role with is the view's own; this enum is the fact the tint hangs off.</summary>
    public enum MachineRole { Transport, Wall, Mining, Processing, Routing, Defence }

    /// <summary>Whether a live note is just information, good news, or the player should look -
    /// <c>Warn</c> is the state a building is in that a fix at the cursor would change.</summary>
    public enum NoteSeverity { Neutral, Good, Warn }

    /// <summary>A line about a building plus how urgently it should be said, never the colour -
    /// colour is a palette question and lives in the view.</summary>
    public readonly struct ReadingNote
    {
        public readonly string Text;
        public readonly NoteSeverity Severity;

        public ReadingNote(string text, NoteSeverity severity)
        {
            Text = text;
            Severity = severity;
        }
    }

    /// <summary>
    /// The one statement of what a machine means to the player: the catalogue's lines, the live sentence
    /// for what a building is doing, its badge shape and its animated work fraction - all dispatched on
    /// the machine's <em>behaviour</em>, so a new content row is described, badged and animated with no
    /// view learning it.
    ///
    /// Every dispatch here enumerates every <see cref="BehaviorKind"/> by name and throws on anything
    /// else, the discipline <see cref="MachineField"/>'s badges hold themselves to - a new behaviour must
    /// be given a role, a line and a badge by name, never silently inherit one. The tests are the same
    /// seam: what the HUD would print is asserted through this interface, not a MonoBehaviour.
    ///
    /// Shapes are spelled with <see cref="Shape.Describe"/> now that the per-shape word table is gone: a
    /// shape's identity is its quadrants, so the words come from the quadrants rather than from a row that
    /// could disagree with them.
    ///
    /// Strings and severity only - no colours, no fonts, no layout. The view formats and tints.
    /// </summary>
    public static class MachineReading
    {
        // ---------------------------------------------------------------- landings

        /// <summary>The cell a drill pushes its mined shape onto: the tile it faces. One statement -
        /// the tick mines through it and the note describes it, so neither can drift.</summary>
        public static Int2 DrillLanding(Int2 cell, Dir direction) => cell + direction.Offset();

        /// <summary>The cell a pipe drops its carried shape onto: two tiles along its facing, jumping
        /// exactly one. One statement - the tick delivers through it and the note describes it, so the
        /// picture cannot drift from the simulation.</summary>
        public static Int2 PipeLanding(Int2 cell, Dir direction) => cell + direction.Offset() * 2;

        // ---------------------------------------------------------------- the catalogue

        /// <summary>What this building is for. Enumerated per behaviour; a new behaviour must join a
        /// role by name rather than silently inherit "transport".</summary>
        public static MachineRole Role(in MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Turret: return MachineRole.Defence;
                case BehaviorKind.Drill: return MachineRole.Mining;
                case BehaviorKind.Cutter:
                case BehaviorKind.Rotator:
                case BehaviorKind.Stacker: return MachineRole.Processing;
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter: return MachineRole.Routing;
                case BehaviorKind.Pipe: return MachineRole.Transport;
                case BehaviorKind.None:
                    // A belt and a wall are both behaviour-less; the tile kind tells them apart.
                    return def.Tile == TileKind.Belt ? MachineRole.Transport : MachineRole.Wall;
                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must declare which role the catalogue files it under");
            }
        }

        public static string RoleLabel(MachineRole role)
        {
            switch (role)
            {
                case MachineRole.Transport: return "TRANSPORT";
                case MachineRole.Wall: return "WALL";
                case MachineRole.Mining: return "MINING";
                case MachineRole.Processing: return "PROCESSING";
                case MachineRole.Routing: return "ROUTING";
                case MachineRole.Defence: return "DEFENCE";
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role,
                        "a new role needs the word the catalogue prints");
            }
        }

        /// <summary>The building in one sentence: the definition's own description where it has one,
        /// and a behaviour-shaped sentence where it does not - so a new gun or operator gets a sensible
        /// line without any view being edited.</summary>
        public static string Blurb(in MachineDef def, ContentDatabase content)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Turret:
                    return "eats any shape from any side · a round matching the target's weakness " +
                           "does full damage, anything else " + ResistShare();

                case BehaviorKind.Cutter:
                    // "Across", not "along": the cut line runs perpendicular to the facing, and the
                    // shipped wording once said "along the line it faces" - which reads as the cut
                    // following the arrow, and taught exactly the wrong aim. The line names the case a
                    // player needs and has not seen: the default east/west placement is the one they
                    // already know.
                    return "splits one shape in two · the cut runs across the facing: " +
                           "north/south cuts top/bottom";

                case BehaviorKind.Rotator:
                    return "turns a shape a quarter clockwise · the orientation fix";

                case BehaviorKind.Stacker:
                    return "unions two shapes that do not overlap · an overlapping pair waits on its belt";

                case BehaviorKind.Sorter:
                    // The row's words alone: what this sorter actually routes for is the machine's own
                    // filter, which can be re-set from the card, and the live note below says it. Quoting
                    // the row's filter here would put the definition's rule and the machine's rule on one
                    // card, disagreeing the moment the player re-routes it.
                    return def.Description;

                case BehaviorKind.None:
                case BehaviorKind.Drill:
                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                    return def.Description;

                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour needs a catalogue line, not a silent description");
            }
        }

        /// <summary>The numbers behind the sentence, read from the same definitions the tick runs
        /// on. Empty is legal ("this behaviour shows no numbers") but unknown is not.</summary>
        public static string Stats(in MachineDef def, ContentDatabase content, SimConfig config)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Drill:
                    return def.Interval > 0f
                        ? (1f / def.Interval).ToString("0.##") + " shapes/s mined"
                        : string.Empty;

                case BehaviorKind.Cutter:
                    return def.Interval.ToString("0.##") + " s per split · both pieces leave at once · holds " +
                           CutterBehavior.SlotBuffer + " a side";

                case BehaviorKind.Rotator:
                    return def.Interval.ToString("0.##") + " s per quarter turn";

                case BehaviorKind.Stacker:
                    return def.Interval.ToString("0.##") + " s per union · one union at a time";

                case BehaviorKind.Pipe:
                    return def.Interval > 0f
                        ? "crosses 2 tiles · " + def.Interval.ToString("0.##") + " s per item"
                        : string.Empty;

                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(def.Build);
                    float resisted = spec.Damage * Balance.ResistantDamageMultiplier;
                    return spec.ShotsPerSecond.ToString("0.##") + " shots/s · " +
                           spec.Damage.ToString("0.##") + " dmg matched, " + resisted.ToString("0.##") +
                           " if not · range " + spec.Range.ToString("0.##") + " tiles";
                }

                case BehaviorKind.Sorter:
                    return "one shape leaves by the side it faces · the rest take the other outlets";

                case BehaviorKind.Splitter:
                    return "deals items round-robin · skips blocked outlets";

                case BehaviorKind.None:
                    return def.Tile == TileKind.Belt
                        ? config.BeltSpeed.ToString("0.##") + " shapes/s per segment"
                        : def.MaxHp > 0f
                            ? def.MaxHp.ToString("0.##") + " hp · blocks enemies"
                            : string.Empty;

                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must say which numbers its line shows");
            }
        }

        // ---------------------------------------------------------------- the live card

        /// <summary>The one "this machine has nowhere to push" sentence. Five behaviours can wait on a
        /// wired outlet, and the wording is one sentence said here, not five copies waiting to drift.</summary>
        private static ReadingNote NoOutlet()
            => new ReadingNote("no outlet - lay a belt pointing away from it", NoteSeverity.Warn);

        /// <summary>
        /// What this building is doing right now, dispatched on its behaviour. The geometry a sentence
        /// names (a drill's landing, a pipe's) comes from the same functions the tick delivers through,
        /// so the card cannot describe a machine the simulation does not have.
        /// </summary>
        public static ReadingNote LiveNote(SimWorld world, Int2 cell, in MachineSnapshot machine)
        {
            ContentDatabase content = world.Content;
            MachineDef def = content.Machine(machine.Build);

            switch (def.Behavior)
            {
                case BehaviorKind.Drill:
                {
                    Int2 onto = DrillLanding(cell, machine.Direction);
                    Quad mineral = world.Terrain.OreAt(cell);
                    return world.TileGrid.Get(onto).Eats()
                        ? new ReadingNote("mining " + Shape.MineralName(mineral) + " onto " + onto,
                            NoteSeverity.Neutral)
                        : new ReadingNote("nothing to push onto at " + onto + " - lay a belt facing it",
                            NoteSeverity.Warn);
                }

                case BehaviorKind.Cutter:
                {
                    if (machine.OutMask.IsEmpty) return NoOutlet();

                    // The sentence names the shapes themselves, which the snapshot does not carry - read
                    // the state rather than the badge.
                    world.Machines.TryGet(cell, out MachineState state);
                    Dir front = machine.Direction;

                    if (!state.Carried.IsEmpty)
                    {
                        (Shape frontPiece, Shape backPiece) = CutterBehavior.Pieces(state.Carried, front);
                        return new ReadingNote("splitting " + state.Carried.Describe() + " · " +
                            Percent(machine.Work) + "% through · " + frontPiece.Describe() + " leaves " +
                            MachineReading.DirName(front) + ", " + backPiece.Describe() + " leaves " +
                            MachineReading.DirName(front.Opposite()), NoteSeverity.Neutral);
                    }

                    if (state.OutputCount > 0 || state.OutputCount2 > 0)
                        return new ReadingNote("holding " + Held(in state) +
                            " - an outlet is full", NoteSeverity.Warn);

                    return new ReadingNote("idle - waiting for a shape to cut", NoteSeverity.Neutral);
                }

                case BehaviorKind.Rotator:
                {
                    if (machine.OutMask.IsEmpty) return NoOutlet();

                    world.Machines.TryGet(cell, out MachineState state);
                    if (state.Carried.IsEmpty)
                        return new ReadingNote("idle - waiting for a shape to turn", NoteSeverity.Neutral);

                    return new ReadingNote("turning " + state.Carried.Describe() + " into " +
                        state.Carried.RotatedCW().Describe() + " · " + Percent(machine.Work) + "% through",
                        NoteSeverity.Neutral);
                }

                case BehaviorKind.Stacker:
                {
                    if (machine.OutMask.IsEmpty) return NoOutlet();

                    world.Machines.TryGet(cell, out MachineState state);

                    if (!state.Carried.IsEmpty && !state.Carried2.IsEmpty)
                        return new ReadingNote("fusing " + state.Carried.Describe() + " + " +
                            state.Carried2.Describe() + " · " + Percent(machine.Work) + "% through",
                            NoteSeverity.Neutral);

                    if (!state.Carried.IsEmpty)
                        return new ReadingNote("holding " + state.Carried.Describe() +
                            " - waiting for a piece that does not overlap it", NoteSeverity.Neutral);

                    if (state.OutputCount > 0)
                        return new ReadingNote("holding " + state.OutputShape.Describe() +
                            " - every outlet is full", NoteSeverity.Warn);

                    return new ReadingNote("idle - waiting for two pieces that fit together",
                        NoteSeverity.Neutral);
                }

                case BehaviorKind.Pipe:
                {
                    Int2 lands = PipeLanding(cell, machine.Direction);
                    return machine.Shape.IsEmpty
                        ? new ReadingNote("empty - nothing delivered", NoteSeverity.Neutral)
                        : new ReadingNote("carrying " + machine.Shape.Describe() + " to " + lands + " · " +
                            Percent(machine.Work) + "% across", NoteSeverity.Neutral);
                }

                case BehaviorKind.Splitter:
                    if (machine.OutMask.IsEmpty) return NoOutlet();

                    return machine.Shape.IsEmpty
                        ? new ReadingNote("dealing whatever arrives round robin", NoteSeverity.Neutral)
                        : new ReadingNote("holding " + machine.Shape.Describe() + " - every outlet is full",
                            NoteSeverity.Warn);

                case BehaviorKind.Sorter:
                    if (machine.OutMask.IsEmpty) return NoOutlet();

                    return new ReadingNote(machine.Filter.Describe() + " leaves by the side it faces · the rest " +
                        "take the other outlets", NoteSeverity.Neutral);

                case BehaviorKind.Turret:
                {
                    if (!machine.Armed)
                        return new ReadingNote("starved - nothing waiting on its lines", NoteSeverity.Warn);

                    string round = machine.Shape.IsEmpty
                        ? string.Empty
                        : " · last round " + machine.Shape.Describe();
                    return new ReadingNote(
                        (machine.HasTarget ? "armed · a target in range" : "armed · nothing in range") + round,
                        NoteSeverity.Good);
                }

                case BehaviorKind.None:
                    // A wall or a belt under inspection: the description is the whole truth.
                    return new ReadingNote(def.Description, NoteSeverity.Neutral);

                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must say what its card tells the player it is doing");
            }
        }

        /// <summary>What a built machine's facing means to it: a cutter's cut line and which piece leaves
        /// by which side, a rotator's outlet, a sorter's filtered shape, a turret's resting barrel, a
        /// drill's landing and a pipe's crossing. The hubs that read their ports off the belts around them
        /// have no facing of their own to name, and say so - the same rule the ghost draws its arrow by,
        /// <see cref="MachineDef.UsesFacing"/>.</summary>
        public static string FacingNote(in MachineDef def, in MachineSnapshot machine)
        {
            if (def.Behavior == BehaviorKind.None && def.Tile != TileKind.Belt)
                return "no facing - a wall only stands in the way";
            if (!def.UsesFacing) return "ports read off the belts around it";

            switch (machine.Behavior)
            {
                case BehaviorKind.Turret: return "barrel rests " + DirName(machine.Direction);
                case BehaviorKind.Sorter:
                    return machine.Filter.Describe() + " leaves " + DirName(machine.Direction);

                case BehaviorKind.Cutter:
                    return CutNote(machine.Direction) +
                           " - the front piece leaves " + DirName(machine.Direction);

                case BehaviorKind.None:      // a belt: its facing is where it carries
                case BehaviorKind.Drill:
                case BehaviorKind.Pipe:
                case BehaviorKind.Rotator:
                case BehaviorKind.Stacker:
                case BehaviorKind.Splitter:
                    return "facing " + DirName(machine.Direction);

                default:
                    throw new ArgumentOutOfRangeException(nameof(machine), machine.Behavior,
                        "a new behaviour must say what its card calls its facing");
            }
        }

        /// <summary>What a placement's facing would mean - the ghost's half of <see cref="FacingNote"/>,
        /// asked before the machine exists. The build card prints it beside the rotate buttons, so a
        /// player pressing Q/E watches the answer to "what will this cut" flip between the two cuts
        /// rather than having to know that the arrow names the outlet and not the blade.
        ///
        /// Deliberately the short forms: the line is a one-line label the turn buttons sit beside, so
        /// every answer here stays within roughly twenty characters - the long forms, and the piece-leaves-
        /// where semantics, live on <see cref="FacingNote"/> and <see cref="LiveNote"/>'s cards, which have
        /// the room.</summary>
        public static string GhostFacingNote(in MachineDef def, Dir direction)
        {
            if (def.Behavior == BehaviorKind.Cutter) return CutNote(direction);
            if (def.Behavior == BehaviorKind.None && def.Tile != TileKind.Belt)
                return "a wall has no facing";

            return def.UsesFacing ? "facing " + DirName(direction) : "ports read off belts";
        }

        /// <summary>The two cuts a cutter can be aimed at, said the one way both cards print it: which
        /// pair of pieces the facing selects. The facing side takes the piece on that side - which piece
        /// leaves where is the sentence <see cref="FacingNote"/> appends.</summary>
        private static string CutNote(Dir direction)
            => direction == Dir.East || direction == Dir.West
                ? "cuts left from right"
                : "cuts top from bottom";

        /// <summary>The sides a machine's belts wire it to: a belt pointing in is an input and a belt
        /// pointing away is an output, both masks straight off <see cref="MachineSnapshot"/>.</summary>
        public static string Ports(in MachineSnapshot machine)
            => machine.InMask.IsEmpty && machine.OutMask.IsEmpty
                ? "no belts wired - a belt pointing in feeds it, one pointing away empties it"
                : "in " + Dirs(machine.InMask) + "  ·  out " + Dirs(machine.OutMask);

        // ---------------------------------------------------------------- the badge and the spin

        /// <summary>
        /// What the building draws on itself. Keyed on the machine's <em>behaviour</em>, not its tile
        /// kind: a cutter shows the piece it is holding, a stacker the union it just made, a sorter the
        /// shape it routes, a turret the last round it fired.
        ///
        /// Every behaviour that shows nothing says so by name; the fallback is a throw, because a new
        /// behaviour silently badge-less is the bug this file's design argues against.
        /// </summary>
        public static Shape Badge(in MachineState m, in MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Cutter:
                    // The pending piece while an outlet drains, else the shape mid-split.
                    if (m.OutputCount > 0) return m.OutputShape;
                    return m.Carried;

                case BehaviorKind.Stacker:
                    // The union while an outlet drains, else the piece already held.
                    if (m.OutputCount > 0) return m.OutputShape;
                    return m.Carried;

                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                case BehaviorKind.Rotator:
                    return m.Carried;

                case BehaviorKind.Turret:
                    // The last round it fired: a universal chamber holds nothing, so the honest badge is
                    // what the line most recently fed it.
                    return m.Carried;

                case BehaviorKind.Sorter:
                    // The filter, not what it happens to be holding: the badge is the machine's
                    // configuration - which shape leaves by the side it faces - and a badge that
                    // flickered between two shapes would read as a flickering setting. Resolved through the
                    // filter's own mode here rather than patched by the view, so the rule is said once: a
                    // mineral rule has no exact item to draw and draws the full mineral it asks for, and an
                    // any-shape rule draws nothing, which is its own honest badge.
                    switch (m.Filter.Mode)
                    {
                        case FilterMode.Exact: return m.Filter.Exact;
                        case FilterMode.Mineral: return Shape.Full(m.Filter.Mineral);
                        default: return Shape.None;
                    }

                case BehaviorKind.None:
                case BehaviorKind.Drill:
                    return Shape.None;

                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must declare here what badge it wears");
            }
        }

        /// <summary>The cutter's second badge: the other piece of the split in flight. Empty for every
        /// behaviour that emits one thing at a time.</summary>
        public static Shape Badge2(in MachineState m, in MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Cutter: return m.OutputCount2 > 0 ? m.OutputShape2 : Shape.None;
                case BehaviorKind.None:
                case BehaviorKind.Drill:
                case BehaviorKind.Rotator:
                case BehaviorKind.Stacker:
                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter:
                case BehaviorKind.Turret:
                    return Shape.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must declare here whether it wears a second badge");
            }
        }

        /// <summary>The work fraction the view animates: the split progressing, the fuse, the turn, the
        /// item crossing the pipe. A pipe whose landing belt is blocked sits at 1 - parked at the far end.
        /// Behaviours with nothing to animate name themselves; the fallback is a throw.</summary>
        public static float Work(in MachineState m, in MachineDef def)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Cutter:
                    return m.Carried.IsEmpty ? 0f : Fraction(m.Cooldown, def.Interval);

                case BehaviorKind.Rotator:
                    return m.Carried.IsEmpty ? 0f : Fraction(m.Cooldown, def.Interval);

                case BehaviorKind.Stacker:
                    return m.Carried.IsEmpty || m.Carried2.IsEmpty ? 0f : Fraction(m.Cooldown, def.Interval);

                case BehaviorKind.Pipe:
                    return m.Carried.IsEmpty ? 0f : Fraction(m.Cooldown, def.Interval);

                case BehaviorKind.None:
                case BehaviorKind.Drill:
                case BehaviorKind.Splitter:
                case BehaviorKind.Sorter:
                case BehaviorKind.Turret:
                    return 0f;

                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must declare here whether it animates a work fraction");
            }
        }

        // ---------------------------------------------------------------- shared spelling

        public static string DirName(Dir direction)
        {
            switch (direction)
            {
                case Dir.North: return "north";
                case Dir.South: return "south";
                case Dir.West: return "west";
                case Dir.East: return "east";
                default:
                    throw new ArgumentOutOfRangeException(nameof(direction), direction,
                        "a new direction needs the word the card writes");
            }
        }

        private static string Dirs(DirMask mask)
        {
            var text = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                var direction = (Dir)i;
                if (!mask.Has(direction)) continue;

                if (text.Length > 0) text.Append(", ");
                text.Append(DirName(direction));
            }

            return text.Length == 0 ? "none" : text.ToString();
        }

        /// <summary>0 while a cycle has just begun, 1 as it completes - which is also the readout a parked
        /// machine shows, since a pipe whose landing belt is blocked sits at 1. A machine with no interval
        /// shows nothing rather than dividing by zero.</summary>
        private static float Fraction(float cooldown, float interval)
            => interval > 0f ? 1f - cooldown / interval : 0f;

        /// <summary>The share of full damage a round does to an enemy it does not match, as the catalogue
        /// says it: read from the balance constant so a retune moves this sentence too.</summary>
        private static string ResistShare()
            => (Balance.ResistantDamageMultiplier * 100f).ToString("0.#") + "%";

        /// <summary>What a cutter is holding back: the pending pieces, said in one phrase.</summary>
        private static string Held(in MachineState state)
        {
            if (state.OutputCount > 0 && state.OutputCount2 > 0)
                return state.OutputShape.Describe() + " and " + state.OutputShape2.Describe();

            return (state.OutputCount > 0 ? state.OutputShape : state.OutputShape2).Describe();
        }

        private static int Percent(float fraction)
            => (int)MathF.Round(fraction * 100f, MidpointRounding.AwayFromZero);
    }
}
