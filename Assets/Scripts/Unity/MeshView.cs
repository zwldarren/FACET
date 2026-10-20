using System.Collections.Generic;
using Facet.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Facet.Game
{
    /// <summary>
    /// The module every view of the simulation extends. It owns the mesh lifecycle - geometry buffers,
    /// mesh and renderer, screen-space constants and the rebuild gate - so a view contributes only the
    /// current frame's geometry.
    ///
    /// The view layer's four rebuild reasons go through two hooks: a view calls <see cref="MarkDirty"/>
    /// from <see cref="Observe"/> when its watched value changed, while the gate owns the shared reasons
    /// (a zoom past tolerance, a gone mesh). The first rebuild happens on the first frame with a real
    /// zoom, so a view never synthesizes a frame of its own.
    ///
    /// One <see cref="MeshView"/> is one GameObject with one mesh - the shape the PlayMode tests pin. Two
    /// optional passes hang off it as children, each made only when a view asks for one: a Sprite override's
    /// pooled SpriteRenderers, and the cast shadows of a view whose bodies stand on the ground. The mesh is
    /// made and freed with its view, and lives as long as the object that draws it, so a view held across a
    /// scene reload still has geometry to draw into.
    /// </summary>
    /// <para>
    /// The class executes in the editor so its mesh frees when the object holding it goes while no game
    /// runs: a preview object is <c>DontSave</c>, so nothing else sweeps its mesh away, and a leak per
    /// preview rebuild otherwise. The editor add is for the freeing, not the drawing - frames belong to
    /// the caller - so the Unity-driven draw below stays a play-mode call.
    /// </para>
    [ExecuteAlways]
    public abstract class MeshView : MonoBehaviour
    {
        /// <summary>Rebuild once the pixel size has moved this far from what the mesh was built at.</summary>
        private const float ZoomTolerance = 0.05f;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        /// <summary>Scratch for <see cref="AppendChevron"/>, so a view drawing many chevrons in one
        /// frame does not allocate a point array per chevron.</summary>
        private readonly Vector2[] _chevron = new Vector2[3];

        private Mesh _mesh;

        /// <summary>The mesh's name and lifetime, kept so a mesh remade by <see cref="AcquireMesh"/> is
        /// made the same way as the first.</summary>
        private string _meshName;
        private HideFlags _meshFlags;

        private FrameClock _frames;
        private Palette _palette;
        private bool _dirty;
        private float _builtWorldPerPixel = -1f;

        /// <summary>Sprites this view draws, created lazily and reused across frames.</summary>
        private readonly List<SpriteRenderer> _sprites = new List<SpriteRenderer>();
        private Transform _spriteRoot;
        private int _spritesUsed;

        /// <summary>The cast-shadow pass: a second mesh on a child object, holding only the silhouettes the
        /// view says cast one. Filled in <see cref="Rebuild"/> alongside the visible mesh, and empty on a
        /// view that casts nothing.</summary>
        private readonly List<Vector3> _shadowVertices = new List<Vector3>();
        private readonly List<Color> _shadowColors = new List<Color>();
        private readonly List<int> _shadowTriangles = new List<int>();
        private Mesh _shadowMesh;
        private GameObject _shadowObject;

        /// <summary>How far behind its view's mesh the shadow pass sits, in world z. Just far enough that the
        /// two swap draw order without the shadow drifting off its body at any zoom.</summary>
        private const float ShadowDepth = 0.002f;

        /// <summary>Sprites sit this far behind the mesh, so its functional overlays draw on top of an
        /// overridden body; small enough never to read as parallax at any zoom.</summary>
        private const float SpriteDepthOffset = 0.001f;

        /// <summary>The simulation this view draws; the mesh lifecycle does not depend on it.</summary>
        protected SimWorld World { get; private set; }

        /// <summary>The one asset the whole look lives in: colours, screen-space thicknesses and
        /// every view's layer.</summary>
        protected Palette Colors => _palette;

        /// <summary>Where this view draws: its z-plane and its sorting order within it.</summary>
        protected abstract Palette.Layer Layer { get; }

        /// <summary>
        /// Whether this view's solid bodies cast a shadow - the default pass that makes the map read as
        /// things standing on a surface rather than as a diagram printed on one.
        ///
        /// A view says yes when everything it draws is a body: the machines, the walkers, the Core. It says
        /// no when it draws the surface itself (the grid, the terrain, the belts under everything), a state
        /// readout rather than a body (health bars, markers, shots, the ghost), or a screen-space mark (the
        /// locator) - a shadow of a health bar would be a shadow of a label, and a shadow of the ground is
        /// just a darker map.
        /// </summary>
        protected virtual bool CastsShadows => false;

        /// <summary>Size of one screen pixel in world units at the current resolution and zoom.</summary>
        protected float WorldPerPixel => _frames != null ? _frames.Frame.WorldPerPixel : 0f;

        /// <summary>The palette's screen-space outline constant at the current zoom, in world units.</summary>
        protected float OutlineWidth => _palette != null ? _palette.OutlinePixels * WorldPerPixel : 0f;

        /// <summary>Build the view: one mesh on this GameObject, on the declared layer, with the shared
        /// view material. Nothing is drawn until the first frame rebuilds.</summary>
        public void Initialize(SimWorld world, FrameClock frames, Palette palette)
        {
            World = world;
            _frames = frames;
            _palette = palette;

            _meshName = "FACET/" + gameObject.name;
            _meshFlags = MeshFlagsFor(gameObject);
            _mesh = NewMesh();
            AttachRenderer(Layer, palette.ResolveViewMaterial());
            if (CastsShadows) AttachShadowPass(palette.ResolveViewMaterial());

            OnInitialized();
            _dirty = true;
        }

        /// <summary>The flags a view's mesh is born with: the same hold on life as the GameObject that
        /// draws it.
        ///
        /// A mesh is not a component, so Unity neither destroys nor keeps it alive with its object: one
        /// with no <see cref="HideFlags.DontSave"/> is swept away by a scene unload the drawing object
        /// survives. That is exactly the pair the Editor's map preview makes - its objects are
        /// <c>HideAndDontSave</c> so a Play-mode transition leaves it alone - and the rule the shared
        /// material in <see cref="ProcMesh"/> and the HUD's pixel texture already follow.</summary>
        private static HideFlags MeshFlagsFor(GameObject owner)
            => (owner.hideFlags & HideFlags.DontSave) != 0 ? HideFlags.DontSave : HideFlags.None;

        private Mesh NewMesh() => new Mesh { name = _meshName, hideFlags = _meshFlags };

        /// <summary>The mesh to fill this frame, made again if it is gone.
        ///
        /// An object marked <c>DontSave</c> can still be destroyed under a running script, and a gone mesh
        /// is one to redraw rather than a reason to throw. Remaking it re-points the filter that held the
        /// old one, so the renderer shows what this frame appends.</summary>
        private Mesh AcquireMesh()
        {
            if (_mesh != null) return _mesh;

            _mesh = NewMesh();

            var filter = GetComponent<MeshFilter>();
            if (filter != null) filter.sharedMesh = _mesh;

            return _mesh;
        }

        /// <summary>The shadow pass's mesh, made again if it is gone - the same rule as
        /// <see cref="AcquireMesh"/>, and the same filter to re-point.</summary>
        private Mesh AcquireShadowMesh()
        {
            if (_shadowMesh != null) return _shadowMesh;

            _shadowMesh = new Mesh { name = _meshName + " Shadow", hideFlags = _meshFlags };

            var filter = _shadowObject != null ? _shadowObject.GetComponent<MeshFilter>() : null;
            if (filter != null) filter.sharedMesh = _shadowMesh;

            return _shadowMesh;
        }

        /// <summary>Free the mesh with the view. A <c>DontSave</c> mesh is nobody else's to destroy, and a
        /// view that rebuilds its meshes would otherwise leak one per object.</summary>
        private void OnDestroy()
        {
            if ((_meshFlags & HideFlags.DontSave) != 0 && _mesh != null) DestroyImmediate(_mesh);
            if ((_meshFlags & HideFlags.DontSave) != 0 && _shadowMesh != null) DestroyImmediate(_shadowMesh);
            _mesh = null;
            _shadowMesh = null;
        }

        /// <summary>Attach the MeshFilter + MeshRenderer pair this view draws through, configured for
        /// flat unlit geometry, and put the object on the layer's z-plane and sorting order. Tile
        /// coordinates are world coordinates, so a wrong z-plane drifts from what the cursor clicks.</summary>
        private void AttachRenderer(Palette.Layer layer, Material material)
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = layer.SortingOrder;

            gameObject.transform.position = new Vector3(0f, 0f, layer.Z);
        }

        /// <summary>
        /// The shadow pass: a child object with its own mesh and renderer, on the view's own sorting order
        /// but <see cref="ShadowDepth"/> further from the camera, so every triangle it holds lands before
        /// every triangle the view draws and a body always covers its own shadow.
        ///
        /// Its own mesh rather than the view's, because a view's mesh is not all body: health bars, spawn
        /// markers and kill bursts ride in the same mesh as the walkers, and the ground under a belt is in
        /// the belt's. The shadow is the silhouettes a view appends into this pass and nothing else.
        /// </summary>
        private void AttachShadowPass(Material material)
        {
            if (_shadowObject != null) return;

            // A rebuild can reach here after the pass was destroyed under a running script: the mesh is
            // kept if it survived, so only the object is rebuilt around it.
            if (_shadowMesh == null)
                _shadowMesh = new Mesh { name = _meshName + " Shadow", hideFlags = _meshFlags };

            _shadowObject = new GameObject("Shadow");
            _shadowObject.hideFlags = _meshFlags;
            _shadowObject.transform.SetParent(transform, false);
            _shadowObject.transform.localPosition = new Vector3(0f, 0f, ShadowDepth);

            var filter = _shadowObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _shadowMesh;

            var renderer = _shadowObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortingOrder = Layer.SortingOrder;
        }

        /// <summary>One-time setup a view needs beyond the mesh. The world and palette are ready.
        /// Called once per view; a map change goes through <see cref="Rebind"/> instead.</summary>
        protected virtual void OnInitialized() { }

        /// <summary>Point this view at a different simulation without rebuilding the mesh machinery: the
        /// mesh, material and pooled sprites stay, and the next frame rebuilds from the new world.
        ///
        /// Anything cached and sized by the old map is dropped in <see cref="OnWorldRebound"/> - the tile
        /// grid, the wave-entry markers, the last belt revision - because those would otherwise keep
        /// drawing the previous map. That is why the hook exists rather than <see cref="Initialize"/>
        /// being called again.</summary>
        public void Rebind(SimWorld world)
        {
            World = world ?? throw new System.ArgumentNullException(nameof(world));
            OnWorldRebound();
            _dirty = true;
        }

        /// <summary>Drop everything cached from the previous world. Called by <see cref="Rebind"/>,
        /// so a view with no such cache needs no override.</summary>
        protected virtual void OnWorldRebound() { }

        /// <summary>Called once per frame before the gate. A view compares its own watched value and
        /// calls <see cref="MarkDirty"/> when it changed. The zoom reason is the gate's, not the view's.</summary>
        protected virtual void Observe(in ViewFrame frame) { }

        /// <summary>Add this frame's geometry. The buffers are empty when this is called.</summary>
        protected abstract void AppendFrame(in ViewFrame frame);

        /// <summary>Ask for a rebuild of the mesh. Every-frame views call this from <see cref="Observe"/>.</summary>
        protected void MarkDirty() => _dirty = true;

        /// <summary>World-space centre of a tile. Tile coordinates are world coordinates, so a cell
        /// spans [x, x+1) and its centre is half a tile in from its origin.</summary>
        protected static Vector2 CellCentre(Int2 cell) => new Vector2(cell.X + 0.5f, cell.Y + 0.5f);

        /// <summary>One CCW quad into the shared buffers.</summary>
        protected void AppendQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            => ProcMesh.AppendQuad(_vertices, _colors, _triangles, a, b, c, d, color);

        /// <summary>One convex CCW polygon, with an optional constant-width outline, into the shared buffers.</summary>
        protected void AppendPolygon(IReadOnlyList<Vector2> points, Vector2 offset,
            Color fill, Color outline, float outlineWidth)
            => ProcMesh.AppendPolygon(_vertices, _colors, _triangles, points, offset, fill, outline, outlineWidth);

        /// <summary>The fill half of one convex CCW polygon, for a figure drawn in two passes.</summary>
        protected void AppendPolygonFill(IReadOnlyList<Vector2> points, Vector2 offset, Color fill)
            => ProcMesh.AppendPolygonFill(_vertices, _colors, _triangles, points, offset, fill);

        /// <summary>The outline half of one convex CCW polygon, for a figure drawn in two passes.</summary>
        protected void AppendPolygonOutline(IReadOnlyList<Vector2> points, Vector2 offset, Color outline,
            float outlineWidth)
            => ProcMesh.AppendPolygonOutline(_vertices, _colors, _triangles, points, offset, outline, outlineWidth);

        /// <summary>One quadrant of a figure, outlined with the rim and the seams at their own widths - see
        /// <see cref="ProcMesh.AppendQuadrantOutline"/>.</summary>
        protected void AppendQuadrantOutline(IReadOnlyList<Vector2> points, Vector2 offset, Color outline,
            float rimWidth, float seamWidth, bool verticalSeam, bool horizontalSeam)
            => ProcMesh.AppendQuadrantOutline(_vertices, _colors, _triangles, points, offset, outline, rimWidth,
                seamWidth, verticalSeam, horizontalSeam);

        /// <summary>One axis-aligned CCW quad from its min/max corners into the shared buffers.</summary>
        protected void AppendRect(float minX, float minY, float maxX, float maxY, Color color)
            => AppendQuad(new Vector2(minX, minY), new Vector2(maxX, minY),
                new Vector2(maxX, maxY), new Vector2(minX, maxY), color);

        /// <summary>One lane between two points, as a CCW quad of width 2w.</summary>
        protected void AppendLane(Vector2 from, Vector2 to, float halfWidth, Color color)
        {
            Vector2 along = to - from;
            var right = new Vector2(-along.y, along.x).normalized * halfWidth;

            AppendQuad(from - right, from + right, to + right, to - right, color);
        }

        /// <summary>One filled chevron pointing along <paramref name="direction"/>, at
        /// <paramref name="back"/> behind and <paramref name="length"/> ahead of the centre. Right-hand
        /// point first, tip last, so the winding stays counter-clockwise for all four directions.</summary>
        protected void AppendChevron(Vector2 centre, Dir direction, float back, float halfWidth,
            float length, Color color)
        {
            Vec2 forward2 = direction.ToVec();
            var forward = new Vector2(forward2.X, forward2.Y);
            var right = new Vector2(-forward.y, forward.x);

            _chevron[0] = centre - forward * back + right * halfWidth;
            _chevron[1] = centre - forward * back - right * halfWidth;
            _chevron[2] = centre + forward * length;

            AppendPolygon(_chevron, Vector2.zero, color, color, 0f);
        }

        /// <summary>The ground drop of a body, in world units at the current zoom.</summary>
        private Vector2 ShadowDrop => Colors.ShadowOffsetPixels * WorldPerPixel;

        /// <summary>The shadow's outer pass: the same silhouette further out and fainter. Two silhouettes
        /// are what make a shadow read as light falling off rather than as a second outline - the near one
        /// carries the darkness under the body's edge, the far one the fade past it.</summary>
        private Color SoftShadow
        {
            get
            {
                Color soft = Colors.Shadow;
                soft.a *= 0.45f;
                return soft;
            }
        }

        private void AppendShadowSilhouette(IReadOnlyList<Vector2> points, Vector2 offset, Vector2 drop,
            Color colour)
            => ProcMesh.AppendPolygonFill(_shadowVertices, _shadowColors, _shadowTriangles, points,
                offset + drop, colour);

        /// <summary>
        /// The shadow a body casts: its own silhouette, filled with the palette's shadow colour and dropped
        /// by the palette's offset. Call it once per solid body, immediately before that body is drawn.
        ///
        /// One shadow per body, and the body's dominant mass rather than each of its parts: the pass is
        /// translucent, so two silhouettes of one machine laid over each other would blend twice as dark
        /// where they cross. A turret's barrel is its aiming readout, not its mass - the octagon is what
        /// stands on the ground.
        /// </summary>
        protected void AppendShadow(IReadOnlyList<Vector2> points, Vector2 offset)
        {
            if (!CastsShadows) return;
            AppendShadowSilhouette(points, offset, ShadowDrop, Colors.Shadow);
            AppendShadowSilhouette(points, offset, ShadowDrop * Colors.ShadowSpread, SoftShadow);
        }

        /// <summary>The shadow of a rectangular part - a drill's frame bar, a wall's inset block.</summary>
        protected void AppendShadowRect(float minX, float minY, float maxX, float maxY)
        {
            if (!CastsShadows) return;
            AppendShadowRectPass(minX, minY, maxX, maxY, ShadowDrop, Colors.Shadow);
            AppendShadowRectPass(minX, minY, maxX, maxY, ShadowDrop * Colors.ShadowSpread, SoftShadow);
        }

        private void AppendShadowRectPass(float minX, float minY, float maxX, float maxY, Vector2 drop,
            Color colour)
            => ProcMesh.AppendQuad(_shadowVertices, _shadowColors, _shadowTriangles,
                new Vector2(minX, minY) + drop, new Vector2(maxX, minY) + drop,
                new Vector2(maxX, maxY) + drop, new Vector2(minX, maxY) + drop, colour);

        /// <summary>The shadow of a lane - one rail of a pipe, whose body is lines rather than a block.</summary>
        protected void AppendShadowLane(Vector2 from, Vector2 to, float halfWidth)
        {
            if (!CastsShadows) return;

            Vector2 delta = to - from;
            if (delta.sqrMagnitude <= 1e-12f) return;

            AppendShadowLanePass(from, to, halfWidth, ShadowDrop, Colors.Shadow);
            AppendShadowLanePass(from, to, halfWidth, ShadowDrop * Colors.ShadowSpread, SoftShadow);
        }

        private void AppendShadowLanePass(Vector2 from, Vector2 to, float halfWidth, Vector2 drop, Color colour)
        {
            Vector2 delta = to - from;
            var right = new Vector2(-delta.y, delta.x).normalized * halfWidth;
            Vector2 a = from + drop;
            Vector2 b = to + drop;
            // Counter-clockwise, the order AppendQuad's fill fan is built for.
            ProcMesh.AppendQuad(_shadowVertices, _shadowColors, _shadowTriangles,
                a - right, b - right, b + right, a + right, colour);
        }

        /// <summary>Draw one frame. The rebuild gate: ask the view for a rebuild, then add the two
        /// shared reasons - the zoom moved the pixel size past tolerance (keeping screen-space constants
        /// true), and the mesh is gone. An invalid frame is skipped rather than drawn at zero zoom.
        ///
        /// A method rather than only a message because the Editor's map preview draws these views with no
        /// game running, so frames are handed in by hand - one per Scene view repaint. That is why a map
        /// can be seen without pressing Play, and why the frame is a parameter rather than the clock's.</summary>
        public void Draw(in ViewFrame frame)
        {
            if (_frames == null || _palette == null) return;
            if (frame.WorldPerPixel <= 0f) return;

            Observe(in frame);
            if (_dirty || ZoomMoved(frame.WorldPerPixel) || _mesh == null ||
                (CastsShadows && (_shadowMesh == null || _shadowObject == null))) Rebuild(in frame);
        }

        /// <summary>One rebuild: empty buffers, the view's geometry, one mesh holding it - and, on a view
        /// that casts shadows, the second mesh holding the silhouettes it said cast one.</summary>
        private void Rebuild(in ViewFrame frame)
        {
            _dirty = false;
            _builtWorldPerPixel = frame.WorldPerPixel;

            _vertices.Clear();
            _colors.Clear();
            _triangles.Clear();

            bool shadows = CastsShadows;
            if (shadows)
            {
                // A gone pass is rebuilt rather than drawn around: the object is what carries the mesh,
                // and a silhouette with nowhere to go would leave a body standing on nothing.
                if (_shadowObject == null) AttachShadowPass(_palette.ResolveViewMaterial());

                _shadowVertices.Clear();
                _shadowColors.Clear();
                _shadowTriangles.Clear();
            }

            Mesh mesh = AcquireMesh();
            AppendFrame(in frame);
            ProcMesh.Finish(_vertices, _colors, _triangles, _meshName, mesh);

            if (shadows)
                ProcMesh.Finish(_shadowVertices, _shadowColors, _shadowTriangles, _meshName + " Shadow",
                    AcquireShadowMesh());
        }

        /// <summary>Unity's own call, for the frames it drives: whatever the clock holds. A play-mode
        /// call - see the class comment on <see cref="ExecuteAlways"/>; in the editor a view draws only
        /// when its caller hands it a frame.</summary>
        private void LateUpdate()
        {
            if (!Application.isPlaying || _frames == null) return;
            Draw(_frames.Frame);
        }

        /// <summary>Outline width for a resolved style, in world units, at the current zoom.</summary>
        protected float OutlineWidthFor(VisualStyle style)
            => style != null ? style.OutlinePixels * WorldPerPixel : OutlineWidth;

        /// <summary>Reset the sprite pool cursor before drawing this frame's overrides.</summary>
        protected void BeginSprites() => _spritesUsed = 0;

        /// <summary>Draw one sprite centred on a world position, scaled so its width and height are
        /// 2 * <paramref name="halfExtent"/> tiles (<paramref name="halfExtent"/> 0 keeps native size),
        /// then offset and rotated. A centred pivot is assumed; sprites are pooled behind the mesh.</summary>
        protected void DrawSprite(Sprite sprite, Vector2 centre, float halfExtent,
            float rotation, Vector2 offset, Color tint)
        {
            if (sprite == null) return;

            SpriteRenderer renderer = RentSprite();
            renderer.sprite = sprite;
            renderer.color = tint;

            Vector2 native = sprite.bounds.size;
            float scaleX = halfExtent > 0f && native.x > 0f ? (halfExtent * 2f) / native.x : 1f;
            float scaleY = halfExtent > 0f && native.y > 0f ? (halfExtent * 2f) / native.y : 1f;

            renderer.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            renderer.transform.localPosition = new Vector3(centre.x + offset.x, centre.y + offset.y, SpriteDepthOffset);
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            renderer.gameObject.SetActive(true);
            _spritesUsed++;
        }

        /// <summary>Draw a style's authored Sprite at a world position with a tint: the short form of
        /// the full call, used wherever a view replaces a body with an image.</summary>
        protected void DrawSprite(VisualStyle style, Vector2 centre, Color tint)
            => DrawSprite(style.Sprite, centre, style.Size, style.Rotation, style.Offset, tint);

        /// <summary>Hide any pooled sprite this frame did not use, so a removed entity stops drawing.
        /// Call after the frame's sprites were drawn.</summary>
        protected void EndSprites()
        {
            for (int i = _spritesUsed; i < _sprites.Count; i++)
            {
                if (_sprites[i].gameObject.activeSelf) _sprites[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Draw one item as its quadrant figure: every filled quadrant's mark, in that mineral's colour
        /// with the standard outline. Empty quadrants are drawn as nothing - the filled part alone is the
        /// silhouette, so a full circle is a disc and a circle-left/square-right item is half a disc beside
        /// half a square.
        ///
        /// <b>Every fill first, then every outline.</b> The quadrants tile the item, so an outline grown
        /// outward from one of them lies inside its neighbour - and in a single mesh a later triangle paints
        /// over an earlier one. Fill-then-outline per quadrant therefore let each quadrant's fill erase the
        /// outline the quadrant before it drew along their shared axis, which left the figure's own seam cross
        /// lopsided and off the item's centre: two passes are what keep the quarter seams (and the cut readout
        /// they are) symmetrical about the middle. One mesh, so no depth buffer settles this for us.
        ///
        /// The second pass also gives the seams their own width (<see cref="Palette.SeamFraction"/>) and caps
        /// the rim at a share of the figure's own radius (<see cref="Palette.OutlineMaxRadiusFraction"/>):
        /// both are about the figure staying readable once the zoom has made it small, where an outline held
        /// at a constant number of pixels would otherwise be most of what is on screen.
        ///
        /// A quadrant whose mineral is overridden with a Sprite draws that sprite inside the quadrant
        /// instead (a sprite is a whole mark and has no point list); a <paramref name="tint"/> recolours
        /// either form. The one place every view that draws an item on the map goes through, and it allocates
        /// nothing: the points and styles are the icon set's, cached at initialisation.
        /// </summary>
        protected void AppendIcon(ShapeIconSet icons, Shape shape, Vector2 centre, Color? tint)
        {
            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Quad mineral = shape.At(quadrant);
                if (mineral == Quad.Empty) continue;

                VisualStyle style = icons.Style(mineral, quadrant);
                if (style == null) continue;

                if (style.Source == VisualSource.Sprite)
                {
                    // The sprite's own size is the author's; how much of the quadrant it may fill and where
                    // the quadrant is are the item's, so a sprite authored larger than its quadrant is
                    // clamped into it rather than laid over its neighbours.
                    DrawSprite(style.Sprite, centre, Mathf.Min(style.Size, icons.Radius) * 0.5f, style.Rotation,
                        style.Offset + VisualShapes.QuadrantCentre(icons.Radius, quadrant),
                        tint ?? Color.white);
                    continue;
                }

                IReadOnlyList<Vector2> points = icons.Points(mineral, quadrant);
                if (points == null) continue;

                AppendPolygonFill(points, centre + style.Offset, tint ?? style.Fill);
            }

            for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
            {
                Quad mineral = shape.At(quadrant);
                if (mineral == Quad.Empty) continue;

                VisualStyle style = icons.Style(mineral, quadrant);
                if (style == null || style.Source == VisualSource.Sprite) continue;

                IReadOnlyList<Vector2> points = icons.Points(mineral, quadrant);
                if (points == null) continue;

                // An outline is a screen constant, and a screen constant has a size in the figure's own
                // terms that grows as the figure shrinks: at the far end of the zoom a disc is thirteen
                // pixels across, and half of it was ring. Past this share of the figure's radius the cap
                // wins, so what is drawn at the edge of the zoom is the shape rather than its outline.
                float rim = Mathf.Min(OutlineWidthFor(style), icons.Radius * Colors.OutlineMaxRadiusFraction);

                // The two edges a quadrant shares with its neighbours are the figure's seams, and each side
                // draws half of one - which is what makes the cross in a disc a line rather than a band
                // twice the rim's width. A flat side with a hole behind it is rim, not seam.
                AppendQuadrantOutline(points, centre + style.Offset, style.Outline, rim,
                    rim * Colors.SeamFraction,
                    verticalSeam: shape.Has(Shape.AcrossVertical(quadrant)),
                    horizontalSeam: shape.Has(Shape.AcrossHorizontal(quadrant)));
            }
        }

        private SpriteRenderer RentSprite()
        {
            while (_spritesUsed >= _sprites.Count)
            {
                if (_spriteRoot == null)
                {
                    var root = new GameObject("Sprites");
                    root.transform.SetParent(transform, false);
                    _spriteRoot = root.transform;
                }

                var go = new GameObject("Sprite");
                go.transform.SetParent(_spriteRoot, false);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = Layer.SortingOrder;
                go.SetActive(false);
                _sprites.Add(renderer);
            }

            return _sprites[_spritesUsed];
        }

        private bool ZoomMoved(float worldPerPixel)
            => _builtWorldPerPixel <= 0f
               || Mathf.Abs(worldPerPixel / _builtWorldPerPixel - 1f) >= ZoomTolerance;
    }
}
