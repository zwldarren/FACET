using System;

namespace Facet.Core
{
    /// <summary>
    /// One row of a map's research schedule: once wave <see cref="AfterWave"/> is cleared, the buildings in
    /// <see cref="Builds"/> appear on the bar, locked, and the player buys them all at once for
    /// <see cref="Cost"/> shapes out of the Core. A cost of 0 hands them over the moment they are offered.
    ///
    /// An offer is announced by its <see cref="Lesson"/>, a few coached lines the player follows by doing them,
    /// or - for a tool no wave tests - by the one line of its <see cref="Hint"/>.
    ///
    /// <see cref="AfterWave"/> 0 is offered before the first wave - a way to put a priced building on the bar
    /// from the opening.
    /// </summary>
    public sealed class ResearchOffer
    {
        public readonly int AfterWave;
        public readonly BuildKind[] Builds;
        public readonly int Cost;
        public readonly string Hint;
        public readonly TutorialScript Lesson;

        public ResearchOffer(int afterWave, BuildKind[] builds, int cost, string hint = null,
            TutorialScript lesson = null)
        {
            AfterWave = Math.Max(0, afterWave);
            Builds = builds ?? new BuildKind[0];
            Cost = Math.Max(0, cost);
            Hint = hint ?? string.Empty;
            Lesson = lesson;
        }

        public bool Contains(BuildKind kind)
        {
            for (int i = 0; i < Builds.Length; i++)
                if (Builds[i] == kind) return true;
            return false;
        }
    }

    /// <summary>
    /// What a map's run researches, and when. A building any offer names is <b>scheduled</b>: locked until its
    /// offer is bought. A building no offer names is not this schedule's business and stays as the map and the
    /// lesson leave it - so a map with an empty schedule plays exactly as before the schedule existed.
    /// </summary>
    public sealed class ResearchSchedule
    {
        public static readonly ResearchSchedule None = new ResearchSchedule(new ResearchOffer[0]);

        public readonly ResearchOffer[] Offers;

        public ResearchSchedule(ResearchOffer[] offers)
        {
            Offers = offers ?? new ResearchOffer[0];
        }

        public bool IsEmpty => Offers.Length == 0;

        /// <summary>The first offer that names this building, or -1. A building named twice is bought by its
        /// first row - the later one is dead data.</summary>
        public int IndexOf(BuildKind kind)
        {
            for (int i = 0; i < Offers.Length; i++)
                if (Offers[i].Contains(kind)) return i;
            return -1;
        }
    }

    /// <summary>
    /// A run's research: which offers have reached the bar, which have been bought, and which offer's lesson is
    /// being taught. Engine-free and driven by the world's own clock - <see cref="Step"/> is told how many waves
    /// are cleared, and a purchase is a request that lands on a tick boundary like every other input.
    ///
    /// Offers are made in schedule order and never withdrawn, so a reader that wants each one exactly once keeps a
    /// count and reads <see cref="OfferedCount"/> past it - no event stream to fall behind on.
    ///
    /// A lesson runs from its offer until its last line is done, one at a time in the order offered. A wave
    /// starting ends every lesson then on offer: the only way one starts during a lesson is N, the player saying
    /// they know this already, and the research itself stays on the bar.
    /// </summary>
    public sealed class ResearchState
    {
        private readonly ResearchSchedule _schedule;
        private readonly EconomyState _economy;
        private readonly bool[] _offered;
        private readonly bool[] _researched;

        /// <summary>Offers made so far, in the order they were made. Indexes into the schedule.</summary>
        private readonly int[] _offerOrder;

        /// <summary>Each offer's lesson state, or null for an offer without one.</summary>
        private readonly TutorialState[] _lessons;

        /// <summary>Has this offer's lesson finished, or been stepped past?</summary>
        private readonly bool[] _lessonOver;

        public ResearchState(ResearchSchedule schedule, EconomyState economy,
            Func<TutorialScript, ResearchState, TutorialState> lessonFactory = null)
        {
            _schedule = schedule ?? ResearchSchedule.None;
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));

            int count = _schedule.Offers.Length;
            _offered = new bool[count];
            _researched = new bool[count];
            _offerOrder = new int[count];
            _lessons = new TutorialState[count];
            _lessonOver = new bool[count];

            if (lessonFactory != null)
                for (int i = 0; i < count; i++)
                    if (_schedule.Offers[i].Lesson != null)
                        _lessons[i] = lessonFactory(_schedule.Offers[i].Lesson, this);

