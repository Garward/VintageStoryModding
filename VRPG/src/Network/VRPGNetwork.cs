using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace VRPG.Network;

public static class VRPGNetwork
{
    public const string ChannelName = "vrpg";

    public static IServerNetworkChannel RegisterServer(ICoreServerAPI api)
    {
        return api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<OpenHubRequestPacket>()
            .RegisterMessageType<OpenHubPacket>()
            .RegisterMessageType<TalentTreeRequestPacket>()
            .RegisterMessageType<TalentTreeSnapshotPacket>()
            .RegisterMessageType<TalentProgressPacket>()
            .RegisterMessageType<TalentEditorOpenRequestPacket>()
            .RegisterMessageType<TalentEditorSnapshotPacket>()
            .RegisterMessageType<TalentEditorOpenSavedTreePacket>()
            .RegisterMessageType<TalentEditorSelectTemplatePacket>()
            .RegisterMessageType<TalentEditorAddModifierPacket>()
            .RegisterMessageType<TalentEditorRenameNodePacket>()
            .RegisterMessageType<TalentEditorRenameTreePacket>()
            .RegisterMessageType<TalentEditorSavePacket>()
            .RegisterMessageType<TalentEditorSaveAsPacket>()
            .RegisterMessageType<TalentEditorDeleteSavedTreePacket>()
            .RegisterMessageType<OpenLibraryPacket>()
            .RegisterMessageType<RpgResourcePacket>()
            .RegisterMessageType<StatAllocationRequestPacket>()
            .RegisterMessageType<SkillEquipRequestPacket>()
            .RegisterMessageType<TalentPlanRequestPacket>()
            .RegisterMessageType<UpdateHubOptionsPacket>()
            .RegisterMessageType<EvasiveStepActivatedPacket>()
            .RegisterMessageType<SkillCastRequestPacket>()
            .RegisterMessageType<SkillChannelStatePacket>()
            .RegisterMessageType<SkillLoadoutPacket>()
            .RegisterMessageType<CombatVisualEventPacket>()
            .RegisterMessageType<GroundAreaUpsertPacket>()
            .RegisterMessageType<GroundAreaRemovePacket>();
    }

    public static IClientNetworkChannel RegisterClient(ICoreClientAPI api)
    {
        return api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType<OpenHubRequestPacket>()
            .RegisterMessageType<OpenHubPacket>()
            .RegisterMessageType<TalentTreeRequestPacket>()
            .RegisterMessageType<TalentTreeSnapshotPacket>()
            .RegisterMessageType<TalentProgressPacket>()
            .RegisterMessageType<TalentEditorOpenRequestPacket>()
            .RegisterMessageType<TalentEditorSnapshotPacket>()
            .RegisterMessageType<TalentEditorOpenSavedTreePacket>()
            .RegisterMessageType<TalentEditorSelectTemplatePacket>()
            .RegisterMessageType<TalentEditorAddModifierPacket>()
            .RegisterMessageType<TalentEditorRenameNodePacket>()
            .RegisterMessageType<TalentEditorRenameTreePacket>()
            .RegisterMessageType<TalentEditorSavePacket>()
            .RegisterMessageType<TalentEditorSaveAsPacket>()
            .RegisterMessageType<TalentEditorDeleteSavedTreePacket>()
            .RegisterMessageType<OpenLibraryPacket>()
            .RegisterMessageType<RpgResourcePacket>()
            .RegisterMessageType<StatAllocationRequestPacket>()
            .RegisterMessageType<SkillEquipRequestPacket>()
            .RegisterMessageType<TalentPlanRequestPacket>()
            .RegisterMessageType<UpdateHubOptionsPacket>()
            .RegisterMessageType<EvasiveStepActivatedPacket>()
            .RegisterMessageType<SkillCastRequestPacket>()
            .RegisterMessageType<SkillChannelStatePacket>()
            .RegisterMessageType<SkillLoadoutPacket>()
            .RegisterMessageType<CombatVisualEventPacket>()
            .RegisterMessageType<GroundAreaUpsertPacket>()
            .RegisterMessageType<GroundAreaRemovePacket>();
    }
}
