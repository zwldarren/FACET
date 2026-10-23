using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Facet.Core
{
    /// <summary>
    /// The progress and the rules of one map's tutorial: which line is current, what has been ticked, which
    /// buildings the run offers, and when the wave clock is let go.
    ///
    /// It is a module of the tick like the wave clock is - <see cref="Step"/> runs once per tick, after the
    /// machines and the Core sink have had their say, so it reads the tick that just finished rather than
    /// yesterday's answer. Every question it asks is a fact the fields already carry (a machine standing, the
    /// economy's banked total, an armed chamber, a half on a belt); it invents no state of its own, which is
    /// why a task cannot be satisfied by a click that built nothing.
    ///
    /// <b>Progress only ever goes forward.</b> A ticked line stays ticked even if the player dismantles what
    /// satisfied it: a wave that has already been released cannot be un-released, and a list that un-ticks
    /// under the player's hands is a list that contradicts itself. <see cref="Reset"/> is the one way back,
    /// because a restart is a new run.
    ///
    /// <b>It gates two things and nothing else.</b> The buildings <see cref="Unlocks"/> admits are the ones
    /// <see cref="BuildController"/> will place, and <see cref="GatesWaves"/> is what holds the wave clock -
    /// so a level that teaches a machine hands it over exactly when the lesson says, and a wave arrives when
    /// the player is ready for it rather than when a countdown says so.
    /// </summary>
    public sealed class TutorialState
    {
        private readonly TutorialScript _script;
        private readonly MachineField _machines;
        private readonly BeltField _belts;
        private readonly EconomyState _economy;

        /// <summary>The clock the run's waves come from. A line that
        /// <see cref="TutorialTask.StartsWave"/> releases its held wait - the tutorial says when a wave
        /// arrives, which is the whole point of gating it.</summary>
        private readonly WaveDirector _waves;

        /// <summary>The buildings the run offers right now: the script's starting set plus every ticked
        /// line's <see cref="TutorialTask.Opens"/>. Rebuilt on the tick a line is ticked and read every frame
        /// by the HUD, so the rebuild happens once and the read stays a walk of a handful of entries.</summary>
        private readonly List<BuildKind> _open = new List<BuildKind>(16);

        /// <summary>Scratch for <see cref="HasHalf"/>, kept across ticks because that question is asked on
        /// every tick of one line and the answer is a walk of a list it would otherwise allocate.</summary>
        private readonly List<MachineSnapshot> _machineScratch = new List<MachineSnapshot>(64);
        private readonly List<ItemSnapshot> _itemScratch = new List<ItemSnapshot>(256);

        private int _index;

        /// <summary>Has a caller set the lesson aside for this world? A world is told this before its first
        /// tick when something other than the player's map pick owns the entry - a scripted scene, a test,
        /// a future lessonless replay door. Sticky across <see cref="Reset"/>: the skip is a call about this
        /// map, and a restart re-opens the same opening rather than reversing it.</summary>
        private bool _skipped;

        public TutorialState(TutorialScript script, MachineField machines, BeltField belts,
            EconomyState economy, WaveDirector waves)
        {
            _script = script;
            _machines = machines ?? throw new ArgumentNullException(nameof(machines));
            _belts = belts ?? throw new ArgumentNullException(nameof(belts));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _waves = waves ?? throw new ArgumentNullException(nameof(waves));

            RebuildOpen();
        }

        /// <summary>Does this run have a lesson at all? False for every map that brings no script and for a
        /// world a caller has <see cref="Skip">skipped</see> - and the answer that makes the whole tutorial
        /// free: no script, no gate, no bar filtering, no extra line in the HUD.</summary>
        public bool Active => _script != null && _script.Count > 0 && !_skipped;

        /// <summary>How many lines the lesson has.</summary>
        public int Count => _script != null ? _script.Count : 0;

        /// <summary>How many lines have been ticked - also the position of the line being taught, which is
        /// <see cref="Count"/> once the lesson is over.</summary>
        public int StepsCompleted => _index;

        /// <summary>Every line ticked. The bar stops filtering and the wave clock stops holding.</summary>
        public bool Complete => !Active || _index >= _script.Count;

        /// <summary>The line being taught, or an empty one once the lesson is over.</summary>
        public TutorialTask Current => _script != null ? _script[_index] : TutorialScript.Empty;

        /// <summary>The line at a position - what the HUD walks to print the ticked ones above the
        /// current.</summary>
        public TutorialTask TaskAt(int index) => _script != null ? _script[index] : TutorialScript.Empty;

        /// <summary>Does this lesson hold the wave clock back? True while there is a lesson left to teach, so
        /// a level's waves arrive because a line was ticked rather than because a countdown ran out - and
        /// false the moment the last line is ticked, which is what lets the level's remaining waves play
        /// normally.</summary>
        public bool GatesWaves => Active && !Complete;

        /// <summary>One tick of the lesson: read the world for the current line, and tick it if it is really
        /// done.
        ///
        /// Only the current line is read. A player who happens to satisfy a later one early is not credited
        /// for it, which is the point of teaching in order: the line that says "cut a half" is the one that
        /// explains what a half is, and a tick that arrived before the sentence was read is a tick nobody
        /// learned from.</summary>
        public void Step()
        {
            if (Complete) return;

            TutorialTask task = _script[_index];
            if (!IsDone(task.Step)) return;

            _index++;
            RebuildOpen();

            // The line is ticked before the wave is released, so the bar it just widened and the wave it
            // just called agree on what the player has. A finished lesson holds nothing back, whatever its last
            // line said: a script whose final line was not the one that starts a wave would otherwise leave the
            // run waiting forever for a clock nobody is left to arm. The clock banks a release granted while a
            // wave is still being fought (<see cref="WaveDirector.ReleaseWait"/>), so a cue ticked mid-wave
            // calls the wave that follows it rather than being swallowed.
            if (task.StartsWave || Complete) _waves.ReleaseWait();
        }

        /// <summary>Would this building be placeable under the lesson? True for every kind on a map with no
        /// script and for every kind once the last line is ticked - so this is only ever a restriction on a
        /// lesson in progress.
        ///
        /// It is asked by <see cref="BuildController.CanPlace"/> and by everything that reads
        /// <see cref="SimWorld.IsUnlocked"/>, which is what makes hiding a building from the bar and refusing
        /// its hotkey one decision instead of two that can drift.</summary>
        public bool Unlocks(BuildKind kind)
        {
            if (!Active || Complete) return true;

            for (int i = 0; i < _open.Count; i++)
                if (_open[i] == kind) return true;

            return false;
        }

        /// <summary>Set the lesson aside: nothing is taught, nothing is held, and the bar is the map's own
        /// from the first frame. A caller's verb for a world it does not want taught - a world is told this
        /// once, before its first tick, and <see cref="Reset"/> does not hear it. The driver does not call
        /// it: every map entry, a replay of a cleared one included, teaches that map's lesson.</summary>
        public void Skip()
        {
            _skipped = true;
        }

        /// <summary>Wipe the lesson back to its first line - a restart is a new run, and a lesson that
        /// survived one would leave the player with a full bar and a fresh wave clock, which is the
        /// half-restarted state a restart exists to avoid.</summary>
        public void Reset()
        {
            _index = 0;
            RebuildOpen();
        }

        private void RebuildOpen()
        {
            _open.Clear();
            if (_script == null) return;

            var starting = _script.Starting;
            for (int i = 0; i < starting.Length; i++) _open.Add(starting[i]);

            // Only the ticked lines have paid out: the list is the run's inventory of tools, and a line not
            // yet taught has handed nothing over.
            for (int i = 0; i < _index && i < _script.Count; i++)
            {
                var opens = _script[i].Opens;
                for (int k = 0; k < opens.Length; k++) _open.Add(opens[k]);
            }
        }

        /// <summary>Is one line's rule satisfied by the world as it stands?</summary>
        private bool IsDone(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.PlaceDrill:
                    return _machines.CountOf(BuildKind.Drill) > 0;

                case TutorialStep.BankShape:
                    return _economy.TotalBanked > 0;

                case TutorialStep.ArmTurret:
                    _machines.CountTurrets(out int total, out int armed);
                    return total > 0 && armed > 0;

                case TutorialStep.MakeHalf:
                    return HasHalf();

                case TutorialStep.ArmTurretWithHalf:
                    return LoadedTurrets() > 0;

                case TutorialStep.TwoTurretsLoaded:
                    return LoadedTurrets() >= 2;

                case TutorialStep.TwoTurretsLoadedAcross:
                    return AcrossHalfTurrets() >= 2;

                case TutorialStep.BeltFork:
                    return HasWiredFork();

                default:
                    // A line carrying <see cref="TutorialScript.NoStep"/> is broken data, reported by the map
                    // validator where a designer can act on it: it never ticks, and saying so every tick would
                    // bury the log it appears in. Any other value is a step this file has no rule for - a new
                    // member added without one - which is a code fault worth hearing on its first tick.
                    Debug.Assert(step == TutorialScript.NoStep,
                        "TutorialStep '" + step +
                        "' has no completion rule - the lesson can never pass this line");
                    return false;
            }
        }

        /// <summary>Is there a half anywhere: on a belt, or held in a machine's chamber, buffer or
        /// output.
        ///
        /// Every place an item can be, because which one a cutter's halves land in is not the player's
        /// decision to describe - one piece leaves by the front onto a belt, the other by a lane over, and a
        /// turret or a pipe may take either before the next tick. Asking only the belts would call a cut that
        /// was immediately swallowed "not done", which reads as a broken tutorial rather than as a fast
        /// line.</summary>
        private bool HasHalf()
        {
            _machines.GetMachines(_machineScratch);
            for (int i = 0; i < _machineScratch.Count; i++)
            {
                MachineSnapshot machine = _machineScratch[i];

                // A sorter's badge is its filter setting, not an item held (<see cref="MachineReading.Badge"/>),
                // so a two-quadrant filter would read as a half that was never cut - a menu click ticking a
                // lesson about the cutter's output. Its pieces are on the belts below like anyone else's.
                if (machine.Behavior == BehaviorKind.Sorter) continue;

                if (machine.Shape.IsHalf || machine.Shape2.IsHalf) return true;
            }

            _belts.GetItems(_itemScratch);
            for (int i = 0; i < _itemScratch.Count; i++)
                if (_itemScratch[i].Shape.IsHalf) return true;

            return false;
        }

        /// <summary>How many turrets hold a half - the count behind both the single-gun line
        /// (<see cref="TutorialStep.ArmTurretWithHalf"/>) and its pair (<see cref="TutorialStep.TwoTurretsLoaded"/>).
        /// A cutter forks into two lanes, so "one" and "both" are the two answers a battery lesson can ask
        /// for, and they read the same walk.</summary>
        private int LoadedTurrets()
        {
            _machines.GetMachines(_machineScratch);
            int loaded = 0;
            for (int i = 0; i < _machineScratch.Count; i++)
            {
                MachineSnapshot machine = _machineScratch[i];
                if (machine.Behavior == BehaviorKind.Turret && machine.Shape.IsHalf) loaded++;
            }

            return loaded;
        }

        /// <summary>How many turrets hold a half cut across the lane - a top or a bottom half. The other
        /// two halves (left/right) are the ones a cutter flowing north or south makes; a cutter turned a
        /// quarter to flow east or west makes these, which is why the line about turning a cutter is judged
        /// by the orientation in the chamber rather than by the count of machines on the map.</summary>
        private int AcrossHalfTurrets()
        {
            _machines.GetMachines(_machineScratch);
            int loaded = 0;
            for (int i = 0; i < _machineScratch.Count; i++)
            {
                MachineSnapshot machine = _machineScratch[i];
                if (machine.Behavior != BehaviorKind.Turret) continue;
                if (!machine.Shape.IsHalf) continue;

                // Top and bottom share an edge across the vertical axis (NW+NE, SW+SE); left and right
                // share one across the horizontal axis and are the cut a north/south flow already made.
                bool across = (machine.Shape.Has(Shape.NW) && machine.Shape.Has(Shape.NE))
                              || (machine.Shape.Has(Shape.SW) && machine.Shape.Has(Shape.SE));
                if (across) loaded++;
            }

            return loaded;
        }

        /// <summary>
        /// Is a cutter standing across a lane with a belt at the front of each of its tiles - the fork's two
        /// outlets wired?
        ///
        /// This is the lesson's one line about a machine's <em>shape</em> rather than its output, and it is a
        /// line of its own because the alternative is the first cut a player ever makes stalling: a cutter
        /// queues four pieces per lane and then stops dead, and a lane nobody drains is exactly the mis-wiring
        /// the HUD counts as stopped. The question is the machine's own
        /// (<see cref="CutterBehavior.MissingLane"/>), put to the player before the machine has to say it -
        /// and it is asked with the machine's own outlet rule (<see cref="MachineDelivery.IsOutput"/>), so the
        /// line that ticks and the badge that reads STOPPED cannot disagree about whether a lane is wired.
        /// </summary>
        private bool HasWiredFork()
        {
            _machines.GetMachines(_machineScratch);
            for (int i = 0; i < _machineScratch.Count; i++)
            {
                MachineSnapshot fork = _machineScratch[i];
                if (fork.Behavior != BehaviorKind.Cutter) continue;

                Dir flow = fork.Direction;
                if (!MachineFootprint.TrySecond(fork.Behavior, fork.Cell, flow, out Int2 lane)) continue;

                if (!MachineDelivery.IsOutput(_belts, fork.Cell, flow)) continue;
                if (!MachineDelivery.IsOutput(_belts, lane, flow)) continue;

                return true;
            }

            return false;
        }
    }
}
