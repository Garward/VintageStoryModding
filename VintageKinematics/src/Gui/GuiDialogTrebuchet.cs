using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Gui
{
    public class GuiDialogTrebuchet : GuiDialogBlockEntity
    {
        private const string DistanceKey = "trebuchetDistance";
        private const string AngleKey = "trebuchetAngle";
        private const string StatusKey = "trebuchetStatus";

        private readonly Action<float, float> onApply;
        private readonly Action<float, float> onLaunch;

        private float distance;
        private float angle;
        private float requiredSu;
        private string status;

        public override double DrawOrder => 0.2;

        public GuiDialogTrebuchet(
            string title,
            BlockPos pos,
            float distance,
            float angle,
            float requiredSu,
            string status,
            Action<float, float> onApply,
            Action<float, float> onLaunch,
            ICoreClientAPI capi)
            : base(title, pos, capi)
        {
            this.distance = distance;
            this.angle = angle;
            this.requiredSu = requiredSu;
            this.status = status;
            this.onApply = onApply;
            this.onLaunch = onLaunch;

            if (IsDuplicate) return;
            ComposeDialog(title);
        }

        private void ComposeDialog(string title)
        {
            double width = 280.0;
            ElementBounds distanceLabel = ElementBounds.Fixed(0.0, 0.0, width, 22.0);
            ElementBounds distanceInput = ElementBounds.Fixed(0.0, 26.0, width, 28.0);
            ElementBounds angleLabel = ElementBounds.Fixed(0.0, 66.0, width, 22.0);
            ElementBounds angleInput = ElementBounds.Fixed(0.0, 92.0, width, 28.0);
            ElementBounds statusBounds = ElementBounds.Fixed(0.0, 132.0, width, 46.0);
            ElementBounds applyBounds = ElementBounds.Fixed(0.0, 190.0, 132.0, 30.0);
            ElementBounds launchBounds = ElementBounds.Fixed(148.0, 190.0, 132.0, 30.0);

            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(distanceLabel, distanceInput, angleLabel, angleInput, statusBounds, applyBounds, launchBounds);

            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
                .WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

            SingleComposer = capi.Gui.CreateCompo("trebuchet-" + BlockEntityPosition, dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar(title, CloseIconPressed)
                .BeginChildElements(bgBounds)
                    .AddStaticText(Lang.Get("vintagekinematics:trebuchet-distance"), CairoFont.WhiteSmallText(), distanceLabel)
                    .AddNumberInput(distanceInput, _ => RefreshStatus(), CairoFont.WhiteDetailText(), DistanceKey)
                    .AddStaticText(Lang.Get("vintagekinematics:trebuchet-angle"), CairoFont.WhiteSmallText(), angleLabel)
                    .AddNumberInput(angleInput, _ => RefreshStatus(), CairoFont.WhiteDetailText(), AngleKey)
                    .AddDynamicText(GetStatusText(), CairoFont.WhiteSmallText(), statusBounds, StatusKey)
                    .AddSmallButton(Lang.Get("vintagekinematics:trebuchet-apply"), OnApplyClicked, applyBounds)
                    .AddSmallButton(Lang.Get("vintagekinematics:trebuchet-launch"), OnLaunchClicked, launchBounds)
                .EndChildElements()
                .Compose();

            SingleComposer.GetNumberInput(DistanceKey)?.SetValue(distance);
            SingleComposer.GetNumberInput(AngleKey)?.SetValue(angle);
            RefreshStatus();
        }

        public void UpdateState(float distance, float angle, float requiredSu, string status)
        {
            this.distance = distance;
            this.angle = angle;
            this.requiredSu = requiredSu;
            this.status = status;
            if (SingleComposer == null) return;

            SingleComposer.GetNumberInput(DistanceKey)?.SetValue(distance);
            SingleComposer.GetNumberInput(AngleKey)?.SetValue(angle);
            SingleComposer.GetDynamicText(StatusKey)?.SetNewText(GetStatusText());
        }

        private bool OnApplyClicked()
        {
            ReadInputs(out float newDistance, out float newAngle);
            onApply?.Invoke(newDistance, newAngle);
            return true;
        }

        private bool OnLaunchClicked()
        {
            ReadInputs(out float newDistance, out float newAngle);
            onLaunch?.Invoke(newDistance, newAngle);
            return true;
        }

        private void RefreshStatus()
        {
            ReadInputs(out distance, out angle);
            requiredSu = EstimateRequiredSu(distance, angle);
            SingleComposer?.GetDynamicText(StatusKey)?.SetNewText(GetStatusText());
        }

        private void ReadInputs(out float readDistance, out float readAngle)
        {
            readDistance = SingleComposer?.GetNumberInput(DistanceKey)?.GetValue() ?? distance;
            readAngle = SingleComposer?.GetNumberInput(AngleKey)?.GetValue() ?? angle;
            readDistance = GameMath.Clamp(readDistance, 8f, 1024f);
            readAngle = GameMath.Clamp(readAngle, 15f, 75f);
        }

        private string GetStatusText()
        {
            return Lang.Get("vintagekinematics:trebuchet-status", requiredSu, status);
        }

        private static float EstimateRequiredSu(float distance, float angle)
        {
            float anglePenalty = 1f + MathF.Abs(GameMath.Clamp(angle, 15f, 75f) - 45f) / 45f;
            return 2048f + GameMath.Clamp(distance, 8f, 1024f) * 96f * anglePenalty;
        }
    }
}
