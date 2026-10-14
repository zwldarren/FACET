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
        private MapAsset _asset;

        [SetUp]
        public void CreateAsset()
        {
            _asset = ScriptableObject.CreateInstance<MapAsset>();
            _asset.Name = "Painted";
            _asset.Width = 12;
            _asset.Height = 10;
            _asset.StartCircles = 55;
            _asset.CoreCell = new Vector2Int(2, 2);
            _asset.SpawnPoints = new[] { new Vector2Int(0, 9), new Vector2Int(11, 0) };
            _asset.Waves = new[]
            {
                new MapAsset.WaveRow
                {
                    Intermission = 0f,
                    Groups = new[]
                    {
                        new MapAsset.SpawnGroupRow { Kind = EnemyKind.Spike, Count = 4, SpawnPoint = 0, Interval = 2f },
                        new MapAsset.SpawnGroupRow { Kind = EnemyKind.Bulwark, Count = 2, SpawnPoint = 1, Interval = 1.5f },
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
            _asset.PaintOre(8, 2, ShapeType.Square);
            _asset.PaintOre(9, 2, ShapeType.Square);

            MapDefinition map = _asset.ToCore();

            Assert.AreEqual("Painted", map.Name);
            Assert.AreEqual(12, map.Width);
            Assert.AreEqual(10, map.Height);
            Assert.AreEqual(55, map.StartCircles);
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
            Assert.AreEqual(ShapeType.Square, map.Patches[0].Shape);

            Assert.AreEqual(2, map.Waves.Length);
            Assert.AreEqual(2, map.Waves[0].Groups.Length);
            Assert.AreEqual(6, map.Waves[0].Total, "four Spikes and two Bulwarks");
            Assert.AreEqual(0, map.Waves[1].Total, "and the second wave is the empty one it was authored as");

            // And the map it describes is one a world can actually be built on: the point of the conversion is
            // that both halves of the game agree about it.
            var world = new SimWorld(map, new SimConfig());
            Assert.AreEqual(new Int2(2, 2), world.Core.Cell);
            Assert.AreEqual(TerrainKind.Rock, world.Terrain.KindAt(new Int2(5, 7)));
            Assert.AreEqual(TerrainKind.Ore, world.Terrain.KindAt(new Int2(8, 2)),
                "the painted vein is ore in the ground the world stamped");
            Assert.IsTrue(world.Terrain.HasOre(new Int2(9, 2)));
            Assert.AreEqual(ShapeType.Square, world.Terrain.OreAt(new Int2(9, 2)));
            Assert.IsFalse(world.CanPlaceBelt(new Int2(0, 0)), "and the void takes nothing");
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
            _asset.PaintOre(3, 3, ShapeType.Circle);

            MapDefinition painted = _asset.ToCore();
            Assert.AreEqual(TerrainKind.Ground, painted.TerrainAt(new Int2(3, 3)),
                "the array under a vein is plain");
            Assert.AreEqual(TerrainKind.Ore, painted.AuthoredGroundAt(new Int2(3, 3)),
                "and the vein is the patch the list turned into");
            Assert.AreEqual(1, painted.Patches.Length);
            Assert.AreEqual(ShapeType.Circle, painted.Patches[0].Shape);

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
            _asset.PaintOre(3, 3, ShapeType.Circle);
            _asset.PaintOre(3, 3, ShapeType.Square);

            Assert.AreEqual(1, _asset.Ore.Length);
            Assert.AreEqual(ShapeType.Square, _asset.Ore[0].Shape);
            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(3, 3),
                "and the ground under it is still plain: a vein is the one entry");
        }

        [Test]
        public void Validate_AVeinWrittenByTheBrush_IsNotAProblem()
        {
            // The two rules the writer keeps: the array never says ore, and the list never names a cell twice. A
            // brush stroke breaking either would be a map error no designer could see, so painting is held to them.
            _asset.PaintOre(7, 7, ShapeType.Circle);
            List<MapProblem> problems = _asset.Validate();

            AssertNoProblem(problems, MapProblemKind.OreInTerrain);
            AssertNoProblem(problems, MapProblemKind.DuplicateOre);
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
                new MapAsset.OreCell { Cell = new Vector2Int(7, 7), Shape = ShapeType.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(7, 7), Shape = ShapeType.Square },
            };

            AssertProblem(_asset.Validate(), MapProblemKind.DuplicateOre, new Int2(7, 7));
            Assert.AreEqual(ShapeType.Circle, _asset.DrawableView().MineralAt(7, 7), "and the first entry wins");
        }

        [Test]
        public void Erase_IsGroundAndNoMineral()
        {
            _asset.PaintOre(3, 3, ShapeType.Circle);
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
            _asset.PaintOre(1, 1, ShapeType.Circle);
            _asset.PaintOre(5, 5, ShapeType.Square);

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
                    Groups = new[] { new MapAsset.SpawnGroupRow { Kind = EnemyKind.Spike, Count = 3, Interval = 1f } },
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
            _asset.StartCircles = 4242;
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
            _asset.PaintOre(2, 6, ShapeType.Circle);
            _asset.PaintOre(3, 6, ShapeType.Circle);
            int forward = _asset.ContentHash();

            _asset.Ore = new[]
            {
                new MapAsset.OreCell { Cell = new Vector2Int(3, 6), Shape = ShapeType.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(2, 6), Shape = ShapeType.Circle },
                new MapAsset.OreCell { Cell = new Vector2Int(40, 40), Shape = ShapeType.Square },
            };

            Assert.AreEqual(forward, _asset.ContentHash(),
                "the same cells claimed in the other order, with junk past the edge, draw the same picture");

            MapDefinition map = _asset.ToCore();
            DrawnMap drawn = _asset.DrawableView();
            Assert.AreEqual(ShapeType.Circle, drawn.MineralAt(2, 6));
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
                new MapAsset.OreCell { Cell = new Vector2Int(5, 5), Shape = ShapeType.None },
                new MapAsset.OreCell { Cell = new Vector2Int(5, 5), Shape = ShapeType.Circle },
            };

            Assert.AreEqual(ShapeType.None, _asset.DrawableView().MineralAt(5, 5), "first entry wins, here");
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
            _asset.PaintOre(6, 1, ShapeType.HalfSquare);

            MapDefinition map = _asset.ToCore();
            DrawnMap drawn = _asset.DrawableView();

            for (int y = 0; y < drawn.Height; y++)
                for (int x = 0; x < drawn.Width; x++)
                    Assert.AreEqual(map.TerrainAt(new Int2(x, y)), drawn.GroundAt(x, y), "cell " + x + "," + y);

            Assert.AreEqual(ShapeType.HalfSquare, drawn.MineralAt(6, 1));
            Assert.AreEqual(TerrainKind.Ground, drawn.GroundAt(6, 1),
                "a vein's ground is plain, as it is in the array - the mineral is what says there is a vein");
            Assert.AreEqual(map.CoreCell.X, drawn.Core.x);
            Assert.AreEqual(map.CoreCell.Y, drawn.Core.y);
            Assert.AreEqual(ShapeType.None, drawn.MineralAt(0, 0), "plain ground carries no mineral");
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
