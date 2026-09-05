namespace VRPG.Interactions;

/// <summary>Dispatches an admitted interaction to an explicitly attached VRPG entity behavior.</summary>
public sealed class EntityBehaviorInteractionHandler : ICombatInteractionHandler
{
    public const string HandlerCode = "vrpg:entity-behavior";

    public string Code => HandlerCode;

    public CombatInteractionHandlerResult Execute(in CombatInteractionHandlerContext context)
    {
        EntityBehaviorVrpgInteractable? behavior =
            context.Target.GetBehavior<EntityBehaviorVrpgInteractable>();
        return behavior?.Activate(
            context.Player,
            context.Definition.Code,
            context.RequestId) == true
                ? CombatInteractionHandlerResult.Accepted()
                : CombatInteractionHandlerResult.Rejected(InteractionRejection.NotOffered);
    }
}
