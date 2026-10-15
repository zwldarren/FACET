using System.Collections.Generic;
using System.Text;
using Facet.Core;
using NUnit.Framework;

namespace Facet.Tests
{
    /// <summary>
    /// The wave generator, which is the run's only source of pressure and the only randomness in Core
    /// (§5). Four properties make it a design tool rather than a random spawner, and each is asserted here
    /// against a map this file builds rather than against a shipped one:
    ///
    /// <list type="bullet">
    /// <item><b>Determinism.</b> One seed is the whole run, so the same seed and wave number always give
    /// the same wave - twice, in one generator, and across two.</item>
    /// <item><b>The legal pool.</b> Every demand it draws is an item this map can actually build. This is
    /// where the unlock ladder gets its teeth, and it is the one property whose failure is unwinnable.</item>
    /// <item><b>Depth as progression.</b> The opening waves are shallow and later ones are deep, and the
    /// archetype rides the depth.</item>
    /// <item><b>Budget.</b> Wave n's pressure grows on one curve, so difficulty is one number.</item>
    /// </list>
    /// </summary>
    public class WaveGeneratorTests
    {
        private static readonly Quad[] BothMinerals = { Quad.Circle, Quad.Square };
        private static readonly Quad[] CircleOnly = { Quad.Circle };

        /// <summary>Everything, from two veins: the far end of the campaign (§7).</summary>
        private static ShapePool FullPool() => new ShapePool(BothMinerals, Operators.All);

        private static WaveGenerator Generator(int seed, ShapePool pool, int doors = 2)
            => new WaveGenerator(seed, pool, doors);

        // ---------------------------------------------------------------- determinism

        [Test]
        public void TheSameSeedAndWaveNumber_GiveTheSameWave_EveryTimeTheyAreAsked()
        {
            // Purity is what lets the preview, the spawner and a test all read one answer rather than
            // three draws of one stream: a HUD asking for the preview every frame must not advance
            // anything, and wave 7 must not depend on how many times wave 3 was looked at.
            WaveGenerator generator = Generator(seed: 12345, FullPool());

            for (int n = 1; n <= 20; n++)
            {
                string first = Signature(generator.Wave(n));
                Assert.AreEqual(first, Signature(generator.Wave(n)), "wave " + n + " was not pure");
            }
        }

        [Test]
        public void TwoGeneratorsFromOneSeed_AgreeWaveForWave()
        {
            // The promise the seed in the HUD makes: a run is reproducible from the number on the screen,
            // on any machine, after any runtime upgrade - which is why the stream is arithmetic rather than
            // a framework's random generator.
            WaveGenerator a = Generator(seed: 99, FullPool());
            WaveGenerator b = Generator(seed: 99, FullPool());

            for (int n = 1; n <= 30; n++)
                Assert.AreEqual(Signature(a.Wave(n)), Signature(b.Wave(n)),
                    "two generators on seed 99 disagreed about wave " + n);
        }

        [Test]
        public void ADifferentSeed_IsADifferentRun()
        {
            // The other half of "there is exactly one source of randomness": it has to actually vary, or
            // the jitter is decoration and every run of a map is the same run.
            WaveGenerator a = Generator(seed: 1, FullPool());
            WaveGenerator b = Generator(seed: 2, FullPool());

            bool differs = false;
            for (int n = 1; n <= 30 && !differs; n++)
                differs = Signature(a.Wave(n)) != Signature(b.Wave(n));

            Assert.IsTrue(differs, "two seeds produced thirty identical waves");
        }

        [Test]
        public void ASeedOfZero_IsStillAWorkingStream()
        {
            // 0 is what a map author leaves in the field by accident, and a generator that answered
            // "nothing" for it would ship a map with no waves at all.
            WaveGenerator generator = Generator(seed: 0, FullPool());

            for (int n = 1; n <= 10; n++)
                Assert.Greater(generator.Wave(n).Total, 0, "wave " + n + " from seed 0 is empty");
        }

