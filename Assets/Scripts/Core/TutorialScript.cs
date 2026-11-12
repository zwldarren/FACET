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

        // ---- the coached lesson's steps (a map with TutorialScript.Coached): one idea per popup ----

        /// <summary>The player has read the popup and pressed its button (or Enter). The one step that is an
        /// act rather than a fact about the run: an explanation has nothing in the world to check, so the
        /// request goes through <see cref="SimWorld.RequestTutorialNext"/> and lands on a tick boundary like
        /// every other request.</summary>
        Acknowledge = 8,

        /// <summary>The drill is in the player's hand - selected on the bar or by its hotkey - or one already
        /// stands, so a player who raced ahead is not held on a line they have passed.</summary>
        SelectDrill = 9,

        /// <summary>The belt is in the player's hand, or a shape has already been banked.</summary>
        SelectBelt = 10,

        /// <summary>The turret is in the player's hand, or one already stands.</summary>
        SelectTurret = 11,

        /// <summary>A turret stands, loaded or not.</summary>
        PlaceTurret = 12,

        /// <summary>The player asked for the wave: N, or the HUD's wave button, or the popup's own button.
        /// A coached lesson holds every wave, and this line is the one that lets it go
        /// (<see cref="TutorialTask.StartsWave"/>).</summary>
        StartWave = 13,

        /// <summary>The map's waves are all cleared. The lesson's last line: it keeps the popup saying what to
        /// do while the wave is being fought.</summary>
        ClearWave = 14,

        // Appended: existing map assets keep their serialized step numbers.
        /// <summary>The cutter is selected, or the lesson's cutter already stands.</summary>
        SelectCutter = 15,

        /// <summary>A cutter stands at the task target, facing the cutter ghost's direction.</summary>
        PlaceCutter = 16,

        /// <summary>The target cutter has received a real item or already produced its halves.</summary>
        FeedCutter = 17,

        /// <summary>An outgoing belt is wired to the target cutter's primary output.</summary>
        WireCutterPrimary = 18,

        /// <summary>Both the primary and displaced output belts are wired on that same cutter.</summary>
        WireCutterSecondary = 19,

        /// <summary>Turrets stand on both turret ghosts; chamber contents are checked in the next step.</summary>
        PlaceCutterTurrets = 20,

        /// <summary>The rotator is selected, or the lesson's rotator already stands.</summary>
        SelectRotator = 21,

        /// <summary>A rotator stands at the task target, facing the rotator ghost's direction.</summary>
        PlaceRotator = 22,

        /// <summary>The turret at the task target holds a round the coming wave is weak to. Read from the
        /// chamber and the wave's own briefing, so a gun holding any other shape cannot pass the line.</summary>
        LoadWaveShape = 23,

        // Appended after the cutter and rotator lessons: the first map's second wave, and the room it opens.
        /// <summary>The wave named by <see cref="TutorialTask.Wave"/> has been cleared. The line that keeps the
        /// popup up while a wave is fought on a level with more than one wave: <see cref="ClearWave"/> can only
        /// say "every wave", which is the run's end, and a lesson about what the next wave needs has to tick in
        /// the pause after the one before it.</summary>
        ClearedWave = 24,

        /// <summary>A turret stands at the line's target. <see cref="PlaceTurret"/> counts any turret, so on a
        /// map that has already taught its first gun it would tick before the player read a lesson about a
        /// second one. This is the target-specific half of the same rule, like <see cref="PlaceCutter"/>.</summary>
        PlaceTurretAtTarget = 25,

        /// <summary>The turret at the line's target holds a round: the second gun is fed, not just built. The
        /// pair with <see cref="PlaceTurretAtTarget"/> is what <see cref="ArmTurret"/> is for a map with one gun
        /// to teach - the fact is the same chamber either way.</summary>
        ArmTurretAtTarget = 26,

        // Appended after the target-specific pair: a later turret a lesson only needs to exist, not to stand
        // on one authored tile. A tutorial's job is to teach a decision, and where the second and third gun go
        // is the player's - a fixed tile there turns a sandbox into a click-the-glowing-square drill.
        /// <summary>At least <see cref="TutorialTask.Turrets"/> turrets stand anywhere on the map. For a lesson
        /// that has already taught one gun and now wants a second (or third) line without prescribing the
        /// tile: the target-specific pair stays for lessons where the layout itself is the thing taught.</summary>
        TurretCount = 27,

        /// <summary>At least <see cref="TutorialTask.Turrets"/> turrets are armed anywhere on the map. The
        /// companion to <see cref="TurretCount"/>, and the fact a wave is survived by: a gun with nothing in
        /// its chamber is a wall.</summary>
        ArmedTurretCount = 28,

        // Appended after the first map's wave and turret-count steps: the research lessons' rules (Frontier).
        /// <summary>The line's building (the first of <see cref="TutorialTask.Opens"/>) has been researched. The
        /// steps below name their building that way because a research lesson has no blueprint: the player's
        /// factory is their own, so these are facts about the run rather than about a tile.</summary>
        ResearchBuilding = 29,

        /// <summary>The line's building is in the player's hand, or one already stands.</summary>
        SelectBuilding = 30,

        /// <summary>The line's building stands anywhere on the map.</summary>
        PlaceBuilding = 31,

        /// <summary>Two turrets stand with a round in the chamber, whatever the round is.</summary>
        TwoTurretsArmed = 32,

        /// <summary>Any turret holds a round the coming wave is weak to.</summary>
        AnyTurretHoldsWaveShape = 33,
    }

    /// <summary>What a coached line draws a box around, so the player knows where to look. Places on the map
    /// (the Core, the gate, the line's target) are boxed in the world; the rest are pieces of the HUD, which
    /// the HUD itself measures (<c>HudView.TryFocusRect</c>), so a box can never drift from what it points at.
    /// A line may name several.</summary>
    public enum TutorialFocus : byte
    {
        None = 0,

        /// <summary>The Core's whole footprint.</summary>
        Core = 1,

        /// <summary>The map's first wave entrance.</summary>
        Gate = 2,

        /// <summary>The line's <see cref="TutorialTask.Target"/> tile, grown to the whole ore patch it sits in
        /// when it is ore - "this patch", not "this one tile".</summary>
        Target = 3,

        /// <summary>Every tile of the line's blueprint (<see cref="TutorialTask.Ghost"/>).</summary>
        Ghost = 4,

        /// <summary>The bar's key for the building this line is about.</summary>
        BarTile = 5,

        /// <summary>The coin readout beside the status card.</summary>
        Stockpile = 6,

        /// <summary>The status card's Core health row.</summary>
        CoreHealth = 7,

        /// <summary>The status card's wave clock and its sentence.</summary>
        WaveClock = 8,

        /// <summary>The status card's briefing: how many enemies, and the shape each group is weak to.</summary>
        WaveChips = 9,

        /// <summary>The wave button in the controls cluster.</summary>
        WaveButton = 10,

        /// <summary>The whole status card.</summary>
        StatusCard = 11,

        /// <summary>The whole build bar: where the buildings the lesson hands over appear.</summary>
        BuildBar = 12,

        /// <summary>Not a box: an animated demo of the line's building (the first of its
        /// <see cref="TutorialTask.Opens"/>) inside the popup - shapes going in and coming out, so the player
        /// sees what the machine does instead of being walked through building one.</summary>
        Demo = 13,
    }

    /// <summary>The pieces of the HUD a coached lesson shows, as flags: a coached map opens with none of them
    /// and each line reveals what it is about to talk about, so the screen never holds a panel the player has
    /// not been introduced to. A map without a coached lesson shows everything, always.</summary>
    [Flags]
    public enum TutorialHud : byte
    {
        None = 0,

        /// <summary>The status card: map, Core health, wave clock and briefing.</summary>
        Status = 1,

        /// <summary>The coin readout.</summary>
        Stockpile = 2,

        /// <summary>The build bar.</summary>
        Bar = 4,

        /// <summary>The controls cluster: wave, pause, speed, help, menu.</summary>
        Controls = 8,

        /// <summary>The info card over the bar.</summary>
        Info = 16,

        All = Status | Stockpile | Bar | Controls | Info,
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

        /// <summary>The coached popup's heading - "Welcome", "Success!" - or empty for none. Only a coached
        /// lesson prints it.</summary>
        public readonly string Title;

        /// <summary>What the coached popup boxes while this line is current. Empty boxes nothing.</summary>
        public readonly TutorialFocus[] Focus;

        /// <summary>The HUD pieces this line brings on screen. A coached lesson's HUD is the union of every line
        /// up to and including the current one (<see cref="TutorialState.Revealed"/>), so a piece once shown
        /// stays shown.</summary>
        public readonly TutorialHud Reveal;

        /// <summary>Ore this line lays down when it ticks, the way <see cref="Opens"/> hands over buildings:
        /// a tutorial level shows one vein at a time, so the player is never choosing between patches before
        /// the lesson needs a second one. Every tile is one-by-one. A restart re-stamps the map without
        /// them.</summary>
        public readonly ShapePatch[] AddsOre;

        /// <summary>Ground this line opens when it ticks: a room the map was authored without, laid down in
        /// the authored order so a floor can be followed by the rock of its walls. The same patches are stamped
        /// into the validator's copy (<see cref="MapDefinition.StampTerrain"/>), so a door inside a room that
        /// opens mid-run is judged against the ground the run ends with. Empty for every line that only
        /// teaches.</summary>
        public readonly TerrainPatch[] AddsGround;

        /// <summary>For <see cref="TutorialStep.ClearedWave"/>: the 1-based wave that must be cleared, 0 for
        /// every other step. A number rather than a step per wave, because which wave a line waits for is the
        /// designer's arrangement and not a rule about lessons.</summary>
        public readonly int Wave;

        /// <summary>For <see cref="TutorialStep.TurretCount"/> and <see cref="TutorialStep.ArmedTurretCount"/>:
        /// how many turrets the line asks for - the second gun, the western gun, and any count a later lesson
        /// needs. 0 for every other step. A number rather than a step per turret, for the same reason
        /// <see cref="Wave"/> is: which one of a battery a lesson is about is the designer's arrangement.</summary>
        public readonly int Turrets;

        public TutorialTask(TutorialStep step, string text, Int2? target, BuildKind[] opens,
            bool startsWave, string hint = null, TutorialGhost[] ghost = null, string title = null,
            TutorialFocus[] focus = null, TutorialHud reveal = TutorialHud.None, ShapePatch[] addsOre = null,
            TerrainPatch[] addsGround = null, int wave = 0, int turrets = 0)
        {
            Step = step;
            Text = text ?? string.Empty;
            Target = target;
            Ghost = ghost ?? new TutorialGhost[0];
            Opens = opens ?? new BuildKind[0];
            StartsWave = startsWave;
            Hint = hint ?? string.Empty;
            Title = title ?? string.Empty;
            Focus = focus ?? new TutorialFocus[0];
            Reveal = reveal;
            AddsOre = addsOre ?? new ShapePatch[0];
            AddsGround = addsGround ?? new TerrainPatch[0];
            Wave = wave;
            Turrets = turrets;
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
    /// the HUD prints. It may also lay what a line's data says - a vein (<see cref="TutorialTask.AddsOre"/>) or
    /// a room the map was authored without (<see cref="TutorialTask.AddsGround"/>) - but those are the map's own
    /// data replayed, not a fourth gate: the waves and the economy stay the map's. That is what makes a tutorial
    /// a lesson laid over a level rather than a different kind of level.
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

        /// <summary>Is this lesson taught by the coach - one big popup per line, boxes around what it is about,
        /// and a HUD that starts empty and is revealed a piece at a time? A tutorial map is coached; Quarry's
        /// lesson laid over a real level is not, and keeps the task card in the corner.</summary>
        public readonly bool Coached;

        /// <summary>The line the run opens with already standing, so a lesson about one machine can start from
        /// a working factory instead of asking the player to rebuild what earlier lessons taught. Stamped by
        /// <see cref="SimWorld"/> at the start of every run, and held in place until the lesson is over
        /// (<see cref="TutorialState.IsPrebuilt"/>).</summary>
        public readonly TutorialGhost[] Prebuilt;

        /// <summary>Does the wave clock run while this lesson is up, as it does on an ordinary map? For a level
        /// that hands the player the whole job: the opening counts down from the first building, N calls the
        /// wave early, and <see cref="TutorialState.GatesWaves"/> stays false.</summary>
        public readonly bool ClockRuns;

        /// <summary>Does the coach spotlight each line: dim the screen around the boxed places and set the popup
        /// down beside them? Off, the popup keeps its one place in the left column and nothing is dimmed. Per
        /// map, so the first tutorial can try it without changing how the other lessons look.</summary>
        public readonly bool Spotlight;

        public TutorialScript(BuildKind[] starting, TutorialTask[] tasks, bool coached = false,
            TutorialGhost[] prebuilt = null, bool clockRuns = false, bool spotlight = false)
        {
            Starting = starting ?? new BuildKind[0];
            Tasks = tasks ?? new TutorialTask[0];
            Coached = coached;
            Prebuilt = prebuilt ?? new TutorialGhost[0];
            ClockRuns = clockRuns;
            Spotlight = spotlight;
        }

        /// <summary>Is this a step that is not about any building - an explanation to read, a wave to call or
        /// to survive? The validator skips its "is the building handed over" rule for these, since there is no
        /// building to hand over.</summary>
        public static bool NeedsNoBuilding(TutorialStep step)
            => step == TutorialStep.Acknowledge || step == TutorialStep.StartWave ||
               step == TutorialStep.ClearWave || step == TutorialStep.ClearedWave
               || step == TutorialStep.ResearchBuilding || step == TutorialStep.SelectBuilding
               || step == TutorialStep.PlaceBuilding;

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
                case TutorialStep.SelectDrill:
                    building = BuildKind.Drill;
                    return true;

                // A belt laid on ore mines it (<see cref="BeltMining"/>), so banking needs only belts.
                case TutorialStep.BankShape:
                case TutorialStep.SelectBelt:
                    building = BuildKind.Belt;
                    return true;

                case TutorialStep.SelectTurret:
                case TutorialStep.PlaceCutterTurrets:
                case TutorialStep.PlaceTurret:
                case TutorialStep.PlaceTurretAtTarget:
                case TutorialStep.ArmTurret:
                case TutorialStep.ArmTurretAtTarget:
                case TutorialStep.ArmTurretWithHalf:
                case TutorialStep.TwoTurretsLoaded:
                case TutorialStep.TwoTurretsLoadedAcross:
                case TutorialStep.TurretCount:
                case TutorialStep.ArmedTurretCount:
                case TutorialStep.TwoTurretsArmed:
                case TutorialStep.AnyTurretHoldsWaveShape:
                    building = BuildKind.Turret;
                    return true;

                case TutorialStep.SelectCutter:
                case TutorialStep.PlaceCutter:
                case TutorialStep.FeedCutter:
                case TutorialStep.WireCutterPrimary:
                case TutorialStep.WireCutterSecondary:
                case TutorialStep.MakeHalf:
                case TutorialStep.BeltFork:
                    building = BuildKind.Cutter;
                    return true;

                // The gun in LoadWaveShape stands from the start; the rotator is what the line waits on.
                case TutorialStep.SelectRotator:
                case TutorialStep.PlaceRotator:
                case TutorialStep.LoadWaveShape:
                    building = BuildKind.Rotator;
                    return true;

                default:
                    building = default;
                    return false;
            }
        }
    }
}
