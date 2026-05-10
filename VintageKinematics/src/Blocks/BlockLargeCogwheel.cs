using VintageKinematics.Network;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockLargeCogwheel : BlockKineticCogwheel
    {
        protected override EnumKineticRole MyRole => EnumKineticRole.LargeCogwheel;
    }
}
