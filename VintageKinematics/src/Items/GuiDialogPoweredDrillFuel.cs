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
            double elementPad = GuiStyle.ElementToDialogPadding;

            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, pad, 42, 1, 1);
            ElementBounds labelBounds = ElementBounds.Fixed(pad, 18, 160, 20);
            ElementBounds insetBounds = slotBounds.ForkBoundingParent(6, 6, 6, 6);
            ElementBounds dialogBounds = insetBounds
                .ForkBoundingParent(elementPad, elementPad + 20, elementPad, elementPad)
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            SingleComposer = capi.Gui
                .CreateCompo("powereddrillfuel-" + inventory.InventoryID, dialogBounds)
                .AddShadedDialogBG(ElementBounds.Fill)
                .AddDialogTitleBar(DialogTitle, CloseIconPressed)
                .BeginChildElements(ElementBounds.Fill)
                .AddStaticText(Lang.Get("vintagekinematics:powereddrill-fuel"), CairoFont.WhiteSmallText(), labelBounds)
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
