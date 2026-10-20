using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The persistence decision, pinned: <see cref="CampaignProgress"/> is the one place the campaign's
    /// cleared count is written and read, and these tests say what a store with nothing in it answers -
    /// the fresh browser profile's case, which is the one a first-time player boots into and the one a
    /// prototype must not lie about. Run in the Editor's own prefs, and put back afterwards: the count
    /// the machine had before the suite ran is restored, so a suite run never leaves the developer's
    /// own progress wiped behind it.
    /// </summary>
    public class CampaignProgressTests
    {
        private int _progressBefore;
        private bool _keyExistedBefore;

        [SetUp]
        public void SaveAndForget()
        {
            _keyExistedBefore = PlayerPrefs.HasKey(CampaignProgress.Key);
            // Raw, not Read(): the restore has to put back exactly what was there, and Read() clamps a
            // negative - which PlayerPrefs can hold - to zero.
            _progressBefore = PlayerPrefs.GetInt(CampaignProgress.Key, 0);
            CampaignProgress.Forget();
        }

        [TearDown]
        public void Restore()
        {
            // Put the store back exactly as it was found: absent is not the same as 0, and a raw value - a
            // negative one PlayerPrefs can hold, which Write would refuse - is not the same as a clamped
            // one. Written through the store directly, because the point is to restore, not to exercise the
            // class under test with a value it exists to reject.
            if (_keyExistedBefore)
            {
                PlayerPrefs.SetInt(CampaignProgress.Key, _progressBefore);
                PlayerPrefs.Save();
            }
            else CampaignProgress.Forget();
        }

        [Test]
        public void AStoreWithNothingInIt_ReadsNoProgress()
        {
            // The fresh profile: no key, no progress. The level list then opens with its first map
            // alone - the rule, stated here so it cannot quietly become "everything open".
            Assert.AreEqual(0, CampaignProgress.Read());
        }

        [Test]
        public void TheCountSurvivesAWriteAndARead()
        {
            CampaignProgress.Write(2);
            Assert.AreEqual(2, CampaignProgress.Read(), "what was written is what is read back");
        }

        [Test]
        public void ANegativeCount_IsRefused()
        {
            // The count only ever grows: a store is written from CampaignState.ClearedCount, which is
            // never negative, and a negative in the store would read as less than a fresh profile.
            CampaignProgress.Write(2);
            CampaignProgress.Write(-4);
            Assert.AreEqual(2, CampaignProgress.Read(), "the last honest count stands");
        }

        [Test]
        public void Forgetting_ReturnsToTheFreshProfile()
        {
            CampaignProgress.Write(1);
            CampaignProgress.Forget();
            Assert.AreEqual(0, CampaignProgress.Read());
        }
    }
}
