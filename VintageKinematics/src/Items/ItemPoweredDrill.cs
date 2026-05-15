using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace VintageKinematics.Items
{
    public class ItemPoweredDrill : Item
    {
        public const string DrillIdAttribute = "drillUid";
        private const string BurnSecondsAttribute = "drillBurnSeconds";
        private const string BurnUpdatedMsAttribute = "drillBurnUpdatedMs";
        private const string NoFuelMessageAttribute = "lastNoFuelMessageMs";
        private const string VisualSpinUntilMsAttribute = "drillVisualSpinUntilMs";
        private const string SpinningElementName = "cog-hub";
        private const int SpinFrameCount = 32;
        private const double SpinRevolutionsPerSecond = 3.0;

        private MultiTextureMeshRef[] spinFrameMeshes;

        public override string GetHeldTpHitAnimation(ItemSlot slot, Entity byEntity)
        {
            return "drillactive";
        }

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity)
        {
            return "drill";
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity is not EntityPlayer entityPlayer)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;

            IPlayer player = byEntity.World.PlayerByUid(entityPlayer.PlayerUID);
            if (player == null) return;

            UpdateBurnTimer(slot.Itemstack);
            string invId = GetOrCreateInventoryInstanceId(player, slot.Itemstack);
            slot.MarkDirty();
            var inv = new InventoryPoweredDrillFuel(invId, api);
            inv.LoadFrom(slot.Itemstack);

            if (api.Side == EnumAppSide.Server)
            {
                player.InventoryManager.OpenInventory(inv);
                return;
            }

            if (api is ICoreClientAPI capi)
            {
                new GuiDialogPoweredDrillFuel(inv, capi).TryOpen();
            }
        }

        public override float OnBlockBreaking(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
        {
            ItemStack stack = itemslot?.Itemstack;
            if (stack == null) return remainingResistance;

            if (api.Side == EnumAppSide.Server)
            {
                float burnSeconds = UpdateBurnTimer(stack);
                if (burnSeconds <= 0)
                {
                    burnSeconds = TryConsumeStoredFuel(player, stack);
                }

                if (burnSeconds <= 0)
                {
                    MaybeNotifyNoFuel(player, stack);
                    return remainingResistance;
                }

                itemslot.MarkDirty();
            }
            else if (!HasUsableFuel(stack))
            {
                return remainingResistance;
            }
            else
            {
                MarkVisualSpinActive(stack);
            }

            return base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            if (target == EnumItemRenderTarget.Gui || itemstack == null || renderinfo.ModelRef == null) return;
            if (!ShouldRenderSpinningCog(itemstack, capi)) return;

            MultiTextureMeshRef mesh = GetSpinFrameMesh(capi, itemstack);
            if (mesh != null)
            {
                renderinfo.ModelRef = mesh;
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);

            if (spinFrameMeshes == null) return;
            for (int i = 0; i < spinFrameMeshes.Length; i++)
            {
                spinFrameMeshes[i]?.Dispose();
                spinFrameMeshes[i] = null;
            }
            spinFrameMeshes = null;
        }

        public override float GetMiningSpeed(IItemStack itemstack, BlockSelection blockSel, Block block, IPlayer forPlayer)
        {
            if (!HasUsableFuel(itemstack as ItemStack)) return 0;
            return base.GetMiningSpeed(itemstack, blockSel, block, forPlayer);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;

            float burnSeconds = UpdateBurnTimer(stack);
            ItemStack storedFuel = GetStoredFuel(stack, world);

            if (burnSeconds > 0)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:powereddrill-fuel-status", burnSeconds));
            }
            else if (storedFuel != null)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:powereddrill-fuel-stored", storedFuel.StackSize));
            }
            else
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:powereddrill-no-fuel"));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-powereddrill-fuel",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }

        public static bool IsValidFuel(ItemStack stack)
        {
            if (stack?.Collectible?.CombustibleProps == null) return false;
            if (stack.Collectible.CombustibleProps.BurnDuration <= 0) return false;

            string code = stack.Collectible.Code?.ToString();
            return code == "game:charcoal"
                || code == "game:coke"
                || code == "game:ore-lignite"
                || code == "game:ore-bituminouscoal"
                || code == "game:ore-anthracite";
        }

        public static string GetOrCreateInventoryInstanceId(IPlayer player, ItemStack drillStack)
        {
            string drillId = drillStack?.Attributes?.GetString(DrillIdAttribute);
            if (string.IsNullOrEmpty(drillId))
            {
                drillId = Guid.NewGuid().ToString("N");
                drillStack?.Attributes?.SetString(DrillIdAttribute, drillId);
            }
            return GetInventoryInstanceId(player, drillId);
        }

        public static string GetInventoryInstanceId(IPlayer player, ItemStack drillStack)
        {
            string drillId = drillStack?.Attributes?.GetString(DrillIdAttribute);
            return GetInventoryInstanceId(player, drillId);
        }

        private static string GetInventoryInstanceId(IPlayer player, string drillId)
        {
            return player.PlayerUID + ":" + drillId;
        }

        private bool HasUsableFuel(ItemStack stack)
        {
            return stack != null && (UpdateBurnTimer(stack) > 0 || GetStoredFuel(stack, api?.World) != null);
        }

        private void MarkVisualSpinActive(ItemStack stack)
        {
            if (stack?.Attributes == null || api?.World == null) return;
            stack.Attributes.SetLong(VisualSpinUntilMsAttribute, api.World.ElapsedMilliseconds + 250);
        }

        private bool ShouldRenderSpinningCog(ItemStack stack, ICoreClientAPI capi)
        {
            if (stack?.Attributes == null || capi?.World == null) return false;

            if (UpdateBurnTimer(stack) > 0) return true;

            long activeUntil = stack.Attributes.GetLong(VisualSpinUntilMsAttribute, 0);
            return activeUntil > capi.World.ElapsedMilliseconds;
        }

        private MultiTextureMeshRef GetSpinFrameMesh(ICoreClientAPI capi, ItemStack stack)
        {
            if (capi?.World == null) return null;

            spinFrameMeshes ??= new MultiTextureMeshRef[SpinFrameCount];
            int frame = (int)((capi.World.ElapsedMilliseconds * SpinRevolutionsPerSecond * SpinFrameCount / 1000.0) % SpinFrameCount);
            if (frame < 0) frame += SpinFrameCount;

            return spinFrameMeshes[frame] ??= BuildSpinFrameMesh(capi, frame);
        }

        private MultiTextureMeshRef BuildSpinFrameMesh(ICoreClientAPI capi, int frame)
        {
            if (Shape?.Base == null) return null;

            AssetLocation shapeLoc = Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, shapeLoc)?.Clone();
            ShapeElement spinner = shape?.GetElementByName(SpinningElementName);
            if (spinner == null) return null;

            spinner.RotationX += 360.0 * frame / SpinFrameCount;

            capi.Tesselator.TesselateShape(this, shape, out MeshData mesh, new Vec3f());
            return mesh == null ? null : capi.Render.UploadMultiTextureMesh(mesh);
        }

        private static ItemStack GetStoredFuel(ItemStack drillStack, IWorldAccessor world = null)
        {
            ITreeAttribute invTree = drillStack?.Attributes?.GetTreeAttribute(InventoryPoweredDrillFuel.InventoryAttributeName);
            ITreeAttribute slotsTree = invTree?.GetTreeAttribute("slots");
            ItemStack stack = slotsTree?.GetItemstack("0");
            if (world != null) stack?.ResolveBlockOrItem(world);
            return stack;
        }

        private float TryConsumeStoredFuel(IPlayer player, ItemStack drillStack)
        {
            var inv = new InventoryPoweredDrillFuel(GetOrCreateInventoryInstanceId(player, drillStack), api);
            inv.LoadFrom(drillStack);

            ItemSlot fuelSlot = inv[0];
            if (!IsValidFuel(fuelSlot.Itemstack)) return 0;

            float burnSeconds = fuelSlot.Itemstack.Collectible.CombustibleProps.BurnDuration;
            fuelSlot.TakeOut(1);
            fuelSlot.MarkDirty();
            inv.SaveTo(drillStack);
            drillStack.Attributes.SetFloat(BurnSecondsAttribute, burnSeconds);
            drillStack.Attributes.SetLong(BurnUpdatedMsAttribute, api.World.ElapsedMilliseconds);
            return burnSeconds;
        }

        private float UpdateBurnTimer(ItemStack stack)
        {
            if (stack?.Attributes == null || api?.World == null) return 0;

            float burnSeconds = stack.Attributes.GetFloat(BurnSecondsAttribute, 0);
            if (burnSeconds <= 0)
            {
                stack.Attributes.SetFloat(BurnSecondsAttribute, 0);
                stack.Attributes.SetLong(BurnUpdatedMsAttribute, api.World.ElapsedMilliseconds);
                return 0;
            }

            long now = api.World.ElapsedMilliseconds;
            long last = stack.Attributes.GetLong(BurnUpdatedMsAttribute, 0);
            if (last <= 0 || last > now)
            {
                stack.Attributes.SetLong(BurnUpdatedMsAttribute, now);
                return burnSeconds;
            }

            float elapsedSeconds = (now - last) / 1000f;
            if (elapsedSeconds <= 0) return burnSeconds;

            burnSeconds = Math.Max(0, burnSeconds - elapsedSeconds);
            stack.Attributes.SetFloat(BurnSecondsAttribute, burnSeconds);
            stack.Attributes.SetLong(BurnUpdatedMsAttribute, now);
            return burnSeconds;
        }

        private void MaybeNotifyNoFuel(IPlayer player, ItemStack stack)
        {
            if (player is not IServerPlayer serverPlayer) return;

            long now = api.World.ElapsedMilliseconds;
            long last = stack.Attributes.GetLong(NoFuelMessageAttribute, 0);
            if (now - last < 1000) return;

            stack.Attributes.SetLong(NoFuelMessageAttribute, now);
            serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("vintagekinematics:powereddrill-no-fuel"), EnumChatType.Notification);
        }
    }
}
