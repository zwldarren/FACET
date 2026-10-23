using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The map asset and the conversion that makes it a map. The asset is the Editor's carrier - what a designer
    /// paints and the painting window writes into - while the simulation only sees the <see cref="MapDefinition"/>
    /// <see cref="MapAsset.ToCore"/> hands over. This file is about that boundary being exact: painted cells in,
    /// cells and patches out, nothing invented on the way.
    /// </summary>
    public class MapAssetTests
    {
        /// <summary>A demand every authored group in this file carries, so the maps here are held to the rule
        /// that a group names the exact item it is weak to - the one wave error a designer can make that is
        /// invisible at runtime.</summary>
        private static ShapeField Demand(Quad mineral) => new ShapeField(mineral, Quad.Empty, Quad.Empty, Quad.Empty);

        private MapAsset _asset;

        [SetUp]
        public void CreateAsset()
        {
            _asset = ScriptableObject.CreateInstance<MapAsset>();
            _asset.Name = "Painted";
            _asset.Width = 12;
            _asset.Height = 10;
            _asset.StartShapes = 55;
            _asset.CoreCell = new Vector2Int(2, 2);
            _asset.SpawnPoints = new[] { new Vector2Int(0, 9), new Vector2Int(11, 0) };
            _asset.Waves = new[]
            {
                new MapAsset.WaveRow
                {
                    Intermission = 0f,
                    Groups = new[]
                    {
                        new MapAsset.SpawnGroupRow
                        {
                            Kind = EnemyKind.Runner, Count = 4, SpawnPoint = 0, Interval = 2f,
                            Weakness = Demand(Quad.Circle),
                        },
                        new MapAsset.SpawnGroupRow
                        {
                            Kind = EnemyKind.Tank, Count = 2, SpawnPoint = 1, Interval = 1.5f,
                            Weakness = Demand(Quad.Square),
                        },
                    },
                },
                new MapAsset.WaveRow { Intermission = 45f },
            };
        }

        [TearDown]
        public void DestroyAsset()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        [Test]
        public void ToCore_APaintedMap_IsTheMapTheSimulationRunsOn()
        {
            // The Core takes the block at (2,2)..(5,5), so everything painted here is deliberately outside it:
            // the Core keeps its tiles, and a rock dropped on one is a map error, not a test of the conversion.
            _asset.Paint(4, 7, TerrainKind.Rock);
            _asset.Paint(5, 7, TerrainKind.Rock);
            _asset.Paint(7, 8, TerrainKind.Water);
            _asset.Paint(0, 0, TerrainKind.Void);
            _asset.PaintOre(8, 2, Quad.Square);
            _asset.PaintOre(9, 2, Quad.Square);

            MapDefinition map = _asset.ToCore();

            Assert.AreEqual("Painted", map.Name);
            Assert.AreEqual(12, map.Width);
            Assert.AreEqual(10, map.Height);
            Assert.AreEqual(55, map.StartShapes);
            Assert.AreEqual(new Int2(2, 2), map.CoreCell, "the Core goes where the asset put it");
            Assert.AreEqual(new[] { new Int2(0, 9), new Int2(11, 0) }, map.SpawnPoints);

            Assert.AreEqual(TerrainKind.Rock, map.TerrainAt(new Int2(4, 7)));
            Assert.AreEqual(TerrainKind.Rock, map.TerrainAt(new Int2(5, 7)));
            Assert.AreEqual(TerrainKind.Water, map.TerrainAt(new Int2(7, 8)));
            Assert.AreEqual(TerrainKind.Void, map.TerrainAt(new Int2(0, 0)));
            Assert.AreEqual(TerrainKind.Ground, map.TerrainAt(new Int2(1, 1)), "and the rest is plain ground");
            Assert.AreEqual(TerrainKind.Ground, map.TerrainAt(new Int2(4, 4)),
                "the Core's own tiles are not terrain, however the map is painted");

            // A painted vein crosses as one rectangle per row it spans - the shape the runtime states ore in -
            // and it carries the mineral, because ore without one is not a patch at all.
            Assert.AreEqual(1, map.Patches.Length, "two adjacent cells in a row are one patch");
            Assert.AreEqual(new Int2(8, 2), map.Patches[0].Origin);
            Assert.AreEqual(2, map.Patches[0].Width);
            Assert.AreEqual(1, map.Patches[0].Height);
            Assert.AreEqual(Quad.Square, map.Patches[0].Mineral);

            Assert.AreEqual(2, map.Waves.Length);
            Assert.AreEqual(2, map.Waves[0].Groups.Length);
            Assert.AreEqual(6, map.Waves[0].Total, "four Runners and two Tanks");
            Assert.AreEqual(0, map.Waves[1].Total, "and the second wave is the empty one it was authored as");
            Assert.AreEqual(map.Waves[0].Groups[1].Weakness,
                Shape.Quarter(Shape.NW, Quad.Square),
                "a group's demand crosses as the exact item it names");

            // And the map it describes is one a world can actually be built on: the point of the conversion is
            // that both halves of the game agree about it.
            var world = new SimWorld(map, new SimConfig());
            Assert.AreEqual(new Int2(2, 2), world.Core.Cell);
            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(new Int2(5, 7)));
            Assert.AreEqual(TerrainKind.Ore, world.Terrain.KindAt(new Int2(8, 2)),
                "the painted vein is ore in the ground the world stamped");
            Assert.IsTrue(world.Terrain.HasOre(new Int2(9, 2)));
            Assert.AreEqual(Quad.Square, world.Terrain.OreAt(new Int2(9, 2)));
            Assert.IsFalse(world.CanPlaceBelt(new Int2(0, 0)), "and the void takes nothing");
        }

        [Test]
        public void ToCore_CarriesTheProgression_SoAMapsLadderIsItsOwnData()
        {
            // The rework's one addition to a map: which operators it hands over, the seed its waves are
            // generated from, and whether it generates at all. All three are the map's, not the campaign's -
            // progression is not a map ordinal.
            _asset.Unlocked = Operators.Cutter | Operators.Rotator;
            _asset.Seed = 4242;
            _asset.GeneratedWaves = 7;

            MapDefinition map = _asset.ToCore();

            Assert.AreEqual(Operators.Cutter | Operators.Rotator, map.Unlocked);
            Assert.AreEqual(4242, map.Seed);
            Assert.AreEqual(7, map.GeneratedWaves);
            Assert.IsTrue(map.GeneratesWaves);

            // And the ladder reaches the simulation: the operator a map has not unlocked cannot be built on it.
            var world = new SimWorld(map, new SimConfig());
            Assert.IsTrue(world.IsUnlocked(BuildKind.Cutter));
            Assert.IsTrue(world.IsUnlocked(BuildKind.Rotator));
            Assert.IsFalse(world.IsUnlocked(BuildKind.Stacker), "the stacker is not this map's to give");
            Assert.IsTrue(world.IsUnlocked(BuildKind.Drill), "and the scaffolding needs no unlocking at all");
        }

        [Test]
        public void ToCore_AMapWithNoObstacles_HandsOverNoTerrainArrayAtAll()
        {
            // A null terrain array already means "all plain ground" to the runtime, so a map painted purely with
            // ground hands over nothing rather than a grid of zeroes - the same choice the shipped maps make.
            MapDefinition map = _asset.ToCore();

            Assert.IsNull(map.Terrain, "nothing was painted, so there is no terrain to state");
            Assert.IsEmpty(map.Patches);
            Assert.IsTrue(new SimWorld(map, new SimConfig()).CanPlaceBelt(new Int2(0, 0)),
                "every tile of it is buildable ground");
        }

        [Test]
        public void Paint_TakesTheMineralWithTheGround()
        {
            _asset.PaintOre(3, 3, Quad.Circle);

            MapDefinition painted = _asset.ToCore();
            Assert.AreEqual(TerrainKind.Ground, painted.TerrainAt(new Int2(3, 3)),
                "the array under a vein is plain");
            Assert.AreEqual(TerrainKind.Ore, painted.AuthoredGroundAt(new Int2(3, 3)),
                "and the vein is the patch the list turned into");
            Assert.AreEqual(1, painted.Patches.Length);
            Assert.AreEqual(Quad.Circle, painted.Patches[0].Mineral);

            // Painting over it is what a designer does when they change their mind: what they must not be left
            // with is a rock that still yields a mineral.
            _asset.Paint(3, 3, TerrainKind.Rock);

            Assert.AreEqual(0, _asset.Ore.Length, "the mineral went with the vein");
            Assert.AreEqual(TerrainKind.Rock, _asset.ToCore().TerrainAt(new Int2(3, 3)));
        }

        [Test]
        public void PaintOre_TwiceOnOneCell_KeepsOneCell()
        {
            // The window paints with a dragged pointer, so the same cell arrives more than once: a second
            // paint is a change of mineral, not a second vein.
            _asset.PaintOre(3, 3, Quad.Circle);
            _asset.PaintOre(3, 3, Quad.Square);

            Assert.AreEqual(1, _asset.Ore.Length);
            Assert.AreEqual(Quad.Square, _asset.Ore[0].Mineral);
            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(3, 3),
                "and the ground under it is still plain: a vein is the one entry");
        }

        [Test]
        public void Validate_AVeinWrittenByTheBrush_IsNotAProblem()
        {
            // The two rules the writer keeps: the array never says ore, and the list never names a cell twice. A
            // brush stroke breaking either would be a map error no designer could see, so painting is held to them.
            _asset.PaintOre(7, 7, Quad.Circle);
            List<MapProblem> problems = _asset.Validate();

            AssertNoProblem(problems, MapProblemKind.OreInTerrain);
            AssertNoProblem(problems, MapProblemKind.DuplicateOre);
        }

        [Test]
        public void Validate_AGroupWithNoDemand_IsReported()
        {
            // The wave rule the rework added, and the reason a group carries a shape at all: a group weak to
            // nothing can only ever be shot at a half damage, so the map is a wave nobody can beat rather
            // than a hard one. Painting is where a designer finds that out.
            _asset.Waves = new[]
            {
                new MapAsset.WaveRow
                {
                    Intermission = 0f,
                    Groups = new[]
                    {
                        new MapAsset.SpawnGroupRow { Kind = EnemyKind.Runner, Count = 3, Interval = 1f },
                    },
                },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.GroupWithoutDemand, new Int2(-1, -1));

            _asset.Waves[0].Groups[0].Weakness = Demand(Quad.Circle);
            AssertNoProblem(_asset.Validate(), MapProblemKind.GroupWithoutDemand);
        }

        [Test]
        public void Validate_ATerrainArrayHandwrittenToSayOre_IsReported()
        {
            // What a hand edit of the array buys: nothing, because the ore list is what the conversion claims
            // cells from - so it is named rather than silently ignored.
            _asset.Terrain = TerrainLayout.Rects(12, 10, new TerrainRect(new Int2(7, 7), 1, 1, TerrainKind.Ore));

            AssertProblem(_asset.Validate(), MapProblemKind.OreInTerrain, new Int2(7, 7));
        }

        [Test]
        public void Validate_TwoEntriesForOneCell_IsReportedAsTheSecondOneBeingIgnored()
        {
            // Hand-appending an entry for a cell the list already has is an edit that does nothing: the first
            // entry is the one that counts, here and in the conversion alike.
            _asset.Ore = new[]
            {
                new MapAsset.OreCell { Cell = new Vector2Int(7, 7), Mineral = Quad.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(7, 7), Mineral = Quad.Square },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.DuplicateOre, new Int2(7, 7));
            Assert.AreEqual(Quad.Circle, _asset.DrawableView().MineralAt(7, 7), "and the first entry wins");
        }

        [Test]
        public void Erase_IsGroundAndNoMineral()
        {
            _asset.PaintOre(3, 3, Quad.Circle);
            _asset.Paint(4, 3, TerrainKind.Water);

            _asset.Erase(3, 3);
            _asset.Erase(4, 3);

            MapDefinition map = _asset.ToCore();
            Assert.AreEqual(TerrainKind.Ground, map.TerrainAt(new Int2(3, 3)));
            Assert.AreEqual(TerrainKind.Ground, map.TerrainAt(new Int2(4, 3)));
            Assert.IsNull(map.Terrain, "erasing what was the only terrain leaves the map plain again");
        }

        [Test]
        public void Resize_KeepsWhatFits_AndDropsWhatDoesNot()
        {
            _asset.Paint(0, 0, TerrainKind.Rock);
            _asset.Paint(5, 5, TerrainKind.Water);
            _asset.PaintOre(1, 1, Quad.Circle);
            _asset.PaintOre(5, 5, Quad.Square);

            _asset.Resize(3, 3);

            Assert.AreEqual(3, _asset.Width);
            Assert.AreEqual(3, _asset.Height);
            Assert.AreEqual(9, _asset.Terrain.Length, "the terrain array follows the size");

            MapDefinition map = _asset.ToCore();
            Assert.AreEqual(TerrainKind.Rock, map.TerrainAt(new Int2(0, 0)), "the corner is where it was");
            Assert.AreEqual(TerrainKind.Ground, map.TerrainAt(new Int2(2, 2)), "and the rest is the new ground");

            Assert.AreEqual(1, _asset.Ore.Length, "the vein that no longer fits is gone");
            Assert.AreEqual(new Vector2Int(1, 1), _asset.Ore[0].Cell);
        }

        [Test]
        public void Resize_DoesNotShearTheMap()
        {
            // The terrain array is a flat grid, so a naive "copy the first n cells" resize turns a wider map into
            // a diagonal smear: row 0 of the old map runs into row 1 of the new. This test catches it.
            _asset.Paint(0, 0, TerrainKind.Rock);
            _asset.Paint(0, 1, TerrainKind.Water);

            _asset.Resize(6, 10);

            Assert.AreEqual(TerrainKind.Rock, _asset.TerrainAt(0, 0));
            Assert.AreEqual(TerrainKind.Water, _asset.TerrainAt(0, 1));
            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(1, 0), "and nothing was smeared along the row");
        }

        [Test]
        public void Validate_ReportsAMapNoWaveCanWalkInto()
        {
            // The whole point of validating an asset is that it says so while it is being painted. Here
            // the designer has walled off the only door they made.
            _asset.SpawnPoints = new[] { new Vector2Int(0, 5) };
            _asset.Waves = new[]
            {
                new MapAsset.WaveRow
                {
                    Intermission = 0f,
                    Groups = new[]
                    {
                        new MapAsset.SpawnGroupRow
                        {
                            Kind = EnemyKind.Runner, Count = 3, Interval = 1f,
                            Weakness = Demand(Quad.Circle),
                        },
                    },
                },
            };
            _asset.CoreCell = new Vector2Int(8, 5);

            for (int y = 0; y < _asset.Height; y++) _asset.Paint(4, y, TerrainKind.Rock);

            List<MapProblem> problems = _asset.Validate();

            Assert.IsNotEmpty(problems, "a wall between the door and the Core is a map nobody can play");
            bool unreachable = false;
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == MapProblemKind.SpawnUnreachable) unreachable = true;

            Assert.IsTrue(unreachable, "and the reason is the door, not something else entirely");
        }

        // ------------------------------------------------------------------ the structural edits
        //
        // The lists used to be the interface: editors appended doors by hand, copied to a List, reassigned the
        // array and remembered the bounds and dedupe, with nothing to answer "changed". The operations now own
        // that and return whether anything changed, which automated tests can reach.

        [Test]
        public void StructuralEdits_AnswerWhetherAnythingChanged()
        {
            Assert.IsTrue(_asset.TryAddDoor(4, 5));
            Assert.IsFalse(_asset.TryAddDoor(4, 5), "two doors at one tile would be two names for one place");
            Assert.IsFalse(_asset.TryAddDoor(40, 5), "and a tile off the map is not a tile");

            Assert.IsTrue(_asset.TryRemoveDoor(4, 5));
            Assert.IsFalse(_asset.TryRemoveDoor(4, 5), "an erase dragged over open ground is not a change");
            Assert.AreEqual(2, _asset.SpawnPoints.Length, "the round trip left the authored doors alone");

            Assert.IsFalse(_asset.SetCore(2, 2), "the Core is where the map says it already is");
            Assert.IsTrue(_asset.SetCore(7, 1));
            Assert.IsTrue(_asset.CentreCore(), "centring a moved Core is a change");
            Assert.IsFalse(_asset.CentreCore(), "and centring what the field already centres is not");

            Assert.IsFalse(_asset.SetSize(12, 10), "an apply that changes nothing is not an edit");
            Assert.IsTrue(_asset.SetSize(20, 0), "and the one-tile floor is the asset's own");
            Assert.AreEqual(20, _asset.Width);
            Assert.AreEqual(1, _asset.Height);
        }

        [Test]
        public void WaveEdits_KeepTheArrayIdentity_AndAnswerForNullRows()
        {
            Assert.AreEqual(2, _asset.Waves.Length);
            Assert.IsTrue(_asset.AddWave());
            Assert.AreEqual(3, _asset.Waves.Length);

            Assert.IsTrue(_asset.AddWaveGroup(2));
            Assert.AreEqual(1, _asset.Waves[2].Groups.Length, "a group dropped into a wave is a group the runtime can spawn");
            Assert.IsTrue(_asset.RemoveWaveGroup(2, 0));
            Assert.IsEmpty(_asset.Waves[2].Groups);
            Assert.IsFalse(_asset.RemoveWaveGroup(2, 0), "and removing from empty says nothing happened");

            Assert.IsTrue(_asset.RemoveWave(2));
            Assert.IsFalse(_asset.RemoveWave(2), "the table now ends before that row");

            _asset.Waves = new MapAsset.WaveRow[] { null };
            Assert.IsFalse(_asset.AddWaveGroup(0), "a row the Inspector can leave empty gets nothing invented into it");
            Assert.IsTrue(_asset.RemoveWave(0));
        }

        [Test]
        public void Queries_AnswerAboutTheAsset_AndWriteNothing()
        {
            // A repaint used to resize the map on its way through Validate - a mutation wearing a query's
            // signature, no Undo entry, no dirty flag. The read path now floors and lays out inside its answer.
            _asset.Width = 0;
            _asset.Height = 5;
            _asset.Terrain = new TerrainKind[3];

            _asset.Validate();
            _asset.DrawableView();
            int digest = _asset.ContentHash();
            MapDefinition map = _asset.ToCore();

            Assert.AreEqual(0, _asset.Width, "the questions left the size field alone");
            Assert.AreEqual(3, _asset.Terrain.Length, "and the half-edited terrain array is still half-edited");
            Assert.AreEqual(1, map.Width, "while the conversion answered with the floors the runtime uses");
            Assert.AreEqual(5, map.Height);
            Assert.AreNotEqual(0, digest, "and even a broken asset walks without throwing at a repaint");
        }

        // ------------------------------------------------------------------ the one canonical walk

        [Test]
        public void TheHash_MovesWithThePicture_AndOnlyWithIt()
        {
            int open = _asset.ContentHash();

            _asset.Paint(6, 6, TerrainKind.Rock);
            int painted = _asset.ContentHash();
            Assert.AreNotEqual(open, painted, "a painted tile changes what draws");

            _asset.Name = "renamed";
            _asset.StartShapes = 4242;
            Assert.AreEqual(painted, _asset.ContentHash(),
                "what nothing draws changes nothing: the name and the stockpile are not pixels");

            Assert.IsTrue(_asset.TryAddDoor(1, 1));
            Assert.AreNotEqual(painted, _asset.ContentHash(), "a door is drawn, so a door is counted");
        }

        [Test]
        public void TheHash_IsThePictureNotTheAuthoringOrder()
        {
            // The same map, two hand-written spellings: the ore list in the other insertion order, each with a
            // junk entry off the map. What draws is one picture either way, but the digest used to hash the raw
            // list - junk and order included.
            _asset.PaintOre(2, 6, Quad.Circle);
            _asset.PaintOre(3, 6, Quad.Circle);
            int forward = _asset.ContentHash();

            _asset.Ore = new[]
            {
                new MapAsset.OreCell { Cell = new Vector2Int(3, 6), Mineral = Quad.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(2, 6), Mineral = Quad.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(40, 40), Mineral = Quad.Square },
            };

            Assert.AreEqual(forward, _asset.ContentHash(),
                "the same cells claimed in the other order, with junk past the edge, draw the same picture");

            MapDefinition map = _asset.ToCore();
            DrawnMap drawn = _asset.DrawableView();
            Assert.AreEqual(Quad.Circle, drawn.MineralAt(2, 6));
            Assert.AreEqual(1, map.Patches.Length, "the row-run rule is the walk's too");
            Assert.AreEqual(new Int2(2, 6), map.Patches[0].Origin);
            Assert.AreEqual(2, map.Patches[0].Width);
        }

        [Test]
        public void TheWalk_ClaimsCells_TheWayTheConversionClaimsThem()
        {
            // The conversion's rule is that the FIRST ore entry for a cell counts, whatever it says; the walk
            // shares that rule, since one canonical walk means the picture and the patches are the same
            // enumeration twice.
            _asset.Ore = new[]
            {
                new MapAsset.OreCell { Cell = new Vector2Int(5, 5), Mineral = Quad.Empty },
                new MapAsset.OreCell { Cell = new Vector2Int(5, 5), Mineral = Quad.Circle },
            };

            Assert.AreEqual(Quad.Empty, _asset.DrawableView().MineralAt(5, 5), "first entry wins, here");
            Assert.IsEmpty(_asset.ToCore().Patches, "and in the conversion");
        }

        [Test]
        public void TheWalk_KeepsTheDoorPositionTheWaveTableNames()
        {
            // Doors are drawn numbered by the position a wave group names them by, so an off-map door is
            // invisible but still occupies its slot: dropping it from the walk would renumber every door behind it.
            _asset.SpawnPoints = new[] { new Vector2Int(0, 9), new Vector2Int(40, 2), new Vector2Int(11, 0) };
            DrawnMap drawn = _asset.DrawableView();

            Assert.AreEqual(2, drawn.DoorCells.Length, "the off-map door is not on any tile");
            Assert.AreEqual(new Vector2Int(0, 9), drawn.DoorCells[0]);
            Assert.AreEqual(0, drawn.DoorPositions[0]);
            Assert.AreEqual(new Vector2Int(11, 0), drawn.DoorCells[1]);
            Assert.AreEqual(2, drawn.DoorPositions[1], "and the third door is still named 2 by a wave group");
        }

        [Test]
        public void TheWalk_IsWhatTheConversionRunsOn()
        {
            // Every cell the picture draws and every cell the runtime gets are the same answer - the agreement
            // the four hand-walks used to be hoped for and never tested.
            _asset.Paint(1, 8, TerrainKind.Void);
            _asset.PaintOre(6, 1, Quad.Square);

            MapDefinition map = _asset.ToCore();
            DrawnMap drawn = _asset.DrawableView();

            for (int y = 0; y < drawn.Height; y++)
                for (int x = 0; x < drawn.Width; x++)
                    Assert.AreEqual(map.TerrainAt(new Int2(x, y)), drawn.GroundAt(x, y), "cell " + x + "," + y);

            Assert.AreEqual(Quad.Square, drawn.MineralAt(6, 1));
            Assert.AreEqual(TerrainKind.Ground, drawn.GroundAt(6, 1),
                "a vein's ground is plain, as it is in the array - the mineral is what says there is a vein");
            Assert.AreEqual(map.CoreCell.X, drawn.Core.x);
            Assert.AreEqual(map.CoreCell.Y, drawn.Core.y);
            Assert.AreEqual(Quad.Empty, drawn.MineralAt(0, 0), "plain ground carries no mineral");
        }

        // ------------------------------------------------------------------ the tutorial rows

        [Test]
        public void Tutorial_AnEmptyTaskList_IsAMapWithoutATutorial()
        {
            // An empty list is the whole answer, so there is no second "has a tutorial" flag to fall out of
            // step with it - and a Starting set on its own teaches nothing.
            _asset.TutorialStarting = new[] { BuildKind.Belt };
            _asset.TutorialTasks = new MapAsset.TutorialTaskRow[0];

            Assert.IsNull(_asset.ToCore().Tutorial);
            AssertNoProblem(_asset.Validate(), MapProblemKind.TutorialStepUnreachable);
        }

        [Test]
        public void Tutorial_ARow_KeepsItsLineAndOnlyAPositiveTargetPointsSomewhere()
        {
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.PlaceDrill, Text = "Drill the vein.",
                    Target = new Vector2Int(3, 4), Opens = new[] { BuildKind.Belt }, StartsWave = true,
                },
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.BankShape, Text = "Bank a shape.",
                    Target = new Vector2Int(-1, -1),
                },
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.BeltFork, Text = "Fork the lane.", Target = new Vector2Int(-1, 4),
                },
            };

            TutorialScript script = _asset.ToCore().Tutorial;
            Assert.IsNotNull(script);
            Assert.AreEqual(3, script.Count);
            Assert.AreEqual("Drill the vein.", script[0].Text);
            Assert.AreEqual(new Int2(3, 4), script[0].Target.Value);
            Assert.AreEqual(1, script[0].Opens.Length);
            Assert.AreEqual(BuildKind.Belt, script[0].Opens[0], "the line hands the belt over when it ticks");
            Assert.IsTrue(script[0].StartsWave);
            Assert.IsNull(script[1].Target, "(-1,-1) is the Inspector's no place to point");
            Assert.IsNull(script[2].Target, "and a half-negative cell is no place either");
            Assert.IsFalse(script[2].StartsWave);
        }

        [Test]
        public void Tutorial_ANullRow_IsALineThatCanNeverTick()
        {
            // A hole is not a PlaceDrill line: that would tick the moment any drill stands, which is what a
            // broken row must not be allowed to do.
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow { Step = TutorialStep.PlaceDrill, Text = "Drill." },
                null,
                new MapAsset.TutorialTaskRow { Step = TutorialStep.BankShape, Text = "Bank." },
            };

            TutorialScript script = _asset.ToCore().Tutorial;
            Assert.AreEqual(3, script.Count, "the list's length is what the HUD counts against");
            Assert.AreEqual(TutorialScript.NoStep, script[1].Step);
            Assert.IsFalse(string.IsNullOrEmpty(script[1].Text), "and the missing line is not silent");

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialLineWithoutText, new Int2(-1, -1));
        }

        [Test]
        public void Validate_ATutorialLineWithNoText_IsReported()
        {
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow { Step = TutorialStep.PlaceDrill, Text = "  " },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialLineWithoutText, new Int2(-1, -1));
        }

        [Test]
        public void Validate_ALineLongerThanTheCard_IsReported()
        {
            // The task card's micro type shrinks to a floor, and past it the sentence would spill past the
            // card onto the map - so the painting window says so, once for an over-long line and once for an
            // over-long hint. The line at exactly the budget is the boundary the rule must not trip on.
            _asset.Unlocked = Operators.All;
            _asset.TutorialStarting = new[] { BuildKind.Drill };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow { Step = TutorialStep.PlaceDrill, Text = new string('x', 89) },
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.BankShape, Text = new string('x', 88), Hint = new string('x', 89),
                },
            };

            List<MapProblem> problems = _asset.Validate();
            AssertProblem(problems, MapProblemKind.TutorialLineTooLong, new Int2(-1, -1));

            int reported = 0;
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == MapProblemKind.TutorialLineTooLong) reported++;
            Assert.AreEqual(2, reported,
                "the over-long text and the over-long hint are two sentences, so they are two problems - " +
                "and the at-budget line is not one");
        }

        [Test]
        public void Validate_AGuideTargetOffTheMap_IsReported()
        {
            // The asset is 12 wide, so column 12 is the first tile off it - an arrow pointing at nothing.
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.PlaceDrill, Text = "Drill.", Target = new Vector2Int(12, 3),
                },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialTargetOffMap, new Int2(12, 3));
        }

        [Test]
        public void Validate_ALineAboutABuildingNoEarlierLineOpens_IsReported()
        {
            // The soft lock: the line can never tick, so the lesson never completes and every wave it holds
            // waits forever. Its own Opens cannot count - they pay out on the tick the line is satisfied.
            _asset.Unlocked = Operators.All;
            _asset.TutorialStarting = new[] { BuildKind.Drill };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.BeltFork, Text = "Fork the lane.",
                    Opens = new[] { BuildKind.Cutter },
                },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialStepUnreachable, new Int2(-1, -1));
        }

        [Test]
        public void Validate_ALineWhoseBuildingTheMapNeverUnlocks_IsReported()
        {
            // The bar hands the cutter over, and the map's operator set still refuses one: the second gate is
            // not a permission to overrule the first.
            _asset.Unlocked = Operators.Rotator;
            _asset.TutorialStarting = new[] { BuildKind.Cutter };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow { Step = TutorialStep.BeltFork, Text = "Fork the lane." },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialStepUnreachable, new Int2(-1, -1));
        }

        [Test]
        public void Tutorial_ARowCarriesItsHint_ForThePlayerWhoCannotSeeTheWay()
        {
            // The hint is the second sentence: the rule the line assumes, said the long way round. It lives
            // with the text because it is the map's own words about the map's own machine, and an empty one is
            // a line nobody gets stuck on rather than a missing field.
            _asset.Unlocked = Operators.All;
            _asset.TutorialStarting = new[] { BuildKind.Drill };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.PlaceDrill, Text = "Drill the vein.",
                    Hint = "Press 2, then click a tile of the vein.",
                },
                new MapAsset.TutorialTaskRow { Step = TutorialStep.BankShape, Text = "Bank a shape." },
            };

            TutorialScript script = _asset.ToCore().Tutorial;
            Assert.AreEqual("Press 2, then click a tile of the vein.", script[0].Hint);
            Assert.AreEqual(string.Empty, script[1].Hint, "a line with no hint says nothing extra");
        }

        [Test]
        public void Validate_ALineWithAStepThisBuildDoesNotKnow_IsReported()
        {
            // A row serialized before a step was renamed, or a hand-edited file: the line can never tick, so
            // the lesson never completes and every wave it gates waits forever. It is a problem to report, not
            // an exception to raise where the designer cannot act on it.
            _asset.Unlocked = Operators.All;
            _asset.TutorialStarting = new[] { BuildKind.Drill };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow { Step = (TutorialStep)99, Text = "A lesson from a newer build." },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.TutorialStepUnknown, new Int2(-1, -1));
        }

        [Test]
        public void Validate_ATutorialThatCanBeTaught_HasNoTutorialProblems()
        {
            _asset.Unlocked = Operators.All;
            _asset.TutorialStarting = new[] { BuildKind.Drill };
            _asset.TutorialTasks = new[]
            {
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.PlaceDrill, Text = "Drill the vein.", Target = new Vector2Int(7, 7),
                    Opens = new[] { BuildKind.Belt, BuildKind.Cutter },
                },
                new MapAsset.TutorialTaskRow
                {
                    Step = TutorialStep.BeltFork, Text = "Fork the lane.", Target = new Vector2Int(7, 7),
                },
            };

            List<MapProblem> problems = _asset.Validate();
            AssertNoProblem(problems, MapProblemKind.TutorialLineWithoutText);
            AssertNoProblem(problems, MapProblemKind.TutorialTargetOffMap);
            AssertNoProblem(problems, MapProblemKind.TutorialStepUnreachable);
        }

        /// <summary>The problem of that kind at that cell, so a test asks about the one rule it broke rather
        /// than the whole list - these maps carry other problems on purpose.</summary>
        private static void AssertProblem(List<MapProblem> problems, MapProblemKind kind, Int2 cell)
        {
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == kind && problems[i].Cell == cell) return;

            Assert.Fail("expected a " + kind + " problem at " + cell + ", got:\n" + Describe(problems));
        }

        /// <summary>No problem of that kind anywhere: the rules about a vein written half at a time are asked
        /// as an absence, because the point is that a brush cannot reach them.</summary>
        private static void AssertNoProblem(List<MapProblem> problems, MapProblemKind kind)
        {
            for (int i = 0; i < problems.Count; i++)
                if (problems[i].Kind == kind)
                    Assert.Fail("unexpected " + kind + " at " + problems[i].Cell + ": " + problems[i].Message);
        }

        private static string Describe(List<MapProblem> problems)
        {
            var text = new System.Text.StringBuilder();
            if (problems.Count == 0) text.Append("  (no problems)");

            for (int i = 0; i < problems.Count; i++)
                text.Append("  ").Append(problems[i].Kind).Append(": ").Append(problems[i].Message).Append('\n');

            return text.ToString();
        }
    }
}
