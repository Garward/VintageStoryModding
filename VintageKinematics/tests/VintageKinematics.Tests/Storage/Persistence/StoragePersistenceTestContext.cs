using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Acceptance;
using VintageKinematics.Storage.Index;
using VintageKinematics.Storage.Persistence;

namespace VintageKinematics.Tests.Storage.Persistence
{
    internal static class StoragePersistenceTestContext
    {
        public static KineticStorageIndex CreateIndex(long capacity = 1000)
        {
            return new KineticStorageIndex(
                new StorageIndexLimits(capacity),
                new AcceptAllStorageValidator(),
                VKStorageKeys.KeyFor,
                StorageTestStacks.ExactMatch);
        }

        public static KineticStoragePersistence CreatePersistence(TestCollectibleResolver resolver)
        {
            return new KineticStoragePersistence(
                world: null,
                collectibleResolver: resolver,
                acceptanceValidator: new KineticStorageAcceptanceValidator());
        }
    }

    internal sealed class TestCollectibleResolver : IStorageCollectibleResolver
    {
        private readonly Dictionary<string, CollectibleObject> collectibles = new();

        public void Register(ItemStack stack)
        {
            CollectibleObject collectible = stack.Collectible;
            collectibles[Key(stack.Class, collectible.Code.ToString())] = collectible;
        }

        public CollectibleObject Resolve(EnumItemClass itemClass, string code)
        {
            collectibles.TryGetValue(Key(itemClass, code), out CollectibleObject collectible);
            return collectible;
        }

        private static string Key(EnumItemClass itemClass, string code)
        {
            return ((int)itemClass) + ":" + code;
        }
    }
}
