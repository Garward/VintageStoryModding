using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.Compatibility;
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
        private System.IDisposable contraptionToolMovingParts;
        private System.IDisposable contraptionToolWork;
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
            Items.ItemKineticBoots.StartSprintSystem(api);
            contraptionToolWork = ContraptionWorkRegistry.Subscribe(new Blocks.ContraptionToolWorkProvider());

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            ImmersiveMiningPoweredToolCompat.Patch(api, harmony);
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
            api.RegisterBlockBehaviorClass("CanonicalDrop", typeof(Blocks.BlockBehaviorCanonicalDrop));
            api.RegisterBlockEntityClass("Kinetic", typeof(BlockEntities.BEKinetic));
            api.RegisterBlockEntityClass("HandCrank", typeof(BlockEntities.BEHandCrank));
            api.RegisterBlockEntityClass("KineticAnimated", typeof(BlockEntities.BEKineticAnimated));
            api.RegisterBlockEntityClass("KineticQuern", typeof(BlockEntities.BEKineticQuern));
            api.RegisterBlockEntityClass("CrusherBasin", typeof(BlockEntities.BECrusherBasin));
            api.RegisterBlockEntityClass("Belt", typeof(BlockEntities.BEBelt));
            api.RegisterBlockEntityClass("Funnel", typeof(BlockEntities.BEFunnel));
            api.RegisterBlockEntityClass("Trashcan", typeof(BlockEntities.BETrashcan));
            api.RegisterBlockEntityClass("CoalMotor", typeof(BlockEntities.BECoalMotor));
            api.RegisterBlockEntityClass("CounterweightDrive", typeof(BlockEntities.BECounterweightDrive));
            api.RegisterBlockEntityClass("Treadwheel", typeof(BlockEntities.BETreadwheel));
            api.RegisterBlockEntityClass("Flywheel", typeof(BlockEntities.BEFlywheel));
            api.RegisterBlockEntityClass("BackpackFlywheelPlaced", typeof(BlockEntities.BEBackpackFlywheelPlaced));
            api.RegisterBlockEntityClass("ReinforcedChest", typeof(BlockEntities.BEReinforcedChest));
            api.RegisterBlockEntityClass("DoubleReinforcedChest", typeof(BlockEntities.BEDoubleReinforcedChest));
            api.RegisterBlockEntityClass("BulkCrate", typeof(BlockEntities.BEBulkCrate));
            api.RegisterMountable("vktreadwheel", BlockEntities.BETreadwheel.GetMountable);
            api.RegisterBlockEntityClass("Trebuchet", typeof(BlockEntities.BETrebuchet));
            api.RegisterMountable("vktrebuchet", BlockEntities.BETrebuchet.GetMountable);
            api.RegisterBlockEntityClass("KineticSieve", typeof(BlockEntities.BEKineticSieve));
            api.RegisterBlockEntityClass("PrimitiveSieve", typeof(BlockEntities.BEPrimitiveSieve));
            api.RegisterBlockEntityClass("KineticSawmill", typeof(BlockEntities.BEKineticSawmill));
            api.RegisterBlockEntityClass("KineticJsonProcessor", typeof(BlockEntities.BEKineticJsonProcessor));
            api.RegisterBlockEntityClass("KineticPress", typeof(BlockEntities.BEKineticPress));
            api.RegisterBlockEntityClass("KineticExtractor", typeof(BlockEntities.BEKineticPress));
            api.RegisterBlockEntityClass("KineticForgePress", typeof(BlockEntities.BEKineticForgePress));
            api.RegisterBlockEntityClass("KineticCharcoalRetort", typeof(BlockEntities.BEKineticCharcoalRetort));
            api.RegisterBlockEntityClass("KineticMixer", typeof(BlockEntities.BEKineticMixer));
            api.RegisterBlockEntityClass("KineticIgniter", typeof(BlockEntities.BEKineticIgniter));
            api.RegisterBlockEntityClass("KineticBore", typeof(BlockEntities.BEKineticBore));
            api.RegisterBlockEntityClass("CopperPipe", typeof(BlockEntities.BECopperPipe));
            api.RegisterBlockEntityClass("CopperPump", typeof(BlockEntities.BECopperPump));
            api.RegisterBlockEntityClass("GeothermalBore", typeof(BlockEntities.BEGeothermalBore));
            api.RegisterBlockEntityClass("GeothermalSteamEngine", typeof(BlockEntities.BEGeothermalSteamEngine));
            api.RegisterBlockEntityClass("GantryCarriage", typeof(BlockEntities.BEGantryCarriage));
            api.RegisterBlockEntityClass("GantryShaft", typeof(BlockEntities.BEGantryShaft));
            api.RegisterBlockEntityClass("KineticClutch", typeof(BlockEntities.BEKineticClutch));
            api.RegisterBlockEntityClass("KineticReverser", typeof(BlockEntities.BEKineticReverser));
            api.RegisterBlockEntityClass("KineticActivator", typeof(BlockEntities.BEKineticActivator));
            api.RegisterEntity("EntityVKContraption", typeof(Entities.EntityVKContraption));
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
            api.RegisterBlockClass("BlockTrashcan", typeof(Blocks.BlockTrashcan));
            api.RegisterBlockClass("BlockCoalMotor", typeof(Blocks.BlockCoalMotor));
            api.RegisterBlockClass("BlockTreadwheel", typeof(Blocks.BlockTreadwheel));
            api.RegisterBlockClass("BlockFlywheel", typeof(Blocks.BlockFlywheel));
            api.RegisterBlockClass("BlockBackpackFlywheelPlaced", typeof(Blocks.BlockBackpackFlywheelPlaced));
            api.RegisterBlockClass("BlockVKStorage", typeof(Blocks.BlockVKStorage));
            api.RegisterBlockClass("BlockBulkCrate", typeof(Blocks.BlockBulkCrate));
            api.RegisterBlockClass("BlockTrebuchet", typeof(Blocks.BlockTrebuchet));
            api.RegisterBlockClass("BlockCounterweightDrive", typeof(Blocks.BlockCounterweightDrive));
            api.RegisterBlockClass("BlockKineticSieve", typeof(Blocks.BlockKineticSieve));
            api.RegisterBlockClass("BlockPrimitiveSieve", typeof(Blocks.BlockPrimitiveSieve));
            api.RegisterBlockClass("BlockKineticSidePlaced", typeof(Blocks.BlockKineticSidePlaced));
            api.RegisterBlockClass("BlockKineticOpenableMachine", typeof(Blocks.BlockKineticOpenableMachine));
            api.RegisterBlockClass("BlockKineticJsonProcessor", typeof(Blocks.BlockKineticJsonProcessor));
            api.RegisterBlockClass("BlockKineticForgePress", typeof(Blocks.BlockKineticForgePress));
            api.RegisterBlockClass("BlockKineticCharcoalRetort", typeof(Blocks.BlockKineticCharcoalRetort));
            api.RegisterBlockClass("BlockKineticBellows", typeof(Blocks.BlockKineticBellows));
            api.RegisterBlockClass("BlockKineticMixer", typeof(Blocks.BlockKineticMixer));
            api.RegisterBlockClass("BlockKineticIgniter", typeof(Blocks.BlockKineticIgniter));
            api.RegisterBlockClass("BlockKineticBore", typeof(Blocks.BlockKineticBore));
            api.RegisterBlockClass("BlockCopperPipe", typeof(Blocks.BlockCopperPipe));
            api.RegisterBlockClass("BlockCopperPump", typeof(Blocks.BlockCopperPump));
            api.RegisterBlockClass("BlockGeothermalPipe", typeof(Blocks.BlockGeothermalPipe));
            api.RegisterBlockClass("BlockGeothermalBore", typeof(Blocks.BlockGeothermalBore));
            api.RegisterBlockClass("BlockGeothermalSteamEngine", typeof(Blocks.BlockGeothermalSteamEngine));
            api.RegisterBlockClass("BlockGantryCarriage", typeof(Blocks.BlockGantryCarriage));
            api.RegisterBlockClass("BlockKineticClutch", typeof(Blocks.BlockKineticClutch));
            api.RegisterBlockClass("BlockKineticReverser", typeof(Blocks.BlockKineticReverser));
            api.RegisterBlockClass("BlockKineticActivator", typeof(Blocks.BlockKineticActivator));
            api.RegisterBlockClass("BlockContraptionTool", typeof(Blocks.BlockContraptionTool));
            api.RegisterItemClass("ItemBelt", typeof(Items.ItemBelt));
            api.RegisterItemClass("ItemPoweredDrill", typeof(Items.ItemPoweredDrill));
            api.RegisterItemClass("ItemPoweredSaw", typeof(Items.ItemPoweredSaw));
            api.RegisterItemClass("ItemBackpackFlywheel", typeof(Items.ItemBackpackFlywheel));
            api.RegisterItemClass("ItemKineticWrench", typeof(Items.ItemKineticWrench));
            api.RegisterItemClass("ItemPogoRod", typeof(Items.ItemPogoRod));
            api.RegisterItemClass("ItemKineticBoots", typeof(Items.ItemKineticBoots));
            api.RegisterItemClass("ItemMechanicalBinder", typeof(Items.ItemMechanicalBinder));
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
            contraptionToolMovingParts = ContraptionMovingPartRegistry.Subscribe(new ContraptionToolMovingPartProvider());
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            Items.ItemKineticBoots.StopSprintSystem();
            contraptionToolWork?.Dispose();
            contraptionToolMovingParts?.Dispose();
            placementPreview?.Dispose();
            base.Dispose();
        }
    }
}
