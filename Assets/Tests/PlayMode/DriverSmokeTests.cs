using System.Collections;
using System.Text.RegularExpressions;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Facet.PlayTests
{
    /// <summary>
    /// The one thing the EditMode tests cannot reach: the view layer actually running.
    ///
    /// These tests build a scene from nothing - camera, driver, views - and then check the three
    /// failures that no compile can catch and no headless simulation test touches: that the driver
    /// assembles its views at all, that it advances the world at its fixed rate while the frame rate
    /// does whatever it likes, and that a view whose simulation state changed has a mesh with geometry
    /// in it. The last one is the honest check for "is it drawn": the mesh is real even when there is
    /// no display to put it on, so an empty or never-rebuilt mesh fails here.
    /// </summary>
    public class DriverSmokeTests
    {
        private GameObject _cameraObject;
        private GameObject _driverObject;

        /// <summary>
        /// The campaign the shipped scene is wired to. Named here for the same reason the game names it in
        /// the scene: a driver built by hand has no Inspector to wire it in, and a driver with no campaign
        /// refuses to start - which is the behaviour, not an inconvenience.
        /// </summary>
        private const string CampaignPath = "Assets/Data/Campaign.asset";

        [SetUp]
        public void CreateScene()
        {
            _cameraObject = new GameObject("Main Camera");
            _cameraObject.tag = "MainCamera";
            Camera camera = _cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 10f;
            camera.transform.position = new Vector3(40f, 24f, -10f);

            // Built switched off, wired, and switched on: Awake runs when a component is added to an
            // active object, and this driver has to have its campaign before its Awake asks for one. Only
            // the campaign is wired - a missing Palette falls back to the code defaults by design, and only
            // the campaign is the thing a run cannot do without.
            _driverObject = new GameObject("FACET");
            _driverObject.SetActive(false);
            SimulationDriver driver = _driverObject.AddComponent<SimulationDriver>();

#if UNITY_EDITOR
            var serialized = new SerializedObject(driver);
            serialized.FindProperty("campaign").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MapSet>(CampaignPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
#endif

            _driverObject.SetActive(true);
        }

        [TearDown]
        public void DestroyScene()
        {
            // Unity's destroyed-object equality makes this safe even if a test replaced the scene.
            if (_driverObject != null) Object.Destroy(_driverObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
        }

        /// <summary>
        /// The one thing that has no fallback. A missing Palette falls back to the code defaults and says so;
        /// a missing campaign is a game with nothing to play, so the driver reports it and stands down rather
        /// than running on nothing - which would be a black screen and no explanation for it.
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
        /// The scene the WebGL build actually boots - not one this test built. If its camera, driver
        /// or Palette wiring were missing, the build would show an empty blue screen and no test
        /// would have noticed.
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

            Assert.AreEqual(ContentDatabase.Default.Turret(BuildKind.Cannon).Damage,
                driver.Content.Turret(BuildKind.Cannon).Damage, 1e-4f, "shipped cannon damage");
            Assert.AreEqual(ContentDatabase.Default.Enemy(EnemyKind.Spike).Hp,
                driver.Content.Enemy(EnemyKind.Spike).Hp, 1e-4f, "shipped spike hp");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs in the shipped scene too");
        }

        [UnityTest]
        public IEnumerator Driver_BuildsEveryView_AndAdvancesTheWorld()
        {
            yield return null;   // let Awake and the first Update run

            var driver = _driverObject.GetComponent<SimulationDriver>();
            Assert.IsNotNull(driver.World, "the driver owns a simulation");

            foreach (string view in new[] { "Grid", "Terrain", "Ore", "Belts", "Core", "Machines", "Items", "Enemies", "Shots", "Cursor", "Hud" })
                Assert.IsNotNull(_driverObject.transform.Find(view), "the driver did not build the " + view + " view");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.4f);
            Assert.Greater(driver.World.TickCount, ticks, "the fixed 30 Hz clock keeps running across frames");
        }

        [UnityTest]
        public IEnumerator PlacingABuilding_PutsGeometryOnScreen()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
            SimWorld world = driver.World;
            var belt = new Int2(20, 10);
            var turret = new Int2(21, 10);

            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.TryPlace(BuildKind.Cannon, turret, Dir.East));
            Assert.IsTrue(world.Belts.TrySpawnItem(belt, ShapeType.HalfCircle));
            world.Enemies.Spawn(EnemyKind.Spike, new Vec2(24f, 10.5f));

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

            var driver = _driverObject.GetComponent<SimulationDriver>();
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
        /// The event stream, seen from the view: a kill has to leave something on the screen. The burst
        /// is drawn where the enemy died and fades over a few ticks, so the honest check is that the
        /// Enemies view carries more geometry right after the kill than it does once the burst has
        /// expired - and that it *rebuilds* without it, or the ring would stay on screen forever.
        /// </summary>
        [UnityTest]
        public IEnumerator AKill_LeavesAVisibleBurst_ThatThenFades()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
            SimWorld world = driver.World;

            var belt = new Int2(20, 10);
            var turret = new Int2(21, 10);
            Assert.IsTrue(world.TryPlaceBelt(belt, Dir.East));
            Assert.IsTrue(world.TryPlace(BuildKind.Cannon, turret, Dir.East));
            Assert.IsTrue(world.Belts.TrySpawnItem(belt, ShapeType.HalfCircle));
            Assert.Greater(world.Enemies.Spawn(EnemyKind.Spike, new Vec2(24f, 10.5f)), 0);

            // Ammo arrives, the shots land, the spike dies (6 hp against 3 damage: two shots). Damage
            // and the kill both go on the stream. The belt holds one item at a time, so the feed keeps
            // refilling it while the turret works.
            for (int i = 0; i < 30 * 6 && world.Enemies.AliveCount > 0; i++)
            {
                if (!world.Belts.HasItemAt(belt)) world.Belts.TrySpawnItem(belt, ShapeType.HalfCircle);
                world.Tick(InputCommand.None);
            }

            Assert.AreEqual(0, world.Enemies.AliveCount, "the cannon killed the spike");
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

            var driver = _driverObject.GetComponent<SimulationDriver>();
            Assert.AreEqual(driver.Catalog.At(0).Name, driver.World.Map.Name, "the driver opens on the first map");
            Assert.AreEqual(0, driver.Campaign.MapIndex, "the driver opens on the first map");

            yield return null;
            Assert.AreEqual(GridVertices(driver.Catalog.At(0)), Vertices("Grid"), "the ground is map 1's");

            driver.Campaign.EnterNext();
            driver.LoadMap();
            yield return null;
            yield return null;

            Assert.AreEqual(driver.Catalog.At(1).Name, driver.World.Map.Name, "the world is the next map now");
            Assert.AreEqual(driver.Catalog.At(1).StartCircles, driver.World.Economy.Circles, "with the map's own budget");
            Assert.AreEqual(0, driver.World.TickCount, "and as a fresh run");
            Assert.AreEqual(GameStatus.Playing, driver.World.Status);

            Assert.AreEqual(GridVertices(driver.Catalog.At(1)), Vertices("Grid"),
                "the ground was rebound to the new map's tile grid");
            Assert.Greater(Vertices("Enemies"), 0, "and the new map's entry points are drawn");

            int ticks = driver.World.TickCount;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(driver.World.TickCount, ticks, "and the clock runs on the new map too");
        }

        /// <summary>
        /// The answer to a click that built nothing, through the whole chain: the tick refuses the
        /// placement, the stream carries the reason, and the HUD's resource band draws it. IMGUI has no
        /// headless test - OnGUI only runs in a real player loop - so "drive the refusal, then draw
        /// frames with it live" is the closest thing to a test of the band, and a throw while drawing it
        /// fails here the way a compile error would.
        /// </summary>
        [UnityTest]
        public IEnumerator ARefusedPlacement_IsReported_AndTheHudDrawsIt()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
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
        /// A map with a ground of its own: rock, water and the void a map is shaped by have to reach the
        /// screen, and the ground view has to keep drawing the tiles they sit on.
        ///
        /// The two shipped maps are plain ground, so the terrain view draws nothing at all on them - which
        /// is itself worth pinning, because "no obstacles" has to cost no geometry rather than a mesh
        /// full of nothing. The campaign's first map that states terrain is found by asking the data, so
        /// adding a fourth map does not quietly turn this into a test of map 3.
        /// </summary>
        [UnityTest]
        public IEnumerator AMapWithItsOwnGround_DrawsIt()
        {
            yield return null;

            var driver = _driverObject.GetComponent<SimulationDriver>();
            Assert.AreEqual(0, Vertices("Terrain"), "the first map is plain ground, so there is nothing to draw");

            int terrainMap = -1;
            for (int i = 0; i < driver.Catalog.Count; i++)
            {
                if (!HasAnObstacle(driver.Catalog.At(i))) continue;
                terrainMap = i;
                break;
            }

            Assert.GreaterOrEqual(terrainMap, 0, "the campaign has a map with a ground of its own");
            while (driver.Campaign.MapIndex < terrainMap) driver.Campaign.EnterNext();

            driver.LoadMap();
            yield return null;
            yield return null;

            Assert.AreEqual(driver.Catalog.At(terrainMap).Name, driver.World.Map.Name);
            Assert.Greater(Vertices("Terrain"), 0, "the rock and the water are drawn");
            Assert.Greater(Vertices("Grid"), 0, "and the ground they stand on is still drawn under them");
        }

        /// <summary>
        /// Whether a map's ground has anything in it that is neither buildable nor a vein. Asked by kind
        /// rather than by "does it carry a terrain array at all", because an authored map carries one as
        /// soon as it has a vein - the array is the ground of every cell, and a vein is a kind of ground -
        /// so the array's existence says nothing about whether there is rock to draw.
        ///
        /// What "anything" means is <see cref="TerrainKindExtensions.IsObstacle"/> - the rock/water/void
        /// rule Core already owns, the one the read cursor and the build rules ask - so a new kind of
        /// obstacle ground is a change there, not here.
        /// </summary>
        private static bool HasAnObstacle(MapDefinition map)
        {
            if (map.Terrain == null) return false;

            for (int i = 0; i < map.Terrain.Length; i++)
            {
                if (map.Terrain[i].IsObstacle()) return true;
            }

            return false;
        }

        /// <summary>
        /// The grid view's vertex count for a map that is plain ground everywhere, which the first two maps
        /// are: one ground quad per row, plus a line quad per grid line - W+1 vertical and H+1
        /// horizontal. A map with terrain in it draws fewer of both, because a run of void is not filled
        /// and a boundary with no ground on either side is not lined, so this helper is only honest for a
        /// map whose whole rectangle is ground.
        /// </summary>
        private static int GridVertices(MapDefinition map) => 4 * (map.Width + 2 * map.Height + 2);

        private int Vertices(string viewName)
        {
            Transform view = _driverObject.transform.Find(viewName);
            Assert.IsNotNull(view, "no " + viewName + " view");

            var filter = view.GetComponent<MeshFilter>();
            Assert.IsNotNull(filter, viewName + " has no mesh filter");
            Assert.IsNotNull(filter.sharedMesh, viewName + " has no mesh");

            return filter.sharedMesh.vertexCount;
        }
    }
}
