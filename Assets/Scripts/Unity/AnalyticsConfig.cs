namespace Facet.Game
{
    /// <summary>
    /// Where the matched-round analytics rows go: a Google Form, one form submission per wave. The form's
    /// responses land in its linked Google Sheet, which <c>Tools/analytics/plot_match_rate.py</c> reads to
    /// draw the chart.
    ///
    /// To point this at a form: open the form, ⋮ ▸ "Get pre-filled link", type a dummy value into every
    /// question, "Get link", and copy it. The link looks like
    /// <c>https://docs.google.com/forms/d/e/FORM_ID/viewform?usp=pp_url&amp;entry.111=x&amp;entry.222=x...</c>:
    /// <see cref="FormResponseUrl"/> is the same address with <c>viewform...</c> replaced by
    /// <c>formResponse</c>, and each <c>entry.NNN</c> is one question's id, in the order the questions are
    /// listed below. Leave <see cref="FormResponseUrl"/> empty to switch sending off.
    /// </summary>
    public static class AnalyticsConfig
    {
        /// <summary>The form's submit address, ending in <c>/formResponse</c>. Empty switches sending off.</summary>
        public const string FormResponseUrl =
            "https://docs.google.com/forms/d/e/1FAIpQLSeR6ivqjqyUHrTSbRYQ-AE8o33wG_bCUcuMaElmerRJFodiEQ/formResponse";

        // One form question each - short-answer questions, titled exactly as below so the plot script
        // finds the columns: Session, Map, Wave, Matched, Resisted, Outcome, Build.

        /// <summary>The <c>Session</c> question: the id shared by every row of one page load.</summary>
        public const string EntrySession = "entry.1625959773";

        /// <summary>The <c>Map</c> question: the map's name, as the campaign names it.</summary>
        public const string EntryMap = "entry.1512937499";

        /// <summary>The <c>Wave</c> question: the wave's 1-based number on that map.</summary>
        public const string EntryWave = "entry.1565484819";

        /// <summary>The <c>Matched</c> question: hits whose round was the target's weakness.</summary>
        public const string EntryMatched = "entry.1668272830";

        /// <summary>The <c>Resisted</c> question: hits whose round was anything else.</summary>
        public const string EntryResisted = "entry.184452456";

        /// <summary>The <c>Outcome</c> question: cleared, won, lost or abandoned.</summary>
        public const string EntryOutcome = "entry.1831195779";

        /// <summary>The <c>Build</c> question: the <see cref="BuildLabel"/> the row came from.</summary>
        public const string EntryBuild = "entry.338387420";

        /// <summary>Which build a row came from, so one build's players can be compared with another's -
        /// change it whenever a build that changes how the game plays is published (alpha, alpha-2, beta).</summary>
        public const string BuildLabel = "alpha";

        /// <summary>Send from the Unity Editor too. Off, so play-testing in the Editor does not fill the
        /// sheet with developer runs: the Editor logs each row to the Console instead.</summary>
        public const bool SendFromEditor = false;

        /// <summary>Whether a form is named. The reporter's own switch, and the one a build that must
        /// collect nothing is pointed at: it returns before it builds a body, asks the browser or logs a
        /// line, so an unconfigured build is silent rather than noisy about failing.</summary>
        public static bool Configured => FormResponseUrl.Length > 0;
    }
}
