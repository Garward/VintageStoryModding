using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    public interface IStorageCollectibleResolver
    {
        CollectibleObject Resolve(EnumItemClass itemClass, string code);
    }
}
