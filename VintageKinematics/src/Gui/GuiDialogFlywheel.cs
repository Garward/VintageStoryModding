using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogFlywheel : GuiDialogBlockEntity
    {
        private const string StatusKey = "flywheelStatus";
        private static readonly float[] BurstSteps = { 1f, 2f, 4f, 8f, 16f };

        private readonly Action<float> onSetBurst;

        private int storedSeconds;
        private int maxStoredSeconds;
        private bool releaseMode;
        private int burstMultiplier;
        private int maxBurstMultiplier;
        private int burstOutput;
        private int remainingSeconds;
        private int bankCount;

        public override double DrawOrder => 0.2;

        public GuiDialogFlywheel(
            string title,
            BlockPos pos,
            int storedSeconds,
            int maxStoredSeconds,
            bool releaseMode,
            int burstMultiplier,
            int maxBurstMultiplier,
            int burstOutput,
            int remainingSeconds,
            int bankCount,
            Action<float> onSetBurst,
            ICoreClientAPI capi)
            : base(title, pos, capi)
        {
            this.storedSeconds = storedSeconds;
            this.maxStoredSeconds = maxStoredSeconds;
            this.releaseMode = releaseMode;
            this.burstMultiplier = burstMultiplier;
            this.maxBurstMultiplier = maxBurstMultiplier;
            this.burstOutput = burstOutput;
            this.remainingSeconds = remainingSeconds;
            this.bankCount = bankCount;
            this.onSetBurst = onSetBurst;

            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double width = 300.0;
            double topOffset = 16.0;
            ElementBounds statusBounds = ElementBounds.Fixed(0.0, topOffset + 0.0, width, 88.0);
            ElementBounds labelBounds = ElementBounds.Fixed(0.0, topOffset + 98.0, width, 22.0);
            ElementBounds button1 = ElementBounds.Fixed(0.0, topOffset + 128.0, 52.0, 30.0);
            ElementBounds button2 = ElementBounds.Fixed(62.0, topOffset + 128.0, 52.0, 30.0);
            ElementBounds button4 = ElementBounds.Fixed(124.0, topOffset + 128.0, 52.0, 30.0);
            ElementBounds button8 = ElementBounds.Fixed(186.0, topOffset + 128.0, 52.0, 30.0);
            ElementBounds button16 = ElementBounds.Fixed(248.0, topOffset + 128.0, 52.0, 30.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(statusBounds, labelBounds, button1, button2, button4, button8, button16);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            SingleComposer = capi.Gui.CreateCompo("flywheel-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddDynamicText(GetStatusText(), CairoFont.WhiteSmallText(), statusBounds, StatusKey)
                    .AddStaticText(Lang.Get("vintagekinematics:flywheel-burst-label"), CairoFont.WhiteSmallText(), labelBounds)
                    .AddSmallButton("1x", () => OnBurstClicked(1f), button1, EnumButtonStyle.Normal, ButtonKey(1f))
                    .AddSmallButton("2x", () => OnBurstClicked(2f), button2, EnumButtonStyle.Normal, ButtonKey(2f))
                    .AddSmallButton("4x", () => OnBurstClicked(4f), button4, EnumButtonStyle.Normal, ButtonKey(4f))
                    .AddSmallButton("8x", () => OnBurstClicked(8f), button8, EnumButtonStyle.Normal, ButtonKey(8f))
                    .AddSmallButton("16x", () => OnBurstClicked(16f), button16, EnumButtonStyle.Normal, ButtonKey(16f))
                .EndChildElements()
                .Compose();

            RefreshButtons();
        }

        public void UpdateState(
            int storedSeconds,
            int maxStoredSeconds,
            bool releaseMode,
            int burstMultiplier,
            int maxBurstMultiplier,
            int burstOutput,
            int remainingSeconds,
            int bankCount)
        {
            this.storedSeconds = storedSeconds;
            this.maxStoredSeconds = maxStoredSeconds;
            this.releaseMode = releaseMode;
            this.burstMultiplier = burstMultiplier;
            this.maxBurstMultiplier = maxBurstMultiplier;
            this.burstOutput = burstOutput;
            this.remainingSeconds = remainingSeconds;
            this.bankCount = bankCount;

            if (SingleComposer == null) return;
            SingleComposer.GetDynamicText(StatusKey)?.SetNewText(GetStatusText());
            RefreshButtons();
        }

        private bool OnBurstClicked(float multiplier)
        {
            if (multiplier > maxBurstMultiplier) return true;
            onSetBurst?.Invoke(multiplier);
            return true;
        }

        private void RefreshButtons()
        {
            if (SingleComposer == null) return;
            foreach (float step in BurstSteps)
            {
                GuiElementTextButton button = SingleComposer.GetButton(ButtonKey(step));
                if (button == null) continue;
                button.Enabled = step <= maxBurstMultiplier;
                button.SetActive((int)step == burstMultiplier);
            }
        }

        private string GetStatusText()
        {
            int percent = maxStoredSeconds > 0 ? (int)MathF.Round(GameMath.Clamp(storedSeconds / (float)maxStoredSeconds, 0f, 1f) * 100f) : 0;
            string mode = releaseMode
                ? Lang.Get("vintagekinematics:flywheel-mode-release")
                : Lang.Get("vintagekinematics:flywheel-mode-charge");

            return Lang.Get(
                "vintagekinematics:flywheel-status",
                storedSeconds,
                maxStoredSeconds,
                percent,
                mode,
                bankCount,
                burstOutput,
                remainingSeconds);
        }

        private static string ButtonKey(float step)
        {
            return "burst" + step.ToString("0");
        }
    }
}
