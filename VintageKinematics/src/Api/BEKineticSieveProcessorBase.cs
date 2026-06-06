using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Crafting;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared shell for VK's weighted-output sieve processors. Keeps the public machine plumbing
    /// from <see cref="BEKineticItemProcessorBase{TRecipe}"/> and adds sieve-specific weighted
    /// rolls, vanilla panning fallback, yield scaling, and effects.
    /// </summary>
    public abstract class BEKineticSieveProcessorBase : BEKineticItemProcessorBase<KineticSieveRecipe>
    {
        public const int SlotInput = 0;
        public const int SlotOutputFirst = 1;

        private const int PannableRollsPerBlock = 8;
        private static readonly AssetLocation PanningSound = new AssetLocation("sounds/player/panning.ogg");

        protected BEBehaviorKineticWorker Worker { get; private set; }
        protected BEBehaviorKinetic Kinetic { get; private set; }

        protected BEKineticSieveProcessorBase(string inventoryClassName, int inventorySize, int outputLast)
            : base(inventoryClassName, inventorySize, SlotInput, SlotOutputFirst, outputLast)
        {
        }

        protected virtual bool AllowCustomSieveRecipes => true;
        protected virtual bool AllowVanillaPanningDrops => true;
        protected virtual float EffectVolume => 0.6f;
        protected virtual int EffectParticleCount => 12;
        protected virtual float EffectParticleSpread => 0.4f;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            Worker = GetBehavior<BEBehaviorKineticWorker>();
            Kinetic = GetBehavior<BEBehaviorKinetic>();

            if (api.Side == EnumAppSide.Server)
            {
                PanLootRoller.Init(api);
            }
        }

        protected override KineticSieveRecipe FindRecipe(ItemStack input)
        {
            if (!AllowCustomSieveRecipes) return null;
            return Api.ModLoader.GetModSystem<KineticSieveRecipeRegistry>()?.FindRecipe(input);
        }

        protected override IEnumerable<ItemStack> GetOutputs(KineticSieveRecipe recipe)
        {
            yield break;
        }

        protected override bool HasProcessableInput()
        {
            ItemSlot slot = MachineInventory[SlotInput];
            if (base.CanProcessInputSlot(slot)) return true;

            VintageKinematicsConfig cfg = Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            return AllowVanillaPanningDrops
                && (cfg?.UseVanillaPanningDrops ?? true)
                && slot?.Itemstack?.Block != null
                && PanLootRoller.IsSieveablePanningSource(slot.Itemstack.Block);
        }

        protected override void OnWorkCycle(KineticWorkCompletedArgs args)
        {
            ItemSlot slot = MachineInventory[SlotInput];
            if (slot.Empty) return;

            ItemStack input = slot.Itemstack;
            List<ItemStack> drops = null;
            bool consume = false;
            bool usedPanningDrops = false;
            VintageKinematicsConfig cfg = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;

            KineticSieveRecipe recipe = FindRecipe(input);
            if (recipe != null)
            {
                ItemStack drop = recipe.RollOutput(Api.World);
                if (drop != null) drops = new List<ItemStack> { drop };
                consume = true;
            }
            else if (AllowVanillaPanningDrops
                && (cfg?.UseVanillaPanningDrops ?? true)
                && input.Block != null
                && PanLootRoller.IsSieveablePanningSource(input.Block))
            {
                drops = new List<ItemStack>(PannableRollsPerBlock);
                for (int i = 0; i < PannableRollsPerBlock; i++)
                {
                    ItemStack drop = PanLootRoller.RollPannableDrop(Api.World, input.Block);
                    if (drop != null) drops.Add(drop);
                }
                consume = true;
                usedPanningDrops = true;
            }

            if (!consume) return;

            PlaySieveEffects(input);

            slot.TakeOut(1);
            slot.MarkDirty();

            if (drops == null) return;
            float sourceYieldMultiplier = usedPanningDrops ? PanningYieldMultiplier(cfg) : 1f;
            foreach (ItemStack drop in drops)
            {
                ItemStack scaled = ApplyYieldMultiplier(drop, cfg, sourceYieldMultiplier);
                if (scaled != null) DepositOutput(scaled);
            }
        }

        protected virtual float PanningYieldMultiplier(VintageKinematicsConfig cfg) => 1f;

        protected virtual Vec3d EffectPosition()
        {
            return new Vec3d(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        }

        protected virtual Vec3d ParticlePosition()
        {
            return OutputDropPosition();
        }

        protected virtual void PlaySieveEffects(ItemStack input)
        {
            Vec3d soundAt = EffectPosition();
            Api.World.PlaySoundAt(PanningSound, soundAt.X, soundAt.Y, soundAt.Z, null, randomizePitch: true, range: 16, volume: EffectVolume);

            if (input?.Block == null) return;
            Vec3d particleAt = ParticlePosition();
            Api.World.SpawnCubeParticles(particleAt, new ItemStack(input.Block), 0.3f, EffectParticleCount, EffectParticleSpread);
        }

        protected ItemStack ApplyYieldMultiplier(ItemStack drop, VintageKinematicsConfig cfg, float sourceYieldMultiplier)
        {
            if (drop == null || drop.StackSize <= 0) return drop;
            float mult = cfg?.ResolveSieveYield(drop.Collectible?.Code, sourceYieldMultiplier) ?? sourceYieldMultiplier;
            if (mult <= 0f) return null;
            if (System.Math.Abs(mult - 1f) < 1e-4f) return drop;

            float scaled = drop.StackSize * mult;
            int whole = (int)System.Math.Floor(scaled);
            float frac = scaled - whole;
            if (frac > 0f && Api.World.Rand.NextDouble() < frac) whole++;
            if (whole <= 0) return null;
            drop.StackSize = whole;
            return drop;
        }

        protected override void OnClientDialogUpdated(GuiDialogBlockEntity dialog)
        {
            if (dialog is IWorkProgressDialog progressDialog && Worker != null)
            {
                progressDialog.Update(Worker.CurrentProgress, Worker.WorkPerCycle);
            }
        }
    }

    public interface IWorkProgressDialog
    {
        void Update(float current, float total);
    }
}
