using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-block press: top face inputs items, bottom face outputs solids and drains liquids
    /// into any <see cref="BlockLiquidContainerBase"/> directly below (e.g. a barrel). Each work
    /// cycle consumes 1 input and produces the recipe's solid and/or liquid outputs.
    /// </summary>
    public class BEKineticPress : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 9;
        public const int InventorySize = 10;

        public const int PacketIdOpenDialog = 5500;

        public const float CapacityLitres = 10f;
        // Liquid portion items use stacksize as portions; 100 portions = 1 litre per the
        // standard waterTightContainerProps.itemsPerLitre convention.
        private const float PortionsPerLitre = 100f;

        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;
        private const float DrainTickIntervalMs = 500f;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private ItemStack liquidStack;
        private float liquidLitres;
        private GuiDialogKineticPress clientDialog;
        public string DialogTitle { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticpress";
        public IOFaceMap IOFaces => ioFaces;

        public BEKineticPress()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticpress-0", null, null, (slotId, self) =>
            {
                return slotId == SlotInput
                    ? new ItemSlot(self)
                    : new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += _ => MarkDirty(true);

            string title = Lang.Get("vintagekinematics:kineticpress-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticpress-title") title = "Kinetic Extractor";
            DialogTitle = title;

            // FromTreeAttributes can run before Api is set, so resolution there is skipped.
            // Resolve on first Initialize so liquidStack.Collectible is non-null thereafter.
            if (liquidStack != null && liquidStack.Collectible == null)
            {
                liquidStack.ResolveBlockOrItem(api.World);
                if (liquidStack.Collectible == null)
                {
                    liquidStack = null;
                    liquidLitres = 0f;
                }
            }

            // Automation input is side-specific so belts can target the press deliberately.
            // Output buffer stays on the bottom for barrels/funnels below the press.
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
                RegisterGameTickListener(OnServerDrainTick, (int)DrainTickIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        // Shaft sits on the variant axis (axis-x → east/west, axis-z → north/south). Automation
        // input belongs on the perpendicular axis so the feed face doesn't fight the shaft.
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

        private void OnServerDrainTick(float dt)
        {
            if (liquidStack == null || liquidLitres <= 0f) return;

            BlockPos belowPos = Pos.DownCopy();
            Block belowBlock = Api.World.BlockAccessor.GetBlock(belowPos);
            if (belowBlock is not BlockLiquidContainerBase lcb) return;

            // Many BlockLiquidContainerBase subclasses (barrel, crock, bowl) store liquid via
            // a BlockEntityContainer. Without that BE present, vanilla TryPutLiquid NREs at
            // BlockLiquidContainerBase.cs:583. Skip if there's no container BE underneath.
            if (Api.World.BlockAccessor.GetBlockEntity(belowPos) is not BlockEntityContainer) return;

            int portionsMoved;
            try
            {
                ItemStack pourStack = liquidStack.Clone();
                // TryPutLiquid uses the stack as a template; sets quantity internally.
                pourStack.StackSize = 999999;
                portionsMoved = lcb.TryPutLiquid(belowPos, pourStack, liquidLitres);
            }
            catch
            {
                return;
            }
            if (portionsMoved <= 0) return;

            float litresMoved = portionsMoved / PortionsPerLitre;
            liquidLitres -= litresMoved;
            if (liquidLitres <= 0.0001f)
            {
                liquidLitres = 0f;
                liquidStack = null;
            }
            MarkDirty(true);
        }

        private static readonly AssetLocation PressSound = new AssetLocation("sounds/player/squeezehoneycomb.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = inventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            var registry = Api.ModLoader.GetModSystem<KineticExtractorRecipeRegistry>();
            var recipe = registry?.FindRecipe(input);
            if (recipe != null)
            {
                if (!TryCustomRecipeCycle(slot, recipe)) return;
            }
            else if (!TryVanillaJuiceableCycle(slot, input))
            {
                return;
            }
            MarkDirty(true);
        }

        private bool TryCustomRecipeCycle(ItemSlot slot, KineticExtractorRecipe recipe)
        {
            // Liquid output gates the cycle: refuse to consume input if the buffer can't hold the produced amount.
            if (recipe.Liquid != null && recipe.Liquid.Code != null)
            {
                if (liquidStack?.Collectible != null && !liquidStack.Collectible.Code.Equals(recipe.Liquid.Code)) return false;
                if (liquidLitres + recipe.Liquid.Litres > CapacityLitres + 0.0001f) return false;
            }

            slot.TakeOut(1);
            slot.MarkDirty();
            Api.World.PlaySoundAt(PressSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.5f);

            if (recipe.Outputs != null)
            {
                foreach (var jstack in recipe.Outputs)
                {
                    if (jstack?.ResolvedItemstack == null) continue;
                    DepositOutput(jstack.ResolvedItemstack.Clone());
                }
            }

            if (recipe.Liquid != null && recipe.Liquid.Code != null)
            {
                if (liquidStack == null)
                {
                    liquidStack = new ItemStack(Api.World.GetItem(recipe.Liquid.Code));
                    if (liquidStack.Collectible == null) liquidStack = null;
                }
                if (liquidStack != null) liquidLitres += recipe.Liquid.Litres;
            }
            return true;
        }

        // Vanilla juiceableProperties parity. Items like fruit/honeycomb declare their press
        // liquid in JSON attributes; we read those and apply a 20% yield bonus so the auto-press
        // remains a meaningful upgrade over the hand-cranked fruit press.
        private const float VanillaYieldBonus = 1.2f;

        private bool TryVanillaJuiceableCycle(ItemSlot slot, ItemStack input)
        {
            if (input?.Collectible == null) return false;
            var attrs = input.ItemAttributes;
            if (attrs == null || !attrs["juiceableProperties"].Exists) return false;

            var props = attrs["juiceableProperties"].AsObject<JuiceableProperties>(null, input.Collectible.Code.Domain);
            if (props == null || !props.LitresPerItem.HasValue || props.LiquidStack == null) return false;

            props.LiquidStack.Resolve(Api.World, "vintagekinematics press juiceable", input.Collectible.Code, true);
            ItemStack resolvedLiquid = props.LiquidStack.ResolvedItemstack;
            if (resolvedLiquid?.Collectible == null) return false;

            float litres = props.LitresPerItem.Value * VanillaYieldBonus;
            AssetLocation liquidCode = resolvedLiquid.Collectible.Code;
            if (liquidStack?.Collectible != null && !liquidStack.Collectible.Code.Equals(liquidCode)) return false;
            if (liquidLitres + litres > CapacityLitres + 0.0001f) return false;

            slot.TakeOut(1);
            slot.MarkDirty();
            Api.World.PlaySoundAt(PressSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.5f);

            if (liquidStack == null) liquidStack = resolvedLiquid.Clone();
            liquidLitres += litres;

            if (props.PressedStack != null)
            {
                props.PressedStack.Resolve(Api.World, "vintagekinematics press juiceable", input.Collectible.Code, true);
                if (props.PressedStack.ResolvedItemstack != null)
                {
                    DepositOutput(props.PressedStack.ResolvedItemstack.Clone());
                }
            }
            return true;
        }

        private void DepositOutput(ItemStack stack)
        {
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, inventory, SlotOutputFirst, SlotOutputLast, stack, ioFaces?.OutputEntries, OutputPushBatch, at);
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticpress-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticpress-title") title = "Kinetic Extractor";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
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
            if (packetid < 1000)
            {
                if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use))
                {
                    Api.World.Logger.Audit("Player {0} sent kinetic press packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
                    return;
                }
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
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
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogKineticPress(title, inventory, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            if (liquidStack != null && liquidLitres > 0f)
            {
                string name = liquidStack.GetName();
                sb.AppendLine();
                sb.AppendLine($"{name}: {liquidLitres:0.##} / {CapacityLitres:0} L");
            }
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
            tree.SetFloat("liquidLitres", liquidLitres);
            if (liquidStack != null)
            {
                tree.SetItemstack("liquidStack", liquidStack);
            }
            else
            {
                tree.RemoveAttribute("liquidStack");
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            liquidLitres = tree.GetFloat("liquidLitres", 0f);
            liquidStack = tree.GetItemstack("liquidStack");
            if (liquidStack != null && Api?.World != null)
            {
                liquidStack.ResolveBlockOrItem(Api.World);
            }
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
