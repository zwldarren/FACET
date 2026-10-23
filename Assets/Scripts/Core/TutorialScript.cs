using System;

namespace Facet.Core
{
    /// <summary>What one tutorial step counts as done. A closed set for the same reason
    /// <see cref="BehaviorKind"/> and <see cref="SimEventKind"/> are: each member is a rule
    /// <see cref="TutorialState"/> knows how to read off the world, and a new lesson is a new member rather
    /// than a plugin nobody needs yet.
    ///
    /// Each member is deliberately a <b>fact about the run</b> and not an action performed: "a drill stands"
    /// rather than "the player pressed 2". A step that watched input could be satisfied by a click that
    /// built nothing, and a tutorial that ticks on the wrong thing is worse than no tutorial at all.</summary>
    public enum TutorialStep : byte
    {
        /// <summary>A drill stands on a vein somewhere. Any drill, on any patch.</summary>
        PlaceDrill = 0,

        /// <summary>A complete shape has really reached the Core. Judged by the bank
        /// (<see cref="EconomyState.TotalBanked"/>) rather than by the belt: a run of belts that is laid but
        /// aimed away from the Core delivers nothing, and must not tick.</summary>
        BankShape = 1,

        /// <summary>A turret stands with a round in its chamber - <see cref="MachineState.Armed"/>, which is
        /// the "not starved" state the HUD already prints. The step the first wave is released on.</summary>
        ArmTurret = 2,

        /// <summary>A half exists anywhere: two quadrants filled that share an edge (<see cref="Shape.IsHalf"/>),
        /// on a belt or in a machine. What a cutter makes, and what the second wave is weak to.</summary>
        MakeHalf = 3,

        /// <summary>A cutter stands across a lane with a belt at the front of <em>both</em> its tiles - the
        /// fork's two outlets wired, which is the one thing about a cutter's <em>shape</em> a player has to
        /// understand before its output can mean anything.</summary>
        BeltFork = 4,

        /// <summary>A turret holds a half in its chamber: the cut has been <em>aimed</em>, not just made.
        /// Making a half (<see cref="MakeHalf"/>) and feeding one to the gun are different facts, and the
        /// second is the one a wave weak to halves is survived by - a lesson that ends at the cut leaves the
        /// player to infer the connection under fire. Judged by the chamber rather than by a belt pointing at
        /// the turret, for the same reason <see cref="BankShape"/> reads the bank: a line aimed at the gun
        /// that never arrives is not a defence.</summary>
        ArmTurretWithHalf = 5,

        /// <summary>Two turrets stand, each holding a half: a full shape cut in two, one piece per gun.
        /// A cutter forks into two lanes, and a lesson that taught a single armed gun would let the player
        /// wire one lane and leave the fork's other half on a dead end - so the pair is its own fact. It
        /// implies <see cref="ArmTurretWithHalf"/> and is the line a two-lane battery is released on.</summary>
        TwoTurretsLoaded = 6,

        /// <summary>Two turrets stand, each holding a half cut <em>across</em> the lane - a top or a bottom
        /// half. <see cref="TwoTurretsLoaded"/> counts any halves; this one counts the orientation, which is
        /// the fact the line about turning a cutter is really about: a cutter flowing north/south makes
        /// left/right halves, and one turned a quarter to flow east/west makes top/bottom. The lesson is
        /// placed by turning the cutter, so the proof is the cut that reached the gun and not a machine
        /// standing somewhere on the map.</summary>
        TwoTurretsLoadedAcross = 7,
    }

    /// <summary>One piece of a line's blueprint: where a building or belt goes, which way it faces, and what
    /// it is. The guide draws these as transparent ghosts on the map, so the lesson can show a finished
    /// layout - several tiles, two-tile machines included - rather than one arrow pointing at a cell.
    ///
    /// It is data for the same reason the line is: the standard answer is the map's, not the HUD's. A ghost
    /// is a <em>picture</em> and never a placement - nothing is built, and the player may put the real
    /// building anywhere the rules allow. Its only promise is the one the lesson makes: do it here and this
    /// is what the result looks like.</summary>
    public readonly struct TutorialGhost
    {
        /// <summary>The building or belt this ghost stands for.</summary>
        public readonly BuildKind Build;

        /// <summary>The tile it stands on. A two-tile machine's second tile is derived from
        /// <see cref="Build"/> and <see cref="Direction"/>, exactly as the placement rule derives it.</summary>
        public readonly Int2 Cell;

        /// <summary>The way the ghost faces. A belt's arrow and a machine's body read it, so the blueprint
        /// says "this way" as well as "here".</summary>
        public readonly Dir Direction;

        public TutorialGhost(BuildKind build, Int2 cell, Dir direction)
        {
            Build = build;
            Cell = cell;
            Direction = direction;
        }
    }

