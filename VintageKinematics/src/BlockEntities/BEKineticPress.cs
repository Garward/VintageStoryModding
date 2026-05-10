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
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-block press: top face inputs items, bottom face outputs solids and drains liquids
    /// into any <see cref="BlockLiquidContainerBase"/> directly below (e.g. a barrel). Each work
    /// cycle consumes 1 input and produces the recipe's solid and/or liquid outputs.
    /// </summary>
    public class BEKineticPress : BlockEntityOpenableContainer
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 4;
        public const int InventorySize = 5;

        public const float CapacityLitres = 10f;
        // Liquid portion items use stacksize as portions; 100 portions = 1 litre per the
        // standard waterTightContainerProps.itemsPerLitre convention.
        private const float PortionsPerLitre = 100f;

        private const float OutputPushIntervalMs = 250f;
        private const int OutputPushBatch = 8;
        private const float DrainTickIntervalMs = 500f;

        private readonly InventoryGeneric inventory;
        private IOFaceMap ioFaces;
        private ItemStack liquidStack;
        private float liquidLitres;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "kineticpress";

        public BEKineticPress()
        {
            inventory = new InventoryGeneric(InventorySize, "kineticpress-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            inventory.SlotModified += _ => MarkDirty(true);

            ioFaces = new IOFaceMap().MapInput(BlockFacing.UP, SlotInput);
            for (int i = SlotOutputFirst; i <= SlotOutputLast; i++)
            {
                ioFaces.MapOutput(BlockFacing.DOWN, i);
            }
            ioFaces.Apply(inventory);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerPushTick, (int)OutputPushIntervalMs);
                RegisterGameTickListener(OnServerDrainTick, (int)DrainTickIntervalMs);
            }

            BEBehaviorKineticWorker worker = GetBehavior<BEBehaviorKineticWorker>();
            if (worker != null) worker.OnWorkCompleted += OnWorkCycle;
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

        private void OnServerDrainTick(float dt)
        {
            if (liquidStack == null || liquidLitres <= 0f) return;

            BlockPos belowPos = Pos.DownCopy();
            Block belowBlock = Api.World.BlockAccessor.GetBlock(belowPos);
            if (belowBlock is not BlockLiquidContainerBase lcb) return;

            ItemStack pourStack = liquidStack.Clone();
            // TryPutLiquid uses the stack as a template; sets quantity internally.
            pourStack.StackSize = 999999;
            int portionsMoved = lcb.TryPutLiquid(belowPos, pourStack, liquidLitres);
            if (portionsMoved <= 0) return;

            float litresMoved = portionsMoved / PortionsPerLitre;
            liquidLitres -= litresMoved;
            if (liquidLitres <= 0.0001f)
            {
                liquidLitres = 0f;
                liquidStack = null;
            }
            MarkDirty(true);
        }

        private static readonly AssetLocation PressSound = new AssetLocation("sounds/block/woodcreak.ogg");

        private void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = inventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            var registry = Api.ModLoader.GetModSystem<KineticPressRecipeRegistry>();
            var recipe = registry?.FindRecipe(input);
            if (recipe == null) return;

            // Liquid output gates the cycle: refuse to consume input if the buffer can't hold the produced amount.
            if (recipe.Liquid != null && recipe.Liquid.Code != null)
            {
                if (liquidStack != null && !liquidStack.Collectible.Code.Equals(recipe.Liquid.Code)) return;
                if (liquidLitres + recipe.Liquid.Litres > CapacityLitres + 0.0001f) return;
            }

            slot.TakeOut(1);
            slot.MarkDirty();

            Api.World.PlaySoundAt(PressSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, randomizePitch: true, range: 16, volume: 0.5f);

            if (recipe.Outputs != null)
            {
                foreach (var jstack in recipe.Outputs)
                {
                    if (jstack?.ResolvedItemstack == null) continue;
                    DepositOutput(jstack.ResolvedItemstack.Clone());
                }
            }

            if (recipe.Liquid != null && recipe.Liquid.Code != null)
            {
                if (liquidStack == null)
                {
                    liquidStack = new ItemStack(Api.World.GetItem(recipe.Liquid.Code));
                    if (liquidStack.Collectible == null) liquidStack = null;
                }
                if (liquidStack != null) liquidLitres += recipe.Liquid.Litres;
            }
            MarkDirty(true);
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

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.World is IServerWorldAccessor)
            {
                string title = Lang.Get("vintagekinematics:kineticpress-title");
                if (string.IsNullOrEmpty(title) || title == "vintagekinematics:kineticpress-title") title = "Press";
                byte[] data = BlockEntityContainerOpen.ToBytes("BlockEntityInventory", title, InventorySize, inventory);
                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    (IServerPlayer)byPlayer, Pos, (int)EnumBlockContainerPacketId.OpenInventory, data);
                byPlayer.InventoryManager.OpenInventory(inventory);
            }
            return true;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            if (liquidStack != null && liquidLitres > 0f)
            {
                string name = liquidStack.GetName();
                sb.AppendLine();
                sb.AppendLine($"{name}: {liquidLitres:0.##} / {CapacityLitres:0} L");
            }
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat("liquidLitres", liquidLitres);
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
            liquidLitres = tree.GetFloat("liquidLitres", 0f);
            liquidStack = tree.GetItemstack("liquidStack");
            if (liquidStack != null && Api?.World != null)
            {
                liquidStack.ResolveBlockOrItem(Api.World);
            }
        }
    }
}
