using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>The operator machines a map has unlocked, as a set. A map's progression <em>is</em>
    /// this set (§7): a map hands over exactly the operators it lists, so teaching cutting and handing
    /// over the cutter are one statement rather than two. It is also the guardrail the wave generator
    /// draws its demand shapes from, so a map can never brief a shape its own operators cannot build.</summary>
    [Flags]
    public enum Operators : byte
    {
        None = 0,

        /// <summary>Splits one item along a line, either way up.</summary>
        Cutter = 1,

        /// <summary>Turns an item a quarter clockwise. It is not what lets a half exist - a cutter is aimed,
        /// so both cut lines are open to it from the start, and the top half and the left half of one mineral
        /// are equally available on a map with no rotator at all. What the rotator does is <em>repair</em>: a
        /// piece already on a belt keeps the orientation its cut gave it, and only a turn can bring a left
        /// half back round to a top half.</summary>
        Rotator = 2,

        /// <summary>Unions two disjoint items. Without it a mixed shape is impossible.</summary>
        Stacker = 4,

        All = Cutter | Rotator | Stacker,
    }

    /// <summary>
    /// Every item a map can make right now: the closure of the map's mined minerals under the operators
    /// that are unlocked. The wave generator draws weakness shapes from this set and nowhere else, which
    /// is what gives the unlock ladder teeth - brief a mixed round on a map with no stacker and the
    /// wave is unwinnable through no fault of the player.
    ///
    /// The closure is wider than "one operator, one new shape" suggests, and the cutter is the reason: it
    /// offers <em>both</em> of its cut lines from the moment it is unlocked, so a map that has one is
    /// already good for every half and quarter of every mineral it mines, in every orientation a cut can
    /// produce. The rotator reaches none of them that the cutter did not (see
    /// <c>TheRotator_AddsNoItemsByItself</c>); what it adds is a way back from an orientation a cut chose.
    ///
    /// The enumeration is a breadth-first walk over at most 80 nodes, done once per pool: minerals are
    /// the roots, and an operator edge costs one step. The step count is the shape's <b>depth</b>, which
    /// is the design's progression currency - "the deeper the demanded shape, the tougher and slower that
    /// group" - so the generator weights by a number that comes out of the algebra rather than a hand-kept
    /// tier table.
    /// </summary>
    public sealed class ShapePool
    {
        private const int Universe = 1 << (Shape.QuadrantCount * 2);   // every 2-bit-per-quadrant code

        private readonly bool[] _known = new bool[Universe];
        private readonly int[] _depth = new int[Universe];

        private readonly List<Shape> _shapes = new List<Shape>();
        private readonly List<Shape>[] _byDepth;

        /// <summary>The same list, wrapped once so it cannot be cast back to the mutable one it came from:
        /// <c>IReadOnlyList&lt;T&gt;</c> is an interface a <c>List&lt;T&gt;</c> satisfies, so handing the list
        /// out directly would make the immutability these members promise a convention rather than a
        /// guarantee.</summary>
        private readonly IReadOnlyList<Shape> _shapesView;
        private readonly IReadOnlyList<Shape>[] _tiers;

        /// <summary>Every non-empty item, ascending by code, whether or not this map can make it. The
        /// inspector, the sorter's picker and the algebra's own tests work off this table.</summary>
        public static IReadOnlyList<Shape> All { get; } = EnumerateAll();

        /// <summary>Every item this map can make, in the order the walk discovered them: the mined minerals
        /// first, then whatever the unlocked operators reach from them, breadth-first by depth. Not sorted by
        /// code - a full circle's code is higher than a quarter's although the circle is mined and the
        /// quarter is cut - so a consumer that wants an order should sort it rather than assume this one.</summary>
        public IReadOnlyList<Shape> Shapes => _shapesView;

        /// <summary>How many operator steps the deepest producible item is from a mined mineral. 0 when
        /// the map can only mine.</summary>
        public int MaxDepth { get; }

        /// <summary>The mined minerals themselves, as full shapes, in the order the map lists them.
        /// Depth 0: what a drill puts on a belt with no operator in the way.</summary>
        public IReadOnlyList<Shape> Minerals { get; }

        public Operators Unlocked { get; }

        public ShapePool(Quad[] minerals, Operators unlocked)
        {
            Unlocked = unlocked & Operators.All;
            Minerals = Roots(minerals);

            Grow();

            int deepest = 0;
            for (int i = 0; i < _shapes.Count; i++)
            {
                int depth = _depth[_shapes[i].Code];
                if (depth > deepest) deepest = depth;
            }

            MaxDepth = deepest;

            // One tier per depth, filled from the depths the walk measured, so a map whose algebra reaches
            // deeper than any guess is still tiered correctly rather than losing its deepest shapes off the
            // end of a fixed array. Wrapped on the way out, so a caller cannot reach the lists themselves.
            _byDepth = new List<Shape>[MaxDepth + 1];
            for (int d = 0; d < _byDepth.Length; d++) _byDepth[d] = new List<Shape>();
            for (int i = 0; i < _shapes.Count; i++)
                _byDepth[_depth[_shapes[i].Code]].Add(_shapes[i]);

            _shapesView = _shapes.AsReadOnly();
            _tiers = new IReadOnlyList<Shape>[_byDepth.Length];
            for (int d = 0; d < _byDepth.Length; d++) _tiers[d] = _byDepth[d].AsReadOnly();
        }

        /// <summary>Can this map make that item? The generator's one question, asked of every candidate
        /// weakness.</summary>
        public bool CanProduce(Shape shape) => !shape.IsEmpty && _known[shape.Code];

        /// <summary>Operator steps from a mined mineral, or -1 for an item this map cannot make.</summary>
        public int Depth(Shape shape) => shape.IsEmpty || !_known[shape.Code] ? -1 : _depth[shape.Code];

        /// <summary>Every producible item at exactly this depth, ascending by code - the tiers the
        /// generator shifts through as waves advance. Empty for a depth past <see cref="MaxDepth"/>.</summary>
        public IReadOnlyList<Shape> AtDepth(int depth)
            => depth >= 0 && depth < _tiers.Length ? _tiers[depth] : Array.Empty<Shape>();

        /// <summary>
        /// The closure itself: a worklist starting at the mined minerals. Every item discovered is
        /// offered every unlocked operator - and, for the stacker, a union with <em>every</em> item found
        /// so far, so a mixed shape the player can actually build is never missed. The walk is
        /// breadth-first, so it guarantees that every reachable item is discovered - but its recorded
        /// depth is only the first derivation it happened to reach the item by, which
        /// <see cref="RelaxDepths"/> then replaces with the true minimum.
        /// </summary>
        private void Grow()
        {
            var queue = new Queue<Shape>();

            for (int i = 0; i < Minerals.Count; i++) Add(Minerals[i], 0, queue);

            while (queue.Count > 0)
            {
                Shape shape = queue.Dequeue();
                int depth = _depth[shape.Code];

                if (Has(Operators.Cutter))
                {
                    (Shape top, Shape bottom) = Shape.Cut(shape, vertical: false);
                    Add(top, depth + 1, queue);
                    Add(bottom, depth + 1, queue);

                    (Shape left, Shape right) = Shape.Cut(shape, vertical: true);
                    Add(left, depth + 1, queue);
                    Add(right, depth + 1, queue);
                }

                if (Has(Operators.Rotator)) Add(shape.RotatedCW(), depth + 1, queue);

                if (!Has(Operators.Stacker)) continue;

                // Against everything known so far, in both orders: the union is commutative but the
                // "which arrived first" of a worklist is not.
                for (int i = _shapes.Count - 1; i >= 0; i--)
                {
                    Shape other = _shapes[i];
                    if (shape.Overlaps(other)) continue;

                    Add(Shape.Stacked(shape, other), Math.Max(depth, _depth[other.Code]) + 1, queue);
                }
            }

            RelaxDepths();
        }

        /// <summary>
        /// Replace the walk's first-discovery depths with the true minimum operator count per item.
        ///
        /// First discovery is only the shallowest derivation when every edge costs one step, and a
        /// stack edge does not: it costs the deeper of its two sides <em>plus one</em>. So an item can
        /// be claimed the first time through a deeper pairing before its cheapest pairing is ever
        /// examined - a half and a quarter only pair when the quarter is dequeued, and by then two
        /// earlier-discovered quarters have usually stacked and moved on. On the shipped two-mineral
        /// ladder that misfiled fourteen of the eighty: L-shaped three-quadrant items recorded at four
        /// steps that take three, and every three-plus-one mixed round at five that takes four - which
        /// pushed the wave generator's depth band past the tier the player is actually building in.
        ///
        /// The cure is the obvious relaxation over the same edges the walk walked: a cut or a turn
        /// costs the source's depth plus one, a stack the deeper side's depth plus one, repeated until
        /// nothing moves. Depths only ever fall, so this terminates at the true minimum; eighty items
        /// make a handful of cheap passes.
        /// </summary>
        private void RelaxDepths()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;

                for (int i = 0; i < _shapes.Count; i++)
                {
                    Shape shape = _shapes[i];
                    int through = _depth[shape.Code] + 1;

                    var across = Shape.Cut(shape, false);
                    var along = Shape.Cut(shape, true);
                    Relax(across.a, through, ref changed);
                    Relax(across.b, through, ref changed);
                    Relax(along.a, through, ref changed);
                    Relax(along.b, through, ref changed);
                    Relax(shape.RotatedCW(), through, ref changed);

                    for (int j = 0; j < _shapes.Count; j++)
                    {
                        Shape other = _shapes[j];
                        if (shape.Overlaps(other)) continue;

                        Relax(Shape.Stacked(shape, other),
                            Math.Max(_depth[shape.Code], _depth[other.Code]) + 1, ref changed);
                    }
                }
            }
        }

        /// <summary>Lower one item's depth to <paramref name="candidate"/> when the candidate is lower
        /// and the item is one the pool holds. The flag is the fixpoint's "go round again".</summary>
        private void Relax(Shape shape, int candidate, ref bool changed)
        {
            if (shape.IsEmpty || !_known[shape.Code] || candidate >= _depth[shape.Code]) return;

            _depth[shape.Code] = candidate;
            changed = true;
        }

        /// <summary>Record an item if it is new, and hand it to the worklist. Nothing is a shape: the empty
        /// side of a cut is not an item. The depth tiers are filled after the walk (the deepest item is not
        /// known until it ends), so this only measures.</summary>
        private void Add(Shape shape, int depth, Queue<Shape> queue)
        {
            if (shape.IsEmpty || _known[shape.Code]) return;

            _known[shape.Code] = true;
            _depth[shape.Code] = depth;
            _shapes.Add(shape);
            queue.Enqueue(shape);
        }

        /// <summary>The map's minerals as full shapes, duplicates and holes dropped: a map that paints the
        /// same vein twice has one mineral, and a patch with no mineral is a map error rather than a
        /// source.</summary>
        private static IReadOnlyList<Shape> Roots(Quad[] minerals)
        {
            var roots = new List<Shape>();
            if (minerals == null) return roots;

            for (int i = 0; i < minerals.Length; i++)
            {
                if (minerals[i] == Quad.Empty) continue;

                Shape full = Shape.Full(minerals[i]);
                if (!roots.Contains(full)) roots.Add(full);
            }

            // Wrapped for the same reason <see cref="Shapes"/> is: what is handed out must not be cast back
            // to the list the pool keeps.
            return roots.AsReadOnly();
        }

        private bool Has(Operators op) => (Unlocked & op) != 0;

        /// <summary>Every code with two bits per quadrant and no invalid value in them: 3^4 items, the
        /// empty one dropped. The design's 80.</summary>
        private static IReadOnlyList<Shape> EnumerateAll()
        {
            var all = new List<Shape>();
            for (uint code = 1; code < Universe; code++)
            {
                var shape = new Shape(code);
                bool valid = true;
                for (int i = 0; i < Shape.QuadrantCount; i++)
                    if (shape.At(i) == (Quad)3) valid = false;

                if (valid) all.Add(shape);
            }

            return all;
        }
    }
}
