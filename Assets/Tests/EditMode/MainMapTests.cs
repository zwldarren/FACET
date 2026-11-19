using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEditor;

namespace Facet.Tests
{
    /// <summary>
    /// The main map (Frontier): a finite six-wave campaign finale on a 40x28 ground, with two balanced
    /// approaches - a north lane behind a ridge and a south lane behind a ridge - that walk east to west
    /// along the map's edges and turn in to the Core's north and south faces. The floor between the ridges
    /// is the factory; a one-tile river in the middle of the eastern third does not reach the north ridge,
    /// so the square ore across it can be belted round the river's head in a handful of tiles and piped
    /// across it in three.
    ///
    /// Progression builds directly on the Workshop without forcing rebuilds: wave 1 opens on the north lane
    /// with full circles while funding the open Core; wave 2 asks for top half-circles (Cutter + Splitter);
    /// wave 3 opens the south lane with full squares, which is the wave the Pipe arrives on - the popup says
    /// so, and says a square banks like a circle; wave 4 introduces the Stacker to combine the already-running
    /// top half-circle and bottom half-square lines; waves 5 and 6 combine both fronts.
    /// </summary>
    public class MainMapTests
    {
        private const string MainMapPath = "Assets/Data/Maps/MainMap.asset";
        private const string TutorialPath = "Assets/Data/Maps/Tutorial.asset";
        private const string WorkshopPath = "Assets/Data/Maps/Workshop.asset";
        private const string CampaignPath = "Assets/Data/Campaign.asset";

        private static MapAsset Load()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapAsset>(MainMapPath);
            Assert.IsNotNull(map, MainMapPath + " is missing");
            return map;
        }

        private static readonly Shape TopHalfCircle = Shape.Of(Quad.Circle, Quad.Circle, Quad.Empty, Quad.Empty);
        private static readonly Shape RightHalfCircle = Shape.Of(Quad.Empty, Quad.Circle, Quad.Circle, Quad.Empty);
        private static readonly Shape FullCircle = Shape.Full(Quad.Circle);
        private static readonly Shape FullSquare = Shape.Full(Quad.Square);
        private static readonly Shape MixedTopHalves = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

