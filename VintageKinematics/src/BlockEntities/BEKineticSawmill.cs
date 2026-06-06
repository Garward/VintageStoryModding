using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using System.IO;
using System.Collections.Generic;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-block sawmill: one input log, one output buffer, mode-selected recipe output.
    /// </summary>
    public class BEKineticSawmill : BEKineticItemProcessorBase<KineticSawmillRecipe>
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;
        public const int SlotOutputLast = 9;
        public const int InventorySize = 10;

        public const int PacketIdOpenDialog = 5400;
        public const int PacketIdSelectMode = 5402;

        private static readonly AssetLocation SawSound = new AssetLocation("game:sounds/tool/groundcrafting/saw1");
        private SawmillMode mode = SawmillMode.Plank;

        public BEKineticSawmill() : base("kineticsawmill", InventorySize, SlotInput, SlotOutputFirst, SlotOutputLast) { }

        public SawmillMode Mode => mode;

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override AssetLocation WorkSound => SawSound;
        protected override string TitleLangCode => "vintagekinematics:kineticsawmill-title";
        protected override string FallbackTitle => "Kinetic Sawmill";

        protected override IOFaceMap BuildIOFaceMap()
        {
            return MachineIoLayouts.SideInputOppositeAndDownOutput(Pos, InputLipFace(Block?.Variant?["side"]), SlotInput, SlotOutputFirst, SlotOutputLast);
        }

        protected override KineticSawmillRecipe FindRecipe(ItemStack input)
        {
            return Api.ModLoader.GetModSystem<KineticSawmillRecipeRegistry>()?.FindRecipe(input, mode);
        }

        protected override System.Collections.Generic.IEnumerable<ItemStack> GetOutputs(KineticSawmillRecipe recipe) => ResolvedOutputs(recipe?.Outputs);

        protected override IEnumerable<ItemStack> GetOutputs(KineticSawmillRecipe recipe, ItemStack input)
        {
            if (recipe?.Outputs == null) yield break;

            string captured = recipe.Ingredient?.Code == null || input?.Collectible?.Code == null
                ? null
                : WildcardUtil.GetWildcardValue(recipe.Ingredient.Code, input.Collectible.Code);

            foreach (JsonItemStack output in recipe.Outputs)
            {
                ItemStack stack = ResolveOutputStack(output, captured);
                if (stack != null) yield return stack;
            }
        }

        private ItemStack ResolveOutputStack(JsonItemStack output, string captured)
        {
            if (output?.Code == null) return null;

            if (captured != null && output.Code.Path?.Contains('*') == true)
            {
                AssetLocation substituted = new AssetLocation(output.Code.Domain, output.Code.Path.Replace("*", captured));
                ItemStack stack;
                if (output.Type == EnumItemClass.Block)
                {
                    Block block = Api.World.GetBlock(substituted);
                    stack = block == null || block.IsMissing ? null : new ItemStack(block);
                }
                else
                {
                    Item item = Api.World.GetItem(substituted);
                    stack = item == null || item.IsMissing ? null : new ItemStack(item);
                }

                if (stack != null) stack.StackSize = System.Math.Max(1, output.StackSize);
                return stack;
            }

            return output.ResolvedItemstack?.Clone();
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketIdSelectMode)
            {
                if (!CheckClaim(player)) return;
                mode = ReadModePacket(data, mode);
                MarkDirty(true);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticSawmill(
                title,
                MachineInventory,
                Pos,
                () => mode,
                OnClientSelectMode,
                CurrentWorkerProgress,
                CurrentWorkerProgressMax,
                CanProgressCurrentRecipe,
                capi);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog)
        {
            (dialog as GuiDialogKineticSawmill)?.OnModeUpdated();
        }

        protected override void WriteState(ITreeAttribute tree) => tree.SetInt("sawmillMode", (int)mode);
        protected override void ReadState(ITreeAttribute tree) => mode = (SawmillMode)tree.GetInt("sawmillMode", 0);

        private void OnClientSelectMode(SawmillMode selectedMode)
        {
            mode = NormalizeMode(selectedMode);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((int)mode);
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSelectMode, ms.ToArray());
            RefreshClientDialog();
        }

        private static SawmillMode ReadModePacket(byte[] data, SawmillMode fallback)
        {
            try
            {
                using var ms = new MemoryStream(data ?? System.Array.Empty<byte>());
                using var br = new BinaryReader(ms);
                return NormalizeMode((SawmillMode)br.ReadInt32());
            }
            catch
            {
                return fallback;
            }
        }

        private static SawmillMode NormalizeMode(SawmillMode selectedMode)
        {
            return System.Enum.IsDefined(typeof(SawmillMode), selectedMode) ? selectedMode : SawmillMode.Plank;
        }

        private static BlockFacing InputLipFace(string side)
        {
            return side switch
            {
                "n" => BlockFacing.WEST,
                "e" => BlockFacing.SOUTH,
                "s" => BlockFacing.EAST,
                _ => BlockFacing.NORTH
            };
        }
    }
}
