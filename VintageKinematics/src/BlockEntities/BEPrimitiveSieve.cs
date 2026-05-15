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
    /// Single-cell hand-crank-friendly sieve. One input, four-slot output buffer.
    /// Handles vanilla pannables at parity; grit and dust are reserved for the full kinetic sieve.
    /// </summary>
    public class BEPrimitiveSieve : BlockEntityOpenableContainer, IFaceMappedContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast  = 4;
        public const int InventorySize = 5;

        private const float OutputPushIntervalMs = 250f;
        private const float ProgressSyncIntervalMs = 500f;
        private const float DustParticleIntervalMs = 350f;
        private const int OutputPushBatch = 8;
        private const int PannableRollsPerBlock = 8;
        private const float PanTopY = 0.6875f;

        public const int PacketIdOpenDialog = 5610;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private GuiDialogPrimitiveSieve clientDialog;
        private BEBehaviorKineticWorker worker;
        private BEBehaviorKinetic kinetic;
        private float prevSyncedProgress;
        public string DialogTitle { get; private set; }

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "primitivesieve";
        public IOFaceMap IOFaces => ioFaces;

        public BEPrimitiveSieve()
        {
            inventory = new InventoryGeneric(InventorySize, "primitivesieve-0", null, null, (slotId, self) =>
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

            string title = Lang.Get("vintagekinematics:primitivesieve-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:primitivesieve-title") title = "Primitive Sieve";
            DialogTitle = title;

            ConfigureIOFaceMap();

            worker = GetBehavior<BEBehaviorKineticWorker>();
            kinetic = GetBehavior<BEBehaviorKinetic>();

            if (api.Side == EnumAppSide.Server)
            {
                PanLootRoller.Init(api);
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                RegisterGameTickListener(OnProgressSyncTick, (int)ProgressSyncIntervalMs);
                RegisterGameTickListener(OnDustParticleTick, (int)DustParticleIntervalMs);
            }

            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        // Small dust puffs from the pan surface while the sieve is actively working. Gives
        // the slow primitive sieve visible life between cycle completions (which are sparse at
        // 4-8 RPM). Uses the input block's texture so the particles match what's being sieved.
        private void OnDustParticleTick(float dt)
        {
            if (kinetic == null || System.Math.Abs(kinetic.CurrentRPM) < 0.01f) return;
            if (kinetic.IsConflicted || (kinetic.Network?.IsOverstressed ?? false)) return;
            ItemSlot input = inventory[SlotInput];
            if (input.Empty || input.Itemstack.Block == null) return;
            if (!PanLootRoller.IsSieveablePanningSource(input.Itemstack.Block)) return;

            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + PanTopY, Pos.Z + 0.5);
            Api.World.SpawnCubeParticles(at, new ItemStack(input.Itemstack.Block), 0.25f, 3, 0.15f);
        }

        // Push BE state to clients whenever the worker is actually grinding, so the dialog's
        // progress arrow tracks reality. Skips ticks when nothing has changed to avoid spamming
        // empty syncs. Mirrors the vanilla quern's 500ms cadence.
        private void OnProgressSyncTick(float dt)
        {
            if (worker == null) return;
            ItemSlot input = inventory[SlotInput];
            float cur = worker.CurrentProgress;
            bool active = !input.Empty && cur != prevSyncedProgress;
            prevSyncedProgress = cur;
            if (active) MarkDirty();
        }

        // Input on top + the perpendicular face nearest the player; output on bottom + the
        // opposite perpendicular face. Lets the player sit the sieve on top of a chest and
        // drop a funnel directly above.
        private void ConfigureIOFaceMap()
        {
            BlockFacing inputFace = AutomationInputFace();
            BlockFacing outputSideFace = inputFace.Opposite;
            ioFaces = new IOFaceMap(Pos)
                .MapInput(BlockFacing.UP, SlotInput)
                .MapInput(inputFace, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(outputSideFace, i).MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);
        }

        // Shaft plugs into the face opposite the player, so input must land on a perpendicular
        // face to avoid fighting the shaft connection.
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
                string title = Lang.Get("vintagekinematics:primitivesieve-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:primitivesieve-title") title = "Primitive Sieve";

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
                    Api.World.Logger.Audit("Player {0} sent primitive sieve packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
                clientDialog = new GuiDialogPrimitiveSieve(title, inventory, Pos, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
                if (worker != null) clientDialog.Update(worker.CurrentProgress, worker.WorkPerCycle);
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
            bool usedPanningDrops = false;
            var cfg = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;

            if ((cfg?.UseVanillaPanningDrops ?? true) && input.Block != null && PanLootRoller.IsSieveablePanningSource(input.Block))
            {
                drops = new List<ItemStack>(PannableRollsPerBlock);
                for (int i = 0; i < PannableRollsPerBlock; i++)
                {
                    ItemStack d = PanLootRoller.RollPannableDrop(Api.World, input.Block);
                    if (d != null) drops.Add(d);
                }
                consume = true;
                usedPanningDrops = true;
            }

            if (!consume) return;

            Api.World.PlaySoundAt(PanningSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.4f);
            Vec3d particlePos = new Vec3d(Pos.X + 0.5, Pos.Y + PanTopY, Pos.Z + 0.5);
            if (input.Block != null)
            {
                Api.World.SpawnCubeParticles(particlePos, new ItemStack(input.Block), 0.3f, 8, 0.4f);
            }

            slot.TakeOut(1);
            slot.MarkDirty();

            if (drops == null) return;
            float sourceYieldMultiplier = usedPanningDrops ? cfg?.ResolvePrimitiveSievePanningYield() ?? 1f : 1f;
            foreach (ItemStack drop in drops)
            {
                ItemStack scaled = ApplyYieldMultiplier(drop, cfg, sourceYieldMultiplier);
                if (scaled != null) DepositOutput(scaled);
            }
        }

        private ItemStack ApplyYieldMultiplier(ItemStack drop, VintageKinematicsConfig cfg, float sourceYieldMultiplier)
        {
            if (drop == null || drop.StackSize <= 0) return drop;
            float mult = cfg?.ResolveSieveYield(drop.Collectible?.Code, sourceYieldMultiplier) ?? sourceYieldMultiplier;
            if (mult <= 0f) return null;
            if (System.Math.Abs(mult - 1f) < 1e-4f) return drop;

            float scaled = drop.StackSize * mult;
            int whole = (int)System.Math.Floor(scaled);
            float frac = scaled - whole;
            if (frac > 0f && Api.World.Rand.NextDouble() < frac) whole++;
            if (whole <= 0) return null;
            drop.StackSize = whole;
            return drop;
        }

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

            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.1, Pos.Z + 0.5);
            Api.World.SpawnItemEntity(stack, at);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            if (Api?.Side == EnumAppSide.Client && clientDialog != null && worker != null)
            {
                clientDialog.Update(worker.CurrentProgress, worker.WorkPerCycle);
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
