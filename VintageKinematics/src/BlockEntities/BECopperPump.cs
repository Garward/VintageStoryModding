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
        private const float MaxBufferedLitres = 1f;
        private const int MaxPipeDistance = 16;

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

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
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

            int sourceCount = inputs.Count;
            int sourceStart = Math.Abs(nextSourceIndex) % sourceCount;
            for (int offset = 0; offset < sourceCount; offset++)
            {
                int sourceIndex = (sourceStart + offset) % sourceCount;
                PumpInput input = inputs[sourceIndex];

                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(input.Stack);
                if (props == null || props.ItemsPerLitre <= 0f) continue;

                int desiredItems = Math.Min(input.AvailableItems, ItemsForBudget(litreBudget, props.ItemsPerLitre));
                if (desiredItems <= 0)
                {
                    hadUsableSource = true;
                    continue;
                }

                hadUsableSource = true;
                ItemStack transferStack = input.Stack.Clone();
                transferStack.StackSize = desiredItems;

                List<PumpSink> sinks = FindSinks(outputFace, transferStack);
                if (sinks.Count == 0) continue;

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

            if (!hadUsableSource) status = "invalidsource";
            else if (ItemsForAnySource(litreBudget, inputs) <= 0) status = "waiting";
            else if (!hadCompatibleSink) status = "nosink";
            else status = hadBlockedSink ? "blocked" : "nosink";
            MarkDirty(true);
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

            if (block is ILiquidSource source)
            {
                try
                {
                    ItemStack content = source.GetContent(pos);
                    if (content != null && content.StackSize > 0)
                    {
                        sources.Add(new PumpInput(pos, source, content.Clone(), content.StackSize));
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

            ItemStack worldLiquid = LiquidStackForWorldBlock(pos);
            if (worldLiquid != null)
            {
                sources.Add(new PumpInput(pos, null, worldLiquid, int.MaxValue));
                sourcePositions.Add(key);
            }
        }

        private bool TryGetInputLiquidAt(BlockPos inputPos, out PumpInput input)
        {
            Block block = Api.World.BlockAccessor.GetBlock(inputPos);

            if (block is ILiquidSource source)
            {
                try
                {
                    ItemStack content = source.GetContent(inputPos);
                    if (content != null && content.StackSize > 0)
                    {
                        input = new PumpInput(inputPos, source, content.Clone(), content.StackSize);
                        return true;
                    }
                }
                catch
                {
                }
            }

            ItemStack worldLiquid = LiquidStackForWorldBlock(inputPos);
            if (worldLiquid != null)
            {
                input = new PumpInput(inputPos, null, worldLiquid, int.MaxValue);
                return true;
            }

            input = default;
            return false;
        }

        private bool TryGetInputLiquid(BlockFacing inputFace, out PumpInput input)
        {
            return TryGetInputLiquidAt(Pos.AddCopy(inputFace), out input);
        }

        private ItemStack LiquidStackForWorldBlock(BlockPos pos)
        {
            Block block = Api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
            WaterTightContainableProps props = block?.Attributes?["waterTightContainerProps"]?.AsObject<WaterTightContainableProps>();
            if (props?.WhenFilled == null || !props.Containable) return null;

            props.WhenFilled.Stack.Resolve(Api.World, "copperpump-source");
            ItemStack contentStack = props.WhenFilled.Stack.ResolvedItemstack;
            if (contentStack == null) return null;

            ItemStack stack = contentStack.Clone();
            stack.StackSize = 1;
            return stack;
        }

        private List<PumpSink> FindSinks(BlockFacing outputFace, ItemStack liquidStack)
        {
            List<PumpSink> sinks = new();
            HashSet<string> sinkPositions = new();
            BlockPos firstPos = Pos.AddCopy(outputFace);
            Block firstBlock = Api.World.BlockAccessor.GetBlock(firstPos);
            TryAddSink(firstPos, firstBlock, liquidStack, sinks, sinkPositions);

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
                    TryAddSink(neighborPos, neighborBlock, liquidStack, sinks, sinkPositions);

                    if (BlockCopperPipe.HasPipeConnection(neighborBlock, BlockCopperPipe.FaceCode(face.Opposite)))
                    {
                        string key = PosKey(neighborPos);
                        if (visited.Add(key)) queue.Enqueue((neighborPos, distance + 1));
                    }
                }
            }

            return sinks;
        }

        private void TryAddSink(BlockPos pos, Block block, ItemStack liquidStack, List<PumpSink> sinks, HashSet<string> sinkPositions)
        {
            if (block is not ILiquidSink sink) return;
            if (!CanAccept(sink, pos, liquidStack)) return;

            string key = PosKey(pos);
            if (!sinkPositions.Add(key)) return;

            sinks.Add(new PumpSink(pos, sink));
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
            tree.SetFloat("litreBudget", litreBudget);
            tree.SetInt("nextSinkIndex", nextSinkIndex);
            tree.SetInt("nextSourceIndex", nextSourceIndex);
            tree.SetString("status", status);
            tree.SetFloat("lastMovedLitres", lastMovedLitres);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
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
        }

        private readonly struct PumpInput
        {
            public readonly BlockPos Pos;
            public readonly ILiquidSource Source;
            public readonly ItemStack Stack;
            public readonly int AvailableItems;

            public PumpInput(BlockPos pos, ILiquidSource source, ItemStack stack, int availableItems)
            {
                Pos = pos;
                Source = source;
                Stack = stack;
                AvailableItems = availableItems;
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
