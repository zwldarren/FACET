using System;
using System.Collections.Generic;
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
                case BehaviorKind.Converter:
                case BehaviorKind.Assembler: return MachineRole.Processing;
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
                    string eats = "eats " + AmmoDiet(content, spec) + " from any side";
                    return spec.SplashRadius > 0f
                        ? eats + " · shells burst over an area"
                        : eats + " · fires at whatever is in range";
                }

                case BehaviorKind.Assembler:
                {
                    // "fuses half-circle + half-square into composite", per two-input recipe it runs.
                    var text = new StringBuilder("fuses ");
                    bool first = true;
                    foreach (RecipeDef recipe in RecipesFor(content, def))
                    {
                        if (!first) text.Append(", ");
                        first = false;
                        text.Append(Glyph(content, recipe.Input)).Append(" + ")
                            .Append(Glyph(content, recipe.Input2)).Append(" into ")
                            .Append(Glyph(content, recipe.Output));
                    }

                    return text + " · outlets read off the belts pointing away";
                }

                case BehaviorKind.Converter:
                {
                    // "splits circle into 2 x half-circle", or every recipe the table holds for a
                    // machine that names none: "splits circle into 2 x half-circle, square into ...".
                    var text = new StringBuilder("splits ");
                    bool first = true;
                    foreach (RecipeDef recipe in RecipesFor(content, def))
                    {
                        if (!first) text.Append(", ");
                        first = false;
                        text.Append(Glyph(content, recipe.Input)).Append(" into ")
                            .Append(Repeat(Glyph(content, recipe.Output), recipe.OutputCount));
                    }

                    return text + " · outlets read off the belts pointing away";
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
                    // The numbers the recipes share, said once; a recipe retuned away from the others
                    // surfaces as both values, slash-joined, rather than passing as uniform.
                    string intervals = Distinct(content, in def, r => r.Interval.ToString("0.##"));
                    string buffers = Distinct(content, in def, r => r.Buffer.ToString());
                    return intervals + " s per split · holds " + buffers + " when blocked";
                }

                case BehaviorKind.Assembler:
                {
                    string fuseIntervals = Distinct(content, in def, r => r.Interval.ToString("0.##"));
                    string fuseBuffers = Distinct(content, in def, r => r.Buffer.ToString());
                    return fuseIntervals + " s per fuse · holds " + fuseBuffers + " when blocked";
                }

                case BehaviorKind.Pipe:
                    return def.Interval > 0f
                        ? "crosses 2 tiles · " + def.Interval.ToString("0.##") + " s per item"
                        : string.Empty;

                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(def.Build);
                    DamageRange(spec, out float min, out float max);
                    string damage = min >= max
                        ? min.ToString("0.##") + " dmg"
                        : min.ToString("0.##") + "-" + max.ToString("0.##") + " dmg";
                    string line = spec.ShotsPerSecond.ToString("0.##") + " shots/s · " + damage +
                           " · range " + spec.Range.ToString("0.##") + " tiles";
                    return spec.SplashRadius > 0f
                        ? line + " · bursts over " + spec.SplashRadius.ToString("0.##") + " tiles"
                        : line;
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
                    return world.TileGrid.Get(onto).Eats()
                        ? new ReadingNote("mining " + Glyph(content, world.Terrain.OreAt(cell)) + " onto " + onto,
                            NoteSeverity.Neutral)
                        : new ReadingNote("nothing to push onto at " + onto + " - lay a belt facing it",
                            NoteSeverity.Warn);
                }

                case BehaviorKind.Converter:
                {
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet - lay a belt pointing away from it", NoteSeverity.Warn);

                    // The live sentence names the shapes themselves, which the snapshot does not carry -
                    // read the state itself rather than the badge.
                    world.Machines.TryGet(cell, out MachineState state);

                    if (state.Carried != ShapeType.None)
                        return new ReadingNote("splitting " + Glyph(content, state.Carried) + " · " +
                            Percent(machine.Work) + "% through", NoteSeverity.Neutral);

                    if (state.OutputCount > 0)
                        return new ReadingNote("holding " + Glyph(content, state.OutputShape) +
                            " - every outlet is full", NoteSeverity.Warn);

                    return new ReadingNote("idle - waiting for " + Diet(content, in def), NoteSeverity.Neutral);
                }

                case BehaviorKind.Assembler:
                {
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet - lay a belt pointing away from it", NoteSeverity.Warn);

                    // As the converter's: the sentence names shapes the snapshot does not carry.
                    world.Machines.TryGet(cell, out MachineState state);

                    if (state.Carried != ShapeType.None && state.Carried2 != ShapeType.None)
                        return new ReadingNote("fusing " + Glyph(content, state.Carried) + " + " +
                            Glyph(content, state.Carried2) + " · " + Percent(machine.Work) + "% through",
                            NoteSeverity.Neutral);

                    if (state.OutputCount > 0)
                        return new ReadingNote("holding " + Glyph(content, state.OutputShape) +
                            " - every outlet is full", NoteSeverity.Warn);

                    if (state.Carried != ShapeType.None)
                    {
                        // The recipe the machine actually committed to, not a fresh table-order pick:
                        // with two fuses sharing an input those disagree, and the sentence would name a
                        // shape the machine will never take.
                        RecipeDef committed = state.CommittedRecipe.IsNone
                            ? content.RecipeForInput(in def, state.Carried)
                            : content.RecipeOrNull(state.CommittedRecipe);
                        ShapeType missing = committed == null ? ShapeType.None
                            : committed.Input == state.Carried ? committed.Input2 : committed.Input;
                        return new ReadingNote("holding " + Glyph(content, state.Carried) +
                            " - waiting for " + Glyph(content, missing), NoteSeverity.Neutral);
                    }

                    return new ReadingNote("idle - waiting for " + Diet(content, in def), NoteSeverity.Neutral);
                }

                case BehaviorKind.Pipe:
                {
                    Int2 lands = PipeLanding(cell, machine.Direction);
                    return machine.Shape == ShapeType.None
                        ? new ReadingNote("empty - nothing delivered", NoteSeverity.Neutral)
                        : new ReadingNote("carrying " + Glyph(content, machine.Shape) + " to " + lands + " · " +
                            Percent(machine.Work) + "% across", NoteSeverity.Neutral);
                }

                case BehaviorKind.Splitter:
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet - lay a belt pointing away from it", NoteSeverity.Warn);

                    return machine.Shape == ShapeType.None
                        ? new ReadingNote("dealing whatever arrives round robin", NoteSeverity.Neutral)
                        : new ReadingNote("holding " + Glyph(content, machine.Shape) + " - every outlet is full",
                            NoteSeverity.Warn);

                case BehaviorKind.Sorter:
                    if (machine.OutMask.IsEmpty)
                        return new ReadingNote("no outlet - lay a belt pointing away from it", NoteSeverity.Warn);

                    return new ReadingNote(Glyph(content, def.Filter) + " leaves by the side it faces · the rest take " +
                        "the other outlets", NoteSeverity.Neutral);

                case BehaviorKind.Turret:
                {
                    TurretDef spec = content.Turret(machine.Build);
                    if (!machine.Armed)
                        return new ReadingNote("starved - no " + AmmoDiet(content, spec) + " waiting on its lines",
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
                return "no facing - a wall only stands in the way";
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
                case BehaviorKind.Assembler:
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
                ? "no belts wired - a belt pointing in feeds it, one pointing away empties it"
                : "in " + Dirs(machine.InMask) + "  ·  out " + Dirs(machine.OutMask);

        // ---------------------------------------------------------------- the badge and the spin

        /// <summary>
        /// What the building draws on itself. Keyed on the machine's <em>behaviour</em>, not its tile
        /// kind: a converter shows what its recipe makes, a turret its diet, a sorter the shape it routes.
        ///
        /// Every behaviour that shows nothing says so by name; the fallback is a throw, because a new
        /// behaviour silently badge-less is the bug this file's design argues against.
        /// </summary>
        public static ShapeType Badge(in MachineState m, in MachineDef def, ContentDatabase content)
        {
            switch (def.Behavior)
            {
                case BehaviorKind.Converter:
                    // The pending batch while it drains, else the shape mid-split: what the machine is
                    // holding, whichever recipe it came from.
                    if (m.OutputCount > 0) return m.OutputShape;
                    return m.Carried;
                case BehaviorKind.Pipe:
                case BehaviorKind.Splitter:
                    return m.Carried;
                case BehaviorKind.Assembler:
                    // The pending fused batch while it drains, else the first half already held.
                    if (m.OutputCount > 0) return m.OutputShape;
                    return m.Carried;
                case BehaviorKind.Turret:
                    return content.Turret(m.Build).PrimaryAmmo;
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
                    RecipeDef running = content.RecipeForInput(in def, m.Carried);
                    return running != null && running.Interval > 0f
                        ? 1f - m.Cooldown / running.Interval
                        : 0f;
                }
                case BehaviorKind.Assembler:
                {
                    // The committed recipe is what the cycle is running, so the fraction is its own clock.
                    // The pair lookup is the fallback for a state that carries no commitment.
                    RecipeDef running = m.Carried2 == ShapeType.None
                        ? null
                        : m.CommittedRecipe.IsNone
                            ? content.RecipeForInputs(in def, m.Carried, m.Carried2)
                            : content.RecipeOrNull(m.CommittedRecipe);
                    return running != null && running.Interval > 0f
                        ? 1f - m.Cooldown / running.Interval
                        : 0f;
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

        /// <summary>A recipe's yield counted out in a sentence: the shape is spelled as a word, so a count
        /// above one is said as "2 x ...".</summary>
        private static string Repeat(string text, int count)
            => count <= 1 ? text : count + " x " + text;

        private static int Percent(float fraction)
            => (int)MathF.Round(fraction * 100f, MidpointRounding.AwayFromZero);

        /// <summary>The recipes a machine runs: its one named recipe, or - naming none - every recipe in
        /// the table with its behaviour's input count, so a converter's lines never quote the fuse and an
        /// assembler's never quote a split.</summary>
        private static IEnumerable<RecipeDef> RecipesFor(ContentDatabase content, MachineDef def)
        {
            if (!def.RecipeId.IsNone)
            {
                yield return content.Recipe(def.RecipeId);
                yield break;
            }

            bool twoInput = def.Behavior == BehaviorKind.Assembler;
            foreach (RecipeDef recipe in content.Recipes)
                if (recipe != null && recipe.IsTwoInput == twoInput) yield return recipe;
        }

        /// <summary>What a converter or assembler eats, spelled for a sentence: every input of every
        /// recipe it runs, duplicates dropped, joined with " or ".</summary>
        private static string Diet(ContentDatabase content, in MachineDef def)
        {
            var text = new StringBuilder();
            foreach (RecipeDef recipe in RecipesFor(content, def))
            {
                AppendDietShape(text, Glyph(content, recipe.Input));
                if (recipe.Input2 != ShapeType.None)
                    AppendDietShape(text, Glyph(content, recipe.Input2));
            }

            return text.ToString();
        }

        private static void AppendDietShape(StringBuilder text, string glyph)
        {
            if (("; " + text + "; ").Contains("; " + glyph + "; ")) return;
            if (text.Length > 0) text.Append(" or ");
            text.Append(glyph);
        }

        /// <summary>What a turret eats, spelled for a sentence: its diet's shapes joined with " or ".</summary>
        private static string AmmoDiet(ContentDatabase content, TurretDef spec)
        {
            var text = new StringBuilder();
            foreach (AmmoDef ammo in spec.AmmoTypes)
            {
                if (ammo == null) continue;
                if (text.Length > 0) text.Append(" or ");
                text.Append(Glyph(content, ammo.Shape));
            }

            return text.ToString();
        }

        /// <summary>The damage floor and ceiling across a turret's diet, for its stats line.</summary>
        private static void DamageRange(TurretDef spec, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            foreach (AmmoDef ammo in spec.AmmoTypes)
            {
                if (ammo == null) continue;
                if (ammo.Damage < min) min = ammo.Damage;
                if (ammo.Damage > max) max = ammo.Damage;
            }
        }

        /// <summary>One value read off every recipe a machine runs, duplicates dropped, joined with
        /// " / " - so two recipes at the shipped 1.5 s read "1.5", and a retune of one reads "1.5 / 2".</summary>
        private static string Distinct(ContentDatabase content, in MachineDef def,
            Func<RecipeDef, string> field)
        {
            var seen = new StringBuilder();
            foreach (RecipeDef recipe in RecipesFor(content, def))
            {
                string value = field(recipe);
                if ((", " + seen + ", ").Contains(", " + value + ", ")) continue;
                if (seen.Length > 0) seen.Append(" / ");
                seen.Append(value);
            }

            return seen.ToString();
        }
    }
}
