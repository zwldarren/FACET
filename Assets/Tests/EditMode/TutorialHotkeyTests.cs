using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>The bar numbers its keys by position, so a lesson that wrote a digit down goes stale the moment the
    /// bar changes - "Press 0" for a turret that sits on 2 is a key that does nothing. A line about a building past
    /// the bar's first key says <c>{key}</c>, and the coach fills in the digit the bar uses now.</summary>
    public class TutorialHotkeyTests
    {
        private const string TutorialPath = "Assets/Data/Maps/Tutorial.asset";

        [Test]
        public void TheTutorialsTurretLines_AskForTheTurretsKey_NotADigit()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>(TutorialPath);
            Assert.IsNotNull(asset, TutorialPath + " is missing");
            TutorialScript script = asset.ToCore().Tutorial;

            int keyed = 0;
            for (int i = 0; i < script.Count; i++)
            {
                TutorialTask line = script[i];
                Assert.IsFalse(line.Text.Contains("Press 0"),
                    "line " + (i + 1) + " names a digit the bar no longer gives the turret: " + line.Text);

                if (!line.Text.Contains("{key}")) continue;
                keyed++;
                Assert.IsTrue(TutorialScript.TryRequiredBuilding(line.Step, out BuildKind building),
                    "line " + (i + 1) + " says {key}, so its step has to name the building whose key it is");
                Assert.AreEqual(BuildKind.Turret, building, "line " + (i + 1) + " is about the turret");
            }

            Assert.AreEqual(3, keyed, "the three turret lines: the first gun, the second, and the left one");
        }
    }
}
