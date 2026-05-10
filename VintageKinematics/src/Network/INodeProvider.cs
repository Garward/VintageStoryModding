using Vintagestory.API.MathTools;

namespace VintageKinematics.Network
{
    public interface INodeProvider
    {
        bool TryGetNode(BlockPos pos, out KineticNode node);
        KineticConnection? TryGetCustomConnection(KineticNode from, KineticNode to);
    }
}
