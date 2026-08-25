namespace VintageKinematics.Storage.Topology
{
    public enum StorageTopologyIssue
    {
        RequiredChunkUnavailable,
        ControllerMissing,
        ControllerRoleMismatch,
        ControllerWarehouseMismatch,
        ControllerReferenceMismatch,
        UnlinkedMemberContact,
        MemberPositionMismatch,
        UnexpectedController,
        ForeignWarehouseContact,
        MemberLimitExceeded,
        GraphDistanceExceeded,
        OrphanedKnownMember,
        CapacityOverflow,
        TypeCapacityOverflow
    }
}
