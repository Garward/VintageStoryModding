using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Network;
using VintageKinematics.Rendering;

namespace VintageKinematics
{
    public class VintageKinematicsModSystem : ModSystem
    {
        private const string HarmonyId = "vintagekinematics";
        private Harmony harmony;
        private KineticPlacementPreviewRenderer placementPreview;

        public override void StartPre(ICoreAPI api)
        {
            // Universal category so the client can also resolve crusher/sieve recipes for
            // bottom-out impact effects (the vanilla "recipes" category is server-only).
            // Must run before asset enumeration, hence StartPre.
            new Vintagestory.API.Common.AssetCategory("vkrecipe", true, EnumAppSide.Universal);
        }

        public override void Start(ICoreAPI api)
        {
            api.Logger.Notification("[VintageKinematics] Starting...");

            api.RegisterBlockEntityBehaviorClass("Kinetic", typeof(BEBehaviorKinetic));
            api.RegisterBlockEntityBehaviorClass("KineticSource", typeof(BEBehaviorKineticSource));
            api.RegisterBlockEntityBehaviorClass("KineticWorker", typeof(BEBehaviorKineticWorker));
            api.RegisterBlockEntityBehaviorClass("KineticAnimator", typeof(BEBehaviorKineticAnimator));
            api.RegisterBlockEntityBehaviorClass("KineticSound", typeof(BEBehaviorKineticSound));
            api.RegisterBlockEntityBehaviorClass("KineticPiston", typeof(BEBehaviorKineticPiston));
            api.RegisterBlockEntityBehaviorClass("KineticAnimationDriver", typeof(BEBehaviorKineticAnimationDriver));
            api.RegisterBlockEntityBehaviorClass("KineticMultiblock", typeof(BEBehaviorKineticMultiblock));
            api.RegisterBlockEntityBehaviorClass("CrusherProcess", typeof(BEBehaviorCrusherProcess));
            api.RegisterBlockEntityClass("Kinetic", typeof(BlockEntities.BEKinetic));
            api.RegisterBlockEntityClass("HandCrank", typeof(BlockEntities.BEHandCrank));
            api.RegisterBlockEntityClass("KineticAnimated", typeof(BlockEntities.BEKineticAnimated));
            api.RegisterBlockEntityClass("KineticQuern", typeof(BlockEntities.BEKineticQuern));
            api.RegisterBlockEntityClass("CrusherBasin", typeof(BlockEntities.BECrusherBasin));
            api.RegisterBlockEntityClass("Belt", typeof(BlockEntities.BEBelt));
            api.RegisterBlockEntityClass("Funnel", typeof(BlockEntities.BEFunnel));
            api.RegisterBlockEntityClass("CoalMotor", typeof(BlockEntities.BECoalMotor));
            api.RegisterBlockEntityClass("KineticSieve", typeof(BlockEntities.BEKineticSieve));
            api.RegisterBlockEntityClass("PrimitiveSieve", typeof(BlockEntities.BEPrimitiveSieve));
            api.RegisterBlockEntityClass("KineticSawmill", typeof(BlockEntities.BEKineticSawmill));
            api.RegisterBlockEntityClass("KineticPress", typeof(BlockEntities.BEKineticPress));
            api.RegisterBlockEntityClass("KineticForgePress", typeof(BlockEntities.BEKineticForgePress));
            api.RegisterBlockEntityClass("KineticIgniter", typeof(BlockEntities.BEKineticIgniter));
            api.RegisterBlockEntityClass("KineticBore", typeof(BlockEntities.BEKineticBore));
            api.RegisterBlockClass("BlockShaft", typeof(Blocks.BlockShaft));
            api.RegisterBlockClass("BlockHandCrank", typeof(Blocks.BlockHandCrank));
            api.RegisterBlockClass("BlockCogwheel", typeof(Blocks.BlockCogwheel));
            api.RegisterBlockClass("BlockLargeCogwheel", typeof(Blocks.BlockLargeCogwheel));
            api.RegisterBlockClass("BlockGearbox", typeof(Blocks.BlockGearbox));
            api.RegisterBlockClass("BlockEncasedShaft", typeof(Blocks.BlockEncasedShaft));
            api.RegisterBlockClass("BlockKineticQuern", typeof(Blocks.BlockKineticQuern));
            api.RegisterBlockClass("BlockCrusher", typeof(Blocks.BlockCrusher));
            api.RegisterBlockClass("BlockCrusherBasin", typeof(Blocks.BlockCrusherBasin));
            api.RegisterBlockClass("BlockPlatePiston", typeof(Blocks.BlockPlatePiston));
            api.RegisterBlockClass("BlockBelt", typeof(Blocks.BlockBelt));
            api.RegisterBlockClass("BlockFunnel", typeof(Blocks.BlockFunnel));
            api.RegisterBlockClass("BlockCoalMotor", typeof(Blocks.BlockCoalMotor));
            api.RegisterBlockClass("BlockKineticSieve", typeof(Blocks.BlockKineticSieve));
            api.RegisterBlockClass("BlockPrimitiveSieve", typeof(Blocks.BlockPrimitiveSieve));
            api.RegisterBlockClass("BlockKineticSawmill", typeof(Blocks.BlockKineticSawmill));
            api.RegisterBlockClass("BlockKineticPress", typeof(Blocks.BlockKineticPress));
            api.RegisterBlockClass("BlockKineticForgePress", typeof(Blocks.BlockKineticForgePress));
            api.RegisterBlockClass("BlockKineticIgniter", typeof(Blocks.BlockKineticIgniter));
            api.RegisterBlockClass("BlockKineticBore", typeof(Blocks.BlockKineticBore));
            api.RegisterItemClass("ItemBelt", typeof(Items.ItemBelt));

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            base.StartClientSide(capi);
            placementPreview = new KineticPlacementPreviewRenderer(capi);
            capi.Event.RegisterRenderer(placementPreview, EnumRenderStage.Opaque);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            placementPreview?.Dispose();
            base.Dispose();
        }
    }
}
