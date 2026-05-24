using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Network;

namespace VintageKinematics.Items
{
    public class ItemBackpackFlywheel : Item, IContainedCustomName
    {
        private const string StoredSecondsAttribute = "vkFlywheelStoredSeconds";
        private const string LastChargeStepAttribute = "vkFlywheelLastChargeStep";
        private const string LastAutomaticChargeMsAttribute = "vkFlywheelLastAutomaticChargeMs";
        private const float MaxStoredSeconds = 180f;
        private const float ChargeStress = 64f;
        private const float DischargeStress = 64f;
        private const float ChargeEfficiency = 0.75f;
        private const float MaxOutputRPM = 16f;
        public static float MaxStoredSecondsValue => MaxStoredSeconds;

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity is not EntityPlayer || byEntity.Controls?.Sneak != true || blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            if (!CanChargeFromKinetic(slot, byEntity.World, blockSel))
            {
                if (TryPlaceKineticBackpack(slot, byEntity, blockSel))
                {
                    handling = EnumHandHandling.PreventDefault;
                    return;
                }

                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            slot.Itemstack.Attributes.SetFloat(LastChargeStepAttribute, 0f);
            handling = EnumHandHandling.PreventDefault;
        }

        private static bool TryPlaceKineticBackpack(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel)
        {
            if (slot?.Itemstack == null || byEntity is not EntityPlayer entityPlayer || blockSel?.Face != BlockFacing.UP) return false;

            IWorldAccessor world = byEntity.World;
            IPlayer player = world.PlayerByUid(entityPlayer.PlayerUID);
            if (player == null) return false;

            BlockPos targetPos = blockSel.Position.AddCopy(blockSel.Face);
            if (!world.Claims.TryAccess(player, targetPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                slot.MarkDirty();
                return false;
            }

            Block support = world.BlockAccessor.GetBlock(targetPos.DownCopy());
            Block placedBase = world.GetBlock(new AssetLocation("vintagekinematics", "backpackflywheelplaced-s"));
            if (placedBase == null || !support.CanAttachBlockAt(world.BlockAccessor, placedBase, targetPos.DownCopy(), BlockFacing.UP)) return false;
            if (world.BlockAccessor.GetBlock(targetPos).Replaceable < 6000) return false;

            string side = SideCode(BlockFacing.HorizontalFromYaw(entityPlayer.Pos.Yaw)) ?? "s";
            Block placed = world.GetBlock(new AssetLocation("vintagekinematics", "backpackflywheelplaced-" + side)) ?? placedBase;

            if (world.Side == EnumAppSide.Server)
            {
                ItemStack storedStack = slot.Itemstack.Clone();
                storedStack.StackSize = 1;

                BlockSelection placeSel = blockSel.Clone();
                placeSel.Position = targetPos;
                string failure = null;
                if (!placed.TryPlaceBlock(world, player, new ItemStack(placed), placeSel, ref failure)) return false;

                if (world.BlockAccessor.GetBlockEntity(targetPos) is BEBackpackFlywheelPlaced be)
                {
                    be.SetStoredStack(storedStack);
                }

                slot.TakeOut(1);
                slot.MarkDirty();
            }

            return true;
        }

        private static string SideCode(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST) return "w";
            return null;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (slot?.Itemstack == null || byEntity?.Controls?.Sneak != true || blockSel == null) return false;

            float last = slot.Itemstack.Attributes.GetFloat(LastChargeStepAttribute, 0f);
            float dt = MathF.Max(0f, secondsUsed - last);
            slot.Itemstack.Attributes.SetFloat(LastChargeStepAttribute, secondsUsed);
            if (dt <= 0f) return true;

            string key = $"backpackflywheel-held-{(byEntity as EntityPlayer)?.PlayerUID ?? "unknown"}";
            return TryChargeSlotFromKinetic(slot, byEntity.World, blockSel, dt, key);
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            return true;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("vintagekinematics:backpackflywheel-charge", GetStoredSeconds(inSlot?.Itemstack), MaxStoredSeconds, ChargePercent(inSlot?.Itemstack)));
        }

        public string GetContainedInfo(ItemSlot inSlot)
        {
            return Lang.Get(
                "vintagekinematics:backpackflywheel-contained-info",
                inSlot?.Itemstack?.GetName() ?? Lang.Get("unknown"),
                GetStoredSeconds(inSlot?.Itemstack),
                MaxStoredSeconds,
                ChargePercent(inSlot?.Itemstack)
            );
        }

