namespace Facet.Game
{
    /// <summary>
    /// The single place a <see cref="ViewFrame"/> is published for a frame. The driver writes it in
    /// production; a test writes it directly. Views read the clock and never see the driver, so the
    /// frame they draw is a value rather than a MonoBehaviour they have to know how to reach into.
    ///
    /// It is a mutable clock rather than a seam with an interface because the two "adapters" write the
    /// same one field: nothing varies across the seam but who sets it.
    /// </summary>
    public sealed class FrameClock
    {
        /// <summary>The frame the views are drawing right now.</summary>
        public ViewFrame Frame { get; set; }
    }
}
