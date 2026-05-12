namespace VintageKinematics.Api
{
    /// <summary>
    /// Implemented by block entities that declare cell-aware face IO via <see cref="IOFaceMap"/>.
    /// Consumed by <see cref="InventoryPusher"/> and the funnel so push/pull through a multiblock
    /// can target a specific cell of the footprint instead of being collapsed to controller-only IO.
    /// </summary>
    public interface IFaceMappedContainer
    {
        IOFaceMap IOFaces { get; }
    }
}