        // ---------------------------------------------------------------- the legal pool

        [Test]
        public void EveryDemandItDraws_IsAnItemTheMapCanActuallyBuild()
        {
            // The guardrail, and the one failure that is unwinnable rather than merely hard: brief a left
            // half-circle on a map with no rotator and no line on it could ever make one. Asserted over
            // every group of every wave rather than on a sample, because one bad draw is one bad run.
            ShapePool pool = FullPool();

            foreach (Operators unlocked in new[]
                     {
                         Operators.None, Operators.Cutter, Operators.Rotator, Operators.Stacker,
                         Operators.Cutter | Operators.Rotator, Operators.Cutter | Operators.Stacker,
                         Operators.All,
                     })
            {
                var ladder = new ShapePool(BothMinerals, unlocked);
                WaveGenerator generator = Generator(seed: 4242, ladder);

                for (int n = 1; n <= 20; n++)
                {
                    WaveDefinition wave = generator.Wave(n);
                    Assert.Greater(wave.Total, 0,
                        "wave " + n + " (" + unlocked + ") arrived with nothing in it");

                    foreach (SpawnGroup group in wave.Groups)
                    {
                        Assert.IsFalse(group.Weakness.IsEmpty,
                            "wave " + n + " (" + unlocked + ") briefed a group with no demand");
                        Assert.IsTrue(ladder.CanProduce(group.Weakness),
                            "wave " + n + " (" + unlocked + ") demands " + group.Weakness.Describe() +
                            ", which this map cannot build");
                        Assert.GreaterOrEqual(ladder.Depth(group.Weakness), 0);
                    }
                }
            }

            Assert.AreEqual(80, pool.Shapes.Count, "the full ladder really is the whole space");
        }

        [Test]
        public void AMapWithSquareOreButNoStacker_NeverDemandsAMixedShape()
        {
            // The ladder's middle rung (§7: map 2 adds the second mineral and the rotator, map 3 the
            // stacker). Without the stacker a mixed item is impossible - every piece a cut makes is one
            // mineral throughout - so briefing one would be a wave nobody could answer.
            var noStacker = new ShapePool(BothMinerals, Operators.Cutter | Operators.Rotator);
            WaveGenerator generator = Generator(seed: 777, noStacker);

            for (int n = 1; n <= 25; n++)
            {
                WaveDefinition wave = generator.Wave(n);
                Assert.Greater(wave.Total, 0, "wave " + n + " is empty");

                foreach (SpawnGroup group in wave.Groups)
                {
                    Assert.IsTrue(group.Weakness.IsPure(Quad.Circle) || group.Weakness.IsPure(Quad.Square),
                        "wave " + n + " demands " + group.Weakness.Describe() +
                        ", and this map has no stacker to make it");
                    Assert.IsFalse(group.Weakness.Contains(Quad.Circle) && group.Weakness.Contains(Quad.Square),
                        "a demand carrying both minerals is a mixed round");
                }
            }

            // A map that mines circles and squares and *has* the stacker does get mixed demands, so the
            // rule above is a rule about the unlock and not about the generator being timid.
            WaveGenerator full = Generator(seed: 777, FullPool());
            bool mixed = false;
            for (int n = 1; n <= 25 && !mixed; n++)
                foreach (SpawnGroup group in full.Wave(n).Groups)
                    mixed |= group.Weakness.Contains(Quad.Circle) && group.Weakness.Contains(Quad.Square);

            Assert.IsTrue(mixed, "with the stacker unlocked, mixed rounds do get briefed");
        }

        [Test]
        public void ACircleOnlyMap_OnlyEverDemandsCircleItems()
        {
            // The other end of the guardrail: no square ore, no square-family shape - "the closure of the
            // map's mined minerals under its unlocked operators", read as a test.
            var circleOnly = new ShapePool(CircleOnly, Operators.All);
            WaveGenerator generator = Generator(seed: 5, circleOnly);

            for (int n = 1; n <= 20; n++)
                foreach (SpawnGroup group in generator.Wave(n).Groups)
                    Assert.IsFalse(group.Weakness.Contains(Quad.Square),
                        "wave " + n + " demands a square-bearing item on a map with no square ore");
        }

