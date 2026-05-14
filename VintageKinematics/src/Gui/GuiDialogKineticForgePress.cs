using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticForgePress : GuiDialogBlockEntity
    {
        private readonly Func<string> getOperationCode;
        private readonly Action<string> onSelectOperation;
        private const string OperationDropdownKey = "forgepress-operation";

        public override double DrawOrder => 0.2;

        public GuiDialogKineticForgePress(
            string title,
            InventoryBase inventory,
            BlockPos pos,
            Func<string> getOperationCode,
            Action<string> onSelectOperation,
            ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            this.getOperationCode = getOperationCode;
            this.onSelectOperation = onSelectOperation;
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = System.Math.Max(3 * (slotSize + slotPad), 260.0);
            double slotColumnWidth = 70.0;

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad, slotColumnWidth, 22.0);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + 24.0, 1, 1);
            ElementBounds fuelLabelBounds = ElementBounds.Fixed(slotPad + slotColumnWidth + 10.0, slotPad, slotColumnWidth, 22.0);
            ElementBounds fuelSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad + slotColumnWidth + 10.0, slotPad + 24.0, 1, 1);
            ElementBounds operationLabelBounds = ElementBounds.Fixed(slotPad, slotPad + 24.0 + inputSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds operationBounds = ElementBounds.Fixed(slotPad, operationLabelBounds.fixedY + 24.0, rowWidth, 32.0);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, operationBounds.fixedY + operationBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(inputLabelBounds, inputSlotBounds, fuelLabelBounds, fuelSlotBounds, operationLabelBounds, operationBounds, outputLabelBounds, outputSlotsBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSlot = new[] { 0 };
            int[] fuelSlot = new[] { 1 };
            int[] outputSlots = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            GetOperationOptions(out string[] operationCodes, out string[] operationNames, out int selectedIndex);

            SingleComposer = capi.Gui.CreateCompo("kineticforgepress-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-input"), CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSlot, inputSlotBounds, "inputslot")
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-fuel"), CairoFont.WhiteSmallText(), fuelLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, fuelSlot, fuelSlotBounds, "fuelslot")
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-operation"), CairoFont.WhiteSmallText(), operationLabelBounds)
                    .AddDropDown(operationCodes, operationNames, selectedIndex, OnOperationSelected, operationBounds, CairoFont.WhiteSmallText(), OperationDropdownKey)
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-outputs"), CairoFont.WhiteSmallText(), outputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();
        }

        private void GetOperationOptions(out string[] operationCodes, out string[] operationNames, out int selectedIndex)
        {
            var registry = capi.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>();
            if (registry == null || registry.OperationCodes.Count == 0)
            {
                operationCodes = new[] { "" };
                operationNames = new[] { Lang.Get("vintagekinematics:kineticforgepress-operation-none") };
                selectedIndex = 0;
                return;
            }

            operationCodes = new string[registry.OperationCodes.Count];
            operationNames = new string[registry.OperationNames.Count];
            string selectedCode = getOperationCode?.Invoke() ?? "";
            selectedIndex = 0;
            for (int i = 0; i < operationCodes.Length; i++)
            {
                operationCodes[i] = registry.OperationCodes[i];
                operationNames[i] = registry.OperationNames[i];
                if (operationCodes[i] == selectedCode) selectedIndex = i;
            }
        }

        private void OnOperationSelected(string code, bool selected)
        {
            if (!selected) return;
            onSelectOperation?.Invoke(code);
        }

        public void OnOperationUpdated()
        {
            if (SingleComposer == null) return;
            ComposeDialog(DialogTitle);
        }
    }
}
