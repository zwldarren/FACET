using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The brush, over the asset it writes. Two surfaces paint one map - the painting window's grid and
    /// the Scene view's own editor - and both go through <see cref="MapPainting"/>, so this file is about
    /// that one answer being right: every brush in the enum is offered, every brush writes what its name
    /// says, erasing is one answer whatever is in hand, and the Core an editor draws is the Core the
    /// runtime will build.
    /// </summary>
    public class MapPaintingTests
    {
        private MapAsset _asset;

        [SetUp]
        public void CreateAsset()
        {
            _asset = ScriptableObject.CreateInstance<MapAsset>();
            _asset.Name = "Painted";
            _asset.Width = 8;
            _asset.Height = 6;
            _asset.CoreCell = new Vector2Int(-1, -1);
        }

        [TearDown]
        public void DestroyAsset()
        {
            if (_asset != null) Object.DestroyImmediate(_asset);
        }

        [Test]
        public void Brushes_OfferEveryBrushTheEnumHas_ExactlyOnce()
        {
            foreach (MapBrush brush in System.Enum.GetValues(typeof(MapBrush)))
            {
                CollectionAssert.Contains(MapPainting.Brushes, brush,
                    brush + " is a brush the enum has, so a surface offering the array has to offer it");
                Assert.AreEqual(1, Count(brush), brush + " is offered once, or a button row has two of it");
            }
        }

        [Test]
        public void Label_EveryBrushHasOne_AndNoTwoShareIt()
        {
            var seen = new System.Collections.Generic.List<string>();

            for (int i = 0; i < MapPainting.Brushes.Length; i++)
            {
                string label = MapPainting.Label(MapPainting.Brushes[i]);
                Assert.IsFalse(string.IsNullOrEmpty(label), "every brush is spelled");

                CollectionAssert.DoesNotContain(seen, label, "two brushes do not share a button's name");
                seen.Add(label);
            }
        }

        [Test]
        public void Stroke_AGroundBrush_PaintsThatGroundAndNothingElse()
        {
            MapPainting.Stroke(_asset, MapBrush.Rock, ShapeType.None, 2, 3, erase: false);

            Assert.AreEqual(TerrainKind.Rock, _asset.TerrainAt(2, 3), "the rock brush paints rock");
            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(3, 3), "and it paints one cell, not a row");
            Assert.AreEqual(0, _asset.SpawnPoints.Length, "and it places nothing");
            Assert.AreEqual(0, _asset.Ore.Length, "and it yields nothing");

            MapPainting.Stroke(_asset, MapBrush.Water, ShapeType.None, 2, 3, erase: false);
            Assert.AreEqual(TerrainKind.Water, _asset.TerrainAt(2, 3), "water over rock is water");

            MapPainting.Stroke(_asset, MapBrush.Void, ShapeType.None, 0, 0, erase: false);
            Assert.AreEqual(TerrainKind.Void, _asset.TerrainAt(0, 0), "the void is a ground kind like any other");
        }

        [Test]
        public void Stroke_Ore_WritesTheMineralAndLeavesTheGroundPlain()
        {
            MapPainting.Stroke(_asset, MapBrush.Ore, ShapeType.HalfSquare, 1, 1, erase: false);

            Assert.AreEqual(1, _asset.Ore.Length, "a vein is the mineral written down");
            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(1, 1),
                "and the ground under it stays plain: one place a vein exists");

            ShapePatch patch = _asset.ToCore().Patches[0];
            Assert.AreEqual(ShapeType.HalfSquare, patch.Shape, "and the conversion reads the mineral back");
            Assert.IsTrue(patch.Contains(new Int2(1, 1)), "at the cell it was painted in");
        }

        [Test]
        public void Stroke_WithNoMineral_PaintsNothingAtAll()
        {
            // ShapeType.None is a shape the enum has but not one a vein can carry: the brush refuses it
            // rather than writing an ore tile that yields nothing.
            MapPainting.Stroke(_asset, MapBrush.Ore, ShapeType.None, 4, 4, erase: false);

            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(4, 4), "no mineral is no vein");
            Assert.AreEqual(0, _asset.Ore.Length, "and nothing written down");
        }

        [Test]
        public void Stroke_ErasingATile_TakesEveryHalfOfItAway()
        {
            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 5, 2, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Ore, ShapeType.Circle, 5, 2, erase: false);

            MapPainting.Stroke(_asset, MapBrush.Ore, ShapeType.Circle, 5, 2, erase: true);

            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(5, 2), "erasing clears the ground");
            Assert.AreEqual(0, _asset.Ore.Length, "and the mineral with it");
            Assert.AreEqual(0, _asset.SpawnPoints.Length, "and the door, whatever brush was in hand");
        }

        [Test]
        public void Stroke_TheCoreBrush_NamesTheFootprintsCornerAndNothingElse()
        {
            MapPainting.Stroke(_asset, MapBrush.Core, ShapeType.None, 3, 1, erase: false);

            Assert.AreEqual(new Vector2Int(3, 1), _asset.CoreCell, "the brush writes the footprint's corner");
            Assert.AreEqual(new Vector2Int(3, 1), _asset.CoreFootprint, "which is then where the Core stands");
            Assert.AreEqual(0, _asset.SpawnPoints.Length, "and it is not a door");
            Assert.AreEqual(0, _asset.Ore.Length, "and it is not a vein");
        }

        [Test]
        public void Stroke_ErasingUnderTheCoreBrush_LeavesTheCoreWhereItWasPut()
        {
            MapPainting.Stroke(_asset, MapBrush.Core, ShapeType.None, 3, 1, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Rock, ShapeType.None, 6, 4, erase: false);

            MapPainting.Stroke(_asset, MapBrush.Core, ShapeType.None, 6, 4, erase: true);

            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(6, 4), "erasing clears the ground");
            Assert.AreEqual(new Vector2Int(3, 1), _asset.CoreCell,
                "and leaves the Core alone: there is no cell that means 'no Core' - the field's own blank " +
                "value is 'centre it', which is a placement and not an absence");
        }

        [Test]
        public void Stroke_TheDoorBrush_AddsOneEntryPointHoweverOftenItIsPainted()
        {
            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 0, 5, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 0, 5, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 7, 0, erase: false);

            CollectionAssert.AreEqual(new[] { new Vector2Int(0, 5), new Vector2Int(7, 0) }, _asset.SpawnPoints,
                "two doors, in the order they were painted - a tile is one place, however often it is named");

            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 0, 5, erase: true);
            CollectionAssert.AreEqual(new[] { new Vector2Int(7, 0) }, _asset.SpawnPoints,
                "and erasing takes the one that is there");
        }

        [Test]
        public void Stroke_OutsideTheMap_WritesNothing()
        {
            MapPainting.Stroke(_asset, MapBrush.Rock, ShapeType.None, -3, 2, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Ore, ShapeType.Circle, 8, 6, erase: false);
            MapPainting.Stroke(_asset, MapBrush.Door, ShapeType.None, 100, 100, erase: false);

            Assert.AreEqual(TerrainKind.Ground, _asset.TerrainAt(0, 0), "nothing inside the map moved");
            Assert.AreEqual(0, _asset.Ore.Length, "and nothing was written past its edge");
            Assert.AreEqual(0, _asset.SpawnPoints.Length, "not even a door the wave table could name");
        }

        [Test]
        public void Fill_AGroundBrush_PaintsEveryTile()
        {
            MapPainting.Fill(_asset, MapBrush.Rock, ShapeType.None);

            for (int y = 0; y < _asset.Height; y++)
            {
                for (int x = 0; x < _asset.Width; x++)
                    Assert.AreEqual(TerrainKind.Rock, _asset.TerrainAt(x, y), "a fill reaches (" + x + "," + y + ")");
            }
        }

        [Test]
        public void Fill_ABrushThatIsNotGround_FillsNothing()
        {
            // The buttons for a fill say the same thing by being disabled; this is the half of the rule
            // that holds even for a caller that forgot to ask.
            MapPainting.Fill(_asset, MapBrush.Core, ShapeType.None);
            MapPainting.Fill(_asset, MapBrush.Door, ShapeType.None);

            Assert.AreEqual(new Vector2Int(-1, -1), _asset.CoreCell, "a whole map of Core cells is not a map");
            Assert.AreEqual(0, _asset.SpawnPoints.Length, "and neither is a whole map of doors");
            Assert.AreEqual(0, _asset.Ore.Length, "and no mineral was invented");
        }

        [Test]
        public void CoreFootprint_WithNoCellNamed_IsTheCoreTheRuntimeWillBuild()
        {
            // The Editor draws the Core where the map says it will stand, which is before the conversion
            // that decides it. One rule, read twice - this is that they are the same rule.
            Int2 built = _asset.ToCore().CoreCell;

            Assert.AreEqual(MapDefinition.CentredCore(_asset.Width, _asset.Height), built,
                "a map that names no Core gets the centred one");
            Assert.AreEqual(built.X, _asset.CoreFootprint.x, "and the editor draws it there");
            Assert.AreEqual(built.Y, _asset.CoreFootprint.y, "in both axes");
        }

        [Test]
        public void CoreFootprint_WithACellNamed_IsThatCell()
        {
            _asset.CoreCell = new Vector2Int(6, 1);

            Assert.AreEqual(new Vector2Int(6, 1), _asset.CoreFootprint, "a named Core is where it was put");
            Assert.AreEqual(new Int2(6, 1), _asset.ToCore().CoreCell, "and the runtime builds it there");
        }

        private int Count(MapBrush brush)
        {
            int count = 0;
            for (int i = 0; i < MapPainting.Brushes.Length; i++)
                if (MapPainting.Brushes[i] == brush) count++;

            return count;
        }
    }
}