        public string GetContainedName(ItemSlot inSlot, int quantity)
        {
            return null;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-backpackflywheel-charge",
                    HotKeyCode = "sneak",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }

        public static bool HasUsableCharge(IPlayer player)
        {
            return FindEquippedFlywheelSlot(player)?.Itemstack is ItemStack stack && GetStoredSeconds(stack) > 0.05f;
        }

        public static bool TryConsumeToolPower(IPlayer player, float seconds, out float remainingSeconds)
        {
            remainingSeconds = 0f;
            ItemSlot slot = FindEquippedFlywheelSlot(player);
            ItemStack stack = slot?.Itemstack;
            if (stack == null) return false;

            float stored = GetStoredSeconds(stack);
            if (stored <= 0.05f) return false;

            stored = MathF.Max(0f, stored - MathF.Max(0.02f, seconds));
            SetStoredSeconds(stack, stored);
            remainingSeconds = stored;
            slot.MarkDirty();
            return true;
        }

        public static float GetEquippedChargeSeconds(IPlayer player)
        {
            ItemStack stack = FindEquippedFlywheelSlot(player)?.Itemstack;
            return GetStoredSeconds(stack);
        }

        public static bool CanChargeEquippedFromKinetic(IPlayer player, IWorldAccessor world, BlockSelection blockSel)
        {
            return CanChargeFromKinetic(FindEquippedFlywheelSlot(player), world, blockSel);
        }

        public static bool TryChargeEquippedFromKinetic(IPlayer player, IWorldAccessor world, BlockSelection blockSel, float dt)
        {
            ItemSlot slot = FindEquippedFlywheelSlot(player);
            string key = $"backpackflywheel-equipped-{player?.PlayerUID ?? "unknown"}";
            return TryChargeSlotFromKinetic(slot, world, blockSel, dt, key);
        }

        public static bool CanChargeSlotFromKinetic(ItemSlot slot, IWorldAccessor world, BlockPos kineticPos)
        {
            if (!IsBackpackFlywheelSlot(slot) || GetStoredSeconds(slot.Itemstack) >= MaxStoredSeconds - 0.001f) return false;
            return TryGetChargeRPM(world, kineticPos, null, out _);
        }

        public static bool TryChargeSlotFromKinetic(ItemSlot slot, IWorldAccessor world, BlockPos kineticPos, float dt, string loadKey, bool throttleByWorldTime = false)
        {
            if (!IsBackpackFlywheelSlot(slot) || world == null || kineticPos == null) return false;
            if (GetStoredSeconds(slot.Itemstack) >= MaxStoredSeconds - 0.001f) return false;

            float chargeDt = MathF.Max(0f, dt);
            if (throttleByWorldTime)
            {
                long now = world.ElapsedMilliseconds;
                long lastMs = slot.Itemstack.Attributes.GetLong(LastAutomaticChargeMsAttribute, 0);
                chargeDt = lastMs > 0 ? MathF.Min(1f, MathF.Max(0f, (now - lastMs) / 1000f)) : chargeDt;
                if (chargeDt <= 0.02f) return GetStoredSeconds(slot.Itemstack) < MaxStoredSeconds - 0.001f;
                slot.Itemstack.Attributes.SetLong(LastAutomaticChargeMsAttribute, now);
            }

            if (!TryGetChargeRPM(world, kineticPos, loadKey, out float rpm)) return false;

            float inputPower = ChargeStress * rpm;
            float ratedOutputPower = DischargeStress * MaxOutputRPM;
            float secondsGained = ratedOutputPower > 0f ? inputPower / ratedOutputPower * ChargeEfficiency * chargeDt : 0f;
            AddCharge(slot.Itemstack, secondsGained);
            slot.MarkDirty();
            return GetStoredSeconds(slot.Itemstack) < MaxStoredSeconds - 0.001f;
        }

        private static bool CanChargeFromKinetic(ItemSlot slot, IWorldAccessor world, BlockSelection blockSel)
        {
            if (!IsBackpackFlywheelSlot(slot) || GetStoredSeconds(slot.Itemstack) >= MaxStoredSeconds - 0.001f) return false;
            return TryGetChargeRPM(world, blockSel, null, out _);
        }

