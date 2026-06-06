using System;
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
using VintageKinematics.Crafting;
using VintageKinematics.Gui;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Two-block mixer: solid inputs enter from one side, liquid input can be
    /// transferred by bucket or consumed from an adjacent liquid container, and finished output
    /// drains to the opposite side or bottom.
    /// </summary>
    public class BEKineticMixer : BlockEntityOpenableContainer, IFaceMappedContainer, IKineticWorkTooltipProvider
    {
        public const int SlotInputFirst = 0;
        public const int SlotInputLast = 8;
        public const int SlotOutputFirst = 9;
        public const int SlotOutputLast = 17;
        public const int InventorySize = 18;

        public const float LiquidCapacityLitres = 10f;
        public const int PacketIdOpenDialog = 5610;

        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private ItemStack liquidStack;
        private float liquidLitres;
        private int mixTicksAccumulated;
        private int mixTicksRequired = 1;
        private string mixingRecipeKey;
        private GuiDialogKineticMixer clientDialog;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticmixer";
        public IOFaceMap IOFaces => ioFaces;

        public BEKineticMixer()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticmixer-0", null, null, (slotId, self) =>
            {
                return slotId >= SlotInputFirst && slotId <= SlotInputLast
                    ? new ItemSlot(self)
                    : new ItemSlotCrusherOutput(self);
            });
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.LateInitialize("kineticmixer-" + Pos, api);
            inventory.ResolveBlocksOrItems();
            inventory.SlotModified += _ => Api.World.BlockAccessor.GetChunkAtBlockPos(Pos)?.MarkModified();

            BuildIOFaceMap();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
        }

        private void BuildIOFaceMap()
        {
            BlockFacing inputFace = AutomationInputFace();
            BlockFacing outputFace = inputFace.Opposite;

            ioFaces = new IOFaceMap(Pos);
            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                ioFaces.MapInput(inputFace, i);
            }
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(outputFace, i);
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);
        }

        private BlockFacing AutomationInputFace()
        {
            return InputLipFace(Block?.Variant?["side"]);
        }

        private static BlockFacing InputLipFace(string side)
        {
            switch (side)
            {
                case "n": return BlockFacing.WEST;
                case "e": return BlockFacing.SOUTH;
                case "s": return BlockFacing.EAST;
                case "w":
                default: return BlockFacing.NORTH;
            }
        }

        private void OnServerPushTick(float dt)
        {
            if (ioFaces == null) return;
            foreach (BlockFacing face in ioFaces.OutputFaces)
            {
                foreach (int slotId in ioFaces.OutputSlotsFor(face))
                {
                    ItemSlot slot = inventory[slotId];
                    if (slot.Empty) continue;
                    int moved = InventoryPusher.TryPush(Api.World, Pos, face, slot, OutputPushBatch);
                    if (moved > 0) MarkDirty(true);
                }
            }
        }

        private static readonly AssetLocation MixSound = new AssetLocation("sounds/player/panning.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            if (Api.Side != EnumAppSide.Server) return;

            var registry = Api.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
            KineticMixerRecipe recipe = registry?.FindRecipe(InputStacks(), liquidStack, liquidLitres);

            if (recipe == null)
            {
                recipe = FindRecipeUsingAdjacentLiquid(registry);
                if (recipe == null)
                {
                    ResetMixingProgress();
                    return;
                }
            }

            int[] slotByIngredient = new int[recipe.Ingredients.Length];
            if (!recipe.TryMapIngredients(InputStacks(), slotByIngredient))
            {
                ResetMixingProgress();
                return;
            }

            string recipeKey = BuildMixingRecipeKey(recipe, slotByIngredient);
            if (mixingRecipeKey != recipeKey)
            {
                mixingRecipeKey = recipeKey;
                mixTicksAccumulated = 0;
            }

            mixTicksRequired = Math.Max(1, recipe.MixTicks);
            mixTicksAccumulated++;
            if (mixTicksAccumulated < mixTicksRequired)
            {
                Api.World.PlaySoundAt(MixSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 12, volume: 0.25f);
                MarkDirty(true);
                return;
            }

            if (!ConsumeRecipeLiquid(recipe)) return;

            for (int i = 0; i < recipe.Ingredients.Length; i++)
            {
                ItemSlot slot = inventory[slotByIngredient[i]];
                slot.TakeOut(recipe.Ingredients[i].StackSize);
                slot.MarkDirty();
            }

            Api.World.PlaySoundAt(MixSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.45f);
            mixTicksAccumulated = 0;
            if (recipe.TryMapIngredients(InputStacks(), null) == false)
            {
                mixingRecipeKey = null;
                mixTicksRequired = 1;
            }

            if (recipe.Outputs != null)
            {
                foreach (JsonItemStack output in recipe.Outputs)
                {
                    if (output?.ResolvedItemstack == null) continue;
                    DepositOutput(output.ResolvedItemstack.Clone());
                }
            }

            MarkDirty(true);
        }

        private string BuildMixingRecipeKey(KineticMixerRecipe recipe, int[] slotByIngredient)
        {
            StringBuilder key = new StringBuilder();
            if (recipe?.Ingredients != null)
            {
                for (int i = 0; i < recipe.Ingredients.Length; i++)
                {
                    if (i > 0) key.Append('|');
                    key.Append(recipe.Ingredients[i]?.Code);
                    key.Append(':');
                    key.Append(recipe.Ingredients[i]?.StackSize ?? 0);
                    key.Append('=');

                    int slotId = i < slotByIngredient.Length ? slotByIngredient[i] : -1;
                    ItemStack stack = slotId >= 0 && slotId < inventory.Count ? inventory[slotId].Itemstack : null;
                    key.Append(stack?.Collectible?.Code);
                }
            }

            key.Append(";liquid=");
            key.Append(recipe?.LiquidCode);
            if (recipe?.LiquidCode != null)
            {
                key.Append('@');
                key.Append(liquidStack?.Collectible?.Code);
            }

            return key.ToString();
        }

        private void ResetMixingProgress()
        {
            if (mixTicksAccumulated == 0 && mixingRecipeKey == null) return;
            mixTicksAccumulated = 0;
            mixTicksRequired = 1;
            mixingRecipeKey = null;
            MarkDirty(true);
        }

        private ItemStack[] InputStacks()
        {
            ItemStack[] stacks = new ItemStack[SlotInputLast - SlotInputFirst + 1];
            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                stacks[i - SlotInputFirst] = inventory[i].Itemstack;
            }
            return stacks;
        }

        private KineticMixerRecipe FindRecipeUsingAdjacentLiquid(KineticMixerRecipeRegistry registry)
        {
            if (registry == null) return null;
            ItemStack[] inputs = InputStacks();
            foreach (KineticMixerRecipe recipe in registry.Recipes)
            {
                if (recipe.LiquidCode == null || recipe.LiquidLitres <= 0f) continue;
                if (!recipe.TryMapIngredients(inputs, null)) continue;
                if (HasAdjacentLiquid(recipe)) return recipe;
            }
            return null;
        }

        private bool ConsumeRecipeLiquid(KineticMixerRecipe recipe)
        {
            if (recipe.LiquidCode == null || recipe.LiquidLitres <= 0f) return true;

            if (recipe.HasRequiredLiquid(liquidStack, liquidLitres))
            {
                return ConsumeInternalLiquid(recipe);
            }

            return ConsumeAdjacentLiquid(recipe);
        }

        private bool ConsumeInternalLiquid(KineticMixerRecipe recipe)
        {
            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (props == null) return false;

            int consumeItems = ItemsForLitres(recipe.LiquidLitres, props.ItemsPerLitre);
            if (liquidStack.StackSize < consumeItems) return false;

            liquidStack.StackSize -= consumeItems;
            if (liquidStack.StackSize <= 0)
            {
                liquidStack = null;
                liquidLitres = 0f;
            }
            else
            {
                liquidLitres = liquidStack.StackSize / props.ItemsPerLitre;
            }
            return true;
        }

        private bool HasAdjacentLiquid(KineticMixerRecipe recipe)
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos pos = Pos.AddCopy(face);
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                if (block is not ILiquidSource source) continue;

                ItemStack content = source.GetContent(pos);
                if (!recipe.MatchesLiquid(content)) continue;
                if (source.GetCurrentLitres(pos) + 0.0001f >= recipe.LiquidLitres) return true;
            }
            return false;
        }

        private bool ConsumeAdjacentLiquid(KineticMixerRecipe recipe)
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos pos = Pos.AddCopy(face);
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                if (block is not ILiquidSource source) continue;

                ItemStack content = source.GetContent(pos);
                if (!recipe.MatchesLiquid(content)) continue;

                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(content);
                if (props == null) continue;

                int consumeItems = ItemsForLitres(recipe.LiquidLitres, props.ItemsPerLitre);
                if (content.StackSize < consumeItems) continue;

                ItemStack taken = source.TryTakeContent(pos, consumeItems);
                return taken != null && taken.StackSize >= consumeItems;
            }
            return false;
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World.Side != EnumAppSide.Server) return true;

            if (TryTransferHeldLiquid(byPlayer)) return true;

            ItemSlot hotbar = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (hotbar != null && !hotbar.Empty)
            {
                if (TryDepositHeldInput(byPlayer, hotbar)) return true;
            }

            if (byPlayer.Entity?.Controls?.Sneak == true)
            {
                if (TryGiveFirstStack(byPlayer, SlotOutputFirst, SlotOutputLast)) return true;
                TryGiveFirstStack(byPlayer, SlotInputFirst, SlotInputLast);
                return true;
            }

            OpenDialog(byPlayer);
            return true;
        }

        private void OpenDialog(IPlayer byPlayer)
        {
            if (Api.World is not IServerWorldAccessor) return;

            string title = Lang.Get("vintagekinematics:kineticmixer-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticmixer-title") title = "Kinetic Mixer";

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);
            var tree = new TreeAttribute();
            inventory.ToTreeAttributes(tree);
            WriteMixingState(tree);
            tree.ToBytes(bw);

            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                (IServerPlayer)byPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
            byPlayer.InventoryManager.OpenInventory(inventory);
        }

        private bool TryTransferHeldLiquid(IPlayer byPlayer)
        {
            ItemSlot hotbar = byPlayer.InventoryManager?.ActiveHotbarSlot;
            ItemStack containerStack = hotbar?.Itemstack;
            if (containerStack?.Collectible is not ILiquidSource source || !source.AllowHeldLiquidTransfer) return false;

            ItemStack content = source.GetContent(containerStack);
            if (content == null) return false;

            var registry = Api.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
            KineticMixerRecipe recipe = registry?.FindPotentialRecipeForLiquid(content);
            if (recipe == null) return false;

            if (liquidStack?.Collectible != null && !liquidStack.Collectible.Code.Equals(content.Collectible.Code)) return true;

            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(content);
            if (props == null) return false;

            float remainingLitres = LiquidCapacityLitres - liquidLitres;
            if (remainingLitres <= 0.0001f) return true;

            float transferLitres = source.TransferSizeLitres > 0f ? source.TransferSizeLitres : recipe.LiquidLitres;
            transferLitres = Math.Min(transferLitres, remainingLitres);
            int desiredItems = Math.Min(content.StackSize, ItemsForLitres(transferLitres, props.ItemsPerLitre));
            if (desiredItems <= 0) return true;

            ItemStack taken = source.TryTakeContent(containerStack, desiredItems);
            if (taken == null || taken.StackSize <= 0) return true;

            if (liquidStack == null)
            {
                liquidStack = taken.Clone();
            }
            else
            {
                liquidStack.StackSize += taken.StackSize;
            }
            liquidLitres = liquidStack.StackSize / props.ItemsPerLitre;

            hotbar.MarkDirty();
            MarkDirty(true);
            return true;
        }

        private bool TryDepositHeldInput(IPlayer byPlayer, ItemSlot hotbar)
        {
            var registry = Api.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
            if (registry?.FindPotentialRecipeFor(hotbar.Itemstack) == null) return false;

            ItemSlot target = FindMergeInputSlot(hotbar.Itemstack) ?? FindEmptyInputSlot();
            if (target == null) return true;

            ItemStack moved = hotbar.TakeOut(1);
            if (moved == null) return true;

            if (target.Empty)
            {
                target.Itemstack = moved;
            }
            else
            {
                target.Itemstack.StackSize += moved.StackSize;
            }

            hotbar.MarkDirty();
            target.MarkDirty();
            MarkDirty(true);
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
                    Api.World.Logger.Audit("Player {0} sent mixer packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
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
            ReadMixingState(tree);

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogKineticMixer(title, inventory, Pos, CurrentMixProgress, CurrentMixProgressMax, CanMixCurrentRecipe, capi);
                clientDialog.OnClosed += () => clientDialog = null;
                clientDialog.TryOpen();
            }
        }

        private float CurrentMixProgress()
        {
            return Math.Max(0, mixTicksAccumulated);
        }

        private float CurrentMixProgressMax()
        {
            return Math.Max(1, mixTicksRequired);
        }

        private bool CanMixCurrentRecipe()
        {
            if (!HasMixableRecipe()) return false;

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return false;
            if (kinetic.IsConflicted || (kinetic.EffectiveNetwork?.IsOverstressed ?? false)) return false;

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            float minRpm = Math.Max(0.01f, worker?.MinRPM ?? 0.01f);
            return Math.Abs(kinetic.CurrentRPM) >= minRpm;
        }

        private bool HasMixableRecipe()
        {
            if (mixTicksAccumulated > 0 || !string.IsNullOrEmpty(mixingRecipeKey)) return true;

            var registry = Api?.ModLoader.GetModSystem<KineticMixerRecipeRegistry>();
            KineticMixerRecipe recipe = registry?.FindRecipe(InputStacks(), liquidStack, liquidLitres) ?? FindRecipeUsingAdjacentLiquid(registry);
            return recipe != null;
        }

        private ItemSlot FindMergeInputSlot(ItemStack stack)
        {
            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;
                if (!slot.Itemstack.Collectible.Code.Equals(stack.Collectible.Code)) continue;

                int max = slot.Itemstack.Collectible.MaxStackSize;
                if (slot.Itemstack.StackSize < max) return slot;
            }
            return null;
        }

        private ItemSlot FindEmptyInputSlot()
        {
            for (int i = SlotInputFirst; i <= SlotInputLast; i++)
            {
                if (inventory[i].Empty) return inventory[i];
            }
            return null;
        }

        private bool TryGiveFirstStack(IPlayer byPlayer, int firstSlot, int lastSlot)
        {
            for (int i = firstSlot; i <= lastSlot; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot.Empty) continue;

                ItemStack stack = slot.Itemstack;
                slot.Itemstack = null;
                slot.MarkDirty();

                if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                {
                    Api.World.SpawnItemEntity(stack, byPlayer.Entity.Pos.XYZ.Add(0, 0.5, 0));
                }

                MarkDirty(true);
                return true;
            }
            return false;
        }

        private void DepositOutput(ItemStack stack)
        {
            Vec3d at = new Vec3d(Pos.X + 0.5, Pos.Y + 0.8, Pos.Z + 0.5);
            MachineOutputHelper.DepositOrPush(this, inventory, SlotOutputFirst, SlotOutputLast, stack, ioFaces?.OutputEntries, OutputPushBatch, at);
        }

        private static int ItemsForLitres(float litres, float itemsPerLitre)
        {
            return Math.Max(1, (int)Math.Ceiling(litres * itemsPerLitre - 0.0001f));
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            if (liquidStack != null && liquidLitres > 0f)
            {
                sb.AppendLine();
                sb.AppendLine($"{liquidStack.GetName()}: {liquidLitres:0.##} / {LiquidCapacityLitres:0} L");
            }
            if (mixTicksAccumulated > 0)
            {
                sb.AppendLine($"Mixing: {mixTicksAccumulated} cycle(s)");
            }
        }

        public bool AppendKineticWorkTooltip(StringBuilder dsc, BEBehaviorKineticWorker worker)
        {
            if (!HasMixableRecipe()) return true;

            if (worker?.FixedWorkRPM > 0f)
            {
                dsc.AppendLine($"Work speed: fixed {worker.FixedWorkRPM:F0} RPM once above {worker.MinRPM:F0} RPM");
            }

            float total = Math.Max(1, CurrentMixProgressMax());
            float current = GameMath.Clamp(CurrentMixProgress(), 0f, total);
            float pct = 100f * current / total;
            dsc.AppendLine($"Work: {current:F0}/{total:F0} mix ticks ({pct:F0}%)");
            return true;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            MeshData body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            inventory.ToTreeAttributes(tree);
            tree.SetFloat("liquidLitres", liquidLitres);
            WriteMixingState(tree);
            if (liquidStack != null)
            {
                tree.SetItemstack("liquidStack", liquidStack);
            }
            else
            {
                tree.RemoveAttribute("liquidStack");
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            inventory.FromTreeAttributes(tree);
            liquidLitres = tree.GetFloat("liquidLitres", 0f);
            liquidStack = tree.GetItemstack("liquidStack");
            ReadMixingState(tree);

            if (Api != null)
            {
                inventory.ResolveBlocksOrItems();
                liquidStack?.ResolveBlockOrItem(Api.World);
                BuildIOFaceMap();
            }
        }

        private void WriteMixingState(ITreeAttribute tree)
        {
            tree.SetInt("mixTicks", mixTicksAccumulated);
            tree.SetInt("mixTicksRequired", mixTicksRequired);
            tree.SetString("mixingRecipeKey", mixingRecipeKey ?? "");
        }

        private void ReadMixingState(ITreeAttribute tree)
        {
            mixTicksAccumulated = tree.GetInt("mixTicks", 0);
            mixTicksRequired = Math.Max(1, tree.GetInt("mixTicksRequired", 1));
            mixingRecipeKey = tree.GetString("mixingRecipeKey", "");
            if (string.IsNullOrEmpty(mixingRecipeKey)) mixingRecipeKey = null;
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
