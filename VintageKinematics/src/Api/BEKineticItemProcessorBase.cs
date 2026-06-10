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
using Vintagestory.GameContent;
using VintageKinematics.BlockEntities;
using VintageKinematics.Rendering;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared shell for one-input kinetic item processors: inventory, IO map, output draining,
    /// worker subscription, dialog packet flow, claim-safe inventory packets, and save/load.
    /// </summary>
    public abstract class BEKineticItemProcessorBase<TRecipe> : BlockEntityOpenableContainer, IFaceMappedContainer, IKineticWorkTooltipProvider
    {
        private readonly string inventoryClassName;
        private readonly int inputFirst;
        private readonly int inputLast;
        private readonly int outputFirst;
        private readonly int outputLast;
        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private GuiDialogBlockEntity clientDialog;

        protected BEKineticItemProcessorBase(string inventoryClassName, int inventorySize, int inputSlot, int outputFirst, int outputLast)
            : this(inventoryClassName, inventorySize, inputSlot, inputSlot, outputFirst, outputLast)
        {
        }

        protected BEKineticItemProcessorBase(string inventoryClassName, int inventorySize, int inputFirst, int inputLast, int outputFirst, int outputLast)
            : this(inventoryClassName, inventorySize, inputFirst, inputLast, outputFirst, outputLast, null)
        {
        }

        protected BEKineticItemProcessorBase(
            string inventoryClassName,
            int inventorySize,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast,
            System.Func<int, InventoryBase, ItemSlot> slotFactory)
        {
            this.inventoryClassName = inventoryClassName;
            this.inputFirst = inputFirst;
            this.inputLast = inputLast;
            this.outputFirst = outputFirst;
            this.outputLast = outputLast;

            inventory = new InventoryGeneric(inventorySize, inventoryClassName + "-0", null, null, (slotId, self) =>
            {
                if (slotFactory != null) return slotFactory(slotId, self);
                return slotId >= inputFirst && slotId <= inputLast ? new ItemSlot(self) : new ItemSlotCrusherOutput(self);
            });
        }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => inventoryClassName;
        public IOFaceMap IOFaces => ioFaces;

        protected InventoryGeneric MachineInventory => inventory;
        protected IOFaceMap MachineIOFaces => ioFaces;
        protected int InputFirst => inputFirst;
        protected int InputLast => inputLast;
        protected int InputSlot => inputFirst;
        protected int OutputFirst => outputFirst;
        protected int OutputLast => outputLast;
        protected virtual int ActiveInputFirst => inputFirst;
        protected virtual int ActiveInputLast => inputLast;
        protected virtual int ActiveOutputFirst => outputFirst;
        protected virtual int ActiveOutputLast => outputLast;

        protected virtual int OpenDialogPacketId => 5400;
        protected virtual float OutputPushIntervalMs => 250f;
        protected virtual int OutputPushBatch => 8;
        protected virtual int InputQuantityPerCycle(TRecipe recipe, ItemStack input) => 1;
        protected virtual AssetLocation WorkSound => null;
        protected virtual float WorkSoundVolume => 0.6f;
        protected virtual string TitleLangCode => "vintagekinematics:" + inventoryClassName + "-title";
        protected virtual string FallbackTitle => inventoryClassName;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize(inventoryClassName + "-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += OnInventorySlotModified;

            RebuildIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        protected void RebuildIOFaceMap()
        {
            ioFaces = BuildIOFaceMap();
            ioFaces?.Apply(inventory);
        }

        protected IOFaceMap BuildJsonIOFaceMap(JsonObject ioEntries = null)
        {
            return JsonMachineIoBuilder.Build(
                ioEntries ?? Block?.Attributes?["vkIo"],
                Block,
                Pos,
                ActiveInputFirst,
                ActiveInputLast,
                ActiveOutputFirst,
                ActiveOutputLast);
        }

        protected abstract IOFaceMap BuildIOFaceMap();
        protected abstract TRecipe FindRecipe(ItemStack input);
        protected abstract IEnumerable<ItemStack> GetOutputs(TRecipe recipe);
        protected abstract GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi);

        protected virtual void OnInventorySlotModified(int slotId)
        {
            Api?.World?.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();
        }

        protected virtual void OnServerPushTick(float dt)
        {
            MachineOutputHelper.FlushOutputs(this, inventory, ioFaces?.OutputEntries, OutputPushBatch);
        }

        protected virtual void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            for (int slotId = ActiveInputFirst; slotId <= ActiveInputLast; slotId++)
            {
                if (TryProcessInputSlot(inventory[slotId])) return;
            }
        }

        private bool TryProcessInputSlot(ItemSlot slot)
        {
            if (slot.Empty) return false;

            ItemStack input = slot.Itemstack;
            TRecipe recipe = FindRecipe(input);
            if (recipe == null) return false;

            int quantity = InputQuantityPerCycle(recipe, input);
            if (quantity <= 0 || input.StackSize < quantity) return false;

            List<ItemStack> outputs = new List<ItemStack>();
            IEnumerable<ItemStack> resolvedOutputs = GetOutputs(recipe, input);
            if (resolvedOutputs != null)
            {
                foreach (ItemStack output in resolvedOutputs)
                {
                    if (output == null || output.StackSize <= 0) continue;
                    outputs.Add(output);
                }
            }
            if (outputs.Count == 0) return false;

            PlayWorkEffects(recipe, input);

            slot.TakeOut(quantity);
            slot.MarkDirty();

            foreach (ItemStack output in outputs)
            {
                DepositOutput(output);
            }

            MarkDirty(true);
            return true;
        }

        protected virtual bool HasProcessableInput()
        {
            for (int slotId = ActiveInputFirst; slotId <= ActiveInputLast; slotId++)
            {
                if (CanProcessInputSlot(inventory[slotId])) return true;
            }

            return false;
        }

        protected virtual bool CanProcessInputSlot(ItemSlot slot)
        {
            if (slot == null || slot.Empty) return false;

            ItemStack input = slot.Itemstack;
            TRecipe recipe = FindRecipe(input);
            if (recipe == null) return false;

            int quantity = InputQuantityPerCycle(recipe, input);
            if (quantity <= 0 || input.StackSize < quantity) return false;

            IEnumerable<ItemStack> outputs = GetOutputs(recipe, input);
            if (outputs == null) return false;
            foreach (ItemStack output in outputs)
            {
                if (output != null && output.StackSize > 0) return true;
            }
            return false;
        }

        protected float CurrentWorkerProgress()
        {
            return Math.Max(0f, GetBehavior<BEBehaviorKineticWorker>()?.CurrentProgress ?? 0f);
        }

        protected float CurrentWorkerProgressMax()
        {
            return Math.Max(1f, GetBehavior<BEBehaviorKineticWorker>()?.WorkPerCycle ?? 1f);
        }

        protected virtual bool CanProgressCurrentRecipe()
        {
            if (!HasProcessableInput()) return false;

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return false;
            if (kinetic.IsConflicted || (kinetic.EffectiveNetwork?.IsOverstressed ?? false)) return false;

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            float minRpm = Math.Max(0.01f, worker?.MinRPM ?? 0.01f);
            return Math.Abs(kinetic.CurrentRPM) >= minRpm;
        }

        public virtual bool AppendKineticWorkTooltip(StringBuilder dsc, BEBehaviorKineticWorker worker)
        {
            if (!HasProcessableInput()) return true;

            KineticTooltipBuilder.AppendWorkProgress(dsc, worker);
            return true;
        }

        protected virtual void PlayWorkEffects(TRecipe recipe, ItemStack input)
        {
            if (WorkSound == null) return;
            Api.World.PlaySoundAt(WorkSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: WorkSoundVolume);
        }

        protected virtual IEnumerable<ItemStack> GetOutputs(TRecipe recipe, ItemStack input)
        {
            return GetOutputs(recipe);
        }

        protected virtual void DepositOutput(ItemStack stack)
        {
            MachineOutputHelper.DepositOrPush(this, inventory, ActiveOutputFirst, ActiveOutputLast, stack, ioFaces?.OutputEntries, OutputPushBatch, OutputDropPosition());
        }

        protected IEnumerable<ItemStack> ResolvedOutputs(IEnumerable<JsonItemStack> outputs)
        {
            if (outputs == null) yield break;
            foreach (JsonItemStack output in outputs)
            {
                if (output?.ResolvedItemstack == null) continue;
                yield return output.ResolvedItemstack.Clone();
            }
        }

        protected virtual Vec3d OutputDropPosition()
        {
            return new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                OpenDialog(byPlayer);
            }
            return true;
        }

        protected bool HandleCrateStyleRightClick(
            IPlayer byPlayer,
            bool crateInput,
            bool crateOutput,
            System.Func<ItemStack, bool> inputFilter = null)
        {
            if (!crateInput && !crateOutput) return false;
            if (Api.World is not IServerWorldAccessor) return true;
            if (byPlayer == null) return true;
            if (!CheckClaim(byPlayer)) return true;

            ItemSlot hotbarSlot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            bool put = byPlayer?.Entity?.Controls?.ShiftKey == true;
            bool bulk = byPlayer?.Entity?.Controls?.CtrlKey == true;

            if (put)
            {
                if (!crateInput) return false;
                if (hotbarSlot == null || hotbarSlot.Empty) return true;
                if (inputFilter != null && !inputFilter(hotbarSlot.Itemstack)) return true;

                TryCratePut(byPlayer, hotbarSlot, bulk);
                return true;
            }

            if (!crateOutput) return false;
            TryCrateTake(byPlayer, bulk);
            return true;
        }

        private void TryCratePut(IPlayer byPlayer, ItemSlot hotbarSlot, bool bulk)
        {
            int quantity = bulk ? hotbarSlot.StackSize : 1;
            AssetLocation code = hotbarSlot.Itemstack?.Collectible?.Code;
            int moved = InventoryRangeInteractionHelper.PutFromSlotIntoRange(
                Api.World, inventory, ActiveInputFirst, ActiveInputLast, hotbarSlot, quantity);
            if (moved <= 0) return;

            InventoryRangeInteractionHelper.PlayBuildSound(byPlayer);
            Api.World.Logger.Audit("{0} Put {1}x{2} into {3} at {4}.",
                byPlayer.PlayerName,
                moved,
                code,
                Block?.Code,
                Pos);
            MarkDirty(true);
        }

        private void TryCrateTake(IPlayer byPlayer, bool bulk)
        {
            ItemSlot sourceSlot = InventoryRangeInteractionHelper.FirstNonEmptySlot(inventory, ActiveOutputFirst, ActiveOutputLast);
            if (sourceSlot == null || sourceSlot.Empty) return;

            AssetLocation code = sourceSlot.Itemstack?.Collectible?.Code;
            int requestedQuantity = bulk ? sourceSlot.Itemstack.Collectible.MaxStackSize : 1;
            int taken = InventoryRangeInteractionHelper.TakeFromRangeToPlayer(
                Api.World, byPlayer, inventory, ActiveOutputFirst, ActiveOutputLast, requestedQuantity);
            if (taken <= 0) return;

            InventoryRangeInteractionHelper.PlayBuildSound(byPlayer);
            Api.World.Logger.Audit("{0} Took {1}x{2} from {3} at {4}.",
                byPlayer.PlayerName,
                taken,
                code,
                Block?.Code,
                Pos);
            MarkDirty(true);
        }

        protected virtual void OpenDialog(IPlayer byPlayer)
        {
            string title = Lang.Get(TitleLangCode);
            if (string.IsNullOrEmpty(title) || title == TitleLangCode) title = FallbackTitle;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);
            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            WriteDialogState(tree);
            tree.ToBytes(bw);

            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, Pos, OpenDialogPacketId, ms.ToArray());
            byPlayer.InventoryManager.OpenInventory(inventory);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(inventory);
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

        protected bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent {1} packet at {2} but has no claim access. Rejected.", player.PlayerName, inventoryClassName, Pos);
            return false;
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != OpenDialogPacketId)
            {
                base.OnReceivedServerPacket(packetid, data);
                return;
            }

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();
            ReadDialogState(tree);

            if (clientDialog == null)
            {
                clientDialog = CreateClientDialog(title, capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                OnClientDialogUpdated(clientDialog);
            }
        }

        protected virtual void WriteDialogState(ITreeAttribute tree) => WriteState(tree);
        protected virtual void ReadDialogState(ITreeAttribute tree) => ReadState(tree);
        protected virtual void OnClientDialogUpdated(GuiDialogBlockEntity dialog) { }
        protected void RefreshClientDialog() => OnClientDialogUpdated(clientDialog);

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            WriteState(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory?.FromTreeAttributes(tree);
            ReadState(tree);
            if (Api != null)
            {
                inventory?.ResolveBlocksOrItems();
                RebuildIOFaceMap();
            }
            if (clientDialog != null) OnClientDialogUpdated(clientDialog);
        }

        protected virtual void WriteState(ITreeAttribute tree) { }
        protected virtual void ReadState(ITreeAttribute tree) { }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeDialog();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeDialog();
        }

        private void DisposeDialog() => GuiDialogUtil.SafeDispose(ref clientDialog);
    }
}
