using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticJsonProcessor : BEKineticItemProcessorBase<KineticProcessRecipe>
    {
        public const int SlotInputFirst = 0;
        public const int SlotInputLast = 63;
        public const int SlotOutputFirst = 64;
        public const int SlotOutputLast = 127;
        public const int InventorySize = 128;
        public const int PacketIdOpenDialog = 5700;

        public BEKineticJsonProcessor() : base("kineticjsonprocessor", InventorySize, SlotInputFirst, SlotInputLast, SlotOutputFirst, SlotOutputLast) { }

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override string TitleLangCode => ProcessorAttr?["titleLangCode"].AsString("vintagekinematics:jsonprocessor-title");
        protected override string FallbackTitle => ProcessorAttr?["title"].AsString("Kinetic Processor");
        protected override AssetLocation WorkSound => SoundFromAttr(ProcessorAttr?["workSound"].AsString(null));
        protected override float WorkSoundVolume => ProcessorAttr?["workSoundVolume"].AsFloat(0.6f) ?? 0.6f;
        protected override int ActiveInputFirst => SlotInputFirst;
        protected override int ActiveInputLast => SlotInputFirst + ActiveInputSlots - 1;
        protected override int ActiveOutputFirst => SlotOutputFirst;
        protected override int ActiveOutputLast => SlotOutputFirst + ActiveOutputSlots - 1;

        private JsonObject ProcessorAttr => Block?.Attributes?["vkProcessor"];
        private string MachineCode => ProcessorAttr?["machineCode"].AsString(null) ?? Block?.Code?.FirstCodePart();
        private int ActiveInputSlots => GameMath.Clamp(ProcessorAttr?["inputSlots"].AsInt(1) ?? 1, 1, SlotInputLast - SlotInputFirst + 1);
        private int ActiveOutputSlots => GameMath.Clamp(ProcessorAttr?["outputSlots"].AsInt(9) ?? 9, 1, SlotOutputLast - SlotOutputFirst + 1);
        private JsonObject ProgressBarAttr => ProcessorAttr?["progressBar"];
        private bool ShowProgressBar => ProgressBarAttr?["enabled"].AsBool(ProcessorAttr?["showProgressBar"].AsBool(true) ?? true) ?? ProcessorAttr?["showProgressBar"].AsBool(true) ?? true;
        private double ProgressBarWidth => ProgressBarAttr?["width"].AsDouble(144.0) ?? 144.0;
        private bool CrateInput => IsCrateStyle(ProcessorAttr?["inputStorageStyle"].AsString(ProcessorAttr?["storageStyle"].AsString("slots")));
        private bool CrateOutput => IsCrateStyle(ProcessorAttr?["outputStorageStyle"].AsString(ProcessorAttr?["storageStyle"].AsString("slots")));

        protected override IOFaceMap BuildIOFaceMap()
        {
            IOFaceMap explicitMap = BuildJsonIOFaceMap(ProcessorAttr?["io"]);
            if (explicitMap != null) return explicitMap;

            string layout = ProcessorAttr?["ioLayout"].AsString("sideInputOppositeAndDownOutput");
            bool multiblock = ProcessorAttr?["ioScope"].AsString("controller") == "multiblock";
            BlockFacing inputFace = InputFaceFromAttr();

            if (layout == "topInputDownOutput")
            {
                return multiblock
                    ? MachineIoLayouts.MultiblockTopInputDownOutput(Block, Pos, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast)
                    : MachineIoLayouts.TopInputDownOutput(Pos, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast);
            }

            if (layout == "sideInputOppositeOutput")
            {
                return multiblock
                    ? MachineIoLayouts.MultiblockSideInputOppositeOutput(Block, Pos, inputFace, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast)
                    : MachineIoLayouts.SideInputOppositeOutput(Pos, inputFace, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast);
            }

            if (layout == "leftInputRightAndDownOutput")
            {
                return multiblock
                    ? MachineIoLayouts.MultiblockLeftInputRightAndDownOutput(Block, Pos, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast)
                    : MachineIoLayouts.SideInputOppositeAndDownOutput(Pos, MultiblockHelper.LeftOf(MultiblockHelper.PlacementFacingFromVariant(Block)), ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast);
            }

            if (multiblock)
            {
                return MachineIoLayouts.MultiblockSideInputOppositeAndDownOutput(Block, Pos, inputFace, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast);
            }

            return MachineIoLayouts.SideInputOppositeAndDownOutput(Pos, inputFace, ActiveInputFirst, ActiveInputLast, ActiveOutputFirst, ActiveOutputLast);
        }

        protected override KineticProcessRecipe FindRecipe(ItemStack input)
        {
            return Api.ModLoader.GetModSystem<KineticProcessRecipeRegistry>()?.FindRecipe(MachineCode, input);
        }

        protected override int InputQuantityPerCycle(KineticProcessRecipe recipe, ItemStack input) => recipe?.InputQuantity ?? 1;
        protected override IEnumerable<ItemStack> GetOutputs(KineticProcessRecipe recipe) => ResolvedOutputs(recipe?.Outputs);

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            return new GuiDialogKineticJsonProcessor(
                title,
                MachineInventory,
                Pos,
                ActiveInputFirst,
                ActiveInputLast,
                ActiveOutputFirst,
                ActiveOutputLast,
                ShowProgressBar,
                ProgressBarWidth,
                CurrentWorkerProgress,
                CurrentWorkerProgressMax,
                CanProgressCurrentRecipe,
                capi);
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            bool crateInput = CrateInput;
            bool crateOutput = CrateOutput;
            if (HandleCrateStyleRightClick(byPlayer, crateInput, crateOutput)) return true;
            return base.OnPlayerRightClick(byPlayer, blockSel);
        }

        private BlockFacing InputFaceFromAttr()
        {
            return JsonMachineIoBuilder.ResolveFace(Block, ProcessorAttr?["inputFace"].AsString("inputLipWest"));
        }

        private static AssetLocation SoundFromAttr(string code)
        {
            return string.IsNullOrEmpty(code) ? null : new AssetLocation(code);
        }

        private static bool IsCrateStyle(string style)
        {
            return style == "crate" || style == "bulk" || style == "range";
        }
    }
}