    /// <summary>One line of a map's tutorial: what to do, where to look, what it hands over, and what to say
    /// if the player cannot see the way.
    ///
    /// It is data, not code, for the same reason a wave is: a tutorial is the map's lesson, and a map that
    /// teaches something else should not need a new build. <see cref="Text"/> is written in the map asset and
    /// printed by the HUD verbatim, so the lesson reads the way its designer wrote it.
    ///
    /// <see cref="Target"/> is the tile the guide points at - one cell, chosen by the designer, not
    /// computed from the player's pointer. A tutorial's job is to narrow the map to one answer; an arrow that
    /// picked the nearest legal cell would jump around under a moving mouse. <see cref="Ghost"/> is the richer
    /// half of the same idea: the standard layout drawn on the map, so a line about a cutter or a belt run can
    /// show its whole shape instead of asking the player to infer it from one tile.</summary>
    public readonly struct TutorialTask
    {
        /// <summary>The rule that ticks this line.</summary>
        public readonly TutorialStep Step;

        /// <summary>The line as the HUD prints it.</summary>
        public readonly string Text;

        /// <summary>Where the guide points, or <see langword="null"/> for a line with no one right place to
        /// be. It is the primary cell - the one the edge marker folds toward when the layout is off screen -
        /// and the first ghost is normally the same tile.</summary>
        public readonly Int2? Target;

        /// <summary>The standard answer, drawn as transparent buildings on the map. Empty for a line with no
        /// one right layout: a lesson about a fact ("bank a shape") can say everything in its sentence.</summary>
        public readonly TutorialGhost[] Ghost;

        /// <summary>The buildings this line hands over when it is ticked. Emptied by
        /// <see cref="TutorialScript"/>'s last line, whose reward is everything
        /// (<see cref="TutorialState.Complete"/>).</summary>
        public readonly BuildKind[] Opens;

        /// <summary>The second sentence, for a player the line has not got through: what the rule actually
        /// asks for, said the long way round. Shown by the HUD only after the line has been current for a
        /// while (<c>HudView.HintDelay</c>), so a step the player is already working through is never talked
        /// over - and empty for a line that needs no help.
        ///
        /// It is here rather than in the HUD because it is the same kind of sentence <see cref="Text"/> is:
        /// the map's own words about the map's own machine.</summary>
        public readonly string Hint;

        /// <summary>Does ticking this line let the next held wave go? Two of a level's lines are the wave
        /// clock's cue - one per wave it teaches - and the rest are not; a flag rather than an ordinal,
        /// because which lesson gates which wave is the designer's arrangement and not a rule about
        /// tutorials.</summary>
        public readonly bool StartsWave;

        public TutorialTask(TutorialStep step, string text, Int2? target, BuildKind[] opens,
            bool startsWave, string hint = null, TutorialGhost[] ghost = null)
        {
            Step = step;
            Text = text ?? string.Empty;
            Target = target;
            Ghost = ghost ?? new TutorialGhost[0];
            Opens = opens ?? new BuildKind[0];
            StartsWave = startsWave;
            Hint = hint ?? string.Empty;
        }
    }

