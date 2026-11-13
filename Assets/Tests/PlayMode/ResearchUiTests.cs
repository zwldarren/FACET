using System.Collections;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Facet.PlayTests
{
    /// <summary>Research through the real views: the locked key on the bar, the coach teaching a lesson without
    /// stopping the run, and the game-over banner's wave count. Runs a fixture-built one-map campaign.</summary>
    public class ResearchUiTests
    {
        private const string MapName = "test research";

        private GameObject _cameraObject;
        private GameObject _driverObject;
        private MapSet _campaign;
        private MapAsset _map;
        private int _progressBefore;
        private bool _progressKeyExisted;

        private SimulationDriver Driver => _driverObject.GetComponent<SimulationDriver>();
        private HudView Hud => _driverObject.GetComponentInChildren<HudView>(true);
        private Transform Popup => _driverObject.transform.Find("TutorialCoach/CoachCanvas/popup");

        [SetUp]
        public void CreateScene()
        {
            _progressKeyExisted = PlayerPrefs.HasKey(CampaignProgress.Key);
            _progressBefore = CampaignProgress.Read();
            CampaignProgress.Forget();
            PlayerPrefs.DeleteKey(BestWave.KeyPrefix + MapName);

            _cameraObject = new GameObject("Main Camera");
            _cameraObject.tag = "MainCamera";
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;

            _driverObject = new GameObject("FACET");
            _driverObject.SetActive(false);
            SimulationDriver driver = _driverObject.AddComponent<SimulationDriver>();

            _map = ResearchMap();
            _campaign = ScriptableObject.CreateInstance<MapSet>();
            _campaign.Maps = new[] { _map };

#if UNITY_EDITOR
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("campaign").objectReferenceValue = _campaign;
            serialized.ApplyModifiedPropertiesWithoutUndo();
#endif

            _driverObject.SetActive(true);
        }

        [TearDown]
        public void DestroyScene()
        {
            if (_driverObject != null) Object.Destroy(_driverObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
            if (_map != null) Object.Destroy(_map);
            if (_campaign != null) Object.Destroy(_campaign);

            PlayerPrefs.DeleteKey(BestWave.KeyPrefix + MapName);
            if (_progressKeyExisted) CampaignProgress.Write(_progressBefore);
            else CampaignProgress.Forget();
        }

        /// <summary>Empty waves, so a test clears one by starting it. Splitter on a popup after wave 1, Pipe on a hint
        /// after wave 2.</summary>
        private static MapAsset ResearchMap()
        {
            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.Name = MapName;
            map.Width = 32;
            map.Height = 20;
            map.StartShapes = 100;
            map.SpawnPoints = new[] { new Vector2Int(30, 10) };

            map.Waves = new MapAsset.WaveRow[5];
            for (int i = 0; i < map.Waves.Length; i++)
                map.Waves[i] = new MapAsset.WaveRow { Intermission = 60f, Groups = new MapAsset.SpawnGroupRow[0] };

            map.Research = new[]
            {
                new MapAsset.ResearchRow
                {
                    AfterWave = 1, Builds = new[] { BuildKind.Splitter }, Cost = 20,
                    Lesson = new[]
                    {
                        new MapAsset.TutorialTaskRow
                        {
                            Step = TutorialStep.ResearchBuilding, Title = "New: Splitter",
                            Text = "Click the SPLITTER to unlock it.", Opens = new[] { BuildKind.Splitter },
                            Focus = new[] { TutorialFocus.BarTile },
                        },
                        new MapAsset.TutorialTaskRow
                        {
                            Step = TutorialStep.SelectBuilding, Title = "Pick the Splitter",
                            Text = "Press 7, or click the SPLITTER.", Opens = new[] { BuildKind.Splitter },
                            Focus = new[] { TutorialFocus.BarTile, TutorialFocus.Demo },
                        },
                    },
                },
                new MapAsset.ResearchRow
                {
                    AfterWave = 2, Builds = new[] { BuildKind.Pipe }, Cost = 10, Hint = "PIPE available",
                },
            };
            return map;
        }

        private IEnumerator ClearWave(SimWorld world)
        {
            int target = world.Waves.Cleared + 1;
            world.Waves.StartNextWave();
            for (int i = 0; i < 300 && world.Waves.Cleared < target; i++) yield return null;
            Assert.AreEqual(target, world.Waves.Cleared, "the empty wave clears");

            // The card and the bar react in their own updates.
            yield return null;
            yield return null;
        }

        private Transform Key(string name)
        {
            Transform row = Hud.transform.Find("HudCanvas/bar/tiles");
            Assert.IsNotNull(row, "the bar has its row of keys");
            foreach (Transform tile in row)
                if (tile.Find("name").GetComponent<TMPro.TMP_Text>().text == name) return tile;
            return null;
        }

        private static string Words(Transform node, string child)
            => node.Find(child).GetComponent<TMPro.TMP_Text>().text;

        /// <summary>Finish the Splitter's lesson, so a test about something later is not talking over it.</summary>
        private IEnumerator SkipLesson(SimWorld world)
        {
            world.RequestResearch(BuildKind.Splitter);
            for (int i = 0; i < 60 && !world.IsUnlocked(BuildKind.Splitter); i++) yield return null;
            Key("Splitter").GetComponent<HudButton>().Pressed.Invoke();
            for (int i = 0; i < 60 && world.Research.Teaching; i++) yield return new WaitForSeconds(0.02f);
            Assert.IsFalse(world.Research.Teaching, "the lesson is over");
            yield return null;
        }

        [UnityTest]
        public IEnumerator BeforeItsWave_NothingIsOffered()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;
            yield return null;

            Assert.IsNotNull(Key("Belt"), "the schedule does not name the belt");
            Assert.IsNull(Key("Unlock Splitter"), "the Splitter waits for its wave");
            Assert.IsNull(Key("Splitter"));
            Assert.IsFalse(Popup.gameObject.activeSelf, "and there is nothing to teach");
        }

        [UnityTest]
        public IEnumerator TheLesson_TeachesThroughTheCoach_WithoutStoppingTheRun()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);

            Assert.IsTrue(Popup.gameObject.activeSelf, "the coach's popup is up");
            Assert.AreEqual("New: Splitter", Words(Popup, "title"));
            Assert.AreEqual("STEP 1 OF 2", Words(Popup, "counter"));
            Assert.IsFalse(world.Paused, "and the run is not held: the player has to do what it says");
            Assert.IsFalse(Popup.Find("demo").gameObject.activeSelf, "a line without a demo shows none");

            Transform box = Popup.parent.Find("focus0");
            Assert.IsNotNull(box, "the key it is about is boxed");
            Assert.IsTrue(box.gameObject.activeSelf);

            float countdown = world.Waves.IntermissionRemaining;
            int tick = world.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(world.TickCount, tick, "the world ticks");
            Assert.AreEqual(countdown, world.Waves.IntermissionRemaining, "and the wave waits");

            // Line one is the locked key's press.
            Key("Unlock Splitter").GetComponent<HudButton>().Pressed.Invoke();
            for (int i = 0; i < 60 && world.Research.Lesson != null && world.Research.Lesson.StepsCompleted == 0; i++)
                yield return new WaitForSeconds(0.02f);
            yield return null;
            Assert.AreEqual("Pick the Splitter", Words(Popup, "title"), "the next line");
            Assert.AreEqual("STEP 2 OF 2", Words(Popup, "counter"));
            Transform demo = Popup.Find("demo");
            Assert.IsTrue(demo.gameObject.activeSelf, "this line plays the machine at work");
            Assert.AreEqual("SPLITTER", Words(demo, "machine/name"));
            int items = 0;
            foreach (Transform child in demo)
                if (child.name.StartsWith("item") && child.gameObject.activeSelf) items++;
            Assert.Greater(items, 0, "with shapes riding through it");

            // Line two is the key's press too, now that it selects.
            Key("Splitter").GetComponent<HudButton>().Pressed.Invoke();
            for (int i = 0; i < 60 && world.Research.Teaching; i++) yield return new WaitForSeconds(0.02f);
            yield return null;

            Assert.IsFalse(Popup.gameObject.activeSelf, "the lesson is over");
            float before = world.Waves.IntermissionRemaining;
            yield return new WaitForSeconds(0.3f);
            Assert.Less(world.Waves.IntermissionRemaining, before, "and the wave is on its way again");
        }

        [UnityTest]
        public IEnumerator TheLockedKey_ShowsItsPrice_AndBuysTheBuilding()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);

            Transform key = Key("Unlock Splitter");
            Assert.IsNotNull(key, "offered: on the bar, locked");
            Assert.AreEqual("20", Words(key, "cost"), "wearing the research price");
            Assert.AreEqual("+", Words(key, "hotkey"), "and not a hotkey that does nothing");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter));

            int before = world.Economy.Shapes;
            key.GetComponent<HudButton>().Pressed.Invoke();
            for (int i = 0; i < 60 && !world.IsUnlocked(BuildKind.Splitter); i++) yield return new WaitForSeconds(0.02f);
            yield return null;

            Assert.IsTrue(world.IsUnlocked(BuildKind.Splitter), "the press bought it");
            Assert.AreEqual(before - 20, world.Economy.Shapes, "out of the Core");

            key = Key("Splitter");
            Assert.IsNotNull(key, "the key now names the building");
            Assert.AreEqual(world.Content.Machine(BuildKind.Splitter).Cost.ToString(), Words(key, "cost"),
                "and wears the building's own price");
        }

        [UnityTest]
        public IEnumerator TheLockedKey_RefusesWhatTheCoreCannotPay()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);

            world.Economy.Reset(5);
            Key("Unlock Splitter").GetComponent<HudButton>().Pressed.Invoke();
            yield return new WaitForSeconds(0.2f);

            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter));
            Assert.AreEqual(5, world.Economy.Shapes, "nothing was spent");
            Assert.IsNotNull(Key("Unlock Splitter"), "and the key is still on offer");
            Assert.AreEqual("New: Splitter", Words(Popup, "title"), "and the lesson is still on that line");
        }

        [UnityTest]
        public IEnumerator AnOfferWithoutALesson_OnlyPutsItsKeyOnTheBar()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);
            yield return SkipLesson(world);

            yield return ClearWave(world);

            Assert.IsFalse(Popup.gameObject.activeSelf, "a hint gets no lesson");
            Assert.IsFalse(world.Paused);
            Assert.IsNotNull(Key("Unlock Pipe"), "but its key is on the bar");
        }

        [UnityTest]
        public IEnumerator CallingTheWave_SkipsTheLesson()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);
            Assert.IsTrue(Popup.gameObject.activeSelf);

            world.RequestNextWave();
            yield return new WaitForSeconds(0.2f);

            Assert.IsFalse(Popup.gameObject.activeSelf, "the player called it: they know this already");
            Assert.IsNotNull(Key("Unlock Splitter"), "and the research is still there to buy");
        }

        [UnityTest]
        public IEnumerator ARestart_TakesTheLessonAndTheOffersBack()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);
            Assert.IsTrue(Popup.gameObject.activeSelf);

            // A restart lands on the next tick, and a frame here is far shorter than a tick.
            world.RequestRestart();
            yield return new WaitForSeconds(0.2f);

            Assert.IsFalse(Popup.gameObject.activeSelf, "the new run never made that offer");
            Assert.IsNull(Key("Unlock Splitter"), "and the key has left the bar");

            yield return ClearWave(world);
            Assert.IsTrue(Popup.gameObject.activeSelf, "offered afresh, taught afresh");
            Assert.AreEqual("STEP 1 OF 2", Words(Popup, "counter"));
        }

        [UnityTest]
        public IEnumerator TheBuildCard_ShowsTheBuildingAtWork_InsteadOfAParagraph()
        {
            yield return null;
            Driver.PlayMap(0, false);
            yield return null;

            Key("Belt").GetComponent<HudButton>().Pressed.Invoke();
            for (int i = 0; i < 60 && Driver.World.SelectedKind != BuildKind.Belt; i++)
                yield return new WaitForSeconds(0.02f);
            yield return null;
            yield return null;

            Transform build = Hud.transform.Find("HudCanvas/info/build");
            Assert.IsNotNull(build, "the info card's build face");
            Assert.IsTrue(build.gameObject.activeInHierarchy, "is up for the building in hand");

            Transform demo = build.Find("demo");
            Assert.IsNotNull(demo, "the card carries a demo");
            Assert.IsTrue(demo.gameObject.activeSelf, "and plays it");
            Assert.IsFalse(build.Find("blurb").gameObject.activeSelf, "in place of the paragraph");
            Assert.IsFalse(build.Find("stats").gameObject.activeSelf, "and the numbers under it");

            int items = 0;
            foreach (Transform child in demo)
                if (child.name.StartsWith("item") && child.gameObject.activeSelf) items++;
            Assert.Greater(items, 0, "with shapes riding through it");

            // The demo sits inside the card, between the header and the facing line.
            var card = (RectTransform)Hud.transform.Find("HudCanvas/info");
            var strip = (RectTransform)demo;
            Assert.LessOrEqual(strip.sizeDelta.x, card.sizeDelta.x, "no wider than the card");
            Assert.Greater(strip.sizeDelta.y, 0f);
        }

        [Test]
        public void EveryBuildingTheBarOffers_HasADemo()
        {
            foreach (BuildKind kind in BuildCatalog.All)
                if (!BuildCatalog.Retired(kind))
                    Assert.IsTrue(MachineDemo.Covers(kind), kind + " has no demo for its card");
        }

        [UnityTest]
        public IEnumerator AnEndlessLoss_ReportsTheWavesHeld_AndKeepsTheBest()
        {
            yield return null;
            Driver.PlayMap(0, true);
            yield return null;

            SimWorld world = Driver.World;
            yield return ClearWave(world);
            yield return null;

            Assert.AreEqual(1, BestWave.Read(MapName), "the record is written as it is beaten");

            world.Core.Hp = 0f;
            for (int i = 0; i < 60 && world.Status != GameStatus.Lost; i++) yield return new WaitForSeconds(0.02f);
            yield return null;
            yield return null;

            Assert.AreEqual(GameStatus.Lost, world.Status);
            Assert.IsFalse(Popup.gameObject.activeSelf, "the lesson steps aside for the end of the run");
            var banner = Hud.transform.Find("HudCanvas/banner");
            Assert.IsTrue(banner.gameObject.activeSelf);
            string words = banner.GetComponentInChildren<TMPro.TMP_Text>().text;
            StringAssert.Contains("you held 1 wave", words);
            StringAssert.Contains("best 1", words);
        }
    }
}
