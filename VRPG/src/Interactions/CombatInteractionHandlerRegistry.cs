using System;
using System.Collections.Generic;
using VRPG.Data.Definitions;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VRPG.Interactions;

public interface ICombatInteractionHandler
{
    string Code { get; }
    CombatInteractionHandlerResult Execute(in CombatInteractionHandlerContext context);
}

public readonly record struct CombatInteractionHandlerContext(
    IServerPlayer Player,
    Entity Target,
    InteractionDefinition Definition,
    long RequestId);

public readonly record struct CombatInteractionHandlerResult(bool Success, InteractionRejection Rejection)
{
    public static CombatInteractionHandlerResult Accepted() => new(true, InteractionRejection.None);
    public static CombatInteractionHandlerResult Rejected(InteractionRejection rejection) => new(false, rejection);
}

public sealed class CombatInteractionHandlerRegistry
{
    private readonly Dictionary<string, ICombatInteractionHandler> handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public int Count => handlers.Count;

    public void Register(ICombatInteractionHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.Code) || !handler.Code.Contains(':'))
        {
            throw new ArgumentException("Interaction handlers require a namespaced code.", nameof(handler));
        }
        if (!handlers.TryAdd(handler.Code, handler))
        {
            throw new InvalidOperationException($"Interaction handler '{handler.Code}' is already registered.");
        }
    }

    public bool TryGet(string code, out ICombatInteractionHandler? handler)
    {
        return handlers.TryGetValue(code ?? "", out handler);
    }
}
