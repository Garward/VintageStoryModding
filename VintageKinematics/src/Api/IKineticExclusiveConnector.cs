namespace VintageKinematics.Api
{
    /// <summary>
    /// Marker for directional/custom kinetic connectors whose refusal is final.
    /// Use this for blocks that expose only explicit kinetic ports; otherwise a
    /// neighboring connector may accept the edge from its side and bypass the
    /// block's own port rules.
    /// </summary>
    public interface IKineticExclusiveConnector : IKineticConnector
    {
    }
}
