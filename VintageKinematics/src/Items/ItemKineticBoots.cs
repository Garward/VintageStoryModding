using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace VintageKinematics.Items
{
    public class ItemKineticBoots : Item
    {
        private const string PlayerModelLibModId = "playermodellib";
        private const string PlayerModelLibSprintSpeedStat = "sprintSpeed";
        private const string VanillaWalkSpeedStat = "walkspeed";
        private const string SprintBoostKey = "vintagekinematics:kineticboots-sprint";
        private const float SprintBoost = 0.25f;
        private const int TickIntervalMs = 100;

        private static readonly Dictionary<EnumAppSide, SprintSystemState> SystemsBySide = new Dictionary<EnumAppSide, SprintSystemState>();

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine(Lang.Get("vintagekinematics:kineticboots-sprint-bonus"));
        }

        public static void StartSprintSystem(ICoreAPI api)
        {
            if (SystemsBySide.ContainsKey(api.Side)) return;

            SprintSystemState state = new SprintSystemState(api, api.ModLoader.IsModEnabled(PlayerModelLibModId));
            state.TickListenerId = api.Event.RegisterGameTickListener(state.OnTick, TickIntervalMs);
            SystemsBySide[api.Side] = state;
        }

        public static void StopSprintSystem()
        {
            foreach (SprintSystemState state in SystemsBySide.Values)
            {
                state.Stop();
            }

            SystemsBySide.Clear();
        }

        private class SprintSystemState
        {
            private readonly ICoreAPI api;
            private readonly bool playerModelLibEnabled;
            private readonly Dictionary<string, AppliedBoostState> appliedByPlayerUid = new Dictionary<string, AppliedBoostState>();

            public long TickListenerId;

            public SprintSystemState(ICoreAPI api, bool playerModelLibEnabled)
            {
                this.api = api;
                this.playerModelLibEnabled = playerModelLibEnabled;
            }

            public void OnTick(float dt)
            {
                if (api?.World?.AllOnlinePlayers == null) return;

                HashSet<string> onlinePlayerUids = new HashSet<string>();
                foreach (IPlayer player in api.World.AllOnlinePlayers)
                {
                    if (player?.Entity == null) continue;

                    onlinePlayerUids.Add(player.PlayerUID);
                    UpdatePlayer(player, player.Entity);
                }

                PruneOfflinePlayers(onlinePlayerUids);
            }

            public void Stop()
            {
                ClearAllTrackedPlayers();

                if (TickListenerId != 0)
                {
                    api.Event.UnregisterGameTickListener(TickListenerId);
                    TickListenerId = 0;
                }

                appliedByPlayerUid.Clear();
            }

            private void UpdatePlayer(IPlayer player, EntityPlayer entity)
            {
                if (!appliedByPlayerUid.TryGetValue(player.PlayerUID, out AppliedBoostState state))
                {
                    RemoveBoosts(entity);
                }

                bool equipped = IsEquipped(player);
                bool sprinting = IsSprinting(entity);
                bool wantsPmlBoost = playerModelLibEnabled && equipped;
                bool wantsVanillaBoost = !playerModelLibEnabled && equipped && sprinting;

                if (state.PlayerModelLibBoostApplied != wantsPmlBoost)
                {
                    if (wantsPmlBoost)
                    {
                        entity.Stats.Set(PlayerModelLibSprintSpeedStat, SprintBoostKey, SprintBoost, false);
                    }
                    else
                    {
                        entity.Stats.Remove(PlayerModelLibSprintSpeedStat, SprintBoostKey);
                    }

                    state.PlayerModelLibBoostApplied = wantsPmlBoost;
                }

                if (state.VanillaBoostApplied != wantsVanillaBoost)
                {
                    if (wantsVanillaBoost)
                    {
                        entity.Stats.Set(VanillaWalkSpeedStat, SprintBoostKey, SprintBoost, false);
                    }
                    else
                    {
                        entity.Stats.Remove(VanillaWalkSpeedStat, SprintBoostKey);
                    }

                    entity.walkSpeed = entity.Stats.GetBlended(VanillaWalkSpeedStat);
                    state.VanillaBoostApplied = wantsVanillaBoost;
                }

                appliedByPlayerUid[player.PlayerUID] = state;
            }

            private bool IsSprinting(EntityPlayer entity)
            {
                EntityControls controls = api.Side == EnumAppSide.Server ? entity.ServerControls : entity.Controls;
                if (controls == null) return false;

                return controls.Sprint && controls.TriesToMove && !controls.Sneak;
            }

            private void PruneOfflinePlayers(HashSet<string> onlinePlayerUids)
            {
                List<string> toRemove = null;
                foreach (string playerUid in appliedByPlayerUid.Keys)
                {
                    if (onlinePlayerUids.Contains(playerUid)) continue;
                    (toRemove ??= new List<string>()).Add(playerUid);
                }

                if (toRemove == null) return;
                foreach (string playerUid in toRemove)
                {
                    appliedByPlayerUid.Remove(playerUid);
                }
            }

            private void ClearAllTrackedPlayers()
            {
                if (api?.World?.AllOnlinePlayers == null) return;

                foreach (IPlayer player in api.World.AllOnlinePlayers)
                {
                    if (player?.Entity == null) continue;
                    RemoveBoosts(player.Entity);
                }
            }
        }

        private static bool IsEquipped(IPlayer player)
        {
            IInventory characterInventory = player.InventoryManager?.GetOwnInventory("character");
            if (characterInventory == null) return false;

            foreach (ItemSlot slot in characterInventory)
            {
                if (slot?.Itemstack?.Collectible is ItemKineticBoots) return true;
            }

            return false;
        }

        private static void RemoveBoosts(EntityPlayer entity)
        {
            entity.Stats.Remove(PlayerModelLibSprintSpeedStat, SprintBoostKey);
            entity.Stats.Remove(VanillaWalkSpeedStat, SprintBoostKey);
            entity.walkSpeed = entity.Stats.GetBlended(VanillaWalkSpeedStat);
        }

        private struct AppliedBoostState
        {
            public bool PlayerModelLibBoostApplied;
            public bool VanillaBoostApplied;
        }
    }
}
