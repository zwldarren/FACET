using System.Collections;
using System.Text.RegularExpressions;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Facet.PlayTests
{
    /// <summary>
    /// The one thing the EditMode tests cannot reach: the view layer actually running. These build a scene
    /// from nothing and check the three failures no compile can catch and no headless test touches - that
    /// the driver assembles its views, advances the world at its fixed rate whatever the frame rate does,
    /// and gives a changed view a mesh with geometry. It runs a fixture-built campaign (see
    /// <see cref="TestCampaign"/>), not the shipped one, because asserting a shipped map's contents would
    /// test today's file; <see cref="TheShippedMainScene_RunsAsBuilt"/> is the one test that loads it.
    /// </summary>
    public class DriverSmokeTests
    {
        /// <summary>
        /// The round the smoke rig's line runs: the top half-circle, an item the cutter makes of a circle when
        /// it flows east or west - and the demand its enemies carry, so a shot that lands is a matched one.
        /// The rig spells the item out rather than using the EditMode harness, which this assembly cannot see.
        /// </summary>
        private static readonly Shape Ammo = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);

        private GameObject _cameraObject;
        private GameObject _driverObject;
        private MapSet _campaign;
        private int _progressBefore;
        private bool _progressKeyExisted;
        private bool _progressSnapshotted;

        private SimulationDriver Driver => _driverObject.GetComponent<SimulationDriver>();

        /// <summary>Leave the boot title the way the title's play button does - as a fresh run on a named
        /// map - because the game now opens held on that screen. Every test that plays rather than looks
        /// at the shell starts here; the ones that watch the title itself call it later.</summary>
        private void BeginRun() => Driver.PlayMap(0, false);

        [SetUp]
        public void CreateScene()
        {
            // The player's progress is snapshotted and forgotten first, before any fixture work that can
            // throw: TearDown runs even when SetUp fails, and on default fields it would Forget() rather
            // than restore - costing the developer their real progress, the loss this isolation prevents.
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

            // Built switched off, wired, and switched on: Awake runs when a component is added to an
            // active object, and this driver must have its campaign before its Awake asks for one. Only
            // the campaign is wired - a missing Palette falls back to the code defaults by design.
            _driverObject = new GameObject("FACET");
            _driverObject.SetActive(false);
            SimulationDriver driver = _driverObject.AddComponent<SimulationDriver>();

            _campaign = TestCampaign();

#if UNITY_EDITOR
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("campaign").objectReferenceValue = _campaign;
            serialized.ApplyModifiedPropertiesWithoutUndo();
#endif

            // The driver opens the campaign on its saved progress' frontier, so a machine that has played
            // the shipped maps must not decide which map the suite starts on (the store is forgotten at
            // the top of CreateScene and put back in TearDown).
            _driverObject.SetActive(true);
        }

        [TearDown]
        public void DestroyScene()
        {
            // Unity's destroyed-object equality makes this safe even if a test replaced the scene.
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

        /// <summary>
        /// The campaign the smoke tests run, built here instead of read from
        /// <c>Assets/Data/Campaign.asset</c>. Two maps, because a swap needs two: the first wears rock,
        /// water and a void corner and carries one vein, and the second is a different size - what the
        /// ground view's rebind is proved against.
        /// </summary>
        private static MapSet TestCampaign()
        {
            var set = ScriptableObject.CreateInstance<MapSet>();
            set.Maps = new[] { GroundMap(), WideMap() };
            return set;
        }

        /// <summary>32x20, rock and water painted, a void corner, one circle vein, one door and one wave
        /// that never arrives: the tests place their own belt, turret and enemy on it.</summary>
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

        /// <summary>The second map: 48x12, plain ground and a door of its own, so the world the driver
        /// builds next is a different shape from the one it opened on.</summary>
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

        /// <summary>
        /// The one thing that has no fallback. A missing Palette falls back to the code defaults and says
        /// so; a missing campaign is a game with nothing to play, so the driver reports it and stands down
        /// rather than running on nothing - a black screen with no explanation for it.
        /// </summary>
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

        /// <summary>
        /// The scene the WebGL build actually boots - not one this test built. If its camera, driver or
        /// Palette wiring were missing, the build would show an empty blue screen and no test would have
        /// noticed.
        /// </summary>
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

            // The shipped scene opens on the title, held: a player's first frame is the wordmark, not a
            // map already ticking under it. The play button is the one door that starts the clock.
            ShellView shell = Object.FindFirstObjectByType<ShellView>();
            Assert.IsNotNull(shell, "the driver builds the shell the title lives in");
            Assert.IsTrue(shell.Open, "and the shipped scene opens on that title");
            Assert.AreEqual(0, driver.World.TickCount, "the held run has spent no time");

            // The committed content asset is what the shipped scene boots with, so it has to agree with
            // the code's own table to the value - otherwise "the shipped game" would depend on which of
            // the two you happened to read.
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

            // Play the title, then let the driver tick: the same door a player's click takes.
            driver.PlayMap(0, false);
            yield return null;

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs in the shipped scene too");
        }

        [UnityTest]
        public IEnumerator Driver_BuildsEveryView_AndAdvancesTheWorld()
        {
            yield return null;   // let Awake and the first Update run

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

        /// <summary>
        /// The boot screen: the scene opens on the shell's title, with the run built behind it and held -
        /// the same hold the in-run menu uses, so no device, no tick and no second of the run is spent
        /// before the player presses play. This is the one test of what a player sees on the first frame.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBootTitle_HoldsTheRun_UntilThePlayerPlays()
        {
            yield return null;   // let Awake and the first Update run

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

        [UnityTest]
        public IEnumerator TheShell_HoldsTheRun_WhileItStands()
        {
            yield return null;   // let Awake and the first Update run
            BeginRun();          // leave the boot title first - the pause menu is opened over a run

            var driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            var hud = _driverObject.GetComponentInChildren<HudView>(true);
            Assert.IsNotNull(shell, "the driver builds the shell");
            Assert.IsNotNull(hud, "and the HUD the menu stands down");

            // Opened the way Esc and the HUD's menu button open it, only without the input device: the
            // shell is the state the driver holds the run on.
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

        /// <summary>The main menu is not a one-way trip any more: the pause menu's far door leaves the
        /// run, but only on the second press - the first arms it and says so, so an accidental click on
        /// the bottom of the card cannot throw a run away. The abandoned run is held, not ticked, and a
        /// fresh play from the title is a fresh run.</summary>
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

        /// <summary>The pause menu's restart door starts this map over: the run is reused rather than
        /// rebuilt (a restart is the world's own reset), the shell closes, and the clock is back at zero
        /// once the next tick applies the request - the same path the R key takes.</summary>
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

        /// <summary>The restart door costs the same run the main-menu door does, so it is armed the same
        /// way: the first press asks and leaves everything standing, the second starts the map over.</summary>
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

        /// <summary>
        /// The keys the driver reads for itself, through a real Input System device: M opens the pause
        /// menu and Escape hands the run back; Escape with a building on the cursor drops that building
        /// instead of stopping the run, and only the next Escape opens the menu - the narrowed contract
        /// the driver's own comment makes. The title's two keys are pinned at both ends (M to the map
        /// list, Enter to play), and an armed door is disarmed by the same Escape that means "back".
        /// </summary>
        [UnityTest]
        public IEnumerator TheDriversKeys_OpenTheMenu_AndEscapeDropsASelectionFirst()
        {
            yield return null;   // let Awake and the first Update run
            SimulationDriver driver = Driver;
            var shell = _driverObject.GetComponentInChildren<ShellView>(true);
            Assert.IsTrue(shell.Open, "the boot title stands");
            Assert.IsTrue(shell.AtTitle, "and it is the title");

            // The fixture severs the real devices, so the machine running the tests cannot press anything.
            // The keyboard it does hand back is the one every key below goes through.
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.AddDevice<Mouse>();   // the shell's UI module binds a pointer; leave it one
                yield return null;   // the test keyboard is the current one from this frame on

                // On the title, M opens the map list and Escape steps back to the title.
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

                // Enter is the title's play door.
                input.Press(keyboard.enterKey);
                yield return null;
                Assert.IsFalse(shell.Open, "Enter starts the run");
                input.Release(keyboard.enterKey);
                yield return new WaitForSeconds(0.2f);
                Assert.Greater(driver.World.TickCount, 0, "and the clock is running");

                // M over a run opens the pause menu; Escape hands the run back.
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

                // A digit puts a building on the cursor. The selection is latched by the input and applied
                // by the next tick, so the world's own answer is read after one tick's worth of time.
                input.Press(keyboard.digit1Key);
                yield return new WaitForSeconds(0.1f);
                Assert.IsNotNull(driver.World.SelectedKind, "a digit selects a building");
                input.Release(keyboard.digit1Key);
                yield return null;

                // Escape, meaning "back", drops it - and must not also stop the run, because that press
                // was the map's. The driver reads the world's selection before the input latch clears it,
                // which is what keeps the two meanings of the key apart.
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

        /// <summary>
        /// The event stream, seen from the view: a kill has to leave something on the screen. The burst is
        /// drawn where the enemy died and fades over a few ticks, so the honest check is that the Enemies
        /// view carries more geometry right after the kill than once the burst has expired - and that it
        /// *rebuilds* without it, or the ring would stay on screen forever.
        /// </summary>
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

            // Ammo arrives, the shots land, the runner dies (6 hp against 4 matched damage: two shots). Damage and
            // the kill both go on the stream. The belt holds one item at a time, so the feed keeps refilling
            // it while the turret works.
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

        /// <summary>
        /// A campaign, without reloading the scene: the driver swaps the world for the next map and every
        /// view follows it. The two caches a map change breaks are both visible from outside - the ground,
        /// whose size comes from the tile grid, and the wave-entry markers, whose count comes from the
        /// map's entry points (a stale one throws, because the marker loop indexes its flags by the map's
        /// count).
        /// </summary>
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

            // The ground follows the world the driver swapped in. The fixture's two maps are different
            // sizes, so a mesh left at the extent it opened on cannot pass this: it has to span the new
            // map's own tile grid, bar the half-line width the frame draws past each edge.
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

        /// <summary>
        /// The answer to a click that built nothing, through the whole chain: the tick refuses the
        /// placement, the stream carries the reason, and the HUD's resource band draws it. IMGUI has no
        /// headless test - OnGUI only runs in a real player loop - so "drive the refusal, then draw frames
        /// with it live" is the closest thing to a test of the band.
        /// </summary>
        [UnityTest]
        public IEnumerator ARefusedPlacement_IsReported_AndTheHudDrawsIt()
        {
            yield return null;
            BeginRun();   // the HUD is stood down behind the boot title; the band only draws on a run

            var driver = Driver;
            SimWorld world = driver.World;

            // The bootstrap budget spent: the next belt is a purchase this run cannot make - the click a
            // player makes before asking why nothing happened.
            world.Economy.Reset(0);

            var cell = new Int2(20, 10);
            world.Tick(Gesture.Aim(cell, BuildKind.Belt));
            world.Tick(Gesture.Release(cell, BuildKind.Belt));

            Assert.IsFalse(world.Belts.Has(cell), "the belt is refused");
            Assert.IsTrue(world.Events.TryLast(SimEventKind.PlacementRejected, out SimEvent refusal),
                "and the click says why");
            Assert.AreEqual(RejectionReason.NoFunds, refusal.Reason);
            Assert.AreEqual(cell, refusal.Cell);

            // Frames with the refusal inside its window: the band draws the message and flashes the
            // number, and this test fails if either throws.
            yield return null;
            yield return null;
        }

        /// <summary>
        /// The locator seen from outside: a wave the player is not looking at has to put something on the
        /// screen edge, or the run is decided off-view with no warning. The enemy goes into the map's far
        /// corner, the one place the fixture guarantees is out of view at any resolution the runner has -
        /// the rig centres the view on the Core. What "out of view" means, and where the arrow goes, is
        /// <see cref="Facet.Tests.ThreatRadarTests"/>'s: pinned from four numbers rather than from a camera.
        /// </summary>
        [UnityTest]
        public IEnumerator AnEnemyOutOfView_IsPointedAt_FromTheEdgeOfTheScreen()
        {
            yield return null;
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

        /// <summary>
        /// A map with a ground of its own: rock, water and the void a map is shaped by have to reach the
        /// screen, and the ground view has to keep drawing the tiles they sit on. The claim that a map with
        /// nothing on it costs no geometry at all is pinned in EditMode
        /// (<see cref="Facet.Tests.TerrainRendererTests"/>), where a plain map can be built to measure.
        /// </summary>
        [UnityTest]
        public IEnumerator AMapWithItsOwnGround_DrawsIt()
        {
            yield return null;
            BeginRun();

            Assert.Greater(Vertices("Terrain"), 0, "the map's own rock and water are drawn");
            Assert.Greater(Vertices("Grid"), 0, "and the ground they stand on is still drawn under them");
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

        /// <summary>The world-unit span of the grid mesh's bounds - the extent the ground view
        /// actually covers, which is what proves a rebinding read the new map's tile grid.</summary>
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
