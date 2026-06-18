using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEGeothermalBore : BEBoreBase, IGeothermalHeatProvider
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

        private readonly List<ItemStack> deployedRods = new List<ItemStack>();
        private readonly List<ItemStack> deployedPipes = new List<ItemStack>();

        private bool tapped;
        private int miningTier;

        private static int geothermalPipeBlockId = -1;

        public bool IsTapped => tapped;

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override int ToggleRetractPacketId => PacketIdToggleRetract;
        protected override string TitleLangCode => "vintagekinematics:geothermalbore-title";
        protected override string FallbackTitle => "Geothermal Bore";

        public bool CanProvideHeatTo(BlockPos consumerPos)
        {
            if (!tapped || consumerPos == null) return false;
            if (!MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out Vec3i size)) return false;

            BlockFacing face = HeatOutputFace();
            BlockPos outputCell = MultiblockHelper.CellAtFaceCenter(baseCorner, size, face, baseCorner.Y, Pos.dimension);
            return outputCell != null && outputCell.AddCopy(face).Equals(consumerPos);
        }

        public BEGeothermalBore()
            : base("geothermalbore", InventorySize, (slotId, self) =>
            {
                if (slotId <= RodSlotLast) return new ItemSlotShaftInput(self);
                return new ItemSlotGeothermalPipeInput(self);
            })
        {
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            miningTier = Block?.Attributes?["miningTier"].AsInt(DefaultMiningTier) ?? DefaultMiningTier;

            Block pipeBlock = api.World.GetBlock(new AssetLocation("vintagekinematics:geothermalpipe"));
            if (geothermalPipeBlockId < 0 && pipeBlock != null) geothermalPipeBlockId = pipeBlock.Id;

            if (api.Side == EnumAppSide.Server)
            {
                var worker = GetBehavior<BEBehaviorKineticWorker>();
                if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
                if (PlacedColumnPositions.Count == 0 && drillDepth > 0) RebuildPlacedColumnPositionsFromDepth();
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

        protected override void WriteExtraDialogState(BinaryWriter writer)
        {
            writer.Write(tapped);
        }

        protected override void ReadExtraDialogState(BinaryReader reader)
        {
            tapped = reader.ReadBoolean();
        }

        protected override void OnServerToggleRetract()
        {
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
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogGeothermalBore(
                title, inventory, Pos,
                () => retracting, () => halted, () => paused, () => tapped, () => drillDepth,
                OnClientToggleRetract, capi);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog) =>
            (dialog as GuiDialogGeothermalBore)?.OnStateUpdated();

        private void OnClientToggleRetract()
        {
            if (retracting) { retracting = false; paused = true; }
            else if (paused && !tapped) { paused = false; }
            else { retracting = true; halted = false; tapped = false; }
            SendClientToggleRetractPacket();
            RefreshClientDialog();
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
                ClearPlacedColumnPositions();
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
            if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, targetPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                halted = true;
                MarkDirty(true);
                return;
            }
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

        private void PlaceVisualPipe(BlockPos columnPos)
        {
            TryPlaceTrackedColumnBlock(columnPos, geothermalPipeBlockId);
        }

        private ItemStack RemoveVisualPipe(ItemStack deployedPipe)
        {
            return RemoveTrackedColumnBlock(geothermalPipeBlockId, deployedPipe, here => new ItemStack(here, 1));
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

        protected override void ReadExtraTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
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

            ReadPlacedColumnPositions(tree, "placedPipePositions");
        }

        protected override void OnAfterTreeAttributesLoaded(IWorldAccessor worldAccessForResolve)
        {
            if (Api != null && Api.Side == EnumAppSide.Server && PlacedColumnPositions.Count == 0 && drillDepth > 0) RebuildPlacedColumnPositionsFromDepth();
        }

        private void AdoptExistingPipeColumn()
        {
            if (!TryAdoptExistingColumn(geothermalPipeBlockId, out BlockPos baseCorner)) return;
            drillDepth = PlacedColumnPositions.Count;
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

        protected override void WriteExtraTreeAttributes(ITreeAttribute tree)
        {
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

            WritePlacedColumnPositions(tree, "placedPipePositions");
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
        }

        public override void OnBlockRemoved()
        {
            // Leave the deployed pipe in-world. A replacement bore placed over the same center
            // column adopts those pipe blocks on initialize and can retract them instead of
            // voiding the installed tap materials when the controller is broken.
            if (Api?.Side == EnumAppSide.Server)
            {
                ClearPlacedColumnPositions();
                deployedRods.Clear();
                deployedPipes.Clear();
            }
            base.OnBlockRemoved();
        }
    }
}