        private static bool TryChargeSlotFromKinetic(ItemSlot slot, IWorldAccessor world, BlockSelection blockSel, float dt, string loadKey)
        {
            if (!IsBackpackFlywheelSlot(slot) || blockSel == null) return false;
            if (GetStoredSeconds(slot.Itemstack) >= MaxStoredSeconds - 0.001f) return false;
            if (!TryGetChargeRPM(world, blockSel, loadKey, out float rpm)) return false;

            float inputPower = ChargeStress * rpm;
            float ratedOutputPower = DischargeStress * MaxOutputRPM;
            float secondsGained = ratedOutputPower > 0f ? inputPower / ratedOutputPower * ChargeEfficiency * MathF.Max(0f, dt) : 0f;
            AddCharge(slot.Itemstack, secondsGained);
            slot.MarkDirty();
            return GetStoredSeconds(slot.Itemstack) < MaxStoredSeconds - 0.001f;
        }

        private static ItemSlot FindEquippedFlywheelSlot(IPlayer player)
        {
            IInventory backpackInventory = player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
            ItemSlot backpackSlot = FindFlywheelBackpackSlot(backpackInventory, requireBackpackSlot: true);
            if (backpackSlot != null) return backpackSlot;

            IInventory characterInventory = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            return FindFlywheelBackpackSlot(characterInventory, requireBackpackSlot: false);
        }

        private static ItemSlot FindFlywheelBackpackSlot(IInventory inventory, bool requireBackpackSlot)
        {
            if (inventory == null) return null;

            int count;
            try
            {
                count = inventory.Count;
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < count; i++)
            {
                ItemSlot slot;
                try
                {
                    slot = inventory[i];
                }
                catch
                {
                    continue;
                }

                if (requireBackpackSlot && slot is not ItemSlotBackpack) continue;
                if (IsBackpackFlywheelSlot(slot)) return slot;
            }

            return null;
        }

        private static bool IsBackpackFlywheelSlot(ItemSlot slot)
        {
            return slot?.Itemstack?.Collectible is ItemBackpackFlywheel;
        }

        private static bool TryGetChargeRPM(IWorldAccessor world, BlockSelection blockSel, string loadKey, out float rpm)
        {
            rpm = 0f;
            if (world == null || blockSel?.Position == null) return false;

            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position)
                ?? world.BlockAccessor.GetBlockEntity(blockSel.Position);
            return TryGetChargeRPM(world, be?.Pos ?? blockSel.Position, loadKey, out rpm);
        }

        private static bool TryGetChargeRPM(IWorldAccessor world, BlockPos kineticPos, string loadKey, out float rpm)
        {
            rpm = 0f;
            if (world == null || kineticPos == null) return false;

            if (world.Side == EnumAppSide.Server && !string.IsNullOrEmpty(loadKey))
            {
                KineticNetworkManager manager = world.Api?.ModLoader.GetModSystem<KineticNetworkManager>();
                return manager != null && manager.TryApplyTransientStress(kineticPos, loadKey, ChargeStress, out rpm);
            }

            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, kineticPos)
                ?? world.BlockAccessor.GetBlockEntity(kineticPos);
            BEBehaviorKinetic kinetic = be?.GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null || MathF.Abs(kinetic.ActualRPM) < KineticNetwork.MinAbsRPM) return false;

            rpm = MathF.Abs(kinetic.ActualRPM);
            return true;
        }

        public static bool AddCharge(ItemStack stack, float seconds)
        {
            if (stack?.Attributes == null) return false;
            float before = GetStoredSeconds(stack);
            SetStoredSeconds(stack, GetStoredSeconds(stack) + MathF.Max(0f, seconds));
            return MathF.Abs(GetStoredSeconds(stack) - before) > 0.001f;
        }

        private static void SetStoredSeconds(ItemStack stack, float seconds)
        {
            stack?.Attributes?.SetFloat(StoredSecondsAttribute, GameMath.Clamp(seconds, 0f, MaxStoredSeconds));
        }

        public static float GetStoredSeconds(ItemStack stack)
        {
            return GameMath.Clamp(stack?.Attributes?.GetFloat(StoredSecondsAttribute, 0f) ?? 0f, 0f, MaxStoredSeconds);
        }

        private static float ChargePercent(ItemStack stack)
        {
            return MaxStoredSeconds > 0f ? GetStoredSeconds(stack) / MaxStoredSeconds * 100f : 0f;
        }
    }
}
