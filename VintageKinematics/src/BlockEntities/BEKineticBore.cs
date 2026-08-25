using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Vertical drill: chews the 3×3 column directly below the multiblock footprint one layer
    /// per work cycle, depositing drops into an output-only 9-slot buffer. Each descent deploys
    /// one drill rod from the 3-slot input row. The drill halts permanently on
    /// bedrock / out-of-mining-tier blocks. Right-clicking opens the dialog with a Retract button
    /// that walks the bore back to the surface, returning one drill rod per layer.
    /// </summary>
    public class BEKineticBore : BEBoreBase, IFaceMappedContainer
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

        private int miningTier;
        // Stack of rod items consumed on descent. Popped back into the input row during retract
        // so the player always recovers what they put in (subject to inventory space).
        private readonly List<ItemStack> deployedShafts = new List<ItemStack>();

        private BoreDrillDescentRenderer descentRenderer;
        private IOFaceMap ioFaces;
        private static int boreShaftBlockId = -1;

        public IOFaceMap IOFaces => ioFaces;

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override int ToggleRetractPacketId => PacketIdToggleRetract;
        protected override string TitleLangCode => "vintagekinematics:kineticbore-title";
        protected override string FallbackTitle => "Kinetic Bore";
        public override bool HasUnretractedColumn => base.HasUnretractedColumn || deployedShafts.Count > 0;

        public BEKineticBore()
            : base("kineticbore", InventorySize, (slotId, self) =>
            {
                if (slotId <= InputSlotLast) return new ItemSlotShaftInput(self);
                return new ItemSlotCrusherOutput(self);
            })
        {
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            miningTier = Block?.Attributes?["miningTier"].AsInt(DefaultMiningTier) ?? DefaultMiningTier;

            Block shaftBlock = api.World.GetBlock(new AssetLocation("vintagekinematics:kineticboreshaft"));
            if (boreShaftBlockId < 0 && shaftBlock != null) boreShaftBlockId = shaftBlock.Id;

            BuildIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                var worker = GetBehavior<BEBehaviorKineticWorker>();
                if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
                // FromTreeAttributes runs before Initialize on world load, with Api still null,
                // so its RebuildPlacedColumnPositionsFromDepth call early-exits and the tracking list is
                // never populated. Without this re-call, breaking a bore that was descended in
                // a previous session would leave the shaft column orphaned forever.
                RebuildPlacedColumnPositionsFromDepth();
                if (drillDepth <= 0) AdoptExistingShaftColumn();
            }

            if (api is ICoreClientAPI capi)
            {
                var kineticBeh = GetBehavior<BEBehaviorKinetic>();
                descentRenderer = new BoreDrillDescentRenderer(capi, Pos, this, kineticBeh, shaftBlock);
                capi.Event.RegisterRenderer(descentRenderer, EnumRenderStage.Opaque);
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            // CollectManagedElements only knows about behaviors (animator/piston). The descent
            // renderer is on the BE itself and owns DrillAssembly independently, so we have to
            // append it to the exclusion list by hand — otherwise the static body mesh would
            // render the drill at its baked position while the renderer draws a second copy
            // at the descended position.
            string[] managed = KineticMeshSplitter.CollectManagedElements(this);
            string[] excluded = new string[managed.Length + 1];
            System.Array.Copy(managed, excluded, managed.Length);
            excluded[managed.Length] = "DrillAssembly";
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticBore(
                title, inventory, Pos,
                () => retracting, () => halted, () => paused, () => drillDepth,
                OnClientToggleRetract, capi);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog) =>
            (dialog as GuiDialogKineticBore)?.OnStateUpdated();

        private void OnClientToggleRetract()
        {
            // Optimistic UI flip — server still re-sends authoritative state on next tick if needed.
            // Mirror the three-state cycle in the server-side handler.
            if (retracting) { retracting = false; paused = true; }
            else if (paused) { paused = false; }
            else { retracting = true; halted = false; }
            SendClientToggleRetractPacket();
            RefreshClientDialog();
        }

        // Declarative IO surface: shaft inputs map to whichever multiblock-aware shaft stubs the
        // BEBehaviorKineticMultiblock already exposes (handled elsewhere), and the output buffer is
        // mapped to the upper-back-center cell pushing outward on the back face. The push tick just
        // iterates the declared outputs; per-variant coord math lives here in one place so neighbour
        // pipes / funnels see a consistent cell-aware IO surface.
        private void BuildIOFaceMap()
        {
            ioFaces = MachineIoLayouts.MultiblockUpperBackCenterOutput(Block, Pos, OutputSlotFirst, OutputSlotLast);
            ioFaces.Apply(inventory);
        }

        // Push out the back face — opposite the shaft input — from the upper (non-spinning) cell so
        // a funnel, chest, or belt placed against the back of the bore drains automatically. The
        // upper layer is the static housing; the lower layer holds the rotating drill bit, so
        // anchoring the push origin on the upper cell avoids visual conflict with the drum.
        private void OnServerPushTick(float dt)
        {
            MachineOutputHelper.FlushOutputs(this, inventory, ioFaces?.OutputEntries, OutputPushBatch);
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
            if (drillDepth <= 0)
            {
                retracting = false;
                // Pause on completion so the bore doesn't race the player and eat the rods that
                // were just returned to the input row. Player resumes by clicking the button again.
                paused = true;
                drillDepth = 0;
                deployedShafts.Clear();
                MarkDirty(true);
                return;
            }

            ItemStack shaft = null;
            if (deployedShafts.Count > 0)
            {
                int lastIdx = deployedShafts.Count - 1;
                shaft = deployedShafts[lastIdx];
                deployedShafts.RemoveAt(lastIdx);
            }
            drillDepth--;
            // Pop the visual shaft at the layer the drill is rising through, before the depth
            // change reaches the client. Otherwise the renderer would briefly draw the drill mesh
            // overlapping the still-present fake block.
            ItemStack returnedShaft = RemoveVisualShaft(shaft);

            if (returnedShaft != null && returnedShaft.StackSize > 0) ReturnShaftToInput(returnedShaft);
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
                    if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, check, EnumBlockAccessFlags.BuildOrBreak))
                    {
                        halted = true;
                        MarkDirty(true);
                        return;
                    }
                    if (IsUnbreakable(b))
                    {
                        halted = true;
                        MarkDirty(true);
                        return;
                    }
                    StorageRemovalCheck removal = KineticStorageRemovalService.Check(
                        Api.World,
                        check,
                        StorageRemovalKind.BlockReplacement);
                    if (!removal.Allowed)
                    {
                        halted = true;
                        MarkDirty(true);
                        return;
                    }
                }
            }

            // Consume the drill rod for this layer before mining so failures past this point still
            // bank progress correctly. The popped rod is tracked on the deployed stack so retract
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
            // Shaft placed at every mined cell, including the very first descent. The drill mesh
            // only occupies the top quarter of its cell (the cutter plate sits where the housing's
            // drill stub used to be), so the bottom 3/4 needs the shaft block to fill the chamber
            // visually. The drill renders on top of the shaft — reads as "drill bit hanging off
            // the bottom of the drill string".
            int shaftY = baseCorner.Y - drillDepth;
            PlaceVisualShaft(baseCorner, shaftY);
            MarkDirty(true);
        }

        // World-space X/Z of the bore's central drill column. Used by the descent renderer
        // to draw the shaft fill-mesh in the housing notch at the correct cell regardless
        // of which corner the controller occupies for this rotation variant.
        public bool TryGetCenterColumnXZ(out int x, out int z)
        {
            if (Block != null && MultiblockHelper.TryGetClaim(Block, Pos, out BlockPos baseCorner, out _))
            {
                x = baseCorner.X + 1;
                z = baseCorner.Z + 1;
                return true;
            }
            x = Pos.X;
            z = Pos.Z;
            return false;
        }

        private void PlaceVisualShaft(BlockPos baseCorner, int atY)
        {
            BlockPos columnPos = CenterColumnPos(baseCorner, atY);
            TryPlaceTrackedColumnBlock(columnPos, boreShaftBlockId);
        }

        private ItemStack RemoveVisualShaft(ItemStack deployedShaft)
        {
            return RemoveTrackedColumnBlock(boreShaftBlockId, deployedShaft, _ =>
            {
                Item drillRod = Api.World.GetItem(new AssetLocation("vintagekinematics:drillrod"));
                return drillRod == null ? null : new ItemStack(drillRod, 1);
            });
        }

        private ItemSlot FindShaftSlot()
        {
            for (int i = InputSlotFirst; i <= InputSlotLast; i++)
            {
                ItemSlot s = inventory[i];
                if (s.Empty) continue;
                if (ItemSlotShaftInput.IsAcceptedCode(s.Itemstack?.Collectible?.Code)) return s;
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
            Vec3d at = new Vec3d(spillPos.X + 0.5, spillPos.Y + 0.5, spillPos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, inventory, OutputSlotFirst, OutputSlotLast, stack, ioFaces?.OutputEntries, OutputPushBatch, at);
        }

        protected override void ReadExtraTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
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
        }

        protected override void OnAfterTreeAttributesLoaded(IWorldAccessor worldAccessForResolve)
        {
            RebuildPlacedColumnPositionsFromDepth();
        }

        private void AdoptExistingShaftColumn()
        {
            if (!TryAdoptExistingColumn(boreShaftBlockId, out _)) return;
            drillDepth = PlacedColumnPositions.Count;
            halted = false;
            retracting = false;
            paused = false;
            MarkDirty(true);
        }

        protected override void WriteExtraTreeAttributes(ITreeAttribute tree)
        {
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
            DisposeDescentRenderer();
        }

        public override void OnBlockRemoved()
        {
            // Leave the deployed column in-world. A replacement bore placed over the same
            // center column adopts those shaft blocks on initialize, letting players recover
            // from accidentally breaking the controller without voiding drill rods.
            if (Api?.Side == EnumAppSide.Server)
            {
                ClearPlacedColumnPositions();
                deployedShafts.Clear();
            }
            base.OnBlockRemoved();
            DisposeDescentRenderer();
        }

        private void DisposeDescentRenderer()
        {
            if (descentRenderer == null) return;
            if (Api is ICoreClientAPI capi) capi.Event.UnregisterRenderer(descentRenderer, EnumRenderStage.Opaque);
            descentRenderer.Dispose();
            descentRenderer = null;
        }

    }
}
