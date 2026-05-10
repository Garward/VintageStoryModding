namespace VintageKinematics.Api
{
    /// <summary>
    /// Returned by <see cref="IKineticConnector.TryConnect"/> to describe
    /// how two blocks couple: gear ratio, direction (+1 same as parent,
    /// -1 reversed), and any phase offset to apply.
    /// </summary>
    public readonly struct KineticConnectionResult
    {
        /// <summary>Gear ratio applied across this edge (multiplies onto the upstream Ratio).</summary>
        public float Ratio        { get; }
        /// <summary>+1 to keep direction, -1 to reverse it.</summary>
        public int Direction      { get; }
        /// <summary>Visual phase offset applied to the downstream node.</summary>
        public float PhaseOffset  { get; }

        /// <summary>Constructs an edge result. <paramref name="phaseOffset"/> defaults to 0.</summary>
        public KineticConnectionResult(float ratio, int direction, float phaseOffset = 0f)
        {
            Ratio = ratio;
            Direction = direction;
            PhaseOffset = phaseOffset;
        }
    }
}
