using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
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
            IOFaceMap explicitMap = BuildJsonIOFaceMap();
            if (explicitMap != null) return explicitMap;

            return MachineIoLayouts.SideInputOppositeAndDownOutput(
                Pos,
                JsonMachineIoBuilder.ResolveFace(Block, "localWest"),
                SlotInput,
                SlotOutputFirst,
                SlotOutputLast);
        }

        protected override KineticSawmillRecipe FindRecipe(ItemStack input)
        {
            return Api.ModLoader.GetModSystem<KineticSawmillRecipeRegistry>()?.FindRecipe(input, mode);
        }

        protected override System.Collections.Generic.IEnumerable<ItemStack> GetOutputs(KineticSawmillRecipe recipe) => ResolvedOutputs(recipe?.Outputs);

        protected override IEnumerable<ItemStack> GetOutputs(KineticSawmillRecipe recipe, ItemStack input)
        {
            if (recipe?.Outputs == null) yield break;

            foreach (JsonItemStack output in recipe.Outputs)
            {
                ItemStack stack = RecipeWildcardUtil.ResolveOutputStack(Api.World, output, recipe.Ingredient?.Code, input?.Collectible?.Code);
                if (stack != null) yield return stack;
            }
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
            return new GuiDialogKineticJsonProcessor(
                title,
                MachineInventory,
                Pos,
                SlotInput,
                SlotInput,
                SlotOutputFirst,
                SlotOutputLast,
                true,
                144.0,
                "left",
                CurrentWorkerProgress,
                CurrentWorkerProgressMax,
                CanProgressCurrentRecipe,
                capi,
                inputColumnsOverride: 1,
                outputColumnsOverride: 3,
                inputLabelLangCode: "vintagekinematics:kineticsawmill-input",
                outputLabelLangCode: "vintagekinematics:kineticsawmill-outputs",
                dialogKeyPrefix: "kineticsawmill",
                machineCode: "kineticsawmill",
                showRecipeBrowser: true,
                recipeTitleLangCode: "vintagekinematics:kineticsawmill-recipes",
                recipeSearchLangCode: "vintagekinematics:kineticsawmill-search-recipes",
                recipeBrowserWidth: 500.0,
                recipeBrowserListHeight: 360.0,
                buildRecipeItems: () => BuildRecipeListItems(capi),
                onRecipeClicked: OnRecipeClicked,
                recipeButtonLabel: () => SawmillModeLabel(mode),
                recipeSortValues: SawmillSortValues(),
                recipeSortNames: SawmillSortNames(),
                recipeBrowserCellHeight: 72);
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog)
        {
            (dialog as GuiDialogKineticJsonProcessor)?.RefreshRecipeButtonLabel();
        }

        private List<IRecipeBrowserListItem> BuildRecipeListItems(ICoreClientAPI capi)
        {
            var registry = capi.ModLoader.GetModSystem<KineticSawmillRecipeRegistry>();
            List<IRecipeBrowserListItem> items = new List<IRecipeBrowserListItem>();
            if (registry == null) return items;

            foreach (KineticSawmillRecipe recipe in registry.Recipes)
            {
                items.Add(new SawmillRecipeListItem(recipe, capi));
            }

            return items;
        }

        private void OnRecipeClicked(IRecipeBrowserListItem item)
        {
            if (item is SawmillRecipeListItem sawmillItem && sawmillItem.Recipe != null)
            {
                OnClientSelectMode(sawmillItem.Recipe.Mode);
            }
        }

        private static string[] SawmillSortValues()
        {
            return new[] { "output", "input", "work" };
        }

        private static string[] SawmillSortNames()
        {
            return new[]
            {
                Lang.Get("vintagekinematics:recipebrowser-sort-output"),
                Lang.Get("vintagekinematics:recipebrowser-sort-input"),
                Lang.Get("vintagekinematics:recipebrowser-sort-work")
            };
        }

        private static string SawmillModeLabel(SawmillMode selectedMode)
        {
            return selectedMode switch
            {
                SawmillMode.Shaft => Lang.Get("vintagekinematics:kineticsawmill-mode-shaft"),
                SawmillMode.Stick => Lang.Get("vintagekinematics:kineticsawmill-mode-stick"),
                SawmillMode.CogwheelSection => Lang.Get("vintagekinematics:kineticsawmill-mode-cogsection"),
                SawmillMode.Firewood => Lang.Get("vintagekinematics:kineticsawmill-mode-firewood"),
                SawmillMode.Gearbox => Lang.Get("vintagekinematics:kineticsawmill-mode-gearbox"),
                SawmillMode.Axle => Lang.Get("vintagekinematics:kineticsawmill-mode-axle"),
                SawmillMode.AngledGear => Lang.Get("vintagekinematics:kineticsawmill-mode-angledgear"),
                _ => Lang.Get("vintagekinematics:kineticsawmill-mode-plank")
            };
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
    }
}
