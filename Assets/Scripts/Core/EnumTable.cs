using System;

namespace Facet.Core
{
    /// <summary>
    /// Ordinal-indexed tables, sized the one way that cannot be short: <b>one slot past the highest value</b>,
    /// never one slot per member.
    ///
    /// The distinction is not academic. A closed enum in this codebase keeps its retired values' numbers dead
    /// (<see cref="BehaviorKind"/> and <see cref="TileKind"/> both do, so that every value authored in
    /// <c>Assets/Data/ContentDatabase.asset</c> keeps meaning what it meant), and a hole makes the member count
    /// smaller than the highest ordinal. A table sized by the count then throws on the first lookup of the
    /// highest-valued member - which is exactly what happened to the behaviour registry when the rework retired
    /// the converter and the assembler and added the rotator and the stacker above them: it built nine slots
    /// for values up to ten, and every world died in <c>MachineSystem</c>'s constructor.
    ///
    /// Sizing by the highest value is what "a lookup is an array index" actually requires, and it costs a few
    /// empty slots per hole.
    /// </summary>
    internal static class EnumTable
    {
        /// <summary>How many slots an ordinal-indexed table for this enum needs.</summary>
        public static int Slots<TEnum>() where TEnum : struct, Enum
        {
            int slots = 0;
            foreach (TEnum value in (TEnum[])Enum.GetValues(typeof(TEnum)))
            {
                int ordinal = Convert.ToInt32(value);
                if (ordinal >= slots) slots = ordinal + 1;
            }

            return slots;
        }

        /// <summary>A fresh table of that size, every slot null.</summary>
        public static T[] Create<TEnum, T>() where TEnum : struct, Enum => new T[Slots<TEnum>()];
    }
}
