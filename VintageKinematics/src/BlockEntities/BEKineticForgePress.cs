using System;
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
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Fueled metal press. Fuel heats the chamber, kinetic cycles form the hot input into parts,
    /// and outputs inherit the chamber temperature.
    /// </summary>
    public class BEKineticForgePress : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        public const int SlotInput = 0;
        public const int SlotFuel = 1;
        public const int SlotOutputFirst = 2;
        public const int SlotOutputLast = 10;
        public const int InventorySize = 11;
        public const int PacketIdOpenDialog = 5510;
        public const int PacketIdSelectOperation = 5511;

        private const float AmbientTemperature = 20f;
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;
        private const float HeatTickIntervalMs = 500f;
        private const float HeatRatePerSecond = 80f;
        private const float CoolRatePerSecond = 18f;
        private const float BellowsHeatRateMultiplier = 1.75f;
        private const float BellowsTemperatureBonus = 150f;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private GuiDialogKineticForgePress clientDialog;
        private float chamberTemperature = AmbientTemperature;
        private float burnSecondsRemaining;
        private float activeBurnTemperature;
        private string selectedOperationCode;
        private int pressTicksAccumulated;
        private string pressingItemCode;
        private string pressingOperationCode;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticforgepress";
        public IOFaceMap IOFaces => ioFaces;

        public BEKineticForgePress()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticforgepress-0", null, null, (slotId, self) =>
            {
                if (slotId == SlotInput) return new ItemSlotForgePressInput(self);
                if (slotId == SlotFuel) return new ItemSlotFuelOnly(self);
                return new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("kineticforgepress-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ => MarkDirty(true);
            EnsureSelectedOperation();

            BlockFacing inputFace = AutomationInputFace();
            ioFaces = new IOFaceMap(Pos)
                .MapInput(inputFace, SlotInput)
                .MapInput(BlockFacing.UP, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                RegisterGameTickListener(OnHeatTick, (int)HeatTickIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        private BlockFacing AutomationInputFace()
        {
            string axis = Block?.Variant?["axis"] ?? "x";
            return axis == "z" ? BlockFacing.EAST : BlockFacing.SOUTH;
        }

        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            foreach (BlockFacing face in ioFaces.OutputFaces)
            {
                foreach (int slotId in ioFaces.OutputSlotsFor(face))
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, Pos, face, slot, OutputPushBatch);
                    if (moved > 0) MarkDirty(true);
                }
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
                bool bellowsAssisted = HasPoweredBellowsAdjacent();
                float targetTemperature = activeBurnTemperature + (bellowsAssisted ? BellowsTemperatureBonus : 0f);
                float heatRate = HeatRatePerSecond * (bellowsAssisted ? BellowsHeatRateMultiplier : 1f);
                burnSecondsRemaining = Math.Max(0f, burnSecondsRemaining - seconds);
                chamberTemperature = Approach(chamberTemperature, targetTemperature, heatRate * seconds);
            }
            else
            {
                chamberTemperature = Approach(chamberTemperature, AmbientTemperature, CoolRatePerSecond * seconds);
                if (chamberTemperature < AmbientTemperature + 0.5f) chamberTemperature = AmbientTemperature;
            }

            HeatInputStack();
            MarkDirty(true);
        }

        private void TryConsumeFuel()
        {
            ItemSlot fuelSlot = inventory[SlotFuel];
            if (fuelSlot.Empty) return;

            ItemStack fuel = fuelSlot.Itemstack;
            CombustibleProperties props = fuel.Collectible.GetCombustibleProperties(Api.World, fuel, Pos);
            if (props == null || props.BurnDuration <= 0f || props.BurnTemperature <= 0) return;
            if (chamberTemperature >= props.BurnTemperature - 1f) return;

            fuelSlot.TakeOut(1);
            fuelSlot.MarkDirty();
            burnSecondsRemaining = props.BurnDuration;
            activeBurnTemperature = props.BurnTemperature;
        }

        private void HeatInputStack()
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty || inputSlot.Itemstack?.Collectible == null) return;
            float current = inputSlot.Itemstack.Collectible.GetTemperature(Api.World, inputSlot.Itemstack);
            float target = Math.Max(current, chamberTemperature);
            inputSlot.Itemstack.Collectible.SetTemperature(Api.World, inputSlot.Itemstack, target);
            inputSlot.MarkDirty();
        }

        private bool HasPoweredBellowsAdjacent()
        {
            foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
            {
                BlockPos pos = Pos.AddCopy(facing);
                BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
                if (be == null) continue;
                string path = be.Block?.Code?.Path ?? "";
                if (!path.Contains("bellows")) continue;

                BEBehaviorKinetic kinetic = be.GetBehavior<BEBehaviorKinetic>();
                if (kinetic != null && Math.Abs(kinetic.ActualRPM) > 0.01f) return true;
            }
            return false;
        }

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty) return;
            EnsureSelectedOperation();

            KineticForgePressRecipe recipe = CurrentRecipe();
            if (recipe == null || chamberTemperature < recipe.RequiredTemperature) return;

            int requiredQty = Math.Max(1, recipe.Ingredient?.StackSize ?? 1);
            if (inputSlot.StackSize < requiredQty) return;

            string inputCode = inputSlot.Itemstack.Collectible.Code.ToString();
            if (pressingItemCode != inputCode || pressingOperationCode != selectedOperationCode)
            {
                pressingItemCode = inputCode;
                pressingOperationCode = selectedOperationCode;
                pressTicksAccumulated = 0;
            }

            pressTicksAccumulated++;
            int effectiveTicks = Math.Max(1, recipe.PressTicks);
            if (pressTicksAccumulated < effectiveTicks)
            {
                MarkDirty(true);
                return;
            }

            string captured = null;
            if (recipe.Ingredient?.Code?.Path?.Contains('*') == true)
            {
                captured = WildcardUtil.GetWildcardValue(recipe.Ingredient.Code, inputSlot.Itemstack.Collectible.Code);
            }

            inputSlot.TakeOut(requiredQty);
            inputSlot.MarkDirty();

            if (recipe.Outputs != null)
            {
                foreach (var output in recipe.Outputs)
                {
                    if (output == null) continue;
                    ItemStack outStack = ResolveOutputStack(output, captured);
                    if (outStack == null) continue;
                    outStack.StackSize = output.StackSize;
                    outStack.Collectible.SetTemperature(Api.World, outStack, chamberTemperature);
                    DepositOutput(outStack);
                }
            }

            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/anvil"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 16, 0.6f);
            pressTicksAccumulated = 0;
            if (inputSlot.Empty) pressingItemCode = null;
            MarkDirty(true);
        }

        private KineticForgePressRecipe CurrentRecipe()
        {
            ItemSlot inputSlot = inventory[SlotInput];
            if (inputSlot.Empty) return null;
            EnsureSelectedOperation();
            return Api.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>()?.FindRecipe(inputSlot.Itemstack, selectedOperationCode);
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

        private void DepositOutput(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (!slot.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;
                int max = slot.Itemstack.Collectible.MaxStackSize;
                int free = max - slot.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = Math.Min(free, stack.StackSize);
                slot.Itemstack.StackSize += take;
                stack.StackSize -= take;
                slot.MarkDirty();
                if (stack.StackSize <= 0) return;
            }

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty) continue;
                slot.Itemstack = stack.Clone();
                slot.MarkDirty();
                return;
            }

            Api.World.SpawnItemEntity(stack, new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5));
        }

        private static float Approach(float current, float target, float delta)
        {
            if (current < target) return Math.Min(target, current + delta);
            return Math.Max(target, current - delta);
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticforgepress-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticforgepress-title") title = "Kinetic Forge Press";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                EnsureSelectedOperation();
                bw.Write(selectedOperationCode ?? "");
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(inventory);
                return;
            }
            if (packetid == PacketIdSelectOperation)
            {
                if (!CheckClaim(player)) return;
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                SetSelectedOperation(br.ReadString());
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent kinetic forge press packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        private void SetSelectedOperation(string operationCode)
        {
            var registry = Api.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>();
            if (registry != null && !registry.HasOperation(operationCode)) return;
            selectedOperationCode = operationCode ?? "";
            pressTicksAccumulated = 0;
            pressingItemCode = null;
            pressingOperationCode = null;
            MarkDirty(true);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenDialog)
            {
                base.OnReceivedServerPacket(packetid, data);
                return;
            }

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            selectedOperationCode = br.ReadString();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogKineticForgePress(title, inventory, Pos, () => selectedOperationCode, OnClientSelectOperation, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.OnOperationUpdated();
            }
        }

        private void OnClientSelectOperation(string operationCode)
        {
            selectedOperationCode = operationCode ?? "";
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(selectedOperationCode);
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSelectOperation, ms.ToArray());
            clientDialog?.OnOperationUpdated();
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            sb.AppendLine();
            sb.AppendLine(Lang.Get("vintagekinematics:kineticforgepress-heat-info", chamberTemperature));
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            tree.SetFloat("chamberTemperature", chamberTemperature);
            tree.SetFloat("burnSecondsRemaining", burnSecondsRemaining);
            tree.SetFloat("activeBurnTemperature", activeBurnTemperature);
            tree.SetString("selectedOperation", selectedOperationCode ?? "");
            tree.SetInt("pressTicks", pressTicksAccumulated);
            tree.SetString("pressingItemCode", pressingItemCode ?? "");
            tree.SetString("pressingOperationCode", pressingOperationCode ?? "");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory?.FromTreeAttributes(tree);
            chamberTemperature = tree.GetFloat("chamberTemperature", AmbientTemperature);
            burnSecondsRemaining = tree.GetFloat("burnSecondsRemaining", 0f);
            activeBurnTemperature = tree.GetFloat("activeBurnTemperature", 0f);
            selectedOperationCode = tree.GetString("selectedOperation", selectedOperationCode);
            pressTicksAccumulated = tree.GetInt("pressTicks", 0);
            pressingItemCode = tree.GetString("pressingItemCode", "");
            if (string.IsNullOrEmpty(pressingItemCode)) pressingItemCode = null;
            pressingOperationCode = tree.GetString("pressingOperationCode", "");
            if (string.IsNullOrEmpty(pressingOperationCode)) pressingOperationCode = null;
            if (Api != null)
            {
                inventory?.ResolveBlocksOrItems();
                EnsureSelectedOperation();
            }
            clientDialog?.OnOperationUpdated();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }
    }
}
