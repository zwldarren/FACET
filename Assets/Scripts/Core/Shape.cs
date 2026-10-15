using System;

namespace Facet.Core
{
    /// <summary>What one quadrant of an item holds. <see cref="Empty"/> is the absence of content rather
    /// than a third mineral - which is what makes the state space 3^4-1 and makes stacking a partial
    /// function instead of a join.</summary>
    public enum Quad : byte
    {
        Empty = 0,
        Circle = 1,
        Square = 2,
    }

    /// <summary>
    /// One item, as four quadrants. This is the game's whole language: a mineral patch mines a full shape,
    /// the cutter splits one along a line, the rotator turns it a quarter clockwise, the stacker unions
    /// two disjoint ones - and an enemy's weakness is an exact one of the 80.
    ///
    /// <b>Identity is the exact quadrant content.</b> A top half-circle and a bottom half-circle are two
    /// different items, and so are circle-left/square-right and square-left/circle-right: rotation is a
    /// real operator with a real effect, which is the whole reason the rotator is a machine rather than a
    /// property. Equality is the code, so the simulation can hash, save and replay a shape for free.
    ///
    /// Two bits per quadrant, in NW, NE, SE, SW order. Eight of the sixteen bits are used; the upper bits
    /// are reserved for the second layer the design leaves room for and deliberately does not implement,
    /// which is why <see cref="Code"/> is a <see cref="uint"/> rather than a byte.
    /// </summary>
    public readonly struct Shape : IEquatable<Shape>
    {
        /// <summary>Quadrant indices, in the order the code stores them: the compass, read clockwise from
        /// the top left. North is +Y on the tile grid, so NW is the top-left corner of the item's
        /// square.</summary>
        public const int NW = 0;
        public const int NE = 1;
        public const int SE = 2;
        public const int SW = 3;

        /// <summary>How many quadrants an item has, full stop. Not a configurable: the algebra is
        /// four-part by definition, and every mask below is written against this.</summary>
        public const int QuadrantCount = 4;

        private const uint CodeMask = 0xFFu;
        private const int BitsPerQuadrant = 2;

        /// <summary>The four quadrants, packed. Two bits each, so a quadrant's content is one masked
        /// shift away and a stack is one OR.</summary>
        public readonly uint Code;

        public Shape(uint code) => Code = code & CodeMask;

        /// <summary>Nothing: the empty item, and the default of every field that holds a shape. Written
        /// as a named value because "no shape" is read far more often than it is constructed.</summary>
        public static readonly Shape None = default;

        /// <summary>The quadrant at an index, or <see cref="Quad.Empty"/> for an index off the item -
        /// out of range is a hole, never a throw on the tick path.</summary>
        public Quad At(int quadrant)
            => quadrant >= 0 && quadrant < QuadrantCount
                ? (Quad)((Code >> (quadrant * BitsPerQuadrant)) & 3u)
                : Quad.Empty;

        public bool IsEmpty => Code == 0;

        /// <summary>
        /// Does this item fill <em>every</em> quadrant? That is the currency: the Core banks a complete
        /// shape and destroys anything else, so one full circle and one full square are worth the same
        /// unit of money and a half is worth nothing at all. Asked as completeness rather than as identity
        /// with one mineral, which is what makes the rule a property of the algebra instead of of the map
        /// the player happens to be on.
        /// </summary>
        public bool IsFull => FilledCount == QuadrantCount;

        /// <summary>Is there content at this quadrant? The question the cut mask and the stack test both
        /// ask, so neither compares against <see cref="Quad.Empty"/> on its own.</summary>
        public bool Has(int quadrant) => At(quadrant) != Quad.Empty;

        /// <summary>
        /// The quadrant sharing this one's edge across the item's vertical axis - NW with NE, SE with SW. The
        /// indices run clockwise from the top left, so that neighbour is one flip of the last bit away.
        ///
        /// Said here rather than in each drawing, because two views ask it (an item figure on the map and the
        /// same figure rasterised for a card) and a seam drawn from both sides is one line only if both
        /// sides agree about which quadrants share it.
        /// </summary>
        public static int AcrossVertical(int quadrant) => quadrant ^ 1;

        /// <summary>The quadrant sharing this one's edge across the item's horizontal axis: NW with SW,
        /// NE with SE. See <see cref="AcrossVertical"/>.</summary>
        public static int AcrossHorizontal(int quadrant) => quadrant ^ 3;

        /// <summary>How many quadrants carry content: 0 for nothing, 4 for a full shape. The design's
        /// "depth" tiers are built on it (a quarter, a half, a three-quarter, a full).</summary>
        public int FilledCount
        {
            get
            {
                int filled = 0;
                for (int i = 0; i < QuadrantCount; i++)
                    if (Has(i)) filled++;

                return filled;
            }
        }

        /// <summary>Build a shape from its four quadrants, named in compass order.</summary>
        public static Shape Of(Quad nw, Quad ne, Quad se, Quad sw)
            => new Shape(Pack(NW, nw) | Pack(NE, ne) | Pack(SE, se) | Pack(SW, sw));

        /// <summary>The item a vein of this mineral yields: all four quadrants of it. What the drill mines
        /// and what the Core banks - a full shape is one currency whichever mineral it is made of
        /// (<see cref="IsFull"/>), and anything less than complete is destroyed.</summary>
        public static Shape Full(Quad mineral) => Of(mineral, mineral, mineral, mineral);

        /// <summary>One quadrant of a mineral: the atom every chain is built up from.</summary>
        public static Shape Quarter(int quadrant, Quad mineral)
            => new Shape(Pack(quadrant, mineral));

        /// <summary>This shape turned a quarter clockwise: each quadrant's content moves to the next one,
        /// NW to NE to SE to SW and back. Four turns is the identity, which is a test rather than a
        /// comment.
        ///
        /// A half turn is <em>not</em> the identity, and that is the design's locked decision: rotating
        /// the top half clockwise gives the right half, and two turns give the bottom half - the
        /// complement piece on the same cut line. The rotator's second job is repairing an orientation
        /// the briefing asked for and the line is not running.</summary>
        public Shape RotatedCW() => new Shape(((Code << BitsPerQuadrant) | (Code >> (BitsPerQuadrant * (QuadrantCount - 1)))) & CodeMask);

        /// <summary>
        /// Split the shape along a line, keeping the quadrants on each side of it and emptying the other
        /// side. The line's own 180-degree symmetry is why the machine stores a facing rather than a
        /// line: a horizontal cut is the same cut facing north or south.
        ///
        /// Returns the two pieces in the order the cutter's outlets are named - <c>a</c> is the side the
        /// cut line's axis grows toward (top for a horizontal cut, left for a vertical one). Which piece
        /// leaves by which outlet is the machine's business, not the algebra's.
        ///
        /// Cutting a quarter yields the quarter itself plus nothing: the empty side emits nothing, and
        /// "an item can be cut at most twice" falls out of this rather than being a rule.
        /// </summary>
        public static (Shape a, Shape b) Cut(Shape shape, bool vertical)
        {
            if (vertical)
            {
                // Left keeps NW and SW, right keeps NE and SE.
                return (new Shape(shape.Code & LeftMask), new Shape(shape.Code & RightMask));
            }

            // Top keeps NW and NE, bottom keeps SE and SW.
            return (new Shape(shape.Code & TopMask), new Shape(shape.Code & BottomMask));
        }

        /// <summary>
        /// The disjoint union of two shapes: everything under, plus everything over. Overlap is illegal -
        /// two different minerals cannot share a quadrant - so this throws; a machine that has to keep
        /// ticking while it waits for a legal pair asks <see cref="Overlaps"/> first and stalls, which is
        /// the stacker's visible queue.
        /// </summary>
        public static Shape Stacked(Shape under, Shape over)
        {
            if (under.Overlaps(over))
                throw new ArgumentException(
                    "cannot stack " + over + " over " + under + " - the two share a quadrant", nameof(over));

            return new Shape(under.Code | over.Code);
        }

        /// <summary>The forgiving half of <see cref="Stacked"/>: false when the two cannot be unioned,
        /// in which case <paramref name="stacked"/> is nothing. The tick path never throws.</summary>
        public static bool TryStack(Shape under, Shape over, out Shape stacked)
        {
            if (under.Overlaps(over))
            {
                stacked = None;
                return false;
            }

            stacked = new Shape(under.Code | over.Code);
            return true;
        }

        /// <summary>
        /// Do the two shapes both fill a quadrant? Asked before a stack, and the whole of the
        /// stacker's "wait for a piece that fits" rule.
        ///
        /// Quadrant by quadrant, deliberately not a bitwise AND of the two codes: a mineral is two bits and
        /// the two minerals are 1 and 2, so a circle over a square in the same quadrant shares no bit at all
        /// - the AND would say "disjoint" and the union would mint a code with a 3 in it, which is not an
        /// item. The encoding is built for a second layer (the spare bits), so the overlap test has to read
        /// the quadrants rather than trust the bits to be one per quadrant.
        /// </summary>
        public bool Overlaps(Shape other)
        {
            for (int i = 0; i < QuadrantCount; i++)
                if (Has(i) && other.Has(i)) return true;

            return false;
        }

        /// <summary>Does this item's structure contain that mineral anywhere? The sorter's
        /// contains-mineral predicate: it lets a line route "anything with square in it" without naming
        /// all fifty mixed shapes by hand.</summary>
        public bool Contains(Quad mineral)
        {
            for (int i = 0; i < QuadrantCount; i++)
                if (At(i) == mineral) return true;

            return false;
        }

        /// <summary>Is every filled quadrant this one mineral? A full circle and a top half-circle are
        /// pure; a circle-top-square-bottom is not. What makes the design's "30 pure, 50 mixed" count
        /// checkable.</summary>
        public bool IsPure(Quad mineral)
        {
            if (IsEmpty) return false;

            for (int i = 0; i < QuadrantCount; i++)
                if (Has(i) && At(i) != mineral) return false;

            return true;
        }

        public bool Equals(Shape other) => Code == other.Code;

        public override bool Equals(object obj) => obj is Shape other && Equals(other);

        public override int GetHashCode() => (int)Code;

        public static bool operator ==(Shape left, Shape right) => left.Code == right.Code;

        public static bool operator !=(Shape left, Shape right) => left.Code != right.Code;

        /// <summary>The compact quadrant code, NW NE SE SW: "O" for a circle quadrant, "S" for a square
        /// one, "-" for a hole. ASCII on purpose - the font a built player draws IMGUI text with has no
        /// symbols for the shapes themselves, which is why the old word-glyph scheme retired and why a
        /// shape is drawn, not spelled, wherever it is a mark.</summary>
        public override string ToString()
        {
            var text = new char[QuadrantCount];
            for (int i = 0; i < QuadrantCount; i++)
                text[i] = At(i) == Quad.Circle ? 'O' : At(i) == Quad.Square ? 'S' : '-';

            return new string(text);
        }

        /// <summary>
        /// The shape said in words, orientation included - what a tooltip, a briefing line or a test
        /// failure prints. The HUD's <em>marks</em> are drawn from the quadrants procedurally; this is
        /// what a sentence quotes, and it names the corners rather than the code so "which way up" is
        /// never a guess.
        /// </summary>
        public string Describe()
        {
            if (IsEmpty) return "nothing";

            string pure = PureName();
            if (pure != null) return pure;

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < QuadrantCount; i++)
            {
                if (!Has(i)) continue;
                if (text.Length > 0) text.Append(" + ");
                text.Append(MineralName(At(i))).Append(' ').Append(CornerName(i));
            }

            return text.ToString();
        }

