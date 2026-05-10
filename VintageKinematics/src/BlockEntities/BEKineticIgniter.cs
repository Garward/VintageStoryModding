using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticIgniter : BlockEntity
    {
        private const float PulseMinSec = 3f;
        private const float PulseMaxSec = 5f;
        private const int ServerTickIntervalMs = 250;

        private static readonly AssetLocation CrackleSound = new AssetLocation("sounds/torch-ignite");
        private static readonly Random rand = new Random();

        private float pulseElapsedSec;
        private float pulseTargetSec;
        private bool wasPowered;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            ResetPulseTimer();

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, ServerTickIntervalMs);
            }
        }

        private void OnServerTick(float dt)
        {
            bool powered = IsPowered();
            SetGlowState(powered);

            if (!powered)
            {
                if (wasPowered)
                {
                    ResetPulseTimer();
                    wasPowered = false;
                }
                return;
            }

            if (!wasPowered)
            {
                ResetPulseTimer();
                wasPowered = true;
                return;
            }

            pulseElapsedSec += dt;
            if (pulseElapsedSec < pulseTargetSec) return;

            pulseElapsedSec = 0f;
            pulseTargetSec = (float)(PulseMinSec + rand.NextDouble() * (PulseMaxSec - PulseMinSec));

            BlockPos target = FrontPos();
            if (target == null) return;

            if (TryIgniteAt(target))
            {
                EmitIgnitionFx(target);
            }
        }

        private bool IsPowered()
        {
            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            if (beh == null) return false;
            if (beh.NetworkConflicted) return false;
            return MathF.Abs(beh.ActualRPM) >= KineticNetwork.MinAbsRPM;
        }

        public bool TryRelightHeldTorch(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (byPlayer == null) return false;
            if (!IsPowered()) return false;

            ItemSlot slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
            if (slot == null || slot.Empty) return false;

            ItemStack stack = slot.Itemstack;
            Block held = stack.Block;
            if (held == null) return false;
            if (!(held is BlockTorch torch) || !torch.IsExtinct) return false;

            Block lit = Api.World.GetBlock(held.CodeWithVariant("state", "lit"));
            if (lit == null) return false;

            ItemStack litStack = new ItemStack(lit);
            if (slot.StackSize == 1)
            {
                slot.Itemstack = litStack;
                slot.MarkDirty();
            }
            else
            {
                slot.TakeOut(1);
                slot.MarkDirty();
                if (!byPlayer.InventoryManager.TryGiveItemstack(litStack, slotNotifyEffect: true))
                {
                    Api.World.SpawnItemEntity(litStack, byPlayer.Entity.Pos.XYZ);
                }
            }

            EmitIgnitionFx(Pos);
            return true;
        }

        // Direct per-BE invocation. Vanilla's IIgnitable contract dereferences byEntity.World
        // for several blocks (forge, bloomery, oven), so passing a synthesized null entity
        // crashes. Calling the public BE methods sidesteps the player-only plumbing.
        private bool TryIgniteAt(BlockPos pos)
        {
            Block block = Api.World.BlockAccessor.GetBlock(pos);
            if (block == null) return false;

            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);

            switch (be)
            {
                case BlockEntityFirepit firepit:
                    if (firepit.canIgniteFuel) return false;
                    firepit.canIgniteFuel = true;
                    firepit.extinguishedTotalHours = Api.World.Calendar.TotalHours;
                    firepit.MarkDirty(true);
                    return true;

                // BlockEntityForge.TryIgnite() is `internal void` — not callable from outside
                // vssurvivalmod. Forge ignition is a known v1 limitation; revisit via
                // reflection or a synthesized EntityAgent if needed later.

                case BlockEntityBloomery bloomery:
                    if (!bloomery.CanIgnite()) return false;
                    return bloomery.TryIgnite();

                case BlockEntityCharcoalPit pit:
                    if (pit.Lit) return false;
                    pit.IgniteNow();
                    return true;

                case BlockEntityOven oven:
                    if (!oven.CanIgnite()) return false;
                    return oven.TryIgnite();
            }

            // Torches and any "Ignitable" behavior block: swap state variant directly.
            if (block is BlockTorch torch)
            {
                if (!torch.IsExtinct) return false;
                Block lit = Api.World.GetBlock(block.CodeWithVariant("state", "lit"));
                if (lit == null || lit.BlockId == block.BlockId) return false;
                Api.World.BlockAccessor.SetBlock(lit.BlockId, pos);
                return true;
            }

            BlockBehaviorIgniteable ignBeh = block.GetBehavior<BlockBehaviorIgniteable>();
            if (ignBeh != null && block.LastCodePart() != "lit")
            {
                ignBeh.Ignite(Api.World, pos);
                Block after = Api.World.BlockAccessor.GetBlock(pos);
                return after != null && after.BlockId != block.BlockId;
            }

            return false;
        }

        private BlockPos FrontPos()
        {
            BlockFacing facing = FrontFacing();
            if (facing == null) return null;
            return Pos.AddCopy(facing);
        }

        private BlockFacing FrontFacing()
        {
            string side = Block?.Variant["side"];
            switch (side)
            {
                case "n": return BlockFacing.NORTH;
                case "e": return BlockFacing.EAST;
                case "s": return BlockFacing.SOUTH;
                case "w": return BlockFacing.WEST;
            }
            return null;
        }

        private void EmitIgnitionFx(BlockPos at)
        {
            if (Api == null) return;
            double cx = at.X + 0.5;
            double cy = at.Y + 0.5;
            double cz = at.Z + 0.5;
            Api.World.PlaySoundAt(CrackleSound, cx, cy, cz, null, randomizePitch: true, range: 16, volume: 0.6f);

            BlockFacing facing = FrontFacing();
            if (facing == null) return;

            Vec3d basePos = new Vec3d(
                Pos.X + 0.5 + facing.Normalf.X * 0.5,
                Pos.Y + 0.5,
                Pos.Z + 0.5 + facing.Normalf.Z * 0.5);

            Block fire = Api.World.GetBlock(new AssetLocation("fire"));
            if (fire?.ParticleProperties == null || fire.ParticleProperties.Length == 0) return;

            var props = fire.ParticleProperties[fire.ParticleProperties.Length - 1].Clone();
            props.basePos = basePos;
            props.Quantity.avg = 4f;
            Api.World.SpawnParticles(props, null);
            props.Quantity.avg = 0f;
        }

        private void SetGlowState(bool glow)
        {
            string desired = glow ? "glow" : "cool";
            if (Block?.Variant["state"] == desired) return;

            Block target = Api.World.GetBlock(Block.CodeWithVariant("state", desired));
            if (target == null || target.BlockId == Block.BlockId) return;

            Api.World.BlockAccessor.ExchangeBlock(target.BlockId, Pos);
            MarkDirty(true);
        }

        private void ResetPulseTimer()
        {
            pulseElapsedSec = 0f;
            pulseTargetSec = (float)(PulseMinSec + rand.NextDouble() * (PulseMaxSec - PulseMinSec));
        }
    }
}
