using VRPG.Config;
using VRPG.Core;
using VRPG.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VRPG.Modules.Dungeons;

public sealed class DungeonModule : IVrpgModule
{
    private readonly DungeonModuleConfig config;
    private readonly VRPGDataRegistry data;
    private readonly VRPGModSystem owner;
    private bool manifoldRegistered;
    private string status = "Not started.";
    private TemporalRiftService? rifts;

    public DungeonModule(DungeonModuleConfig config, VRPGDataRegistry data, VRPGModSystem owner)
    {
        this.config = config;
        this.data = data;
        this.owner = owner;
    }

    public string Code => "dungeons";

    public void StartServerSide(ICoreServerAPI api)
    {
        var adapter = new ManifoldOptionalAdapter(api, owner, config, data);
        manifoldRegistered = adapter.TryRegisterDungeonDimension(out status);

        if (manifoldRegistered)
        {
            api.Logger.Notification("[VRPG/Dungeons] {0}", status);
        }
        else
        {
            api.Logger.Warning("[VRPG/Dungeons] Disabled cleanly: {0}", status);
        }

        rifts = new TemporalRiftService(api, config, manifoldRegistered);
        rifts.Start();

        api.ChatCommands.Create("vrpgrift")
            .WithDescription("VRPG temporal rift module status.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("status")
                .WithDescription("Show temporal rift integration status.")
                .HandleWith(_ => TextCommandResult.Success(
                    manifoldRegistered
                        ? "VRPG temporal rift module active: " + status
                        : "VRPG temporal rift module disabled: " + status))
            .EndSubCommand();
    }

    public void Dispose()
    {
        rifts?.Stop();
        rifts = null;
    }
}