    /// <summary>
    /// A map's tutorial: the buildings the run opens with, then the lines it teaches in order.
    ///
    /// Everything a tutorial changes about a run hangs off this one object, and a map without one is a map
    /// without a tutorial - <see langword="null"/> rather than an empty script, so "this level teaches
    /// nothing" is one comparison and every reader of it can skip its work entirely.
    ///
    /// <b>What a script changes.</b> Three things, all in <see cref="TutorialState"/>: which buildings the
    /// bottom bar offers (and therefore which hotkeys work), when the wave clock is let go, and which line
    /// the HUD prints. Nothing else about the run moves - the map, its waves and its economy are the map's
    /// own. That is what makes a tutorial a lesson laid over a level rather than a different kind of level.
    /// </summary>
    public sealed class TutorialScript
    {
        /// <summary>The step no rule answers: what a line with no rule is given, and what a reader past the end
        /// of a script receives. <see cref="TutorialState"/>'s rule switch refuses it, so such a line never
        /// ticks - which is the honest state for a row that is broken data rather than a lesson: the run stops
        /// on that line and says so, instead of a blank line deciding the lesson is over.</summary>
        public const TutorialStep NoStep = (TutorialStep)byte.MaxValue;

        /// <summary>An empty line, normalized like every other one: empty text rather than null, an empty
        /// blueprint rather than a null one, and a step no rule answers rather than
        /// <see cref="TutorialStep.PlaceDrill"/>, which would tick the moment any drill stands. Readers
        /// walking a script they have been told the length of get this instead of a default struct, so
        /// <see cref="TutorialTask"/>'s invariants hold for every task handed out.</summary>
        public static readonly TutorialTask Empty =
            new TutorialTask(NoStep, string.Empty, null, null, false, string.Empty);

        /// <summary>The buildings a run opens with, before any line is ticked. Everything else waits for a
        /// line that opens it - a bar that starts with one tile is the lesson's first sentence.</summary>
        public readonly BuildKind[] Starting;

        /// <summary>The lines, in the order they are taught.</summary>
        public readonly TutorialTask[] Tasks;

        public TutorialScript(BuildKind[] starting, TutorialTask[] tasks)
        {
            Starting = starting ?? new BuildKind[0];
            Tasks = tasks ?? new TutorialTask[0];
        }

        /// <summary>How many lines the script has.</summary>
        public int Count => Tasks.Length;

        /// <summary>The line at a position, or an empty one past the end - a reader walking the list it has
        /// already been told the length of should not have to guard twice.</summary>
        public TutorialTask this[int index]
            => index >= 0 && index < Tasks.Length ? Tasks[index] : Empty;

        /// <summary>
        /// The one building a line's rule is about: the machine that has to be in the player's hands before the
        /// line can be done at all. This is the authoring-time half of <see cref="TutorialState"/>'s rule - the
        /// map validator asks it to refuse a lesson that can never pass a line - so a new member has to name its
        /// building here or this throws rather than leaving a map that stalls silently.
        ///
        /// It is deliberately the machine and not its supply chain: <see cref="TutorialStep.BankShape"/> is
        /// about the bank, and a validator that also demanded belts would cry wolf on a map whose drill faces
        /// the Core.
        /// </summary>
        public static BuildKind RequiredBuilding(TutorialStep step)
            => TryRequiredBuilding(step, out BuildKind building)
                ? building
                : throw new ArgumentOutOfRangeException(nameof(step), step,
                    "a tutorial step must name the building it needs, or a map can never be validated");

        /// <summary>
        /// The same question asked by a reader that may be holding a value the enum no longer has - a row
        /// serialized before a step was renamed or removed, or a hand-edited asset. False is that reader's
        /// answer: the line names a lesson nothing can teach, which is a problem to report
        /// (<see cref="MapProblemKind.TutorialStepUnknown"/>) rather than an exception to raise in the
        /// painting window.
        /// </summary>
        public static bool TryRequiredBuilding(TutorialStep step, out BuildKind building)
        {
            switch (step)
            {
                case TutorialStep.PlaceDrill:
                case TutorialStep.BankShape:
                    building = BuildKind.Drill;
                    return true;

                case TutorialStep.ArmTurret:
                case TutorialStep.ArmTurretWithHalf:
                case TutorialStep.TwoTurretsLoaded:
                case TutorialStep.TwoTurretsLoadedAcross:
                    building = BuildKind.Turret;
                    return true;

                case TutorialStep.MakeHalf:
                case TutorialStep.BeltFork:
                    building = BuildKind.Cutter;
                    return true;

                default:
                    building = default;
                    return false;
            }
        }
    }
}
