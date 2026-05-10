namespace VintageKinematics.Api
{
    /// <summary>
    /// Read-only snapshot of a kinetic network's state. Returned by
    /// <see cref="BEBehaviorKinetic.Network"/> so consumer mods can inspect
    /// stress, RPM, and status without holding a reference to the mutable
    /// internal network type.
    /// </summary>
    public interface IKineticNetworkInfo
    {
        /// <summary>Unique id assigned when the network was built.</summary>
        long NetworkId { get; }
        /// <summary>Signed RPM produced by the active source. Sign sets whole-network direction.</summary>
        float SourceRPM { get; }
        /// <summary>Per-network upper RPM cap, derived from declared tier averages.</summary>
        float MaxRPM { get; }
        /// <summary>True if two sources disagree on direction; the network is halted.</summary>
        bool IsConflicted { get; }
        /// <summary>True if total consumer stress exceeds source capacity; the network is halted.</summary>
        bool IsOverstressed { get; }
        /// <summary>Sum of consumer stress (Su, RPM-scaled).</summary>
        float StressTotal { get; }
        /// <summary>Sum of source capacity (Su, source rated-RPM × |stressImpact|).</summary>
        float StressCapacity { get; }
        /// <summary>Total number of kinetic nodes in this network.</summary>
        int NodeCount { get; }
    }
}
