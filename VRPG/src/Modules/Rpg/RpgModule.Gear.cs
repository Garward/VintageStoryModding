using System;
using System.Linq;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.StatusEffects;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg;

public sealed partial class RpgModule
{
    public bool GearReady => gearRuntime.IsReady;
    /// <summary>Pure, uncommitted starter authoring for the later atomic onboarding transaction.</summary>
    public StarterGearSetFactory? StarterGear => GearReady ? gearRuntime.Starters : null;

    private EquippedGearStats ResolveEquippedStats(IServerPlayer player) => gearRuntime.Owns(player.Entity)
        ? gearRuntime.ResolveStats(player.PlayerUID) : EquippedGearStats.Invalid;

    private void ValidateGearReadiness()
    {
        if (serverApi == null) return;
        GearReadinessResult result = gearRuntime.ValidateAndRecover(code =>
            !string.IsNullOrWhiteSpace(code) && serverApi.World.GetItem(new AssetLocation(code)) != null);
        if (result.Ready)
            serverApi.Logger.Notification("[VRPG/Gear] Ready: {0} validated entries; {1} gear bases. Automatic Evasive Step awaits selected-class authority.",
                result.ValidEntries, data.GearBases.All.Count);
        else
            serverApi.Logger.Error("[VRPG/Gear] Unavailable: {0}", result.Error);
    }

    private void RegisterGearInspection(ICoreServerAPI api)
    {
        if (!config.EnableDataCommands) return;
        api.ChatCommands.Create("vrpggear")
            .WithDescription("Inspect gear readiness and starter sets.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("status")
                .WithDescription("Show whether gear is ready.")
                .HandleWith(_ => GearReady ? TextCommandResult.Success("Gear is ready.")
                    : TextCommandResult.Error("Gear is unavailable. Check the server log."))
            .EndSubCommand()
            .BeginSubCommand("inspectstarters")
                .WithDescription("Inspect starter sets without granting items.")
                .HandleWith(args =>
                {
                    if (StarterGear == null) return TextCommandResult.Error("Gear is unavailable. Check the server log.");
                    int count = 0;
                    foreach (var definition in data.Classes.All.Where(definition => definition.Starter != null))
                    {
                        StarterGearSetResult result = StarterGear.Create(definition.Code);
                        if (!result.Success)
                        {
                            api.Logger.Error("[VRPG/Gear] Starter inspection failed: {0}: {1}", definition.Code, result.Error);
                            return TextCommandResult.Error("A starter set could not be inspected. Check the server log.");
                        }
                        foreach (var item in result.Items)
                        {
                            GearItemStateCodec.TryDecode(item.Prototype.ExactState, out var gear, out _);
                            api.Logger.Notification("[VRPG/Gear] Inspection only: {0} {1} base={2} chassis={3} level={4} power={5} armor={6}",
                                result.ClassCode, item.RoleCode, gear!.BaseCode, gear.VisualChassisAssetCode,
                                gear.RequiredLevel, gear.Resolved.WeaponPower, gear.Resolved.Armor);
                        }
                        count++;
                    }
                    return TextCommandResult.Success($"Inspected {count} starter sets. No items were granted. See the server log for details.");
                })
            .EndSubCommand();
    }

    private void OnPlayerLeave(IServerPlayer player)
    {
        gearRuntime.RemovePlayer(player.PlayerUID);
        talentEditorDrafts.Remove(player.PlayerUID);
        evasiveStep?.RemovePlayer(player.PlayerUID);
        movementEffects?.RemovePlayer(player.PlayerUID);
        skills?.RemovePlayer(player.PlayerUID);
        combatStates?.RemovePlayer(player.PlayerUID);
        interactions?.RemovePlayer(player.PlayerUID);
        failureNotifications?.RemovePlayer(player.PlayerUID);
        groundAreas?.RemovePlayer(player.PlayerUID);
        if (player.Entity != null)
        {
            statusEffects.RemoveEntity(player.Entity.EntityId);
            // The entity may already be absent from World.GetEntityById during disconnect.
            StatusSync.Write(player.Entity.WatchedAttributes, statusEffects.Get(player.Entity.EntityId));
            player.Entity.WatchedAttributes.MarkPathDirty(StatusSync.TreeKey);
        }
    }
}
