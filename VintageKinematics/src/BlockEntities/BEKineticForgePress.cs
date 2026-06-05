using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Fueled metal press. Fuel heats the chamber, kinetic cycles form the hot input into parts,
    /// and outputs inherit the chamber temperature.
    /// </summary>
    public class BEKineticForgePress : BEKineticForgePressBase
    {
        public const int SlotInput = 0;
        public const int SlotFuel = 1;
        public const int SlotDie = 11;
        public const int SlotOutputFirst = 2;
        public const int SlotOutputLast = 10;
        public const int InventorySize = 12;
        // Dialog packets are handled by BEKineticForgePressBase; this is the forge-press operation picker.
        public const int PacketIdSelectOperation = 5511;
        private const int DefaultRefractoryLiningBrickCost = 24;

        private const float DefaultOutputPushIntervalMs = 250f;
        private const int DefaultOutputPushBatch = 8;
        private const float DefaultAmbientTemperature = 20f;
        private const float DefaultHeatTickMs = 500f;
        private const float DefaultHeatRatePerSecond = 80f;
        private const float DefaultCoolRatePerSecond = 18f;
        private const float DefaultInputHeatTransferMultiplier = 1.0f;
        private const float DefaultInputCoolTransferMultiplier = 0.25f;
        private const int DefaultMaxBellowsAssistCount = 2;
        private const float DefaultBellowsTemperatureBonusPerUnit = 75f;
        private const float DefaultRefractoryLiningTemperatureBonus = 250f;
        private const float DefaultRefractoryLiningFuelDurationMultiplier = 2f;
        private const float DefaultBellowsHeatRateBonusPerUnit = 0.5f;
        private const float DefaultBellowsStackPenaltyReliefPerUnit = 0.5f;
        private const float DefaultMaxBellowsStackPenaltyRelief = 0.9f;
        private const float DefaultSmokeParticleY = 35.5f / 16f;
        private static readonly AssetLocation PressHitSound = new AssetLocation("sounds/effect/anvilhit");
        private static readonly AssetLocation PressCompleteSound = new AssetLocation("game:sounds/block/anvil");
        private static readonly Vec3d[] DefaultSmokeStackLocalPositions =
        {
            new Vec3d(-6f / 16f, DefaultSmokeParticleY, 22f / 16f),
            new Vec3d(22f / 16f, DefaultSmokeParticleY, 22f / 16f)
        };

        private ItemStackDisplayRenderer activeInputRenderer;
        private float chamberTemperature = DefaultAmbientTemperature;
        private float burnSecondsRemaining;
        private float activeBurnTemperature;
        private string selectedOperationCode;
        private int pressTicksAccumulated;
        private string pressingItemCode;
        private string pressingOperationCode;
        private string pressingDieCode;
        private bool hasTier3RefractoryLining;

        public bool HasTier3RefractoryLining => hasTier3RefractoryLining;

        public BEKineticForgePress()
            : base("kineticforgepress", InventorySize, CreateSlot)
        {
        }

        private static ItemSlot CreateSlot(int slotId, InventoryBase self)
        {
            if (slotId == SlotInput) return new ItemSlotForgePressInput(self);
            if (slotId == SlotFuel) return new ItemSlotFuelOnly(self);
            if (slotId == SlotDie) return new ItemSlotForgePressDie(self);
            return new ItemSlotCrusherOutput(self);
        }

        protected override string TitleLangCode => "vintagekinematics:kineticforgepress-title";
        protected override string FallbackTitle => "Kinetic Forge Press";
        protected override float OutputPushIntervalMs => KineticForgePressAttributes.OutputPushIntervalMs(Block, DefaultOutputPushIntervalMs);
        protected override int OutputPushBatch => KineticForgePressAttributes.OutputPushBatch(Block, DefaultOutputPushBatch);
        private float AmbientTemperature => KineticForgePressAttributes.AmbientTemperature(Block, DefaultAmbientTemperature);
        private float HeatTickMs => KineticForgePressAttributes.HeatTickMs(Block, DefaultHeatTickMs);
        private float HeatRatePerSecond => KineticForgePressAttributes.HeatRatePerSecond(Block, DefaultHeatRatePerSecond);
        private float CoolRatePerSecond => KineticForgePressAttributes.CoolRatePerSecond(Block, DefaultCoolRatePerSecond);
        private float InputHeatTransferMultiplier => KineticForgePressAttributes.InputHeatTransferMultiplier(Block, DefaultInputHeatTransferMultiplier);
        private float InputCoolTransferMultiplier => KineticForgePressAttributes.InputCoolTransferMultiplier(Block, DefaultInputCoolTransferMultiplier);
        private int MaxBellowsAssistCount => Math.Max(0, KineticForgePressAttributes.MaxBellowsAssistCount(Block, DefaultMaxBellowsAssistCount));
        private float BellowsTemperatureBonusPerUnit => KineticForgePressAttributes.BellowsTemperatureBonusPerUnit(Block, DefaultBellowsTemperatureBonusPerUnit);
        private float RefractoryLiningTemperatureBonus => KineticForgePressAttributes.RefractoryLiningTemperatureBonus(Block, DefaultRefractoryLiningTemperatureBonus);
        private float RefractoryLiningFuelDurationMultiplier => Math.Max(0.001f, KineticForgePressAttributes.RefractoryLiningFuelDurationMultiplier(Block, DefaultRefractoryLiningFuelDurationMultiplier));
        private float BellowsHeatRateBonusPerUnit => KineticForgePressAttributes.BellowsHeatRateBonusPerUnit(Block, DefaultBellowsHeatRateBonusPerUnit);
        private float BellowsStackPenaltyReliefPerUnit => KineticForgePressAttributes.BellowsStackPenaltyReliefPerUnit(Block, DefaultBellowsStackPenaltyReliefPerUnit);
        private float MaxBellowsStackPenaltyRelief => GameMath.Clamp(KineticForgePressAttributes.MaxBellowsStackPenaltyRelief(Block, DefaultMaxBellowsStackPenaltyRelief), 0f, 1f);
        private int RefractoryLiningBrickCost => Math.Max(0, KineticForgePressAttributes.RefractoryLiningBrickCost(Block, DefaultRefractoryLiningBrickCost));
        private Vec3d[] SmokeStackLocalPositions => KineticForgePressAttributes.SmokeStackLocalPositions(Block, DefaultSmokeStackLocalPositions);

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnHeatTick, (int)HeatTickMs);

            if (api is ICoreClientAPI capi)
            {
                activeInputRenderer = new ItemStackDisplayRenderer(capi, Pos, new ItemStackDisplayTransform
                {
                    Translation = new Vec3f(0.5f, 15.25f / 16f, 0.5f),
                    Scale = new Vec3f(0.4f, 0.4f, 0.4f),
                    RenderRange = 32
                });
                capi.Event.RegisterRenderer(activeInputRenderer, EnumRenderStage.Opaque);
                activeInputRenderer.UpdateStack(inventory[SlotInput]?.Itemstack);
            }
        }

        protected override void OnAfterInventoryInitialized()
        {
            EnsureSelectedOperation();
        }

        protected override void OnInventorySlotModified(int slotId)
        {
            base.OnInventorySlotModified(slotId);
            if (slotId == SlotInput && activeInputRenderer != null)
            {
                activeInputRenderer.UpdateStack(inventory[SlotInput]?.Itemstack);
            }
        }

        private BlockFacing AutomationInputFace()
        {
            string side = Block?.Variant?["side"];
            switch (side)
            {
                case "n": return BlockFacing.WEST;
                case "e": return BlockFacing.NORTH;
                case "s": return BlockFacing.EAST;
                case "w": return BlockFacing.SOUTH;
            }

            string axis = Block?.Variant?["axis"] ?? "z";
            return axis == "z" ? BlockFacing.WEST : BlockFacing.SOUTH;
        }

        protected override IOFaceMap BuildIOFaceMap()
        {
            IOFaceMap map = new IOFaceMap(Pos);
            BlockFacing inputFace = AutomationInputFace();
            BlockFacing outputFace = inputFace.Opposite;

            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size))
            {
                MapInputCell(map, Pos, inputFace);
                MapInputCell(map, Pos, BlockFacing.UP);
                MapOutputCell(map, Pos, outputFace);
                MapOutputCell(map, Pos, BlockFacing.DOWN);
                return map;
            }

            int centerX = baseCorner.X + size.X / 2;
            int centerZ = baseCorner.Z + size.Z / 2;
            int minX = baseCorner.X;
            int maxX = baseCorner.X + size.X - 1;
            int minZ = baseCorner.Z;
            int maxZ = baseCorner.Z + size.Z - 1;
            int bottomY = baseCorner.Y;
            int topY = baseCorner.Y + size.Y - 1;

            BlockPos inputCell;
            BlockPos outputCell;
            switch (inputFace.Code)
            {
                case "east":
                    inputCell = new BlockPos(maxX, bottomY, centerZ, Pos.dimension);
                    outputCell = new BlockPos(minX, bottomY, centerZ, Pos.dimension);
                    break;
                case "west":
                    inputCell = new BlockPos(minX, bottomY, centerZ, Pos.dimension);
                    outputCell = new BlockPos(maxX, bottomY, centerZ, Pos.dimension);
                    break;
                case "north":
                    inputCell = new BlockPos(centerX, bottomY, minZ, Pos.dimension);
                    outputCell = new BlockPos(centerX, bottomY, maxZ, Pos.dimension);
                    break;
                case "south":
                default:
                    inputCell = new BlockPos(centerX, bottomY, maxZ, Pos.dimension);
                    outputCell = new BlockPos(centerX, bottomY, minZ, Pos.dimension);
                    break;
            }

            MapInputCell(map, inputCell, inputFace);
            MapInputCell(map, new BlockPos(centerX, topY, centerZ, Pos.dimension), BlockFacing.UP);
            MapOutputCell(map, outputCell, outputFace);
            MapOutputCell(map, new BlockPos(centerX, bottomY, centerZ, Pos.dimension), BlockFacing.DOWN);
            return map;
        }

        private void MapInputCell(IOFaceMap map, BlockPos cell, BlockFacing face)
        {
            map.MapInput(cell, face, SlotInput);
            map.MapInput(cell, face, SlotFuel);
        }

        private void MapOutputCell(IOFaceMap map, BlockPos cell, BlockFacing face)
        {
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                map.MapOutput(cell, face, i);
            }
        }

        private void OnHeatTick(float dt)
        {
            EnsureSelectedOperation();
            float seconds = Math.Max(0.05f, dt);
            if (burnSecondsRemaining <= 0f)
            {
                TryConsumeFuel();
            }

            if (burnSecondsRemaining > 0f)
            {
                int bellowsCount = PoweredBellowsCount();
                float targetTemperature = FuelTargetTemperature(activeBurnTemperature, bellowsCount);
                float heatRate = HeatRatePerSecond * (1f + BellowsHeatRateBonusPerUnit * bellowsCount);
                float fuelUsageSpeed = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.ResolveForgePressFuelUsageSpeed() ?? 1f;
                if (hasTier3RefractoryLining) fuelUsageSpeed /= RefractoryLiningFuelDurationMultiplier;
                burnSecondsRemaining = Math.Max(0f, burnSecondsRemaining - seconds * fuelUsageSpeed);
                chamberTemperature = Approach(chamberTemperature, targetTemperature, heatRate * seconds);
                EmitHeatingSmoke();
            }
            else
            {
                chamberTemperature = Approach(chamberTemperature, AmbientTemperature, CoolRatePerSecond * seconds);
                if (chamberTemperature < AmbientTemperature + 0.5f) chamberTemperature = AmbientTemperature;
            }

            HeatInputStack(seconds);
            MarkDirty(true);
        }

        private void EmitHeatingSmoke()
        {
            if (Api.Side != EnumAppSide.Server) return;

            foreach (Vec3d local in SmokeStackLocalPositions)
            {
                Vec3d at = LocalShapePointToWorld(local);
                var particles = new SimpleParticleProperties(
                    minQuantity: 1,
                    maxQuantity: 2,
                    color: ColorUtil.ToRgba(120, 90, 90, 90),
                    minPos: at.AddCopy(-0.05, 0.0, -0.05),
                    maxPos: at.AddCopy(0.05, 0.02, 0.05),
                    minVelocity: new Vec3f(-0.015f, 0.045f, -0.015f),
                    maxVelocity: new Vec3f(0.015f, 0.085f, 0.015f),
                    lifeLength: 2.25f,
                    gravityEffect: -0.03f,
                    minSize: 0.18f,
                    maxSize: 0.34f,
                    model: EnumParticleModel.Quad
                );
                Api.World.SpawnParticles(particles);
            }
        }

        private Vec3d LocalShapePointToWorld(Vec3d local)
        {
            double x = local.X;
            double z = local.Z;
            int rotateY = (int)(Block?.Shape?.rotateY ?? 0f);
            int steps = (((rotateY / 90) % 4) + 4) % 4;
            for (int i = 0; i < steps; i++)
            {
                double dx = x - 0.5;
                double dz = z - 0.5;
                x = 0.5 + dz;
                z = 0.5 - dx;
            }
            return new Vec3d(Pos.X + x, Pos.Y + local.Y, Pos.Z + z);
        }

        private void TryConsumeFuel()
        {
            ItemSlot fuelSlot = inventory[SlotFuel];
            if (fuelSlot.Empty) return;

            ItemStack fuel = fuelSlot.Itemstack;
            CombustibleProperties props = fuel.Collectible.GetCombustibleProperties(Api.World, fuel, Pos);
            if (props == null || props.BurnDuration <= 0f || props.BurnTemperature <= 0) return;

            int bellowsCount = PoweredBellowsCount();
            float targetTemperature = FuelTargetTemperature(props.BurnTemperature, bellowsCount);
            if (chamberTemperature >= targetTemperature - 1f) return;

            fuelSlot.TakeOut(1);
            fuelSlot.MarkDirty();
            burnSecondsRemaining = props.BurnDuration;
            activeBurnTemperature = props.BurnTemperature;
        }

        private float FuelTargetTemperature(float burnTemperature, int bellowsCount)
        {
            return burnTemperature + BellowsTemperatureBonusPerUnit * bellowsCount + RefractoryLiningHeatBonus();
        }

        private float RefractoryLiningHeatBonus()
        {
            return hasTier3RefractoryLining ? RefractoryLiningTemperatureBonus : 0f;
        }

        public bool TryUpgradeRefractoryLining(IPlayer byPlayer)
        {
            if (byPlayer == null) return false;

            ItemSlot slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack stack = slot?.Itemstack;
            if (!IsTier3RefractoryBrick(stack)) return false;

            if (hasTier3RefractoryLining)
            {
                if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("vintagekinematics:kineticforgepress-lining-already"), EnumChatType.Notification);
                }
                return true;
            }

            bool isCreative = byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative;
            if (!isCreative && stack.StackSize < RefractoryLiningBrickCost)
            {
                if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer serverPlayer)
                {
                    serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("vintagekinematics:kineticforgepress-lining-needs", RefractoryLiningBrickCost), EnumChatType.Notification);
                }
                return true;
            }

            if (Api.Side != EnumAppSide.Server) return true;

            if (!Api.World.Claims.TryAccess(byPlayer, Pos, EnumBlockAccessFlags.Use)) return true;

            if (!isCreative)
            {
                slot.TakeOut(RefractoryLiningBrickCost);
                slot.MarkDirty();
            }

            hasTier3RefractoryLining = true;
            MarkDirty(true);

            Api.World.PlaySoundAt(new AssetLocation("sounds/block/ceramicplace"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16f, 0.9f);
            if (byPlayer is IServerPlayer sp)
            {
                sp.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("vintagekinematics:kineticforgepress-lining-upgraded"), EnumChatType.Notification);
            }

            return true;
        }

        private static bool IsTier3RefractoryBrick(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            return code != null && code.Domain == "game" && code.Path == "refractorybrick-fired-tier3";
        }

        private void HeatInputStack(float seconds)
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty || inputSlot.Itemstack?.Collectible == null) return;

            float current = inputSlot.Itemstack.Collectible.GetTemperature(Api.World, inputSlot.Itemstack);
            float target = chamberTemperature;
            if (Math.Abs(current - target) < 0.5f) return;

            float transferSeconds = seconds * (current < target ? InputHeatTransferMultiplier : InputCoolTransferMultiplier);
            transferSeconds *= 1f + GameMath.Clamp(Math.Abs(target - current) / 30f, 0f, 1.6f);

            float newTemperature = ChangeItemTemperature(current, target, transferSeconds);
            if (current < target)
            {
                float effectiveStackSize = EffectiveHeatMass(inputSlot.Itemstack, PoweredBellowsCount());
                newTemperature = (newTemperature + (effectiveStackSize - 1f) * current) / effectiveStackSize;
            }

            int maxTemperature = MaxItemTemperature(inputSlot.Itemstack);
            if (maxTemperature > 0) newTemperature = Math.Min(maxTemperature, newTemperature);

            inputSlot.Itemstack.Collectible.SetTemperature(Api.World, inputSlot.Itemstack, newTemperature);
            inputSlot.MarkDirty();
        }

        private int PoweredBellowsCount()
        {
            int count = 0;

            if (MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size))
            {
                int minX = baseCorner.X;
                int maxX = baseCorner.X + size.X - 1;
                int minY = baseCorner.Y;
                int maxY = baseCorner.Y + size.Y - 1;
                int minZ = baseCorner.Z;
                int maxZ = baseCorner.Z + size.Z - 1;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (IsPoweredBellowsAt(new BlockPos(x, y, minZ - 1, Pos.dimension)) && ++count >= MaxBellowsAssistCount) return count;
                        if (IsPoweredBellowsAt(new BlockPos(x, y, maxZ + 1, Pos.dimension)) && ++count >= MaxBellowsAssistCount) return count;
                    }

                    for (int z = minZ; z <= maxZ; z++)
                    {
                        if (IsPoweredBellowsAt(new BlockPos(minX - 1, y, z, Pos.dimension)) && ++count >= MaxBellowsAssistCount) return count;
                        if (IsPoweredBellowsAt(new BlockPos(maxX + 1, y, z, Pos.dimension)) && ++count >= MaxBellowsAssistCount) return count;
                    }
                }

                return count;
            }

            foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
            {
                if (IsPoweredBellowsAt(Pos.AddCopy(facing)) && ++count >= MaxBellowsAssistCount) return count;
            }
            return count;
        }

        private float EffectiveHeatMass(ItemStack stack, int bellowsCount)
        {
            float mass = StackHeatUnits(stack);
            float relief = MathF.Min(MaxBellowsStackPenaltyRelief, GameMath.Clamp(bellowsCount, 0, MaxBellowsAssistCount) * BellowsStackPenaltyReliefPerUnit);
            float reducedMass = MathF.Sqrt(mass);
            return mass + (reducedMass - mass) * relief;
        }

        private float StackHeatUnits(ItemStack stack)
        {
            if (stack == null) return 1f;

            float units = Math.Max(1f, stack.StackSize);
            CombustibleProperties props = stack.Collectible.GetCombustibleProperties(Api.World, stack, Pos);
            if (props?.SmeltedStack?.ResolvedItemstack != null && props.SmeltedRatio > 1)
            {
                units = stack.StackSize / (float)props.SmeltedRatio;
            }

            return Math.Max(1f, units);
        }

        private bool IsPoweredBellowsAt(BlockPos pos)
        {
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return false;

            string path = be.Block?.Code?.Path ?? "";
            if (!path.Contains("bellows")) return false;

            BEBehaviorKinetic kinetic = be.GetBehavior<BEBehaviorKinetic>();
            return kinetic != null && Math.Abs(kinetic.ActualRPM) >= KineticNetwork.MinAbsRPM;
        }

        protected override void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty) return;
            EnsureSelectedOperation();

            KineticForgePressRecipe recipe = CurrentRecipe();
            if (recipe == null) return;

            float inputTemperature = inputSlot.Itemstack.Collectible.GetTemperature(Api.World, inputSlot.Itemstack);
            if (inputTemperature < RequiredTemperatureFor(recipe, inputSlot.Itemstack))
            {
                ResetPressProgress();
                return;
            }

            int requiredQty = RequiredQuantityFor(recipe, inputSlot.Itemstack);
            if (inputSlot.StackSize < requiredQty)
            {
                ResetPressProgress();
                return;
            }

            string inputCode = inputSlot.Itemstack.Collectible.Code.ToString();
            string dieCode = inventory[SlotDie].Itemstack?.Collectible?.Code?.ToString() ?? "";
            if (pressingItemCode != inputCode || pressingOperationCode != selectedOperationCode || pressingDieCode != dieCode)
            {
                pressingItemCode = inputCode;
                pressingOperationCode = selectedOperationCode;
                pressingDieCode = dieCode;
                pressTicksAccumulated = 0;
            }

            pressTicksAccumulated++;
            int effectiveTicks = Math.Max(1, recipe.PressTicks);
            if (pressTicksAccumulated < effectiveTicks)
            {
                Api.World.PlaySoundAt(PressHitSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 14, 0.35f);
                MarkDirty(true);
                return;
            }

            string captured = null;
            if (recipe.Ingredient?.Code?.Path?.Contains('*') == true)
            {
                captured = WildcardUtil.GetWildcardValue(recipe.Ingredient.Code, inputSlot.Itemstack.Collectible.Code);
            }

            List<ItemStack> outputs = ResolveOutputStacks(recipe, captured, inputTemperature);
            if (RecipeShouldProduceOutput(recipe) && outputs.Count == 0)
            {
                Api.World.Logger.Warning("[VintageKinematics] Forge press recipe '{0}' matched input '{1}' but produced no resolvable outputs. Input was not consumed.",
                    recipe.OperationCode, inputSlot.Itemstack.Collectible.Code);
                ResetPressProgress();
                return;
            }

            inputSlot.TakeOut(requiredQty);
            inputSlot.MarkDirty();

            foreach (ItemStack outStack in outputs)
            {
                DepositOutput(outStack);
            }

            Api.World.PlaySoundAt(PressCompleteSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 16, 0.6f);
            pressTicksAccumulated = 0;
            if (inputSlot.Empty) pressingItemCode = null;
            MarkDirty(true);
        }

        private void ResetPressProgress()
        {
            if (pressTicksAccumulated == 0 && pressingItemCode == null && pressingOperationCode == null && pressingDieCode == null) return;

            pressTicksAccumulated = 0;
            pressingItemCode = null;
            pressingOperationCode = null;
            pressingDieCode = null;
            MarkDirty(true);
        }

        private KineticForgePressRecipe CurrentRecipe()
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty) return null;
            EnsureSelectedOperation();
            return Api.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>()?.FindRecipe(inputSlot.Itemstack, selectedOperationCode, inventory[SlotDie].Itemstack);
        }

        private void EnsureSelectedOperation()
        {
            var registry = Api?.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>();
            if (registry == null) return;
            if (registry.HasOperation(selectedOperationCode)) return;
            selectedOperationCode = registry.FirstOperationCode();
        }

        private ItemStack ResolveOutputStack(JsonItemStack output, string captured)
        {
            if (captured != null && output.Code?.Path?.Contains('*') == true)
            {
                AssetLocation substituted = new AssetLocation(output.Code.Domain, output.Code.Path.Replace("*", captured));
                if (output.Type == EnumItemClass.Block)
                {
                    Block block = Api.World.GetBlock(substituted);
                    return block == null ? null : new ItemStack(block, 1);
                }

                Item item = Api.World.GetItem(substituted);
                return item == null ? null : new ItemStack(item, 1);
            }
            return output.ResolvedItemstack?.Clone();
        }

        private List<ItemStack> ResolveOutputStacks(KineticForgePressRecipe recipe, string captured, float inputTemperature)
        {
            List<ItemStack> outputs = new List<ItemStack>();
            AddInputSmeltedOutput(recipe, inputTemperature, outputs);
            if (recipe?.Outputs == null) return outputs;

            foreach (JsonItemStack output in recipe.Outputs)
            {
                if (output == null) continue;
                ItemStack outStack = ResolveOutputStack(output, captured);
                if (outStack == null)
                {
                    Api.World.Logger.Warning("[VintageKinematics] Forge press recipe '{0}' output '{1}' could not be resolved.",
                        recipe.OperationCode, output.Code);
                    continue;
                }

                outStack.StackSize = Math.Max(1, output.StackSize);
                outStack.Collectible.SetTemperature(Api.World, outStack, inputTemperature);
                outputs.Add(outStack);
            }

            return outputs;
        }

        private void AddInputSmeltedOutput(KineticForgePressRecipe recipe, float inputTemperature, List<ItemStack> outputs)
        {
            if (recipe?.UseInputSmeltedStack != true) return;

            ItemStack input = inventory[SlotInput]?.Itemstack;
            CombustibleProperties props = input?.Collectible?.GetCombustibleProperties(Api.World, input, Pos);
            ItemStack smelted = props?.SmeltedStack?.ResolvedItemstack?.Clone();
            if (smelted == null) return;

            smelted.StackSize = Math.Max(1, smelted.StackSize);
            smelted.Collectible.SetTemperature(Api.World, smelted, inputTemperature);
            outputs.Add(smelted);
        }

        private bool RecipeShouldProduceOutput(KineticForgePressRecipe recipe)
        {
            return recipe?.UseInputSmeltedStack == true || (recipe?.Outputs != null && recipe.Outputs.Length > 0);
        }

        private int RequiredQuantityFor(KineticForgePressRecipe recipe, ItemStack input)
        {
            if (recipe?.UseInputSmeltedStack == true)
            {
                CombustibleProperties props = input?.Collectible?.GetCombustibleProperties(Api.World, input, Pos);
                if (props?.SmeltedStack?.ResolvedItemstack != null && props.SmeltedRatio > 0)
                {
                    return Math.Max(1, props.SmeltedRatio);
                }
            }

            return Math.Max(1, recipe?.Ingredient?.StackSize ?? 1);
        }

        private float RequiredTemperatureFor(KineticForgePressRecipe recipe, ItemStack input)
        {
            if (recipe?.UseInputSmeltedStack == true && recipe.UseInputSmeltingTemperature)
            {
                CombustibleProperties props = input?.Collectible?.GetCombustibleProperties(Api.World, input, Pos);
                if (props != null && props.MeltingPoint > 0)
                {
                    return props.MeltingPoint;
                }
            }

            return recipe?.RequiredTemperature ?? 0f;
        }

        private void DepositOutput(ItemStack stack)
        {
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, inventory, SlotOutputFirst, SlotOutputLast, stack, MachineIOFaces?.OutputEntries, OutputPushBatch, at);
        }

        private static float Approach(float current, float target, float delta)
        {
            if (current < target) return Math.Min(target, current + delta);
            return Math.Max(target, current - delta);
        }

        private static float ChangeItemTemperature(float fromTemp, float toTemp, float seconds)
        {
            float diff = Math.Abs(fromTemp - toTemp);
            float delta = seconds + seconds * (diff / 28f);
            if (diff < delta || diff < 1f) return toTemp;
            return fromTemp > toTemp ? fromTemp - delta : fromTemp + delta;
        }

        private int MaxItemTemperature(ItemStack stack)
        {
            if (stack == null) return 0;
            int combustibleMax = stack.Collectible.GetCombustibleProperties(Api.World, stack, null)?.MaxTemperature ?? 0;
            int attributeMax = stack.ItemAttributes?["maxTemperature"].AsInt(0) ?? 0;
            return Math.Max(combustibleMax, attributeMax);
        }

        protected override bool HandleCustomClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid != PacketIdSelectOperation) return false;
            if (!CheckClaim(player)) return true;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            SetSelectedOperation(br.ReadString());
            return true;
        }

        private void SetSelectedOperation(string operationCode)
        {
            var registry = Api.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>();
            if (registry != null && !registry.HasOperation(operationCode)) return;
            selectedOperationCode = operationCode ?? "";
            pressTicksAccumulated = 0;
            pressingItemCode = null;
            pressingOperationCode = null;
            pressingDieCode = null;
            MarkDirty(true);
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticForgePress(title, inventory, Pos, () => selectedOperationCode, CurrentPressProgress, CurrentPressProgressMax, CanPressCurrentRecipe, OnClientSelectOperation, capi);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog)
        {
            (dialog as GuiDialogKineticForgePress)?.OnOperationUpdated();
        }

        private void OnClientSelectOperation(string operationCode)
        {
            selectedOperationCode = operationCode ?? "";
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(selectedOperationCode);
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSelectOperation, ms.ToArray());
            RefreshClientDialog();
        }

        private float CurrentPressProgress()
        {
            return Math.Max(0, pressTicksAccumulated);
        }

        private float CurrentPressProgressMax()
        {
            KineticForgePressRecipe recipe = CurrentRecipe();
            return Math.Max(1, recipe?.PressTicks ?? 1);
        }

        private bool CanPressCurrentRecipe()
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty || inputSlot.Itemstack?.Collectible == null) return false;

            KineticForgePressRecipe recipe = CurrentRecipe();
            if (recipe == null) return false;

            int requiredQty = RequiredQuantityFor(recipe, inputSlot.Itemstack);
            if (inputSlot.StackSize < requiredQty) return false;

            float inputTemperature = inputSlot.Itemstack.Collectible.GetTemperature(Api.World, inputSlot.Itemstack);
            if (inputTemperature < RequiredTemperatureFor(recipe, inputSlot.Itemstack)) return false;

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return false;
            if (kinetic.IsConflicted || (kinetic.EffectiveNetwork?.IsOverstressed ?? false)) return false;

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            float minRpm = Math.Max(0.01f, worker?.MinRPM ?? 0.01f);
            return Math.Abs(kinetic.CurrentRPM) >= minRpm;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            sb.AppendLine();
            sb.AppendLine(Lang.Get("vintagekinematics:kineticforgepress-heat-info", chamberTemperature));
            int bellowsCount = PoweredBellowsCount();
            sb.AppendLine($"Bellows heat boost: {bellowsCount}/{MaxBellowsAssistCount}");
            sb.AppendLine(hasTier3RefractoryLining
                ? Lang.Get("vintagekinematics:kineticforgepress-lining-active")
                : Lang.Get("vintagekinematics:kineticforgepress-lining-missing", RefractoryLiningBrickCost));
        }

        protected override void WriteState(ITreeAttribute tree)
        {
            tree.SetFloat("chamberTemperature", chamberTemperature);
            tree.SetFloat("burnSecondsRemaining", burnSecondsRemaining);
            tree.SetFloat("activeBurnTemperature", activeBurnTemperature);
            tree.SetString("selectedOperation", selectedOperationCode ?? "");
            tree.SetInt("pressTicks", pressTicksAccumulated);
            tree.SetString("pressingItemCode", pressingItemCode ?? "");
            tree.SetString("pressingOperationCode", pressingOperationCode ?? "");
            tree.SetString("pressingDieCode", pressingDieCode ?? "");
            tree.SetBool("tier3RefractoryLining", hasTier3RefractoryLining);
        }

        protected override void ReadState(ITreeAttribute tree)
        {
            chamberTemperature = tree.GetFloat("chamberTemperature", AmbientTemperature);
            burnSecondsRemaining = tree.GetFloat("burnSecondsRemaining", 0f);
            activeBurnTemperature = tree.GetFloat("activeBurnTemperature", 0f);
            selectedOperationCode = tree.GetString("selectedOperation", selectedOperationCode);
            pressTicksAccumulated = tree.GetInt("pressTicks", 0);
            pressingItemCode = tree.GetString("pressingItemCode", "");
            if (string.IsNullOrEmpty(pressingItemCode)) pressingItemCode = null;
            pressingOperationCode = tree.GetString("pressingOperationCode", "");
            if (string.IsNullOrEmpty(pressingOperationCode)) pressingOperationCode = null;
            pressingDieCode = tree.GetString("pressingDieCode", "");
            if (string.IsNullOrEmpty(pressingDieCode)) pressingDieCode = null;
            hasTier3RefractoryLining = tree.GetBool("tier3RefractoryLining", false);
        }

        protected override void OnAfterStateRead()
        {
            activeInputRenderer?.UpdateStack(inventory[SlotInput]?.Itemstack);
            RefreshClientDialog();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeActiveInputRenderer();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeActiveInputRenderer();
        }

        private void DisposeActiveInputRenderer()
        {
            if (Api is ICoreClientAPI capi && activeInputRenderer != null)
            {
                capi.Event.UnregisterRenderer(activeInputRenderer, EnumRenderStage.Opaque);
                activeInputRenderer.Dispose();
                activeInputRenderer = null;
            }
        }
    }
}
