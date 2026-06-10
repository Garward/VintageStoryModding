using System;
using System.Text;
using Cairo;
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
        private const string MiningModeAttribute = "vkDrillMiningMode";
        private const int SpinFrameCount = 32;
        private const double SpinRevolutionsPerSecond = 3.0;

        private MultiTextureMeshRef[] spinFrameMeshes;
        private SkillItem[] titaniumMiningModes;

        protected virtual string FuelDialogTitleLangCode => "vintagekinematics:powereddrill-title";
        protected virtual string NoFuelLangCode => "vintagekinematics:powereddrill-no-fuel";
        protected virtual string FlywheelPowerStatusLangCode => "vintagekinematics:poweredtool-flywheel-status";
        protected virtual string SpinningElementName => "cog-hub";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            if (api is not ICoreClientAPI capi) return;

            titaniumMiningModes = new[]
            {
                new SkillItem
                {
                    Code = new AssetLocation("vintagekinematics", "drill-1x1"),
                    Name = Lang.Get("vintagekinematics:powereddrill-mode-1x1")
                }.WithIcon(capi, (cr, x, y, w, h, c) => DrawMiningGridIcon(cr, x, y, w, h, c, 1)),
                new SkillItem
                {
                    Code = new AssetLocation("vintagekinematics", "drill-3x3"),
                    Name = Lang.Get("vintagekinematics:powereddrill-mode-3x3")
                }.WithIcon(capi, (cr, x, y, w, h, c) => DrawMiningGridIcon(cr, x, y, w, h, c, 3)),
                new SkillItem
                {
                    Code = new AssetLocation("vintagekinematics", "drill-5x5"),
                    Name = Lang.Get("vintagekinematics:powereddrill-mode-5x5")
                }.WithIcon(capi, (cr, x, y, w, h, c) => DrawMiningGridIcon(cr, x, y, w, h, c, 5))
            };
        }

        private static void DrawMiningGridIcon(Context cr, int x, int y, float width, float height, double[] rgba, int gridSize)
        {
            gridSize = Math.Max(1, Math.Min(5, gridSize));

            double r = rgba != null && rgba.Length > 0 ? rgba[0] : 1.0;
            double g = rgba != null && rgba.Length > 1 ? rgba[1] : 1.0;
            double b = rgba != null && rgba.Length > 2 ? rgba[2] : 1.0;

            double margin = gridSize == 1 ? 7.0 : 2.0;
            double gap = gridSize == 1 ? 0.0 : 1.15;
            double size = Math.Min(width, height) - margin * 2.0;
            double cell = (size - gap * (gridSize - 1)) / gridSize;
            double startX = x + (width - size) / 2.0;
            double startY = y + (height - size) / 2.0;
            int center = gridSize / 2;

            cr.Save();
            cr.LineWidth = 1.15;

            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    bool isCenter = row == center && col == center;
                    double cellX = startX + col * (cell + gap);
                    double cellY = startY + row * (cell + gap);

                    cr.Rectangle(cellX, cellY, cell, cell);
                    if (isCenter)
                    {
                        cr.SetSourceRGBA(r, g, b, 0.92);
                    }
                    else
                    {
                        cr.SetSourceRGBA(r, g, b, 0.34);
                    }
                    cr.FillPreserve();

                    cr.SetSourceRGBA(r, g, b, isCenter ? 0.95 : 0.62);
                    cr.Stroke();
                }
            }

            double markSize = Math.Max(2.5, cell * 0.34);
            double centerX = startX + center * (cell + gap) + cell / 2.0;
            double centerY = startY + center * (cell + gap) + cell / 2.0;
            cr.Arc(centerX, centerY, markSize / 2.0, 0, GameMath.TWOPI);
            cr.SetSourceRGBA(0.1, 0.08, 0.05, 0.78);
            cr.Fill();
            cr.Restore();
        }

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
                new GuiDialogPoweredDrillFuel(inv, Lang.Get(FuelDialogTitleLangCode), capi).TryOpen();
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
                    if (ItemBackpackFlywheel.TryConsumeToolPower(player, dt, out _))
                    {
                        itemslot.MarkDirty();
                        return base.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
                    }

                    burnSeconds = TryConsumeStoredFuel(player, stack);
                }

                if (burnSeconds <= 0)
                {
                    MaybeNotifyNoFuel(player, stack);
                    return remainingResistance;
                }

                itemslot.MarkDirty();
            }
            else if (!HasUsablePower(player, stack))
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

            if (titaniumMiningModes != null)
            {
                for (int i = 0; i < titaniumMiningModes.Length; i++)
                {
                    titaniumMiningModes[i]?.Dispose();
                    titaniumMiningModes[i] = null;
                }
                titaniumMiningModes = null;
            }

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
            if (!HasUsablePower(forPlayer, itemstack as ItemStack)) return 0;
            return base.GetMiningSpeed(itemstack, blockSel, block, forPlayer);
        }

        public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1f)
        {
            bool result = base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);

            if (world.Side != EnumAppSide.Server
                || blockSel?.Position == null
                || itemslot?.Itemstack == null
                || !IsTitaniumDrill(itemslot.Itemstack)
                || GetMiningModeRadius(itemslot) <= 0
                || byEntity is not EntityPlayer entityPlayer)
            {
                return result;
            }

            IPlayer player = world.PlayerByUid(entityPlayer.PlayerUID);
            if (player == null || !HasUsablePower(player, itemslot.Itemstack)) return result;

            BreakExtraMiningModeBlocks(world, player, itemslot, blockSel, dropQuantityMultiplier);
            return result;
        }

        public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
        {
            if (IsTitaniumDrill(slot?.Itemstack)) return titaniumMiningModes;
            return base.GetToolModes(slot, forPlayer, blockSel);
        }

        public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!IsTitaniumDrill(slot?.Itemstack)) return base.GetToolMode(slot, byPlayer, blockSel);
            return GameMath.Clamp(slot.Itemstack.Attributes.GetInt(MiningModeAttribute, 0), 0, 2);
        }

        public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
        {
            if (!IsTitaniumDrill(slot?.Itemstack))
            {
                base.SetToolMode(slot, byPlayer, blockSel, toolMode);
                return;
            }

            slot.Itemstack.Attributes.SetInt(MiningModeAttribute, GameMath.Clamp(toolMode, 0, 2));
            slot.MarkDirty();
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            ItemStack stack = inSlot?.Itemstack;
            if (stack == null) return;

            float burnSeconds = UpdateBurnTimer(stack);
            ItemStack storedFuel = GetStoredFuel(stack, world);

            IPlayer localPlayer = (api as ICoreClientAPI)?.World?.Player;
            float flywheelSeconds = ItemBackpackFlywheel.GetEquippedChargeSeconds(localPlayer);
            if (burnSeconds > 0)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:powereddrill-fuel-status", burnSeconds));
            }
            else if (flywheelSeconds > 0f)
            {
                dsc.AppendLine(Lang.Get(FlywheelPowerStatusLangCode, flywheelSeconds));
            }
            else if (storedFuel != null)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:powereddrill-fuel-stored", storedFuel.StackSize));
            }
            else
            {
                dsc.AppendLine(Lang.Get(NoFuelLangCode));
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            WorldInteraction[] interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-powereddrill-fuel",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));

            if (IsTitaniumDrill(inSlot?.Itemstack))
            {
                interactions = interactions.Append(new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-powereddrill-mode",
                    HotKeyCode = "toolmodeselect"
                });
            }

            return interactions;
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

        private bool HasUsablePower(IPlayer player, ItemStack stack)
        {
            return stack != null
                && (UpdateBurnTimer(stack) > 0
                    || ItemBackpackFlywheel.HasUsableCharge(player)
                    || GetStoredFuel(stack, api?.World) != null);
        }

        private void BreakExtraMiningModeBlocks(IWorldAccessor world, IPlayer player, ItemSlot itemslot, BlockSelection centerSel, float dropQuantityMultiplier)
        {
            int radius = GetMiningModeRadius(itemslot);
            BlockFacing face = centerSel.Face ?? BlockFacing.UP;

            for (int a = -radius; a <= radius; a++)
            {
                for (int b = -radius; b <= radius; b++)
                {
                    if (a == 0 && b == 0) continue;

                    BlockPos pos = OffsetInPlane(centerSel.Position, face.Axis, a, b);
                    if (!CanBreakAsExtraMiningModeBlock(world, player, itemslot, centerSel, pos)) continue;

                    world.BlockAccessor.BreakBlock(pos, player, dropQuantityMultiplier);
                    world.BlockAccessor.MarkBlockDirty(pos);
                }
            }
        }

        private bool CanBreakAsExtraMiningModeBlock(IWorldAccessor world, IPlayer player, ItemSlot itemslot, BlockSelection centerSel, BlockPos pos)
        {
            if (!world.Claims.TryAccess(player, pos, EnumBlockAccessFlags.BuildOrBreak)) return false;

            Block block = world.BlockAccessor.GetBlock(pos);
            if (block == null || block.Id == 0) return false;
            if (itemslot.Itemstack?.Collectible == null) return false;

            EnumBlockMaterial material = block.GetBlockMaterial(world.BlockAccessor, pos);
            if (MiningSpeed == null || !MiningSpeed.ContainsKey(material)) return false;
            if (block.RequiredMiningTier > 0 && ToolTier < block.RequiredMiningTier) return false;

            BlockSelection blockSel = new BlockSelection(pos, centerSel.Face ?? BlockFacing.UP, block);
            return base.GetMiningSpeed(itemslot.Itemstack, blockSel, block, player) > 0;
        }

        private static BlockPos OffsetInPlane(BlockPos origin, EnumAxis axis, int a, int b)
        {
            return axis switch
            {
                EnumAxis.X => origin.AddCopy(0, b, a),
                EnumAxis.Y => origin.AddCopy(a, 0, b),
                _ => origin.AddCopy(a, b, 0)
            };
        }

        private int GetMiningModeRadius(ItemSlot slot)
        {
            return GetToolMode(slot, null, null) switch
            {
                1 => 1,
                2 => 2,
                _ => 0
            };
        }

        private static bool IsTitaniumDrill(ItemStack stack)
        {
            return stack?.Collectible?.Code?.Path == "powereddrill-titanium";
        }

        private void MarkVisualSpinActive(ItemStack stack)
        {
            if (stack?.Attributes == null || api?.World == null) return;
            stack.Attributes.SetLong(VisualSpinUntilMsAttribute, api.World.ElapsedMilliseconds + 250);
        }

        private bool ShouldRenderSpinningCog(ItemStack stack, ICoreClientAPI capi)
        {
            if (stack?.Attributes == null || capi?.World == null) return false;

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
            serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get(NoFuelLangCode), EnumChatType.Notification);
        }
    }
}