            Reset();
        }

        public ResearchSchedule Schedule => _schedule;

        /// <summary>Does this run research anything at all?</summary>
        public bool Active => !_schedule.IsEmpty;

        /// <summary>How many offers have reached the bar this run.</summary>
        public int OfferedCount { get; private set; }

        /// <summary>The <paramref name="n"/>th offer made this run (0-based, in the order made).</summary>
        public ResearchOffer OfferedAt(int n) => _schedule.Offers[_offerOrder[n]];

        /// <summary>The lesson being taught right now, or null. The coach reads it.</summary>
        public TutorialState Lesson { get; private set; }

        /// <summary>Is a lesson being taught? The wave countdown waits while it is.</summary>
        public bool Teaching => Lesson != null;

        /// <summary>Is this building placeable as far as research is concerned? True for anything the schedule
        /// does not name.</summary>
        public bool Allows(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            return i < 0 || _researched[i];
        }

        /// <summary>Is this building on the bar but not yet bought - the locked key with a price on it?</summary>
        public bool Researchable(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            return i >= 0 && _offered[i] && !_researched[i];
        }

        /// <summary>Has this building's offer been bought?</summary>
        public bool Researched(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            return i >= 0 && _researched[i];
        }

        /// <summary>What buying this building's offer costs, or 0 for a building the schedule does not name.</summary>
        public int CostOf(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            return i < 0 ? 0 : _schedule.Offers[i].Cost;
        }

        /// <summary>The offer that names this building, or null.</summary>
        public ResearchOffer OfferFor(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            return i < 0 ? null : _schedule.Offers[i];
        }

        public bool CanAfford(BuildKind kind) => Researchable(kind) && _economy.Shapes >= CostOf(kind);

        /// <summary>The popup's button, for the lesson being taught.</summary>
        public void Acknowledge() => Lesson?.Acknowledge();

        /// <summary>What the player is holding, for the lesson being taught.</summary>
        public void NoteSelection(BuildKind? selected) => Lesson?.NoteSelection(selected);

        /// <summary>Make every offer whose wave has been cleared, then advance the lesson. A free offer is bought
        /// as it is made. A running wave ends the lessons on offer.</summary>
        public void Step(int wavesCleared, bool waveRunning = false)
        {
            MakeOffers(wavesCleared);

            // A wave running, or one already behind it - a lesson is about the wave that follows its offer.
            for (int n = 0; n < OfferedCount; n++)
            {
                int i = _offerOrder[n];
                if (waveRunning || wavesCleared > _schedule.Offers[i].AfterWave) _lessonOver[i] = true;
            }

            AdvanceLesson();
        }

        /// <summary>Buy the offer that names this building. False when it is not on offer, already bought, or the
        /// stockpile cannot cover it.</summary>
        public bool TryResearch(BuildKind kind)
        {
            int i = _schedule.IndexOf(kind);
            if (i < 0 || !_offered[i] || _researched[i]) return false;
            if (!_economy.TrySpend(_schedule.Offers[i].Cost)) return false;

            _researched[i] = true;
            return true;
        }

        public void Reset()
        {
            Array.Clear(_offered, 0, _offered.Length);
            Array.Clear(_researched, 0, _researched.Length);
            Array.Clear(_lessonOver, 0, _lessonOver.Length);
            for (int i = 0; i < _lessons.Length; i++) _lessons[i]?.Reset();
            OfferedCount = 0;
            Lesson = null;

            // Offers due before any wave is cleared stand from the first frame, not from the first tick.
            MakeOffers(0);
            PickLesson();
        }

        private void MakeOffers(int wavesCleared)
        {
            for (int i = 0; i < _schedule.Offers.Length; i++)
            {
                if (_offered[i] || _schedule.Offers[i].AfterWave > wavesCleared) continue;

                _offered[i] = true;
                _offerOrder[OfferedCount++] = i;
                if (_schedule.Offers[i].Cost == 0) _researched[i] = true;
            }
        }

        /// <summary>Tick the current lesson's line, and move to the next lesson when it is done. One line a tick:
        /// the player reads each.</summary>
        private void AdvanceLesson()
        {
            PickLesson();
            if (Lesson == null) return;

            Lesson.Step();
            if (Lesson.Complete) PickLesson();
        }

        /// <summary>The first offered lesson still to teach. Marks finished ones over as it passes them.</summary>
        private void PickLesson()
        {
            Lesson = null;
            for (int n = 0; n < OfferedCount; n++)
            {
                int i = _offerOrder[n];
                if (_lessonOver[i] || _lessons[i] == null) continue;

                if (_lessons[i].Complete)
                {
                    _lessonOver[i] = true;
                    continue;
                }

                Lesson = _lessons[i];
                return;
            }
        }
    }
}
