using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Gui;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BECopperPump : BEKineticAnimated
    {
        private const float LitresPerSecondAt16Rpm = 0.1f;
        private const float TickSeconds = 0.25f;
        private const float MaxBufferedLitres = 1f;
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
        private InventoryLiquidFilter filterInv;
        private bool whitelist;
        private bool fuzzy;
        private bool suppressClientSync;
        private GuiDialogFunnelFilter invDialog;

        public BECopperPump()
        {
            filterInv = new InventoryLiquidFilter(FilterSlotCount, "copperpumpfilter", null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filterInv.LateInitialize("copperpumpfilter-" + Pos, api);
            filterInv.ResolveBlocksOrItems();
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnServerTick, (int)(TickSeconds * 1000));
            if (api.Side == EnumAppSide.Client) filterInv.SlotModified += OnClientFilterSlotModified;
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

            if (!hadUsableSource) status = hadFilteredSource ? "filteredsource" : "invalidsource";
            else if (ItemsForAnySource(litreBudget, inputs) <= 0) status = "waiting";
            else if (!hadCompatibleSink) status = "nosink";
            else status = hadBlockedSink ? "blocked" : "nosink";
            MarkDirty(true);
        }

        private bool MatchesFilter(ItemStack stack)
        {
            return ItemFilterMatcher.Matches(stack, filterInv, FilterSlotCount, whitelist, fuzzy);
        }

        private void OnClientFilterSlotModified(int slotId)
        {
            if (Api?.Side != EnumAppSide.Client || suppressClientSync) return;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(slotId);
            ItemSlot slot = filterInv[slotId];
            if (slot.Empty)
            {
                bw.Write(false);
            }
            else
            {
                bw.Write(true);
                slot.Itemstack.ToBytes(bw);
            }

            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetFilter, ms.ToArray());
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
            filterInv?.ToTreeAttributes(tree);
            tree.SetBool("whitelist", whitelist);
            tree.SetBool("fuzzy", fuzzy);
            tree.SetFloat("litreBudget", litreBudget);
            tree.SetInt("nextSinkIndex", nextSinkIndex);
            tree.SetInt("nextSourceIndex", nextSourceIndex);
            tree.SetString("status", status);
            tree.SetFloat("lastMovedLitres", lastMovedLitres);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            filterInv ??= new InventoryLiquidFilter(FilterSlotCount, "copperpumpfilter", null);
            suppressClientSync = true;
            try
            {
                filterInv.FromTreeAttributes(tree);
            }
            finally
            {
                suppressClientSync = false;
            }
            whitelist = tree.GetBool("whitelist", false);
            fuzzy = tree.GetBool("fuzzy", false);
            litreBudget = tree.GetFloat("litreBudget", 0f);
            nextSinkIndex = tree.GetInt("nextSinkIndex", 0);
            nextSourceIndex = tree.GetInt("nextSourceIndex", 0);
            status = tree.GetString("status", "idle");
            lastMovedLitres = tree.GetFloat("lastMovedLitres", 0f);

            if (Api != null) filterInv.ResolveBlocksOrItems();
            invDialog?.OnFilterStateUpdated();
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

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:copperpump-filter-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:copperpump-filter-title") title = "Pump Filter";

                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);
                bw.Write(title);
                bw.Write(whitelist);
                bw.Write(fuzzy);
                var tree = new TreeAttribute();
                filterInv.ToTreeAttributes(tree);
                tree.ToBytes(bw);

                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
                byPlayer.InventoryManager.OpenInventory(filterInv);
            }

            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == 1001)
            {
                player.InventoryManager?.CloseInventory(filterInv);
                return;
            }
            if (packetid == PacketIdToggleMode)
            {
                if (!CheckClaim(player)) return;
                whitelist = !whitelist;
                MarkDirty(true);
                return;
            }
            if (packetid == PacketIdToggleFuzzy)
            {
                if (!CheckClaim(player)) return;
                fuzzy = !fuzzy;
                MarkDirty(true);
                return;
            }
            if (packetid == PacketIdSetFilter)
            {
                if (!CheckClaim(player)) return;
                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                int slotId = br.ReadInt32();
                if (slotId != 0) return;

                bool hasStack = br.ReadBoolean();
                ItemStack stack = null;
                if (hasStack)
                {
                    stack = new ItemStack();
                    stack.FromBytes(br);
                    stack.ResolveBlockOrItem(Api.World);
                    stack.StackSize = 1;
                    if (BlockLiquidContainerBase.GetContainableProps(stack) == null) return;
                }

                filterInv[0].Itemstack = stack;
                filterInv[0].MarkDirty();
                MarkDirty(true);
                return;
            }
            if (packetid < 1000)
            {
                if (!CheckClaim(player)) return;
                filterInv.InvNetworkUtil.HandleClientPacket(player, packetid, data);
            }
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent copper pump filter packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
            whitelist = br.ReadBoolean();
            fuzzy = br.ReadBoolean();
            var tree = new TreeAttribute();
            tree.FromBytes(br);

            suppressClientSync = true;
            try
            {
                filterInv.FromTreeAttributes(tree);
                filterInv.ResolveBlocksOrItems();
            }
            finally
            {
                suppressClientSync = false;
            }

            if (invDialog == null)
            {
                invDialog = new GuiDialogFunnelFilter(
                    title, filterInv, Pos, FilterSlotCount,
                    () => whitelist, () => fuzzy,
                    OnClientToggleMode, OnClientToggleFuzzy, capi,
                    centerSingleSlot: true);
                invDialog.OnClosed += OnDialogClosed;
                invDialog.TryOpen();
            }
            else
            {
                invDialog.OnFilterStateUpdated();
            }
        }

        private void OnClientToggleMode()
        {
            whitelist = !whitelist;
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleMode);
            invDialog?.OnFilterStateUpdated();
        }

        private void OnClientToggleFuzzy()
        {
            fuzzy = !fuzzy;
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleFuzzy);
            invDialog?.OnFilterStateUpdated();
        }

        private void OnDialogClosed()
        {
            invDialog = null;
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

        private void DisposeDialog() => GuiDialogUtil.SafeDispose(ref invDialog);

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