        [Test]
        public void TheCampaign_IsTheTutorial_TheWorkshop_ThenTheMainMap()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<MapSet>(CampaignPath);
            Assert.IsNotNull(campaign);
            Assert.AreEqual(3, campaign.Maps.Length);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<MapAsset>(TutorialPath), campaign.Maps[0]);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<MapAsset>(WorkshopPath), campaign.Maps[1]);
            Assert.AreSame(Load(), campaign.Maps[2]);
        }

        [Test]
        public void TheWorkshop_TeachesTheCutterAndRotator_ThenExamsThem()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapAsset>(WorkshopPath);
            Assert.IsNotNull(asset, WorkshopPath + " is missing");
            CollectionAssert.IsEmpty(asset.Validate());

            MapDefinition map = asset.ToCore();
            Assert.IsTrue(map.Tutorial.Coached);
            Assert.IsTrue(map.Tutorial.ClockRuns, "the waves are the exam, so the clock is not the lesson's");

            var world = new SimWorld(map, new SimConfig());
            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Rotator));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Sink));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Splitter), "Frontier teaches that one");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Stacker));

            int demos = 0;
            for (int i = 0; i < map.Tutorial.Count; i++)
            {
                TutorialTask line = map.Tutorial[i];
                if (System.Array.IndexOf(line.Focus, TutorialFocus.Demo) >= 0)
                {
                    demos++;
                    Assert.AreEqual(TutorialStep.Acknowledge, line.Step, "a demo is read, not rebuilt");
                }
            }
            Assert.AreEqual(2, demos, "one demo for the cutter, one for the rotator");

            foreach (SpawnGroup group in map.Waves[0].Groups)
                Assert.AreEqual(TopHalfCircle, group.Weakness, "wave 1 asks for the cut");
            foreach (SpawnGroup group in map.Waves[2].Groups)
                Assert.AreEqual(RightHalfCircle, group.Weakness, "wave 3 asks for the turn");
        }

        [Test]
        public void TheMainMap_IsValid_Compact_Finite_AndOpenAroundTheCore()
        {
            MapAsset asset = Load();
            CollectionAssert.IsEmpty(asset.Validate());

            MapDefinition map = asset.ToCore();
            Assert.AreEqual(40, map.Width, "wide enough for two lanes and a floor, without frantic panning");
            Assert.AreEqual(28, map.Height);
            Assert.AreEqual(0, map.GeneratedWaves, "the sixth authored wave is the finale");
            Assert.AreEqual(6, map.Waves.Length, "four escalating single-front waves, then two combined assaults");
            Assert.AreEqual(80, map.StartShapes);
            Assert.IsNull(map.Tutorial, "it teaches by the coach's popups, not by a scripted lesson");
            Assert.AreEqual(2, map.SpawnPoints.Length, "north canyon and south canyon");
            CollectionAssert.AreEqual(new[] { new Int2(39, 24), new Int2(39, 3) }, map.SpawnPoints);
            Assert.AreEqual(new Int2(5, 12), map.CoreCell);

            // The Core sits in open ground so the player can bank multiple short belts effortlessly.
            int openPerimeter = 0;
            for (int x = map.CoreCell.X; x < map.CoreCell.X + CoreState.Size; x++)
            {
                if (map.TerrainAt(new Int2(x, map.CoreCell.Y - 1)) == TerrainKind.Ground) openPerimeter++;
                if (map.TerrainAt(new Int2(x, map.CoreCell.Y + CoreState.Size)) == TerrainKind.Ground) openPerimeter++;
            }
            for (int y = map.CoreCell.Y; y < map.CoreCell.Y + CoreState.Size; y++)
            {
                if (map.TerrainAt(new Int2(map.CoreCell.X - 1, y)) == TerrainKind.Ground) openPerimeter++;
                if (map.TerrainAt(new Int2(map.CoreCell.X + CoreState.Size, y)) == TerrainKind.Ground) openPerimeter++;
            }
            Assert.GreaterOrEqual(openPerimeter, 12, "at least three faces of the Core stay open for banking");

            var world = new SimWorld(map, new SimConfig());
            Assert.AreEqual(6, world.Waves.TotalWaves);
            Assert.AreEqual(80, world.Economy.Shapes);
        }

        [Test]
        public void TheRun_OpensWithWhatTheWorkshopTaught()
        {
            var world = new SimWorld(Load().ToCore(), new SimConfig());

            foreach (BuildKind kind in new[] { BuildKind.Belt, BuildKind.Turret, BuildKind.Cutter, BuildKind.Sink,
                         BuildKind.Rotator })
                Assert.IsTrue(world.IsUnlocked(kind), kind + " was learned in the Workshop");
            foreach (BuildKind kind in new[] { BuildKind.Splitter, BuildKind.Pipe, BuildKind.Stacker,
                         BuildKind.Sorter })
            {
                Assert.IsFalse(world.IsUnlocked(kind), kind + " waits for research");
                Assert.IsFalse(world.Researchable(kind), kind + " is not on offer yet");
            }
        }

        [Test]
        public void TheSchedule_UnlocksOneNewMachinePerWave_AndKeepsSorterOffTheBar()
        {
            MapDefinition map = Load().ToCore();
            ResearchOffer[] offers = map.Research.Offers;

            Assert.AreEqual(4, offers.Length);
            AssertOffer(offers[0], 1, BuildKind.Splitter);
            AssertOffer(offers[1], 2, BuildKind.Pipe);
            AssertOffer(offers[2], 3, BuildKind.Stacker);
            AssertOffer(offers[3], 7, BuildKind.Sorter);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(0, offers[i].Cost, "active unlocks arrive free with their wave");
                Assert.IsEmpty(offers[i].Hint, "the coach popup carries the explanation");
                AssertLesson(offers[i]);
            }

            Assert.Greater(offers[3].AfterWave, map.Waves.Length,
                "Sorter is held past the final wave so Frontier never introduces an unused machine");
            Assert.IsNull(offers[3].Lesson);
        }

        private static void AssertOffer(ResearchOffer offer, int afterWave, params BuildKind[] builds)
        {
            Assert.AreEqual(afterWave, offer.AfterWave);
            CollectionAssert.AreEquivalent(builds, offer.Builds);
        }

        private static void AssertLesson(ResearchOffer offer)
        {
            TutorialScript lesson = offer.Lesson;
            Assert.IsNotNull(lesson, "every active building is introduced");
            Assert.IsTrue(lesson.Coached, "by the coach's popup");
            Assert.IsTrue(lesson.Spotlight, "which dims the screen around what the line is about");
            Assert.AreEqual(1, lesson.Count, "one line per machine");

            TutorialTask line = lesson[0];
            Assert.AreEqual(TutorialStep.Acknowledge, line.Step, "read, not performed");
            Assert.IsNotEmpty(line.Title, "every line has a heading");
            Assert.IsNotEmpty(line.Text, "and something to say");
            Assert.LessOrEqual(line.Text.Length, 150, "and says it briefly: " + line.Title);
            Assert.IsEmpty(line.Hint, "a line that asks for nothing cannot be got stuck on");
            Assert.IsFalse(line.Target.HasValue, "no authored tile the player has to hit");
            Assert.IsEmpty(line.Ghost, "and no blueprint to copy");
            Assert.AreEqual(1, line.Opens.Length, "names the one building it is about: " + line.Title);
            Assert.AreEqual(offer.Builds[0], line.Opens[0]);
            CollectionAssert.Contains(line.Focus, TutorialFocus.Demo, "beside the machine at work");
            CollectionAssert.Contains(line.Focus, TutorialFocus.BarTile, "and the key it arrived on");
            Assert.IsTrue(MachineDemo.Covers(offer.Builds[0]), "a demo the popup can draw");
        }

        [Test]
        public void TheOffers_ArriveOnePerWave_Free_AndTheWaveClockWaitsForTheRead()
        {
            var world = new SimWorld(Load().ToCore(), new SimConfig());

            ClearWaveWithoutCombat(world);
            Assert.AreEqual(1, world.Research.OfferedCount, "wave 1 offers Splitter");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Splitter));
            Assert.IsTrue(world.Research.Teaching);

            float held = world.Waves.IntermissionRemaining;
            Sim.Tick(world, 60);
            Assert.AreEqual(held, world.Waves.IntermissionRemaining, "the countdown waits for the read");

            world.RequestTutorialNext();
            Sim.Tick(world, 3);
            Assert.IsFalse(world.Research.Teaching, "one popup per wave: one press and done");
            Assert.Less(world.Waves.IntermissionRemaining, held, "and then the clock runs again");

            ClearWaveWithoutCombat(world);
            Assert.AreEqual(2, world.Research.OfferedCount, "wave 2 offers Pipe alone");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Pipe));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Stacker), "Stacker waits for wave 3 so popups never stack");

            world.RequestTutorialNext();
            Sim.Tick(world, 3);
            Assert.IsFalse(world.Research.Teaching);

            ClearWaveWithoutCombat(world);
            Assert.AreEqual(3, world.Research.OfferedCount, "wave 3 offers Stacker");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Stacker));

            world.RequestTutorialNext();
            Sim.Tick(world, 3);
            Assert.IsFalse(world.Research.Teaching);

            ClearWaveWithoutCombat(world);
            Assert.AreEqual(3, world.Research.OfferedCount, "no new unlocks after wave 4");
            Assert.IsFalse(world.IsUnlocked(BuildKind.Sorter), "Sorter stays off the bar for the whole run");
            Assert.IsFalse(world.OnBar(BuildKind.Sorter));
        }

        [Test]
        public void TheWaves_EscalateCleanly_FromWorkshopCut_ThroughPipe_ToMixedStacker()
        {
            MapDefinition map = Load().ToCore();

            int[][] doorsByWave =
            {
                new[] { 0 },
                new[] { 0 },
                new[] { 1 },
                new[] { 1 },
                new[] { 0, 1 },
                new[] { 0, 1 },
            };

            Shape[] northExpected = { FullCircle, TopHalfCircle, default, default, TopHalfCircle, TopHalfCircle };
            Shape[] southExpected = { default, default, FullSquare, MixedTopHalves, MixedTopHalves, MixedTopHalves };

            for (int w = 0; w < map.Waves.Length; w++)
            {
                var doors = new HashSet<int>();
                foreach (SpawnGroup group in map.Waves[w].Groups)
                {
                    doors.Add(group.SpawnPoint);
                    Shape expected = group.SpawnPoint == 0 ? northExpected[w] : southExpected[w];
                    Assert.AreEqual(expected, group.Weakness,
                        "wave " + (w + 1) + " door " + group.SpawnPoint + " weakness");
                }

                CollectionAssert.AreEquivalent(doorsByWave[w], doors,
                    "wave " + (w + 1) + " uses the intended fronts");
            }
        }

        [Test]
        public void TheSetups_GiveExtraRoomToOpeningAndFirstStacker()
        {
            WaveDefinition[] waves = Load().ToCore().Waves;
            Assert.GreaterOrEqual(waves[0].Intermission, 45f, "wave 1 setup builds economy and first defense");
            Assert.GreaterOrEqual(waves[3].Intermission, 45f, "wave 4 setup gives generous time for the first Stacker");

            float total = 0f;
            for (int w = 0; w < waves.Length; w++) total += waves[w].Intermission;
            Assert.LessOrEqual(total, 215f);
        }

        [Test]
        public void TheTwoDoors_WalkBalancedDisjointRoads_AndNoGunHoldsBoth()
        {
            MapDefinition map = Load().ToCore();
            var world = new SimWorld(map, new SimConfig());
            var routes = new List<Int2>[map.SpawnPoints.Length];

            for (int door = 0; door < map.SpawnPoints.Length; door++)
            {
                routes[door] = new List<Int2>();
                world.TraceEnemyRoute(door, routes[door]);
            }

            Assert.AreEqual(routes[0].Count, routes[1].Count, "north and south lanes are equally paced");
            Assert.AreEqual(41, routes[0].Count, "40 walkable steps plus Core arrival cell");
            Assert.AreEqual(new Int2(8, 15), routes[0][routes[0].Count - 1], "north face of Core");
            Assert.AreEqual(new Int2(8, 12), routes[1][routes[1].Count - 1], "south face of Core");

            var shared = new HashSet<Int2>(routes[0]);
            shared.IntersectWith(routes[1]);
            Assert.IsEmpty(shared, "north and south roads never share a cell");

            float rangeSq = world.Content.Turret(BuildKind.Turret).Range;
            rangeSq *= rangeSq;
            int share0 = routes[0].Count * 2 / 5;
            int share1 = routes[1].Count * 2 / 5;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var gun = new Int2(x, y);
                    if (map.TerrainAt(gun) != TerrainKind.Ground || map.IsCoreTile(gun) || map.PatchClaims(gun))
                        continue;
                    if (Covered(routes[0], gun, rangeSq) <= share0) continue;

                    Assert.LessOrEqual(Covered(routes[1], gun, rangeSq), share1,
                        "a gun at " + gun + " covers two fifths of both roads");
                }
            }
        }

        [Test]
        public void TheVeins_AreFivePurposefulBlocks_FourTilesClearOfEveryLane()
        {
            MapDefinition map = Load().ToCore();
            var world = new SimWorld(map, new SimConfig());
            var lane = new HashSet<Int2>();

            for (int door = 0; door < map.SpawnPoints.Length; door++)
            {
                var route = new List<Int2>();
                world.TraceEnemyRoute(door, route);
                lane.UnionWith(route);
            }

            var mineralAt = new Dictionary<Int2, Quad>();
            foreach (ShapePatch patch in map.Patches)
                for (int dy = 0; dy < patch.Height; dy++)
                    for (int dx = 0; dx < patch.Width; dx++)
                        mineralAt[new Int2(patch.Origin.X + dx, patch.Origin.Y + dy)] = patch.Mineral;

            // Three circle seams on the west half (two beside the Core's opening lines, one in the floor's
            // north-east corner) and two square seams across the river: the one at its mouth, which a belt
            // can reach round the head, and the one behind the water, which wants the Pipe.
            Int2[] origins =
            {
                new Int2(1, 17), new Int2(1, 7), new Int2(15, 16),
                new Int2(31, 15), new Int2(31, 10),
            };
            Quad[] minerals =
            {
                Quad.Circle, Quad.Circle, Quad.Circle,
                Quad.Square, Quad.Square,
            };
            for (int seam = 0; seam < origins.Length; seam++)
                for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 3; dx++)
                    {
                        Int2 cell = origins[seam] + new Int2(dx, dy);
                        Assert.IsTrue(mineralAt.ContainsKey(cell), "missing seam tile at " + cell);
                        Assert.AreEqual(minerals[seam], mineralAt[cell], "wrong mineral at " + cell);
                    }

            Assert.AreEqual(45, mineralAt.Count, "five 3x3 seams with no leftover dead ore");
            CollectionAssert.AreEquivalent(new[] { Quad.Circle, Quad.Square }, map.MineralList(),
                "the map's money is drawn in both minerals, circle first");

            foreach (Int2 cell in mineralAt.Keys)
            {
                int gapSq = int.MaxValue;
                foreach (Int2 step in lane)
                {
                    int ex = step.X - cell.X;
                    int ey = step.Y - cell.Y;
                    int d = ex * ex + ey * ey;
                    if (d < gapSq) gapSq = d;
                }

                Assert.GreaterOrEqual(gapSq, 16,
                    "ore at " + cell + " must stand at least 4 tiles clear of every lane");
            }
        }

        [Test]
        public void TheRidgesAndRiver_ShieldTheFactoryFloor_AndGivePipeAClearShortcut()
        {
            MapDefinition map = Load().ToCore();

            for (int x = 11; x <= 38; x++)
                Assert.AreEqual(TerrainKind.Rock, map.TerrainAt(new Int2(x, 22)), "north ridge at x=" + x);
            for (int x = 11; x <= 37; x++)
                Assert.AreEqual(TerrainKind.Rock, map.TerrainAt(new Int2(x, 5)), "south ridge at x=" + x);
            for (int y = 6; y <= 14; y++)
                Assert.AreEqual(TerrainKind.Water, map.TerrainAt(new Int2(27, y)), "one-tile river at y=" + y);

            // The river stops short of the north ridge: the ground at its head is what lets a belt walk from
            // the east ore round the water instead of having to buy a Pipe for it.
            for (int y = 15; y <= 22; y++)
                Assert.AreNotEqual(TerrainKind.Water, map.TerrainAt(new Int2(27, y)),
                    "the river's head is open ground at y=" + y);

            // Both turret shelves cover their own canyon cleanly from behind rock.
            var world = new SimWorld(map, new SimConfig());
            float rangeSq = world.Content.Turret(BuildKind.Turret).Range;
            rangeSq *= rangeSq;
            var north = new List<Int2>();
            var south = new List<Int2>();
            world.TraceEnemyRoute(0, north);
            world.TraceEnemyRoute(1, south);

            Assert.GreaterOrEqual(Covered(north, new Int2(22, 21), rangeSq), 10);
            Assert.GreaterOrEqual(Covered(south, new Int2(22, 6), rangeSq), 10);
        }

        /// <summary>
        /// The map's first promise: money. A belt laid on the circle vein beside the Core and run into the
        /// Core's north face banks - and what it banks is a <em>shape</em>, because a shape is what the run
        /// spends.
        /// </summary>
        [Test]
        public void ACircleLineIntoTheCore_Banks()
        {
            MapDefinition frontier = Load().ToCore();
            var world = new SimWorld(Open(frontier, frontier.Waves), new SimConfig());

            Sim.LayRun(world, new Int2(3, 19), Dir.East, 5);    // (3,19) .. (7,19)
            Sim.LayRun(world, new Int2(8, 19), Dir.South, 4);   // (8,19) .. (8,16), into the Core at (8,15)
            Sim.Tick(world, 30 * 20);

            Assert.Greater(world.Economy.TotalBanked, 0,
                "a circle vein belted into the Core is money - the Core pays for the line that feeds it");
        }

        /// <summary>
        /// The map's second promise, and the reason the Pipe arrives on wave 2: the square vein is across a
        /// one-tile river that takes no belt, so a line only reaches it by hopping the water. Belted there and
        /// piped over, it banks exactly what a circle banks - <b>one shape</b> - which is the fact the coach's
        /// popup states in words on the wave the Pipe is handed over.
        /// </summary>
        [Test]
        public void ASquareLinePipedAcrossTheRiver_BanksLikeACircle()
        {
            MapDefinition frontier = Load().ToCore();
            var world = new SimWorld(Open(frontier, frontier.Waves), new SimConfig());

            Assert.IsFalse(world.TryPlaceBelt(new Int2(27, 10), Dir.West),
                "belts cannot cross the river - nothing but a Pipe gets the squares across");

            Sim.LayRun(world, new Int2(31, 10), Dir.West, 3);              // (31,10) .. (29,10)
            Sim.Place(world, BuildKind.Pipe, new Int2(28, 10), Dir.West);  // hops (27,10), lands on (26,10)
            Sim.LayRun(world, new Int2(26, 10), Dir.West, 17);             // (26,10) .. (10,10)
            Sim.LayRun(world, new Int2(9, 10), Dir.North, 3);              // (9,10) .. (9,12)
            Sim.LayRun(world, new Int2(9, 13), Dir.West, 1);               // into the Core's east face

            Sim.Tick(world, 30 * 30);

            Assert.Greater(world.Economy.TotalBanked, 0,
                "a full square piped over the river is money, exactly as a full circle is");
        }

        /// <summary>
        /// Wave 4 asks for the round the map's own chain makes - a circle half stacked on a square half -
        /// because the map mines both minerals and hands over the cutter and the stacker. A gun chambered with
        /// that round kills a mixed Tank in three matched shots, where the same tank fed plain circles takes
        /// far longer and the Core pays for it; the two outcomes together are what make the demand a demand
        /// rather than a damage number.
        ///
        /// The tank is spawned beside the gun rather than walked down the lane on purpose: which way a wave
        /// walks is <see cref="PathField"/>'s flanking judgement, and this is about the round.
        /// </summary>
        [Test]
        public void Wave4sMixedTank_IsAnsweredByTheStackedMixedRound_AndNotByACircle()
        {
            MapDefinition frontier = Load().ToCore();
            Shape mixed = Shape.Of(Quad.Circle, Quad.Circle, Quad.Square, Quad.Square);

            foreach (SpawnGroup group in frontier.Waves[3].Groups)
                Assert.AreEqual(mixed, group.Weakness, "wave 4 asks for the stacked round");

            int withMixed = ShotsToKill(frontier, mixed);
            int withCircle = ShotsToKill(frontier, Sim.Circle);

            Assert.Greater(withMixed, 0, "the gun fired at all");
            Assert.Less(withMixed, withCircle,
                "the round the map's chain builds is worth building: " + withMixed + " shots against " +
                withCircle + " with plain circles");
        }

        /// <summary>
        /// How many shots one gun needs to put a wave-4 Tank down, chambered with <paramref name="round"/> and
        /// restocked every tick. A tank is 10 health and a matched round does 4 at this map's mismatch share of
        /// 1/4, so a mismatched shot is 1 - which is the whole reason the map teaches the mixed round.
        /// </summary>
        private static int ShotsToKill(MapDefinition frontier, Shape round)
        {
            var idle = new[]
            {
                new WaveDefinition(3600f, new[] { new SpawnGroup(EnemyKind.Runner, 1, 0, 0f, 1f, Shape.Full(Quad.Circle)) }),
            };
            var world = new SimWorld(Open(frontier, idle), new SimConfig());

            var gun = new Int2(30, 14);
            var feed = new Int2(30, 15);
            Sim.LayRun(world, feed, Dir.South, 1);
            Sim.Place(world, BuildKind.Turret, gun, Dir.North);

            SpawnGroup tank = frontier.Waves[3].Groups[0];
            world.Enemies.Spawn(tank.Kind, new Vec2(30.5f, 16.5f), tank.Weakness);

            for (int t = 0; t < 30 * 60 && world.Enemies.AliveCount > 0; t++)
            {
                world.Belts.TrySpawnItem(feed, round);
                world.Tick(InputCommand.None);
            }

            return world.ShotsFired;
        }

        /// <summary>
        /// The shipped map, with its lesson removed and its wave table replaced, so a test can build its own
        /// layout on the same ground without a coach popup holding the clock or the map's own waves spawning
        /// into a test that is counting enemies.
        /// </summary>
        private static MapDefinition Open(MapDefinition frontier, WaveDefinition[] waves)
            => new MapDefinition(frontier.Name, frontier.Width, frontier.Height, 1000, frontier.Patches,
                frontier.SpawnPoints, waves, frontier.Terrain, frontier.CoreCell,
                frontier.Unlocked, frontier.Seed, frontier.GeneratedWaves, null, ResearchSchedule.None,
                frontier.MismatchDamage);

        private static void ClearWaveWithoutCombat(SimWorld world)
        {
            int target = world.Waves.Cleared + 1;
            world.Waves.StartNextWave();
            for (int i = 0; i < 3000 && world.Waves.Cleared < target; i++)
            {
                world.Enemies.Clear();
                world.Tick(InputCommand.None);
            }
            Assert.AreEqual(target, world.Waves.Cleared);
        }

        private static int Covered(List<Int2> route, Int2 gun, float rangeSq)
        {
            int covered = 0;
            foreach (Int2 step in route)
            {
                int dx = step.X - gun.X;
                int dy = step.Y - gun.Y;
                if (dx * dx + dy * dy <= rangeSq) covered++;
            }

            return covered;
        }
    }
}
