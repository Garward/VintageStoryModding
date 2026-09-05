using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VRPG.Interactions;

/// <summary>Explicit opt-in surface for data-defined VRPG entity interactions.</summary>
public sealed class EntityBehaviorVrpgInteractable : EntityBehavior
{
    public const string BehaviorCode = "vrpgInteractable";
    public const string OfferedAttribute = "vrpgInteractions";
    public const string TargetTagsAttribute = "vrpgInteractionTags";
    public const string RevisionAttribute = "vrpgInteractionRevision";
    private string[] offered = Array.Empty<string>();
    private string[] targetTags = Array.Empty<string>();

    public EntityBehaviorVrpgInteractable(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => BehaviorCode;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        string?[] configured = attributes?["interactions"].AsArray<string>(Array.Empty<string>())
            ?? Array.Empty<string?>();
        offered = configured
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string?[] configuredTags = attributes?["tags"].AsArray<string>(Array.Empty<string>())
            ?? Array.Empty<string?>();
        targetTags = configuredTags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entity.Api.Side == EnumAppSide.Server)
        {
            entity.WatchedAttributes.SetStringArray(OfferedAttribute, offered);
            entity.WatchedAttributes.SetStringArray(TargetTagsAttribute, targetTags);
            entity.WatchedAttributes.MarkPathDirty(OfferedAttribute);
            entity.WatchedAttributes.MarkPathDirty(TargetTagsAttribute);
        }
    }

    public bool Offers(string interactionCode)
    {
        foreach (string code in offered)
        {
            if (string.Equals(code, interactionCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool Activate(IServerPlayer player, string interactionCode, long requestId)
    {
        if (entity.Api.Side != EnumAppSide.Server || !Offers(interactionCode))
        {
            return false;
        }

        long revision = entity.WatchedAttributes.GetLong(RevisionAttribute) + 1;
        entity.WatchedAttributes.SetLong(RevisionAttribute, revision);
        entity.WatchedAttributes.SetString("vrpgLastInteraction", interactionCode);
        entity.WatchedAttributes.SetString("vrpgLastInteractingPlayer", player.PlayerUID);
        entity.WatchedAttributes.MarkPathDirty(RevisionAttribute);
        entity.Notify("vrpgInteractionActivated", new VrpgInteractionActivation(
            player,
            interactionCode,
            requestId,
            revision));
        return true;
    }
}

public readonly record struct VrpgInteractionActivation(
    IServerPlayer Player,
    string InteractionCode,
    long RequestId,
    long Revision);
