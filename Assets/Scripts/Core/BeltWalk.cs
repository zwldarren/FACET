using System;
using System.Collections.Generic;

namespace Facet.Core
{
    /// <summary>
    /// The staircase a belt drag lays: the one statement of the walk rule, shared by the drag and by
    /// anything predicting what a drag from A to B produces. The walk is Manhattan and takes the drag's
    /// dominant axis first (the axis rule <c>BuildController</c> uses to aim a machine), decided once
    /// per walk rather than per step. Geometry only - whether a cell accepts a belt is
    /// <c>BuildController</c>'s question; <see cref="Predict"/> answers "what would the drag lay".
    /// </summary>
    public static class BeltWalk
    {
        /// <summary>One cell of the staircase and the direction the belt laid there points.</summary>
        public readonly struct Step
        {
            public readonly Int2 Cell;
            public readonly Dir Direction;

            public Step(Int2 cell, Dir direction)
            {
                Cell = cell;
                Direction = direction;
            }
        }

        /// <summary>Which axis a walk from <paramref name="from"/> to <paramref name="to"/> takes
        /// first: Y when the drag ran further along Y, X winning a tie as the machine aim does.</summary>
        public static bool YFirst(Int2 from, Int2 to)
            => Math.Abs(to.Y - from.Y) > Math.Abs(to.X - from.X);

        /// <summary>The next cell of the staircase one step closer to the target.</summary>
        public static Int2 NextStep(Int2 from, Int2 to, bool yFirst)
        {
            if (yFirst && from.Y != to.Y)
                return new Int2(from.X, from.Y + Math.Sign(to.Y - from.Y));

            return from.X != to.X
                ? new Int2(from.X + Math.Sign(to.X - from.X), from.Y)
                : new Int2(from.X, from.Y + Math.Sign(to.Y - from.Y));
        }

        /// <summary>The way a belt on <paramref name="from"/> points so its item rides to
        /// <paramref name="to"/> - the neighbouring-cell case of the machine-aim rule.</summary>
        public static Dir DirectionOf(Int2 from, Int2 to)
        {
            if (to.X > from.X) return Dir.East;
            if (to.X < from.X) return Dir.West;
            return to.Y > from.Y ? Dir.North : Dir.South;
        }

        /// <summary>Every cell the drag from <paramref name="from"/> to <paramref name="to"/> lays, in
        /// walk order with each direction; the end cell is included, and a zero-length walk lays
        /// nothing (a plain click is release-time placement, not a staircase). Allocates - the drag
        /// itself walks incrementally through <see cref="NextStep"/>.</summary>
        public static List<Step> Predict(Int2 from, Int2 to)
        {
            var steps = new List<Step>();
            Predict(from, to, steps);
            return steps;
        }

        /// <summary><see cref="Predict"/> into a caller-owned list, for anyone asking per frame.</summary>
        public static void Predict(Int2 from, Int2 to, List<Step> into)
        {
            bool yFirst = YFirst(from, to);
            Int2 cur = from;
            Dir dir = Dir.East;

            while (cur != to)
            {
                Int2 next = NextStep(cur, to, yFirst);
                dir = DirectionOf(cur, next);
                into.Add(new Step(cur, dir));
                cur = next;
            }

            // The walk's last step placed the end cell pointing the way it was entered, so the
            // staircase delivers into wherever the drag promised it would.
            if (cur == to && !(from == to)) into.Add(new Step(to, dir));
        }
    }
}
