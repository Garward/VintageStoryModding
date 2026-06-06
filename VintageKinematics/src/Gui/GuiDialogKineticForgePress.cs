using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Crafting;

namespace VintageKinematics.Gui
{
    public class GuiDialogKineticForgePress : GuiDialogBlockEntity
    {
        private readonly Func<string> getOperationCode;
        private readonly Func<float> getProgress;
        private readonly Func<float> getProgressMax;
        private readonly Func<bool> getCanProgress;
        private readonly Action<string> onSelectOperation;
        private readonly MachineRecipeBrowser<ForgePressRecipeListItem> recipeBrowser;
        private bool draggingDialog;
        private Vec2i dragStartMouse;
        private Vec2i dragStartDialog;
        private float drawnProgress = -1f;
        private float drawnProgressMax = -1f;
        private bool drawnProgressActive;
        private long lastProgressRedrawMs;
        private const string OperationButtonKey = "forgepress-recipes";
        private const string ProgressBarKey = "forgepress-progress";
        private const double BrowserWidth = 560.0;
        private const double BrowserListHeight = 292.0;
        private const int RecipeListCellHeight = 64;
        private const double TitleBarDragHeight = 32.0;
        private const double TitleBarReservedRightWidth = 92.0;

        public override double DrawOrder => 0.2;

