using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Worlds.Generation.Onboarding;

/// <summary>Resolves inert authored assets and verifies collision before any world writes.</summary>
public sealed class TavernBlockPalette
{
    private readonly Dictionary<TavernBlock, Block> blocks = new();
    private readonly Dictionary<int, TavernBlock> roles = new();

    public TavernBlockPalette(System.Func<AssetLocation, Block?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        Add(TavernBlock.Stone, "vrpg:tavern-stone", 1);
        Add(TavernBlock.Timber, "vrpg:tavern-timber", 1);
        Add(TavernBlock.BottomSlab, "vrpg:tavern-slab", 0.5f);
        Add(TavernBlock.Lamp, "vrpg:tavern-lamp", 1);

        void Add(TavernBlock role, string code, float height)
        {
            Block? block = resolve(new AssetLocation(code));
            if (block == null || block.Id <= 0 || block.Code?.ToString() != code
                || block.GetType() != typeof(Block) || !string.IsNullOrEmpty(block.EntityClass)
                || block.BlockBehaviors.Length != 0 || block.BlockEntityBehaviors.Length != 0
                || block.CollectibleBehaviors.Length != 0 || block.IsLiquid()
                || block.CombustibleProps != null || block.Drops == null || block.Drops.Length != 0
                || !HasCollision(block.CollisionBoxes, height))
                throw new InvalidOperationException("Missing or unsafe tavern asset: " + code);
            blocks.Add(role, block);
            roles.Add(block.Id, role);
        }
    }

    public int BlockId(TavernBlock role) => role == TavernBlock.Air ? 0 : blocks[role].Id;

    public TavernBlock Classify(Block block, IBlockAccessor accessor, BlockPos position)
    {
        if (block.Id == 0) return TavernBlock.Air;
        if (!roles.TryGetValue(block.Id, out TavernBlock role)
            || !HasCollision(block.GetCollisionBoxes(accessor, position), role == TavernBlock.BottomSlab ? 0.5f : 1))
            return (TavernBlock)(-1);
        return role;
    }

    private static bool HasCollision(Cuboidf[]? boxes, float height) =>
        boxes is { Length: 1 } && boxes[0].X1 == 0 && boxes[0].Y1 == 0 && boxes[0].Z1 == 0
        && boxes[0].X2 == 1 && boxes[0].Y2 == height && boxes[0].Z2 == 1;
}
