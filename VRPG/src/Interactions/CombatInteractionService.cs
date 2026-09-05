using System;
using System.Collections.Generic;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Interactions;

/// <summary>Resolves interaction requests again on the server before dispatching a registered verb.</summary>
public sealed class CombatInteractionService
{
    private const int MaximumCodeLength = 128;
    private readonly ICoreServerAPI api;
    private readonly CombatInteractionConfig config;
    private readonly VRPGDataRegistry data;
    private readonly IServerNetworkChannel channel;
    private readonly CombatInteractionHandlerRegistry handlers;
    private readonly Dictionary<CooldownKey, long> readyAtMilliseconds = new();

    public CombatInteractionService(
        ICoreServerAPI api,
        CombatInteractionConfig config,
        VRPGDataRegistry data,
        IServerNetworkChannel channel,
        CombatInteractionHandlerRegistry handlers)
    {
        this.api = api;
        this.config = config;
        this.data = data;
        this.channel = channel;
        this.handlers = handlers;
    }

    public void Handle(IServerPlayer player, CombatInteractionRequestPacket packet)
    {
        long now = api.World.ElapsedMilliseconds;
        bool requestValid = packet.RequestId > 0
            && packet.TargetEntityId > 0
            && !string.IsNullOrWhiteSpace(packet.InteractionCode)
            && packet.InteractionCode.Length <= MaximumCodeLength;
        Entity? target = requestValid ? api.World.GetEntityById(packet.TargetEntityId) : null;
        InteractionDefinition? definition = requestValid
            ? data.Interactions.Get(packet.InteractionCode)
            : null;
        EntityPlayer? playerEntity = player.Entity;
        string context = ResolveTemporaryContext(player);
        ICombatInteractionHandler? handler = null;
        bool handlerRegistered = definition != null
            && handlers.TryGet(definition.HandlerCode, out handler);
        var cooldownKey = new CooldownKey(player.PlayerUID, packet.TargetEntityId, packet.InteractionCode);

        InteractionRejection rejection = InteractionPolicy.Evaluate(new InteractionPolicyFacts(
            FeatureEnabled: config.Enabled,
            RequestValid: requestValid && definition != null,
            PlayerAvailable: playerEntity?.Alive == true,
            TargetExists: target != null,
            TargetAvailable: target?.Alive == true && target.IsInteractable,
            ContextAllowed: definition != null && AllowsContext(definition, context),
            SameDimension: target != null && playerEntity != null && playerEntity.Pos.Dimension == target.Pos.Dimension,
            WithinRange: target != null && definition != null && playerEntity != null && IsWithinRange(playerEntity, target, definition.Range),
            HasLineOfSight: target != null && definition != null && playerEntity != null && (!definition.RequireLineOfSight || HasLineOfSight(playerEntity, target)),
            InteractionOffered: target != null
                && definition != null
                && Offers(target, packet.InteractionCode)
                && HasRequiredTags(target, definition),
            HasPermission: true,
            CooldownReady: !readyAtMilliseconds.TryGetValue(cooldownKey, out long readyAt) || now >= readyAt,
            HandlerRegistered: handlerRegistered));

        if (rejection != InteractionRejection.None || definition == null || target == null || handler == null)
        {
            SendResult(player, packet, false, rejection);
            return;
        }

        CombatInteractionHandlerResult result;
        try
        {
            result = handler.Execute(new CombatInteractionHandlerContext(
                player,
                target,
                definition,
                packet.RequestId));
        }
        catch (Exception ex)
        {
            api.Logger.Error(
                "[VRPG/Interaction] Handler {0} failed for {1} on entity {2}: {3}",
                definition.HandlerCode,
                player.PlayerUID,
                target.EntityId,
                ex);
            result = CombatInteractionHandlerResult.Rejected(InteractionRejection.Unavailable);
        }

        if (result.Success)
        {
            long cooldownMs = Math.Max(0L, (long)(definition.CooldownSeconds * 1000f));
            readyAtMilliseconds[cooldownKey] = now + cooldownMs;
        }
        SendResult(player, packet, result.Success, result.Rejection);
    }

    private void SendResult(
        IServerPlayer player,
        CombatInteractionRequestPacket request,
        bool success,
        InteractionRejection rejection)
    {
        channel.SendPacket(new CombatInteractionResultPacket
        {
            RequestId = request.RequestId,
            TargetEntityId = request.TargetEntityId,
            InteractionCode = request.InteractionCode,
            Success = success,
            RejectionCode = InteractionRejectionCodes.From(rejection)
        }, player);
    }

    private static bool Offers(Entity target, string interactionCode)
    {
        string[] offered = target.WatchedAttributes.GetStringArray(
            EntityBehaviorVrpgInteractable.OfferedAttribute,
            Array.Empty<string>());
        foreach (string code in offered)
        {
            if (string.Equals(code, interactionCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasRequiredTags(Entity target, InteractionDefinition definition)
    {
        var actual = new HashSet<string>(
            target.WatchedAttributes.GetStringArray(
                EntityBehaviorVrpgInteractable.TargetTagsAttribute,
                Array.Empty<string>()),
            StringComparer.OrdinalIgnoreCase);
        foreach (string required in definition.TargetTags ?? Array.Empty<string>())
        {
            if (!actual.Contains(required)) return false;
        }
        return true;
    }

    private static bool AllowsContext(InteractionDefinition definition, string context)
    {
        foreach (string allowed in definition.AllowedContexts ?? Array.Empty<string>())
        {
            if (string.Equals(allowed, context, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ResolveTemporaryContext(IServerPlayer player)
    {
        return player.WorldData.CurrentGameMode switch
        {
            EnumGameMode.Survival => "adventure",
            EnumGameMode.Creative => "build",
            _ => ""
        };
    }

    private static bool IsWithinRange(EntityPlayer player, Entity target, float range)
    {
        Vec3d from = EyePosition(player);
        Vec3d to = EntityCenter(target);
        return from.SquareDistanceTo(to) <= range * range;
    }

    private bool HasLineOfSight(EntityPlayer player, Entity target)
    {
        BlockSelection? blockSelection = null;
        EntitySelection? entitySelection = null;
        api.World.RayTraceForSelection(
            EyePosition(player),
            EntityCenter(target),
            ref blockSelection,
            ref entitySelection,
            null,
            entity => entity.EntityId == target.EntityId);
        return entitySelection?.Entity?.EntityId == target.EntityId || blockSelection == null;
    }

    private static Vec3d EyePosition(EntityPlayer entity)
    {
        return new Vec3d(
            entity.Pos.X + entity.LocalEyePos.X,
            entity.Pos.InternalY + entity.LocalEyePos.Y,
            entity.Pos.Z + entity.LocalEyePos.Z);
    }

    private static Vec3d EntityCenter(Entity entity)
    {
        return new Vec3d(
            entity.Pos.X,
            entity.Pos.InternalY + Math.Max(0.1f, entity.SelectionBox.YSize) * 0.5,
            entity.Pos.Z);
    }

    private readonly record struct CooldownKey(string PlayerUid, long EntityId, string InteractionCode);
}
