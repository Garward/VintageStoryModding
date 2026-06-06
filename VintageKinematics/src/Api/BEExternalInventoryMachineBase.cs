using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Gui;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Base for inventory/controller blocks whose progress is driven by an external block entity
    /// or behavior, such as a kinetic head above a passive basin.
    /// </summary>
    public abstract class BEExternalInventoryMachineBase : BEOpenableInventoryMachineBase
    {
        protected BEExternalInventoryMachineBase(string inventoryClassName, int inventorySize, int inputSlot, int outputFirst, int outputLast)
            : base(inventoryClassName, inventorySize, inputSlot, outputFirst, outputLast)
        {
        }

        protected BEExternalInventoryMachineBase(string inventoryClassName, int inventorySize, int inputFirst, int inputLast, int outputFirst, int outputLast)
            : base(inventoryClassName, inventorySize, inputFirst, inputLast, outputFirst, outputLast)
        {
        }

        protected BEExternalInventoryMachineBase(
            string inventoryClassName,
            int inventorySize,
            int inputFirst,
            int inputLast,
            int outputFirst,
            int outputLast,
            System.Func<int, InventoryBase, ItemSlot> slotFactory)
            : base(inventoryClassName, inventorySize, inputFirst, inputLast, outputFirst, outputLast, slotFactory)
        {
        }

        protected virtual JsonObject ExternalAttr => Block?.Attributes?["vkExternalProcessor"];
        protected virtual JsonObject ProgressSourceAttr => ExternalAttr?["progressSource"];
        protected override string TitleLangCode => ExternalAttr?["titleLangCode"].AsString(base.TitleLangCode) ?? base.TitleLangCode;
        protected override string FallbackTitle => ExternalAttr?["title"].AsString(base.FallbackTitle) ?? base.FallbackTitle;
        protected virtual bool ShowProgressBar => ProgressBarAttr?["enabled"].AsBool(ExternalAttr?["showProgressBar"].AsBool(true) ?? true) ?? ExternalAttr?["showProgressBar"].AsBool(true) ?? true;
        protected virtual double ProgressBarWidth => ProgressBarAttr?["width"].AsDouble(144.0) ?? 144.0;
        protected virtual string ProgressBarAlign => ProgressBarAttr?["align"].AsString("left") ?? "left";
        protected virtual int InputColumns => ExternalAttr?["inputColumns"].AsInt(0) ?? 0;
        protected virtual int OutputColumns => ExternalAttr?["outputColumns"].AsInt(0) ?? 0;
        protected virtual string InputLabelLangCode => ExternalAttr?["inputLabelLangCode"].AsString("vintagekinematics:jsonprocessor-input") ?? "vintagekinematics:jsonprocessor-input";
        protected virtual string OutputLabelLangCode => ExternalAttr?["outputLabelLangCode"].AsString("vintagekinematics:jsonprocessor-outputs") ?? "vintagekinematics:jsonprocessor-outputs";
        protected virtual string DialogKeyPrefix => ExternalAttr?["dialogKey"].AsString(InventoryClassName) ?? InventoryClassName;

        private JsonObject ProgressBarAttr => ExternalAttr?["progressBar"];

        protected override IOFaceMap BuildIOFaceMap()
        {
            IOFaceMap explicitMap = BuildJsonIOFaceMap(ExternalAttr?["io"]);
            if (explicitMap != null) return explicitMap;
            return MachineIoLayouts.SideInputOppositeAndDownOutput(
                Pos,
                JsonMachineIoBuilder.ResolveFace(Block, ExternalAttr?["inputFace"].AsString("left")),
                ActiveInputFirst,
                ActiveInputLast,
                ActiveOutputFirst,
                ActiveOutputLast);
        }

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
                ProgressBarAlign,
                CurrentExternalProgress,
                CurrentExternalProgressMax,
                CanProgressExternalWork,
                capi,
                inputColumnsOverride: InputColumns,
                outputColumnsOverride: OutputColumns,
                inputLabelLangCode: InputLabelLangCode,
                outputLabelLangCode: OutputLabelLangCode,
                dialogKeyPrefix: DialogKeyPrefix);
        }

        protected IExternalWorkProgressProvider ResolveProgressProvider()
        {
            BlockEntity be = ResolveProgressBlockEntity();
            if (be == null) return null;

            string code = ProgressSourceAttr?["behavior"].AsString(null);
            if (be is IExternalWorkProgressProvider direct && ProgressProviderMatches(direct, code)) return direct;

            foreach (BlockEntityBehavior behavior in be.Behaviors)
            {
                if (behavior is IExternalWorkProgressProvider provider && ProgressProviderMatches(provider, code))
                {
                    return provider;
                }
            }

            return null;
        }

        protected BlockEntity ResolveProgressBlockEntity()
        {
            Vec3i offset = ReadOffset(ProgressSourceAttr?["offset"], new Vec3i(0, 1, 0));
            if (ProgressSourceAttr?["rotateOffset"].AsBool(false) == true)
            {
                offset = RotateOffsetY(offset, (int)(Block?.Shape?.rotateY ?? 0f));
            }

            return Api?.World?.BlockAccessor.GetBlockEntity(Pos.AddCopy(offset.X, offset.Y, offset.Z));
        }

        protected virtual float CurrentExternalProgress() => ResolveProgressProvider()?.ExternalWorkProgress ?? 0f;
        protected virtual float CurrentExternalProgressMax() => ResolveProgressProvider()?.ExternalWorkProgressMax ?? 1f;
        protected virtual bool CanProgressExternalWork() => ResolveProgressProvider()?.CanProgressExternalWork() == true;

        private static bool ProgressProviderMatches(IExternalWorkProgressProvider provider, string code)
        {
            if (provider == null) return false;
            if (string.IsNullOrEmpty(code)) return true;
            if (provider.ExternalProgressProviderCode == code) return true;
            return provider.GetType().Name == code;
        }

        private static Vec3i ReadOffset(JsonObject attr, Vec3i fallback)
        {
            if (attr == null || !attr.Exists) return fallback;

            JsonObject[] values = attr.AsArray();
            if (values != null && values.Length >= 3)
            {
                return new Vec3i(values[0].AsInt(), values[1].AsInt(), values[2].AsInt());
            }

            return new Vec3i(attr["x"].AsInt(fallback.X), attr["y"].AsInt(fallback.Y), attr["z"].AsInt(fallback.Z));
        }

        private static Vec3i RotateOffsetY(Vec3i offset, int rotateYDeg)
        {
            int steps = (((rotateYDeg / 90) % 4) + 4) % 4;
            int x = offset.X;
            int z = offset.Z;
            for (int i = 0; i < steps; i++)
            {
                int nx = z;
                int nz = -x;
                x = nx;
                z = nz;
            }
            return new Vec3i(x, offset.Y, z);
        }
    }
}
