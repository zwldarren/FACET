using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>The operator machines a map has unlocked, as a set. The campaign's progression <em>is</em>
    /// this set (§7): map 1 teaches cutting and unlocks the cutter, map 2 the second mineral and the
    /// rotator, map 3 the stacker. It is also the guardrail the wave generator draws its demand shapes
    /// from, so a map can never brief a shape its own operators cannot build.</summary>
    [Flags]
    public enum Operators : byte
    {
        None = 0,

        /// <summary>Splits one item along a line, either way up.</summary>
        Cutter = 1,

        /// <summary>Turns an item a quarter clockwise. Without it a half-circle only ever exists as mined
        /// or as cut - there are no left or right pieces to be had.</summary>
        Rotator = 2,

        /// <summary>Unions two disjoint items. Without it a mixed shape is impossible.</summary>
        Stacker = 4,

        All = Cutter | Rotator | Stacker,
    }

    /// <summary>
    /// Every item a map can make right now: the closure of the map's mined minerals under the operators
    /// that are unlocked. The wave generator draws weakness shapes from this set and nowhere else, which
    /// is what gives the unlock ladder teeth - brief a left half-circle on a map with no rotator and the
    /// wave is unwinnable through no fault of the player.
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
        /// so far, so a mixed shape the player can actually build is never missed. Depth is the operator
        /// count, so the first time an item is reached is its shallowest - the worklist is breadth-first
        /// and every edge costs one step.
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
                // "which arrived first" of a worklist is not, and the depth taken is the deeper side plus
                // the step either way.
                for (int i = _shapes.Count - 1; i >= 0; i--)
                {
                    Shape other = _shapes[i];
                    if (shape.Overlaps(other)) continue;

                    Add(Shape.Stacked(shape, other), Math.Max(depth, _depth[other.Code]) + 1, queue);
                }
            }
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
