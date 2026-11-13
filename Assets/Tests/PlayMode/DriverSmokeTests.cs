using System.Collections;
using System.Text.RegularExpressions;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Facet.PlayTests
{
    /// <summary>The view layer actually running - the driver assembles its views, advances the world at its
    /// fixed rate, and gives a changed view mesh. Runs a fixture-built campaign, not the shipped one.</summary>
    public class DriverSmokeTests
    {
        /// <summary>The round the rig's line runs: the cutter's half-circle, and the demand its enemies carry,
        /// so a shot that lands is a matched one.</summary>
        private static readonly Shape Ammo = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);

        private GameObject _cameraObject;
        private GameObject _driverObject;
        private MapSet _campaign;
        private int _progressBefore;
        private bool _progressKeyExisted;
        private bool _progressSnapshotted;

        private SimulationDriver Driver => _driverObject.GetComponent<SimulationDriver>();

        /// <summary>Leave the boot title the way the play button does - the game opens held on it.</summary>
        private void BeginRun() => Driver.PlayMap(0, false);

        [SetUp]
        public void CreateScene()
        {
            // Snapshot and forget the player's progress before anything that can throw: TearDown runs even
            // when SetUp fails, and on default fields it would Forget() rather than restore.
            _progressKeyExisted = PlayerPrefs.HasKey(CampaignProgress.Key);
            _progressBefore = CampaignProgress.Read();
            CampaignProgress.Forget();
            _progressSnapshotted = true;

            _cameraObject = new GameObject("Main Camera");
            _cameraObject.tag = "MainCamera";
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(40f, 24f, -10f);

            // Built switched off, wired, then switched on: Awake would otherwise ask for a campaign the
            // driver has not got yet. Only the campaign is wired - a missing Palette falls back by design.
            _driverObject = new GameObject("FACET");
            _driverObject.SetActive(false);
            SimulationDriver driver = _driverObject.AddComponent<SimulationDriver>();

            _campaign = TestCampaign();

#if UNITY_EDITOR
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("campaign").objectReferenceValue = _campaign;
            serialized.ApplyModifiedPropertiesWithoutUndo();
#endif

            // The driver opens on its saved progress' frontier, so a machine that has played the shipped
            // maps must not decide which map the suite starts on.
            _driverObject.SetActive(true);
        }

        [TearDown]
        public void DestroyScene()
        {
            if (_driverObject != null) Object.Destroy(_driverObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
            if (_campaign != null)
            {
                if (_campaign.Maps != null)
                    foreach (MapAsset map in _campaign.Maps)
                        if (map != null) Object.Destroy(map);

                Object.Destroy(_campaign);
            }

            if (_progressSnapshotted)
            {
                _progressSnapshotted = false;
                if (_progressKeyExisted) CampaignProgress.Write(_progressBefore);
                else CampaignProgress.Forget();
            }
        }

        /// <summary>Two maps because a swap needs two, and the second is a different size - what the ground
        /// view's rebind is proved against. A third teaches one line.</summary>
        private static MapSet TestCampaign()
        {
            var set = ScriptableObject.CreateInstance<MapSet>();
            set.Maps = new[] { GroundMap(), WideMap(), LessonMap() };
            return set;
        }

        /// <summary>32x20, rock and water painted, a void corner, one circle vein, one door and one wave that
        /// never arrives: the tests place their own belt, turret and enemy on it.</summary>
        private static MapAsset GroundMap()
        {
            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.Name = "test ground";
            map.Width = 32;
            map.Height = 20;
            map.StartShapes = 80;
            map.Terrain = TerrainLayout.Rects(32, 20,
                new TerrainRect(new Int2(2, 2), 6, 4, TerrainKind.Rock),
                new TerrainRect(new Int2(20, 12), 4, 4, TerrainKind.Water),
                new TerrainRect(new Int2(30, 18), 2, 2, TerrainKind.Void));
            map.Ore = new[] { new MapAsset.OreCell { Cell = new Vector2Int(6, 12), Mineral = Quad.Circle } };
            map.SpawnPoints = new[] { new Vector2Int(2, 18) };
            map.Waves = new[] { OneWave() };
            return map;
        }

        /// <summary>48x12, plain ground and a door of its own: the world built next is a different shape.</summary>
        private static MapAsset WideMap()
        {
            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.Name = "test wide";
            map.Width = 48;
            map.Height = 12;
            map.StartShapes = 80;
            map.SpawnPoints = new[] { new Vector2Int(45, 2) };
            map.Waves = new[] { OneWave() };
            return map;
        }

        /// <summary>A plain field that teaches one line, so a test can ask whether a replay is taught again.</summary>
        private static MapAsset LessonMap()
        {
            var map = ScriptableObject.CreateInstance<MapAsset>();
            map.Name = "test lesson";
            map.Width = 32;
            map.Height = 20;
            map.StartShapes = 80;
            map.SpawnPoints = new[] { new Vector2Int(2, 18) };
            map.Waves = new[] { OneWave() };
            map.TutorialStarting = new[] { BuildKind.Belt };
            map.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.SelectBelt,
                    Text = "Pick up a belt.",
                    Target = new Vector2Int(6, 12),
                },
            };
            return map;
        }

        private static MapAsset.WaveRow OneWave()
            => new MapAsset.WaveRow
            {
                Intermission = 60f,
                Groups = new[]
                {
                    new MapAsset.SpawnGroupRow
                    {
                        Kind = EnemyKind.Runner, Count = 2, SpawnPoint = 0, FirstDelay = 0f, Interval = 2f,
                        Weakness = ShapeField.FromCore(Ammo),
                    },
                },
            };

        /// <summary>The one thing with no fallback: a missing campaign is a game with nothing to play, so the
        /// driver reports it and stands down rather than ticking nothing.</summary>
        [UnityTest]
        public IEnumerator ADriverWithNoCampaign_RefusesToStart()
        {
            LogAssert.Expect(LogType.Error, new Regex("FACET: no campaign"));

            var bare = new GameObject("FACET (no campaign)");
            SimulationDriver driver = bare.AddComponent<SimulationDriver>();
            yield return null;

            Assert.IsNull(driver.World, "there is no world to build");
            Assert.IsFalse(driver.enabled, "and the driver stands down rather than ticking nothing");

            Object.Destroy(bare);
        }

        /// <summary>The scene the build actually boots, not one this test built: a missing camera, driver or
        /// Palette wiring would be an empty blue screen no test would notice.</summary>
        [UnityTest]
        public IEnumerator TheShippedMainScene_RunsAsBuilt()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;

            SimulationDriver driver = Object.FindFirstObjectByType<SimulationDriver>();
            Assert.IsNotNull(driver, "Assets/Scenes/Main.unity has to contain the driver");
            Assert.IsNotNull(driver.Colors, "and has to have the Palette asset wired, not the code default");
            Assert.IsNotNull(driver.World, "and has to be running");
            Assert.AreEqual(driver.Catalog.At(0).Width, driver.World.TileGrid.Width, "on the map the design calls for");
            Assert.AreEqual(driver.Catalog.At(0).Height, driver.World.TileGrid.Height);
            Assert.IsNotNull(Camera.main, "with a MainCamera for the views to sit under");

            // The shipped scene opens held on the title: no map ticks until the play button starts the clock.
            ShellView shell = Object.FindFirstObjectByType<ShellView>();
            Assert.IsNotNull(shell, "the driver builds the shell the title lives in");
            Assert.IsTrue(shell.Open, "and the shipped scene opens on that title");
            Assert.AreEqual(0, driver.World.TickCount, "the held run has spent no time");

            // The committed content asset must agree with the code's own table, or "the shipped game"
            // depends on which of the two you happened to read.
            Assert.IsNotNull(driver.Content, "and a content table");
            foreach (BuildKind kind in driver.Content.BuildKinds)
            {
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Cost, driver.Content.Machine(kind).Cost,
                    "the committed content asset disagrees with the shipped cost for " + kind);
                Assert.AreEqual(ContentDatabase.Default.Machine(kind).Behavior, driver.Content.Machine(kind).Behavior,
                    "the committed content asset disagrees with the shipped behaviour for " + kind);
            }

            Assert.AreEqual(ContentDatabase.Default.Turret(BuildKind.Turret).Damage,
                driver.Content.Turret(BuildKind.Turret).Damage, 1e-4f, "the shipped gun's damage");
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Runner).Hp,
                driver.Content.Enemy(EnemyKind.Runner).Hp, 1e-4f, "the shipped runner's hp");

            // The same door a player's click takes.
            driver.PlayMap(0, false);
            yield return null;

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs in the shipped scene too");
        }

        [UnityTest]
        public IEnumerator Driver_BuildsEveryView_AndAdvancesTheWorld()
        {
            yield return null;

            var driver = Driver;
            Assert.IsNotNull(driver.World, "the driver owns a simulation");
            BeginRun();          // the boot title holds the run; the play door hands it back
            yield return null;

            foreach (string view in new[] { "Grid", "Terrain", "Ore", "Belts", "Core", "Machines", "Items", "Enemies", "Shots", "Cursor", "ThreatRadar", "Hud", "Shell" })
                Assert.IsNotNull(_driverObject.transform.Find(view), "the driver did not build the " + view + " view");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(driver.World.TickCount, ticks, "the fixed 30 Hz clock keeps running across frames");
        }

        /// <summary>The one test of a player's first frame: the scene opens on the title, with the run built
        /// behind it and held - no device, no tick and no second spent before play.</summary>
        [UnityTest]
        public IEnumerator TheBootTitle_HoldsTheRun_UntilThePlayerPlays()
        {
            yield return null;

            SimulationDriver driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            Assert.IsNotNull(shell, "the driver builds the shell that opens the game");
            Assert.IsTrue(shell.Open, "and the boot lands on its title screen");
            Assert.AreEqual(0, driver.World.TickCount, "the held run has spent no time");

            int held = driver.World.TickCount;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(held, driver.World.TickCount, "no time passes on the title");

            driver.PlayMap(0, false);
            yield return null;
            Assert.IsFalse(shell.Open, "the play door closes the title");

            int started = driver.World.TickCount;
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(driver.World.TickCount, started, "and the clock runs once a run is started");
        }

        /// <summary>The one path a press takes that a key does not: uGUI dispatches from inside its own walk of
        /// the card, so a shell that rebuilt itself in the callback delivered that same click again to the level
        /// list's first row - "the maps" started map 1. Pressed the way the input module presses it.</summary>
        [UnityTest]
        public IEnumerator TheTitlesMapButton_OpensTheList_InsteadOfStartingARun()
        {
            yield return null;

            SimulationDriver driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            Assert.IsTrue(shell.AtTitle, "the boot lands on the title");
            SimWorld held = driver.World;

            Assert.IsTrue(Press(shell, "title/maps"), "the title's map button survives its own press");
            yield return null;   // the shell builds the screen the press asked for in its own update

            Assert.IsFalse(shell.AtTitle, "the maps button opens the map list");
            Assert.IsTrue(shell.Open, "and the shell is still the screen in front of the run");
            Assert.AreSame(held, driver.World, "a list is not a run: nothing was started");
            Assert.AreEqual(0, driver.World.TickCount, "and the run behind it is still held");

            // The list is a picker, not a wall: the row starts the map it names, and closes the list.
            Assert.IsTrue(Press(shell, "levels/card/map0"), "a map row answers its press");
            yield return null;

            Assert.IsFalse(shell.Open, "picking a map closes the shell onto the run");
            Assert.AreEqual(driver.Catalog.At(0).Name, driver.World.Map.Name, "and starts that map");
            Assert.AreEqual(0, driver.Campaign.MapIndex, "the campaign is on it");
        }

        /// <summary>The armed doors are pressed the same way, and here the rule shows: the press that never
        /// finished came back on the next frame at the same button, collapsing two presses into one.</summary>
        [UnityTest]
        public IEnumerator TheArmedRestartDoor_TakesOnePress_ToArm_AndOneToTake()
        {
            yield return null;
            BeginRun();

            SimulationDriver driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            SimWorld run = driver.World;
            var belt = new Int2(20, 10);
            Assert.IsTrue(run.TryPlaceBelt(belt, Dir.East));

            driver.OpenMenu();
            yield return null;

            Assert.IsTrue(Press(shell, "pause/card/restart"), "the restart button answers its press");
            yield return null;

            Assert.IsTrue(shell.Open, "one press only arms the door");
            Assert.AreSame(run, driver.World, "and leaves the run where it was");
            Assert.IsTrue(run.Belts.Has(belt), "with the factory still standing");

            Assert.IsTrue(Press(shell, "pause/card/restart"), "the armed door answers the next press");
            yield return null;
            yield return new WaitForSeconds(0.2f);   // the next tick applies the restart request

            Assert.IsFalse(shell.Open, "the second press takes the door");
            Assert.IsFalse(run.Belts.Has(belt), "and starts the run over");
        }

        [UnityTest]
        public IEnumerator TheShell_HoldsTheRun_WhileItStands()
        {
            yield return null;
            BeginRun();          // the pause menu is opened over a run, so leave the boot title first

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Assert.IsNotNull(shell, "the driver builds the shell");
            Assert.IsNotNull(hud, "and the HUD the menu stands down");

            // Esc and the HUD's menu button open it this way too, only with the input device: OpenMenu is the
            // state the driver holds the run on.
            driver.OpenMenu();
            yield return null;

            Assert.IsTrue(shell.Open, "the pause menu is the one on screen");
            Assert.IsFalse(hud.enabled, "and the HUD is stood down behind it");

            int heldTicks = driver.World.TickCount;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(heldTicks, driver.World.TickCount, "the run does not tick behind the shell");
            Assert.IsTrue(shell.Open, "and the shell is still the one on screen");

            driver.ResumeRun();
            yield return null;
            Assert.IsFalse(shell.Open, "resume closes the menu");
            Assert.IsTrue(hud.enabled, "and hands the HUD back");
            yield return new WaitForSeconds(0.2f);   // the 30 Hz clock needs a tick's worth of time
            Assert.Greater(driver.World.TickCount, heldTicks, "and it ticks again once the shell is gone");
        }

        /// <summary>The status rail is a prefab (<c>Resources/Hud/HudStatus</c>), but <see cref="HudLayout"/> still
        /// owns its rect: a prefab free to place itself would reserve one place and draw in another.</summary>
        [UnityTest]
        public IEnumerator TheHudStatusRail_IsThePrefabAtTheLayoutsRect()
        {
            yield return null;
            BeginRun();
            yield return null;   // the rebuild a map change asks for lands on the next frame

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Assert.IsNotNull(hud, "the driver builds the HUD");
            Transform canvas = hud.transform.Find("HudCanvas");
            Assert.IsNotNull(canvas, "the HUD builds its canvas");

            var status = canvas.Find("status") as RectTransform;
            Assert.IsNotNull(status, "the rail is the prefab's own 'status' node, not a built card");

            float s = HudLayout.Scale(Screen.height);
            Rect laid = HudLayout.Status(s);
            Assert.AreEqual(laid.width, status.sizeDelta.x, 0.5f, "the rail's width is the layout's");
            Assert.AreEqual(laid.height, status.sizeDelta.y, 0.5f, "the rail's height is the layout's");
            Assert.AreEqual(laid.x, status.anchoredPosition.x, 0.5f, "the rail's left edge is the layout's");
            Assert.AreEqual(-laid.y, status.anchoredPosition.y, 0.5f, "the rail's top edge is the layout's");

            // Authored in design units, scaled once at bind: the track is the rail less one 14-unit inset each side.
            var track = status.Find("health.back") as RectTransform;
            Assert.IsNotNull(track, "the prefab carries the Core's track");
            Assert.AreEqual((HudLayout.StatusWidth - 2f * 14f) * s, track.sizeDelta.x, 0.5f,
                "the card's rows carry the design units scaled to this screen");

            // Two rows of six slots: a prefab that lost one fails here rather than mid-wave.
            Assert.IsNotNull(status.Find("chip5.name"), "the prefab carries the first chip row's six slots");
            Assert.IsNotNull(status.Find("after5.name"), "and the second row's");
        }

        /// <summary>The "how to play" card is a prefab (<c>Resources/Hud/HudTutorial</c>) and the lesson's card is
        /// its own (<c>Resources/Hud/HudTasks</c>): two cards, one rect. With no lesson the reference card owns it.</summary>
        [UnityTest]
        public IEnumerator TheHudTutorialCard_IsThePrefabAtTheLayoutsRect()
        {
            yield return null;
            BeginRun();
            yield return null;   // the rebuild a map change asks for lands on the next frame

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");
            Assert.IsNotNull(canvas, "the HUD builds its canvas");

            var tutorial = canvas.Find("tutorial") as RectTransform;
            Assert.IsNotNull(tutorial, "the reference card is the prefab's own 'tutorial' node");

            float s = HudLayout.Scale(Screen.height);
            Rect laid = HudLayout.Tutorial(s);
            Assert.AreEqual(laid.width, tutorial.sizeDelta.x, 0.5f, "the card is the layout's width");
            Assert.AreEqual(laid.height, tutorial.sizeDelta.y, 0.5f, "and the layout's height");
            Assert.AreEqual(laid.x, tutorial.anchoredPosition.x, 0.5f, "hung off the screen's top-left");
            Assert.AreEqual(-laid.y, tutorial.anchoredPosition.y, 0.5f, "under the status rail");

            // The fixture's first map teaches nothing, so the corner is the reference card's.
            Assert.IsTrue(tutorial.gameObject.activeSelf, "a map with no lesson leaves the card up");
            Assert.IsFalse(canvas.Find("tasks").gameObject.activeSelf, "and the lesson's card down");

            // Rows in design units, scaled once at bind: the one card inset, the heading's 22, the rule on 27,
            // the body's 66.
            AssertRow(tutorial, "title", 14f, 4f, 250f, 22f, s);
            AssertRow(tutorial, "rule", 14f, 27f, 332f, 1f, s);
            AssertRow(tutorial, "body", 14f, 30f, 332f, 66f, s);

            var hide = tutorial.Find("hide") as RectTransform;
            Assert.IsNotNull(hide, "the card carries its hide button");
            Assert.AreEqual(-14f * s, hide.anchoredPosition.x, 0.5f, "inside the card's right inset");
            Assert.AreEqual(-5f * s, hide.anchoredPosition.y, 0.5f, "one line down from its top");
            Assert.AreEqual(58f * s, hide.sizeDelta.x, 0.5f, "the button is its design width");
            Assert.AreEqual(18f * s, hide.sizeDelta.y, 0.5f, "and its design height");

            var button = hide.GetComponent<HudButton>();
            Assert.IsNotNull(button, "the hide button answers the pointer");
            Assert.IsNotNull(button.Fill, "it has its surface");
            Assert.IsTrue(button.Fill.Image.raycastTarget, "and takes the click");
            Assert.IsNotNull(hide.Find("hide.label"), "and its words");
        }

        /// <summary>The lesson's card is the second prefab (<c>Resources/Hud/HudTasks</c>) and the one whose
        /// contents are the map's. Drives it on the map with a lesson, swaps to the reference card with <c>T</c>,
        /// and presses that card's own way back.</summary>
        [UnityTest]
        public IEnumerator TheHudTaskCard_IsThePrefab_AndPrintsTheLesson()
        {
            yield return null;
            Driver.PlayMap(2, false);   // the fixture's map with a lesson on it
            yield return null;

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");

            var tasks = canvas.Find("tasks") as RectTransform;
            Assert.IsNotNull(tasks, "the lesson's card is the prefab's own 'tasks' node");
            Assert.IsTrue(tasks.gameObject.activeSelf, "a lesson owns the corner while it is being taught");
            Assert.IsFalse(canvas.Find("tutorial").gameObject.activeSelf, "and the reference card waits");

            float s = HudLayout.Scale(Screen.height);
            Rect laid = HudLayout.Tutorial(s);
            Assert.AreEqual(laid.size, tasks.sizeDelta, "the two cards are one rect and one place");
            Assert.AreEqual(-laid.y, tasks.anchoredPosition.y, 0.5f, "under the status rail");

            // Heading, the count at the far end, the hairline, the lines' 74 and the hint's own 14 under them:
            // the card never grows.
            AssertRow(tasks, "title", 14f, 4f, 190f, 22f, s);
            AssertRow(tasks, "count", 302f, 8f, 44f, 14f, s);
            AssertRow(tasks, "rule", 14f, 27f, 332f, 1f, s);
            AssertRow(tasks, "body", 14f, 30f, 332f, 74f, s);
            AssertRow(tasks, "hint", 14f, 90f, 332f, 14f, s);

            // The line the map authored is the line on the card; the count is how far into the list it has got.
            TutorialState lesson = Driver.World.Tutorial;
            Assert.AreEqual("TUTORIAL", tasks.Find("title").GetComponent<TMPro.TMP_Text>().text,
                "the card names the lesson");
            Assert.AreEqual("0 / " + lesson.Count,
                tasks.Find("count").GetComponent<TMPro.TMP_Text>().text, "and counts the steps");
            StringAssert.Contains(_campaign.Maps[2].TutorialTasks[0].Text,
                tasks.Find("body").GetComponent<TMPro.TMP_Text>().text, "and prints the line being taught");
            Assert.IsFalse(tasks.Find("hint").gameObject.activeSelf,
                "the hint is the card's second voice and stays quiet until the line has stood");

            // The fixture severs the real devices, so the keyboard it hands back is the one the key goes through.
            Transform tutorial = canvas.Find("tutorial");
            Transform hide = tutorial.Find("hide");
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();   // the HUD's event system binds a pointer
                yield return null;

                input.Press(keyboard.tKey);
                yield return AssertWithinFrames(() => tutorial.gameObject.activeSelf,
                    "T hands the corner to the reference card");
                Assert.IsFalse(tasks.gameObject.activeSelf, "and the lesson's card stands down");

                Assert.AreEqual("TASKS  T", hide.Find("hide.label").GetComponent<TMPro.TMP_Text>().text,
                    "the hide button says where the same key goes back to");

                input.Release(keyboard.tKey);
                yield return null;

                // The button itself hands the corner back, one deliberate press - and the press must not take
                // the card down inside its own dispatch.
                var press = new PointerEventData(EventSystem.current);
                ExecuteEvents.ExecuteHierarchy(hide.gameObject, press, ExecuteEvents.pointerDownHandler);
                yield return AssertWithinFrames(() => tasks.gameObject.activeSelf,
                    "the button gives the corner back to the lesson");
                Assert.IsFalse(tutorial.gameObject.activeSelf, "and takes its own card away");
            }
            finally
            {
                input.TearDown();
            }
        }

        /// <summary>The controls cluster is a prefab (<c>Resources/Hud/HudControls</c>), and each button's left
        /// edge is the sum of the widths and gaps before it - which is what proves the asset still agrees with
        /// the layout.</summary>
        [UnityTest]
        public IEnumerator TheHudControlsCluster_IsThePrefabAtTheLayoutsRect()
        {
            yield return null;
            BeginRun();
            yield return null;   // the rebuild a map change asks for lands on the next frame

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Assert.IsNotNull(hud, "the driver builds the HUD");
            Transform canvas = hud.transform.Find("HudCanvas");
            Assert.IsNotNull(canvas, "the HUD builds its canvas");

            var controls = canvas.Find("controls") as RectTransform;
            Assert.IsNotNull(controls, "the cluster is the prefab's own 'controls' node");

            float s = HudLayout.Scale(Screen.height);
            Rect laid = HudLayout.Controls(Screen.width, s);
            Assert.AreEqual(laid.width, controls.sizeDelta.x, 0.5f, "the cluster is as wide as the layout says");
            Assert.AreEqual(laid.height, controls.sizeDelta.y, 0.5f, "and as tall");
            Assert.AreEqual(-(Screen.width - laid.xMax), controls.anchoredPosition.x, 0.5f,
                "hung off the screen's top-right corner");
            Assert.AreEqual(-laid.y, controls.anchoredPosition.y, 0.5f, "one margin down");

            float gap = HudLayout.ControlGap * s;
            AssertControlLeft(controls, "wave", 0f);
            AssertControlLeft(controls, "pause", HudLayout.WaveButtonWidth * s + gap);
            AssertControlLeft(controls, "gear",
                (HudLayout.WaveButtonWidth + HudLayout.ControlsHeight) * s + 2f * gap);
            AssertControlLeft(controls, "menu",
                (HudLayout.WaveButtonWidth + 2f * HudLayout.ControlsHeight) * s + 3f * gap);
            AssertControlLeft(controls, "help", (HudLayout.ControlsWidth - HudLayout.ControlsHeight) * s);

            // Wired as well as placed: a node renamed in the asset fails here, not on a player's click.
            foreach (string name in new[] { "wave", "pause", "gear", "menu", "help" })
            {
                Transform node = controls.Find(name);
                Assert.IsNotNull(node, "the prefab carries '" + name + "'");
                var button = node.GetComponent<HudButton>();
                Assert.IsNotNull(button, "'" + name + "' answers the pointer");
                Assert.IsNotNull(button.Fill, "'" + name + "' has its surface");
                Assert.IsNotNull(button.Label, "'" + name + "' has its words");
                Assert.IsTrue(button.Fill.Image.raycastTarget, "'" + name + "' takes the click");
            }

            // The pause glyph is the asset's too, at its one design size.
            var bars = controls.Find("pause/bars") as RectTransform;
            Assert.IsNotNull(bars, "the pause button draws its glyph rather than typing it");
            Assert.AreEqual(10f * s, bars.sizeDelta.x, 0.5f, "the glyph is its design size");
            Assert.AreEqual(12f * s, bars.sizeDelta.y, 0.5f, "and its design height");
        }

        /// <summary>The build bar is a prefab (<c>Resources/Hud/HudBar</c>) and so is a key (<c>Resources/Hud/HudBarTile</c>).
        /// The bar's length is the run's, so this reads both ends: the full table, and the strip a lesson opens.</summary>
        [UnityTest]
        public IEnumerator TheHudBuildBar_IsOneKeyAsset_AndItsStripFollowsTheRun()
        {
            yield return null;
            BeginRun();
            yield return null;   // the rebuild a map change asks for lands on the next frame

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");
            var bar = canvas.Find("bar") as RectTransform;
            Assert.IsNotNull(bar, "the bar is the prefab's own 'bar' node");

            float s = HudLayout.Scale(Screen.height);

            // Counted from the kind's own gate, not the bar's list, so a bar that dropped or reordered a key
            // cannot agree with this by accident.
            var expected = new System.Collections.Generic.List<BuildKind>();
            foreach (BuildKind kind in driver.Content.BuildKinds)
                if (driver.World.IsUnlocked(kind)) expected.Add(kind);

            // The row the keys sit in is the one place inside the bar that is the asset's rather than the run's.
            var row = bar.Find("tiles") as RectTransform;
            Assert.IsNotNull(row, "the card carries the row its keys sit in");
            AssertRow(bar, "tiles", HudLayout.BarPad, 6f, 0f, 0f, s);

            // The fixture's first map hands everything over, so every building has a key on the strip.
            Assert.AreEqual(expected.Count, KeyCount(row), "one key per building this run offers");
            Assert.AreEqual(HudLayout.BarTiles, expected.Count,
                "the strip is authored for the table it ships with");

            Rect laid = HudLayout.Bar(Screen.width, Screen.height, s, expected.Count);
            Assert.AreEqual(laid.width, bar.sizeDelta.x, 0.5f, "the card is the width the layout names");
            Assert.AreEqual(laid.height, bar.sizeDelta.y, 0.5f, "and its height");
            Assert.AreEqual(laid.x, bar.anchoredPosition.x, 0.5f, "centred along the bottom");
            Assert.AreEqual(Screen.height - laid.yMax, bar.anchoredPosition.y, 0.5f,
                "one margin up from the screen's bottom edge");

            // One key at the row's origin, 66.75 by 40, and the next one key plus its air on.
            var key = row.Find("tile0") as RectTransform;
            Assert.IsNotNull(key, "the row carries the first key");
            AssertRow(row, "tile0", 0f, 0f, HudLayout.BarTileWidth, 40f, s);
            AssertRow(row, "tile1", HudLayout.BarTileWidth + HudLayout.BarGap, 0f,
                HudLayout.BarTileWidth, 40f, s);

            // The key's pieces in design units.
            AssertRow(key, "name", 6f, 3f, HudLayout.BarTileWidth - 12f, 16f, s);
            AssertRow(key, "keycap", 6f, 21f, 16f, 14f, s);
            AssertRow(key, "hotkey", 6f, 21f, 16f, 14f, s);
            AssertRow(key, "cost", 24f, 21f, HudLayout.BarTileWidth - 30f, 14f, s);

            // The mark's square is the asset's, but where it stands is measured against the number's own width on
            // the first update (PlaceCostIcon), so three digits push it left and four keep it in.
            var markRect = key.Find("costIcon") as RectTransform;
            Assert.IsNotNull(markRect, "the key carries the price's mark");
            Assert.AreEqual(9f * s, markRect.sizeDelta.x, 0.5f, "the mark is its design size");
            Assert.AreEqual(9f * s, markRect.sizeDelta.y, 0.5f, "and square");
            Assert.Greater(markRect.anchoredPosition.x, 24f * s, "and stands clear of the keycap");
            Assert.Less(markRect.anchoredPosition.x, (HudLayout.BarTileWidth - 6f) * s,
                "inside the key's right inset");

            // The afford line's pivot is the key's lower-left corner, so its units measure upward from the floor.
            var afford = key.Find("afford") as RectTransform;
            Assert.IsNotNull(afford, "the key carries the line the bank's share is drawn on");
            Assert.AreEqual(6f * s, afford.anchoredPosition.x, 0.5f, "one inset in");
            Assert.AreEqual(2f * s, afford.anchoredPosition.y, 0.5f, "two units up from the floor");
            Assert.AreEqual((HudLayout.BarTileWidth - 12f) * s, afford.sizeDelta.x, 0.5f,
                "and the key's inner width, which is the length the fraction measures against");

            // The words on the keys are the machine's, key by key and in the table's order.
            for (int i = 0; i < expected.Count; i++)
            {
                Transform keyI = row.Find("tile" + i);
                Assert.IsNotNull(keyI, "the run's kind " + i + " has a key");

                MachineDef machine = driver.Content.Machine(expected[i]);
                Assert.AreEqual(machine.Name, LabelText(keyI, "name"), "key " + i + " names its building");
                Assert.AreEqual(BuildInput.HotkeyLabel((int)expected[i]), LabelText(keyI, "hotkey"),
                    "key " + i + " says the key that presses it");
                Assert.AreEqual(machine.Cost.ToString(), LabelText(keyI, "cost"), "key " + i + " prices it");
            }

            var button = key.GetComponent<HudButton>();
            Assert.IsNotNull(button, "the key answers the pointer");
            Assert.IsNotNull(button.Fill, "the key has its surface");
            Assert.IsTrue(button.Fill.Image.raycastTarget, "and takes the click");

            // The atlas is rasterised at runtime, so the binder adopts the node and paints it; a bare texture is a
            // mark the bar forgot to paint.
            var mark = markRect.GetComponent<RawImage>();
            Assert.IsNotNull(mark, "the price's mark is a picture");
            Assert.IsNotNull(mark.texture, "and the bar painted it");

            // The selection is latched by the input and applied by the next tick.
            var press = new PointerEventData(EventSystem.current);
            ExecuteEvents.ExecuteHierarchy(key.gameObject, press, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(driver.World.SelectedKind.HasValue, "a click selects the kind the key names");
            Assert.AreEqual(expected[0], driver.World.SelectedKind.Value, "and it is that key's own kind");

            // The strip follows the run: a lesson that handed over one building gets one key, not a full shelf.
            Driver.PlayMap(2, false);

            // The old bar is torn down with Destroy, which lands at the end of a frame, so for a frame or two
            // canvas.Find("bar") can still answer with the old card and its nine keys. Wait for the rebuild to
            // settle rather than for one frame - the assertions below are unchanged.
            for (int frame = 0; frame < 10; frame++)
            {
                yield return null;
                var settling = canvas.Find("bar/tiles") as RectTransform;
                if (settling != null && KeyCount(settling) == 1) break;
            }

            var lessonRow = canvas.Find("bar/tiles") as RectTransform;
            Assert.IsNotNull(lessonRow, "the lesson's bar is the same card and the same row");
            Assert.AreEqual(1, KeyCount(lessonRow), "a run of one building gets one key");
            Assert.IsNull(lessonRow.Find("tile1"), "and no key the run has not handed over");

            Rect narrow = HudLayout.Bar(Screen.width, Screen.height, s, 1);
            Assert.AreEqual(narrow.width, canvas.Find("bar").GetComponent<RectTransform>().sizeDelta.x,
                0.5f, "the card is only as wide as the keys it holds");
            AssertRow(lessonRow, "tile0", 0f, 0f, HudLayout.BarTileWidth, 40f, s);
        }

        /// <summary>The info card is a prefab (<c>Resources/Hud/HudInfo</c>): one surface, two faces, and the
        /// sorter's picker band - the one thing that makes it taller, and the six rows are the one thing that
        /// moves when it does.</summary>
        [UnityTest]
        public IEnumerator TheHudInfoCard_IsOneAsset_WhosePickerBandGrowsIt()
        {
            yield return null;
            BeginRun();
            yield return null;   // the rebuild a map change asks for lands on the next frame
            yield return null;   // and the card's faces are dressed on the one after it

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");
            var info = canvas.Find("info") as RectTransform;
            Assert.IsNotNull(info, "the info card is the prefab's own 'info' node");

            float s = HudLayout.Scale(Screen.height);
            var build = (RectTransform)info.Find("build");
            var inspect = (RectTransform)info.Find("inspect");
            Assert.IsNotNull(build, "the card carries the face that describes what can be built");
            Assert.IsNotNull(inspect, "and the face that reads what is on the map");

            // With nothing selected the read face is up; the run only says which face is showing.
            Assert.IsFalse(build.gameObject.activeSelf, "nothing is selected, so the build face is down");
            Assert.IsTrue(inspect.gameObject.activeSelf, "and the read face is up");

            Rect laid = HudLayout.Info(Screen.width, Screen.height, s);
            Assert.AreEqual(laid.width, info.sizeDelta.x, 0.5f, "the card is the layout's width");
            Assert.AreEqual(laid.height, info.sizeDelta.y, 0.5f, "and its base height, with no band on it");
            Assert.AreEqual(-(Screen.width - laid.xMax), info.anchoredPosition.x, 0.5f,
                "floated off the screen's right edge");
            Assert.AreEqual(Screen.height - laid.yMax, info.anchoredPosition.y, 0.5f, "above the bar");

            // The build face's grid.
            AssertRow(build, "title", 14f, 4f, 142f, 20f, s);
            AssertRow(build, "right", 14f, 6f, 272f, 16f, s);
            AssertRow(build, "rule", 14f, 27f, 272f, 1f, s);
            AssertRow(build, "blurb", 14f, 31f, 272f, 48f, s);
            AssertRow(build, "stats", 14f, 82f, 272f, 22f, s);
            AssertRow(build, "facing", 14f, 108f, 120f, 14f, s);

            // The three actions along the bottom edge, hung off the card's right corner.
            AssertRight(build, "turnL", 98f, 112f, 24f, 20f, s);
            AssertRight(build, "turnR", 70f, 112f, 24f, 20f, s);
            AssertRight(build, "inspect", 14f, 112f, 52f, 20f, s);
            foreach (string name in new[] { "turnL", "turnR", "inspect" })
            {
                var button = build.Find(name).GetComponent<HudButton>();
                Assert.IsNotNull(button, "'" + name + "' answers the pointer");
                Assert.IsNotNull(button.Label, "'" + name + "' has its words");
                Assert.IsTrue(button.Fill.Image.raycastTarget, "'" + name + "' takes the click");
            }

            Assert.AreEqual("<", LabelText(build, "turnL/turnL.label"),
                "the turn buttons say which way they turn");
            Assert.AreEqual(">", LabelText(build, "turnR/turnR.label"));
            Assert.AreEqual("READ", LabelText(build, "inspect/inspect.label"), "and the third says what it opens");

            // The read face's six rows are the asset's grid; an empty card prints two of them, lower than a
            // fact's first row since nothing sits under them.
            for (int i = 0; i < 6; i++)
                AssertRow(inspect, "row" + i, 14f, 33f + 25f + 15f * i, 272f, 14f, s);

            Assert.IsTrue(inspect.Find("row0").gameObject.activeSelf, "the empty card says what it reads");
            Assert.IsTrue(inspect.Find("row1").gameObject.activeSelf, "and how to leave the cursor");
            Assert.IsFalse(inspect.Find("row5").gameObject.activeSelf, "and prints nothing else");

            var picker = (RectTransform)inspect.Find("picker");
            Assert.IsNotNull(picker, "the card carries the sorter's picker band");
            Assert.IsFalse(picker.gameObject.activeSelf, "which is down while the card reads ground");

            // Now a sorter: the one machine whose card grows, because its routing rule is the player's and the
            // picker is where it is set. One frame for the card to see the pin.
            var cell = new Int2(21, 10);
            SimWorld world = driver.World;
            Assert.IsTrue(world.TryPlace(BuildKind.Sorter, cell, Dir.East), "the fixture's map takes a sorter");

            // The pin is input: the same command the cursor's click sends.
            world.Tick(new InputCommand(cursorCell: cell, inspectPressed: true));
            yield return null;

            Assert.IsTrue(picker.gameObject.activeSelf, "a sorter's card wears the picker");
            Rect grown = HudLayout.Info(Screen.width, Screen.height, s, true);
            Assert.Greater(info.sizeDelta.y, grown.height - 0.5f,
                "the card grows by at least the band's height");
            Assert.Greater(grown.height, laid.height, "which is taller than the base card");

            // The note under the band wraps, so the card grows again by the lines it took - and it is the
            // layout's height, not the asset's, that says where the card ends.
            Assert.Greater(hud.InfoExtra, 0f, "and the rows that wrap grow it further");
            Rect wrapped = HudLayout.Info(Screen.width, Screen.height, s, true, hud.InfoExtra);
            Assert.AreEqual(wrapped.height, info.sizeDelta.y, 0.5f,
                "the card is the band plus the rows it wrapped");
            Assert.AreEqual(-(Screen.width - wrapped.xMax), info.anchoredPosition.x, 0.5f,
                "and grows upward, still on the right edge");

            // The rows still start under the band.
            AssertRow(inspect, "row0", 14f, 33f + HudLayout.InfoPickHeight, 272f, 14f, s);

            // The band in design units: the quartered canvas, the mode toggle beside it, the sentence under both.
            AssertRow(inspect, "picker", 0f, 32f, 300f, HudLayout.InfoPickHeight, s);
            AssertRow(picker, "title", 12f, 0f, 120f, 12f, s);
            var pickCanvas = (RectTransform)picker.Find("canvas");
            AssertRow(picker, "canvas", 12f, 13f, 44f, 44f, s);
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                // North is up: the cell a quadrant is drawn in is the cell the pointer presses.
                float x = quadrant == Shape.NW || quadrant == Shape.SW ? 0f : 22f;
                float y = quadrant == Shape.NW || quadrant == Shape.NE ? 0f : 22f;
                AssertRow(pickCanvas, "ghost" + quadrant, x, y, 22f, 22f, s);
                AssertRow(pickCanvas, "pick" + quadrant, x, y, 22f, 22f, s);
                Assert.IsTrue(pickCanvas.Find("pick" + quadrant).GetComponent<HudButton>().Fill.Image
                    .raycastTarget, "quadrant " + quadrant + " takes the click");
            }

            AssertRow(picker, "mode", 64f, 13f, 224f, 18f, s);
            AssertRow(picker, "caption", 64f, 34f, 224f, 12f, s);
            Assert.AreEqual("ROUTES", LabelText(picker, "title"), "the band names itself");

            // The atlas is rasterised at runtime, so the asset carries the node and the card paints it. The click
            // sends a rule, the next tick applies it, the card reads it back.
            var figure = pickCanvas.Find("figure").GetComponent<RawImage>();
            Assert.IsNotNull(figure, "the canvas carries one picture over its cells");

            Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot before), "the sorter is there");
            var press = new PointerEventData(EventSystem.current);
            ExecuteEvents.ExecuteHierarchy(pickCanvas.Find("pick" + Shape.NW).gameObject, press,
                ExecuteEvents.pointerDownHandler);
            yield return new WaitForSeconds(0.1f);

            Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot after), "still there");

            // A quadrant cycles that quadrant's shape (an exact-item filter) or the mineral itself (what a fresh
            // sorter ships). The mode toggle is the other button, so the click kept the mode it could not change.
            Assert.AreEqual(before.Filter.Mode, after.Filter.Mode, "a quadrant click keeps the sorter's mode");
            Assert.AreNotEqual(before.Filter.Describe(), after.Filter.Describe(),
                "and the rule the machine routes changed");
            Assert.IsNotNull(figure.texture, "the card paints the item the machine now routes");
        }

        /// <summary>The read card's rows are measured, not a fixed grid: the cutter's facing note is half again
        /// the 272-unit row, so it wraps, the rows under it move down, and the card grows upward by the lines it
        /// took.</summary>
        [UnityTest]
        public IEnumerator TheInfoCard_WrapsItsLongRows_AndGrowsRatherThanPrintingPastItsEdge()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            SimWorld world = driver.World;

            // The cutter is the lesson's to hand over on the campaign's first map. This is a card test, so the
            // lesson steps aside and the whole table is the run's.
            world.Tutorial.Skip();

            Int2 cell = default;
            bool placed = false;
            for (int y = 1; y < world.TileGrid.Height - 1 && !placed; y++)
            {
                for (int x = 1; x < world.TileGrid.Width - 1 && !placed; x++)
                {
                    cell = new Int2(x, y);
                    placed = world.TryPlace(BuildKind.Cutter, cell, Dir.East);
                }
            }

            Assert.IsTrue(placed, "the fixture's map has room for a cutter");
            world.Tick(new InputCommand(cursorCell: cell, inspectPressed: true));
            yield return null;
            yield return null;

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            var card = hud.transform.Find("HudCanvas/info") as RectTransform;
            var inspect = (RectTransform)card.Find("inspect");
            Assert.IsTrue(inspect.gameObject.activeSelf, "the pin is read by the read face");

            float s = HudLayout.Scale(Screen.height);

            // The facing note wraps, so the stack is taller than the card's body and the card grows by that much.
            Assert.Greater(hud.InfoExtra, 0f, "a long sentence wraps, and the card grows to hold it");
            Rect laid = HudLayout.Info(Screen.width, Screen.height, s, false, hud.InfoExtra);
            Assert.AreEqual(laid.height, card.sizeDelta.y, 0.5f,
                "the card is the layout's height, extra and all");
            Assert.Greater(card.sizeDelta.y, HudLayout.InfoHeight * s, "and taller than the base card");

            // Every visible row is inside the card: tall enough for the lines it took, the last one above the foot.
            float bottom = 0f;
            for (int i = 0; i < 6; i++)
            {
                var row = (RectTransform)inspect.Find("row" + i);
                if (!row.gameObject.activeSelf) continue;

                var text = row.GetComponent<TMPro.TMP_Text>();
                Assert.LessOrEqual(text.GetPreferredValues(text.text, row.rect.width, 0f).y,
                    row.rect.height + 1.5f, "'" + row.name + "' is tall enough for its wrapped lines");
                bottom = -row.anchoredPosition.y + row.rect.height;
            }

            Assert.LessOrEqual(bottom, card.sizeDelta.y - 12f * s + 0.5f,
                "and the last line ends inside the card, above its foot");
        }

        /// <summary>The bank is a prefab (<c>Resources/Hud/HudStockpile</c>): a pill, the shape it counts, the
        /// number. The mark stands left, so the number grows rightwards without touching it (issue #12) - which is
        /// what lets its geometry be read against the layout rather than against a picture.</summary>
        [UnityTest]
        public IEnumerator TheHudStockpile_IsThePrefabAtTheLayoutsCorner()
        {
            yield return null;
            BeginRun();
            yield return null;

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            var panel = hud.transform.Find("HudCanvas/stockpile") as RectTransform;
            Assert.IsNotNull(panel, "the bank is the prefab's own 'stockpile' node");

            float s = HudLayout.Scale(Screen.height);
            Rect laid = HudLayout.Stockpile(Screen.width, s);
            Assert.AreEqual(laid.width, panel.sizeDelta.x, 0.5f, "the bank is the width the layout names");
            Assert.AreEqual(laid.height, panel.sizeDelta.y, 0.5f, "and the height");
            Assert.AreEqual(laid.x, panel.anchoredPosition.x, 0.5f, "at the layout's left edge");
            Assert.AreEqual(-laid.y, panel.anchoredPosition.y, 0.5f, "and its one margin down");

            // The coin at the demand figure's size, centred on the pill; the number six units to its right.
            AssertRow(panel, "icon", 12f, (HudLayout.StockpileHeight - HudLayout.DemandFigure) * 0.5f,
                HudLayout.DemandFigure, HudLayout.DemandFigure, s);
            AssertRow(panel, "number", 12f + HudLayout.DemandFigure + 6f, 2f,
                HudLayout.StockpileWidth - (12f + HudLayout.DemandFigure + 12f), 40f, s);

            // The atlas is rasterised at runtime, so the binder paints the map's mineral onto an empty picture;
            // a mark the palette cannot draw leaves the number bare rather than showing a white square.
            var mark = panel.Find("icon").GetComponent<RawImage>();
            Assert.IsNotNull(mark, "the coin is a picture");
            Assert.IsNotNull(mark.texture, "and the binder painted it");
            Assert.AreEqual(mark.texture != null, mark.gameObject.activeSelf,
                "the mark is shown exactly when there is one to show");
            Assert.AreEqual(driver.World.Economy.Shapes.ToString(), LabelText(panel, "number"),
                "the number is the bank's own");
        }

        /// <summary>The banner is a prefab (<c>Resources/Hud/HudBanner</c>): the run's one unmissable sentence, on a
        /// card the layout does not size - it is an overlay, off the screen's 22 % budget.</summary>
        [UnityTest]
        public IEnumerator TheHudBanner_IsThePrefabCentred_AndSaysWhyTheRunStopped()
        {
            yield return null;
            BeginRun();
            yield return null;

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            var banner = hud.transform.Find("HudCanvas/banner") as RectTransform;
            Assert.IsNotNull(banner, "the banner is the prefab's own 'banner' node");
            Assert.IsFalse(banner.gameObject.activeSelf, "and it waits for something to say");

            // Nothing on the HUD opens a screen, so a paused run is one card and one line.
            driver.World.Paused = true;
            yield return null;

            float s = HudLayout.Scale(Screen.height);
            Assert.IsTrue(banner.gameObject.activeSelf, "a paused run says so");

            // 560 by 170, centred, its top edge on the overlay's own 42 % band - which is why it is the one card
            // the layout does not place.
            Assert.AreEqual(560f * s, banner.sizeDelta.x, 0.5f, "the banner is its design width");
            Assert.AreEqual(170f * s, banner.sizeDelta.y, 0.5f, "and its design height");
            Assert.AreEqual(0f, banner.anchoredPosition.x, 0.5f, "centred on the screen");
            Assert.AreEqual(-(Screen.height - 170f * s) * 0.42f, banner.anchoredPosition.y, 0.5f,
                "with its top edge on the overlay's own 42 % band");

            // The line fills the card: 18 off the top and 136 tall, so its box ends 16 above the floor.
            var label = (RectTransform)banner.Find("label");
            Assert.AreEqual(0f, label.anchoredPosition.x, 0.5f, "the line spans the card");
            Assert.AreEqual(-86f * s, label.anchoredPosition.y, 0.5f, "18 down and half of its own 136");
            Assert.AreEqual(136f * s, label.sizeDelta.y, 0.5f, "the line is its design height");

            string words = LabelText(banner, "label");
            StringAssert.StartsWith("PAUSED", words, "the banner names the state the run is in");
            StringAssert.Contains("Space to resume", words, "and the key that leaves it");
        }

        /// <summary>The toast is a prefab (<c>Resources/Hud/HudToast</c>): the strip of news under the top cards. Its
        /// size follows its sentence, so the one thing the asset can say about the words is where they start and
        /// end - which is why its line is an inset.</summary>
        [UnityTest]
        public IEnumerator TheHudToast_IsThePrefab_AndItsLineIsAnInset()
        {
            yield return null;
            BeginRun();
            yield return null;

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            var toast = hud.transform.Find("HudCanvas/toast") as RectTransform;
            Assert.IsNotNull(toast, "the toast is the prefab's own 'toast' node");
            Assert.IsFalse(toast.gameObject.activeSelf, "and it starts hidden");

            // A refused hotkey is the driver's own path into the strip: the lesson answers it in one sentence.
            hud.NoticeLocked(BuildKind.Cutter);
            yield return null;

            float s = HudLayout.Scale(Screen.height);
            Assert.IsTrue(toast.gameObject.activeSelf, "a notice puts the strip up");

            Transform line = toast.Find("label");
            Assert.IsNotNull(line, "the strip carries its one line");
            var text = line.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual(0f, text.rectTransform.anchoredPosition.x, 0.5f, "the line is inset symmetrically");
            Assert.AreEqual(-28f * s, text.rectTransform.sizeDelta.x, 0.5f, "14 units off each side");
            Assert.AreEqual(-16f * s, text.rectTransform.sizeDelta.y, 0.5f, "8 off the top and the bottom");

            // The strip is its words plus the insets: no design number sizes it.
            Assert.AreEqual(Mathf.Min(text.preferredWidth + 28f * s, Screen.width - 24f * s),
                toast.sizeDelta.x, 0.5f, "the strip is as wide as its sentence and its padding");
            Assert.AreEqual(text.preferredHeight + 16f * s, toast.sizeDelta.y, 0.5f,
                "and as tall as one line and its two insets");
            Assert.AreEqual(0f, toast.anchoredPosition.x, 0.5f, "centred under the top cards");

            MachineDef def = driver.Content.Machine(BuildKind.Cutter);
            string words = LabelText(toast, "label");
            StringAssert.Contains("NOT YET", words, "the strip says what the press did not do");
            StringAssert.Contains(def.Name, words, "and names the building the lesson is keeping");

            // The fade is the strip's one CanvasGroup, not an alpha painted into each of its three surfaces as
            // well: a half-faded strip would otherwise read quarter-strength. Wind the notice into its fade.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            float seconds = (float)typeof(HudView).GetField("_noticeFor", flags).GetValue(hud);
            typeof(HudView).GetField("_noticeFrom", flags).SetValue(hud, Time.unscaledTime - seconds * 0.95f);
            yield return null;

            var group = toast.GetComponent<CanvasGroup>();
            Assert.Less(group.alpha, 0.5f, "the strip fades as one piece");
            Assert.AreEqual(1f, text.color.a, 0.001f, "and its ink is not faded a second time");
        }

        /// <summary>The tooltip is a prefab (<c>Resources/Hud/HudTooltip</c>): a card, a title and three lines - the
        /// one panel sized to its own words, so this reads the design units the words sit at and the box they grow
        /// into.</summary>
        [UnityTest]
        public IEnumerator TheHudTooltip_IsThePrefab_AndDescribesWhatThePointerIsOn()
        {
            yield return null;
            BeginRun();
            yield return null;

            var driver = Driver;
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");
            var tooltip = canvas.Find("tooltip") as RectTransform;
            Assert.IsNotNull(tooltip, "the tooltip is the prefab's own 'tooltip' node");
            Assert.IsFalse(tooltip.gameObject.activeSelf, "and it starts hidden - the pointer is not on the map");

            float s = HudLayout.Scale(Screen.height);

            // A machine under the cursor, through the cursor's own door.
            var cell = new Int2(21, 10);
            SimWorld world = driver.World;
            Assert.IsTrue(world.TryPlace(BuildKind.Sorter, cell, Dir.East), "the fixture's map takes a sorter");

            // A test cannot put the mouse on a cell, so this takes UpdateTooltip's two steps by reflection: fill
            // from the cell, then place beside the pointer.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object filled = hud.GetType().GetMethod("FillTooltip", flags).Invoke(hud, new object[] { cell });
            Assert.AreEqual(true, filled, "a machine's cell has something to say");
            tooltip.gameObject.SetActive(true);

            var pointer = new Vector2(400f, 300f);
            hud.GetType().GetMethod("PositionTooltip", flags).Invoke(hud, new object[] { pointer });

            // The title's band, then one line every 14 design units.
            AssertRow(tooltip, "title", 10f, 5f, 200f, 16f, s);
            for (int i = 0; i < 3; i++)
                AssertRow(tooltip, "row" + i, 10f, 23f + 14f * i, 200f, 13f, s);

            // The words are the run's: two lines for a sorter just built, with nothing under it.
            MachineDef def = driver.Content.Machine(BuildKind.Sorter);
            Assert.AreEqual(def.Name, LabelText(tooltip, "title"), "the title names what is under the pointer");
            Assert.IsTrue(tooltip.Find("row0").gameObject.activeSelf, "the first line says how it joins the line");
            Assert.IsTrue(tooltip.Find("row1").gameObject.activeSelf, "and the second what it is doing");
            Assert.IsFalse(tooltip.Find("row2").gameObject.activeSelf, "the card prints no empty line");

            // The box is the widest line plus 20 units of air: the lines are a fixed width, the box is not.
            var title = tooltip.Find("title").GetComponent<TMPro.TMP_Text>();
            float widest = title.preferredWidth;
            for (int i = 0; i < 2; i++)
                widest = Mathf.Max(widest, tooltip.Find("row" + i).GetComponent<TMPro.TMP_Text>().preferredWidth);

            Assert.AreEqual(Mathf.Min(widest + 20f * s, Screen.width - 16f), tooltip.sizeDelta.x, 0.5f,
                "the box is the widest line and its air");
            Assert.AreEqual((20f + 14f * 2 + 6f) * s, tooltip.sizeDelta.y, 0.5f,
                "and one band plus its two lines");

            // Beside the pointer, over its right shoulder; the 16-pixel step is the pointer's, not the HUD's.
            Assert.AreEqual(pointer.x + 16f, tooltip.anchoredPosition.x, 0.5f, "the box opens beside the pointer");
            Assert.AreEqual(-(Screen.height - pointer.y - 16f), tooltip.anchoredPosition.y, 0.5f,
                "and just under the top of it");

            // At the bottom-right corner the box flips left and lifts above the pointer rather than printing off
            // the screen - the corner the read cursor works in, just above the bar.
            hud.GetType().GetMethod("PositionTooltip", flags).Invoke(hud, new object[] { new Vector2(Screen.width - 4f, 4f) });
            float top = -tooltip.anchoredPosition.y;
            Assert.GreaterOrEqual(tooltip.anchoredPosition.x, 8f, "the box is held inside the left edge");
            Assert.LessOrEqual(tooltip.anchoredPosition.x + tooltip.sizeDelta.x, Screen.width - 8f + 0.5f,
                "and inside the right one");
            Assert.GreaterOrEqual(top, 8f, "the box is held inside the top edge");
            Assert.LessOrEqual(top + tooltip.sizeDelta.y, Screen.height - 8f + 0.5f,
                "and inside the bottom one");
        }

        /// <summary>The help card is a prefab (<c>Resources/Hud/HudHelp</c>) and the one panel whose row count is
        /// code's: one row per <see cref="HudView.KeyLines"/> entry. A line added without a row fails here, and a row
        /// the list no longer fills fails as an extra.</summary>
        [UnityTest]
        public IEnumerator TheHudHelpCard_IsThePrefab_AndPrintsEveryKeyLine()
        {
            yield return null;
            BeginRun();
            yield return null;

            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Transform canvas = hud.transform.Find("HudCanvas");
            var help = canvas.Find("help") as RectTransform;
            Assert.IsNotNull(help, "the card is the prefab's own 'help' node");
            Assert.IsFalse(help.gameObject.activeSelf, "and it starts closed");

            float s = HudLayout.Scale(Screen.height);
            string[] lines = HudView.KeyLines();
            Assert.AreEqual(HudLayout.HelpHeightFor(lines.Length) * s, help.sizeDelta.y, 0.5f,
                "the card is as tall as the list it prints");
            Assert.AreEqual(HudLayout.HelpWidth * s, help.sizeDelta.x, 0.5f, "and the width of the card it is");

            Assert.IsNotNull(help.Find("header"), "the card names itself");
            Assert.IsNotNull(help.Find("rule"), "and rules a line under the name");
            for (int i = 0; i < lines.Length; i++)
            {
                Transform row = help.Find("key" + i);
                Assert.IsNotNull(row, "the card carries row " + i);
                var text = row.GetComponent<TMPro.TMP_Text>();
                Assert.IsNotNull(text, "row " + i + " is a label");
                Assert.AreEqual(lines[i], text.text, "row " + i + " prints its line");

                // The rows sit on the step HelpHeightFor adds up: a row moved in the asset moves the button under
                // it in the code, and the two stop agreeing here.
                var rect = (RectTransform)row;
                Assert.AreEqual(-(HudLayout.HelpLineTop + i * HudLayout.HelpLineStep) * s, rect.anchoredPosition.y,
                    0.5f, "row " + i + " is on the layout's line step");
            }

            Assert.IsNull(help.Find("key" + lines.Length), "the card carries no row the list does not fill");

            // Restart is the card's one deliberate click, one card away from the cluster for exactly that.
            foreach (string name in new[] { "restart", "close" })
            {
                Transform node = help.Find(name);
                Assert.IsNotNull(node, "the card carries '" + name + "'");
                var button = node.GetComponent<HudButton>();
                Assert.IsNotNull(button, "'" + name + "' answers the pointer");
                Assert.IsNotNull(button.Label, "'" + name + "' has its words");
            }
        }

        /// <summary>The shell's help card is its own prefab (<c>Resources/Hud/ShellHelp</c>): the same keyboard and
        /// height, wider, and no restart - a menu is not a run. Reached through the pause menu's door, which must not
        /// tear the card down inside the press's dispatch.</summary>
        [UnityTest]
        public IEnumerator TheShellsHelpCard_IsThePrefab_AndClosesBackToThePauseMenu()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            Assert.IsNotNull(shell, "the driver builds the shell");

            driver.OpenMenu();
            yield return null;
            Assert.IsTrue(Press(shell, "pause/card/howto"), "the door survives its own press");
            yield return null;   // the rebuild the press asked for lands on the next frame

            var card = shell.transform.Find("ShellCanvas/help") as RectTransform;
            Assert.IsNotNull(card, "the card is the prefab's own 'help' node");

            float s = HudLayout.Scale(Screen.height);
            string[] lines = HudView.KeyLines();
            Assert.AreEqual(HudLayout.HelpHeightFor(lines.Length) * s, card.sizeDelta.y, 0.5f,
                "the card is as tall as the run's");
            Assert.AreEqual(300f * s, card.sizeDelta.x, 0.5f, "and wider, because a screen has room for it");

            for (int i = 0; i < lines.Length; i++)
            {
                Transform row = card.Find("key" + i);
                Assert.IsNotNull(row, "the card carries row " + i);
                var text = row.GetComponent<TMPro.TMP_Text>();
                Assert.IsNotNull(text, "row " + i + " is a label");
                Assert.AreEqual(lines[i], text.text, "row " + i + " prints its line");
            }

            Assert.IsNull(card.Find("restart"), "a menu is not a run: there is no restart on this card");
            Assert.IsNotNull(card.Find("close"), "the card has its one way out");

            // One press closes it, back onto the menu it came from rather than out of the run.
            Assert.IsTrue(Press(shell, "help/close"), "the close button survives its own press");
            yield return null;
            Assert.IsTrue(shell.Open, "the shell still stands after its card closes");
            Assert.IsNotNull(shell.transform.Find("ShellCanvas/pause"), "and the menu is the screen it returns to");
        }

        /// <summary>The pause menu's far door leaves the run only on the second press - the first arms it and says
        /// so, so a stray click cannot throw a run away. The abandoned run is held, not ticked.</summary>
        [UnityTest]
        public IEnumerator ThePauseMenu_TakesTwoPresses_ToLeaveTheRunForTheTitle()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            SimWorld run = driver.World;

            driver.OpenMenu();
            yield return null;
            Assert.IsTrue(shell.Open, "the pause menu is up");

            shell.RequestMainMenu();
            yield return null;
            Assert.IsTrue(shell.Open, "the first press only arms the main-menu door");
            Assert.AreEqual(run, driver.World, "and leaves the run exactly where it was");

            shell.RequestMainMenu();
            yield return null;
            Assert.IsTrue(shell.Open, "the title stands where the menu was");
            Assert.IsTrue(shell.AtTitle, "and it is the title, not another pause card");
            Assert.AreEqual(run, driver.World, "the abandoned run is still built behind it");

            int held = driver.World.TickCount;
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(held, driver.World.TickCount, "and it does not tick behind the title");

            driver.PlayMap(0, false);
            yield return null;
            int started = driver.World.TickCount;
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(driver.World.TickCount, started, "play from the title starts a fresh run");
        }

        /// <summary>The restart door starts this map over: the run is reused rather than rebuilt, since a restart
        /// is the world's own reset. The clock is back at zero once the next tick applies the request.</summary>
        [UnityTest]
        public IEnumerator ThePauseMenusRestartDoor_StartsTheRunOver()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            SimWorld run = driver.World;
            var belt = new Int2(20, 10);
            Assert.IsTrue(run.TryPlaceBelt(belt, Dir.East));
            for (int i = 0; i < 10; i++) run.Tick(InputCommand.None);
            Assert.Greater(run.TickCount, 0, "the run has time on it to lose");

            driver.OpenMenu();
            yield return null;
            driver.RestartRun();
            yield return null;                    // let the shell go
            yield return new WaitForSeconds(0.2f); // and let the next tick apply the restart request

            Assert.IsFalse(shell.Open, "the restart closes the menu");
            Assert.AreEqual(run, driver.World, "and reuses the run's own world");
            Assert.IsFalse(run.Belts.Has(belt), "the placed belt is gone with the restart");
            Assert.Less(run.TickCount, 10, "and the clock started over");
        }

        /// <summary>The restart door costs the same run the main-menu door does, so it is armed the same way.</summary>
        [UnityTest]
        public IEnumerator TheRestartDoor_TakesTwoPresses_ToStartTheRunOver()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            SimWorld run = driver.World;
            var belt = new Int2(20, 10);
            Assert.IsTrue(run.TryPlaceBelt(belt, Dir.East));

            driver.OpenMenu();
            yield return null;

            shell.RequestRestart();
            yield return null;
            Assert.IsTrue(shell.Open, "the first press only arms the restart door");
            Assert.IsTrue(run.Belts.Has(belt), "and leaves the factory where it was");

            shell.RequestRestart();
            yield return null;
            yield return new WaitForSeconds(0.2f);   // the next tick applies the restart request

            Assert.IsFalse(shell.Open, "the second press closes the menu");
            Assert.IsFalse(run.Belts.Has(belt), "and starts the run over");
        }

        /// <summary>The keys the driver reads for itself, through a real Input System device: M opens the menu,
        /// Escape hands the run back - or drops the building on the cursor first, so only the next Escape opens the
        /// menu - and an armed door is disarmed by Escape.</summary>
        [UnityTest]
        public IEnumerator TheDriversKeys_OpenTheMenu_AndEscapeDropsASelectionFirst()
        {
            yield return null;
            SimulationDriver driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            Assert.IsTrue(shell.Open, "the boot title stands");
            Assert.IsTrue(shell.AtTitle, "and it is the title");

            // The fixture severs the real devices, so the machine running the tests cannot press anything. The
            // keyboard it does hand back is the one every key below goes through.
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();   // the shell's UI module binds a pointer; leave it one
                yield return null;   // the test keyboard is the current one from this frame on

                input.Press(keyboard.mKey);
                yield return null;
                Assert.IsFalse(shell.AtTitle, "M opens the title's map list");
                input.Release(keyboard.mKey);
                yield return null;

                input.Press(keyboard.escapeKey);
                yield return null;
                Assert.IsTrue(shell.AtTitle, "Escape steps back to the title");
                input.Release(keyboard.escapeKey);
                yield return null;

                input.Press(keyboard.enterKey);
                yield return null;
                Assert.IsFalse(shell.Open, "Enter starts the run");
                input.Release(keyboard.enterKey);
                yield return new WaitForSeconds(0.2f);
                Assert.Greater(driver.World.TickCount, 0, "and the clock is running");

                input.Press(keyboard.mKey);
                yield return null;
                Assert.IsTrue(shell.Open, "M opens the pause menu over the run");
                input.Release(keyboard.mKey);
                yield return null;

                // An armed door is disarmed by Escape, not taken by it.
                shell.RequestRestart();
                Assert.IsTrue(shell.Open, "the restart door is armed over the pause card");
                input.Press(keyboard.escapeKey);
                yield return null;
                Assert.IsTrue(shell.Open, "Escape disarms it rather than leaving");
                input.Release(keyboard.escapeKey);
                yield return null;

                input.Press(keyboard.escapeKey);
                yield return null;
                Assert.IsFalse(shell.Open, "the next Escape resumes the run");
                input.Release(keyboard.escapeKey);
                yield return null;

                // A digit puts a building on the cursor. The selection is latched by the input and applied by
                // the next tick, hence the wait.
                input.Press(keyboard.digit1Key);
                yield return new WaitForSeconds(0.1f);
                Assert.IsNotNull(driver.World.SelectedKind, "a digit selects a building");
                input.Release(keyboard.digit1Key);
                yield return null;

                // Escape, meaning "back", drops it and must not also stop the run: the driver reads the world's
                // selection before the input latch clears it.
                input.Press(keyboard.escapeKey);
                yield return new WaitForSeconds(0.1f);
                Assert.IsNull(driver.World.SelectedKind, "Escape drops the selection");
                Assert.IsFalse(shell.Open, "and the run keeps going");
                input.Release(keyboard.escapeKey);
                yield return null;

                // With nothing selected, the next Escape is the menu's.
                input.Press(keyboard.escapeKey);
                yield return null;
                Assert.IsTrue(shell.Open, "the next Escape opens the pause menu");
                Assert.IsFalse(shell.AtTitle, "and it is the pause card, not the title");
                input.Release(keyboard.escapeKey);
                yield return null;
            }
            finally
            {
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator PlacingABuilding_PutsGeometryOnScreen()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            SimWorld world = driver.World;
            var belt = new Int2(20, 10);
            var turret = new Int2(21, 10);

            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.TryPlace(BuildKind.Turret, turret, Dir.East));
            Assert.IsTrue(world.Belts.TrySpawnItem(belt, Ammo));
            world.Enemies.Spawn(EnemyKind.Runner, new Vec2(24f, 10.5f), Ammo);

            // One frame for the views to notice the revision bump, one for the new enemy to be drawn.
            yield return null;
            yield return null;

            Assert.Greater(Vertices("Belts"), 0, "a placed belt has a mesh");
            Assert.Greater(Vertices("Machines"), 0, "a placed turret has a mesh");
            Assert.Greater(Vertices("Ore"), 0, "the map's ore is drawn");
            Assert.Greater(Vertices("Items"), 0, "the shape riding the belt is drawn");
            Assert.Greater(Vertices("Enemies"), 0, "the enemy walking at the Core is drawn");
        }

        [UnityTest]
        public IEnumerator Restart_ClearsWhatTheViewsDraw()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            SimWorld world = driver.World;
            Assert.IsTrue(world.TryPlaceBelt(new Int2(20, 10), Dir.East));

            yield return null;
            Assert.Greater(Vertices("Belts"), 0);

            world.Tick(new InputCommand(restartPressed: true));

            yield return null;
            Assert.AreEqual(0, Vertices("Belts"), "a restarted run has no belts to draw");
            Assert.AreEqual(GameStatus.Playing, world.Status);
        }

        /// <summary>A kill has to leave something on the screen. The honest check is that the Enemies view carries
        /// more geometry right after the kill than once the burst has expired - and that it rebuilds without it, or
        /// the ring would stay forever.</summary>
        [UnityTest]
        public IEnumerator AKill_LeavesAVisibleBurst_ThatThenFades()
        {
            yield return null;
            BeginRun();   // the burst fades on the run's own tick, so the clock has to be running

            var driver = Driver;
            SimWorld world = driver.World;

            var belt = new Int2(20, 10);
            var turret = new Int2(21, 10);
            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.TryPlace(BuildKind.Turret, turret, Dir.East));
            Assert.IsTrue(world.Belts.TrySpawnItem(belt, Ammo));
            Assert.Greater(world.Enemies.Spawn(EnemyKind.Runner, new Vec2(24f, 10.5f), Ammo), 0);

            // Ammo arrives, the shots land, the runner dies (6 hp against 4 matched damage: two shots). The belt
            // holds one item at a time, so the feed keeps refilling it while the turret works.
            for (int i = 0; i < 30 * 6 && world.Enemies.AliveCount > 0; i++)
            {
                if (!world.Belts.HasItemAt(belt)) world.Belts.TrySpawnItem(belt, Ammo);
                world.Tick(InputCommand.None);
            }

            Assert.AreEqual(0, world.Enemies.AliveCount, "the gun killed the runner");
            Assert.Greater(world.Events.CountOf(SimEventKind.EnemyKilled), 0, "and the stream says so");

            yield return null;
            int bursting = Vertices("Enemies");

            yield return new WaitForSeconds(1f);
            int faded = Vertices("Enemies");

            Assert.Greater(bursting, faded, "the kill burst is gone a second later - it is not permanent");
        }

        /// <summary>A campaign without reloading the scene: the driver swaps the world for the next map and every
        /// view follows. The two caches a map change breaks are visible from outside - the ground, whose size comes
        /// from the tile grid, and the wave-entry markers, whose count comes from the map's entry points.</summary>
        [UnityTest]
        public IEnumerator LoadingTheNextMap_SwapsTheWorld_UnderTheSameViews()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            Assert.AreEqual(driver.Catalog.At(0).Name, driver.World.Map.Name, "the driver opens on the first map");
            Assert.AreEqual(0, driver.Campaign.MapIndex, "the driver opens on the first map");

            yield return null;
            Vector3 mapOneSpan = GridSpan();
            Assert.Greater(Vertices("Grid"), 0, "the ground is drawn for the first map");

            driver.Campaign.EnterNext();
            driver.LoadMap();
            yield return null;
            yield return null;

            Assert.AreEqual(driver.Catalog.At(1).Name, driver.World.Map.Name, "the world is the next map now");
            Assert.AreEqual(driver.Catalog.At(1).StartShapes, driver.World.Economy.Shapes, "with the map's own budget");
            Assert.AreEqual(0, driver.World.TickCount, "and as a fresh run");
            Assert.AreEqual(GameStatus.Playing, driver.World.Status);

            // The two fixture maps are different sizes, so a mesh left at the extent it opened on cannot pass
            // this: it has to span the new map's own tile grid, bar the half-line width the frame draws past
            // each edge.
            Vector3 mapTwoSpan = GridSpan();
            Assert.Greater(mapTwoSpan.x, mapOneSpan.x,
                "the ground was rebound to the wider map, not left at the extent it opened on");
            Assert.AreEqual(driver.World.TileGrid.Width, mapTwoSpan.x, 0.25f,
                "and it spans the new map's width");
            Assert.AreEqual(driver.World.TileGrid.Height, mapTwoSpan.y, 0.25f,
                "and its height");
            Assert.Greater(Vertices("Grid"), 0, "and the new grid is drawn");
            Assert.Greater(Vertices("Enemies"), 0, "and the new map's entry points are drawn");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs on the new map too");
        }

        /// <summary>A cleared map is still taught when it is played again. The driver used to set the lesson aside
        /// whenever ClearedCount > MapIndex - reading "cleared" as "was taught" - so the maps list's first row opened
        /// a lessonless Quarry, and a save cleared before the lesson was authored could never see it.</summary>
        [UnityTest]
        public IEnumerator AReplayOfAClearedMap_StillTeachesItsLesson()
        {
            yield return null;
            BeginRun();

            var driver = Driver;
            driver.Campaign.StartMap(2);
            driver.Campaign.MarkCleared();   // the save now says this map, at least, has been cleared
            Assert.Greater(driver.Campaign.ClearedCount, driver.Campaign.MapIndex,
                "the state the old skip read: cleared, at the index being entered");

            driver.LoadMap();
            yield return null;

            Assert.AreEqual(driver.Catalog.At(2).Name, driver.World.Map.Name, "the world is the cleared map");
            Assert.IsTrue(driver.World.Tutorial.Active, "and it teaches, a replay of a cleared map or not");
            Assert.AreEqual(0, driver.World.Tutorial.StepsCompleted, "from the first line");
        }

        /// <summary>A click that built nothing, through the whole chain: the tick refuses the placement, the stream
        /// carries the reason, the HUD's resource band draws it. IMGUI has no headless test - OnGUI needs a real
        /// player loop.</summary>
        [UnityTest]
        public IEnumerator ARefusedPlacement_IsReported_AndTheHudDrawsIt()
        {
            yield return null;
            BeginRun();   // the HUD is stood down behind the boot title; the band only draws on a run

            var driver = Driver;
            SimWorld world = driver.World;

            // The bootstrap budget spent: the next belt is a purchase this run cannot make.
            world.Economy.Reset(0);

            var cell = new Int2(20, 10);
            world.Tick(Gesture.Aim(cell, BuildKind.Belt));
            world.Tick(Gesture.Release(cell, BuildKind.Belt));

            Assert.IsFalse(world.Belts.Has(cell), "the belt is refused");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal),
                "and the click says why");
            Assert.AreEqual(RejectionReason.NoFunds, refusal.Reason);
            Assert.AreEqual(cell, refusal.Cell);

            // Frames with the refusal inside its window: the band draws the message and flashes the number, and
            // this test fails if either throws.
            yield return null;
            yield return null;
        }

        /// <summary>A wave the player is not looking at has to put something on the screen edge, or the run is
        /// decided off-view with no warning. Where the arrow goes is <see cref="Facet.Tests.ThreatRadarTests"/>'s,
        /// pinned from four numbers rather than from a camera.</summary>
        [UnityTest]
        public IEnumerator AnEnemyOutOfView_IsPointedAt_FromTheEdgeOfTheScreen()
        {
            yield return null;

            // At the rig's own size 10 the camera frames the whole 32x20 map, and a wide Game View - the shape a
            // wide editor window gives it - sees the far corner, where the walker would rightly get no arrow. The
            // rig reads the camera's size on the map it opens, so a closer view set before the run stays the run's.
            var camera = _cameraObject.GetComponent<Camera>();
            camera.orthographicSize = 4f;

            BeginRun();

            var driver = Driver;
            SimWorld world = driver.World;
            int before = Vertices("ThreatRadar");

            world.Enemies.Spawn(EnemyKind.Runner,
                new Vec2(world.TileGrid.Width - 1.5f, world.TileGrid.Height - 1.5f), Ammo);

            yield return null;
            yield return null;

            Assert.Greater(Vertices("ThreatRadar"), before,
                "the walker at the far corner gets an arrow on the edge of the view");
        }

        /// <summary>A map's own rock, water and void have to reach the screen, with the ground still drawn under
        /// them. The plain-map case is pinned in EditMode (<see cref="Facet.Tests.TerrainRendererTests"/>).</summary>
        [UnityTest]
        public IEnumerator AMapWithItsOwnGround_DrawsIt()
        {
            yield return null;
            BeginRun();

            Assert.Greater(Vertices("Terrain"), 0, "the map's own rock and water are drawn");
            Assert.Greater(Vertices("Grid"), 0, "and the ground they stand on is still drawn under them");
        }

        /// <summary>Walk frames until the screen says what the press asked for: a key is read in
        /// <c>UpdateControls</c>, which runs after <c>UpdateTutorial</c>, so the card lands a frame after the
        /// press - and a test that looks on the very next frame reads the screen the press was made on.</summary>
        private static IEnumerator AssertWithinFrames(System.Func<bool> condition, string message,
            int frames = 6)
        {
            for (int i = 0; i < frames && !condition(); i++) yield return null;
            Assert.IsTrue(condition(), message);
        }

        /// <summary>One control's left edge, in the cluster's own pixels - the number the builder used to compute
        /// from its neighbours' widths and the gaps between them.</summary>
        private static void AssertControlLeft(RectTransform cluster, string name, float left)
        {
            Transform node = cluster.Find(name);
            Assert.IsNotNull(node, "the cluster carries '" + name + "'");
            var rect = (RectTransform)node;
            Assert.AreEqual(left, rect.anchoredPosition.x, 0.5f,
                "'" + name + "' is where the layout's arithmetic put it");
            Assert.AreEqual(0f, rect.anchoredPosition.y, 0.5f, "'" + name + "' sits on the cluster's one row");
        }

        /// <summary>One row of a card, read back off the asset in the design units it was authored in and scaled
        /// once at bind.</summary>
        private static void AssertRow(RectTransform card, string name, float left, float top, float width,
            float height, float scale)
        {
            Transform node = card.Find(name);
            Assert.IsNotNull(node, "the card carries '" + name + "'");
            var row = (RectTransform)node;
            Assert.AreEqual(left * scale, row.anchoredPosition.x, 0.5f, "'" + name + "' is its design column");
            Assert.AreEqual(-top * scale, row.anchoredPosition.y, 0.5f, "and its design row");
            Assert.AreEqual(width * scale, row.sizeDelta.x, 0.5f, "'" + name + "' is its design width");
            Assert.AreEqual(height * scale, row.sizeDelta.y, 0.5f, "and its design height");
        }

        /// <summary>One piece hung off its card's top-right corner, read back the same way: xFromRight grows
        /// leftwards.</summary>
        private static void AssertRight(RectTransform card, string name, float right, float top, float width,
            float height, float scale)
        {
            Transform node = card.Find(name);
            Assert.IsNotNull(node, "the card carries '" + name + "'");
            var rt = (RectTransform)node;
            Assert.AreEqual(-right * scale, rt.anchoredPosition.x, 0.5f, "'" + name + "' is its design column");
            Assert.AreEqual(-top * scale, rt.anchoredPosition.y, 0.5f, "and its design row");
            Assert.AreEqual(width * scale, rt.sizeDelta.x, 0.5f, "'" + name + "' is its design width");
            Assert.AreEqual(height * scale, rt.sizeDelta.y, 0.5f, "and its design height");
        }

        /// <summary>How many keys the bar's row carries: counted off the asset rather than asked of the view.</summary>
        private static int KeyCount(RectTransform row)
        {
            int count = 0;
            for (int i = 0; i < row.childCount; i++)
                if (row.GetChild(i).name.StartsWith("tile", System.StringComparison.Ordinal)) count++;

            return count;
        }

        /// <summary>One label's words, found by the name the asset gives it.</summary>
        private static string LabelText(Transform root, string name)
        {
            Transform node = root.Find(name);
            Assert.IsNotNull(node, "'" + root.name + "' carries '" + name + "'");
            return node.GetComponent<TMPro.TMP_Text>().text;
        }

        /// <summary>Press a shell button the way the input module presses it, and answer whether the widget is
        /// still standing. That last part is the point: uGUI reads the target after the handler returns, so a screen
        /// that rebuilds inside its own press destroys the object the dispatch still holds.</summary>
        private static bool Press(ShellView shell, string path)
        {
            Transform button = shell.transform.Find("ShellCanvas/" + path);
            Assert.IsNotNull(button, "no " + path + " button on the shell");

            var press = new PointerEventData(EventSystem.current);
            ExecuteEvents.ExecuteHierarchy(button.gameObject, press, ExecuteEvents.pointerDownHandler);

            return button != null;   // a widget destroyed by its own press reads as null
        }

        private int Vertices(string viewName)
        {
            Transform view = _driverObject.transform.Find(viewName);
            Assert.IsNotNull(view, "no " + viewName + " view");

            var filter = view.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, viewName + " has no mesh filter");
            Assert.IsNotNull(filter.sharedMesh, viewName + " has no mesh");

            return filter.sharedMesh.vertexCount;
        }

        /// <summary>The world-unit span of the grid mesh's bounds - what proves a rebinding read the new map's
        /// tile grid.</summary>
        private Vector3 GridSpan()
        {
            Transform view = _driverObject.transform.Find("Grid");
            Assert.IsNotNull(view, "no Grid view");
            var filter = view.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, "Grid has no mesh filter");
            Assert.IsNotNull(filter.sharedMesh, "Grid has no mesh");

            return filter.sharedMesh.bounds.size;
        }
    }
}