        // ---------------------------------------------------------------- depth as progression

        [Test]
        public void TheOpeningWavesAreShallow_AndTheBandOpensAsTheRunGoesOn()
        {
            // The design's depth weighting (§5.4): "the first two waves draw from shallow tiers (halves,
            // quarters), shifting toward mixed and full shapes as waves advance". The cap is what makes
            // that true, and the archetype rides on the depth, so the same rule also decides how tough a
            // wave is - which is why a shallow demand comes as the fast fragile archetype.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 31, pool);

            Assert.AreEqual(1, generator.DepthCap(1), "the opening asks for what three machines can make");
            Assert.AreEqual(1, generator.DepthCap(2));
            Assert.AreEqual(2, generator.DepthCap(3), "and the band opens after the opening");
            Assert.AreEqual(2, generator.DepthCap(6));
            Assert.AreEqual(pool.MaxDepth, generator.DepthCap(7), "by wave 7 the whole ladder is in play");
            Assert.AreEqual(pool.MaxDepth, generator.DepthCap(30));
            Assert.IsNotEmpty(pool.AtDepth(1), "and the shallow tier the opening draws from has members");

            for (int n = 1; n <= 20; n++)
            {
                int cap = generator.DepthCap(n);
                foreach (SpawnGroup group in generator.Wave(n).Groups)
                {
                    int depth = pool.Depth(group.Weakness);
                    Assert.GreaterOrEqual(depth, 1, "wave " + n + " demanded a mined item, not a built one");
                    Assert.LessOrEqual(depth, cap, "wave " + n + " went past its own depth cap");

                    EnemyKind expected = depth <= 1 ? EnemyKind.Runner : EnemyKind.Tank;
                    Assert.AreEqual(expected, group.Kind,
                        "wave " + n + "'s depth-" + depth + " demand rode the wrong archetype");
                }
            }

            // The bands really do open: the deepest demand of the opening waves is shallower than the
            // deepest demand of a later window.
            Assert.Less(Deepest(generator, pool, 1, 2), Deepest(generator, pool, 10, 20),
                "later waves ask deeper than the opening does");
        }

        [Test]
        public void AMapWhoseDeepestItemIsShallow_CapsItsOwnDemandsAtThat()
        {
            // The cap is the map's own reach, not a constant: a circle-only map with a cutter cannot be
            // asked for anything past a quarter however long the run goes, which is why the band is
            // clamped rather than trusted.
            var shallow = new ShapePool(CircleOnly, Operators.Cutter);
            WaveGenerator generator = Generator(seed: 8, shallow);

            Assert.AreEqual(shallow.MaxDepth, generator.DepthCap(30));
            for (int n = 1; n <= 20; n++)
                foreach (SpawnGroup group in generator.Wave(n).Groups)
                    Assert.LessOrEqual(shallow.Depth(group.Weakness), shallow.MaxDepth);
        }

        // ---------------------------------------------------------------- the budget

        [Test]
        public void TheBudget_GrowsOnOneCurve_AndSoDoesTheWave()
        {
            // "The curve is the only place difficulty grows, and it is one number to retune": wave n
            // costs base * growth^(n-1), so the pressure a wave carries climbs monotonically - which is
            // the property a player feels as "it gets harder", and the one a retune must not break.
            Assert.AreEqual(WaveGenerator.BaseBudget, WaveGenerator.BudgetCurve(1), Sim.Tol);
            Assert.Greater(WaveGenerator.Growth, 1f, "growth above one is what makes it a curve");

            for (int n = 1; n < 30; n++)
                Assert.Greater(WaveGenerator.BudgetCurve(n + 1), WaveGenerator.BudgetCurve(n),
                    "the budget curve is not monotone at wave " + n);

            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 16, pool);

