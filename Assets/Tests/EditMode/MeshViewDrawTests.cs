using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The rebuild gate, driven by hand. A view is normally advanced by Unity's frame message - what the
    /// PlayMode gate test pins, and why nothing outside a running game could draw one. The gate is a
    /// method (<see cref="MeshView.Draw"/>) for the one caller with no frames at all: the Editor's map
    /// preview, which draws the game's views in the Scene view. These are that caller's contract.
    /// </summary>
    public class MeshViewDrawTests
    {
        /// <summary>A frame with a real pixel size: the gate refuses to draw at zero zoom, which is the
        /// one frame a preview could otherwise hand it while a Scene view is still being laid out.</summary>
        private static readonly ViewFrame Frame = new ViewFrame(1f, 0.02f);

        private GameObject _object;
        private Palette _palette;
        private FrameClock _clock;
        private CountingView _view;

        [SetUp]
        public void CreateView()
        {
            _object = new GameObject("Counting");
            _view = _object.AddComponent<CountingView>();

            _palette = ScriptableObject.CreateInstance<Palette>();
            _clock = new FrameClock { Frame = Frame };

            _view.Initialize(Sim.NewWorld(8, 4), _clock, _palette);
        }

        [TearDown]
        public void DestroyView()
        {
            if (_object != null) Object.DestroyImmediate(_object);
            if (_palette != null) Object.DestroyImmediate(_palette);
        }

        [Test]
        public void Draw_BuildsWithNoFrame_AndGatesTheSameWay()
        {
            _view.Draw(_clock.Frame);
            Assert.AreEqual(1, _view.Appends, "a frame handed in by hand builds the mesh once");

            _view.Draw(_clock.Frame);
            Assert.AreEqual(1, _view.Appends, "the same frame again does not rebuild");

            _view.Draw(new ViewFrame(1f, Frame.WorldPerPixel * 1.15f));
            Assert.AreEqual(2, _view.Appends, "a zoom past the tolerance rebuilds, as it does in game");

            _view.Draw(new ViewFrame(0f, 0f));
            Assert.AreEqual(2, _view.Appends, "and a frame with no zoom is refused rather than drawn");
        }

        [Test]
        public void Draw_OfARealView_PutsTheMapsObstaclesInAMesh()
        {
            // The map the preview is really for: ground that is not all ground, drawn by the view that
            // draws the map's own rock - so what lands in the mesh is the map's data, not a shape the test
            // asked for.
            var go = new GameObject("Terrain");
            try
            {
                TerrainRenderer view = go.AddComponent<TerrainRenderer>();
                view.Initialize(RockyWorld(), _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                Assert.Greater(filter.sharedMesh.vertexCount, 0, "with the map's rock in it");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Draw_OfTheItemView_DrawsTheFilledQuadrantsAsOneFigure()
        {
            // An item is drawn as its quadrants, so the mesh grows with them: nothing for a hole, one mark
            // for a quarter, two for a half and four for a full circle. That is what "a full circle reads as
            // a disc and a top half-circle as half a disc" means once the geometry is counted.
            int quarter = ItemVertices(Shape.Quarter(Shape.NW, Quad.Circle));
            int half = ItemVertices(Sim.TopHalfCircle);
            int full = ItemVertices(Sim.Circle);

            Assert.Greater(quarter, 0, "a filled quadrant is geometry - the empty ones are not");
            Assert.Greater(half, quarter, "two quadrants are more than one");
            Assert.Greater(full, half, "and a full circle is four of them");
        }

        /// <summary>The item view's mesh size for a world whose one belt cell carries the given item, drawn
        /// by hand the way the preview draws it. The cell is clear of the Core, which an 8x4 test map puts
        /// down its middle.</summary>
        private int ItemVertices(Shape item)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            var cell = new Int2(0, 1);
            Assert.IsTrue(world.TryPlaceBelt(cell, Dir.East), "the item's cell is free");
            Assert.IsTrue(world.Belts.TrySpawnItem(cell, item), "the belt takes the item");

            var go = new GameObject("Items");
            try
            {
                ItemRenderer view = go.AddComponent<ItemRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Draw_AfterItsMeshWasDestroyed_MakesAnotherOne()
        {
            // What a Play-mode transition does to a preview that outlives it: the mesh goes, the view stays.
            // A lost mesh is a reason to rebuild - otherwise the gate would sit out an unchanged frame and
            // leave the view drawing into nothing.
            var go = new GameObject("Terrain");
            try
            {
                TerrainRenderer view = go.AddComponent<TerrainRenderer>();
                view.Initialize(RockyWorld(), _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Mesh first = filter.sharedMesh;
                Object.DestroyImmediate(first);

                Assert.DoesNotThrow(() => view.Draw(_clock.Frame), "the same unchanged frame is still drawn");

                Mesh second = filter.sharedMesh;
                Assert.AreNotSame(first, second, "into a mesh made again");
                Assert.Greater(second.vertexCount, 0, "and filled with what the frame appends");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Initialize_TheMeshLivesAndDiesWithTheViewThatMadeIt()
        {
            // A preview object is DontSave so a scene reload does not take it away, so its mesh has to be
            // too - and then nothing else frees it, which is the view's own job.
            var persistent = new GameObject("Preview") { hideFlags = HideFlags.HideAndDontSave };
            var scene = new GameObject("Scene");
            try
            {
                Mesh kept = MeshOf(persistent);
                Mesh ordinary = MeshOf(scene);

                Assert.AreEqual(HideFlags.DontSave, kept.hideFlags,
                    "a DontSave view's mesh is DontSave too, so a scene unload does not take the geometry away from a view that survives it");
                Assert.AreEqual(HideFlags.None, ordinary.hideFlags,
                    "and a view that belongs to a scene asks for no special hold on it");

                Object.DestroyImmediate(persistent);
                Assert.IsTrue(kept == null,
                    "the view frees its own DontSave mesh when it goes, rather than leaking one per object");
            }
            finally
            {
                if (scene != null) Object.DestroyImmediate(scene);
                if (persistent != null) Object.DestroyImmediate(persistent);
            }
        }

        [Test]
        public void APlacedCutter_StandsOnTwoTiles_WithItsBladeAlongTheFlow()
        {
            // A placed cutter's one facing readout - the outlets are read off the belts, so without its blade
            // nothing on the machine says which two pieces it is making, and a line of cutters all reads as the
            // same machine whatever way each is aimed. What the mesh has to show is the rest of the rule too:
            // one body over the pair of tiles, and the blade along the flow through the middle of the tile the
            // player placed, rather than across it. A rotator in the same empty state draws one tile and no
            // blade, so the two differences are read on their own.
            var cell = new Int2(0, 1);
            Dir lane = MachineFootprint.LaneOf(Dir.North);
            Vector2[] cutter = OperatorFigure(BuildKind.Cutter, cell, Dir.North);
            Vector2[] rotator = OperatorFigure(BuildKind.Rotator, cell, Dir.North);

            Assert.IsTrue(HasVertexIn(cutter, cell), "the cutter covers the tile it stands on");
            Assert.IsTrue(HasVertexIn(cutter, cell + lane.Offset()),
                "and the lane tile one over to its right, which is what makes it two tiles wide");
            Assert.IsFalse(HasVertexIn(rotator, cell + lane.Offset()),
                "while the rotator's body stays inside its own tile");

            // The blade: the one geometric fact of the cut, from one edge of the tile to the other along the
            // flow, on the tile's own middle line. Nothing else on a cutter with no belts wired stands within a
            // tenth of a tile of that line, so finding geometry there is finding the blade - and finding none
            // across it is finding that the cut no longer runs over the way the belt runs.
            int from = 0;
            int to = 0;
            foreach (Vector2 v in cutter)
            {
                if (Mathf.Abs(v.x - (cell.X + 0.5f)) > 0.1f) continue;
                if (Mathf.Abs(v.y - cell.Y) <= 0.1f) from++;
                if (Mathf.Abs(v.y - (cell.Y + 1f)) <= 0.1f) to++;
            }

            Assert.Greater(from, 0, "the blade starts on the near edge of the tile, along the flow");
            Assert.Greater(to, 0, "and runs to the far one, a whole tile of cut");
            Assert.IsFalse(HasVertexNear(cutter, new Vector2(cell.X, cell.Y + 0.5f), 0.1f),
                "and nothing is drawn across the tile: the cut follows the belt instead of crossing it");
        }

        [Test]
        public void TheCutterGhost_DrawsItsFork_WhereADrillGhostDrawsNone()
        {
            // The ghost is where the aim is chosen, so the fork is drawn there too: a ghost flowing north reads
            // "two tiles wide, the cut is here, both lanes leave that way" before the click rather than after. A
            // drill's ghost shows the same body, border and facing chevron and nothing else, so the mesh
            // difference is the cutter's own second tile, a body on each of them, its blade and its two chevrons.
            var cell = new Int2(0, 1);
            int cutter = GhostVertices(BuildKind.Cutter, cell);
            int drill = GhostVertices(BuildKind.Drill, cell);

            Assert.Greater(cutter, drill, "the ghost's fork is geometry a drill's ghost lacks");
        }

        [Test]
        public void ATutorialLine_IsDrawnAsTheBuildingsItPromises_NotAsOneColourOfCell()
        {
            // A blueprint used to be one accent wash per ghost, which said "something goes here" for every
            // cell alike - a line of identical squares never told the player whether this one wanted a belt or
            // a gun. So each ghost now wears its building's own figure, and the differences below are the ones
            // the player reads the answer off: a drill is hollow (the ore under it is its identity), a turret
            // is a body with a gun and a ring, a belt is one tile of bed, a cutter is a fork over two tiles.
            var cell = new Int2(4, 2);
            Vector2 centre = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);

            GhostFigure drill = GhostFigureOf(BuildKind.Drill, cell, Dir.West);
            GhostFigure belt = GhostFigureOf(BuildKind.Belt, cell, Dir.East);
            GhostFigure turret = GhostFigureOf(BuildKind.Turret, cell, Dir.North);
            GhostFigure cutter = GhostFigureOf(BuildKind.Cutter, cell, Dir.East);

            Assert.Greater(drill.Points.Length, 0, "the drill's ghost is drawn");
            Assert.Greater(belt.Points.Length, 0, "the belt's ghost is drawn");
            Assert.Greater(turret.Points.Length, 0, "the turret's ghost is drawn");
            Assert.Greater(cutter.Points.Length, 0, "the cutter's ghost is drawn");

            Assert.IsFalse(drill.Covers(centre),
                "a drill's ghost is the hollow frame the machine stands in, " +
                "so the ore patch under it stays its identity");
            Assert.IsTrue(belt.Covers(centre), "a belt's ghost is a bed, and a bed covers its tile's middle");
            Assert.IsTrue(turret.Covers(centre), "a turret's ghost is a body on its tile");

            Assert.LessOrEqual(belt.Reach(cell), 0.5f + 0.06f, "a belt's ghost stays inside the one tile it is");
            Assert.Greater(turret.Reach(cell), 1f,
                "while a turret's ghost draws the gun and the coverage it is bought for, past its own tile");

            Int2 laneCell = cell + MachineFootprint.LaneOf(Dir.East).Offset();
            var laneCentre = new Vector2(laneCell.X + 0.5f, laneCell.Y + 0.5f);
            Assert.IsTrue(cutter.Covers(laneCentre), "a cutter's fork covers the second tile it stands on");
            Assert.IsFalse(drill.Covers(laneCentre), "and a drill's frame stays on its own tile");
        }

        [Test]
        public void TheGuideLiesOnTheMap_UnderWhatIsBuiltOnIt_AndItsArrowAboveTheMap()
        {
            // A blueprint is a plan for a cell that is still empty, so the plan has to lose every pixel it
            // shares with something already built: where the player has laid a belt, the belt's direction arrow
            // is the answer they need next, and a ghost painted over it hides that answer. It must still win
            // over the ground and the vein it is drawn on, or a drill's ghost would disappear under the ore
            // patch that is its whole identity. The off-screen arrow is the other case - not a tile at all -
            // and goes in front of everything on the map; it only ever draws in a frame where the blueprint
            // does not, so one view can carry both planes. More negative z is closer to the camera.
            var palette = ScriptableObject.CreateInstance<Palette>();
            try
            {
                Assert.Less(palette.GuideLayer.Z, palette.ShapePatchLayer.Z,
                    "the plan draws in front of the vein it stands on");
                Assert.Greater(palette.GuideLayer.Z, palette.BeltLayer.Z,
                    "and behind a belt laid on that cell, so the built run covers its own ghost");
                Assert.Greater(palette.GuideLayer.Z, palette.MachineLayer.Z, "and behind a built machine");
                Assert.Greater(palette.GuideLayer.Z, palette.CoreLayer.Z, "and behind the Core");
                Assert.Greater(palette.GuideLayer.Z, palette.EnemyLayer.Z, "and behind an enemy walking over it");

                Assert.Less(palette.GuideMarkerLayer.Z, palette.BeltLayer.Z,
                    "the off-screen arrow reads over the map's belts");
                Assert.Less(palette.GuideMarkerLayer.Z, palette.MachineLayer.Z, "over its machines");
                Assert.Less(palette.GuideMarkerLayer.Z, palette.EnemyLayer.Z, "and over its enemies");
                Assert.Greater(palette.GuideMarkerLayer.Z, palette.CursorLayer.Z,
                    "while the player's own placement ghost stays in front of it");
                Assert.Greater(palette.GuideMarkerLayer.Z, palette.RadarLayer.Z,
                    "and the locator's arrows stay in front of that");
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void ABeltGhostDockingIntoAMachine_DrawsOneLaneIn_AndNotACross()
        {
            // The join between a line and the machine it feeds is one lane: the cell in front of the drill is
            // fed across the edge the drill stands on, so it owes a lane in from that side and its exit lane
            // out the other - a run docking into the frame. Every side of that cell used to be asked "is this
            // tile a machine's front?", which the front tile answers yes to from all four, so the joint grew a
            // lane in from every side at once and read as a cross standing on the track. The two sides nothing
            // arrives on are what tells the two drawings apart.
            var drill = new Int2(5, 2);
            var joint = drill + Dir.West.Offset();          // the cell in front of the drill
            GhostFigure figure = BlueprintOf(joint, new[]
            {
                new TutorialGhost(BuildKind.Drill, drill, Dir.West),
                new TutorialGhost(BuildKind.Belt, joint, Dir.West),
                new TutorialGhost(BuildKind.Belt, joint + Dir.West.Offset(), Dir.West),
            });

            var middle = new Vector2(joint.X + 0.5f, joint.Y + 0.5f);
            Assert.IsTrue(figure.Covers(middle), "the joint is a belt cell, so it draws its bed");
            Assert.IsTrue(figure.Covers(middle + new Vector2(0.35f, 0f)),
                "a lane runs in from the drill's side, so the run docks into the machine");
            Assert.IsTrue(figure.Covers(middle + new Vector2(-0.35f, 0f)),
                "and the belt's own exit lane runs on out the other side");

            Assert.IsFalse(figure.Covers(middle + new Vector2(0f, 0.35f)),
                "nothing arrives from the north, so no lane is drawn there");
            Assert.IsFalse(figure.Covers(middle + new Vector2(0f, -0.35f)),
                "nor from the south: the join is a run, not a cross");
        }

        /// <summary>
        /// One ghost of the given kind drawn by the real guide, on a lesson whose only line asks for it: the
        /// camera looks straight at the cell, so the layout is on screen and the blueprint - not the edge
        /// marker - is what lands in the mesh.
        /// </summary>
        private GhostFigure GhostFigureOf(BuildKind kind, Int2 cell, Dir direction)
            => BlueprintOf(cell, new[] { new TutorialGhost(kind, cell, direction) });

        /// <summary>
        /// A whole blueprint - several pieces of one line - drawn by the real guide: the camera looks straight
        /// at <paramref name="target"/>, so the layout is on screen and the blueprint, not the edge marker, is
        /// what lands in the mesh. The pieces are drawn together because that is the state the guide reads
        /// them in: what a belt cell draws depends on its neighbours in the same blueprint.
        /// </summary>
        private GhostFigure BlueprintOf(Int2 target, TutorialGhost[] ghosts)
        {
            var line = new TutorialTask(TutorialStep.PlaceDrill, "a blueprint", target,
                new BuildKind[0], false, null, ghosts);
            var script = new TutorialScript(new[] { BuildKind.Belt }, new[] { line });

            MapDefinition rig = Sim.RigMap();
            var map = new MapDefinition("guide", rig.Width, rig.Height, rig.StartShapes,
                rig.Patches, rig.SpawnPoints, new[] { new WaveDefinition(10f) }, rig.Terrain,
                rig.CoreCell, Operators.All, rig.Seed, 0, script);

            var world = new SimWorld(map, new SimConfig());

            var cameraObject = new GameObject("Guide Camera");
            var viewObject = new GameObject("TutorialGuide");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 6f;
                camera.aspect = 1.6f;
                camera.transform.position = new Vector3(target.X + 0.5f, target.Y + 0.5f, -10f);

                var view = viewObject.AddComponent<TutorialGuideView>();
                view.Initialize(world, _clock, _palette);
                view.InitializeGuide(camera);
                view.Draw(_clock.Frame);

                MeshFilter filter = viewObject.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the guide draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");

                Mesh mesh = filter.sharedMesh;
                return new GhostFigure
                {
                    Points = Flat(mesh),
                    Vertices = mesh.vertices,
                    Triangles = mesh.triangles,
                };
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>A drawn ghost, read out of the mesh before the view that made it goes - the mesh is the
        /// view's and is freed with it. <see cref="Covers"/> is "is this point inside the figure", asked of
        /// the triangles rather than of the outline, because hollow and filled are drawn from the same rim.</summary>
        private sealed class GhostFigure
        {
            public Vector2[] Points;
            public Vector3[] Vertices;
            public int[] Triangles;

            public bool Covers(Vector2 point)
            {
                for (int i = 0; i + 2 < Triangles.Length; i += 3)
                {
                    if (Inside(point, Vertices[Triangles[i]], Vertices[Triangles[i + 1]],
                        Vertices[Triangles[i + 2]])) return true;
                }

                return false;
            }

            /// <summary>How far the figure reaches from a tile's middle, in tiles: a ghost of one tile stays
            /// at half, and a gun's barrel or ring is what pushes past it.</summary>
            public float Reach(Int2 cell)
            {
                var centre = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
                float reach = 0f;
                foreach (Vector2 p in Points) reach = Mathf.Max(reach, (p - centre).magnitude);

                return reach;
            }

            private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = Cross(p, a, b);
                float d2 = Cross(p, b, c);
                float d3 = Cross(p, c, a);

                bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
                bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
                return !(negative && positive);
            }

            private static float Cross(Vector2 p, Vector2 a, Vector2 b)
                => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        [Test]
        public void TheQuarterSeamsOfAQuadrantFigure_AreSymmetricAboutItsCentre()
        {
            // Regression for the cross in the middle of a disc. An item is drawn as its four quadrants, and
            // the edges they share are the item's own quarter seams - the readout a cutter's cut line is
            // chosen against. An outline grows outward into the neighbour's quadrant, and one mesh has no
            // depth buffer, so drawing each quadrant fill-then-outline let the next quadrant's fill erase the
            // side of the seam the previous one had just drawn: the cross came out lopsided and off the
            // item's centre. Asked of the finished mesh as the property the figure should have - mirror
            // symmetry about its own centre - because that is what "the seam is in the middle" means.
            Vector2[] figure = ItemFigure(Sim.Circle);

            Assert.Greater(figure.Length, 0, "the item view drew something");

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 v in figure)
            {
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minY) minY = v.y;
                if (v.y > maxY) maxY = v.y;
            }

            var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            // A disc's box is the disc, so its middle is the item's middle, whatever the outline did.
            foreach (Vector2 v in figure)
            {
                AssertMirrored(figure, new Vector2(2f * centre.x - v.x, v.y), "across the blade");
                AssertMirrored(figure, new Vector2(v.x, 2f * centre.y - v.y), "across the other blade");
            }
        }

        /// <summary>Is there geometry at the mirror of <paramref name="expected"/>, to within the float noise
        /// of two arc segment angles? The erased half-seam of the bug is a whole outline width away - 0.04
        /// tiles at this frame's zoom - so this tolerance tells them apart by two orders of magnitude.</summary>
        private static void AssertMirrored(Vector2[] figure, Vector2 expected, string about)
        {
            float nearest = float.MaxValue;
            foreach (Vector2 v in figure)
            {
                float distance = (v - expected).magnitude;
                if (distance < nearest) nearest = distance;
            }

            Assert.Less(nearest, 1e-4f, "no geometry " + about + " at " + expected +
                " - the figure is not symmetric about its own centre, so its seams do not meet in the middle");
        }

        /// <summary>The item view's mesh vertices for a world whose one belt cell carries the given item:
        /// the real view, drawn by hand, read out of the mesh it built for itself.</summary>
        private Vector2[] ItemFigure(Shape item)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            var cell = new Int2(0, 1);
            Assert.IsTrue(world.TryPlaceBelt(cell, Dir.East), "the item's cell is free");
            Assert.IsTrue(world.Belts.TrySpawnItem(cell, item), "the belt takes the item");

            var go = new GameObject("Items");
            try
            {
                ItemRenderer view = go.AddComponent<ItemRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");

                Vector3[] vertices = filter.sharedMesh.vertices;
                var flat = new Vector2[vertices.Length];
                for (int i = 0; i < vertices.Length; i++) flat[i] = new Vector2(vertices[i].x, vertices[i].y);
                return flat;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheSeamBetweenTwoQuadrants_IsOneLine_AndThinnerThanTheRim()
        {
            // The same cross, in the terms the geometry is built in. Two quadrants share every seam and each
            // outline grows outward into the other's territory, so a seam both of them drew at the rim's
            // width came out twice as heavy as the figure's own edge. Each side draws half of it instead: the
            // two bands meet on the axis, the pair is one line of the width asked for, and it is thinner than
            // the rim - which is what keeps a disc from reading as a cross once the zoom has made it small.
            const float rim = 0.20f;
            const float seam = 0.10f;

            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            // The two quadrants either side of the vertical axis, each with its neighbour across that axis
            // filled - and with the axis the other way empty, which is the flat side of a half item.
            foreach (int quadrant in new[] { Shape.NE, Shape.NW })
            {
                Vector2[] points = ProcMesh.QuarterDisc(4, 1f, quadrant);
                ProcMesh.AppendQuadrantOutline(verts, colors, tris, points, Vector2.zero, Color.white, rim,
                    seam, verticalSeam: true, horizontalSeam: false);
            }

            // Every band is one quad - a, b, b + n, a + n - so its width is |n|, and the bands that lie on
            // the shared axis are the seam's. The rest, including the flat side whose neighbour is a hole,
            // are the rim.
            float widestSeam = 0f;
            float widestRim = 0f;
            float spanMin = float.MaxValue;
            float spanMax = float.MinValue;

            for (int q = 0; q + 3 < verts.Count; q += 4)
            {
                float width = (verts[q + 2] - verts[q + 1]).magnitude;

                if (Mathf.Abs(verts[q].x) <= 1e-4f && Mathf.Abs(verts[q + 1].x) <= 1e-4f)
                {
                    widestSeam = Mathf.Max(widestSeam, width);

                    // The band's reach is what the straddle is about: all four corners, not just the
                    // two that sit on the axis - the far pair is the half that lands in the neighbour.
                    for (int v = 0; v < 4; v++)
                    {
                        spanMin = Mathf.Min(spanMin, verts[q + v].x);
                        spanMax = Mathf.Max(spanMax, verts[q + v].x);
                    }

                    continue;
                }

                widestRim = Mathf.Max(widestRim, width);
            }

            Assert.AreEqual(seam * 0.5f, widestSeam, 1e-5f,
                "each side of a seam draws half of it, so the two sides together are one line");
            Assert.AreEqual(seam * 0.5f, spanMax, 1e-5f,
                "and the half lands in the neighbour's territory, so the line straddles the axis");
            Assert.AreEqual(-seam * 0.5f, spanMin, 1e-5f, "evenly, which is what keeps it centred");
            Assert.AreEqual(rim, widestRim, 1e-5f, "the figure's own edges keep the rim's width");
            Assert.Less(widestSeam * 2f, widestRim, "so the seam the two of them make is thinner than the rim");
        }

        [Test]
        public void AnItemAtTheFarEndOfTheZoom_KeepsItsShape_RatherThanItsOutline()
        {
            // An outline is a constant number of screen pixels, so its size in the item's own terms grows as
            // the item shrinks: at the far end of the camera's zoom a disc is thirteen pixels across and a
            // two-pixel ring is a third of what is on the screen. Past a share of the item's own radius the
            // outline gives way, so what the player sees there is the shape rather than its edge.
            const float worldPerPixel = 0.04f;          // 25 pixels to the tile: well past the zoom's far end
            float radius = _palette.Items.Radius;

            Mesh mesh = ItemMesh(Sim.Circle, worldPerPixel);
            Vector3[] vertices = mesh.vertices;
            Color[] colours = mesh.colors;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
            }

            var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            float disc = 0f;
            float ring = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                float r = new Vector2(vertices[i].x - centre.x, vertices[i].y - centre.y).magnitude;

                // The outline's vertices are the ones the outline colour was appended for; the disc's are the
                // fill's. One item, one figure, so the two sets are the whole mesh.
                if ((colours[i] - _palette.Outline).maxColorComponent <= 1e-3f) ring = Mathf.Max(ring, r);
                else disc = Mathf.Max(disc, r);
            }

            Assert.AreEqual(radius, disc, 1e-3f, "the disc is as big as the palette says an item is");
            Assert.Greater(ring, disc, "and its outline is outside it");
            Assert.LessOrEqual(ring - disc, radius * _palette.OutlineMaxRadiusFraction + 1e-4f,
                "by no more than the share of the radius the palette allows");
            Assert.Less(ring - disc, 2f * worldPerPixel,
                "which is what bit here: the uncapped width would be twice the pixel size, a fifth of the item");
        }

        /// <summary>The item view's mesh for a world whose one belt cell carries the given item, drawn at
        /// <paramref name="worldPerPixel"/> rather than at the default frame's zoom.</summary>
        private Mesh ItemMesh(Shape item, float worldPerPixel)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            var cell = new Int2(0, 1);
            Assert.IsTrue(world.TryPlaceBelt(cell, Dir.East), "the item's cell is free");
            Assert.IsTrue(world.Belts.TrySpawnItem(cell, item), "the belt takes the item");

            var go = new GameObject("Items");
            try
            {
                ItemRenderer view = go.AddComponent<ItemRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(new ViewFrame(1f, worldPerPixel));

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>A map with rock in it, so a real view has something to put in the mesh.</summary>
        private static SimWorld RockyWorld()
            => new SimWorld(new MapDefinition("rocky", 8, 4, 0,
                new ShapePatch[0], new Int2[0], new WaveDefinition[0],
                TerrainLayout.Rects(8, 4, new TerrainRect(new Int2(1, 1), 3, 2, TerrainKind.Rock))),
                new SimConfig());

        /// <summary>A view on the given object, and the mesh it made for itself.</summary>
        private Mesh MeshOf(GameObject go)
        {
            CountingView view = go.AddComponent<CountingView>();
            view.Initialize(Sim.NewWorld(8, 4), _clock, _palette);

            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh, go.name + " drew no mesh");
            return mesh;
        }

        /// <summary>
        /// The machine view's mesh vertices for one operator placed alone on bare ground: no belts wired, so no
        /// stubs, and an empty chamber, so no badge - the two operators differ by their own footprints and
        /// blades. Drawn by hand the way the preview draws it, and read out of the mesh it built for itself.
        /// The cell is clear of the Core, which an 8x4 test map puts down its middle.
        /// </summary>
        private Vector2[] OperatorFigure(BuildKind kind, Int2 cell, Dir facing)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            Sim.Place(world, kind, cell, facing);

            var go = new GameObject("Machines");
            try
            {
                MachineRenderer view = go.AddComponent<MachineRenderer>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the machine view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return Flat(filter.sharedMesh);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>The cursor view's mesh size with the given kind selected and the cursor on the given
        /// cell - the ghost's own geometry, nothing pinned and no turret ring.</summary>
        private int GhostVertices(BuildKind kind, Int2 cell)
        {
            SimWorld world = Sim.NewWorld(8, 4);
            world.Tick(new InputCommand(cursorCell: cell, selected: kind));

            var go = new GameObject("Cursor");
            try
            {
                CursorView view = go.AddComponent<CursorView>();
                view.Initialize(world, _clock, _palette);
                view.Draw(_clock.Frame);

                MeshFilter filter = go.GetComponent<MeshFilter>();
                Assert.IsNotNull(filter, "the cursor view draws through a mesh filter");
                Assert.IsNotNull(filter.sharedMesh, "and built its mesh without a frame arriving");
                return filter.sharedMesh.vertexCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>The mesh's vertices flattened: tile coordinates, with the z-plane dropped.</summary>
        private static Vector2[] Flat(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            var flat = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) flat[i] = new Vector2(vertices[i].x, vertices[i].y);
            return flat;
        }

        /// <summary>
        /// Is part of the figure inside this tile? The margin keeps a vertex that merely crosses the tile's edge
        /// - an outline grown outward from the tile next door - from counting as standing on it.
        /// </summary>
        private static bool HasVertexIn(Vector2[] figure, Int2 cell)
        {
            const float margin = 0.02f;
            foreach (Vector2 v in figure)
            {
                if (v.x < cell.X + margin || v.x > cell.X + 1f - margin) continue;
                if (v.y < cell.Y + margin || v.y > cell.Y + 1f - margin) continue;
                return true;
            }

            return false;
        }

        /// <summary>Is there a vertex of the figure at this point, to within
        /// <paramref name="tolerance"/>?</summary>
        private static bool HasVertexNear(Vector2[] figure, Vector2 expected, float tolerance)
        {
            foreach (Vector2 v in figure)
                if ((v - expected).magnitude <= tolerance) return true;

            return false;
        }

        /// <summary>A view whose rebuilds can be counted, so the gate is exercised through the same
        /// interface the preview uses rather than around it.</summary>
        private sealed class CountingView : MeshView
        {
            public int Appends { get; private set; }

            protected override Palette.Layer Layer => Colors.GridLayer;

            protected override void AppendFrame(in ViewFrame frame) => Appends++;
        }
    }
}
