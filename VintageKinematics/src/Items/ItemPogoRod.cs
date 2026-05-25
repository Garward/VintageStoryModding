using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Items
{
    public class ItemPogoRod : Item
    {
        private static readonly AssetLocation BoingSound = new AssetLocation("vintagekinematics", "sounds/effect/boing.ogg");
        private const string NextLaunchMsAttribute = "vkPogoNextLaunchMs";
        private const string NextLandingMsAttribute = "vkPogoNextLandingMs";
        private const string SustainUntilMsAttribute = "vkPogoSustainUntilMs";
        private const string SustainSpeedAttribute = "vkPogoSustainSpeed";
        private const string SustainDirXAttribute = "vkPogoSustainDirX";
        private const string SustainDirZAttribute = "vkPogoSustainDirZ";
        private const string KnockbackAttribute = "dmgkb";
        private const string KnockbackXAttribute = "kbdirX";
        private const string KnockbackYAttribute = "kbdirY";
        private const string KnockbackZAttribute = "kbdirZ";
        private const long LaunchCooldownMs = 650;
        private const long LandingCooldownMs = 300;
        private const double LaunchVerticalMotion = 0.22;
        private const double LaunchForwardMotion = 0.055;
        private const double LaunchForwardFloor = 0.12;
        private const double ReboundForwardFloor = 0.11;
        private const double AirSustainSpeed = 0.115;
        private const double BoostedAirSustainSpeed = 0.36;
        private const double BoostedLaunchVerticalMotion = 0.57;
        private const double BoostedLaunchForwardMotion = 0.195;
        private const double BoostedLaunchForwardFloor = 0.3;
        private const double BoostedReboundForwardFloor = 0.27;
        private const float BoostedLaunchChargeSeconds = 0.25f;
        private const float BoostedReboundChargeSeconds = 0.18f;
        private const float BoostSustainChargeMultiplier = 1f;
        private const long SustainDurationMs = 1400;
        private const double MinImpactMotion = -0.16;

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity)
        {
            return "pogo";
        }

        public override string GetHeldTpIdleAnimation(ItemSlot activeHotbarSlot, Entity forEntity, EnumHand hand)
        {
            return hand == EnumHand.Right ? "pogo" : null;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity is not EntityPlayer entityPlayer)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            TryLaunch(entityPlayer);
        }

        private static bool TryLaunch(EntityPlayer entityPlayer)
        {
            IPlayer player = entityPlayer.Player;
            if (player?.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return false;
            if (!IsActive(entityPlayer) || !IsGrounded(entityPlayer) || entityPlayer.Swimming) return false;

            long now = entityPlayer.World.ElapsedMilliseconds;
            if (entityPlayer.Attributes.GetLong(NextLaunchMsAttribute, 0) > now) return false;
            entityPlayer.Attributes.SetLong(NextLaunchMsAttribute, now + LaunchCooldownMs);

            bool boosted = TryUseFlywheelBoost(entityPlayer, BoostedLaunchChargeSeconds);
            ApplyLaunch(entityPlayer, boosted);
            StartSustain(entityPlayer, boosted);
            PlayBoing(entityPlayer, player, boosted ? 1.0f : entityPlayer.World.Side == EnumAppSide.Client ? 0.55f : 0.9f);
            return true;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            return true;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            return false;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-pogorod-launch",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        public static bool IsActive(EntityPlayer player)
        {
            ItemSlot slot = player?.Player?.InventoryManager?.ActiveHotbarSlot;
            return slot?.Itemstack?.Collectible is ItemPogoRod;
        }

        public static void AbsorbFallDamageIfActive(Entity entity, DamageSource damageSource, ref float damage)
        {
            if (damage <= 0 || damageSource?.Source != EnumDamageSource.Fall) return;
            if (entity is not EntityPlayer player || !IsFallGuardActive(player)) return;

            damage = 0;
        }

        public static void ReboundIfActive(EntityPlayer player, double motionY)
        {
            if (player?.World == null) return;
            if (motionY > MinImpactMotion || !IsFallGuardActive(player)) return;

            IPlayer byPlayer = player.Player;
            if (byPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return;

            long now = player.World.ElapsedMilliseconds;
            if (player.Attributes.GetLong(NextLandingMsAttribute, 0) > now) return;
            player.Attributes.SetLong(NextLandingMsAttribute, now + LandingCooldownMs);

            if (player.Controls?.Sneak == true)
            {
                Vec2d settled = MaintainDirectionalMomentum(player, player.Pos.Motion.X, player.Pos.Motion.Z, ReboundForwardFloor);
                SetMotion(player, settled.X, 0.02, settled.Y);
                ClearSustain(player);
                PlayBoing(player, byPlayer, 0.45f);
                return;
            }

            bool boosted = TryUseFlywheelBoost(player, BoostedReboundChargeSeconds);
            double rebound = boosted
                ? GameMath.Clamp(-motionY * 1.23, 0.3, 0.72)
                : GameMath.Clamp(-motionY * 0.62, 0.13, 0.28);
            Vec2d motion = MaintainDirectionalMomentum(player, player.Pos.Motion.X, player.Pos.Motion.Z, boosted ? BoostedReboundForwardFloor : ReboundForwardFloor);
            SetMotion(player, motion.X, rebound, motion.Y);
            StartSustain(player, boosted);
            PlayBoing(player, byPlayer, boosted ? 1.0f : 0.85f);
        }

        public static void SustainAirMomentum(EntityPlayer player, float dt)
        {
            TryLaunchHeld(player);

            if (player?.World == null || player.Swimming || IsGrounded(player) || !IsFallGuardActive(player))
            {
                ClearSustain(player);
                return;
            }

            long now = player.World.ElapsedMilliseconds;
            if (player.Attributes.GetLong(SustainUntilMsAttribute, 0) <= now) return;

            double sustainSpeed = player.Attributes.GetDouble(SustainSpeedAttribute, AirSustainSpeed);
            if (ShouldBoostSustain(player, dt)) sustainSpeed = BoostedAirSustainSpeed;

            Vec2d motion = MaintainStoredDirectionMomentum(player, player.Pos.Motion.X, player.Pos.Motion.Z, sustainSpeed);
            player.Pos.Motion.X = motion.X;
            player.Pos.Motion.Z = motion.Y;
        }

        private static void TryLaunchHeld(EntityPlayer player)
        {
            EntityControls controls = player?.Controls;
            if (controls == null || !controls.RightMouseDown) return;
            TryLaunch(player);
        }

        private static void ApplyLaunch(EntityPlayer player, bool boosted)
        {
            Vec2d forwardVec = FacingVector(BlockFacing.HorizontalFromYaw(player.Pos.Yaw));
            double impulse = boosted ? BoostedLaunchForwardMotion : LaunchForwardMotion;
            double motionX = player.Pos.Motion.X + forwardVec.X * impulse;
            double motionZ = player.Pos.Motion.Z + forwardVec.Y * impulse;
            Vec2d motion = MaintainDirectionalMomentum(player, motionX, motionZ, boosted ? BoostedLaunchForwardFloor : LaunchForwardFloor);
            SetMotion(player, motion.X, boosted ? BoostedLaunchVerticalMotion : LaunchVerticalMotion, motion.Y);
        }

        private static Vec2d MaintainDirectionalMomentum(EntityPlayer player, double motionX, double motionZ, double minForwardSpeed)
        {
            if (!TryGetMoveDirection(player, out double dirX, out double dirZ)) return new Vec2d(motionX, motionZ);

            return MaintainMomentumAlong(motionX, motionZ, dirX, dirZ, minForwardSpeed);
        }

        private static Vec2d MaintainStoredDirectionMomentum(EntityPlayer player, double motionX, double motionZ, double minForwardSpeed)
        {
            double dirX = player.Attributes.GetDouble(SustainDirXAttribute, 0);
            double dirZ = player.Attributes.GetDouble(SustainDirZAttribute, 0);
            double len = System.Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len <= 0.0001) return new Vec2d(motionX, motionZ);

            return MaintainMomentumAlong(motionX, motionZ, dirX / len, dirZ / len, minForwardSpeed);
        }

        private static Vec2d MaintainMomentumAlong(double motionX, double motionZ, double dirX, double dirZ, double minForwardSpeed)
        {
            double projected = motionX * dirX + motionZ * dirZ;
            if (projected >= minForwardSpeed) return new Vec2d(motionX, motionZ);

            double add = minForwardSpeed - projected;
            return new Vec2d(motionX + dirX * add, motionZ + dirZ * add);
        }

        private static bool TryGetMoveDirection(EntityPlayer player, out double dirX, out double dirZ)
        {
            dirX = 0;
            dirZ = 0;

            EntityControls controls = player?.Controls;
            if (controls == null) return false;

            double forward = controls.Forward ? 1 : 0;
            if (controls.Backward) forward -= 1;

            double strafe = controls.Right ? 1 : 0;
            if (controls.Left) strafe -= 1;

            if (forward == 0 && strafe == 0) return false;

            BlockFacing facing = BlockFacing.HorizontalFromYaw(player.Pos.Yaw);
            Vec2d forwardVec = FacingVector(facing);
            Vec2d rightVec = FacingVector(RightOf(facing));

            dirX = forwardVec.X * forward + rightVec.X * strafe;
            dirZ = forwardVec.Y * forward + rightVec.Y * strafe;

            double len = System.Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len <= 0.0001) return false;

            dirX /= len;
            dirZ /= len;
            return true;
        }

        private static Vec2d FacingVector(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return new Vec2d(0, -1);
            if (facing == BlockFacing.EAST) return new Vec2d(1, 0);
            if (facing == BlockFacing.SOUTH) return new Vec2d(0, 1);
            if (facing == BlockFacing.WEST) return new Vec2d(-1, 0);
            return new Vec2d(0, 1);
        }

        private static BlockFacing RightOf(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return BlockFacing.EAST;
            if (facing == BlockFacing.EAST) return BlockFacing.SOUTH;
            if (facing == BlockFacing.SOUTH) return BlockFacing.WEST;
            if (facing == BlockFacing.WEST) return BlockFacing.NORTH;
            return BlockFacing.WEST;
        }

        private static bool TryUseFlywheelBoost(EntityPlayer player, float chargeSeconds)
        {
            if (player?.Controls?.Sprint != true || player.Player == null) return false;
            if (!TryGetMoveDirection(player, out _, out _)) return false;

            if (player.World?.Side == EnumAppSide.Server)
            {
                return ItemBackpackFlywheel.TryConsumeToolPower(player.Player, chargeSeconds, out _);
            }

            return ItemBackpackFlywheel.HasUsableCharge(player.Player);
        }

        private static bool ShouldBoostSustain(EntityPlayer player, float dt)
        {
            if (player?.Controls?.Sprint != true || player.Player == null) return false;
            if (!TryGetMoveDirection(player, out _, out _)) return false;

            float cost = System.MathF.Max(0.02f, dt * BoostSustainChargeMultiplier);
            if (player.World?.Side == EnumAppSide.Server)
            {
                return ItemBackpackFlywheel.TryConsumeToolPower(player.Player, cost, out _);
            }

            return ItemBackpackFlywheel.HasUsableCharge(player.Player);
        }

        private static void StartSustain(EntityPlayer player, bool boosted)
        {
            if (player?.World == null) return;
            if (!TryGetMoveDirection(player, out double dirX, out double dirZ))
            {
                double speedSq = player.Pos.Motion.X * player.Pos.Motion.X + player.Pos.Motion.Z * player.Pos.Motion.Z;
                if (speedSq <= 0.0001) return;
                double speed = System.Math.Sqrt(speedSq);
                dirX = player.Pos.Motion.X / speed;
                dirZ = player.Pos.Motion.Z / speed;
            }

            player.Attributes.SetLong(SustainUntilMsAttribute, player.World.ElapsedMilliseconds + SustainDurationMs);
            player.Attributes.SetDouble(SustainSpeedAttribute, boosted ? BoostedAirSustainSpeed : AirSustainSpeed);
            player.Attributes.SetDouble(SustainDirXAttribute, dirX);
            player.Attributes.SetDouble(SustainDirZAttribute, dirZ);
        }

        private static void ClearSustain(EntityPlayer player)
        {
            player?.Attributes?.RemoveAttribute(SustainUntilMsAttribute);
            player?.Attributes?.RemoveAttribute(SustainSpeedAttribute);
            player?.Attributes?.RemoveAttribute(SustainDirXAttribute);
            player?.Attributes?.RemoveAttribute(SustainDirZAttribute);
        }

        private static void SetMotion(EntityPlayer player, double x, double y, double z)
        {
            player.Pos.Motion.Set(x, y, z);
            player.WatchedAttributes.SetDouble(KnockbackXAttribute, x);
            player.WatchedAttributes.SetDouble(KnockbackYAttribute, y);
            player.WatchedAttributes.SetDouble(KnockbackZAttribute, z);
            player.Attributes.SetInt(KnockbackAttribute, 1);
            player.OnGround = false;
        }

        private static bool IsGrounded(EntityPlayer player)
        {
            return player.OnGround || player.CollidedVertically;
        }

        private static bool IsFallGuardActive(EntityPlayer player)
        {
            return IsActive(player);
        }

        private static void PlayBoing(EntityPlayer player, IPlayer byPlayer, float volume)
        {
            player.World.PlaySoundAt(BoingSound, player, byPlayer, true, 24, volume);
        }
    }

    [HarmonyPatch(typeof(Entity), nameof(Entity.ReceiveDamage))]
    internal static class EntityReceiveDamagePogoPatch
    {
        public static void Prefix(Entity __instance, DamageSource damageSource, ref float damage)
        {
            ItemPogoRod.AbsorbFallDamageIfActive(__instance, damageSource, ref damage);
        }
    }

    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.OnFallToGround))]
    internal static class EntityPlayerFallPogoPatch
    {
        public static void Postfix(EntityPlayer __instance, double motionY)
        {
            ItemPogoRod.ReboundIfActive(__instance, motionY);
        }
    }

    [HarmonyPatch(typeof(EntityPlayer), nameof(EntityPlayer.OnGameTick))]
    internal static class EntityPlayerGameTickPogoPatch
    {
        public static void Postfix(EntityPlayer __instance, float dt)
        {
            ItemPogoRod.SustainAirMomentum(__instance, dt);
        }
    }
}
