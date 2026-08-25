using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Prevents ambiguous or cross-warehouse storage placement before world mutation.
    /// </summary>
    public sealed class BlockBehaviorRequireStorageOwnershipOnPlace : BlockBehavior
    {
        private StoragePlacementRole role = StoragePlacementRole.Cell;

        public BlockBehaviorRequireStorageOwnershipOnPlace(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            role = properties["role"].AsString("cell") == "controller"
                ? StoragePlacementRole.Controller
                : StoragePlacementRole.Cell;
        }

        public override bool CanPlaceBlock(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            ref EnumHandling handling,
            ref string failureCode)
        {
            StoragePlacementDecision decision = WorldStoragePlacementPolicy.Evaluate(
                world,
                blockSel.Position,
                role);
            if (decision.Allowed) return true;

            failureCode = "storage-placement-" + decision.Issue.ToString().ToLowerInvariant();
            handling = EnumHandling.PreventSubsequent;
            return false;
        }
    }
}
