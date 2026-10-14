using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Everything FACET draws, as one asset: the shared material, every colour and outline
    /// thickness, the per-view geometry (radii, insets, chevrons, alphas) and layer depths. The art rule
    /// "colour says which side it is on, polygon count says how damaged it is" makes this the single
    /// source of truth for the look, so a re-skin is a duplicate of this asset, not a code change. One
    /// number is deliberately absent: an enemy's body radius derives from
    /// <see cref="Balance.EnemySpacing"/>, so the drawn body and the room a walker keeps cannot drift.
    ///
    /// Palette is light: a near-white map on a darker void, dark flat shapes and one dark outline, so a
    /// hundred small coloured parts stay readable. Thicknesses are in screen pixels because FACET has no
    /// textures and no fixed zoom; a constant world-unit width would be wrong at every zoom but one.</summary>
    [CreateAssetMenu(menuName = "FACET/Palette", fileName = "Palette")]
    public sealed class Palette : ScriptableObject
    {
        [Tooltip("Everything outside the map: the void behind the world.")]
        public Color Background = new Color(0.878f, 0.886f, 0.902f, 1f);

        [Tooltip("The map itself: the ground every buildable tile sits on.")]
        public Color Platform = new Color(0.965f, 0.969f, 0.976f, 1f);

        [Tooltip("Tile grid lines, drawn faintly on the ground.")]
        public Color GridLine = new Color(0.055f, 0.070f, 0.094f, 0.09f);

        [Tooltip("Tile grid line thickness in screen pixels.")]
        public float GridLinePixels = 1f;

        [Tooltip("Outline used by every polygon. Dark on purpose: it is what separates one part from another.")]
        public Color Outline = new Color(0.157f, 0.176f, 0.216f, 1f);

        [Tooltip("Outline thickness in screen pixels (design doc: 2 px).")]
        public float OutlinePixels = 2f;

        [Tooltip("Cursor ghost border thickness in screen pixels.")]
        public float CursorEdgePixels = 5f;

        [Tooltip("Belt bed.")]
        public Color Belt = new Color(0.804f, 0.820f, 0.847f, 1f);

        [Tooltip("Belt direction chevron. Reads as 'this way'.")]
        public Color BeltArrow = new Color(0.427f, 0.455f, 0.502f, 1f);

        [Tooltip("A jammed belt segment - the twist's failure state, so it has to shout.")]
        public Color Jam = new Color(0.878f, 0.263f, 0.263f, 1f);

        [Tooltip("The bed of a shape patch in the ground. The shape itself is drawn in its own colour.")]
        public Color ShapePatchBed = new Color(0.902f, 0.898f, 0.871f, 1f);

        [Tooltip("Map-authored rock: the ground a wave has to walk around. Dark and inert, so it reads " +
            "as the map rather than as something the player built - the wall they buy is drawn as a " +
            "machine, in the machines' colour.")]
        public Color Rock = new Color(0.290f, 0.318f, 0.365f, 1f);

        [Tooltip("Map-authored water: the other impassable ground. Blue is otherwise the half-circle " +
            "ammunition's colour, so this is deliberately a paler, greyer blue than the shapes use - " +
            "the ground must never be mistaken for something riding a belt.")]
        public Color Water = new Color(0.478f, 0.663f, 0.796f, 1f);

        [Tooltip("Drill and decomposer bodies: a machine that is not a turret.")]
        public Color MachineBody = new Color(0.361f, 0.408f, 0.482f, 1f);

        [Tooltip("Turret bodies, one shade darker than the production machines.")]
        public Color TurretBody = new Color(0.239f, 0.278f, 0.345f, 1f);

        [Tooltip("A machine icon with nothing to work on: a starved turret's ammo shape, an idle decomposer.")]
        public Color MachineIdle = new Color(0.749f, 0.776f, 0.816f, 1f);

        [Tooltip("Range ring drawn around the turret being placed.")]
        public Color RangeRing = new Color(0.243f, 0.353f, 0.549f, 0.5f);

        [Tooltip("Spike: fast and light, weak to half-circles.")]
        public Color SpikeBody = new Color(0.867f, 0.286f, 0.247f, 1f);

        [Tooltip("Bulwark: slow and heavy, weak to half-squares. A darker brick red than the Spike - " +
            "colour says which side it is on, so the second enemy stays in the enemy family rather " +
            "than wearing one of the ammunitions' cool colours.")]
        public Color BulwarkBody = new Color(0.545f, 0.173f, 0.192f, 1f);

        [Tooltip("Wave entry points: brighter while the next wave is counting down.")]
        public Color SpawnMarker = new Color(0.851f, 0.290f, 0.259f, 1f);

        [Tooltip("Circle mineral - the currency: banked at the Core, spent on buildings.")]
        public Color CircleShape = new Color(1f, 0.780f, 0.280f, 1f);

        [Tooltip("Half-circle ammo - the Cannon's diet, split out of circles.")]
        public Color HalfCircleShape = new Color(0.361f, 0.757f, 1f, 1f);

        [Tooltip("Square mineral - the second chain's food, mined from square patches. Warm like the " +
            "circle, so the two minerals read as 'mined' rather than as one mineral and one product.")]
        public Color SquareShape = new Color(0.941f, 0.376f, 0.678f, 1f);

        [Tooltip("Half-square ammo - the Mortar's diet, split out of squares. Cool like the half-circle, " +
            "but green rather than blue: the two ammunitions have to be told apart at a glance, because " +
            "feeding the wrong gun one of them jams the line.")]
        public Color HalfSquareShape = new Color(0.322f, 0.831f, 0.729f, 1f);

        [Tooltip("The defended Core.")]
        public Color Core = new Color(0.976f, 0.980f, 0.988f, 1f);

        [Tooltip("What the Core tints toward as it takes damage.")]
        public Color CoreDamage = new Color(0.878f, 0.263f, 0.263f, 1f);

        [Tooltip("Ghost preview where the cursor may build.")]
        public Color CursorOk = new Color(0.192f, 0.706f, 0.376f, 1f);

        [Tooltip("Ghost preview where it may not.")]
        public Color CursorBlocked = new Color(0.851f, 0.239f, 0.239f, 1f);

        [Tooltip("Ghost preview where the build is legal but the stockpile cannot cover it.")]
        public Color CursorNoFunds = new Color(0.945f, 0.651f, 0.253f, 1f);

        [Tooltip("HUD panel behind the readouts.")]
        public Color HudPanel = new Color(0.055f, 0.071f, 0.098f, 0.82f);

        [Tooltip("HUD body text.")]
        public Color HudText = new Color(0.933f, 0.949f, 0.969f, 1f);

        [Tooltip("HUD emphasis: the selected building, the wave number.")]
        public Color HudAccent = new Color(0.353f, 0.722f, 1f, 1f);

        [Tooltip("HUD warning: a jam, a leaking Core, a lost run.")]
        public Color HudWarn = new Color(0.945f, 0.451f, 0.353f, 1f);

        [Tooltip("HUD good news: turrets armed, a held wave.")]
        public Color HudGood = new Color(0.290f, 0.788f, 0.451f, 1f);

        [Header("Material")]
        [Tooltip("The one material every view draws with: unlit, multiplying the meshes' vertex " +
            "colours (the built-in 'Sprites/Default' shader is the one that does). It lives as an " +
            "asset so a build cannot strip it: every mesh is generated at runtime, so nothing else " +
            "carries the shader reference into a build. When empty, a runtime material is created " +
            "as a fallback - fine in the Editor, which never strips shaders; fatal in a build.")]
        public Material ViewMaterial;

        [Header("Shapes")]
        [Tooltip("Sides of every 'circle' polygon. 16 reads as round at any zoom the camera allows.")]
        public int CircleSides = 16;

        [Header("Layers")]
        public Layer GridLayer = new Layer(0f, -10);
        public Layer TerrainLayer = new Layer(-0.01f, -9);
        public Layer ShapePatchLayer = new Layer(-0.02f, -8);
        public Layer BeltLayer = new Layer(-0.05f, -5);
        public Layer CoreLayer = new Layer(-0.06f, 0);
        public Layer MachineLayer = new Layer(-0.07f, 1);
        public Layer ItemLayer = new Layer(-0.08f, 0);
        public Layer EnemyLayer = new Layer(-0.09f, 5);
        public Layer ProjectileLayer = new Layer(-0.10f, 6);
        public Layer CursorLayer = new Layer(-0.2f, 10);

        /// <summary>Where the off-screen locator draws: in front of the placement ghost, so the arrow is not
        /// hidden by it. It is the only view whose geometry is screen geometry, sitting at the open screen's
        /// edge whatever the map does, so it is never confused with a tile.</summary>
        public Layer RadarLayer = new Layer(-0.3f, 12);

        [Header("Belts")]
        public BeltLook Belts = new BeltLook();

        [Header("Shape patches in the ground")]
        public ShapePatchLook Patches = new ShapePatchLook();

        [Header("Items riding the belts")]
        public ItemLook Items = new ItemLook();

        [Header("Drills, decomposers and turrets")]
        public MachineLook Machines = new MachineLook();

        [Header("Enemies and wave entry points")]
        public EnemyLook Enemies = new EnemyLook();

        [Header("The defended Core")]
        public CoreLook CoreBlock = new CoreLook();

        [Header("Shots in flight")]
        public ProjectileLook Projectiles = new ProjectileLook();

        [Header("Placement ghost")]
        public CursorLook Cursor = new CursorLook();

        [Header("Out-of-view locator")]
        public RadarLook Radar = new RadarLook();

        [Header("Per-content visual overrides")]
        [Tooltip("Tick an entry's Override to replace that content's built-in look - a sprite for the " +
            "Core, the shapes, an enemy or a machine body, or a different procedural silhouette/colour. " +
            "Untouched entries keep the shipped look, so this block is purely additive: open " +
            "Assets/Data/Palette.asset to re-skin one thing without touching code.")]
        public VisualCatalog Visuals = new VisualCatalog();

        /// <summary>Body colour for a machine, keyed on what it <em>does</em> rather than its tile kind,
        /// so the second gun reads as a turret the moment its row says so.</summary>
        public Color BodyColor(BehaviorKind behavior)
            => behavior == BehaviorKind.Turret ? TurretBody : MachineBody;

        /// <summary>Body colour for an enemy; every kind is named, so a third enemy cannot borrow the
        /// Spike's colour - the switch says there is no colour for it yet. The roster test walks the same
        /// enum.</summary>
        public Color EnemyColor(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Spike: return SpikeBody;
                case EnemyKind.Bulwark: return BulwarkBody;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(kind), kind,
                        "a new enemy must get a body colour here (Palette) and a look in EnemyVisuals");
            }
        }

        /// <summary>Colour for one shape. <see cref="ShapeType.None"/> deliberately draws as the
        /// outline; an unlisted shape is a hole in this file, not a fallback.</summary>
        public Color ShapeColor(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.None: return Outline;
                case ShapeType.Circle: return CircleShape;
                case ShapeType.HalfCircle: return HalfCircleShape;
                case ShapeType.Square: return SquareShape;
                case ShapeType.HalfSquare: return HalfSquareShape;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(shape), shape,
                        "a new shape must get a colour here (Palette) and a silhouette below");
            }
        }

        /// <summary>The one place that decides a shape's shipped silhouette: a ground patch, a belt item, a
        /// machine's held item and an enemy's weakness all come through here, so a new mineral is a colour
        /// and a case rather than four drawings.</summary>
        public ProcShape ProcShapeOf(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.None:
                case ShapeType.Circle: return ProcShape.Circle;
                case ShapeType.HalfCircle: return ProcShape.HalfDisc;
                case ShapeType.Square: return ProcShape.RegularPolygon;
                case ShapeType.HalfSquare: return ProcShape.HalfRect;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(shape), shape,
                        "a new shape must get a silhouette here (Palette.ProcShapeOf)");
            }
        }

        /// <summary>Sides for a shape drawn as a regular polygon. The other silhouettes ignore it.</summary>
        public int SidesOf(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Square: return 4;
                default: return CircleSides;   // every round shape - and only the known ones reach here
            }
        }

        /// <summary>The icon set every view that draws a shape as an icon builds from - a belt item, a
        /// ground patch, an enemy's weakness, a machine's held item, a shot. One set covers every shape in
        /// the content table.</summary>
        public ShapeIconSet ShapeIcons(float radius, float outlinePixels)
            => new ShapeIconSet(this, Visuals.Items, radius, outlinePixels);

        /// <summary>The material every view shares: the assigned asset, or a runtime-built fallback on the
        /// vertex-colour shader, which keeps an unwired scene and the PlayMode tests runnable.</summary>
        public Material ResolveViewMaterial()
        {
            return ViewMaterial != null ? ViewMaterial : ProcMesh.UnlitMaterial();
        }

        /// <summary>Where one view draws: its z-plane and sorting order. Tile coordinates are world
        /// coordinates, so a view at the wrong Z drifts from what the cursor clicks on.</summary>
        [Serializable]
        public sealed class Layer
        {
            [Tooltip("World-space z of the view's mesh. The camera sits at -10, so more negative is closer to it.")]
            public float Z;

            [Tooltip("Sorting order within the mesh queue; higher draws on top.")]
            public int SortingOrder;

            public Layer(float z, int sortingOrder)
            {
                Z = z;
                SortingOrder = sortingOrder;
            }
        }

        [Serializable]
        public sealed class BeltLook
        {
            [Tooltip("Half the width of a belt strip. Neighbouring belts connect edge to edge, " +
                "so the gap between two parallel lines is what keeps the tile grid reading.")]
            public float HalfWidth = 0.17f;

            [Tooltip("Direction chevron: how far the tip reaches toward the next cell.")]
            public float ChevronLength = 0.20f;
            [Tooltip("Direction chevron: how far the base sits behind the cell centre.")]
            public float ChevronBack = 0.12f;
            [Tooltip("Direction chevron: half the base width.")]
            public float ChevronHalfWidth = 0.18f;
        }

        [Serializable]
        public sealed class ShapePatchLook
        {
            [Tooltip("Bed inset in tiles on every side, so the patch does not collide with the grid lines.")]
            public float BedInset = 0.06f;

            [Tooltip("Icon radius in tiles. Small enough to sit inside the drill's frame once one is placed.")]
            public float IconRadius = 0.28f;
        }

        [Serializable]
        public sealed class ItemLook
        {
            [Tooltip("Item radius in tiles. Small enough that a packed belt still reads as separate cells.")]
            public float Radius = 0.22f;
        }

        [Serializable]
        public sealed class MachineLook
        {
            [Tooltip("Drill frame: inset from the tile edge, in tiles. The drill is a frame, not a block, " +
                "so the shape patch icon underneath stays visible.")]
            public float FrameInset = 0.04f;
            [Tooltip("Drill frame: bar thickness, in tiles.")]
            public float FrameThickness = 0.14f;

            public float DecomposerRadius = 0.44f;
            public float DecomposerIconRadius = 0.24f;
            public float TurretRadius = 0.42f;
            [Tooltip("Ammo icon on a turret - the armed/silenced readout.")]
            public float AmmoIconRadius = 0.22f;

            [Tooltip("Barrel length measured from the turret centre.")]
            public float BarrelLength = 0.62f;
            [Tooltip("Barrel half width.")]
            public float BarrelHalfWidth = 0.08f;

            [Tooltip("Pipe: how far off the tube's centreline each rail runs.")]
            public float PipeRailOffset = 0.18f;
            [Tooltip("Pipe: rail thickness.")]
            public float PipeRailWidth = 0.07f;
            [Tooltip("Pipe: end collar half width - the collars are what read as 'over'.")]
            public float PipeCollarHalfWidth = 0.30f;
            [Tooltip("Pipe: end collar thickness along the flow.")]
            public float PipeCollarThickness = 0.10f;
            [Tooltip("Pipe: radius of the item riding over the tube.")]
            public float PipeItemRadius = 0.18f;

            [Tooltip("Splitter: hub half size. The hub is a box the port stubs dock into.")]
            public float SplitterHubHalfSize = 0.30f;
            [Tooltip("Splitter: port stub half width, matched to the belt strip.")]
            public float SplitterStubHalfWidth = 0.17f;

            [Tooltip("Wall: inset from the tile edge, in tiles. A wall is a plain block, a touch smaller " +
                "than its cell so the grid still reads between two of them.")]
            public float WallInset = 0.1f;

            [Tooltip("Sorter: radius of the triangle whose apex points out the side the filtered shape " +
                "leaves by. The apex is the direction readout - a sorter needs no chevron, and no other " +
                "building is a triangle, so the silhouette says 'router' on its own.")]
            public float SorterRadius = 0.44f;
            [Tooltip("Sorter: radius of the shape badge at its centre - which shape it routes.")]
            public float SorterIconRadius = 0.17f;

            [Tooltip("Outlet chevron, scaled down from the belt's: it points out the conversion direction " +
                "without competing with the tile's own arrow.")]
            public float ChevronLength = 0.13f;
            public float ChevronBack = 0.08f;
            public float ChevronHalfWidth = 0.11f;
            [Tooltip("How far toward the outlet edge the chevron sits.")]
            public float ChevronOffset = 0.36f;
        }

        [Serializable]
        public sealed class EnemyLook
        {
            /// <summary>Body radius in tiles: half of <see cref="Balance.EnemySpacing"/>, the simulation's
            /// rule for how close two walkers may stand, so a re-skin cannot make two enemies overlap.</summary>
            public float BodyRadius => Balance.EnemySpacing * 0.5f;

            [Tooltip("Weakness-icon radius. Roughly half the body, so it reads as a label, not a second enemy.")]
            public float WeaknessRadius = 0.20f;

            [Tooltip("The spawn-point frame sits this far inside its cell, so it never touches the grid lines.")]
            public float SpawnFrameInset = 0.10f;
            [Tooltip("Frame band thickness in tiles. Thin enough to be a marker, thick enough to see across the map.")]
            public float SpawnFrameThickness = 0.12f;
            [Tooltip("The next-wave preview quad sits this far inside its cell, visibly inside the hollow frame.")]
            public float SpawnInnerMarkerInset = 0.28f;
            [Tooltip("Steady frame alpha while a wave is running: present but not shouting over the fight.")]
            public float SpawnWaveRunningAlpha = 0.22f;
            [Tooltip("Intermission pulse oscillates around this alpha, so the countdown is visible on the map itself.")]
            public float SpawnPulseCenter = 0.55f;
            public float SpawnPulseAmplitude = 0.20f;
            public float SpawnPulseCycles = 3f;
            [Tooltip("The preview quad is near-solid: 'this door opens next' has to survive being glanced at.")]
            public float SpawnPreviewAlpha = 0.9f;

            [Tooltip("Hit flash: how many ticks an enemy stays white-hot after a shot lands. Long " +
                "enough to see, short enough that a stream of hits reads as a stream.")]
            public int HitFlashTicks = 4;
            [Tooltip("Hit flash: the colour an enemy's body is replaced with while it flashes.")]
            public Color HitFlash = new Color(1f, 1f, 1f, 1f);

            [Tooltip("Kill burst: how many ticks the ring is drawn for after a kill.")]
            public int KillRingTicks = 10;
            [Tooltip("Kill burst: how far the ring has expanded when it fades out, in tiles.")]
            public float KillRingRadius = 0.8f;
            [Tooltip("Kill burst: ring thickness in screen pixels at the moment of the kill - it thins " +
                "as it expands, which is what reads as a burst rather than a bubble.")]
            public float KillRingPixels = 5f;
            [Tooltip("Kill burst: segments in the ring. Enough that a three-quarter-tile ring reads round.")]
            public int KillRingSegments = 28;
        }

        [Serializable]
        public sealed class CoreLook
        {
            [Tooltip("Gap left between the block and its edge tiles, so the grid still reads around it.")]
            public float Inset = 0.08f;
            [Tooltip("Side of the emblem, in tiles. It is the block's identity, not a wall.")]
            public float EmblemTiles = 1.4f;
        }

        [Serializable]
        public sealed class ProjectileLook
        {
            [Tooltip("Shot radius in tiles. Smaller than a belt item so the flight reads as something " +
                "in motion, not a second row of items.")]
            public float Radius = 0.13f;
        }

        [Serializable]
        public sealed class CursorLook
        {
            public float FillAlpha = 0.16f;
            public float EdgeAlpha = 0.85f;
            [Tooltip("Alpha of the machine-footprint ghost inside the cell.")]
            public float BodyAlpha = 0.3f;
            [Tooltip("Inset of the machine-footprint ghost quad.")]
            public float BodyInset = 0.14f;
            [Tooltip("Cap on the border, so a far-zoomed-out cell's edges can never cross each other.")]
            public float MaxEdgeThickness = 0.45f;
            [Tooltip("Segments in the range ring. Enough that a 9-tile radius reads as a circle.")]
            public int RingSegments = 72;
            public float ChevronLength = 0.26f;
            public float ChevronBack = 0.16f;
            public float ChevronHalfWidth = 0.22f;
        }

        /// <summary>The out-of-view locator: what an arrow pointing at something the player cannot see is
        /// made of. Every size is in <em>screen pixels</em> rather than tiles, because the arrow is a mark on
        /// the window: a fixed world-unit size would shrink to nothing at exactly the zoom where the player
        /// most needs to be told a wave is off screen. The one world-unit number is the fade's distance band,
        /// which is the map's, in tiles.</summary>
        [Serializable]
        public sealed class RadarLook
        {
            [Tooltip("How far in from the edge of the open screen an arrow sits. The panels' own strips are " +
                "already given up by the locator's rectangle; this is the clearance so an arrow never " +
                "touches the card it just stepped around.")]
            public float EdgeInsetPixels = 16f;

            [Tooltip("Arrow length, tip to base, in pixels.")]
            public float MarkerLengthPixels = 22f;

            [Tooltip("Arrow half-width at its base, in pixels.")]
            public float MarkerHalfWidthPixels = 11f;

            [Tooltip("Two enemies whose arrows would land this close together on the edge are one arrow: a " +
                "wave walking in from one door is a single piece of news, not sixteen.")]
            public float MergePixels = 46f;

            [Tooltip("Cap on the arrows drawn at once. Beyond it a subject joins the nearest arrow of its " +
                "kind rather than adding another, and the count that arrow carries stays true.")]
            public int MaxMarkers = 12;

            [Tooltip("One dot per enemy the arrow stands for, stacked along the edge - up to this many, of " +
                "this size, this far apart. Past the cap the dots stop; a row of pips an edge cannot hold is " +
                "a row of pips nobody can count.")]
            public int MaxPips = 6;
            public float PipRadiusPixels = 3f;
            public float PipGapPixels = 8f;

            [Tooltip("The arrow is brightest this close to what the player is looking at and dimmest this " +
                "far: distance is what says whether an off-screen wave is news or an emergency.")]
            public float NearTiles = 14f;
            public float FarTiles = 48f;
            public float AlphaNear = 0.95f;
            public float AlphaFar = 0.35f;

            [Tooltip("An arrow whose enemies are hitting something right now does not fade with distance: it " +
                "pulses at full strength over this many ticks, because that is the one arrow the player has " +
                "to look at.")]
            public int UrgentPulseTicks = 20;
            public float UrgentPulseLow = 0.45f;

            [Tooltip("The Core's own arrow: the run's one home marker, in the Core's colour and deliberately " +
                "quieter than a threat - it is navigation, not a warning.")]
            public float CoreMarkerScale = 0.8f;
            public float CoreAlpha = 0.5f;

            [Tooltip("How long after the Core is hit the danger frame holds, in ticks: long enough to see at " +
                "any frame rate, short enough that it marks the hit rather than the aftermath.")]
            public int DangerTicks = 18;

            [Tooltip("The danger frame: the band drawn around the open screen while the Core is being chewed " +
                "off camera, and how loud it is. It says look back without saying where, so it stays thin and " +
                "translucent - the arrows are what say where.")]
            public float DangerThicknessPixels = 10f;
            public float DangerAlpha = 0.3f;

            [Tooltip("A jam marker: the square's half size in pixels. A jam is the one thing off-screen the " +
                "player can fix with a single click, so it is marked in the red a jammed segment wears on the " +
                "belt itself.")]
            public float JamMarkerHalfPixels = 8f;
        }
    }
}