            Assert.Less(Enemies(generator, 1, 3), Enemies(generator, 10, 13),
                "later waves buy more enemies out of the same curve");

            for (int n = 1; n <= 20; n++)
                Assert.Greater(generator.Wave(n).Total, 0, "a wave is never empty, not even at wave " + n);
        }

        [Test]
        public void AJitteredBudget_NeverStraysFarFromTheCurve()
        {
            // The jitter is ±15%: enough that two runs of one map do not play out identically, little
            // enough that a surprise wave is never a wall. Measured over many waves rather than asserted
            // from the constant, because the jitter is a property of the stream - and measured in what the
            // wave *spent* rather than in how many enemies it holds, since a Tank costs two and a half
            // Runners and a count is not a budget. Both bounds matter: the ceiling is the jitter, and the
            // floor is the one that catches a wave which stopped early instead of buying out its budget,
            // which is the failure mode a smaller pool actually hits. Once the curve has climbed past what a
            // wave may hold, the floor is the ceiling instead - a capped wave has spent all a wave may spend.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 2024, pool);

            for (int n = 1; n <= 40; n++)
            {
                WaveDefinition wave = generator.Wave(n);
                float curve = WaveGenerator.BudgetCurve(n);
                float spent = Spend(wave);
                float reachable = System.Math.Min(curve * (1f - WaveGenerator.Jitter),
                    WaveGenerator.MaxEnemies * WaveGenerator.RunnerCost);

                Assert.Greater(wave.Total, 0, "a wave is never empty, not even at wave " + n);
                Assert.LessOrEqual(spent, curve * (1f + WaveGenerator.Jitter) + WaveGenerator.TankCost,
                    "wave " + n + " spent far past its budget");
                Assert.GreaterOrEqual(spent, reachable - WaveGenerator.TankCost,
                    "wave " + n + " left most of its budget unspent");
            }
        }

        [Test]
        public void TheGeneratedStretch_ContinuesTheAuthoredFinale_RatherThanDroppingBack()
        {
            // A map that scripts its opening hands the clock a generator that must pick up where the table
            // left off: the finale's budget is the generated curve's base, and the depth band never falls
            // below the deepest demand the table itself made. Without the handover the wave after the
            // finale was a quarter of the exam the player had just fought - and the deep line they built
            // for it sat idle through the first generated waves.
            ShapePool pool = FullPool();
            var generator = new WaveGenerator(11, pool, 1, authoredWaves: 3, authoredEndBudget: 47f,
                authoredEndDepth: 2);

            WaveDefinition wave = generator.Wave(4);
            float spent = Spend(wave);

            Assert.GreaterOrEqual(spent, 47f * (1f - WaveGenerator.Jitter) - WaveGenerator.TankCost,
                "wave 4 carries at least the finale's pressure, not a first wave's worth");
            Assert.LessOrEqual(spent, 47f * (1f + WaveGenerator.Jitter) + WaveGenerator.TankCost,
                "and the curve has grown from the finale, not past its own jitter");

            foreach (SpawnGroup group in wave.Groups)
            {
                Assert.IsTrue(pool.CanProduce(group.Weakness), "every demand is still buildable");
                Assert.GreaterOrEqual(pool.Depth(group.Weakness), 2,
                    "the band never asks for less depth than the table's finale taught");
            }
        }

