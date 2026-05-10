using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Horizontal sieve drum: 1×1×3 multiblock. Slot 0 is the input; slots 1–3 are the
    /// output buffer. Each kinetic-work cycle consumes one item from input, rolls a drop,
    /// and cascades it into the first available output slot (matching same-material first,
    /// then empty). If all outputs are full, the drop spills at the middle cell.
    /// </summary>
    public class BEKineticSieve : BlockEntityOpenableContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast  = 3;

        // Match the basin's tick pacing so multi-block kinetic processors all eject at the same
        // visible cadence; 8 items per tick keeps stacks moving without flooding adjacent buffers.
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticsieve";

        public BEKineticSieve()
        {
            inventory = new InventoryGeneric(4, "kineticsieve-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += _ => MarkDirty(true);

            // Top face only feeds the input slot, bottom exposes the output buffer. Without
            // this, a funnel under the drum would pull the unprocessed input back out before
            // the sieve gets a chance to work.
            ioFaces = new IOFaceMap().MapInput(BlockFacing.UP, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);

            if (api.Side == EnumAppSide.Server)
            {
                PanLootRoller.Init(api);
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        // Active eject from the drum's center cell, downward. The sieve is a 1×1×3 multiblock
        // and the controller sits at one end, so pushing from Pos would drop items beneath the
        // wrong cell. The middle cell is where the panning particles spawn, so the visible
        // exit point matches where items land.
        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            BlockPos middle = MiddleCellPos();
            foreach (BlockFacing face in ioFaces.OutputFaces)
            {
                foreach (int slotId in ioFaces.OutputSlotsFor(face))
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, middle, face, slot, OutputPushBatch);
                    if (moved > 0) MarkDirty(true);
                }
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticsieve-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticsieve-title") title = "Kinetic Sieve";
                byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityInventory", title, 4, inventory);
                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, (int)EnumBlockContainerPacketId.OpenInventory, data);
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
            return true;
        }

        private static readonly AssetLocation PanningSound = new AssetLocation("sounds/player/panning.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = inventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            ItemStack drop = null;
            bool consume = false;

            // 1) Custom recipe registry first — handles mod-defined inputs (grit items, etc.).
            var registry = Api.ModLoader.GetModSystem<KineticSieveRecipeRegistry>();
            var recipe = registry?.FindRecipe(input);
            if (recipe != null)
            {
                drop = recipe.RollOutput(Api.World);
                consume = true;
            }
            // 2) Fall through to vanilla pannable parity for raw blocks like gravel/sand/soil.
            else if (input.Block != null && PanLootRoller.IsPannable(input.Block))
            {
                drop = PanLootRoller.RollPannableDrop(Api.World, input.Block);
                consume = true;
            }

            if (!consume) return;

            BlockPos mid = MiddleCellPos();
            Api.World.PlaySoundAt(PanningSound, mid.X + 0.5, mid.Y + 0.5, mid.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.6f);
            Vec3d particlePos = new Vec3d(mid.X + 0.5, mid.Y + 0.1, mid.Z + 0.5);
            if (input.Block != null)
            {
                Api.World.SpawnCubeParticles(particlePos, new ItemStack(input.Block), 0.3f, 12, 0.4f);
            }

            slot.TakeOut(1);
            slot.MarkDirty();

            if (drop == null) return;
            DepositOutput(drop);
        }

        // Cascading deposit: prefer same-material slots with headroom (so stacks merge),
        // then fill the first empty slot. If everything is full, spill at the middle cell
        // so the sieve never silently halts.
        private void DepositOutput(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (!s.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;
                int max = s.Itemstack.Collectible.MaxStackSize;
                int free = max - s.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, stack.StackSize);
                s.Itemstack.StackSize += take;
                stack.StackSize -= take;
                s.MarkDirty();
                if (stack.StackSize <= 0) return;
            }

            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = stack.Clone();
                s.MarkDirty();
                return;
            }

            BlockPos mid = MiddleCellPos();
            Vec3d at = new Vec3d(mid.X + 0.5, mid.Y + 0.1, mid.Z + 0.5);
            Api.World.SpawnItemEntity(stack, at);
        }

        // Drum center cell — one step from the controller along the drum axis, in the direction
        // away from the player. The "side" variant encodes which face points toward the player.
        private BlockPos MiddleCellPos()
        {
            string side = Block?.Variant["side"];
            int dx = 0, dz = 0;
            switch (side)
            {
                case "n": dz =  1; break;
                case "s": dz = -1; break;
                case "e": dx = -1; break;
                case "w": dx =  1; break;
            }
            return new BlockPos(Pos.X + dx, Pos.Y, Pos.Z + dz, Pos.dimension);
        }
    }
}
