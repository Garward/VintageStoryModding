using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Basin under a crusher head. 3-slot inventory: input (top, visualised in the well),
    /// side-output (opposite the input face), and bottom-output. Side-output and bottom-output
    /// are filled by <see cref="VintageKinematics.Api.BEBehaviorCrusherProcess"/> on the head above.
    /// Uses <see cref="BlockEntityOpenableContainer"/> so the GUI dialog opens via the standard
    /// server→client packet flow.
    /// </summary>
    public class BECrusherBasin : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 9;
        public const int InventorySize = 10;

        public const int PacketIdOpenDialog = 5400;

        // Re-check outputs ~4x/sec — fast enough that funnels next to the basin feel "instant"
        // but slow enough that an empty basin doesn't burn cycles scanning neighbours.
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        private readonly InventoryGeneric inventory;
        private CrusherBasinItemRenderer itemRenderer;
        private GuiDialogCrusherBasin clientDialog;
        private IOFaceMap ioFaces;
        private long pushListenerId;
        public string DialogTitle { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "crusherbasin";
        public IOFaceMap IOFaces => ioFaces;

        public BECrusherBasin()
        {
            // Slot 0 is a normal input slot; slots 1-2 are output-only (player can take but
            // not deposit), so users can't accidentally treat the basin as bonus storage.
            inventory = new InventoryGeneric(InventorySize, "crusherbasin-0", null, null, (slotId, self) =>
            {
                return slotId == SlotInput
                    ? new ItemSlot(self)
                    : new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += OnSlotModified;

            string title = Lang.Get("vintagekinematics:basin-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:basin-title") title = "Crusher Basin";
            DialogTitle = title;

            BuildIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                pushListenerId = RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            if (api is ICoreClientAPI capi)
            {
                itemRenderer = new CrusherBasinItemRenderer(capi, Pos);
                capi.Event.RegisterRenderer(itemRenderer, EnumRenderStage.Opaque);
                itemRenderer.UpdateStack(inventory[SlotInput]?.Itemstack);
            }
        }

        private void BuildIOFaceMap()
        {
            // New variants encode placement facing: left face is input, right face is output.
            // Legacy side=x maps to facing south; side=z maps to facing west.
            // Both output faces drain the full 9-slot buffer so a funnel on either face sees
            // everything — no need to micro-partition the buffer per face.
            BlockFacing facing = FacingFromVariant(Block?.Variant?["side"]);
            BlockFacing inputFace = LeftOf(facing);
            BlockFacing sideOutputFace = RightOf(facing);

            ioFaces = new IOFaceMap(Pos)
                .MapInput(inputFace, SlotInput)
                .MapInput(BlockFacing.UP, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(sideOutputFace, i);
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);
        }

        private static BlockFacing FacingFromVariant(string side)
        {
            switch (side)
            {
                case "n": return BlockFacing.NORTH;
                case "e": return BlockFacing.EAST;
                case "w": return BlockFacing.WEST;
                case "z": return BlockFacing.WEST;
                case "s":
                case "x":
                default: return BlockFacing.SOUTH;
            }
        }

        private static BlockFacing LeftOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.WEST;
            if (facing == BlockFacing.EAST) return BlockFacing.NORTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.EAST;
            if (facing == BlockFacing.WEST) return BlockFacing.SOUTH;
            return BlockFacing.EAST;
        }

        private static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.WEST;
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

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:basin-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:basin-title") title = "Crusher Basin";

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
                    Api.World.Logger.Audit("Player {0} sent crusher basin packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
                clientDialog = new GuiDialogCrusherBasin(title, inventory, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
        }

        private void OnSlotModified(int slotId)
        {
            MarkDirty(true);
            if (slotId == SlotInput && itemRenderer != null)
            {
                itemRenderer.UpdateStack(inventory[SlotInput]?.Itemstack);
            }
        }

        /// <summary>
        /// Try to deposit a stack into the side-output slot first, falling back to bottom-output.
        /// Returns true if the entire stack was consumed; remaining stack is dropped at the basin
        /// top if both outputs are full.
        /// </summary>
        public void DepositOutput(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;

            // First pass: stack-merge into matching slots so the buffer stays compact and
            // funnels see one big stack per item type instead of fragmenting across cells.
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                var slot = inventory[i];
                if (slot.Empty) continue;
                if (!slot.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;
                int max = slot.Itemstack.Collectible.MaxStackSize;
                int free = max - slot.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, stack.StackSize);
                slot.Itemstack.StackSize += take;
                stack.StackSize -= take;
                slot.MarkDirty();
                if (stack.StackSize <= 0) return;
            }

            // Second pass: drop into the first empty slot.
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                var slot = inventory[i];
                if (!slot.Empty) continue;
                slot.Itemstack = stack.Clone();
                slot.MarkDirty();
                return;
            }

            // Buffer full; spill at the top of the basin so the player has a recoverable failure.
            Vec3d dropAt = new Vec3d(Pos.X + 0.5, Pos.Y + 0.7, Pos.Z + 0.5);
            Api.World.SpawnItemEntity(stack, dropAt);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            DisposeRenderer();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            DisposeRenderer();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        private void DisposeRenderer()
        {
            if (Api is ICoreClientAPI capi && itemRenderer != null)
            {
                capi.Event.UnregisterRenderer(itemRenderer, EnumRenderStage.Opaque);
                itemRenderer.Dispose();
                itemRenderer = null;
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            base.FromTreeAttributes(tree, world);
            if (Api is ICoreClientAPI && itemRenderer != null)
            {
                itemRenderer.UpdateStack(inventory[SlotInput]?.Itemstack);
            }
        }
    }
}