        [Test]
        public void ADeepWaveNumber_IsCapped_AndAnsweredAtOnce()
        {
            // Two properties in one, and the second is why the ceiling is not merely a balance number: a wave
            // whose budget has outrun what a wave can hold is capped rather than asked for ten thousand
            // million million enemies, and a wave number in the hundreds is answered in one pass rather than
            // by a loop that spends the budget an enemy at a time. A preview on an endless run asks exactly
            // this, every frame it is open - and the suite would hang rather than fail if it regressed.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 5, pool);

            Assert.Greater(WaveGenerator.BudgetCurve(200),
                WaveGenerator.MaxEnemies * WaveGenerator.TankCost,
                "the test's premise: wave 200's budget is far past anything a wave may hold");

            var clock = System.Diagnostics.Stopwatch.StartNew();
            WaveDefinition deep = generator.Wave(200);
            clock.Stop();

            Assert.Less(clock.ElapsedMilliseconds, 2000,
                "a deep wave number is answered in one pass, not by buying its budget an enemy at a time");
            Assert.AreEqual(WaveGenerator.MaxEnemies, deep.Total, "and the wave is capped at its ceiling");
            Assert.LessOrEqual(deep.Groups.Length, WaveGenerator.MaxGroups);

            // It is still a wave: distinct pairs, each with a demand and a schedule, and each carrying more
            // than the single enemy a new pair is born with - that is where the budget went.
            var seen = new List<Shape>();
            foreach (SpawnGroup group in deep.Groups)
            {
                Assert.IsFalse(seen.Contains(group.Weakness), "a deep wave still briefs each shape once");
                seen.Add(group.Weakness);
                Assert.Greater(group.Count, 1, "and its counts are where the budget went");
                Assert.Greater(group.Interval, 0f);
            }
        }

        [Test]
        public void AMapThatCanOnlyMine_IsStillAskedForItsOwnOres()
        {
            // A map with veins and no unlocked operators can build nothing past the ores themselves, so the
            // mined minerals are the only demands it can be given. A band starting above them would ask for
            // something this map cannot make on every single wave and hand the director waves with nothing in
            // them - a bounded run won without a fight, and an endless one that advances through empty waves
            // for ever.
            var oresOnly = new ShapePool(BothMinerals, Operators.None);
            WaveGenerator generator = Generator(seed: 11, oresOnly);

            Assert.AreEqual(0, oresOnly.MaxDepth, "the test's premise: this map can only mine");
            Assert.AreEqual(0, generator.DepthCap(1));
            Assert.AreEqual(0, generator.DepthCap(30), "and it stays there however long the run goes");

            for (int n = 1; n <= 10; n++)
            {
                WaveDefinition wave = generator.Wave(n);
                Assert.Greater(wave.Total, 0, "wave " + n + " is empty");

                foreach (SpawnGroup group in wave.Groups)
                {
                    Assert.AreEqual(0, oresOnly.Depth(group.Weakness),
                        "wave " + n + " demands something this map cannot build");
                    Assert.AreEqual(EnemyKind.Runner, group.Kind, "a mined ore arrives as the fast archetype");
                }
            }
        }

        [Test]
        public void AWaveNeverBriefsTheSameShapeTwice()
        {
            // Sampling without replacement: within one wave the pool is drawn without repeats, so a wave
            // is a set of distinct decisions rather than the same demand wearing sixteen faces. It is also
            // what keeps the archetype-and-demand pair unique per group.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 606, pool);

            for (int n = 1; n <= 20; n++)
            {
                WaveDefinition wave = generator.Wave(n);
                var seen = new List<Shape>();

                foreach (SpawnGroup group in wave.Groups)
                {
                    Assert.IsFalse(seen.Contains(group.Weakness),
                        "wave " + n + " briefed " + group.Weakness.Describe() + " twice");
                    seen.Add(group.Weakness);
                }

                Assert.LessOrEqual(wave.Groups.Length, WaveGenerator.MaxGroups,
                    "a wave past the director's group bound would lose groups to its fixed timers");
            }
        }

        // ---------------------------------------------------------------- the director's half

