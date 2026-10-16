using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>Draws every belt cell as one mesh of connected strips: each cell lays its centre hub, one lane
    /// toward its exit edge, and one lane from every side a feeding neighbour sits on - so a straight run reads
    /// as one continuous belt, a corner as a turn, two lines meeting as one shape. A lane that reaches a cell
    /// which eats (belt, machine, Core) docks into it; a lane to nowhere stops short, so a dangling end is
    /// visible.
    ///
    /// The bed changes only when a belt is laid, re-pointed or jammed, so this view rebuilds when the belt
    /// field's revision moves - its rebuild reason, declared in <see cref="Observe"/>. That is also why belts
    /// carry no outline: an outline is baked at a fixed pixel width, so it would be wrong at every zoom but the
    /// one it was built at. The gap between parallel lines separates the runs, and the chevron is what must be
    /// read, so nothing is lost.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class BeltRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly List<BeltSnapshot> _belts = new List<BeltSnapshot>();
        private readonly List<int> _feeds = new List<int>();
        private readonly List<Vector2> _hub = new List<Vector2>();
        private int _seenRevision = -1;

        /// <summary>How far along a cell the chevrons have slid, 0..1. One phase for every belt, so the
        /// chevron leaving one cell's exit edge is the one entering the next cell's entry edge and a run reads
        /// as one moving stream rather than a field of arrows ticking in place.</summary>
        private float _phase;
        private int _phaseFrame = -1;

        /// <summary>A new world starts its belt revision at zero, which this view may already have drawn - so the
        /// cache has to be dropped or the new map's belts would never appear.</summary>
        protected override void OnWorldRebound() => _seenRevision = -1;

        protected override Palette.Layer Layer => Colors.BeltLayer;

        /// <summary>Items moving deliberately do not bump the revision, so a rebuild means a cell was actually laid,
        /// re-pointed or jammed - not that the line is busy. The one exception is the chevrons' slide: while it is
        /// on and the run is live, the bed is redrawn every frame so the arrows move with the items.</summary>
        protected override void Observe(in ViewFrame frame)
        {
            int revision = World.Belts.Revision;
            if (revision != _seenRevision)
            {
                _seenRevision = revision;
                MarkDirty();
            }

            float scroll = Colors.Belts.ChevronScroll;
            if (scroll <= 0f || World.Paused || World.Status != GameStatus.Playing) return;
            if (Time.frameCount == _phaseFrame) return;   // the Editor preview may draw a frame twice

            _phaseFrame = Time.frameCount;
            _phase = Mathf.Repeat(_phase + Time.deltaTime * scroll * World.Config.BeltSpeed, 1f);
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Belts.GetBelts(_belts);
            Palette.BeltLook look = Colors.Belts;
            float w = look.HalfWidth;
            float rail = Mathf.Max(0f, look.RailWidth);

            // Which sides feed each cell, asked once per rebuild: the rail, bed and chevron passes all need it,
            // and a machine's answer (its port masks) is not free.
            _feeds.Clear();
            for (int i = 0; i < _belts.Count; i++) _feeds.Add(FeedMask(_belts[i]));

            // Two passes over the same geometry: every rail first, then every bed on top. A bed laid over its own
            // rail only would leave a neighbour's rail drawn across the joint where two cells meet.
            if (rail > 0f)
                for (int i = 0; i < _belts.Count; i++) AppendBed(_belts[i], _feeds[i], w + rail, rail, Colors.BeltRail);

            for (int i = 0; i < _belts.Count; i++)
            {
                BeltSnapshot belt = _belts[i];

                // Three beds: a belt, one a machine refused something on, and one whose item has nowhere to
                // go at all. The last two are different problems - clear the offending item, or finish the
                // line - so they are different colours, and the short exit lane says the second one in
                // geometry as well.
                Color bed = belt.Jammed ? Colors.Jam : belt.Blocked ? Colors.BeltBlocked : Colors.Belt;
                AppendBed(belt, _feeds[i], w, 0f, bed);
                AppendChevron(belt, _feeds[i], w);
            }
        }

        /// <summary>
        /// One cell's bed at half width <paramref name="halfWidth"/>: the hub every lane docks into, one lane toward
        /// the exit edge, and one lane from every side something feeds in from. The rail pass calls this a rail's
        /// width wider, with <paramref name="cap"/> set to that width so a lane that stops short (the dangling end
        /// of a run) is closed off by the rail rather than left open.
        ///
        /// A pure turn - fed from exactly one side, at right angles to the exit - gets a round elbow: the hub's
        /// outer corner is a quarter circle, so the L reads as one curved piece of track (the item turns at the
        /// centre, <see cref="BeltField.WorldPositionOf"/>, and now the bed says so).
        /// </summary>
        private void AppendBed(BeltSnapshot belt, int feeds, float halfWidth, float cap, Color color)
        {
            var centre = CellCentre(belt.Cell);
            Vector2 forward = ToVector(belt.Direction);
            Dir entry = PathEntry(belt, feeds, out bool turning);

            // The hub; also the corner joint, so an L's two lanes meet under it and a turn never shows a seam.
            if (turning) AppendElbow(centre, ToVector(entry.Opposite()), forward, halfWidth, color);
            else AppendSquareHub(centre, halfWidth, color);

            // The exit lane: full length when the next cell takes the item, short when the run just ends - a
            // dangling half-lane is the "this goes nowhere" readout.
            bool exitFull = TakesFrom(belt.Cell + belt.Direction.Offset());
            float exitLength = exitFull ? 0.5f : 0.32f + cap;
            AppendLane(centre, centre + forward * exitLength, halfWidth, color);

            // A lane from every side something feeds in from: belts pointing at me, and machines whose facing pushes
            // onto me (a drill or an operator pushing out, a splitter whose output port I am). A pipe lands from
            // above instead - its own tube, drawn by the machine view, is that hand-off's visual.
            for (int s = 0; s < Sides.Length; s++)
            {
                if (!Fed(feeds, Sides[s])) continue;
                Vector2 side = ToVector(Sides[s]);
                AppendLane(centre + side * 0.5f, centre, halfWidth, color);
            }
        }

        /// <summary>Which sides of <paramref name="belt"/> deliver into it, one bit per <see cref="Dir"/>.</summary>
        private int FeedMask(BeltSnapshot belt)
        {
            int mask = 0;
            for (int s = 0; s < Sides.Length; s++)
                if (DeliversInto(belt, Sides[s])) mask |= 1 << (int)Sides[s];
            return mask;
        }

        private static bool Fed(int feeds, Dir side) => (feeds & (1 << (int)side)) != 0;

        private static int CountFeeds(int feeds)
        {
            int count = 0;
            for (int bits = feeds; bits != 0; bits &= bits - 1) count++;
            return count;
        }

        /// <summary>
        /// The way an item usually comes onto this cell, for drawing: straight through when it is fed from behind
        /// (or not at all), otherwise from its one feeding side. <paramref name="turning"/> is a pure turn - one
        /// feed, at right angles to the exit - which is the only shape that gets a round elbow; a merge keeps the
        /// square hub, because more than one lane docks into it.
        /// </summary>
        private static Dir PathEntry(BeltSnapshot belt, int feeds, out bool turning)
        {
            turning = false;
            int fedCount = CountFeeds(feeds);
            if (fedCount == 0 || Fed(feeds, belt.Direction.Opposite())) return belt.Direction;

            for (int s = 0; s < Sides.Length; s++)
            {
                if (!Fed(feeds, Sides[s])) continue;
                // A feed from the side the belt points at is a head-to-head pair - a visible mistake, drawn
                // as it is rather than bent into a U.
                turning = fedCount == 1 && Sides[s] != belt.Direction;
                return Sides[s].Opposite();
            }
            return belt.Direction;
        }

        private void AppendSquareHub(Vector2 centre, float halfWidth, Color color)
            => AppendQuad(
                new Vector2(centre.x - halfWidth, centre.y - halfWidth), new Vector2(centre.x + halfWidth, centre.y - halfWidth),
                new Vector2(centre.x + halfWidth, centre.y + halfWidth), new Vector2(centre.x - halfWidth, centre.y + halfWidth),
                color);

        /// <summary>
        /// The hub of a turn: the square hub with its outer corner - the one away from both lanes - replaced by a
        /// quarter circle about the centre. <paramref name="toFeed"/> points at the side the item comes in from and
        /// <paramref name="forward"/> at the exit, so the outer corner is the one at <c>-toFeed - forward</c>.
        /// </summary>
        private void AppendElbow(Vector2 centre, Vector2 toFeed, Vector2 forward, float r, Color color)
        {
            int segments = Mathf.Max(1, Colors.Belts.CornerSegments);

            _hub.Clear();
            _hub.Add(centre + toFeed * r + forward * r);    // the inner corner, between the two lanes
            _hub.Add(centre - toFeed * r + forward * r);
            for (int k = 0; k <= segments; k++)
            {
                float a = 0.5f * Mathf.PI * k / segments;
                _hub.Add(centre - toFeed * (r * Mathf.Cos(a)) - forward * (r * Mathf.Sin(a)));
            }
            _hub.Add(centre + toFeed * r - forward * r);

            if (SignedArea(_hub) < 0f) _hub.Reverse();     // the mesh wants counter-clockwise for every turn
            AppendPolygon(_hub, Vector2.zero, color, color, 0f);
        }

        private static float SignedArea(List<Vector2> points)
        {
            float area = 0f;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
                area += points[j].x * points[i].y - points[i].x * points[j].y;
            return area * 0.5f;
        }

        private static Vector2 ToVector(Dir direction)
        {
            Vec2 v = direction.ToVec();
            return new Vector2(v.X, v.Y);
        }

        /// <summary>Would an item on <paramref name="cell"/> be taken? Belts dock into each other whatever
        /// way the neighbour faces (a head-to-head pair is a visible mistake, not a gap), and into anything
        /// that takes: a machine with an intake or the Core. A wall is solid and takes nothing, which is why
        /// this asks <see cref="TileKindExtensions.Takes"/> rather than <c>Eats</c> - the same rule the belt
        /// layer's dead-end marker is derived from, so the short dangling lane and the amber bed say one
        /// thing rather than two.</summary>
        private bool TakesFrom(Int2 cell) => World.TileGrid.Get(cell).Takes();

        /// <summary>Does the neighbour on <paramref name="side"/> push items onto this belt?</summary>
        private bool DeliversInto(BeltSnapshot belt, Dir side)
        {
            Int2 neighbour = belt.Cell + side.Offset();

            if (World.Belts.TryGet(neighbour, out BeltState other) && other.Direction == side.Opposite())
                return true;

            // A machine pushes onto me when the side facing me is one of its output ports - the same mask the
            // simulation pushes through, not a second reading. Asked of the tile this belt docks with rather
            // than of the machine's snapshot: a cutter stands on two tiles and each of them has its own
            // fronts, so the lane beside the blade docks with a port the blade tile's own mask knows nothing
            // about.
            World.Machines.PortMasksAt(neighbour, out _, out DirMask outMask);
            return outMask.Has(side.Opposite());
        }

        /// <summary>
        /// The direction chevron. Still at the cell centre when the slide is off or the segment is jammed (a jam is
        /// a stop, and its chevron is drawn in the outline colour so it reads as one); otherwise it rides the same
        /// path an item does - in along the entry, out along the exit, turning at the centre - at the shared
        /// <see cref="_phase"/>. A chevron that would hang off the end of the bed (behind a run's first cell, past
        /// a dangling end) is simply not drawn that frame, so arrows enter and leave a run cleanly.
        /// </summary>
        private void AppendChevron(BeltSnapshot belt, int feeds, float halfWidth)
        {
            var centre = CellCentre(belt.Cell);
            Palette.BeltLook look = Colors.Belts;

            if (belt.Jammed || look.ChevronScroll <= 0f)
            {
                AppendChevron(centre, belt.Direction, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength,
                    belt.Jammed ? Colors.Outline : Colors.BeltArrow);
                return;
            }

            Dir entry = PathEntry(belt, feeds, out _);
            bool fedFromEntry = feeds != 0;
            bool exitFull = TakesFrom(belt.Cell + belt.Direction.Offset());

            float p = _phase;
            float bedStart = fedFromEntry ? 0f : 0.5f - halfWidth;
            float bedEnd = exitFull ? 1f : 0.5f + 0.32f;
            if (p - look.ChevronBack < bedStart || p + look.ChevronLength > bedEnd) return;

            Dir heading = p < 0.5f ? entry : belt.Direction;
            Vector2 at = centre + ToVector(heading) * (p - 0.5f);
            AppendChevron(at, heading, look.ChevronBack, look.ChevronHalfWidth, look.ChevronLength, Colors.BeltArrow);
        }
    }
}
