using System;
using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Where the map is bigger than the screen, the fight leaves the player's view: a wave walks in at a
    /// door nobody is looking at, a belt jams three screens away, and the Core is chewed on out of sight.
    /// This is the answer to "where?", in the one form that does not need a second map: everything out of
    /// view is pointed at from the edge of the view the player already has.
    ///
    /// It is arithmetic on a rectangle, not a renderer. <see cref="Viewport"/> is the part of the screen no
    /// HUD panel covers, <see cref="Collect"/> folds the world's snapshots outside it onto that rectangle's
    /// edge, and the result's two readers are <see cref="ThreatRadarView"/> (which draws the arrows) and
    /// <see cref="HudView"/> (which says the same numbers). One rule, stated once: what counts as out of
    /// view, what makes one arrow rather than six, and which arrow is urgent cannot disagree between the
    /// picture and the sentence, because both ask this class.
    ///
    /// Three subjects are worth locating, and no fourth: a <see cref="Subject.Threat"/> (enemies out of
    /// view, because the wave decides the run), a <see cref="Subject.Jam"/> (a blocked belt segment, whose
    /// fix is a right-click on a tile the player cannot see), and the <see cref="Subject.Core"/> (because
    /// everything else is only worth getting back to).
    ///
    /// Pure and camera-free on purpose (<see cref="HudLayout"/> and <see cref="PanLimits"/> are the same
    /// kind of module): the geometry is pinned from EditMode, where a viewport is four numbers and a wave
    /// is a list of snapshots, rather than by a scene and a resolution.
    /// </summary>
    public static class ThreatRadar
    {
        /// <summary>
        /// The screen the camera is showing, in world units, and the HUD panels drawn over it.
        ///
        /// The <b>ring</b> - the screen with the markers' own margin off every side - is where an arrow is
        /// put. A <b>panel</b> is a box the map can be neither seen nor clicked through, and it decides
        /// whether something is out of view at all: a walker under the status card is as unfindable as one
        /// off the display, while the free ground below that card needs no arrow.
        ///
        /// So this reads <see cref="HudLayout.Panels"/> and not <see cref="HudLayout.Insets"/>: the insets
        /// are one number per side, right for a camera bound but wrong for a question about a tile - taken
        /// as a rectangle they would call most of the screen out of view.
        /// </summary>
        public readonly struct Viewport
        {
            /// <summary>How many times an arrow steps back along its own sight line to get clear of a
            /// panel before it is accepted where it stands. Two is the ordinary case; the rest is room for a
            /// layout that grows.</summary>
            private const int MaxClearanceSteps = 6;

            /// <summary>Where the camera is looking.</summary>
            public readonly Vector2 Centre;

            /// <summary>The screen's own edges.</summary>
            public readonly float Left, Right, Bottom, Top;

            /// <summary>The ring: the screen with <see cref="Margin"/> off every side, never past the centre
            /// it is measured from. A subject outside it is out of view, and it is the rectangle an arrow is
            /// folded onto.</summary>
            public readonly float RingLeft, RingRight, RingBottom, RingTop;

            /// <summary>How far a marker's body keeps from the screen's edge and from any panel's.</summary>
            public readonly float Margin;

            private readonly Rect[] _panels;
            private readonly int _count;

            public Viewport(Vector2 centre, Vector2 halfScreen, float margin, Rect[] panels, int count)
            {
                Centre = centre;
                Left = centre.x - halfScreen.x;
                Right = centre.x + halfScreen.x;
                Bottom = centre.y - halfScreen.y;
                Top = centre.y + halfScreen.y;
                Margin = margin;

                // A window smaller than its margin collapses the ring onto the middle rather than turning it
                // inside out - a left edge right of its own right edge would read as "nothing is ever visible".
                RingLeft = Mathf.Min(Left + margin, centre.x);
                RingRight = Mathf.Max(Right - margin, centre.x);
                RingBottom = Mathf.Min(Bottom + margin, centre.y);
                RingTop = Mathf.Max(Top - margin, centre.y);

                _panels = panels;
                _count = panels != null ? Mathf.Min(count, panels.Length) : 0;
            }

            /// <summary>Is this world point somewhere the player can both see and click?</summary>
            public bool IsOpen(Vector2 p)
                => p.x >= RingLeft && p.x <= RingRight && p.y >= RingBottom && p.y <= RingTop
                   && !Covers(p);

            /// <summary>How far the point is from what the player is looking at, in tiles: the number the
            /// readout prints and the arrow fades by, so "18 tiles north-east" is the same 18 in both.</summary>
            public float Distance(Vector2 p) => Vector2.Distance(p, Centre);

            /// <summary>Is a HUD panel drawn over this point?</summary>
            public bool Covers(Vector2 p)
            {
                for (int i = 0; i < _count; i++)
                {
                    Rect r = _panels[i];
                    if (p.x >= r.xMin && p.x <= r.xMax && p.y >= r.yMin && p.y <= r.yMax) return true;
                }

                return false;
            }

            /// <summary>Where to put the arrow that points at a subject out of view: on the ring, on the line
            /// from the camera through the subject, backed up until it is clear of any card it would sit
            /// under. The sight line is what the arrow <em>means</em>, so it slides in along it and never
            /// sideways off it.
            ///
            /// A subject exactly on the centre (<see cref="IsOpen"/> has refused it, but arithmetic does not
            /// know that) points east rather than dividing by zero.</summary>
            public Vector2 EdgeTowards(Vector2 p, out Vector2 direction)
            {
                Vector2 d = p - Centre;
                if (d.sqrMagnitude < 1e-8f) d = Vector2.right;
                direction = d.normalized;

                float u = RayToRing(direction);

                for (int step = 0; step < MaxClearanceSteps; step++)
                {
                    float shift = ShiftIntoClear(Centre + direction * u, direction);
                    if (shift <= 0f) break;
                    u = Mathf.Max(0f, u - shift - Margin);
                }

                return Centre + direction * u;
            }

            /// <summary>The rectangle the warning frame is drawn on: the ring, with any side pushed in past
            /// the panel covering the middle of that side - a frame the build bar eats along its whole length
            /// reads as a broken four rather than a warning.</summary>
            public Rect Free
            {
                get
                {
                    float left = RingLeft, right = RingRight, bottom = RingBottom, top = RingTop;

                    for (int i = 0; i < _count; i++)
                    {
                        Rect p = _panels[i];

                        if (p.xMin <= Centre.x && Centre.x <= p.xMax)
                        {
                            if (p.yMin <= bottom && bottom <= p.yMax) bottom = Mathf.Max(bottom, p.yMax + Margin);
                            if (p.yMin <= top && top <= p.yMax) top = Mathf.Min(top, p.yMin - Margin);
                        }

                        if (p.yMin <= Centre.y && Centre.y <= p.yMax)
                        {
                            if (p.xMin <= left && left <= p.xMax) left = Mathf.Max(left, p.xMax + Margin);
                            if (p.xMin <= right && right <= p.xMax) right = Mathf.Min(right, p.xMin - Margin);
                        }
                    }

                    return Rect.MinMaxRect(Mathf.Min(left, right), Mathf.Min(bottom, top),
                        Mathf.Max(left, right), Mathf.Max(bottom, top));
                }
            }

            /// <summary>The ring, along a unit ray out of the camera's centre.</summary>
            private float RayToRing(Vector2 direction)
            {
                float u = float.MaxValue;
                if (direction.x > 1e-6f) u = Mathf.Min(u, (RingRight - Centre.x) / direction.x);
                else if (direction.x < -1e-6f) u = Mathf.Min(u, (RingLeft - Centre.x) / direction.x);
                if (direction.y > 1e-6f) u = Mathf.Min(u, (RingTop - Centre.y) / direction.y);
                else if (direction.y < -1e-6f) u = Mathf.Min(u, (RingBottom - Centre.y) / direction.y);

                return Mathf.Max(0f, u);
            }

            /// <summary>How far a point standing under a panel is from that panel's near edge, measured back
            /// along the sight line - or 0 when no panel covers it.</summary>
            private float ShiftIntoClear(Vector2 at, Vector2 direction)
            {
                for (int i = 0; i < _count; i++)
                {
                    Rect r = _panels[i];
                    if (at.x < r.xMin || at.x > r.xMax || at.y < r.yMin || at.y > r.yMax) continue;

                    float x = Mathf.Abs(direction.x) < 1e-6f ? float.MaxValue
                        : (direction.x > 0f ? at.x - r.xMin : r.xMax - at.x) / Mathf.Abs(direction.x);
                    float y = Mathf.Abs(direction.y) < 1e-6f ? float.MaxValue
                        : (direction.y > 0f ? at.y - r.yMin : r.yMax - at.y) / Mathf.Abs(direction.y);

                    return Mathf.Min(x, y);
                }

                return 0f;
            }
        }

        /// <summary>One reader's handle on the window. It owns the two scratch arrays the panel boxes need -
        /// screen pixels as <see cref="HudLayout"/> lays them out, and the same boxes in world units - so
        /// the two readers can ask their question every frame without allocating.</summary>
        public sealed class ScreenReader
        {
            private readonly Rect[] _pixels = new Rect[HudLayout.PanelCount];
            private readonly Rect[] _world = new Rect[HudLayout.PanelCount];

            /// <summary>Measure the window <paramref name="camera"/> is showing, with the HUD's panels in it
            /// and the arrows' margin off its edge. <paramref name="tutorialUp"/> and
            /// <paramref name="infoGrown"/> are the two panels that are not always there - the tutorial card
            /// and the sorter picker's band on the info card - so they are the two the caller has to say:
            /// neither has a default, because a caller that forgot one would silently draw arrows under a
            /// card that is on screen.</summary>
            public Viewport Measure(Camera camera, bool tutorialUp, float marginPixels, bool infoGrown)
            {
                if (camera == null) return default;

                float halfHeight = camera.orthographicSize;
                var halfScreen = new Vector2(halfHeight * camera.aspect, halfHeight);
                Vector3 position = camera.transform.position;
                var centre = new Vector2(position.x, position.y);

                // One screen pixel in world units - the same number every view scales its screen-space
                // constants with (see MeshView.WorldPerPixel).
                float pixels = Screen.height > 0 ? Screen.height : 1f;
                float pixel = 2f * halfHeight / pixels;

                int count = HudLayout.Panels(Screen.width, Screen.height,
                    HudLayout.Scale(Screen.height), tutorialUp, _pixels, infoGrown);

                // Screen pixels grow down and world units up, so a panel's top edge in pixels is its bottom
                // edge in the map - the same flip the cursor's arithmetic makes.
                float left = centre.x - halfScreen.x, top = centre.y + halfScreen.y;
                for (int i = 0; i < count; i++)
                {
                    Rect r = _pixels[i];
                    _world[i] = Rect.MinMaxRect(
                        left + r.xMin * pixel, top - r.yMax * pixel,
                        left + r.xMax * pixel, top - r.yMin * pixel);
                }

                return new Viewport(centre, halfScreen, marginPixels * pixel, _world, count);
            }
        }

        /// <summary>What one arrow stands for.</summary>
        public enum Subject : byte
        {
            /// <summary>Enemies out of view. The count says how many, the colour says what they are.</summary>
            Threat = 0,

            /// <summary>A jammed belt segment out of view: the arrow marks where the right-click has to go.</summary>
            Jam = 1,

            /// <summary>The Core, off the screen. Navigation rather than warning - nothing is lost by
            /// looking away from it, only by letting something reach it unseen.</summary>
            Core = 2,
        }

        /// <summary>One arrow: a place on the edge of the open screen - the ring, stepped in clear of any
        /// card it would otherwise sit under - and what it is standing for.</summary>
        public readonly struct Marker
        {
            public readonly Subject What;

            /// <summary>World position: on the ring, on the line from the camera to the subject.</summary>
            public readonly Vector2 At;

            /// <summary>Unit vector from the camera out toward everything this arrow stands for.</summary>
            public readonly Vector2 Direction;

            /// <summary>How many of the subject it stands for. 1 for the Core, which is one thing.</summary>
            public readonly int Count;

            /// <summary>Tiles from the camera to the <em>nearest</em> thing it stands for: what the arrow
            /// fades with, and what the card prints.</summary>
            public readonly float Distance;

            /// <summary>Is one of them hitting something right now? The urgent arrow is the one the player
            /// has to look at, so it is the one drawn loud.</summary>
            public readonly bool Urgent;

            /// <summary>Which enemy the arrow wears: the nearest one it stands for. A cluster of two kinds is
            /// drawn in the nearer one's colour and counts both.</summary>
            public readonly EnemyKind Kind;

            public Marker(Subject what, Vector2 at, Vector2 direction, int count, float distance,
                bool urgent, EnemyKind kind)
            {
                What = what;
                At = at;
                Direction = direction;
                Count = count;
                Distance = distance;
                Urgent = urgent;
                Kind = kind;
            }

            /// <summary>This arrow plus one more subject at the same place. The cluster keeps the nearer
            /// thing's distance and colour and gains the other's count and urgency, because an arrow that
            /// stops shouting when a second enemy joins hides the fact it exists for.</summary>
            public Marker Fold(in Marker other)
            {
                bool nearer = other.Distance <= Distance;
                return new Marker(What, At, Direction, Count + other.Count,
                    nearer ? other.Distance : Distance,
                    Urgent || other.Urgent,
                    nearer ? other.Kind : Kind);
            }
        }

        /// <summary>What the walk found, in the few numbers a sentence needs. The arrows are the same
        /// answer drawn; this is the same answer read.</summary>
        public readonly struct Summary
        {
            /// <summary>Enemies out of view, whether or not they got an arrow of their own.</summary>
            public readonly int Threats;

            /// <summary>Jammed segments out of view.</summary>
            public readonly int Jams;

            /// <summary>Tiles to the nearest enemy out of view, or <see cref="float.MaxValue"/> when there
            /// is none to be near.</summary>
            public readonly float Nearest;

            /// <summary>Is something out of view hitting the Core or a building right now?</summary>
            public readonly bool Urgent;

            /// <summary>Is the Core off the screen?</summary>
            public readonly bool CoreIsOut;

            public bool HasThreats => Threats > 0;
            public bool HasJams => Jams > 0;

            /// <summary>Anything worth an arrow at all.</summary>
            public bool HasAnything => HasThreats || HasJams || CoreIsOut;

            public Summary(int threats, int jams, float nearest, bool urgent, bool coreIsOut)
            {
                Threats = threats;
                Jams = jams;
                Nearest = nearest;
                Urgent = urgent;
                CoreIsOut = coreIsOut;
            }
        }

        /// <summary>An empty summary: nothing out of view, nothing to point at.</summary>
        public static readonly Summary None = new Summary(0, 0, float.MaxValue, false, false);

        /// <summary>
        /// Walk the world's own snapshots and put everything outside <paramref name="view"/>'s open area on
        /// its edge.
        ///
        /// Enemies are clustered by where their arrow would land, not by where they are: a wave of sixteen
        /// walking in from the north-west is one arrow with sixteen behind it, because six arrows on one
        /// edge is noise that buries the useful part - that the north-west is where the wave is.
        /// <paramref name="mergeDistance"/> is that clustering, in world units, derived by the caller from
        /// pixels so the arrows stay the same density at every zoom.
        ///
        /// Markers are folded in the order the snapshots arrive, the field's stable slot order, so the same
        /// state always draws the same arrows. Nothing is allocated: the markers go into a caller-owned
        /// array, and the lists are the ones the views already reuse.
        /// </summary>
        /// <param name="jams">Jammed belt cells (<see cref="BeltField.GetJammed"/>), or empty.</param>
        /// <param name="core">The Core's centre, in world units.</param>
        /// <param name="into">Where the arrows go; at most <see cref="Array.Length"/> of them.</param>
        /// <param name="summary">What was found, including what no arrow had room for.</param>
        /// <returns>How many markers were written.</returns>
        public static int Collect(in Viewport view, IReadOnlyList<EnemySnapshot> enemies,
            IReadOnlyList<Int2> jams, Vector2 core, float mergeDistance, Marker[] into, out Summary summary)
        {
            int written = 0;
            int threats = 0, jamsOut = 0;
            float nearest = float.MaxValue;
            bool urgent = false;

            float mergeSq = mergeDistance * mergeDistance;

            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemySnapshot e = enemies[i];
                    var at = new Vector2(e.Position.X, e.Position.Y);
                    if (view.IsOpen(at)) continue;

                    threats++;
                    float distance = view.Distance(at);
                    if (distance < nearest) nearest = distance;
                    if (e.Attacking) urgent = true;

                    var marker = new Marker(Subject.Threat, view.EdgeTowards(at, out Vector2 direction),
                        direction, 1, distance, e.Attacking, e.Kind);
                    written = FoldOrAdd(into, written, in marker, mergeSq);
                }
            }

            if (jams != null)
            {
                for (int i = 0; i < jams.Count; i++)
                {
                    Int2 cell = jams[i];
                    var at = new Vector2(cell.X + 0.5f, cell.Y + 0.5f);
                    if (view.IsOpen(at)) continue;

                    jamsOut++;
                    var marker = new Marker(Subject.Jam, view.EdgeTowards(at, out Vector2 direction),
                        direction, 1, view.Distance(at), false, default);
                    written = FoldOrAdd(into, written, in marker, mergeSq);
                }
            }

            bool coreIsOut = !view.IsOpen(core);
            if (coreIsOut)
            {
                var marker = new Marker(Subject.Core, view.EdgeTowards(core, out Vector2 direction),
                    direction, 1, view.Distance(core), false, default);
                written = FoldOrAdd(into, written, in marker, mergeSq);
            }

            summary = new Summary(threats, jamsOut, nearest, urgent, coreIsOut);
            return written;
        }

        /// <summary>Add a marker, or grow the one already standing where it would. A full array does not
        /// drop the subject - it merges into the nearest marker of the same kind, so the count stays true
        /// while the arrows stay a handful; silently losing enemies would be the same lie as a card saying
        /// "nothing out of view" while six walkers march.</summary>
        private static int FoldOrAdd(Marker[] into, int written, in Marker marker, float mergeSq)
        {
            int nearestSlot = -1;
            float nearestSq = float.MaxValue;

            for (int i = 0; i < written; i++)
            {
                if (into[i].What != marker.What) continue;

                float sq = (into[i].At - marker.At).sqrMagnitude;
                if (sq <= mergeSq)
                {
                    into[i] = into[i].Fold(in marker);
                    return written;
                }

                if (sq >= nearestSq) continue;
                nearestSq = sq;
                nearestSlot = i;
            }

            if (written < into.Length)
            {
                into[written] = marker;
                return written + 1;
            }

            // Full: fold into the closest marker of its kind. nearestSlot is only -1 when the array holds
            // no marker of this kind at all, which a cap of one kind cannot reach while this loop ran.
            if (nearestSlot >= 0) into[nearestSlot] = into[nearestSlot].Fold(in marker);
            return written;
        }

        /// <summary>Was the Core hit inside the locator's window, what did it cost, and how many ticks ago?
        /// The event stream's own window question (<see cref="SimEventBuffer.TryLastWithin"/>), which lets
        /// the danger frame, the health bar's flash and the number beside it say "the Core is being hurt"
        /// off one beat and fade off one age, rather than each watching for an edge. Paused, no new events
        /// arrive and the window closes by itself: nothing left pulsing after a restart.</summary>
        public static bool TryCoreHit(SimWorld world, int windowTicks, out float damage, out int age)
        {
            damage = 0f;
            age = -1;
            if (world == null) return false;
            if (!world.Events.TryLastWithin(SimEventKind.CoreDamaged,
                    world.TickCount - windowTicks, out SimEvent hit)) return false;

            damage = hit.Amount;
            age = world.TickCount - hit.Tick;
            return true;
        }

        /// <summary>How many ticks ago the Core was last hit, or -1 when the last hit is older than
        /// <paramref name="windowTicks"/>. The same window question in the one number a fade works from.</summary>
        public static int CoreHitAge(SimWorld world, int windowTicks)
            => TryCoreHit(world, windowTicks, out _, out int age) ? age : -1;

        /// <summary>Is the Core being hit inside the locator's window? The frame's and the card's one answer,
        /// in the <see cref="CoreHitAge"/> terms both readers work in.</summary>
        public static bool CoreIsBeingHit(SimWorld world, int windowTicks) => CoreHitAge(world, windowTicks) >= 0;

        /// <summary>The Core's centre in world units, for a reader that wants the point rather than the cell.</summary>
        public static Vector2 CoreCentre(SimWorld world)
        {
            Vec2 c = world.Core.Center;
            return new Vector2(c.X, c.Y);
        }
    }
}
