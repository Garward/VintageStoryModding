using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BECopperPump : BEKineticAnimated
    {
        private const float LitresPerSecondAt16Rpm = 0.1f;
        private const float TickSeconds = 0.25f;
        private const float MaxBufferedLitres = WorldLiquidPumpPolicy.LavaSourceLitres;
        private const float MaxOrdinaryTransferLitres = 1f;
        private const int MaxPipeDistance = 16;
        private const int FilterSlotCount = 1;

        public const int PacketIdOpenDialog = 5500;
        public const int PacketIdToggleMode = 5501;
        public const int PacketIdSetFilter = 5502;
        public const int PacketIdToggleFuzzy = 5503;

        private static readonly BlockFacing[] PipeFaces =
        {
            BlockFacing.NORTH,
            BlockFacing.EAST,
            BlockFacing.SOUTH,
            BlockFacing.WEST,
            BlockFacing.UP,
            BlockFacing.DOWN
        };

        private float litreBudget;
        private int nextSinkIndex;
        private int nextSourceIndex;
        private string status = "idle";
        private float lastMovedLitres;
        private readonly FilterDialogController filter;

        public BECopperPump()
        {
            filter = new FilterDialogController(
                this,
                new InventoryLiquidFilter(FilterSlotCount, "copperpumpfilter", null),
                "copperpumpfilter",
                "vintagekinematics:copperpump-filter-title",
                "Pump Filter",
                "copper pump",
                PacketIdOpenDialog,
                PacketIdToggleMode,
                PacketIdSetFilter,
                PacketIdToggleFuzzy,
                () => FilterSlotCount,
                centerSingleSlot: () => true,
                validateFilterStack: stack => BlockLiquidContainerBase.GetContainableProps(stack) != null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filter.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, (int)(TickSeconds * 1000));
        }

        private void OnServerTick(float dt)
        {
            lastMovedLitres = 0f;

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = MathF.Abs(kinetic?.ActualRPM ?? 0f);
            if (kinetic == null || kinetic.IsConflicted || (kinetic.EffectiveNetwork?.IsOverstressed ?? false) || rpm < KineticNetwork.MinAbsRPM)
            {
                status = "nopower";
                MarkDirty(true);
                return;
            }

            BlockFacing outputFace = OutputFace();
            BlockFacing inputFace = outputFace.Opposite;
            List<PumpInput> inputs = FindSources(inputFace);
            if (inputs.Count == 0)
            {
                status = "nosource";
                MarkDirty(true);
                return;
            }

            litreBudget = Math.Min(MaxBufferedLitres, litreBudget + MathF.Max(0.001f, dt) * LitresPerSecondAt16Rpm * rpm / 16f);
            bool hadUsableSource = false;
            bool hadCompatibleSink = false;
            bool hadBlockedSink = false;
            bool hadFilteredSource = false;
            bool isChargingLavaTransfer = false;
            bool isChargingWorldOutput = false;

            int sourceCount = inputs.Count;
            int sourceStart = Math.Abs(nextSourceIndex) % sourceCount;
            for (int offset = 0; offset < sourceCount; offset++)
            {
                int sourceIndex = (sourceStart + offset) % sourceCount;
                PumpInput input = inputs[sourceIndex];
                if (!MatchesFilter(input.Stack))
                {
                    hadFilteredSource = true;
                    continue;
                }

                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(input.Stack);
                if (props == null || props.ItemsPerLitre <= 0f) continue;

                if (input.IsWorldLava)
                {
                    hadUsableSource = true;
                    if (litreBudget + 0.0001f < WorldLiquidPumpPolicy.LavaSourceLitres)
                    {
                        isChargingLavaTransfer = true;
                        continue;
                    }

                    int sourceItems = (int)MathF.Round(WorldLiquidPumpPolicy.LavaSourceLitres * props.ItemsPerLitre);
                    ItemStack lavaStack = input.Stack.Clone();
                    lavaStack.StackSize = sourceItems;
                    List<PumpSink> lavaSinks = FindSinks(outputFace, lavaStack, requireIronTank: true);
                    if (lavaSinks.Count == 0) continue;

                    hadCompatibleSink = true;
                    if (!TryCommitWorldLava(input, lavaSinks, lavaStack))
                    {
                        hadBlockedSink = true;
                        continue;
                    }

                    lastMovedLitres = WorldLiquidPumpPolicy.LavaSourceLitres;
                    litreBudget = MathF.Max(0f, litreBudget - lastMovedLitres);
                    nextSourceIndex = (sourceIndex + 1) % sourceCount;
                    status = "active";
                    MarkDirty(true);
                    return;
                }

                float ordinaryTransferBudget = Math.Min(litreBudget, MaxOrdinaryTransferLitres);
                int desiredItems = Math.Min(input.AvailableItems, ItemsForBudget(ordinaryTransferBudget, props.ItemsPerLitre));
                if (desiredItems <= 0)
                {
                    hadUsableSource = true;
                    continue;
                }

                hadUsableSource = true;
                ItemStack transferStack = input.Stack.Clone();
                transferStack.StackSize = desiredItems;

                List<PumpSink> sinks = FindSinks(outputFace, transferStack, requireIronTank: false);
                if (sinks.Count == 0)
                {
                    int sourceItems = (int)MathF.Round(WorldLiquidPumpPolicy.LavaSourceLitres * props.ItemsPerLitre);
                    if (input.Source != null
                        && input.AvailableItems >= sourceItems
                        && TryGetWorldOutput(outputFace, input.Stack, out WorldLiquidOutputDefinition worldOutput))
                    {
                        hadCompatibleSink = true;
                        if (litreBudget + 0.0001f < WorldLiquidPumpPolicy.LavaSourceLitres)
                        {
                            isChargingWorldOutput = true;
                            continue;
                        }

                        if (TryCommitOwnedLiquidToWorld(input, worldOutput, sourceItems))
                        {
                            lastMovedLitres = WorldLiquidPumpPolicy.LavaSourceLitres;
                            litreBudget = MathF.Max(0f, litreBudget - lastMovedLitres);
                            nextSourceIndex = (sourceIndex + 1) % sourceCount;
                            status = "active";
                            MarkDirty(true);
                            return;
                        }

                        hadBlockedSink = true;
                    }

                    continue;
                }

                hadCompatibleSink = true;
                int movedItems = TryRoundRobinPut(sinks, transferStack, desiredItems / props.ItemsPerLitre);
                if (movedItems <= 0)
                {
                    hadBlockedSink = true;
                    continue;
                }

                if (input.Source != null)
                {
                    ItemStack taken = input.Source.TryTakeContent(input.Pos, movedItems);
                    if (taken == null || taken.StackSize <= 0)
                    {
                        status = "sourcerace";
                        MarkDirty(true);
                        return;
                    }
                    movedItems = Math.Min(movedItems, taken.StackSize);
                }

                lastMovedLitres = movedItems / props.ItemsPerLitre;
                litreBudget = MathF.Max(0f, litreBudget - lastMovedLitres);
                nextSourceIndex = (sourceIndex + 1) % sourceCount;
                status = "active";
                MarkDirty(true);
                return;
            }

            if (isChargingLavaTransfer) status = "lavacharging";
            else if (isChargingWorldOutput) status = "worldcharging";
            else if (!hadUsableSource) status = hadFilteredSource ? "filteredsource" : "invalidsource";
            else if (ItemsForAnySource(litreBudget, inputs) <= 0) status = "waiting";
            else if (!hadCompatibleSink) status = "nosink";
            else status = hadBlockedSink ? "blocked" : "nosink";
            MarkDirty(true);
        }

        private bool MatchesFilter(ItemStack stack)
        {
            return filter.Matches(stack);
        }

        private int TryRoundRobinPut(List<PumpSink> sinks, ItemStack transferStack, float desiredLitres)
        {
            int count = sinks.Count;
            int start = count == 0 ? 0 : Math.Abs(nextSinkIndex) % count;
            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                PumpSink candidate = sinks[index];

                try
                {
                    ItemStack putStack = transferStack.Clone();
                    putStack.StackSize = transferStack.StackSize;
                    int moved = candidate.Sink.TryPutLiquid(candidate.Pos, putStack, desiredLitres);
                    if (moved > 0)
                    {
                        nextSinkIndex = (index + 1) % count;
                        return moved;
                    }
                }
                catch
                {
                    // Some vanilla liquid sinks require a container BE. Treat broken sinks as full.
                }
            }

            return 0;
        }

        private int ItemsForAnySource(float litres, List<PumpInput> inputs)
        {
            foreach (PumpInput input in inputs)
            {
                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(input.Stack);
                if (props != null && props.ItemsPerLitre > 0f && ItemsForBudget(litres, props.ItemsPerLitre) > 0)
                {
                    return 1;
                }
            }
            return 0;
        }

        private List<PumpInput> FindSources(BlockFacing inputFace)
        {
            List<PumpInput> sources = new();
            HashSet<string> sourcePositions = new();

            // Bucket filling reads the target position's fluid layer. Do the same for a pump
            // placed into water so it can draw from its own waterlogged/block-fluid position.
            TryAddWorldLiquidSource(Pos, sources, sourcePositions);

            BlockPos firstPos = Pos.AddCopy(inputFace);
            Block firstBlock = Api.World.BlockAccessor.GetBlock(firstPos);
            TryAddSource(firstPos, firstBlock, sources, sourcePositions);

            string entryFace = BlockCopperPipe.FaceCode(inputFace.Opposite);
            if (!BlockCopperPipe.HasPipeConnection(firstBlock, entryFace)) return sources;

            Queue<(BlockPos Pos, int Distance)> queue = new();
            HashSet<string> visited = new();
            queue.Enqueue((firstPos, 1));
            visited.Add(PosKey(firstPos));

            while (queue.Count > 0)
            {
                (BlockPos pipePos, int distance) = queue.Dequeue();
                if (distance > MaxPipeDistance) continue;

                Block pipeBlock = Api.World.BlockAccessor.GetBlock(pipePos);
                if (!BlockCopperPipe.IsCopperPipe(pipeBlock)) continue;

                foreach (BlockFacing face in PipeFaces)
                {
                    string faceCode = BlockCopperPipe.FaceCode(face);
                    if (!BlockCopperPipe.HasPipeConnection(pipeBlock, faceCode)) continue;

                    BlockPos neighborPos = pipePos.AddCopy(face);
                    Block neighborBlock = Api.World.BlockAccessor.GetBlock(neighborPos);
                    TryAddSource(neighborPos, neighborBlock, sources, sourcePositions);

                    if (BlockCopperPipe.HasPipeConnection(neighborBlock, BlockCopperPipe.FaceCode(face.Opposite)))
                    {
                        string key = PosKey(neighborPos);
                        if (visited.Add(key)) queue.Enqueue((neighborPos, distance + 1));
                    }
                }
            }

            return sources;
        }

        private void TryAddSource(BlockPos pos, Block block, List<PumpInput> sources, HashSet<string> sourcePositions)
        {
            string key = PosKey(pos);
            if (sourcePositions.Contains(key)) return;

            ILiquidSource source = LiquidSourceResolver.FindOwnedSource(Api.World, pos, block);
            if (source != null)
            {
                try
                {
                    ItemStack content = source.GetContent(pos);
                    if (content != null && content.StackSize > 0)
                    {
                        sources.Add(new PumpInput(pos, source, content.Clone(), content.StackSize, isWorldLava: false));
                        sourcePositions.Add(key);
                        return;
                    }
                }
                catch
                {
                }
            }

            TryAddWorldLiquidSource(pos, sources, sourcePositions);
        }

        private void TryAddWorldLiquidSource(BlockPos pos, List<PumpInput> sources, HashSet<string> sourcePositions)
        {
            string key = PosKey(pos);
            if (sourcePositions.Contains(key)) return;

            if (TryGetWorldLiquid(pos, out ItemStack worldLiquid, out bool isWorldLava))
            {
                int availableItems = isWorldLava ? worldLiquid.StackSize : int.MaxValue;
                sources.Add(new PumpInput(pos, null, worldLiquid, availableItems, isWorldLava));
                sourcePositions.Add(key);
            }
        }

        private bool TryGetInputLiquidAt(BlockPos inputPos, out PumpInput input)
        {
            Block block = Api.World.BlockAccessor.GetBlock(inputPos);
            ILiquidSource source = LiquidSourceResolver.FindOwnedSource(Api.World, inputPos, block);
            if (source != null)
            {
                try
                {
                    ItemStack content = source.GetContent(inputPos);
                    if (content != null && content.StackSize > 0)
                    {
                        input = new PumpInput(inputPos, source, content.Clone(), content.StackSize, isWorldLava: false);
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (TryGetWorldLiquid(inputPos, out ItemStack worldLiquid, out bool isWorldLava))
            {
                input = new PumpInput(
                    inputPos,
                    null,
                    worldLiquid,
                    isWorldLava ? worldLiquid.StackSize : int.MaxValue,
                    isWorldLava);
                return true;
            }

            input = default;
            return false;
        }

        private bool TryGetInputLiquid(BlockFacing inputFace, out PumpInput input)
        {
            return TryGetInputLiquidAt(Pos.AddCopy(inputFace), out input);
        }

        private bool TryGetWorldLiquid(BlockPos pos, out ItemStack stack, out bool isWorldLava)
        {
            Block block = Api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
            isWorldLava = WorldLiquidPumpPolicy.IsVanillaLavaSource(
                block?.Code?.Domain,
                block?.LiquidCode,
                block?.LiquidLevel ?? 0);

            if (isWorldLava)
            {
                Item lavaPortion = Api.World.GetItem(new AssetLocation("vintagekinematics:lavaportion"));
                if (lavaPortion == null)
                {
                    stack = null;
                    return false;
                }

                stack = new ItemStack(lavaPortion);
                WaterTightContainableProps lavaProps = BlockLiquidContainerBase.GetContainableProps(stack);
                if (lavaProps == null || lavaProps.ItemsPerLitre <= 0f)
                {
                    stack = null;
                    return false;
                }

                stack.StackSize = (int)MathF.Round(WorldLiquidPumpPolicy.LavaSourceLitres * lavaProps.ItemsPerLitre);
                return true;
            }

            if (!LiquidSourceResolver.IsRenewableVanillaWorldWater(block))
            {
                stack = null;
                return false;
            }

            WaterTightContainableProps props = block?.Attributes?["waterTightContainerProps"]?.AsObject<WaterTightContainableProps>();
            if (props?.WhenFilled == null || !props.Containable)
            {
                stack = null;
                return false;
            }

            props.WhenFilled.Stack.Resolve(Api.World, "copperpump-source");
            ItemStack contentStack = props.WhenFilled.Stack.ResolvedItemstack;
            if (contentStack == null)
            {
                stack = null;
                return false;
            }

            stack = contentStack.Clone();
            stack.StackSize = 1;
            return true;
        }

        private List<PumpSink> FindSinks(BlockFacing outputFace, ItemStack liquidStack, bool requireIronTank)
        {
            List<PumpSink> sinks = new();
            HashSet<string> sinkPositions = new();
            BlockPos firstPos = Pos.AddCopy(outputFace);
            Block firstBlock = Api.World.BlockAccessor.GetBlock(firstPos);
            TryAddSink(firstPos, firstBlock, liquidStack, requireIronTank, sinks, sinkPositions);

            string entryFace = BlockCopperPipe.FaceCode(outputFace.Opposite);
            if (!BlockCopperPipe.HasPipeConnection(firstBlock, entryFace)) return sinks;

            Queue<(BlockPos Pos, int Distance)> queue = new();
            HashSet<string> visited = new();
            queue.Enqueue((firstPos, 1));
            visited.Add(PosKey(firstPos));

            while (queue.Count > 0)
            {
                (BlockPos pipePos, int distance) = queue.Dequeue();
                if (distance > MaxPipeDistance) continue;

                Block pipeBlock = Api.World.BlockAccessor.GetBlock(pipePos);
                if (!BlockCopperPipe.IsCopperPipe(pipeBlock)) continue;

                foreach (BlockFacing face in PipeFaces)
                {
                    string faceCode = BlockCopperPipe.FaceCode(face);
                    if (!BlockCopperPipe.HasPipeConnection(pipeBlock, faceCode)) continue;

                    BlockPos neighborPos = pipePos.AddCopy(face);
                    Block neighborBlock = Api.World.BlockAccessor.GetBlock(neighborPos);
                    TryAddSink(neighborPos, neighborBlock, liquidStack, requireIronTank, sinks, sinkPositions);

                    if (BlockCopperPipe.HasPipeConnection(neighborBlock, BlockCopperPipe.FaceCode(face.Opposite)))
                    {
                        string key = PosKey(neighborPos);
                        if (visited.Add(key)) queue.Enqueue((neighborPos, distance + 1));
                    }
                }
            }

            return sinks;
        }

        private void TryAddSink(
            BlockPos pos,
            Block block,
            ItemStack liquidStack,
            bool requireIronTank,
            List<PumpSink> sinks,
            HashSet<string> sinkPositions)
        {
            if (requireIronTank && block is not BlockIronFluidTank) return;

            ILiquidSink sink = block as ILiquidSink;
            if (sink == null)
            {
                sink = MultiblockHelper.GetMultiblockAwareBE(Api.World, pos)?.Block as ILiquidSink;
            }
            if (sink == null) return;

            if (!CanAccept(sink, pos, liquidStack)) return;

            string key = PosKey(pos);
            if (!sinkPositions.Add(key)) return;

            sinks.Add(new PumpSink(pos, sink));
        }

        private bool TryCommitWorldLava(
            PumpInput input,
            List<PumpSink> sinks,
            ItemStack lavaStack)
        {
            int count = sinks.Count;
            int start = count == 0 ? 0 : Math.Abs(nextSinkIndex) % count;

            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                PumpSink candidate = sinks[index];
                float freeLitres;
                try
                {
                    freeLitres = candidate.Sink.CapacityLitres - candidate.Sink.GetCurrentLitres(candidate.Pos);
                }
                catch
                {
                    continue;
                }

                if (!WorldLiquidPumpPolicy.CanCommitLavaSource(
                    candidate.Sink is BlockIronFluidTank,
                    freeLitres))
                {
                    continue;
                }

                Block sourceBlock = Api.World.BlockAccessor.GetBlock(input.Pos, BlockLayersAccess.Fluid);
                if (!WorldLiquidPumpPolicy.IsVanillaLavaSource(
                    sourceBlock?.Code?.Domain,
                    sourceBlock?.LiquidCode,
                    sourceBlock?.LiquidLevel ?? 0))
                {
                    return false;
                }

                Api.World.BlockAccessor.SetBlock(0, input.Pos, BlockLayersAccess.Fluid);
                int movedItems = 0;
                try
                {
                    movedItems = candidate.Sink.TryPutLiquid(
                        candidate.Pos,
                        lavaStack.Clone(),
                        WorldLiquidPumpPolicy.LavaSourceLitres);
                }
                catch
                {
                    movedItems = 0;
                }

                if (movedItems == lavaStack.StackSize)
                {
                    nextSinkIndex = (index + 1) % count;
                    Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(input.Pos);
                    return true;
                }

                if (movedItems > 0 && candidate.Sink is ILiquidSource rollbackSource)
                {
                    rollbackSource.TryTakeContent(candidate.Pos, movedItems);
                }

                Api.World.BlockAccessor.SetBlock(sourceBlock.BlockId, input.Pos, BlockLayersAccess.Fluid);
                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(input.Pos);
                return false;
            }

            return false;
        }

        private bool TryGetWorldOutput(
            BlockFacing outputFace,
            ItemStack storedLiquid,
            out WorldLiquidOutputDefinition output)
        {
            AssetLocation code = storedLiquid?.Collectible?.Code;
            if (!WorldLiquidOutputPolicy.TryResolve(code?.Domain, code?.Path, out output)) return false;

            BlockPos directTarget = Pos.AddCopy(outputFace);
            return PumpWorldOutput.IsPotentialTarget(Api.World, directTarget, output);
        }

        private bool TryCommitOwnedLiquidToWorld(
            PumpInput input,
            WorldLiquidOutputDefinition output,
            int requiredItems)
        {
            BlockPos directTarget = Pos.AddCopy(OutputFace());
            if (!PumpWorldOutput.TryFindPlacement(Api.World, directTarget, output, out BlockPos placement))
            {
                return false;
            }

            ItemStack current;
            try
            {
                current = input.Source.GetContent(input.Pos);
            }
            catch
            {
                return false;
            }

            if (current == null
                || current.StackSize < requiredItems
                || !current.Equals(Api.World, input.Stack, GlobalConstants.IgnoredStackAttributes))
            {
                return false;
            }

            Block sourceBlock = Api.World.GetBlock(new AssetLocation(output.SourceBlockCode));
            if (sourceBlock == null) return false;

            ItemStack taken;
            try
            {
                taken = input.Source.TryTakeContent(input.Pos, requiredItems);
            }
            catch
            {
                return false;
            }

            if (taken == null || taken.StackSize != requiredItems)
            {
                RestoreOwnedSource(input, taken, requiredItems);
                return false;
            }

            if (!PumpWorldOutput.IsSupportedPlacementCandidate(Api.World, placement, output.LiquidCode))
            {
                RestoreOwnedSource(input, taken, requiredItems);
                return false;
            }

            Block previousFluid = Api.World.BlockAccessor.GetBlock(placement, BlockLayersAccess.Fluid);
            try
            {
                Api.World.BlockAccessor.SetBlock(sourceBlock.BlockId, placement, BlockLayersAccess.Fluid);
                Block placed = Api.World.BlockAccessor.GetBlock(placement, BlockLayersAccess.Fluid);
                if (!WorldLiquidOutputPolicy.IsMatchingFullSource(
                    placed?.Code?.Domain,
                    placed?.LiquidCode,
                    placed?.LiquidLevel ?? 0,
                    output.LiquidCode))
                {
                    Api.World.BlockAccessor.SetBlock(previousFluid.BlockId, placement, BlockLayersAccess.Fluid);
                    RestoreOwnedSource(input, taken, requiredItems);
                    return false;
                }

                Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(placement);
                return true;
            }
            catch
            {
                Api.World.BlockAccessor.SetBlock(previousFluid.BlockId, placement, BlockLayersAccess.Fluid);
                RestoreOwnedSource(input, taken, requiredItems);
                return false;
            }
        }

        private void RestoreOwnedSource(PumpInput input, ItemStack taken, int expectedItems)
        {
            if (taken == null || taken.StackSize <= 0 || input.Source is not ILiquidSink sink) return;

            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(taken);
            if (props == null || props.ItemsPerLitre <= 0f) return;

            float litres = Math.Min(expectedItems, taken.StackSize) / props.ItemsPerLitre;
            try
            {
                sink.TryPutLiquid(input.Pos, taken, litres);
            }
            catch
            {
                Api.Logger.Error(
                    "[VintageKinematics] Could not roll back a failed world-liquid output at {0}.",
                    input.Pos);
            }
        }

        private bool CanAccept(ILiquidSink sink, BlockPos pos, ItemStack liquidStack)
        {
            try
            {
                if (sink.IsFull(pos)) return false;
                ItemStack existing = sink.GetContent(pos);
                if (existing == null || existing.StackSize <= 0) return true;
                return existing.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes);
            }
            catch
            {
                return false;
            }
        }

        private BlockFacing OutputFace()
        {
            string direction = Block?.Variant?["direction"];
            return BlockFacing.FromFirstLetter(direction ?? "s") ?? BlockFacing.SOUTH;
        }

        private static int ItemsForBudget(float litres, float itemsPerLitre)
        {
            return Math.Max(0, (int)Math.Floor(litres * itemsPerLitre + 0.0001f));
        }

        private static string PosKey(BlockPos pos)
        {
            return $"{pos.dimension}:{pos.X}:{pos.InternalY}:{pos.Z}";
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            filter.WriteToTree(tree);
            tree.SetFloat("litreBudget", litreBudget);
            tree.SetInt("nextSinkIndex", nextSinkIndex);
            tree.SetInt("nextSourceIndex", nextSourceIndex);
            tree.SetString("status", status);
            tree.SetFloat("lastMovedLitres", lastMovedLitres);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            filter.ReadFromTree(tree);
            litreBudget = tree.GetFloat("litreBudget", 0f);
            nextSinkIndex = tree.GetInt("nextSinkIndex", 0);
            nextSourceIndex = tree.GetInt("nextSourceIndex", 0);
            status = tree.GetString("status", "idle");
            lastMovedLitres = tree.GetFloat("lastMovedLitres", 0f);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine(Lang.Get("vintagekinematics:copperpump-status-" + status));
            if (lastMovedLitres > 0f)
            {
                dsc.AppendLine(Lang.Get("vintagekinematics:copperpump-lastmoved", lastMovedLitres));
            }
            if (status == "lavacharging")
            {
                dsc.AppendLine(Lang.Get(
                    "vintagekinematics:copperpump-lavaprogress",
                    litreBudget,
                    WorldLiquidPumpPolicy.LavaSourceLitres));
            }
            if (status == "worldcharging")
            {
                dsc.AppendLine(Lang.Get(
                    "vintagekinematics:copperpump-worldprogress",
                    litreBudget,
                    WorldLiquidPumpPolicy.LavaSourceLitres));
            }
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            return filter.Open(byPlayer);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (filter.OnReceivedClientPacket(player, packetid, data)) return;
            base.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (filter.OnReceivedServerPacket(packetid, data)) return;
            base.OnReceivedServerPacket(packetid, data);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            filter.DisposeDialog();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            filter.DisposeDialog();
        }

        private readonly struct PumpInput
        {
            public readonly BlockPos Pos;
            public readonly ILiquidSource Source;
            public readonly ItemStack Stack;
            public readonly int AvailableItems;
            public readonly bool IsWorldLava;

            public PumpInput(BlockPos pos, ILiquidSource source, ItemStack stack, int availableItems, bool isWorldLava)
            {
                Pos = pos;
                Source = source;
                Stack = stack;
                AvailableItems = availableItems;
                IsWorldLava = isWorldLava;
            }
        }

        private readonly struct PumpSink
        {
            public readonly BlockPos Pos;
            public readonly ILiquidSink Sink;

            public PumpSink(BlockPos pos, ILiquidSink sink)
            {
                Pos = pos;
                Sink = sink;
            }
        }
    }
}
