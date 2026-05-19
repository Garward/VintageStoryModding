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
        private bool contentClassesRegistered;

        public override void StartPre(ICoreAPI api)
        {
            // Universal category so the client can also resolve crusher/sieve recipes for
            // bottom-out impact effects (the vanilla "recipes" category is server-only).
            // Must run before asset enumeration, hence StartPre.
            new Vintagestory.API.Common.AssetCategory("vkrecipe", true, EnumAppSide.Universal);
            RegisterContentClasses(api);
        }

        public override void Start(ICoreAPI api)
        {
            api.Logger.Notification("[VintageKinematics] Starting...");
            RegisterContentClasses(api);

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
        }

        private void RegisterContentClasses(ICoreAPI api)
        {
            if (contentClassesRegistered) return;
            contentClassesRegistered = true;

            api.RegisterBlockEntityBehaviorClass("Kinetic", typeof(BEBehaviorKinetic));
            api.RegisterBlockEntityBehaviorClass("KineticSource", typeof(BEBehaviorKineticSource));
            api.RegisterBlockEntityBehaviorClass("KineticWorker", typeof(BEBehaviorKineticWorker));
            api.RegisterBlockEntityBehaviorClass("KineticAnimator", typeof(BEBehaviorKineticAnimator));
            api.RegisterBlockEntityBehaviorClass("KineticSound", typeof(BEBehaviorKineticSound));
            api.RegisterBlockEntityBehaviorClass("KineticPiston", typeof(BEBehaviorKineticPiston));
            api.RegisterBlockEntityBehaviorClass("KineticStretch", typeof(BEBehaviorKineticStretch));
            api.RegisterBlockEntityBehaviorClass("KineticLinkedPleat", typeof(BEBehaviorKineticLinkedPleat));
            api.RegisterBlockEntityBehaviorClass("KineticAnimationDriver", typeof(BEBehaviorKineticAnimationDriver));
            api.RegisterBlockEntityBehaviorClass("KineticMultiblock", typeof(BEBehaviorKineticMultiblock));
            api.RegisterBlockEntityBehaviorClass("BellowsPulse", typeof(BEBehaviorBellowsPulse));
            api.RegisterBlockEntityBehaviorClass("CrusherProcess", typeof(BEBehaviorCrusherProcess));
            api.RegisterBlockEntityClass("Kinetic", typeof(BlockEntities.BEKinetic));
            api.RegisterBlockEntityClass("HandCrank", typeof(BlockEntities.BEHandCrank));
            api.RegisterBlockEntityClass("KineticAnimated", typeof(BlockEntities.BEKineticAnimated));
            api.RegisterBlockEntityClass("KineticQuern", typeof(BlockEntities.BEKineticQuern));
            api.RegisterBlockEntityClass("CrusherBasin", typeof(BlockEntities.BECrusherBasin));
            api.RegisterBlockEntityClass("Belt", typeof(BlockEntities.BEBelt));
            api.RegisterBlockEntityClass("Funnel", typeof(BlockEntities.BEFunnel));
            api.RegisterBlockEntityClass("CoalMotor", typeof(BlockEntities.BECoalMotor));
            api.RegisterBlockEntityClass("CounterweightDrive", typeof(BlockEntities.BECounterweightDrive));
            api.RegisterBlockEntityClass("Treadwheel", typeof(BlockEntities.BETreadwheel));
            api.RegisterBlockEntityClass("Flywheel", typeof(BlockEntities.BEFlywheel));
            api.RegisterBlockEntityClass("ReinforcedChest", typeof(BlockEntities.BEReinforcedChest));
            api.RegisterBlockEntityClass("DoubleReinforcedChest", typeof(BlockEntities.BEDoubleReinforcedChest));
            api.RegisterBlockEntityClass("BulkCrate", typeof(BlockEntities.BEBulkCrate));
            api.RegisterMountable("vktreadwheel", BlockEntities.BETreadwheel.GetMountable);
            api.RegisterBlockEntityClass("Trebuchet", typeof(BlockEntities.BETrebuchet));
            api.RegisterMountable("vktrebuchet", BlockEntities.BETrebuchet.GetMountable);
            api.RegisterBlockEntityClass("KineticSieve", typeof(BlockEntities.BEKineticSieve));
            api.RegisterBlockEntityClass("PrimitiveSieve", typeof(BlockEntities.BEPrimitiveSieve));
            api.RegisterBlockEntityClass("KineticSawmill", typeof(BlockEntities.BEKineticSawmill));
            api.RegisterBlockEntityClass("KineticPress", typeof(BlockEntities.BEKineticPress));
            api.RegisterBlockEntityClass("KineticForgePress", typeof(BlockEntities.BEKineticForgePress));
            api.RegisterBlockEntityClass("KineticMixer", typeof(BlockEntities.BEKineticMixer));
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
            api.RegisterBlockClass("BlockTreadwheel", typeof(Blocks.BlockTreadwheel));
            api.RegisterBlockClass("BlockFlywheel", typeof(Blocks.BlockFlywheel));
            api.RegisterBlockClass("BlockVKStorage", typeof(Blocks.BlockVKStorage));
            api.RegisterBlockClass("BlockTrebuchet", typeof(Blocks.BlockTrebuchet));
            api.RegisterBlockClass("BlockCounterweightDrive", typeof(Blocks.BlockCounterweightDrive));
            api.RegisterBlockClass("BlockKineticSieve", typeof(Blocks.BlockKineticSieve));
            api.RegisterBlockClass("BlockPrimitiveSieve", typeof(Blocks.BlockPrimitiveSieve));
            api.RegisterBlockClass("BlockKineticSawmill", typeof(Blocks.BlockKineticSawmill));
            api.RegisterBlockClass("BlockKineticPress", typeof(Blocks.BlockKineticPress));
            api.RegisterBlockClass("BlockKineticForgePress", typeof(Blocks.BlockKineticForgePress));
            api.RegisterBlockClass("BlockKineticBellows", typeof(Blocks.BlockKineticBellows));
            api.RegisterBlockClass("BlockKineticMixer", typeof(Blocks.BlockKineticMixer));
            api.RegisterBlockClass("BlockKineticIgniter", typeof(Blocks.BlockKineticIgniter));
            api.RegisterBlockClass("BlockKineticBore", typeof(Blocks.BlockKineticBore));
            api.RegisterItemClass("ItemBelt", typeof(Items.ItemBelt));
            api.RegisterItemClass("ItemPoweredDrill", typeof(Items.ItemPoweredDrill));
            api.RegisterItemClass("ItemKineticWrench", typeof(Items.ItemKineticWrench));
            api.RegisterItemClass("ItemPogoRod", typeof(Items.ItemPogoRod));
            api.ClassRegistry.GetType().GetMethod("RegisterInventoryClass")?.Invoke(
                api.ClassRegistry,
                new object[] { Items.InventoryPoweredDrillFuel.InventoryClassName, typeof(Items.InventoryPoweredDrillFuel) }
            );
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
