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
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Vertical drill: chews the 3×3 column directly below the multiblock footprint one layer
    /// per work cycle, depositing drops into an output-only 9-slot buffer. Each descent consumes
    /// one shaft (or encased shaft) from the 3-slot input row. The drill halts permanently on
    /// bedrock / out-of-mining-tier blocks. Right-clicking opens the dialog with a Retract button
    /// that walks the bore back to the surface, returning one shaft per layer.
    /// </summary>
    public class BEKineticBore : BlockEntity
    {
        public const int InputSlotFirst = 0;
        public const int InputSlotLast = 2;
        public const int OutputSlotFirst = 3;
        public const int OutputSlotLast = 11;
        public const int InventorySize = 12;

        public const int PacketIdOpenDialog = 5500;
        public const int PacketIdToggleRetract = 5501;

        // 250 ms cadence keeps eject visually in lockstep with basin/sieve.
        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;
        // Above this resistance the bore halts even if mining tier matches — covers admin/reinforced
        // blocks that aren't tier-gated but shouldn't be eaten by a machine.
        private const float UnbreakableResistance = 50f;
        // Default mining tier when the blocktype doesn't override via attributes. 5 = steel-tier,
        // enough for every standard ore + granite without trivializing rarer materials.
        private const int DefaultMiningTier = 5;

        private readonly InventoryGeneric inventory;
        private int drillDepth;
        private bool halted;
        private bool retracting;
        // Paused after a completed retract so the bore doesn't immediately consume the just-returned
        // shafts. Player must click the button again to resume drilling.
        private bool paused;
        private int miningTier;
        // Stack of shaft items consumed on descent. Popped back into the input row during retract
        // so the player always recovers what they put in (subject to inventory space).
        private readonly List<ItemStack> deployedShafts = new List<ItemStack>();

        private GuiDialogKineticBore clientDialog;

        public InventoryBase Inventory => inventory;

        public int DrillDepth => drillDepth;
        public bool Halted => halted;
        public bool Retracting => retracting;
        public bool Paused => paused;

        public BEKineticBore()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticbore-0", null, null, (slotId, self) =>
            {
                if (slotId <= InputSlotLast) return new ItemSlotShaftInput(self);
                return new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("kineticbore-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ => MarkDirty(true);

            miningTier = Block?.Attributes?["miningTier"].AsInt(DefaultMiningTier) ?? DefaultMiningTier;

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                var worker = GetBehavior<BEBehaviorKineticWorker>();
                if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticbore-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticbore-title") title = "Kinetic Bore";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(drillDepth);
                bw.Write(halted);
                bw.Write(retracting);
                bw.Write(paused);
                var tree = new TreeAttribute();
                inventory.ToTreeAttributes(tree);
                tree.ToBytes(bw);
                byte[] data = ms.ToArray();

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, data);
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
            if (packetid == PacketIdToggleRetract)
            {
                if (!CheckClaim(player)) return;
                // Three-state cycle driven by one button:
                //   drilling   -> start retracting (clears halt so a stuck bore can recover)
                //   retracting -> pause (so cancelling a retract doesn't immediately re-consume shafts)
                //   paused     -> resume drilling
                if (retracting)
                {
                    retracting = false;
                    paused = true;
                }
                else if (paused)
                {
                    paused = false;
                }
                else
                {
                    retracting = true;
                    halted = false;
                }
                MarkDirty(true);
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            }
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent bore packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenDialog) return;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            drillDepth = br.ReadInt32();
            halted = br.ReadBoolean();
            retracting = br.ReadBoolean();
            paused = br.ReadBoolean();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogKineticBore(
                    title, inventory, Pos,
                    () => retracting, () => halted, () => paused, () => drillDepth,
                    OnClientToggleRetract, capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.OnStateUpdated();
            }
        }

        private void OnClientToggleRetract()
        {
            // Optimistic UI flip — server still re-sends authoritative state on next tick if needed.
            // Mirror the three-state cycle in the server-side handler.
            if (retracting) { retracting = false; paused = true; }
            else if (paused) { paused = false; }
            else { retracting = true; halted = false; }
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleRetract);
            clientDialog?.OnStateUpdated();
        }

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        // Push out the back face — opposite the shaft input — from the upper (non-spinning) cell
        // so a funnel, chest, or belt placed against the back of the bore drains automatically.
        // The upper layer is the static housing; the lower layer holds the rotating drill bit, so
        // anchoring the push origin on the upper cell avoids visual conflict with the drum.
        private void OnServerPushTick(float dt)
        {
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size)) return;

            string side = Block?.Variant["side"] ?? "n";
            BlockFacing backFace;
            int backX, backZ;
            int midX = baseCorner.X + size.X / 2;
            int midZ = baseCorner.Z + size.Z / 2;
            switch (side)
            {
                case "s": backFace = BlockFacing.SOUTH; backX = midX;                          backZ = baseCorner.Z + size.Z - 1; break;
                case "e": backFace = BlockFacing.EAST;  backX = baseCorner.X + size.X - 1;     backZ = midZ;                      break;
                case "w": backFace = BlockFacing.WEST;  backX = baseCorner.X;                  backZ = midZ;                      break;
                default:  backFace = BlockFacing.NORTH; backX = midX;                          backZ = baseCorner.Z;              break;
            }
            BlockPos pushFrom = new BlockPos(backX, baseCorner.Y + size.Y - 1, backZ, Pos.dimension);

            for (int slotId = OutputSlotFirst; slotId <= OutputSlotLast; slotId++)
            {
                ItemSlot slot = inventory[slotId];
                if (slot.Empty) continue;
                int moved = InventoryPusher.TryPush(Api.World, pushFrom, backFace, slot, OutputPushBatch);
                if (moved > 0) MarkDirty(true);
            }
        }

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            if (Api.Side != EnumAppSide.Server) return;

            if (retracting)
            {
                StepRetract();
                return;
            }

            if (paused) return;
            if (halted) return;
            StepDescent();
        }

        private void StepRetract()
        {
            if (drillDepth <= 0 || deployedShafts.Count == 0)
            {
                retracting = false;
                // Pause on completion so the bore doesn't race the player and eat the shafts that
                // were just returned to the input row. Player resumes by clicking the button again.
                paused = true;
                drillDepth = 0;
                deployedShafts.Clear();
                MarkDirty(true);
                return;
            }

            int lastIdx = deployedShafts.Count - 1;
            ItemStack shaft = deployedShafts[lastIdx];
            deployedShafts.RemoveAt(lastIdx);
            drillDepth--;

            if (shaft != null && shaft.StackSize > 0) ReturnShaftToInput(shaft);
            MarkDirty(true);
        }

        private void StepDescent()
        {
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size))
            {
                halted = true;
                MarkDirty(true);
                return;
            }

            int targetY = baseCorner.Y - 1 - drillDepth;
            if (targetY < 1)
            {
                halted = true;
                MarkDirty(true);
                return;
            }

            ItemSlot shaftSlot = FindShaftSlot();
            if (shaftSlot == null) return;

            // First pass: refuse to start mining a layer that contains a single unbreakable block.
            // Without this, an ore-and-bedrock checkerboard would get partially eaten; players
            // expect the bore to stop cleanly the moment it hits something it can't handle.
            for (int dx = 0; dx < size.X; dx++)
            {
                for (int dz = 0; dz < size.Z; dz++)
                {
                    BlockPos check = new BlockPos(baseCorner.X + dx, targetY, baseCorner.Z + dz, Pos.dimension);
                    Block b = Api.World.BlockAccessor.GetBlock(check);
                    if (b == null || b.Id == 0) continue;
                    if (IsUnbreakable(b))
                    {
                        halted = true;
                        MarkDirty(true);
                        return;
                    }
                }
            }

            // Consume the drill rod for this layer before mining so failures past this point still
            // bank progress correctly. The popped shaft is tracked on the deployed stack so retract
            // can return the same item type that was deployed.
            ItemStack consumed = shaftSlot.TakeOut(1);
            shaftSlot.MarkDirty();
            if (consumed != null) deployedShafts.Add(consumed);

            // Second pass: mine. Drops go through the buffer-first path; anything that doesn't fit
            // spills as an item entity at the mined cell so the bore can't silently lose drops.
            for (int dx = 0; dx < size.X; dx++)
            {
                for (int dz = 0; dz < size.Z; dz++)
                {
                    BlockPos minePos = new BlockPos(baseCorner.X + dx, targetY, baseCorner.Z + dz, Pos.dimension);
                    Block b = Api.World.BlockAccessor.GetBlock(minePos);
                    if (b == null || b.Id == 0) continue;

                    ItemStack[] drops = null;
                    try { drops = b.GetDrops(Api.World, minePos, null, 1f); }
                    catch (System.Exception ex)
                    {
                        // Some blocks assume a non-null player in GetDrops (special tool-gated drops).
                        // We can't satisfy that for a machine, so we just lose the drops rather than
                        // halt — the block still gets removed below to keep progress moving.
                        Api.Logger.Debug("[VK] Bore at {0}: GetDrops threw for {1}: {2}", Pos, b.Code, ex.Message);
                    }
                    if (drops != null)
                    {
                        foreach (ItemStack drop in drops)
                        {
                            if (drop != null && drop.StackSize > 0) DepositOrDrop(drop, minePos);
                        }
                    }
                    Api.World.BlockAccessor.SetBlock(0, minePos);
                }
            }

            drillDepth++;
            MarkDirty(true);
        }

        private ItemSlot FindShaftSlot()
        {
            for (int i = InputSlotFirst; i <= InputSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                string code = s.Itemstack?.Collectible?.Code?.FirstCodePart();
                if (ItemSlotShaftInput.IsAcceptedCode(code)) return s;
            }
            return null;
        }

        private void ReturnShaftToInput(ItemStack shaft)
        {
            for (int i = InputSlotFirst; i <= InputSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (!s.Itemstack.Collectible.Code.Equals(shaft.Collectible.Code)) continue;
                int max = s.Itemstack.Collectible.MaxStackSize;
                int free = max - s.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, shaft.StackSize);
                s.Itemstack.StackSize += take;
                shaft.StackSize -= take;
                s.MarkDirty();
                if (shaft.StackSize <= 0) return;
            }
            for (int i = InputSlotFirst; i <= InputSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = shaft.Clone();
                s.MarkDirty();
                return;
            }
            // Inputs full — spill at the controller so the player can pick the leftover up.
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 1.1, Pos.Z + 0.5);
            Api.World.SpawnItemEntity(shaft, at);
        }

        private bool IsUnbreakable(Block b)
        {
            if (b.Resistance > UnbreakableResistance) return true;
            if (b.RequiredMiningTier > miningTier) return true;
            return false;
        }

        private void DepositOrDrop(ItemStack stack, BlockPos spillPos)
        {
            for (int i = OutputSlotFirst; i <= OutputSlotLast; i++)
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
            for (int i = OutputSlotFirst; i <= OutputSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = stack.Clone();
                s.MarkDirty();
                return;
            }
            Vec3d at = new Vec3d(spillPos.X + 0.5, spillPos.Y + 0.5, spillPos.Z + 0.5);
            Api.World.SpawnItemEntity(stack, at);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            inventory?.FromTreeAttributes(tree);
            drillDepth = tree.GetInt("drillDepth", 0);
            halted = tree.GetBool("halted", false);
            retracting = tree.GetBool("retracting", false);
            paused = tree.GetBool("paused", false);

            deployedShafts.Clear();
            ITreeAttribute shaftsTree = tree.GetTreeAttribute("deployedShafts");
            if (shaftsTree != null)
            {
                int count = shaftsTree.GetInt("count", 0);
                for (int i = 0; i < count; i++)
                {
                    ItemStack stack = shaftsTree.GetItemstack("s" + i);
                    if (stack != null)
                    {
                        if (worldAccessForResolve != null) stack.ResolveBlockOrItem(worldAccessForResolve);
                        deployedShafts.Add(stack);
                    }
                }
            }

            if (Api != null) inventory?.ResolveBlocksOrItems();
            clientDialog?.OnStateUpdated();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            tree.SetInt("drillDepth", drillDepth);
            tree.SetBool("halted", halted);
            tree.SetBool("retracting", retracting);
            tree.SetBool("paused", paused);

            ITreeAttribute shaftsTree = new TreeAttribute();
            shaftsTree.SetInt("count", deployedShafts.Count);
            for (int i = 0; i < deployedShafts.Count; i++)
            {
                shaftsTree.SetItemstack("s" + i, deployedShafts[i]);
            }
            tree["deployedShafts"] = shaftsTree;
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagekinematics:bore-depth", drillDepth));
            if (retracting) dsc.AppendLine(Lang.Get("vintagekinematics:bore-retracting"));
            else if (paused) dsc.AppendLine(Lang.Get("vintagekinematics:bore-paused"));
            else if (halted) dsc.AppendLine(Lang.Get("vintagekinematics:bore-halted"));
        }

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

        private void DisposeDialog()
        {
            if (clientDialog == null) return;
            if (clientDialog.IsOpened()) clientDialog.TryClose();
            clientDialog.Dispose();
            clientDialog = null;
        }
    }
}
