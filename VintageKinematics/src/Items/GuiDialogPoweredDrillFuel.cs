using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VintageKinematics.Items
{
    public class GuiDialogPoweredDrillFuel : GuiDialogGeneric
    {
        private readonly InventoryPoweredDrillFuel inventory;

        public override double DrawOrder => 0.2;

        public GuiDialogPoweredDrillFuel(InventoryPoweredDrillFuel inventory, ICoreClientAPI capi) : this(inventory, Lang.Get("vintagekinematics:powereddrill-title"), capi)
        {
        }

        public GuiDialogPoweredDrillFuel(InventoryPoweredDrillFuel inventory, string title, ICoreClientAPI capi) : base(title, capi)
        {
            this.inventory = inventory;
            Compose();
        }

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            capi.Network.SendPacketClient(capi.World.Player.InventoryManager.OpenInventory(inventory));
        }

        public override void OnGuiClosed()
        {
            inventory.SaveToSourceSlot();
            capi.World.Player.InventoryManager.CloseInventoryAndSync(inventory);
            base.OnGuiClosed();
        }

        private void Compose()
        {
            double pad = GuiElementItemSlotGrid.unscaledSlotPadding;
            double topOffset = 16.0;
            double rowWidth = 180.0;
            ElementBounds slotMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0.0, 0.0, 1, 1);
            double slotX = pad + (rowWidth - slotMeasure.fixedWidth) / 2.0;

            ElementBounds labelBounds = ElementBounds.Fixed(pad, pad + topOffset, rowWidth, 20);
            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotX, pad + topOffset + 24.0, 1, 1);
            ElementBounds insetBounds = slotBounds.ForkBoundingParent(6, 6, 6, 6);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(labelBounds, insetBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            SingleComposer = capi.Gui
                .CreateCompo("powereddrillfuel-" + inventory.InventoryID, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(DialogTitle, CloseIconPressed)
                .BeginChildElements(bgBounds)
                .AddStaticText(Lang.Get("vintagekinematics:powereddrill-fuel"), CairoFont.WhiteSmallText(), EnumTextOrientation.Center, labelBounds)
                .AddInset(insetBounds)
                .AddItemSlotGrid(inventory, SendInvPacket, 1, new[] { 0 }, slotBounds, "fuelslot")
                .EndChildElements()
                .Compose();
        }

        private void SendInvPacket(object packet)
        {
            capi.Network.SendPacketClient(packet);
        }

        private void CloseIconPressed()
        {
            TryClose();
        }
    }
}