        /// <summary>The one-mineral names, when a shape is pure: "full circle", "left half-square",
        /// "quarter circle (SE)". Null for a mixed shape, which <see cref="Describe"/> then spells
        /// quadrant by quadrant.</summary>
        private string PureName()
        {
            Quad mineral = Quad.Empty;
            for (int i = 0; i < QuadrantCount; i++)
            {
                if (!Has(i)) continue;
                mineral = At(i);
                break;
            }

            if (!IsPure(mineral)) return null;

            string name = MineralName(mineral);
            switch (FilledCount)
            {
                case 1:
                    return "quarter " + name + " (" + CornerCode(FilledQuadrant()) + ")";

                case 2:
                    // Two filled quadrants of one mineral: a true half when they share an edge, a
                    // diagonal pair when they meet only at the centre.
                    if (Has(NW) && Has(NE)) return "top half-" + name;
                    if (Has(SW) && Has(SE)) return "bottom half-" + name;
                    if (Has(NW) && Has(SW)) return "left half-" + name;
                    if (Has(NE) && Has(SE)) return "right half-" + name;
                    return "diagonal half-" + name;

                case 3:
                    return "three-quarter " + name;

                default:
                    return "full " + name;
            }
        }

        /// <summary>The one quadrant carrying content, for a shape whose <see cref="FilledCount"/> is
        /// 1 - the atom's corner.</summary>
        private int FilledQuadrant()
        {
            for (int i = 0; i < QuadrantCount; i++)
                if (Has(i)) return i;

            return NW;
        }

        public static string MineralName(Quad mineral)
            => mineral == Quad.Circle ? "circle" : mineral == Quad.Square ? "square" : "nothing";

        /// <summary>The corner a quadrant index names, spelled out for a sentence.</summary>
        public static string CornerName(int quadrant)
        {
            switch (quadrant)
            {
                case NW: return "top-left";
                case NE: return "top-right";
                case SE: return "bottom-right";
                default: return "bottom-left";
            }
        }

        /// <summary>The same corner as a two-letter code, for a compact parenthetical.</summary>
        public static string CornerCode(int quadrant)
        {
            switch (quadrant)
            {
                case NW: return "NW";
                case NE: return "NE";
                case SE: return "SE";
                default: return "SW";
            }
        }

        private static uint Pack(int quadrant, Quad value)
            => ((uint)value & 3u) << (quadrant * BitsPerQuadrant);

        private const uint TopMask = 0x0Fu;      // NW | NE
        private const uint BottomMask = 0xF0u;   // SE | SW
        private const uint LeftMask = 0xC3u;     // NW | SW
        private const uint RightMask = 0x3Cu;    // NE | SE
    }
}
