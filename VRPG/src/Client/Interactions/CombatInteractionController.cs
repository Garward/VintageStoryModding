using System;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Interactions;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Interactions;

/// <summary>Owns native secondary interaction input for explicitly opted-in entities.</summary>
public sealed class CombatInteractionController : IDisposable
{
    private const long ResultLifetimeMilliseconds = 1400;
    private readonly ICoreClientAPI api;
    private readonly CombatInteractionConfig config;
    private readonly VRPGDataRegistry data;
    private readonly IClientNetworkChannel channel;
    private Candidate? candidate;
    private long nextRequestId = 1;
    private long pendingRequestId;
    private long resultExpiresAt;
    private bool pressLatched;

    public CombatInteractionController(
        ICoreClientAPI api,
        CombatInteractionConfig config,
        VRPGDataRegistry data,
        IClientNetworkChannel channel)
    {
        this.api = api;
        this.config = config;
        this.data = data;
        this.channel = channel;
        api.Input.InWorldAction += OnInWorldAction;
    }

    public InteractionPromptState Prompt { get; private set; } = InteractionPromptState.Hidden;

    public event Action<InteractionPromptState>? PromptChanged;

    public void Tick()
    {
        if (!api.Input.InWorldMouseButton.Right)
        {
            pressLatched = false;
        }

        Candidate? next = ResolveCandidate();
        candidate = next;
        long now = api.ElapsedMilliseconds;
        if (pendingRequestId != 0)
        {
            if (next == null)
            {
                SetPrompt(InteractionPromptState.Hidden);
            }
            return;
        }
        if (resultExpiresAt > now)
        {
            return;
        }
        resultExpiresAt = 0;
        SetPrompt(next == null
            ? InteractionPromptState.Hidden
            : new InteractionPromptState(
                InteractionPromptPhase.Ready,
                Lang.Get(next.Definition.PromptKey),
                next.TargetName,
                ""));
    }

    public void HandleResult(CombatInteractionResultPacket packet)
    {
        if (packet.RequestId != pendingRequestId)
        {
            return;
        }

        pendingRequestId = 0;
        resultExpiresAt = api.ElapsedMilliseconds + ResultLifetimeMilliseconds;
        Candidate? current = candidate;
        string action = current == null ? "" : Lang.Get(current.Definition.PromptKey);
        string target = current?.TargetName ?? "";
        SetPrompt(packet.Success
            ? new InteractionPromptState(
                InteractionPromptPhase.Success,
                action,
                target,
                Lang.Get("vrpg-interaction-success"))
            : new InteractionPromptState(
                InteractionPromptPhase.Rejected,
                action,
                target,
                Lang.Get("vrpg-interaction-" + NormalizeRejection(packet.RejectionCode))));
    }

    private void OnInWorldAction(EnumEntityAction action, bool on, ref EnumHandling handled)
    {
        if (action != EnumEntityAction.InWorldRightMouseDown
            || !on
            || !config.Enabled
            || !VrpgClientHudRuntime.CombatActionBarActive)
        {
            return;
        }

        Candidate? current = ResolveCandidate();
        candidate = current;
        if (current == null)
        {
            Entity? selected = api.World?.Player?.CurrentEntitySelection?.Entity;
            if (selected != null && !AllowsAuditedVanillaFallback(selected))
            {
                handled = EnumHandling.PreventDefault;
            }
            return;
        }

        handled = EnumHandling.PreventDefault;
        if (pressLatched || pendingRequestId != 0)
        {
            return;
        }

        pressLatched = true;
        pendingRequestId = nextRequestId++;
        SetPrompt(new InteractionPromptState(
            InteractionPromptPhase.Pending,
            Lang.Get(current.Definition.PromptKey),
            current.TargetName,
            Lang.Get("vrpg-interaction-pending")));
        channel.SendPacket(new CombatInteractionRequestPacket
        {
            RequestId = pendingRequestId,
            TargetEntityId = current.EntityId,
            InteractionCode = current.Definition.Code,
            SelectionBoxIndex = current.SelectionBoxIndex
        });
    }

    private Candidate? ResolveCandidate()
    {
        if (!config.Enabled || !VrpgClientHudRuntime.CombatActionBarActive)
        {
            return null;
        }

        IPlayer? player = api.World?.Player;
        EntitySelection? selection = player?.CurrentEntitySelection;
        Entity? target = selection?.Entity;
        if (player?.Entity == null || target == null || !target.Alive || !target.IsInteractable)
        {
            return null;
        }

        string[] offered = target.WatchedAttributes.GetStringArray(
            EntityBehaviorVrpgInteractable.OfferedAttribute,
            Array.Empty<string>());
        string[] tags = target.WatchedAttributes.GetStringArray(
            EntityBehaviorVrpgInteractable.TargetTagsAttribute,
            Array.Empty<string>());
        double distance = EyePosition(player.Entity).DistanceTo(EntityCenter(target));
        InteractionDefinition? definition = CombatInteractionCandidateResolver.Resolve(
            data.Interactions.All,
            offered,
            tags,
            ResolveTemporaryContext(player),
            distance,
            hasLineOfSight: true);
        return definition == null
            ? null
            : new Candidate(target.EntityId, selection!.SelectionBoxIndex, target.GetName(), definition);
    }

    private void SetPrompt(InteractionPromptState state)
    {
        if (state == Prompt) return;
        Prompt = state;
        PromptChanged?.Invoke(state);
    }

    private static string ResolveTemporaryContext(IPlayer player)
    {
        return player.WorldData.CurrentGameMode switch
        {
            EnumGameMode.Survival => "adventure",
            EnumGameMode.Creative => "build",
            _ => ""
        };
    }

    private static string NormalizeRejection(string code)
    {
        return string.IsNullOrWhiteSpace(code) ? "unavailable" : code.Trim().ToLowerInvariant();
    }

    private bool AllowsAuditedVanillaFallback(Entity target)
    {
        if (!config.AllowAuditedVanillaFallback)
        {
            return false;
        }

        string targetCode = target.Code?.ToString() ?? "";
        foreach (string allowed in config.AuditedVanillaEntityCodes ?? Array.Empty<string>())
        {
            if (string.Equals(allowed, targetCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static Vec3d EyePosition(EntityPlayer entity) => new(
        entity.Pos.X + entity.LocalEyePos.X,
        entity.Pos.InternalY + entity.LocalEyePos.Y,
        entity.Pos.Z + entity.LocalEyePos.Z);

    private static Vec3d EntityCenter(Entity entity) => new(
        entity.Pos.X,
        entity.Pos.InternalY + Math.Max(0.1f, entity.SelectionBox.YSize) * 0.5,
        entity.Pos.Z);

    public void Dispose()
    {
        api.Input.InWorldAction -= OnInWorldAction;
        PromptChanged = null;
    }

    private sealed record Candidate(
        long EntityId,
        int SelectionBoxIndex,
        string TargetName,
        InteractionDefinition Definition);
}
