using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
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
        public const int PacketIdToggleMode = 5401;

        private static readonly AssetLocation SawSound = new AssetLocation("sounds/block/woodchop.ogg");
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

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketIdToggleMode)
            {
                if (!CheckClaim(player)) return;
                mode = NextMode(mode);
                MarkDirty(true);
                return;
            }
            base.OnReceivedClientPacket(player, packetid, data);
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticSawmill(title, MachineInventory, Pos, () => mode, OnClientToggleMode, capi);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog)
        {
            (dialog as GuiDialogKineticSawmill)?.OnModeUpdated();
        }

        protected override void WriteState(ITreeAttribute tree) => tree.SetInt("sawmillMode", (int)mode);
        protected override void ReadState(ITreeAttribute tree) => mode = (SawmillMode)tree.GetInt("sawmillMode", 0);

        private void OnClientToggleMode()
        {
            mode = NextMode(mode);
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdToggleMode);
            RefreshClientDialog();
        }

        private static SawmillMode NextMode(SawmillMode m) => m switch
        {
            SawmillMode.Plank => SawmillMode.Shaft,
            SawmillMode.Shaft => SawmillMode.Stick,
            SawmillMode.Stick => SawmillMode.CogwheelSection,
            SawmillMode.CogwheelSection => SawmillMode.Firewood,
            _ => SawmillMode.Plank
        };

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
