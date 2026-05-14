using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using VintageKinematics.BlockEntities;
using VintageKinematics.Crafting;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Subscribes to the sibling <see cref="BEBehaviorKineticPiston"/>'s "head" element bottom-out
    /// landmark (oscillate phase π) on both sides. Client side fires impact particles + sound the
    /// instant its renderer's wave hits bottom-out (no packet round-trip — locked to visuals).
    /// Server side advances the per-recipe crush tick counter and mutates the basin inventory.
    /// </summary>
    public class BEBehaviorCrusherProcess : BlockEntityBehavior
    {
        private int crushTicksAccumulated = 0;
        private string crushingItemCode = null;
        private float crushTickMultiplier = 1f;

        public BEBehaviorCrusherProcess(BlockEntity be) : base(be) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            if (properties != null && properties["crushTickMultiplier"].Exists)
            {
                crushTickMultiplier = properties["crushTickMultiplier"].AsFloat(1f);
            }
            var piston = Blockentity.GetBehavior<BEBehaviorKineticPiston>();
            if (piston == null)
            {
                api.Logger.Warning($"[VintageKinematics] CrusherProcess at {Pos} has no sibling KineticPiston - inert");
                return;
            }
            piston.OnPhaseCross("head", MathF.PI, OnHeadBottomOut);
        }

        private void OnHeadBottomOut()
        {
            var kin = Blockentity.GetBehavior<BEBehaviorKinetic>();
            if (kin == null || MathF.Abs(kin.ActualRPM) < 0.01f) return;

            BlockPos basinPos = Pos.DownCopy();
            BECrusherBasin basin = Api.World.BlockAccessor.GetBlockEntity(basinPos) as BECrusherBasin;
            if (basin == null) return;

            ItemSlot inputSlot = basin.Inventory[BECrusherBasin.SlotInput];
            if (inputSlot.Empty) return;

            var registry = Api.ModLoader.GetModSystem<KineticCrusherRecipeRegistry>();
            var recipe = registry?.FindRecipe(inputSlot.Itemstack);
            if (recipe == null) return;

            int requiredQty = Math.Max(1, recipe.Ingredient?.StackSize ?? 1);
            if (inputSlot.StackSize < requiredQty) return;

            if (Api.Side == EnumAppSide.Client)
            {
                SpawnImpactEffects(basinPos);
                return;
            }

            string code = inputSlot.Itemstack.Collectible.Code.ToString();
            if (crushingItemCode != code)
            {
                crushingItemCode = code;
                crushTicksAccumulated = 0;
            }

            crushTicksAccumulated++;
            int effectiveTicks = (int)Math.Ceiling(recipe.CrushTicks * crushTickMultiplier);
            if (effectiveTicks < 1) effectiveTicks = 1;
            if (crushTicksAccumulated >= effectiveTicks)
            {
                // Capture wildcard value (e.g. "iron" from "ingot-iron" matched by "ingot-*") so
                // outputs with '*' in their code resolve to the same metal as the input.
                string captured = null;
                if (recipe.Ingredient?.Code?.Path?.Contains('*') == true)
                {
                    captured = WildcardUtil.GetWildcardValue(recipe.Ingredient.Code, inputSlot.Itemstack.Collectible.Code);
                }

                inputSlot.TakeOut(requiredQty);
                inputSlot.MarkDirty();

                if (recipe.Outputs != null)
                {
                    foreach (var o in recipe.Outputs)
                    {
                        if (o == null) continue;
                        ItemStack outStack = ResolveOutputStack(o, captured);
                        if (outStack == null) continue;
                        outStack.StackSize = o.StackSize;
                        basin.DepositOutput(outStack);
                    }
                }

                crushTicksAccumulated = 0;
                if (inputSlot.Empty) crushingItemCode = null;
            }
        }

        private ItemStack ResolveOutputStack(JsonItemStack o, string captured)
        {
            if (captured != null && o.Code?.Path?.Contains('*') == true)
            {
                AssetLocation substituted = new AssetLocation(o.Code.Domain, o.Code.Path.Replace("*", captured));
                if (o.Type == EnumItemClass.Block)
                {
                    Block b = Api.World.GetBlock(substituted);
                    return b == null ? null : new ItemStack(b, 1);
                }
                else
                {
                    Item it = Api.World.GetItem(substituted);
                    return it == null ? null : new ItemStack(it, 1);
                }
            }
            return o.ResolvedItemstack?.Clone();
        }

        private void SpawnImpactEffects(BlockPos basinPos)
        {
            Vec3d at = new Vec3d(basinPos.X + 0.5, basinPos.Y + 0.55, basinPos.Z + 0.5);

            var particles = new SimpleParticleProperties(
                minQuantity: 6,
                maxQuantity: 10,
                color: ColorUtil.ColorFromRgba(160, 160, 160, 200),
                minPos: at.AddCopy(-0.25, 0.0, -0.25),
                maxPos: at.AddCopy(0.25, 0.05, 0.25),
                minVelocity: new Vec3f(-0.4f, 0.2f, -0.4f),
                maxVelocity: new Vec3f(0.4f, 0.6f, 0.4f),
                lifeLength: 0.5f,
                gravityEffect: 0.5f,
                minSize: 0.14f,
                maxSize: 0.22f,
                model: EnumParticleModel.Quad
            );
            Api.World.SpawnParticles(particles);
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/anvil"), at.X, at.Y, at.Z, null, true, 16, 0.7f);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("crushTicks", crushTicksAccumulated);
            tree.SetString("crushingCode", crushingItemCode ?? "");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            crushTicksAccumulated = tree.GetInt("crushTicks", 0);
            string code = tree.GetString("crushingCode", "");
            crushingItemCode = string.IsNullOrEmpty(code) ? null : code;
        }
    }
}
