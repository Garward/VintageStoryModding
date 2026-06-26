namespace VintageKinematics.Api
{
    /// <summary>
    /// Stateful wrapper around <see cref="KineticConditionEvaluator"/> for automation blocks
    /// that need edge detection. Poll it periodically and act when <see cref="Changed"/> is true.
    /// </summary>
    public class KineticConditionState
    {
        public bool Active { get; private set; }
        public bool Changed { get; private set; }
        public bool RisingEdge { get; private set; }
        public bool FallingEdge { get; private set; }

        public bool Update(IKineticNetworkInfo network, KineticConditionSettings settings)
        {
            bool next = KineticConditionEvaluator.Evaluate(network, settings, Active);
            Changed = next != Active;
            RisingEdge = Changed && next;
            FallingEdge = Changed && !next;
            Active = next;
            return Changed;
        }

        public void Reset(bool active = false)
        {
            Active = active;
            Changed = false;
            RisingEdge = false;
            FallingEdge = false;
        }
    }
}
