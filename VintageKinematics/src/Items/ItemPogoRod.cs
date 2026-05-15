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
        private const string KnockbackAttribute = "dmgkb";
        private const string KnockbackXAttribute = "kbdirX";
        private const string KnockbackYAttribute = "kbdirY";
        private const string KnockbackZAttribute = "kbdirZ";
        private const long LaunchCooldownMs = 650;
        private const long LandingCooldownMs = 300;
        private const float LaunchAnimationSeconds = 0.35f;
        private const double LaunchVerticalMotion = 0.22;
        private const double LaunchForwardMotion = 0.055;
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

            IPlayer player = entityPlayer.Player;
            if (player?.WorldData?.CurrentGameMode == EnumGameMode.Spectator) return;
            if (!IsGrounded(entityPlayer) || entityPlayer.Swimming) return;

            long now = api.World.ElapsedMilliseconds;
            if (entityPlayer.Attributes.GetLong(NextLaunchMsAttribute, 0) > now) return;
            entityPlayer.Attributes.SetLong(NextLaunchMsAttribute, now + LaunchCooldownMs);

            ApplyLaunch(entityPlayer);
            PlayBoing(entityPlayer, player, api.Side == EnumAppSide.Client ? 0.55f : 0.9f);
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            return secondsUsed >= LaunchAnimationSeconds;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            return secondsUsed < LaunchAnimationSeconds;
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
                SetMotion(player, player.Pos.Motion.X, 0.02, player.Pos.Motion.Z);
                PlayBoing(player, byPlayer, 0.45f);
                return;
            }

            double rebound = GameMath.Clamp(-motionY * 0.62, 0.13, 0.28);
            SetMotion(player, player.Pos.Motion.X, rebound, player.Pos.Motion.Z);
            PlayBoing(player, byPlayer, 0.85f);
        }

        private static void ApplyLaunch(EntityPlayer player)
        {
            double yaw = player.Pos.Yaw;
            double forwardX = -GameMath.Sin(yaw);
            double forwardZ = GameMath.Cos(yaw);
            double motionX = player.Pos.Motion.X + forwardX * LaunchForwardMotion;
            double motionZ = player.Pos.Motion.Z + forwardZ * LaunchForwardMotion;
            SetMotion(player, motionX, LaunchVerticalMotion, motionZ);
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
            if (!IsActive(player)) return false;

            EntityControls controls = player.Controls;
            return controls != null && (controls.RightMouseDown || controls.HandUse == EnumHandInteract.HeldItemInteract);
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
}