        [Test]
        public void PastItsAuthoredTable_TheDirectorGenerates()
        {
            // A scripted opening and a generated run are one mechanism (§5): the map's table is played
            // first and the generator takes over, which is why a tutorial map can hand the rest of the run
            // to the curve without a second mode.
            SimWorld world = new SimWorld(GeneratedMap(authored: 2, generated: 12), new SimConfig());
            WaveDirector waves = world.Waves;

            Assert.IsFalse(waves.Endless, "a bounded map is not endless");
            Assert.AreEqual(2, waves.AuthoredCount);
            Assert.AreEqual(14, waves.TotalWaves, "the table plus what the generator answers");
            Assert.AreEqual(7, waves.Seed, "and the run's seed is the map's");

            Assert.AreSame(world.Map.Waves[0], waves.Definition(1), "the authored rows are played as written");
            Assert.AreSame(world.Map.Waves[1], waves.Definition(2));
            Assert.IsNotNull(waves.Definition(3), "and past the table the generator answers");
            Assert.IsNotNull(waves.Definition(14));
            Assert.IsNull(waves.Definition(15), "but only as far as the map asked for");
            Assert.IsNull(waves.Definition(0), "and there is no wave zero");
        }

        [Test]
        public void WithEndless_UnboundsACopyOfTheSameMap()
        {
            // §7's endless entry: the driver re-opens the campaign's last map with the generator unbounded,
            // and it starts from a copy - the authored data stays what the editor drew.
            MapDefinition bounded = GeneratedMap(authored: 2, generated: 12);
            MapDefinition endless = bounded.WithEndless();

            Assert.AreEqual(-1, endless.GeneratedWaves, "the copy is the endless one");
            Assert.IsTrue(endless.GeneratesWaves);
            Assert.AreEqual(bounded.Name, endless.Name, "the same map, only unbounded");
            Assert.AreEqual(bounded.Seed, endless.Seed, "the seed stays the host's per-run decision");
            Assert.AreSame(bounded.Waves, endless.Waves, "the authored table is carried over, not copied");
            Assert.AreEqual(12, bounded.GeneratedWaves, "and the map the copy came from is untouched");
        }

        [Test]
        public void PreviewingAWave_DoesNotDisturbTheOneBeingFought()
        {
            // The briefing shows the next wave AND the one after (§5.5), on every frame, so the lookups
            // have to be pure - a preview that advanced the stream would make the wave that arrives differ
            // from the wave that was announced.
            SimWorld world = new SimWorld(GeneratedMap(authored: 0, generated: -1), new SimConfig());
            WaveDirector waves = world.Waves;

            string third = Signature(waves.Definition(3));
            string seventh = Signature(waves.Definition(7));
            string thirdAgain = Signature(waves.Definition(3));

            Assert.AreEqual(third, thirdAgain, "asking for wave 7 changed wave 3");
            Assert.AreNotEqual(third, seventh, "and the test's premise: different waves differ");

            // The same two answers a briefing reads, in the order it reads them.
            Assert.IsNotNull(waves.NextWaveDefinition, "the opening waits on wave 1");
            Assert.IsNotNull(waves.WaveAfterNextDefinition, "and the one after it is previewable too");
            Assert.AreEqual(Signature(waves.Definition(1)), Signature(waves.NextWaveDefinition));
            Assert.AreEqual(Signature(waves.Definition(2)), Signature(waves.WaveAfterNextDefinition));
        }

        [Test]
        public void AMidGameRun_ShowsMoreThanTenDistinctShapes()
        {
            // The rework's own acceptance criterion (§9): "a mid-game run shows more than 10 distinct
            // shapes, and every wave forces at least one new decision". This is the generator's half of it,
            // measured where a mid-game run actually is - the window after the depth band has opened.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 20240607, pool);
            var distinct = new HashSet<Shape>();

            for (int n = 5; n <= 12; n++)
                foreach (SpawnGroup group in generator.Wave(n).Groups)
                    distinct.Add(group.Weakness);

            Assert.Greater(distinct.Count, 10,
                "waves 5..12 of a two-mineral, all-operator map briefed only " + distinct.Count +
                " distinct shapes");

            // And they are worth reading: a set that was all quarters would say nothing about the ladder.
            int deepest = 0;
            foreach (Shape shape in distinct) deepest = System.Math.Max(deepest, pool.Depth(shape));

            Assert.GreaterOrEqual(deepest, 3, "the window should reach past the opening's halves and quarters");
        }

