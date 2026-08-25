using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Acceptance;

namespace VintageKinematics.Storage.Persistence
{
    internal sealed class StorageRecordResolver
    {
        private readonly IWorldAccessor world;
        private readonly IStorageCollectibleResolver collectibleResolver;
        private readonly IStorageAcceptanceValidator acceptanceValidator;

        public StorageRecordResolver(
            IWorldAccessor world,
            IStorageCollectibleResolver collectibleResolver,
            IStorageAcceptanceValidator acceptanceValidator)
        {
            this.world = world;
            this.collectibleResolver = collectibleResolver;
            this.acceptanceValidator = acceptanceValidator;
        }

        public StorageRecordResolution Resolve(PersistedStorageEntry record)
        {
            if (!StorageAttributeCodec.TryDecode(record.AttributeBytes, out TreeAttribute attributes))
            {
                return Quarantine(record, StorageQuarantineReason.InvalidAttributes);
            }

            CollectibleObject collectible;
            try
            {
                collectible = collectibleResolver.Resolve(record.ItemClass, record.Code);
            }
            catch
            {
                collectible = null;
            }
            if (!MatchesRecord(collectible, record)) return StorageRecordResolution.Unresolved();

            ItemStack exemplar = new ItemStack(collectible, 1) { Attributes = attributes };
            StorageAcceptanceResult acceptance = acceptanceValidator.Validate(world, exemplar, 1);
            if (!acceptance.Accepted)
            {
                return Quarantine(
                    record,
                    StorageQuarantineReason.UnsafeItemState,
                    acceptance.MessageLangCode);
            }
            return StorageRecordResolution.Resolved(exemplar);
        }

        private static bool MatchesRecord(CollectibleObject collectible, PersistedStorageEntry record)
        {
            return collectible != null
                && collectible.ItemClass == record.ItemClass
                && string.Equals(collectible.Code?.ToString(), record.Code, StringComparison.Ordinal);
        }

        private static StorageRecordResolution Quarantine(
            PersistedStorageEntry record,
            StorageQuarantineReason reason,
            string detail = null)
        {
            return StorageRecordResolution.Quarantined(
                new QuarantinedStorageEntry(reason, record.RawRecordBytes, detail));
        }
    }
}
