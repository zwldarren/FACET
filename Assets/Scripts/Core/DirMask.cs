namespace Facet.Core
{
    /// <summary>
    /// A set of the four cardinal directions, as four bits. It exists so a machine's ports can be
    /// computed once, in the simulation, and handed to the view as a value: the alternative - the view
    /// re-deriving "which sides is this machine wired on" from the belts and the machine's kind - is
    /// the duplicated rule that let the drawn picture drift from the simulation.
    ///
    /// Bit index is <see cref="Dir"/>'s ordinal, so <c>1 &lt;&lt; (int)Dir.North</c> is North's bit.
    /// </summary>
    public readonly struct DirMask
    {
        public readonly byte Bits;

        public DirMask(byte bits) => Bits = bits;

        public static readonly DirMask None = default;

        /// <summary>All four directions: the "this outlet may take anything" mask. A machine that
        /// does not filter uses it so the shared push rule needs no second code path.</summary>
        public static readonly DirMask All = new DirMask(0b1111);

        public bool Has(Dir direction) => (Bits & (1 << (int)direction)) != 0;

        public bool IsEmpty => Bits == 0;

        /// <summary>This mask plus one direction.</summary>
        public DirMask With(Dir direction) => new DirMask((byte)(Bits | (1 << (int)direction)));

        public override string ToString() => "DirMask(" + Bits + ")";
    }
}
