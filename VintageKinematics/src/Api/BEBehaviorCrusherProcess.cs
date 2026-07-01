using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
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
    public class BEBehaviorCrusherProcess : BlockEntityBehavior, IExternalWorkProgressProvider
    {
        private const int DefaultVanillaCrushTicks = 4;

        private int crushTicksAccumulated = 0;
        private string crushingItemCode = null;
        private float crushTickMultiplier = 1f;

        public BEBehaviorCrusherProcess(BlockEntity be) : base(be) { }

        public string ExternalProgressProviderCode => "CrusherProcess";
        public float ExternalWorkProgress => CurrentCrushProgress;
        public float ExternalWorkProgressMax => CurrentCrushProgressMax;

        public float CurrentCrushProgress => Math.Max(0, crushTicksAccumulated);

        public float CurrentCrushProgressMax
        {
            get
            {
                return TryResolveCurrentWork(out _, out _, out _, out _, out _, out int effectiveTicks)
                    ? Math.Max(1, effectiveTicks)
                    : 1;
            }
        }

        public bool CanProcessCurrentBasin()
        {
            if (!TryResolveCurrentWork(out _, out _, out _, out _, out _, out _)) return false;

            var kin = Blockentity.GetBehavior<BEBehaviorKinetic>();
            return kin != null && MathF.Abs(kin.ActualRPM) >= 0.01f;
        }

        public bool CanProgressExternalWork() => CanProcessCurrentBasin();

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            JsonObject defaults = Block?.Attributes?["vkCrusherProcess"];
            if (properties != null && properties["crushTickMultiplier"].Exists)
            {
                crushTickMultiplier = properties["crushTickMultiplier"].AsFloat(1f);
            }
            else if (defaults != null && defaults["crushTickMultiplier"].Exists)
            {
                crushTickMultiplier = defaults["crushTickMultiplier"].AsFloat(1f);
            }
            else
            {
                Dictionary<string, float> byMetal = defaults?["crushTickMultiplierByMetal"].AsObject<Dictionary<string, float>>(null);
                string metal = Block?.Variant?["metal"];
                if (!string.IsNullOrEmpty(metal) && byMetal != null && byMetal.TryGetValue(metal, out float multiplier))
                {
                    crushTickMultiplier = multiplier;
                }
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
            if (basin == null)
            {
                ResetProgressIfNeeded();
                return;
            }

            ItemSlot inputSlot = basin.Inventory[BECrusherBasin.SlotInput];
            if (inputSlot.Empty)
            {
                ResetProgressIfNeeded();
                return;
            }

            var recipe = GetUsableCustomRecipe(inputSlot.Itemstack);
            CrushingProperties vanillaProps = recipe == null ? GetUsableVanillaCrushingProps(inputSlot.Itemstack) : null;
            if (recipe == null && vanillaProps == null)
            {
                ResetProgressIfNeeded();
                return;
            }

            int requiredQty = recipe == null ? 1 : Math.Max(1, recipe.Ingredient?.StackSize ?? 1);
            if (inputSlot.StackSize < requiredQty)
            {
                ResetProgressIfNeeded();
                return;
            }

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
            int baseCrushTicks = recipe?.CrushTicks ?? DefaultVanillaCrushTicks;
            int effectiveTicks = (int)Math.Ceiling(baseCrushTicks * crushTickMultiplier);
            if (effectiveTicks < 1) effectiveTicks = 1;
            if (crushTicksAccumulated >= effectiveTicks)
            {
                if (recipe != null) CompleteCustomRecipe(inputSlot, basin, recipe, requiredQty);
                else CompleteVanillaCrushing(inputSlot, basin, vanillaProps);

                crushTicksAccumulated = 0;
                if (inputSlot.Empty) crushingItemCode = null;
            }

            Blockentity.MarkDirty(true);
        }

        private bool TryResolveCurrentWork(
            out BECrusherBasin basin,
            out ItemSlot inputSlot,
            out KineticCrusherRecipe recipe,
            out CrushingProperties vanillaProps,
            out int requiredQty,
            out int effectiveTicks)
        {
            basin = Api?.World?.BlockAccessor.GetBlockEntity(Pos.DownCopy()) as BECrusherBasin;
            inputSlot = basin?.Inventory?[BECrusherBasin.SlotInput];
            recipe = null;
            vanillaProps = null;
            requiredQty = 0;
            effectiveTicks = 1;

            if (inputSlot == null || inputSlot.Empty) return false;

            recipe = GetUsableCustomRecipe(inputSlot.Itemstack);
            vanillaProps = recipe == null ? GetUsableVanillaCrushingProps(inputSlot.Itemstack) : null;
            if (recipe == null && vanillaProps == null) return false;

            requiredQty = recipe == null ? 1 : Math.Max(1, recipe.Ingredient?.StackSize ?? 1);
            if (inputSlot.StackSize < requiredQty) return false;

            int baseCrushTicks = recipe?.CrushTicks ?? DefaultVanillaCrushTicks;
            effectiveTicks = (int)Math.Ceiling(baseCrushTicks * crushTickMultiplier);
            if (effectiveTicks < 1) effectiveTicks = 1;
            return true;
        }

        private void ResetProgressIfNeeded()
        {
            if (crushTicksAccumulated == 0 && crushingItemCode == null) return;
            crushTicksAccumulated = 0;
            crushingItemCode = null;
            if (Api?.Side == EnumAppSide.Server) Blockentity.MarkDirty(true);
        }

        private void CompleteCustomRecipe(ItemSlot inputSlot, BECrusherBasin basin, KineticCrusherRecipe recipe, int requiredQty)
        {
            // Capture wildcard value (e.g. "iron" from "ingot-iron" matched by "ingot-*") so
            // outputs with '*' in their code resolve to the same metal as the input.
            string captured = null;
            if (recipe.Ingredient?.Code?.Path?.Contains('*') == true)
            {
                captured = RecipeWildcardUtil.GetWildcardValue(recipe.Ingredient.Code, inputSlot.Itemstack.Collectible.Code);
            }

            inputSlot.TakeOut(requiredQty);
            inputSlot.MarkDirty();

            if (recipe.Outputs == null) return;
            foreach (var o in recipe.Outputs)
            {
                if (o == null) continue;
                ItemStack outStack = RecipeWildcardUtil.ResolveOutputStack(Api.World, o, captured, inputSlot.Itemstack?.Collectible?.Code);
                if (outStack == null) continue;
                outStack.StackSize = o.StackSize;
                basin.DepositOutput(outStack);
            }
        }

        private void CompleteVanillaCrushing(ItemSlot inputSlot, BECrusherBasin basin, CrushingProperties props)
        {
            ItemStack outStack = props?.CrushedStack?.ResolvedItemstack?.Clone();

            inputSlot.TakeOut(1);
            inputSlot.MarkDirty();

            if (outStack == null) return;
            outStack.StackSize = GameMath.RoundRandom(Api.World.Rand, props.Quantity.nextFloat(outStack.StackSize, Api.World.Rand));
            if (outStack.StackSize <= 0) return;

            basin.DepositOutput(outStack);
        }

        private CrushingProperties GetUsableVanillaCrushingProps(ItemStack stack)
        {
            CrushingProperties props = stack?.Collectible?.GetCrushingProperties(Api.World, stack);
            if (props?.CrushedStack?.ResolvedItemstack == null) return null;
            return props.HardnessTier <= PulverizerTier ? props : null;
        }

        private KineticCrusherRecipe GetUsableCustomRecipe(ItemStack stack)
        {
            var registry = Api.ModLoader.GetModSystem<KineticCrusherRecipeRegistry>();
            var recipe = registry?.FindRecipe(stack);
            if (recipe == null) return null;

            int requiredTier = GetCustomRecipeTier(stack, recipe);
            return requiredTier <= PulverizerTier ? recipe : null;
        }

        private int GetCustomRecipeTier(ItemStack stack, KineticCrusherRecipe recipe)
        {
            CrushingProperties props = stack?.Collectible?.GetCrushingProperties(Api.World, stack);
            if (props != null) return props.HardnessTier;

            string path = stack?.Collectible?.Code?.Path ?? recipe?.Ingredient?.Code?.Path;
            return GetOreTierFromCodePath(path);
        }

        private int GetOreTierFromCodePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;

            switch (GetOreTypeFromCodePath(path))
            {
                case "limonite":
                case "hematite":
                case "magnetite":
                case "quartz_nativesilver":
                case "quartz_nativegold":
                    return 3;

                case "pentlandite":
                case "uranium":
                case "wolframite":
                case "rhodochrosite":
                case "chromite":
                case "ilmenite":
                    return 4;

                case "nativecopper":
                case "malachite":
                case "galena":
                case "galena_nativesilver":
                case "cassiterite":
                case "sphalerite":
                case "bismuthinite":
                    return 2;

                default:
                    return 0;
            }
        }

        private string GetOreTypeFromCodePath(string path)
        {
            if (path.StartsWith("nugget-")) return path.Substring("nugget-".Length);
            if (path.StartsWith("crushed-")) return path.Substring("crushed-".Length);

            if (!path.StartsWith("ore-")) return null;
            string[] parts = path.Split('-');
            if (parts.Length < 3) return null;
            return parts[2];
        }

        private int PulverizerTier
        {
            get
            {
                switch (Blockentity.Block?.Variant?["metal"])
                {
                    case "bronze": return 3;
                    case "iron": return int.MaxValue;
                    default: return 1;
                }
            }
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
