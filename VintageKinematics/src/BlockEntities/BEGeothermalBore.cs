using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEGeothermalBore : BlockEntity, IGeothermalHeatProvider
    {
        public const int RodSlotFirst = 0;
        public const int RodSlotLast = 2;
        public const int PipeSlotFirst = 3;
        public const int PipeSlotLast = 5;
        public const int InventorySize = 6;

        public const int PacketIdOpenDialog = 5520;
        public const int PacketIdToggleRetract = 5521;

        private const float UnbreakableResistance = 50f;
        private const int DefaultMiningTier = 5;

        private readonly InventoryGeneric inventory;
        private readonly List<ItemStack> deployedRods = new List<ItemStack>();
        private readonly List<ItemStack> deployedPipes = new List<ItemStack>();
        private readonly List<BlockPos> placedPipePositions = new List<BlockPos>();

        private int drillDepth;
        private bool halted;
        private bool retracting;
        private bool paused;
        private bool tapped;
        private int miningTier;

        private GuiDialogGeothermalBore clientDialog;
        private static int geothermalPipeBlockId = -1;

        public InventoryBase Inventory => inventory;
        public int DrillDepth => drillDepth;
        public bool Halted => halted;
        public bool Retracting => retracting;
        public bool Paused => paused;
        public bool IsTapped => tapped;

        public bool CanProvideHeatTo(BlockPos consumerPos)
        {
            if (!tapped || consumerPos == null) return false;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size)) return false;

            BlockFacing face = HeatOutputFace();
            BlockPos outputCell = HeatOutputCell(baseCorner, size, face);
            return outputCell != null && outputCell.AddCopy(face).Equals(consumerPos);
        }

        public BEGeothermalBore()
        {
            inventory = new InventoryGeneric(InventorySize, "geothermalbore-0", null, null, (slotId, self) =>
            {
                if (slotId <= RodSlotLast) return new ItemSlotShaftInput(self);
                return new ItemSlotGeothermalPipeInput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("geothermalbore-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ => Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();

            miningTier = Block?.Attributes?["miningTier"].AsInt(DefaultMiningTier) ?? DefaultMiningTier;

            Block pipeBlock = api.World.GetBlock(new AssetLocation("vintagekinematics:geothermalpipe"));
            if (geothermalPipeBlockId < 0 && pipeBlock != null) geothermalPipeBlockId = pipeBlock.Id;

            if (api.Side == EnumAppSide.Server)
            {
                var worker = GetBehavior<BEBehaviorKineticWorker>();
                if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
                if (placedPipePositions.Count == 0 && drillDepth > 0) RebuildPlacedPipePositions();
                if (drillDepth <= 0) AdoptExistingPipeColumn();
                SetGlowState(tapped);
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
                string title = Lang.Get("vintagekinematics:geothermalbore-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:geothermalbore-title") title = "Geothermal Bore";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(drillDepth);
                bw.Write(halted);
                bw.Write(retracting);
                bw.Write(paused);
                bw.Write(tapped);
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
            if (packetid == PacketIdToggleRetract)
            {
                if (!CheckClaim(player)) return;
                if (retracting)
                {
                    retracting = false;
                    paused = true;
                }
                else if (paused && !tapped)
                {
                    paused = false;
                }
                else
                {
                    retracting = true;
                    halted = false;
                    SetTapped(false);
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
            Api.World.Logger.Audit("Player {0} sent geothermal bore packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
            tapped = br.ReadBoolean();
            var tree = new TreeAttribute();
            tree.FromBytes(br);
            inventory.FromTreeAttributes(tree);
            inventory.ResolveBlocksOrItems();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogGeothermalBore(
                    title, inventory, Pos,
                    () => retracting, () => halted, () => paused, () => tapped, () => drillDepth,
                    OnClientToggleRetract, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.OnStateUpdated();
            }
        }

        private void OnClientToggleRetract()
        {
            if (retracting) { retracting = false; paused = true; }
            else if (paused && !tapped) { paused = false; }
            else { retracting = true; halted = false; tapped = false; }
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleRetract);
            clientDialog?.OnStateUpdated();
        }

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            if (Api.Side != EnumAppSide.Server) return;
            if (retracting)
            {
                StepRetract();
                return;
            }
            if (paused || halted || tapped) return;
            StepDescent();
        }

        private void StepRetract()
        {
            if (drillDepth <= 0)
            {
                retracting = false;
                paused = true;
                SetTapped(false);
                drillDepth = 0;
                deployedRods.Clear();
                deployedPipes.Clear();
                placedPipePositions.Clear();
                MarkDirty(true);
                return;
            }

            ItemStack rod = null;
            if (deployedRods.Count > 0)
            {
                int lastIdx = deployedRods.Count - 1;
                rod = deployedRods[lastIdx];
                deployedRods.RemoveAt(lastIdx);
            }
            ItemStack pipe = null;
            if (deployedPipes.Count > 0)
            {
                int lastIdx = deployedPipes.Count - 1;
                pipe = deployedPipes[lastIdx];
                deployedPipes.RemoveAt(lastIdx);
            }
            drillDepth--;
            ItemStack returnedPipe = RemoveVisualPipe(pipe);

            if (rod != null && rod.StackSize > 0) ReturnRodToInput(rod);
            if (returnedPipe != null && returnedPipe.StackSize > 0) ReturnPipeToInput(returnedPipe);
            MarkDirty(true);
        }

        private void StepDescent()
        {
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out _))
            {
                halted = true;
                MarkDirty(true);
                return;
            }

            int targetY = baseCorner.Y - 1 - drillDepth;
            if (targetY <= 1)
            {
                SetTapped(true);
                paused = true;
                halted = false;
                MarkDirty(true);
                return;
            }

            BlockPos targetPos = CenterColumnPos(baseCorner, targetY);
            Block targetBlock = Api.World.BlockAccessor.GetBlock(targetPos);
            if (IsBedrock(targetBlock))
            {
                SetTapped(true);
                paused = true;
                halted = false;
                MarkDirty(true);
                return;
            }
            if (targetBlock != null && targetBlock.Id != 0 && IsUnbreakable(targetBlock))
            {
                halted = true;
                MarkDirty(true);
                return;
            }

            ItemSlot rodSlot = FindRodSlot();
            ItemSlot pipeSlot = FindPipeSlot();
            if (rodSlot == null || pipeSlot == null) return;

            ItemStack consumedRod = rodSlot.TakeOut(1);
            rodSlot.MarkDirty();
            ItemStack consumedPipe = pipeSlot.TakeOut(1);
            pipeSlot.MarkDirty();
            if (consumedRod != null) deployedRods.Add(consumedRod);
            if (consumedPipe != null) deployedPipes.Add(consumedPipe);

            PlaceVisualPipe(targetPos);
            drillDepth++;
            MarkDirty(true);
        }

        private BlockPos CenterColumnPos(BlockPos baseCorner, int y) =>
            new BlockPos(baseCorner.X + 1, y, baseCorner.Z + 1, Pos.dimension);

        private void SetTapped(bool value)
        {
            tapped = value;
            SetGlowState(value);
        }

        private void SetGlowState(bool glow)
        {
            if (Api?.Side != EnumAppSide.Server) return;
            string desired = glow ? "glow" : "cool";
            if (Block?.Variant?["state"] == desired) return;

            Block target = Api.World.GetBlock(Block.CodeWithVariant("state", desired));
            if (target == null || target.BlockId == Block.BlockId) return;

            Api.World.BlockAccessor.ExchangeBlock(target.BlockId, Pos);
        }

        private BlockFacing HeatOutputFace()
        {
            return BlockFacing.FromFirstLetter(Block?.Variant?["side"] ?? "n") ?? BlockFacing.NORTH;
        }

        private BlockPos HeatOutputCell(BlockPos baseCorner, Vec3i size, BlockFacing face)
        {
            int midX = baseCorner.X + size.X / 2;
            int midZ = baseCorner.Z + size.Z / 2;
            int y = baseCorner.Y;
            if (face == BlockFacing.NORTH) return new BlockPos(midX, y, baseCorner.Z, Pos.dimension);
            if (face == BlockFacing.SOUTH) return new BlockPos(midX, y, baseCorner.Z + size.Z - 1, Pos.dimension);
            if (face == BlockFacing.EAST) return new BlockPos(baseCorner.X + size.X - 1, y, midZ, Pos.dimension);
            if (face == BlockFacing.WEST) return new BlockPos(baseCorner.X, y, midZ, Pos.dimension);
            return null;
        }

        private void PlaceVisualPipe(BlockPos columnPos)
        {
            if (geothermalPipeBlockId < 0) return;
            Api.World.BlockAccessor.SetBlock(geothermalPipeBlockId, columnPos);
            placedPipePositions.Add(columnPos.Copy());
        }

        private ItemStack RemoveVisualPipe(ItemStack deployedPipe)
        {
            if (placedPipePositions.Count == 0) return null;
            int lastIdx = placedPipePositions.Count - 1;
            BlockPos columnPos = placedPipePositions[lastIdx];
            placedPipePositions.RemoveAt(lastIdx);
            Block here = Api.World.BlockAccessor.GetBlock(columnPos);
            if (here?.Id != geothermalPipeBlockId) return null;

            Api.World.BlockAccessor.SetBlock(0, columnPos);
            if (deployedPipe != null) return deployedPipe;
            return new ItemStack(here, 1);
        }

        private ItemSlot FindRodSlot()
        {
            for (int i = RodSlotFirst; i <= RodSlotLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (ItemSlotShaftInput.IsAcceptedCode(slot.Itemstack?.Collectible?.Code)) return slot;
            }
            return null;
        }

        private ItemSlot FindPipeSlot()
        {
            for (int i = PipeSlotFirst; i <= PipeSlotLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (ItemSlotGeothermalPipeInput.IsAcceptedCode(slot.Itemstack?.Collectible?.Code)) return slot;
            }
            return null;
        }

        private void ReturnRodToInput(ItemStack rod)
        {
            for (int i = RodSlotFirst; i <= RodSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (!s.Itemstack.Collectible.Code.Equals(rod.Collectible.Code)) continue;
                int max = s.Itemstack.Collectible.MaxStackSize;
                int free = max - s.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, rod.StackSize);
                s.Itemstack.StackSize += take;
                rod.StackSize -= take;
                s.MarkDirty();
                if (rod.StackSize <= 0) return;
            }
            for (int i = RodSlotFirst; i <= RodSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = rod.Clone();
                s.MarkDirty();
                return;
            }
            Api.World.SpawnItemEntity(rod, new Vec3d(Pos.X + 0.5, Pos.Y + 1.1, Pos.Z + 0.5));
        }

        private void ReturnPipeToInput(ItemStack pipe)
        {
            for (int i = PipeSlotFirst; i <= PipeSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (!s.Itemstack.Collectible.Code.Equals(pipe.Collectible.Code)) continue;
                int max = s.Itemstack.Collectible.MaxStackSize;
                int free = max - s.Itemstack.StackSize;
                if (free <= 0) continue;
                int take = System.Math.Min(free, pipe.StackSize);
                s.Itemstack.StackSize += take;
                pipe.StackSize -= take;
                s.MarkDirty();
                if (pipe.StackSize <= 0) return;
            }
            for (int i = PipeSlotFirst; i <= PipeSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (!s.Empty) continue;
                s.Itemstack = pipe.Clone();
                s.MarkDirty();
                return;
            }
            Api.World.SpawnItemEntity(pipe, new Vec3d(Pos.X + 0.5, Pos.Y + 1.1, Pos.Z + 0.5));
        }

        private bool IsBedrock(Block block)
        {
            return block?.Code?.Path != null && block.Code.Path.Contains("bedrock");
        }

        private bool IsUnbreakable(Block block)
        {
            if (block.Resistance > UnbreakableResistance) return true;
            if (block.RequiredMiningTier > miningTier) return true;
            return false;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            inventory?.FromTreeAttributes(tree);
            drillDepth = tree.GetInt("drillDepth", 0);
            halted = tree.GetBool("halted", false);
            retracting = tree.GetBool("retracting", false);
            paused = tree.GetBool("paused", false);
            tapped = tree.GetBool("tapped", false);

            deployedRods.Clear();
            ITreeAttribute rodsTree = tree.GetTreeAttribute("deployedRods");
            if (rodsTree != null)
            {
                int count = rodsTree.GetInt("count", 0);
                for (int i = 0; i < count; i++)
                {
                    ItemStack stack = rodsTree.GetItemstack("s" + i);
                    if (stack != null)
                    {
                        if (worldAccessForResolve != null) stack.ResolveBlockOrItem(worldAccessForResolve);
                        deployedRods.Add(stack);
                    }
                }
            }

            deployedPipes.Clear();
            ITreeAttribute deployedPipesTree = tree.GetTreeAttribute("deployedPipes");
            if (deployedPipesTree != null)
            {
                int count = deployedPipesTree.GetInt("count", 0);
                for (int i = 0; i < count; i++)
                {
                    ItemStack stack = deployedPipesTree.GetItemstack("s" + i);
                    if (stack != null)
                    {
                        if (worldAccessForResolve != null) stack.ResolveBlockOrItem(worldAccessForResolve);
                        deployedPipes.Add(stack);
                    }
                }
            }

            placedPipePositions.Clear();
            ITreeAttribute pipesTree = tree.GetTreeAttribute("placedPipePositions");
            if (pipesTree != null)
            {
                int count = pipesTree.GetInt("count", 0);
                for (int i = 0; i < count; i++)
                {
                    placedPipePositions.Add(new BlockPos(
                        pipesTree.GetInt("x" + i),
                        pipesTree.GetInt("y" + i),
                        pipesTree.GetInt("z" + i),
                        pipesTree.GetInt("d" + i, Pos.dimension)));
                }
            }

            if (Api != null) inventory?.ResolveBlocksOrItems();
            if (Api != null && Api.Side == EnumAppSide.Server && placedPipePositions.Count == 0 && drillDepth > 0) RebuildPlacedPipePositions();
            clientDialog?.OnStateUpdated();
        }

        private void RebuildPlacedPipePositions()
        {
            placedPipePositions.Clear();
            if (Api == null || Api.Side != EnumAppSide.Server || drillDepth <= 0) return;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out _)) return;
            for (int d = 1; d <= drillDepth; d++)
            {
                placedPipePositions.Add(CenterColumnPos(baseCorner, baseCorner.Y - d));
            }
        }

        private void AdoptExistingPipeColumn()
        {
            if (Api == null || Api.Side != EnumAppSide.Server || geothermalPipeBlockId < 0) return;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out _)) return;

            placedPipePositions.Clear();
            for (int y = baseCorner.Y - 1; y > 0; y--)
            {
                BlockPos columnPos = CenterColumnPos(baseCorner, y);
                Block here = Api.World.BlockAccessor.GetBlock(columnPos);
                if (here?.Id != geothermalPipeBlockId) break;
                placedPipePositions.Add(columnPos);
            }

            if (placedPipePositions.Count <= 0) return;
            drillDepth = placedPipePositions.Count;
            halted = false;
            retracting = false;
            paused = false;
            SetTapped(IsTapComplete(baseCorner));
            if (tapped) paused = true;
            MarkDirty(true);
        }

        private bool IsTapComplete(BlockPos baseCorner)
        {
            int nextY = baseCorner.Y - 1 - drillDepth;
            if (nextY <= 1) return true;
            Block next = Api.World.BlockAccessor.GetBlock(CenterColumnPos(baseCorner, nextY));
            return IsBedrock(next);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory?.ToTreeAttributes(tree);
            tree.SetInt("drillDepth", drillDepth);
            tree.SetBool("halted", halted);
            tree.SetBool("retracting", retracting);
            tree.SetBool("paused", paused);
            tree.SetBool("tapped", tapped);

            ITreeAttribute rodsTree = new TreeAttribute();
            rodsTree.SetInt("count", deployedRods.Count);
            for (int i = 0; i < deployedRods.Count; i++)
            {
                rodsTree.SetItemstack("s" + i, deployedRods[i]);
            }
            tree["deployedRods"] = rodsTree;

            ITreeAttribute deployedPipesTree = new TreeAttribute();
            deployedPipesTree.SetInt("count", deployedPipes.Count);
            for (int i = 0; i < deployedPipes.Count; i++)
            {
                deployedPipesTree.SetItemstack("s" + i, deployedPipes[i]);
            }
            tree["deployedPipes"] = deployedPipesTree;

            ITreeAttribute pipesTree = new TreeAttribute();
            pipesTree.SetInt("count", placedPipePositions.Count);
            for (int i = 0; i < placedPipePositions.Count; i++)
            {
                BlockPos p = placedPipePositions[i];
                pipesTree.SetInt("x" + i, p.X);
                pipesTree.SetInt("y" + i, p.Y);
                pipesTree.SetInt("z" + i, p.Z);
                pipesTree.SetInt("d" + i, p.dimension);
            }
            tree["placedPipePositions"] = pipesTree;
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagekinematics:geothermalbore-depth", drillDepth));
            if (tapped) dsc.AppendLine(Lang.Get("vintagekinematics:geothermalbore-tapped"));
            else if (retracting) dsc.AppendLine(Lang.Get("vintagekinematics:bore-retracting"));
            else if (paused) dsc.AppendLine(Lang.Get("vintagekinematics:bore-paused"));
            else if (halted) dsc.AppendLine(Lang.Get("vintagekinematics:geothermalbore-halted"));
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }

        public override void OnBlockRemoved()
        {
            // Leave the deployed pipe in-world. A replacement bore placed over the same center
            // column adopts those pipe blocks on initialize and can retract them instead of
            // voiding the installed tap materials when the controller is broken.
            if (Api?.Side == EnumAppSide.Server)
            {
                placedPipePositions.Clear();
                deployedRods.Clear();
                deployedPipes.Clear();
            }
            base.OnBlockRemoved();
            GuiDialogUtil.SafeDispose(ref clientDialog);
        }
    }
}
