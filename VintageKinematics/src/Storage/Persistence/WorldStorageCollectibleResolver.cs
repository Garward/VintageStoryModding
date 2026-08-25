using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Persistence
{
    public sealed class WorldStorageCollectibleResolver : IStorageCollectibleResolver
    {
        private readonly IWorldAccessor world;

        public WorldStorageCollectibleResolver(IWorldAccessor world)
        {
            this.world = world;
        }

        public CollectibleObject Resolve(EnumItemClass itemClass, string code)
        {
            if (world == null || string.IsNullOrEmpty(code)) return null;

            AssetLocation location = new AssetLocation(code);
            return itemClass == EnumItemClass.Block
                ? world.GetBlock(location)
                : world.GetItem(location);
        }
    }
}
