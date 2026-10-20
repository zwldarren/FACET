using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// The campaign's progress, as it survives a session: how many maps of the campaign have been
    /// cleared, written to and read from <c>PlayerPrefs</c>. One key, one class, and the only place in
    /// the project the progress touches a store - the driver seeds its <see cref="Facet.Core.CampaignState"/>
    /// from <see cref="Read"/> at boot and calls <see cref="Write"/> when the count grows, so no reader
    /// of progress can drift from the writer of it.
    ///
    /// This is the persistence decision issue §5 asked for, stated: a level list means something across
    /// a page reload, so the cleared count is written down. In a WebGL build PlayerPrefs persists through
    /// IndexedDB per browser profile, which is exactly the lifetime a prototype wants; a fresh profile
    /// reads 0 and the campaign opens with its first map alone - that is the rule, not an accident.
    ///
    /// Deliberately in the Game assembly, not Core: Core is engine-free, and PlayerPrefs is an engine
    /// answer. The count is the only progress there is - a run itself is never saved; a world is one run,
    /// and "continue" means the campaign's frontier map, freshly built (see SimulationDriver.PlayMap).
    /// </summary>
    public static class CampaignProgress
    {
        /// <summary>The one key the count lives under. Namespaced, so a prototype that grows more
        /// progress later grows it beside this and not over it.</summary>
        public const string Key = "FACET.ClearedCount";

        /// <summary>The count as the store holds it, or 0 where it holds nothing - the fresh profile's
        /// answer, and the same one a player who cleared their prefs gets.</summary>
        public static int Read() => Mathf.Max(0, PlayerPrefs.GetInt(Key, 0));

        /// <summary>Put the count in the store, written through at once so a tab closed a second later
        /// keeps it - a run can end at any moment, and progress lost to a crash is progress the level
        /// list re-locks. Negative counts are refused outright - the store keeps what it had, because
        /// the count only ever grows, and its only writer reads a never-negative answer.</summary>
        public static void Write(int clearedCount)
        {
            if (clearedCount < 0)
            {
                // Refused, not clamped: the count only ever grows, so a negative is a bug upstream rather
                // than a value to round. Said out loud so the invariant is auditable during development.
                Debug.LogWarning("FACET: refused a negative cleared count (" + clearedCount +
                    ") - the store keeps what it had.");
                return;
            }

            PlayerPrefs.SetInt(Key, clearedCount);
            PlayerPrefs.Save();
        }

        /// <summary>Forget the count - the test suite's way back to a fresh profile, and nobody else's.
        /// Written through like <see cref="Write"/>, so a suite that forgets and then crashes has genuinely
        /// forgotten rather than left the old count waiting in an unflushed buffer.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
