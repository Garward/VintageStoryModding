namespace VintageKinematics.Api
{
    /// <summary>
    /// Optional escape-hatch hook. Implement on a BlockEntity (or one of its
    /// behaviors) to receive RPM updates directly, bypassing the standard
    /// <see cref="BEBehaviorKinetic.OnRPMChanged"/> path. Most consumers
    /// should use the behaviors instead — this hook exists for advanced
    /// integrations that don't fit the behavior model.
    /// </summary>
    public interface IKineticConsumer
    {
        /// <summary>
        /// Called whenever the network's RPM-relevant state changes.
        /// <paramref name="newRPM"/> is the signed effective RPM at this BlockEntity's position.
        /// </summary>
        void OnNetworkRPMChanged(float newRPM, IKineticNetworkInfo network);
    }
}
