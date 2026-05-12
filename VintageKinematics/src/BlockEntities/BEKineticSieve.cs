using System.Collections.Generic;
using System.IO;
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
    /// Horizontal sieve drum: 1×1×3 multiblock. Slot 0 is the input; slots 1–3 are the
    /// output buffer. Each kinetic-work cycle consumes one item from input, rolls a drop,
    /// and cascades it into the first available output slot (matching same-material first,
    /// then empty). If all outputs are full, the drop spills at the middle cell.
    /// </summary>
    public class BEKineticSieve : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast  = 9;
        public const int InventorySize = 10;

        // Match the basin's tick pacing so multi-block kinetic processors all eject at the same
        // visible cadence; 8 items per tick keeps stacks moving without flooding adjacent buffers.
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        // Vanilla gold pan consumes 1/8 of a block per pan action. Rolling 8 times per
        // consumed block keeps the kinetic sieve's yield in line with hand panning.
        private const int PannableRollsPerBlock = 8;

        public const int PacketIdOpenDialog = 5600;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private GuiDialogKineticSieve clientDialog;
        public string DialogTitle { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticsieve";
        public IOFaceMap IOFaces => ioFaces;

        public BEKineticSieve()
        {
            // Output slots refuse player insertion so users can't dump random goods into the
            // sieve and confuse the workflow. The input slot stays a plain ItemSlot.
            inventory = new InventoryGeneric(InventorySize, "kineticsieve-0", null, null, (slotId, self) =>
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

            string title = Lang.Get("vintagekinematics:kineticsieve-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticsieve-title") title = "Kinetic Sieve";
            DialogTitle = title;

            // The drum is 3 cells long. Expose input on the perpendicular face and on UP of every
            // cell, and outputs on the opposite perpendicular face and DOWN of every cell, so a
            // funnel can sit above/below/beside any segment of the drum and still find the right
            // slot. Without per-cell coverage, funnels on non-controller / non-middle cells silently
            // miss the map.
            BlockFacing inputFace = AutomationInputFace();
            BlockFacing outputSideFace = inputFace.Opposite;
            ioFaces = new IOFaceMap(Pos);
            foreach (BlockPos cell in AllCells())
            {
                ioFaces.MapInput(cell, inputFace, SlotInput);
                ioFaces.MapInput(cell, BlockFacing.UP, SlotInput);
                for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
                {
                    ioFaces.MapOutput(cell, outputSideFace, i);
                    ioFaces.MapOutput(cell, BlockFacing.DOWN, i);
                }
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

        // The drum axis runs along the "side" direction — that's where the shaft plugs in and
        // where the multiblock extends. Automation input must land on the perpendicular axis so
        // the feed face doesn't fight the shaft connection.
        private BlockFacing AutomationInputFace()
        {
            string side = Block?.Variant?["side"] ?? "n";
            switch (side)
            {
                case "n":
                case "s": return BlockFacing.EAST;
                case "e":
                case "w": return BlockFacing.SOUTH;
                default:  return BlockFacing.EAST;
            }
        }

        // Active eject driven entirely by the declarative output map: each entry's cell is the
        // origin of the push and its face is the direction outward. The middle cell is the only
        // mapped output cell for the sieve, so this iterates exactly the slots that should drain.
        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            foreach (FaceMapEntry entry in ioFaces.OutputEntries)
            {
                foreach (int slotId in entry.SlotIds)
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, entry.Cell, entry.Face, slot, OutputPushBatch);
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
                    Api.World.Logger.Audit("Player {0} sent sieve packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
                clientDialog = new GuiDialogKineticSieve(title, inventory, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
        }

        private static readonly AssetLocation PanningSound = new AssetLocation("sounds/player/panning.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = inventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            List<ItemStack> drops = null;
            bool consume = false;

            // 1) Custom recipe registry first — handles mod-defined inputs (grit items, etc.).
            var registry = Api.ModLoader.GetModSystem<KineticSieveRecipeRegistry>();
            var recipe = registry?.FindRecipe(input);
            if (recipe != null)
            {
                ItemStack d = recipe.RollOutput(Api.World);
                if (d != null) drops = new List<ItemStack> { d };
                consume = true;
            }
            // 2) Fall through to vanilla pannable parity for raw blocks like gravel/sand/soil.
            // Vanilla gold pan consumes 1/8 block per pan, so one sieved block = 8 rolls.
            else if (input.Block != null && PanLootRoller.IsPannable(input.Block))
            {
                drops = new List<ItemStack>(PannableRollsPerBlock);
                for (int i = 0; i < PannableRollsPerBlock; i++)
                {
                    ItemStack d = PanLootRoller.RollPannableDrop(Api.World, input.Block);
                    if (d != null) drops.Add(d);
                }
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

            if (drops == null) return;
            var cfg = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            foreach (ItemStack drop in drops)
            {
                ItemStack scaled = ApplyYieldMultiplier(drop, cfg);
                if (scaled != null) DepositOutput(scaled);
            }
        }

        // Probabilistic rounding: a fractional yield like 2.5x on a 1-item drop produces 3 items
        // half the time and 2 the other half, preserving the average. Returns null when the roll
        // ends up at zero (e.g. a 0.3x multiplier sometimes eats a single-item drop entirely).
        private ItemStack ApplyYieldMultiplier(ItemStack drop, VintageKinematicsConfig cfg)
        {
            if (drop == null || drop.StackSize <= 0) return drop;
            float mult = cfg?.ResolveSieveYield(drop.Collectible?.Code) ?? 1f;
            if (mult <= 0f) return null;
            // Fast path: 1.0 leaves stacks exactly as authored.
            if (System.Math.Abs(mult - 1f) < 1e-4f) return drop;

            float scaled = drop.StackSize * mult;
            int whole = (int)System.Math.Floor(scaled);
            float frac = scaled - whole;
            if (frac > 0f && Api.World.Rand.NextDouble() < frac) whole++;
            if (whole <= 0) return null;
            drop.StackSize = whole;
            return drop;
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

        // All three cells of the drum: controller, middle, far. Used for cell-aware IO mapping
        // so funnels above/below any segment hit the same slot map.
        private BlockPos[] AllCells()
        {
            BlockPos mid = MiddleCellPos();
            int dx = mid.X - Pos.X;
            int dz = mid.Z - Pos.Z;
            BlockPos far = new BlockPos(Pos.X + 2 * dx, Pos.Y, Pos.Z + 2 * dz, Pos.dimension);
            return new[] { Pos, mid, far };
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