        [Test]
        public void EveryGroup_EntersAtADoorTheMapHas_AndStaggered()
        {
            // The schedule is what makes a wave readable as it arrives: groups enter in turn, each behind
            // the one before it, and a wave with more groups than doors wraps round them.
            ShapePool pool = FullPool();
            WaveGenerator generator = Generator(seed: 3, pool, doors: 3);

            for (int n = 1; n <= 20; n++)
            {
                WaveDefinition wave = generator.Wave(n);
                for (int g = 0; g < wave.Groups.Length; g++)
                {
                    SpawnGroup group = wave.Groups[g];

                    Assert.GreaterOrEqual(group.SpawnPoint, 0);
                    Assert.Less(group.SpawnPoint, 3, "wave " + n + " named a door the map does not have");
                    Assert.GreaterOrEqual(group.FirstDelay, 0f);
                    Assert.Greater(group.Interval, 0f, "a group with no spacing would arrive as one block");
                    Assert.GreaterOrEqual(group.Count, 1);
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A map with one circle vein and one square vein, a door, and as many authored waves as
        /// asked for - the shape the campaign's later maps have.</summary>
        private static MapDefinition GeneratedMap(int authored, int generated)
        {
            var table = new WaveDefinition[authored];
            for (int i = 0; i < authored; i++)
                table[i] = new WaveDefinition(15f, new SpawnGroup(EnemyKind.Runner, 1, 0, 0f, 1f, Sim.Circle));

            return new MapDefinition("generated", 32, 20, Sim.TestCircles,
                new[]
                {
                    new ShapePatch(new Int2(2, 2), 1, 1, Quad.Circle),
                    new ShapePatch(new Int2(2, 4), 1, 1, Quad.Square),
                },
                new[] { new Int2(16, 18) },
                table,
                coreCell: new Int2(14, 8),
                unlocked: Operators.All,
                seed: 7,
                generatedWaves: generated);
        }

        /// <summary>Everything about a wave that a player would notice, as one comparable string - the
        /// shape of the group list, the schedule and each group's demand.</summary>
        private static string Signature(WaveDefinition wave)
        {
            if (wave == null) return "(none)";

            var text = new StringBuilder();
            text.Append(wave.Intermission.ToString("0.###")).Append(" [");
            foreach (SpawnGroup group in wave.Groups)
            {
                text.Append(group.Kind).Append('x').Append(group.Count)
                    .Append('@').Append(group.SpawnPoint)
                    .Append('+').Append(group.FirstDelay.ToString("0.###"))
                    .Append('/').Append(group.Interval.ToString("0.###"))
                    .Append('=').Append(group.Weakness.Code).Append("; ");
            }

            return text.Append(']').ToString();
        }

        /// <summary>The deepest demand in a window of waves - the number the depth bands are about.</summary>
        private static int Deepest(WaveGenerator generator, ShapePool pool, int from, int to)
        {
            int deepest = 0;
            for (int n = from; n <= to; n++)
                foreach (SpawnGroup group in generator.Wave(n).Groups)
                    deepest = System.Math.Max(deepest, pool.Depth(group.Weakness));

            return deepest;
        }

        /// <summary>How many enemies a window of waves adds up to, so "the budget grows" can be read as
        /// something a player actually faces.</summary>
        private static int Enemies(WaveGenerator generator, int from, int to)
        {
            int total = 0;
            for (int n = from; n <= to; n++) total += generator.Wave(n).Total;
            return total;
        }

        /// <summary>What a wave costs the budget: its groups' counts weighted by their archetype. The number
        /// the curve is about, unlike the raw count - one Tank is worth two and a half Runners.</summary>
        private static float Spend(WaveDefinition wave)
        {
            float spent = 0f;
            foreach (SpawnGroup group in wave.Groups)
                spent += group.Count * (group.Kind == EnemyKind.Runner
                    ? WaveGenerator.RunnerCost
                    : WaveGenerator.TankCost);

            return spent;
        }
    }
}
