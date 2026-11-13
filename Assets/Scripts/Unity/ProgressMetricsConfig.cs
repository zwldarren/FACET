namespace Facet.Game
{
    /// <summary>Metrics 3 and 4 only. Existing AnalyticsConfig and its forms are unchanged.
    /// Form URLs and field IDs come from the supplied prefilled links.</summary>
    public static class ProgressMetricsConfig
    {
        // TEMPORARY: verify both forms receive rows, then set this back to false.
        public const bool SendFromEditor = false;
        // Change when the tested tutorial/map changes; compare matching builds and tasks only.
        public const string BuildLabel = "beta";

        public const string TaskFormUrl = "https://docs.google.com/forms/d/e/1FAIpQLSeys14N87ec9yW94gI-4iyx6Z1jA01R0r4fM3RpAVTNoMsSnA/formResponse";
        // session, run, map, task_index, task_kind, task_label, seconds, outcome, build
        public static readonly string[] TaskEntries = {
            "entry.1296407257", // session
            "entry.544944071", // run
            "entry.2140847159", // map
            "entry.1297738832", // task_index
            "entry.300216993", // task_kind
            "entry.182410792", // task_label
            "entry.1373117393", // seconds
            "entry.840253951", // outcome
            "entry.1072084016" // build
        };

        public const string CoreFormUrl = "https://docs.google.com/forms/d/e/1FAIpQLSdgnlHbE5qbphMjlx_eKgsDiubtHaSX72yMdDXPI4xnAChZSQ/formResponse";
        // session, run, map, wave, start_hp, end_hp, lost_hp, max_hp, lost_percent_of_max,
        // outcome, complete_coverage, build
        public static readonly string[] CoreEntries = {
            "entry.297741892", // session
            "entry.1240903656", // run
            "entry.2048074757", // map
            "entry.1142664439", // wave
            "entry.1749535084", // start_hp
            "entry.1955296456", // end_hp
            "entry.1962841332", // lost_hp
            "entry.1866086815", // max_hp
            "entry.375638461", // lost_percent_of_max
            "entry.597513247", // outcome
            "entry.278803397", // complete_coverage
            "entry.1748241780" // build
        };
    }
}
