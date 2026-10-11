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
    /// The one statement of what a machine means to the player: the catalogue's lines for the build
    /// bar and the inspect card, the live sentence for what a building is doing right now, its badge
    /// shape and its animated work fraction - all dispatched on the machine's <em>behaviour</em>, so a
    /// new row in the content table is described, badged and animated without any view learning it.
    ///
    /// This module exists because the same three questions ("what is it", "what is it doing", "what
    /// does its facing mean") used to be answered in parallel by the HUD's narration, the snapshot's
    /// badge dispatch and the renderer's draw dispatch - and only the latter two threw on an unknown
    /// behaviour, while the HUD quietly called everything "TRANSPORT". Every dispatch here enumerates
    /// every <see cref="BehaviorKind"/> by name and throws on anything else, the discipline
    /// <see cref="MachineField"/>'s badges already hold themselves to. The tests are the same seam:
    /// what the HUD would print is asserted through this interface, not through a MonoBehaviour.
    ///
    /// Strings and severity only - no colours, no fonts, no layout. The view formats and tints.
    /// </summary>
    public static class MachineReading
    {
        // ---------------------------------------------------------------- landings

        /// <summary>The cell a drill pushes its mined shape onto: the tile it faces. One statement -
        /// the tick mines through it and the note describes it, so neither can drift.</summary>
        public static Int2 DrillLanding(Int2 cell, Dir direction) => cell + direction.Offset();

        /// <summary>The cell a pipe drops its carried shape onto: two tiles along its facing,
        /// jumping exactly one. One statement - the tick delivers through it and the note describes
        /// it, so the picture cannot drift from the simulation, which is the deal the snapshot
        /// masks make elsewhere in this assembly.</summary>
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
                case BehaviorKind.Converter: return MachineRole.Processing;
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
        /// and a behaviour-shaped sentence where it does not - so a new turret or converter gets a
        /// sensible line without any view being edited.</summary>
        public static string Blurb(in MachineDef def, ContentDatabase content)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(def.Build);
                    return "eats " + Glyph(content, spec.Ammo) + " from any side · fires at whatever is in range";
                }

                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = content.Recipe(def.RecipeId);
                    return "splits " + Glyph(content, recipe.Input) + " into " +
                           Repeat(Glyph(content, recipe.Output), recipe.OutputCount) +
                           " · outlets read off the belts pointing away";
                }

                case BehaviorKind.Sorter:
                    return def.Description + " (" + Glyph(content, def.Filter) + ")";

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
                        ? (1f / def.Interval).ToString("0.##") + " " + Glyph(content, ShapeType.Circle) + "/s mined"
                        : string.Empty;

                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = content.Recipe(def.RecipeId);
                    return recipe.Interval.ToString("0.##") + " s per split · holds " + recipe.Buffer + " when blocked";
                }

                case BehaviorKind.Pipe:
                    return def.Interval > 0f
                        ? "crosses 2 tiles · " + def.Interval.ToString("0.##") + " s per item"
                        : string.Empty;

                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(def.Build);
                    return spec.ShotsPerSecond.ToString("0.##") + " shots/s · " + spec.Damage.ToString("0.##") +
                           " dmg · range " + spec.Range.ToString("0.##") + " tiles";
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

        /// <summary>
        /// What this building is doing right now, dispatched on its behaviour. Every sentence is
        /// about a state the player can act on, and the geometry a sentence names - a drill's
        /// landing, a pipe's - comes from the same functions the tick delivers through, so the card
        /// cannot describe a machine the simulation does not have.
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
                    return world.TileGrid.Get(onto).Eats()
                        ? new ReadingNote("mining " + Glyph(content, world.Terrain.OreAt(cell)) + " onto " + onto,
                            NoteSeverity.Neutral)
                        : new ReadingNote("nothing to push onto at " + onto + " — lay a belt facing it",
                            NoteSeverity.Warn);
                }

                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = content.Recipe(def.RecipeId);
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet — lay a belt pointing away from it", NoteSeverity.Warn);

                    if (machine.Work > 0f)
                        return new ReadingNote("splitting " + Glyph(content, recipe.Input) + " · " +
                            Percent(machine.Work) + "% through", NoteSeverity.Neutral);

                    return machine.Shape == recipe.Output
                        ? new ReadingNote("holding " + Glyph(content, recipe.Output) + " — every outlet is full",
                            NoteSeverity.Warn)
                        : new ReadingNote("idle — waiting for " + Glyph(content, recipe.Input),
                            NoteSeverity.Neutral);
                }

                case BehaviorKind.Pipe:
                {
                    Int2 lands = PipeLanding(cell, machine.Direction);
                    return machine.Shape == ShapeType.None
                        ? new ReadingNote("empty — nothing delivered", NoteSeverity.Neutral)
                        : new ReadingNote("carrying " + Glyph(content, machine.Shape) + " to " + lands + " · " +
                            Percent(machine.Work) + "% across", NoteSeverity.Neutral);
                }

                case BehaviorKind.Splitter:
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet — lay a belt pointing away from it", NoteSeverity.Warn);

                    return machine.Shape == ShapeType.None
                        ? new ReadingNote("dealing whatever arrives round robin", NoteSeverity.Neutral)
                        : new ReadingNote("holding " + Glyph(content, machine.Shape) + " — every outlet is full",
                            NoteSeverity.Warn);

                case BehaviorKind.Sorter:
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet — lay a belt pointing away from it", NoteSeverity.Warn);

                    return new ReadingNote(Glyph(content, def.Filter) + " leaves by the side it faces · the rest take " +
                        "the other outlets", NoteSeverity.Neutral);

                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(machine.Build);
                    if (!machine.Armed)
                        return new ReadingNote("starved — no " + Glyph(content, spec.Ammo) + " waiting on its lines",
                            NoteSeverity.Warn);

                    return new ReadingNote(machine.HasTarget ? "armed · a target in range" : "armed · nothing in range",
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

        /// <summary>What a built machine's facing means to it: a sorter's filtered shape leaves by it,
        /// a turret's barrel rests on it, a drill pushes onto it and a pipe carries across in it. The
        /// hubs that read their ports off the belts around them have no facing of their own to name,
        /// and say so - the same rule the ghost draws its arrow by, <see cref="MachineDef.UsesFacing"/>.</summary>
        public static string FacingNote(in MachineDef def, in MachineSnapshot machine, ContentDatabase content)
        {
            if (def.Behavior == BehaviorKind.None && def.Tile != TileKind.Belt)
                return "no facing — a wall only stands in the way";
            if (!def.UsesFacing) return "ports read off the belts around it";

            switch (machine.Behavior)
            {
                case BehaviorKind.Turret: return "barrel rests " + DirName(machine.Direction);
                case BehaviorKind.Sorter:
                    return Glyph(content, def.Filter) + " leaves " + DirName(machine.Direction);

                case BehaviorKind.None:      // a belt: its facing is where it carries
                case BehaviorKind.Drill:
                case BehaviorKind.Pipe:
                case BehaviorKind.Converter:
                case BehaviorKind.Splitter:
                    return "facing " + DirName(machine.Direction);

                default:
                    throw new ArgumentOutOfRangeException(nameof(machine), machine.Behavior,
                        "a new behaviour must say what its card calls its facing");
            }
        }

        /// <summary>The sides a machine's belts wire it to: a belt pointing in is an input and a belt
        /// pointing away is an output, both masks straight off <see cref="MachineSnapshot"/>.</summary>
        public static string Ports(in MachineSnapshot machine)
            => machine.InMask.IsEmpty && machine.OutMask.IsEmpty
                ? "no belts wired — a belt pointing in feeds it, one pointing away empties it"
                : "in " + Dirs(machine.InMask) + "  ·  out " + Dirs(machine.OutMask);

        // ---------------------------------------------------------------- the badge and the spin

        /// <summary>
        /// What the building draws on itself. Keyed on the machine's <em>behaviour</em>, not on its
        /// tile kind: a converter shows what its recipe is making, a turret its diet, a sorter the
        /// shape it routes. The two converters therefore show the two different minerals without
        /// this method naming either of them - a cutter's badge is simply the second recipe's output.
        ///
        /// Every behaviour that shows nothing says so by name; the fallback is a throw, because a
        /// new behaviour silently badge-less is the bug this file's whole design argues against.
        /// </summary>
        public static ShapeType Badge(in MachineState m, in MachineDef def, ContentDatabase content)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = RecipeOf(def, content);
                    if (recipe == null) return ShapeType.None;
                    if (m.OutputCount > 0) return recipe.Output;
                    return m.Carried == recipe.Input ? recipe.Input : ShapeType.None;
                }
                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                    return m.Carried;
                case BehaviorKind.Turret:
                    return content.Turret(m.Build).Ammo;
                case BehaviorKind.Sorter:
                    // The filter, not what it happens to be holding: the badge is the machine's
                    // configuration - which shape leaves by the side it faces - and a badge that
                    // flickered between two shapes would read as a flickering setting.
                    return def.Filter;
                case BehaviorKind.None:
                case BehaviorKind.Drill:
                    return ShapeType.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(def), def.Behavior,
                        "a new behaviour must declare here what badge it wears");
            }
        }

        /// <summary>The work fraction the view animates: the split progressing, the item crossing
        /// the pipe. A pipe whose landing belt is blocked sits at 1 - parked at the far end.
        /// Behaviours with nothing to animate name themselves; the fallback is a throw.</summary>
        public static float Work(in MachineState m, in MachineDef def, ContentDatabase content)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Converter:
                {
                    RecipeDef recipe = RecipeOf(def, content);
                    if (recipe == null || recipe.Interval <= 0f) return 0f;
                    return m.Carried == recipe.Input ? 1f - m.Cooldown / recipe.Interval : 0f;
                }
                case BehaviorKind.Pipe:
                {
                    float interval = def.Interval;
                    return m.Carried != ShapeType.None && interval > 0f
                        ? 1f - m.Cooldown / interval
                        : 0f;
                }
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

        private static string Glyph(ContentDatabase content, ShapeType shape) => content.Shape(shape).Glyph;

        private static string Repeat(string text, int count)
        {
            var sb = new StringBuilder(text.Length * Math.Max(count, 1));
            for (int i = 0; i < count; i++) sb.Append(text);
            return sb.ToString();
        }

        private static int Percent(float fraction)
            => (int)MathF.Round(fraction * 100f, MidpointRounding.AwayFromZero);

        private static RecipeDef RecipeOf(in MachineDef def, ContentDatabase content)
        {
            ContentId id = def.RecipeId;
            return id.IsNone ? null : content.Recipe(id);
        }
    }
}