        public GuiDialogKineticForgePress(
            string title,
            InventoryBase inventory,
            BlockPos pos,
            Func<string> getOperationCode,
            Func<float> getProgress,
            Func<float> getProgressMax,
            Func<bool> getCanProgress,
            Action<string> onSelectOperation,
            ICoreClientAPI capi)
            : base(title, inventory, pos, capi)
        {
            this.getOperationCode = getOperationCode;
            this.getProgress = getProgress;
            this.getProgressMax = getProgressMax;
            this.getCanProgress = getCanProgress;
            this.onSelectOperation = onSelectOperation;
            recipeBrowser = new MachineRecipeBrowser<ForgePressRecipeListItem>(
                "forgepress-recipe",
                "vintagekinematics:kineticforgepress-recipes",
                "vintagekinematics:kineticforgepress-search-recipes",
                BuildRecipeListItems,
                item => this.onSelectOperation?.Invoke(item.Recipe.OperationCode),
                () => SingleComposer,
                BrowserWidth,
                BrowserListHeight,
                RecipeListCellHeight);
            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        public override void OnRenderGUI(float deltaTime)
        {
            RefreshProgressBar(false);
            foreach (var composer in Composers)
            {
                composer.Value.Render(deltaTime);
                MouseOverCursor = composer.Value.MouseOverCursor;
            }
        }

        private void ComposeDialog(string title)
        {
            double slotPad = GuiElementItemSlotGridBase.unscaledSlotPadding;
            double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            double rowWidth = System.Math.Max(3 * (slotSize + slotPad), 260.0);
            double slotColumnWidth = 70.0;
            double topOffset = 16.0;
            GetOperationOptions(out _, out string[] operationNames, out int selectedIndex);

            ElementBounds inputLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset, slotColumnWidth, 22.0);
            ElementBounds inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, slotPad + topOffset + 24.0, 1, 1);
            ElementBounds fuelLabelBounds = ElementBounds.Fixed(slotPad + slotColumnWidth + 10.0, slotPad + topOffset, slotColumnWidth, 22.0);
            ElementBounds fuelSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad + slotColumnWidth + 10.0, slotPad + topOffset + 24.0, 1, 1);
            ElementBounds dieLabelBounds = ElementBounds.Fixed(slotPad + 2.0 * (slotColumnWidth + 10.0), slotPad + topOffset, slotColumnWidth, 22.0);
            ElementBounds dieSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad + 2.0 * (slotColumnWidth + 10.0), slotPad + topOffset + 24.0, 1, 1);
            ElementBounds operationLabelBounds = ElementBounds.Fixed(slotPad, slotPad + topOffset + 24.0 + inputSlotBounds.fixedHeight + 8.0, rowWidth, 22.0);
            ElementBounds operationButtonBounds = ElementBounds.Fixed(slotPad, operationLabelBounds.fixedY + 24.0, rowWidth, 32.0);
            ElementBounds progressBounds = ElementBounds.Fixed(slotPad, operationButtonBounds.fixedY + operationButtonBounds.fixedHeight + 8.0, rowWidth, 18.0);
            ElementBounds outputLabelBounds = ElementBounds.Fixed(slotPad, progressBounds.fixedY + progressBounds.fixedHeight + 10.0, rowWidth, 22.0);
            ElementBounds outputSlotsBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotPad, outputLabelBounds.fixedY + 24.0, 3, 3);

            double browserX = slotPad + rowWidth + 24.0;
            recipeBrowser.SetBounds(browserX, slotPad + topOffset);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            List<ElementBounds> childBounds = new List<ElementBounds>
            {
                inputLabelBounds,
                inputSlotBounds,
                fuelLabelBounds,
                fuelSlotBounds,
                dieLabelBounds,
                dieSlotBounds,
                operationLabelBounds,
                operationButtonBounds,
                progressBounds,
                outputLabelBounds,
                outputSlotsBounds
            };
            recipeBrowser.AddBounds(childBounds);
            bgBounds.WithChildren(childBounds.ToArray());

            string dialogName = "kineticforgepress-" + BlockEntityPosition;
            EnsureMovableDialogPosition(dialogName, rowWidth + (recipeBrowser.IsOpen ? recipeBrowser.Width + 32.0 : 0.0), outputSlotsBounds.fixedY + outputSlotsBounds.fixedHeight + 40.0);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            int[] inputSlot = new[] { 0 };
            int[] fuelSlot = new[] { 1 };
            int[] dieSlot = new[] { 11 };
            int[] outputSlots = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 };

            GuiComposer composer = capi.Gui.CreateCompo(dialogName, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-input"), CairoFont.WhiteSmallText(), inputLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, inputSlot, inputSlotBounds, "inputslot")
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-fuel"), CairoFont.WhiteSmallText(), fuelLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, fuelSlot, fuelSlotBounds, "fuelslot")
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-die"), CairoFont.WhiteSmallText(), dieLabelBounds)
                    .AddItemSlotGrid(Inventory, DoSendPacket, 1, dieSlot, dieSlotBounds, "dieslot")
                    .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-operation"), CairoFont.WhiteSmallText(), operationLabelBounds)
                    .AddSmallButton(GetOperationButtonLabel(operationNames, selectedIndex), OnToggleRecipeBrowser, operationButtonBounds, EnumButtonStyle.Normal, OperationButtonKey)
                    .AddDynamicCustomDraw(progressBounds, OnDrawProgressBar, ProgressBarKey);

            composer = recipeBrowser.AddToComposer(composer);

            GuiComposer oldComposer = SingleComposer;
            SingleComposer = composer
                .AddStaticText(Lang.Get("vintagekinematics:kineticforgepress-outputs"), CairoFont.WhiteSmallText(), outputLabelBounds)
                .AddItemSlotGrid(Inventory, DoSendPacket, 3, outputSlots, outputSlotsBounds, "outputslots")
                .EndChildElements()
                .Compose();
            oldComposer?.Dispose();
            RefreshProgressBar(true);

            recipeBrowser.AfterCompose(SingleComposer);
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

        private string GetOperationButtonLabel(string[] operationNames, int selectedIndex)
        {
            if (operationNames.Length == 0) return Lang.Get("vintagekinematics:kineticforgepress-operation-none");
            return operationNames[System.Math.Max(0, System.Math.Min(selectedIndex, operationNames.Length - 1))];
        }

        private bool OnToggleRecipeBrowser()
        {
            return recipeBrowser.Toggle(() => ComposeDialog(DialogTitle));
        }

        public override void Dispose()
        {
            recipeBrowser?.Dispose();
            base.Dispose();
        }

        public override void OnMouseDown(MouseEvent args)
        {
            if (TryStartDialogDrag(args))
            {
                args.Handled = true;
                return;
            }

            base.OnMouseDown(args);
        }

        public override void OnMouseMove(MouseEvent args)
        {
            if (draggingDialog)
            {
                MoveDraggedDialog(args.X, args.Y);
                args.Handled = true;
                return;
            }

            base.OnMouseMove(args);
        }

        public override void OnMouseUp(MouseEvent args)
        {
            if (draggingDialog)
            {
                draggingDialog = false;
                args.Handled = true;
                return;
            }

            base.OnMouseUp(args);
        }

        private List<ForgePressRecipeListItem> BuildRecipeListItems()
        {
            var registry = capi.ModLoader.GetModSystem<KineticForgePressRecipeRegistry>();
            var items = new List<ForgePressRecipeListItem>();
            if (registry == null) return items;

            foreach (KineticForgePressRecipe recipe in registry.Recipes)
            {
                items.Add(new ForgePressRecipeListItem(recipe));
            }

            return items;
        }

        private void EnsureMovableDialogPosition(string dialogName, double contentWidth, double contentHeight)
        {
            double dialogWidth = contentWidth + 2.0 * GuiStyle.ElementToDialogPadding;
            int maxX = (int)System.Math.Max(0.0, capi.Render.FrameWidth / RuntimeEnv.GUIScale - dialogWidth - GuiStyle.DialogToScreenPadding);
            Vec2i existing = capi.Gui.GetDialogPosition(dialogName);
            if (existing != null)
            {
                if (existing.X > maxX)
                {
                    capi.Gui.SetDialogPosition(dialogName, new Vec2i(maxX, existing.Y));
                }
                return;
            }

            int x = maxX;
            int y = (int)System.Math.Max(0.0, capi.Render.FrameHeight / RuntimeEnv.GUIScale / 2.0 - contentHeight / 2.0);
            capi.Gui.SetDialogPosition(dialogName, new Vec2i(x, y));
        }

        private bool TryStartDialogDrag(MouseEvent args)
        {
            if (args.Button != EnumMouseButton.Left || SingleComposer == null) return false;

            ElementBounds bounds = SingleComposer.Bounds;
            double localX = args.X - bounds.absX;
            double localY = args.Y - bounds.absY;
            if (localY < 0.0 || localY > GuiElement.scaled(TitleBarDragHeight)) return false;
            if (localX < 0.0 || localX > bounds.OuterWidth - GuiElement.scaled(TitleBarReservedRightWidth)) return false;

            draggingDialog = true;
            dragStartMouse = new Vec2i(args.X, args.Y);
            dragStartDialog = new Vec2i((int)bounds.fixedX, (int)bounds.fixedY);
            ForceComposerMovableAtCurrentPosition(bounds);
            return true;
        }

        private void MoveDraggedDialog(int mouseX, int mouseY)
        {
            ElementBounds bounds = SingleComposer.Bounds;
            double scale = RuntimeEnv.GUIScale;
            int x = dragStartDialog.X + (int)((mouseX - dragStartMouse.X) / scale);
            int y = dragStartDialog.Y + (int)((mouseY - dragStartMouse.Y) / scale);

            x = (int)GameMath.Clamp(x, 0, System.Math.Max(0, capi.Render.FrameWidth / scale - bounds.OuterWidth / scale));
            y = (int)GameMath.Clamp(y, 0, System.Math.Max(0, capi.Render.FrameHeight / scale - bounds.OuterHeight / scale));

            bounds.fixedX = x;
            bounds.fixedY = y;
            bounds.fixedOffsetX = 0.0;
            bounds.fixedOffsetY = 0.0;
            bounds.Alignment = EnumDialogArea.None;
            bounds.absMarginX = 0.0;
            bounds.absMarginY = 0.0;
            bounds.CalcWorldBounds();
            capi.Gui.SetDialogPosition(SingleComposer.DialogName, new Vec2i(x, y));
        }

        private void ForceComposerMovableAtCurrentPosition(ElementBounds bounds)
        {
            if (bounds.Alignment == EnumDialogArea.None) return;

            bounds.fixedX = bounds.absX / RuntimeEnv.GUIScale;
            bounds.fixedY = bounds.absY / RuntimeEnv.GUIScale;
            bounds.fixedOffsetX = 0.0;
            bounds.fixedOffsetY = 0.0;
            bounds.Alignment = EnumDialogArea.None;
            bounds.absMarginX = 0.0;
            bounds.absMarginY = 0.0;
            bounds.CalcWorldBounds();
        }

        public void OnOperationUpdated()
        {
            if (SingleComposer == null) return;
            RefreshOperationButtonLabel();
            RefreshProgressBar(true);
        }

        private void RefreshOperationButtonLabel()
        {
            GuiElementTextButton button = SingleComposer.GetButton(OperationButtonKey);
            if (button == null) return;

            GetOperationOptions(out _, out string[] operationNames, out int selectedIndex);
            string label = GetOperationButtonLabel(operationNames, selectedIndex);
            if (button.Text == label) return;

            button.Text = label;
            SingleComposer.ReCompose();
        }

        private void RefreshProgressBar(bool force)
        {
            if (SingleComposer == null) return;
            bool active = getCanProgress?.Invoke() == true;
            float progress = active ? getProgress?.Invoke() ?? 0f : 0f;
            float progressMax = active ? getProgressMax?.Invoke() ?? 1f : 1f;
            if (progressMax <= 0f) progressMax = 1f;

            bool changed = active != drawnProgressActive || Math.Abs(progress - drawnProgress) > 0.001f || Math.Abs(progressMax - drawnProgressMax) > 0.001f;
            if (!force && !changed) return;
            if (!force && capi.ElapsedMilliseconds - lastProgressRedrawMs < 125) return;

            drawnProgressActive = active;
            drawnProgress = progress;
            drawnProgressMax = progressMax;
            SingleComposer.GetCustomDraw(ProgressBarKey)?.Redraw();
            lastProgressRedrawMs = capi.ElapsedMilliseconds;
        }

        private void OnDrawProgressBar(Context ctx, ImageSurface surface, ElementBounds currentBounds)
        {
            float max = drawnProgressMax > 0f ? drawnProgressMax : 1f;
            float frac = GameMath.Clamp(drawnProgress / max, 0f, 1f);
            double width = currentBounds.InnerWidth;
            double height = currentBounds.InnerHeight;
            double radius = GuiElement.scaled(2.0);

            ctx.Save();
            RoundedRectangle(ctx, 0.0, 0.0, width, height, radius);
            ctx.SetSourceRGBA(0.05, 0.04, 0.03, 0.82);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.55, 0.46, 0.34, 0.9);
            ctx.LineWidth = GuiElement.scaled(1.0);
            ctx.Stroke();

            if (drawnProgressActive && frac > 0f)
            {
                double inset = GuiElement.scaled(2.0);
                double fillWidth = Math.Max(0.0, (width - 2.0 * inset) * frac);
                RoundedRectangle(ctx, inset, inset, fillWidth, Math.Max(0.0, height - 2.0 * inset), radius);
                using (LinearGradient gradient = new LinearGradient(0, 0, width, 0))
                {
                    gradient.AddColorStop(0, new Color(0.72, 0.36, 0.08, 1));
                    gradient.AddColorStop(1, new Color(0.95, 0.68, 0.18, 1));
                    ctx.SetSource(gradient);
                    ctx.Fill();
                }
            }

            ctx.Restore();
        }

        private static void RoundedRectangle(Context ctx, double x, double y, double width, double height, double radius)
        {
            if (width <= 0.0 || height <= 0.0) return;
            radius = Math.Min(radius, Math.Min(width, height) / 2.0);
            ctx.NewSubPath();
            ctx.Arc(x + width - radius, y + radius, radius, -Math.PI / 2.0, 0.0);
            ctx.Arc(x + width - radius, y + height - radius, radius, 0.0, Math.PI / 2.0);
            ctx.Arc(x + radius, y + height - radius, radius, Math.PI / 2.0, Math.PI);
            ctx.Arc(x + radius, y + radius, radius, Math.PI, 3.0 * Math.PI / 2.0);
            ctx.ClosePath();
        }
    }
}
