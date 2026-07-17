using System;
using System.Collections.Generic;
using HarmonyLib;
using VRPG.Config;
using VRPG.Core;
using VRPG.Client;
using VRPG.Client.UI;
using VRPG.Client.Patches;
using VRPG.Client.Visuals;
using VRPG.Data;
using VRPG.Data.Library;
using VRPG.Modules.Dungeons;
using VRPG.Modules.Rpg;
using VRPG.Modules.Rpg.Skills;
using VRPG.Network;
using VRPG.Patches;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG;

public sealed class VRPGModSystem : ModSystem
{
    private readonly List<IVrpgModule> modules = new List<IVrpgModule>();
    private VRPGDataRegistry? dataRegistry;
    private LibraryIndex libraryIndex = new LibraryIndex();
    private VRPGConfig config = new VRPGConfig();
    private IServerNetworkChannel? serverChannel;
    private IClientNetworkChannel? clientChannel;
    private RpgModule? rpgModule;
    private GuiDialogVRPGHub? clientHubDialog;
    private GuiDialogVRPGLibrary? clientLibraryDialog;
    private GuiDialogVRPGTalentEditor? clientTalentEditorDialog;
    private HudElementVRPGResources? clientResourceHud;
    private HudElementVRPGEntityHealthBars? clientEntityHud;
    private HudElementVRPGSkillHotbar? clientSkillHotbar;
    private HudElementVRPGPlayerStatus? clientPlayerStatusHud;
    private VisualDirector? visualDirector;
    private HudElementVRPGCombatText? clientCombatText;
    private HudElementVRPGWindowPulse? clientWindowPulse;
    private HudElementVRPGChannelBar? clientChannelBar;
    private GroundTelegraphRenderer? telegraphRenderer;
    private AuraEmitterSystem? auraEmitters;
    private Harmony? clientHarmony;
    private Harmony? serverHarmony;
    private TalentTreeSnapshotPacket clientTalentTree = new TalentTreeSnapshotPacket();
    private readonly HashSet<int> heldSkillSlots = new HashSet<int>();
    private ICoreClientAPI? clientApi;
    private long skillInputTickListenerId;

    public VRPGDataRegistry DataRegistry => dataRegistry ?? throw new InvalidOperationException("VRPG data is not loaded yet.");
    public LibraryIndex LibraryIndex => libraryIndex;
    public VRPGConfig Config => config;

    public override double ExecuteOrder() => 0.55;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        new AssetCategory("vrpg", true, EnumAppSide.Universal);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterItemClass("vrpg.riftchart", typeof(ItemRiftChart));
        api.RegisterEntity("EntityVrpgSkillProjectile", typeof(EntityVrpgSkillProjectile));
        config = VRPGConfigStore.Load(api);
        dataRegistry = new VRPGDataRegistry();
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        dataRegistry ??= new VRPGDataRegistry();
        dataRegistry.Load(api);
        libraryIndex = LibraryIndex.Build(dataRegistry);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        serverChannel = VRPGNetwork.RegisterServer(api)
            .SetMessageHandler<OpenHubRequestPacket>((player, packet) => HandleOpenHubRequest(api, player, packet))
            .SetMessageHandler<TalentTreeRequestPacket>((player, packet) => SendTalentTreeIfChanged(player, packet.KnownTreeCode, packet.KnownContentHash))
            .SetMessageHandler<TalentEditorOpenRequestPacket>((player, _) => HandleTalentEditorOpen(player))
            .SetMessageHandler<TalentEditorOpenSavedTreePacket>((player, packet) => HandleTalentEditorOpenSavedTree(player, packet))
            .SetMessageHandler<TalentEditorSelectTemplatePacket>((player, packet) => HandleTalentEditorTemplate(player, packet))
            .SetMessageHandler<TalentEditorAddModifierPacket>((player, packet) => HandleTalentEditorModifier(player, packet))
            .SetMessageHandler<TalentEditorRenameNodePacket>((player, packet) => HandleTalentEditorRename(player, packet))
            .SetMessageHandler<TalentEditorRenameTreePacket>((player, packet) => HandleTalentEditorTreeRename(player, packet))
            .SetMessageHandler<TalentEditorSavePacket>((player, _) => HandleTalentEditorSave(player))
            .SetMessageHandler<TalentEditorSaveAsPacket>((player, packet) => HandleTalentEditorSaveAs(player, packet))
            .SetMessageHandler<TalentEditorDeleteSavedTreePacket>((player, packet) => HandleTalentEditorDeleteSavedTree(player, packet))
            .SetMessageHandler<StatAllocationRequestPacket>((player, packet) => HandleStatAllocationRequest(api, player, packet))
            .SetMessageHandler<SkillEquipRequestPacket>((player, packet) => HandleSkillEquipRequest(api, player, packet))
            .SetMessageHandler<TalentPlanRequestPacket>((player, packet) => HandleTalentPlanRequest(api, player, packet))
            .SetMessageHandler<UpdateHubOptionsPacket>((player, packet) => HandleHubOptionsRequest(api, player, packet))
            .SetMessageHandler<SkillCastRequestPacket>((player, packet) => HandleSkillCastRequest(player, packet));
        modules.Clear();
        rpgModule = null;

        if (config.Modules.Rpg.Enabled)
        {
            serverHarmony = new Harmony("vrpg.server");
            EntityDamageScalingPatch.Patch(serverHarmony);
            rpgModule = new RpgModule(config.Modules.Rpg, DataRegistry, serverChannel!);
            AddAndStart(api, rpgModule);
        }

        if (config.Modules.Dungeons.Enabled)
        {
            AddAndStart(api, new DungeonModule(config.Modules.Dungeons, DataRegistry, this));
        }

        RegisterCommands(api);
        api.Logger.Notification("[VRPG] Loaded. RPG={0}, Dungeons={1}, Data={2}.",
            config.Modules.Rpg.Enabled,
            config.Modules.Dungeons.Enabled,
            DataRegistry.Summary());
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        clientApi = api;

        clientChannel = VRPGNetwork.RegisterClient(api);
        clientChannel
            .SetMessageHandler<OpenHubPacket>(packet => OpenHubDialog(api, packet))
            .SetMessageHandler<TalentTreeSnapshotPacket>(packet => ReceiveTalentTreeSnapshot(api, packet))
            .SetMessageHandler<TalentProgressPacket>(packet => clientHubDialog?.UpdateTalentProgress(packet.Talents))
            .SetMessageHandler<TalentEditorSnapshotPacket>(packet => OpenTalentEditorDialog(api, packet))
            .SetMessageHandler<OpenLibraryPacket>(packet => OpenLibraryDialog(api, packet))
            .SetMessageHandler<RpgResourcePacket>(packet => OpenResourceHudPacket(api, packet))
            .SetMessageHandler<SkillLoadoutPacket>(packet => OpenSkillHotbarPacket(api, packet))
            .SetMessageHandler<EvasiveStepActivatedPacket>(packet => ApplyEvasiveStepMotion(api, packet))
            .SetMessageHandler<SkillChannelStatePacket>(packet => clientChannelBar?.SetState(packet))
            .SetMessageHandler<CombatVisualEventPacket>(packet => visualDirector?.HandleEvent(packet))
            .SetMessageHandler<GroundAreaUpsertPacket>(packet => visualDirector?.HandleAreaUpsert(packet))
            .SetMessageHandler<GroundAreaRemovePacket>(packet => visualDirector?.HandleAreaRemove(packet));

        api.Input.RegisterHotKey(VrpgHotkeys.Hub, "VRPG — Open Hub", GlKeys.V, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler(VrpgHotkeys.Hub, OnHubHotkey);
        RegisterSkillHotkeys(api);
        skillInputTickListenerId = api.Event.RegisterGameTickListener(_ => PollReleasedSkillHotkeys(api), 25);

        if (config.Modules.Rpg.Enabled)
        {
            visualDirector = new VisualDirector(api, DataRegistry, config.Modules.Rpg.CombatVisuals);
            telegraphRenderer = new GroundTelegraphRenderer(api, visualDirector.Areas, visualDirector.Styles, config.Modules.Rpg.CombatVisuals);
            api.Event.RegisterRenderer(telegraphRenderer, EnumRenderStage.OIT, "vrpg-telegraphs");
            auraEmitters = new AuraEmitterSystem(api, DataRegistry, config.Modules.Rpg.CombatVisuals, visualDirector.Budget);
            auraEmitters.Start();
            clientPlayerStatusHud = new HudElementVRPGPlayerStatus(api, DataRegistry);
        }

        if (visualDirector != null)
        {
            clientCombatText = new HudElementVRPGCombatText(api, visualDirector);
        }

        if (visualDirector != null)
        {
            clientWindowPulse = new HudElementVRPGWindowPulse(api);
            visualDirector.WindowPulse = clientWindowPulse;
            clientChannelBar = new HudElementVRPGChannelBar(api);
        }

        if (config.Modules.Rpg.Enabled && config.Modules.Rpg.Hud.Enabled)
        {
            clientResourceHud = new HudElementVRPGResources(api, config.Modules.Rpg.Hud);
            clientResourceHud.TryOpen();
        }

        if (config.Modules.Rpg.Enabled && config.Modules.Rpg.EntityHud.Enabled)
        {
            clientEntityHud = new HudElementVRPGEntityHealthBars(api, config.Modules.Rpg.EntityHud, DataRegistry);
            clientEntityHud.TryOpen();
        }

        if (config.Modules.Rpg.Enabled && config.Modules.Rpg.SkillHotbar.Enabled)
        {
            clientSkillHotbar = new HudElementVRPGSkillHotbar(api, config.Modules.Rpg.SkillHotbar, () => PersistClientConfig(api));
            clientSkillHotbar.TryOpen();
        }

        VrpgClientHudRuntime.HideVanillaStatbar =
            config.Modules.Rpg.Enabled && config.Modules.Rpg.Hud.Enabled && config.Modules.Rpg.Hud.HideVanillaStatbar;

        if (config.Modules.Rpg.Enabled && config.Modules.Rpg.Hud.HideVanillaStatbar)
        {
            clientHarmony = new Harmony("vrpg.client");
            HudStatbarPatch.Patch(clientHarmony);
        }

        api.ChatCommands.Create("vrpglib")
            .WithDescription("Open the local VRPG library UI smoke test.")
            .HandleWith(_ =>
            {
                EnsureDataLoaded(api);
                OpenLibraryPacket packet = BuildLibraryPacket();
                OpenLibraryDialog(api, packet);
                return TextCommandResult.Success("Opened local VRPG library UI with " + packet.Entries.Length + " entries.");
            });
    }

    public override void Dispose()
    {
        for (int i = modules.Count - 1; i >= 0; i--)
        {
            try
            {
                modules[i].Dispose();
            }
            catch
            {
            }
        }

        modules.Clear();
        clientHubDialog?.TryClose();
        clientHubDialog?.Dispose();
        clientHubDialog = null;
        clientLibraryDialog?.TryClose();
        clientLibraryDialog?.Dispose();
        clientLibraryDialog = null;
        clientTalentEditorDialog?.TryClose();
        clientTalentEditorDialog?.Dispose();
        clientTalentEditorDialog = null;
        clientResourceHud?.Dispose();
        clientResourceHud = null;
        clientEntityHud?.Dispose();
        clientEntityHud = null;
        clientSkillHotbar?.Dispose();
        clientSkillHotbar = null;
        clientPlayerStatusHud?.Dispose();
        clientPlayerStatusHud = null;
        clientCombatText?.Dispose();
        clientCombatText = null;
        clientWindowPulse?.Dispose();
        clientWindowPulse = null;
        clientChannelBar?.Dispose();
        clientChannelBar = null;
        if (telegraphRenderer != null)
        {
            clientApi?.Event.UnregisterRenderer(telegraphRenderer, EnumRenderStage.OIT);
            telegraphRenderer.Dispose();
            telegraphRenderer = null;
        }
        visualDirector?.Dispose();
        visualDirector = null;
        auraEmitters?.Dispose();
        auraEmitters = null;
        if (clientApi != null && skillInputTickListenerId != 0)
        {
            clientApi.Event.UnregisterGameTickListener(skillInputTickListenerId);
        }
        skillInputTickListenerId = 0;
        heldSkillSlots.Clear();
        clientApi = null;
        VrpgClientHudRuntime.HideVanillaStatbar = false;
        serverHarmony?.UnpatchAll("vrpg.server");
        serverHarmony = null;
        clientHarmony?.UnpatchAll("vrpg.client");
        clientHarmony = null;
        rpgModule = null;
        base.Dispose();
    }

    private void AddAndStart(ICoreServerAPI api, IVrpgModule module)
    {
        modules.Add(module);
        module.StartServerSide(api);
    }

    private void RegisterCommands(ICoreServerAPI api)
    {
        api.ChatCommands.GetOrCreate("vrpg")
            .WithDescription("VRPG gameplay, authoring, and data commands.")
            .WithAlias("vrpgrpg")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("hub")
                .WithDescription("Open the VRPG Hub.")
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    if (args.Caller.Player is not IServerPlayer player)
                        return TextCommandResult.Error("Only a player can open the VRPG Hub.");
                    HandleOpenHubRequest(api, player, new OpenHubRequestPacket());
                    return TextCommandResult.Success("Opening the VRPG Hub.");
                })
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("Show loaded VRPG modules and data counts.")
                .HandleWith(_ =>
                {
                    EnsureDataLoaded(api);
                    return TextCommandResult.Success(BuildStatus());
                })
            .EndSubCommand()
            .BeginSubCommand("reload")
                .WithDescription("Reload VRPG config and JSON data assets.")
                .RequiresPrivilege(Privilege.controlserver)
                .HandleWith(_ =>
                {
                    config = VRPGConfigStore.Load(api);
                    DataRegistry.Load(api);
                    libraryIndex = LibraryIndex.Build(DataRegistry);
                    bool talentTreeChanged = rpgModule?.SynchronizeSavedTalentTree() == true;
                    return TextCommandResult.Success("VRPG reloaded: " + DataRegistry.Summary()
                        + (talentTreeChanged ? ". Talent tree saved and synchronized." : ". Talent tree unchanged."));
                })
            .EndSubCommand()
            .BeginSubCommand("library")
                .WithDescription("Search or inspect the generated VRPG library.")
                .BeginSubCommand("open")
                    .WithDescription("Open the generated VRPG library UI.")
                    .RequiresPlayer()
                    .HandleWith(args =>
                    {
                        EnsureDataLoaded(api);
                        if (args.Caller.Player is not IServerPlayer player)
                        {
                            return TextCommandResult.Error("Only a player can open the VRPG library UI.");
                        }

                        OpenLibraryPacket packet = BuildLibraryPacket();
                        serverChannel?.SendPacket(packet, player);
                        api.Logger.Notification("[VRPG] Sent library UI packet with {0} entries to {1}.", packet.Entries.Length, player.PlayerName);
                        return TextCommandResult.Success("Opening VRPG library UI with " + packet.Entries.Length + " entries. If nothing appears, install the VRPG mod on the client too.");
                    })
                .EndSubCommand()
                .BeginSubCommand("list")
                    .WithDescription("List generated library entries.")
                    .HandleWith(_ =>
                    {
                        EnsureDataLoaded(api);
                        return TextCommandResult.Success(libraryIndex.FormatList("Library", libraryIndex.Entries));
                    })
                .EndSubCommand()
                .BeginSubCommand("search")
                    .WithDescription("Search generated library entries.")
                    .WithArgs(api.ChatCommands.Parsers.Word("query"))
                    .HandleWith(args =>
                    {
                        EnsureDataLoaded(api);
                        return TextCommandResult.Success(libraryIndex.FormatList("Library search", libraryIndex.Search((string)args[0])));
                    })
                .EndSubCommand()
                .BeginSubCommand("categories")
                    .WithDescription("List generated library categories.")
                    .HandleWith(_ =>
                    {
                        EnsureDataLoaded(api);
                        return TextCommandResult.Success(libraryIndex.FormatCategories());
                    })
                .EndSubCommand()
            .EndSubCommand()
            .BeginSubCommand("talents")
                .WithDescription("List VRPG talent nodes.")
                .HandleWith(_ => TextCommandResult.Success(DataRegistry.Talents.FormatList("Talents")))
            .EndSubCommand()
            .BeginSubCommand("talenteditor")
                .WithDescription("Open the server-authoritative VRPG talent-tree editor.")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    if (args.Caller.Player is not IServerPlayer player)
                        return TextCommandResult.Error("Only a player can open the talent editor.");
                    HandleTalentEditorOpen(player);
                    return TextCommandResult.Success("Opening the VRPG talent-tree editor.");
                })
            .EndSubCommand()
            .BeginSubCommand("stats")
                .WithDescription("List VRPG stat definitions.")
                .HandleWith(_ => TextCommandResult.Success(DataRegistry.Stats.FormatList("Stats")))
            .EndSubCommand()
            .BeginSubCommand("statfamilies")
                .WithDescription("List VRPG stat-family definition files.")
                .HandleWith(_ => TextCommandResult.Success(DataRegistry.StatFamilies.FormatList("Stat families")))
            .EndSubCommand()
            .BeginSubCommand("vfx")
                .WithDescription("Fire synthetic combat visuals for testing.")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("event")
                    .WithDescription("Fire a combat visual event at the aimed point (kinds: impact, burst, ray, damage, heal, shield, break, counter, consume, windowopen, mark).")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Word("kind"), api.ChatCommands.Parsers.OptionalWord("style"))
                    .HandleWith(args => HandleVfxEvent(api, args))
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription("Apply a status effect to the looked-at entity (or yourself).")
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("code"),
                        api.ChatCommands.Parsers.OptionalInt("stacks", 1),
                        api.ChatCommands.Parsers.OptionalFloat("magnitude", 0f),
                        api.ChatCommands.Parsers.OptionalFloat("seconds", 8f))
                    .HandleWith(args => HandleVfxStatus(args))
                .EndSubCommand()
                .BeginSubCommand("area")
                    .WithDescription("Place a ground area at the aimed block (states: armed, triggered, active, expiring).")
                    .RequiresPlayer()
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("style"),
                        api.ChatCommands.Parsers.OptionalWord("state"),
                        api.ChatCommands.Parsers.OptionalFloat("radius", 3f),
                        api.ChatCommands.Parsers.OptionalFloat("seconds", 20f))
                    .HandleWith(args => HandleVfxArea(args))
                .EndSubCommand()
                .BeginSubCommand("empower")
                    .WithDescription("Toggle the empowered glow on a loadout slot.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.Int("slot"), api.ChatCommands.Parsers.Word("onoff"))
                    .HandleWith(args => HandleVfxEmpower(args))
                .EndSubCommand()
                .BeginSubCommand("stress")
                    .WithDescription("Spray synthetic damage events around you to verify budgets.")
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.OptionalInt("eventsPerSecond", 60), api.ChatCommands.Parsers.OptionalInt("seconds", 10))
                    .HandleWith(args => HandleVfxStress(api, args))
                .EndSubCommand()
            .EndSubCommand();
    }

    private TextCommandResult HandleVfxEvent(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.VisualBroadcaster == null)
        {
            return TextCommandResult.Error("The VRPG visual runtime is not available.");
        }

        if (!Enum.TryParse((string)args[0], true, out CombatVisualKind kind))
        {
            return TextCommandResult.Error("Unknown visual kind: " + args[0]);
        }

        Vintagestory.API.MathTools.Vec3d position =
            player.CurrentBlockSelection?.FullPosition
            ?? player.Entity.Pos.XYZ.AheadCopy(4, 0, player.Entity.Pos.Yaw);
        long targetId = player.CurrentEntitySelection?.Entity?.EntityId ?? player.Entity.EntityId;

        rpgModule.VisualBroadcaster.Send(new CombatVisualEventPacket
        {
            Kind = (byte)kind,
            StyleCode = (args.Parsers[1].IsMissing ? "" : (string)args[1]) ?? "",
            SourceEntityId = player.Entity.EntityId,
            TargetEntityId = targetId,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            Radius = 2.5f,
            Magnitude = 17f,
            FallbackColorRgba = unchecked((int)0xccf2ede4)
        });
        return TextCommandResult.Success("Fired " + kind + ".");
    }

    private TextCommandResult HandleVfxStatus(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule == null)
        {
            return TextCommandResult.Error("The VRPG rpg runtime is not available.");
        }

        long entityId = player.CurrentEntitySelection?.Entity?.EntityId ?? player.Entity.EntityId;
        bool applied = rpgModule.ApplyStatus(entityId, (string)args[0], (int)args[1], (float)args[2], (float)args[3]);
        return applied
            ? TextCommandResult.Success("Applied " + args[0] + " to entity " + entityId + ".")
            : TextCommandResult.Error("Unknown status effect: " + args[0]);
    }

    private TextCommandResult HandleVfxArea(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.GroundAreas == null)
        {
            return TextCommandResult.Error("The VRPG area runtime is not available.");
        }

        Vintagestory.API.MathTools.Vec3d position =
            player.CurrentBlockSelection?.FullPosition ?? player.Entity.Pos.XYZ;
        string stateWord = args.Parsers[1].IsMissing ? "active" : (string)args[1];
        if (!Enum.TryParse(stateWord, true, out GroundAreaState state))
        {
            return TextCommandResult.Error("Unknown area state: " + stateWord);
        }

        long id = rpgModule.GroundAreas.Place(
            player.PlayerUID, (string)args[0], GroundAreaShape.Disc, position,
            (float)args[2], state, (float)args[3]);
        return TextCommandResult.Success("Placed area " + id + " (" + state + ").");
    }

    private TextCommandResult HandleVfxEmpower(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule == null)
        {
            return TextCommandResult.Error("The VRPG rpg runtime is not available.");
        }

        int slot = (int)args[0] - 1;
        SkillLoadoutPacket loadout = rpgModule.BuildSkillLoadout(player);
        if (slot < 0 || slot >= loadout.Slots.Length || string.IsNullOrEmpty(loadout.Slots[slot].Code))
        {
            return TextCommandResult.Error("Slot " + args[0] + " has no equipped skill.");
        }

        bool on = string.Equals((string)args[1], "on", StringComparison.OrdinalIgnoreCase);
        rpgModule.SetSkillEmpowered(player, loadout.Slots[slot].Code, on);
        return TextCommandResult.Success((on ? "Empowered " : "Cleared ") + loadout.Slots[slot].Name + ".");
    }

    private TextCommandResult HandleVfxStress(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player || rpgModule?.VisualBroadcaster == null)
        {
            return TextCommandResult.Error("The VRPG visual runtime is not available.");
        }

        int eventsPerSecond = Math.Clamp((int)args[0], 1, 500);
        int seconds = Math.Clamp((int)args[1], 1, 60);
        var random = new Random();
        long endAtMs = api.World.ElapsedMilliseconds + seconds * 1000L;
        long listenerId = 0;
        listenerId = api.Event.RegisterGameTickListener(_ =>
        {
            if (api.World.ElapsedMilliseconds >= endAtMs)
            {
                api.Event.UnregisterGameTickListener(listenerId);
                return;
            }

            var pos = player.Entity.Pos.XYZ;
            for (int i = 0; i < Math.Max(1, eventsPerSecond / 4); i++)
            {
                rpgModule!.VisualBroadcaster!.Send(new CombatVisualEventPacket
                {
                    Kind = (byte)(random.NextDouble() < 0.7 ? CombatVisualKind.Damage : CombatVisualKind.Burst),
                    StyleCode = "",
                    FallbackColorRgba = unchecked((int)0xccf2ede4),
                    DamageType = (byte)random.Next(0, 6),
                    Magnitude = random.Next(1, 40),
                    X = pos.X + random.NextDouble() * 12 - 6,
                    Y = pos.Y + 1,
                    Z = pos.Z + random.NextDouble() * 12 - 6,
                    Radius = 1.5f
                });
            }
        }, 250);
        return TextCommandResult.Success("Stressing " + eventsPerSecond + " events/s for " + seconds + "s.");
    }

    private string BuildStatus()
    {
        return "VRPG modules: " + string.Join(", ", modules.ConvertAll(module => module.Code))
            + ". Data: " + DataRegistry.Summary()
            + ". Library: " + libraryIndex.Count + " generated entries";
    }

    private void EnsureDataLoaded(ICoreAPI api)
    {
        if (libraryIndex.Count > 0)
        {
            return;
        }

        DataRegistry.Load(api);
        libraryIndex = LibraryIndex.Build(DataRegistry);
        api.Logger.Notification("[VRPG] Data check: {0}. Library={1}.", DataRegistry.Summary(), libraryIndex.Count);
    }

    private OpenLibraryPacket BuildLibraryPacket()
    {
        var packet = new OpenLibraryPacket
        {
            Entries = new LibraryEntryPacket[libraryIndex.Entries.Count]
        };

        for (int i = 0; i < libraryIndex.Entries.Count; i++)
        {
            LibraryEntry entry = libraryIndex.Entries[i];
            packet.Entries[i] = new LibraryEntryPacket
            {
                Code = entry.Code,
                Name = entry.Name,
                Category = entry.Category,
                Summary = entry.Summary,
                Source = entry.Source,
                Tags = entry.Tags,
                Fields = Array.ConvertAll(entry.Fields, field => new LibraryFieldPacket
                {
                    Label = field.Label,
                    Value = field.Value
                })
            };
        }

        return packet;
    }

    private void SendHubPacket(ICoreServerAPI api, IServerPlayer player)
    {
        SendHubPacket(api, player, "", false);
    }

    private void HandleOpenHubRequest(ICoreServerAPI api, IServerPlayer player, OpenHubRequestPacket request)
    {
        SendTalentTreeIfChanged(player, request.KnownTalentTreeCode, request.KnownTalentTreeHash);
        SendHubPacket(api, player);
    }

    private void SendTalentTreeIfChanged(IServerPlayer player, string knownCode, string knownHash)
    {
        TalentTreeSnapshotPacket snapshot = rpgModule?.BuildTalentTreeSnapshot() ?? new TalentTreeSnapshotPacket
        {
            TreeCode = "vrpg:default"
        };
        if (string.Equals(snapshot.TreeCode, knownCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.ContentHash, knownHash, StringComparison.Ordinal))
        {
            return;
        }

        serverChannel?.SendPacket(snapshot, player);
    }

    private bool CanEditTalentTree(IServerPlayer player)
    {
        return player.HasPrivilege(Privilege.controlserver) && rpgModule != null;
    }

    private void HandleTalentEditorOpen(IServerPlayer player)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.OpenTalentEditor(player), player);
    }

    private void HandleTalentEditorTemplate(IServerPlayer player, TalentEditorSelectTemplatePacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.SelectTalentEditorTemplate(player, packet.TemplateCode), player);
    }

    private void HandleTalentEditorOpenSavedTree(IServerPlayer player, TalentEditorOpenSavedTreePacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.OpenSavedTalentTree(player, packet.TreeCode), player);
    }

    private void HandleTalentEditorModifier(IServerPlayer player, TalentEditorAddModifierPacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.AddTalentEditorModifier(player, packet), player);
    }

    private void HandleTalentEditorRename(IServerPlayer player, TalentEditorRenameNodePacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.RenameTalentEditorNode(player, packet), player);
    }

    private void HandleTalentEditorTreeRename(IServerPlayer player, TalentEditorRenameTreePacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.RenameTalentEditorTree(player, packet), player);
    }

    private void HandleTalentEditorSave(IServerPlayer player)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.SaveTalentEditor(player), player);
    }

    private void HandleTalentEditorSaveAs(IServerPlayer player, TalentEditorSaveAsPacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.SaveTalentEditorAs(player, packet.Name), player);
    }

    private void HandleTalentEditorDeleteSavedTree(IServerPlayer player, TalentEditorDeleteSavedTreePacket packet)
    {
        if (CanEditTalentTree(player)) serverChannel?.SendPacket(rpgModule!.DeleteSavedTalentTree(player, packet.TreeCode), player);
    }

    private void SendHubPacket(ICoreServerAPI api, IServerPlayer player, string feedbackMessage, bool feedbackError)
    {
        EnsureDataLoaded(api);
        OpenHubPacket packet = BuildHubPacket(player);
        packet.FeedbackMessage = feedbackMessage ?? "";
        packet.FeedbackError = feedbackError;
        serverChannel?.SendPacket(packet, player);
        api.Logger.Notification("[VRPG] Sent hub UI packet with {0} entries to {1}.", packet.Entries.Length, player.PlayerName);
    }

    private bool OnHubHotkey(KeyCombination keyCombination)
    {
        if (clientHubDialog?.IsOpened() == true)
        {
            clientHubDialog.TryClose();
            return true;
        }

        if (clientLibraryDialog?.IsOpened() == true)
        {
            clientLibraryDialog.TryClose();
            return true;
        }

        clientChannel?.SendPacket(new OpenHubRequestPacket
        {
            KnownTalentTreeCode = clientTalentTree.TreeCode,
            KnownTalentTreeHash = clientTalentTree.ContentHash
        });
        return true;
    }

    public bool HandleSkillProjectileImpact(EntityVrpgSkillProjectile projectile)
    {
        return rpgModule?.HandleProjectileImpact(projectile) == true;
    }

    private void RegisterSkillHotkeys(ICoreClientAPI api)
    {
        GlKeys[] keys =
        {
            GlKeys.Number1, GlKeys.Number2, GlKeys.Number3, GlKeys.Number4,
            GlKeys.Number5, GlKeys.Number6, GlKeys.Number7, GlKeys.Number8
        };
        for (int i = 0; i < VrpgHotkeys.SkillCodes.Length; i++)
        {
            int slot = i;
            api.Input.RegisterHotKey(
                VrpgHotkeys.SkillCodes[i],
                "VRPG — Skill Slot " + (i + 1),
                keys[i],
                HotkeyType.CharacterControls,
                altPressed: true);
            HotKey hotKey = api.Input.GetHotKeyByCode(VrpgHotkeys.SkillCodes[i]);
            hotKey.TriggerOnUpAlso = true;
            api.Input.SetHotKeyHandler(
                VrpgHotkeys.SkillCodes[i],
                keyCombination => OnSkillHotkey(slot, pressed: !keyCombination.OnKeyUp));
        }
    }

    private bool OnSkillHotkey(int slot, bool pressed)
    {
        if (!config.Modules.Rpg.Enabled)
        {
            return false;
        }

        bool stateChanged = pressed
            ? heldSkillSlots.Add(slot)
            : heldSkillSlots.Remove(slot);
        if (!stateChanged)
        {
            return true;
        }

        clientChannel?.SendPacket(new SkillCastRequestPacket { Slot = slot, Pressed = pressed });
        return true;
    }

    private void PollReleasedSkillHotkeys(ICoreClientAPI api)
    {
        if (heldSkillSlots.Count == 0)
        {
            return;
        }

        var released = new List<int>();
        foreach (int slot in heldSkillSlots)
        {
            if (slot < 0
                || slot >= VrpgHotkeys.SkillCodes.Length
                || !api.Input.IsHotKeyPressed(VrpgHotkeys.SkillCodes[slot]))
            {
                released.Add(slot);
            }
        }

        foreach (int slot in released)
        {
            heldSkillSlots.Remove(slot);
            clientChannel?.SendPacket(new SkillCastRequestPacket { Slot = slot, Pressed = false });
        }
    }

    private static void ApplyEvasiveStepMotion(ICoreClientAPI api, EvasiveStepActivatedPacket packet)
    {
        if (api.World.Player?.Entity == null)
        {
            return;
        }

        var player = api.World.Player.Entity;
        double motionY = player.Pos.Motion.Y;
        player.Pos.Motion.Set(packet.MotionX, motionY, packet.MotionZ);
        player.WatchedAttributes.SetDouble("kbdirX", packet.MotionX);
        player.WatchedAttributes.SetDouble("kbdirY", motionY);
        player.WatchedAttributes.SetDouble("kbdirZ", packet.MotionZ);
        player.Attributes.SetInt("dmgkb", 1);
    }

    private void HandleSkillCastRequest(IServerPlayer player, SkillCastRequestPacket packet)
    {
        string error = "";
        AbilityFailureKind failureKind = AbilityFailureKind.Other;
        if (rpgModule?.TryHandleSkillInput(player, packet.Slot, packet.Pressed, out error, out failureKind) == true)
        {
            return;
        }

        if (rpgModule != null)
        {
            rpgModule.NotifyAbilityFailure(player, failureKind, string.IsNullOrWhiteSpace(error) ? "The VRPG skill could not be cast." : error);
        }
    }

    private void HandleHubOptionsRequest(ICoreServerAPI api, IServerPlayer player, UpdateHubOptionsPacket packet)
    {
        if (rpgModule == null)
        {
            return;
        }

        rpgModule.UpdateOptions(player, packet.ShowCooldownNotifications, packet.ShowResourceNotifications);
        SendHubPacket(api, player);
    }

    private void HandleStatAllocationRequest(ICoreServerAPI api, IServerPlayer player, StatAllocationRequestPacket packet)
    {
        string error = "";
        if (rpgModule == null || !rpgModule.TryAdjustBaseStat(player, packet.StatCode, packet.Delta, out error))
        {
            SendHubPacket(api, player, string.IsNullOrWhiteSpace(error) ? "The stat could not be adjusted." : error, true);
            return;
        }

        SendHubPacket(api, player, "Stat allocation updated.", false);
    }

    private void HandleSkillEquipRequest(ICoreServerAPI api, IServerPlayer player, SkillEquipRequestPacket packet)
    {
        string message = "The VRPG skill runtime is not available.";
        bool success = rpgModule?.TryEquipSkill(player, packet.Slot - 1, packet.SkillCode, out message) == true;
        SendHubPacket(api, player, message, !success);
    }

    private void HandleTalentPlanRequest(ICoreServerAPI api, IServerPlayer player, TalentPlanRequestPacket packet)
    {
        string message = "The VRPG talent runtime is not available.";
        bool success = rpgModule?.TryApplyTalentPlan(player, packet.Allocate, packet.Refund, out message) == true;
        SendHubPacket(api, player, message, !success);
    }

    private OpenHubPacket BuildHubPacket(IServerPlayer player)
    {
        OpenLibraryPacket library = BuildLibraryPacket();
        TalentTreeSnapshotPacket tree = rpgModule?.BuildTalentTreeSnapshot() ?? new TalentTreeSnapshotPacket();
        return new OpenHubPacket
        {
            Entries = library.Entries,
            Resources = rpgModule?.BuildResourceSnapshot(player),
            BaseStats = rpgModule?.BuildBaseStats(player) ?? Array.Empty<HubStatPacket>(),
            Talents = rpgModule?.BuildTalents(player) ?? Array.Empty<string>(),
            Classes = rpgModule?.BuildClasses(player) ?? Array.Empty<HubClassPacket>(),
            Options = rpgModule?.BuildOptions(player) ?? new HubOptionsPacket(),
            TalentTreeCode = tree.TreeCode,
            TalentTreeHash = tree.ContentHash,
            CanEditTalentTree = player.HasPrivilege(Privilege.controlserver)
        };
    }

    private void ReceiveTalentTreeSnapshot(ICoreClientAPI api, TalentTreeSnapshotPacket packet)
    {
        if (packet.SchemaVersion != TalentTreeSnapshotPacket.CurrentSchemaVersion
            || packet.Nodes == null
            || packet.Nodes.Length > TalentGraphComponent.MaximumNodeCount)
        {
            api.Logger.Error("[VRPG] Rejected incompatible talent tree snapshot {0} schema={1} nodes={2}.",
                packet.TreeCode,
                packet.SchemaVersion,
                packet.Nodes?.Length ?? 0);
            return;
        }

        clientTalentTree = packet;
        clientHubDialog?.UpdateTalentTree(packet);
    }

    private void OpenHubDialog(ICoreClientAPI api, OpenHubPacket packet)
    {
        packet.Options.SkillHotbarLocked = config.Modules.Rpg.SkillHotbar.Locked;
        packet.Options.SkillHotbarSlots = config.Modules.Rpg.SkillHotbar.SlotCount;
        bool treeMissing = !string.Equals(clientTalentTree.TreeCode, packet.TalentTreeCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(clientTalentTree.ContentHash, packet.TalentTreeHash, StringComparison.Ordinal);
        if (treeMissing)
        {
            clientChannel?.SendPacket(new TalentTreeRequestPacket
            {
                KnownTreeCode = clientTalentTree.TreeCode,
                KnownContentHash = clientTalentTree.ContentHash
            });
        }
        if (clientHubDialog?.IsOpened() == true)
        {
            clientHubDialog.UpdatePacket(packet);
            return;
        }

        clientLibraryDialog?.TryClose();
        clientLibraryDialog?.Dispose();
        clientLibraryDialog = null;
        clientHubDialog?.TryClose();
        clientHubDialog?.Dispose();
        clientHubDialog = new GuiDialogVRPGHub(
            api,
            packet,
            clientTalentTree,
            entries => OpenLibraryDialog(api, new OpenLibraryPacket { Entries = entries }),
            (statCode, delta) => clientChannel?.SendPacket(new StatAllocationRequestPacket { StatCode = statCode, Delta = delta }),
            (showCooldown, showResources) => clientChannel?.SendPacket(new UpdateHubOptionsPacket
            {
                ShowCooldownNotifications = showCooldown,
                ShowResourceNotifications = showResources
            }),
            (locked, slotCount) => SetClientHotbarOptions(api, locked, slotCount),
            (slot, skillCode) => clientChannel?.SendPacket(new SkillEquipRequestPacket { Slot = slot, SkillCode = skillCode }),
            (allocate, refund) => clientChannel?.SendPacket(new TalentPlanRequestPacket { Allocate = allocate, Refund = refund }),
            () => clientChannel?.SendPacket(new TalentEditorOpenRequestPacket()),
            config.Modules.Rpg.CombatVisuals,
            () => PersistClientConfig(api));
        clientHubDialog.TryOpen();
    }

    private void OpenTalentEditorDialog(ICoreClientAPI api, TalentEditorSnapshotPacket packet)
    {
        if (clientTalentEditorDialog?.IsOpened() == true)
        {
            clientTalentEditorDialog.UpdatePacket(packet);
            return;
        }

        clientHubDialog?.TryClose();
        clientTalentEditorDialog?.Dispose();
        clientTalentEditorDialog = new GuiDialogVRPGTalentEditor(
            api,
            packet,
            tree => clientChannel?.SendPacket(new TalentEditorOpenSavedTreePacket { TreeCode = tree }),
            template => clientChannel?.SendPacket(new TalentEditorSelectTemplatePacket { TemplateCode = template }),
            (node, stat, operation, amount) => clientChannel?.SendPacket(new TalentEditorAddModifierPacket
            {
                NodeCode = node,
                StatCode = stat,
                Operation = operation,
                Amount = amount
            }),
            (node, name) => clientChannel?.SendPacket(new TalentEditorRenameNodePacket { NodeCode = node, Name = name }),
            name => clientChannel?.SendPacket(new TalentEditorRenameTreePacket { Name = name }),
            () => clientChannel?.SendPacket(new TalentEditorSavePacket()),
            name => clientChannel?.SendPacket(new TalentEditorSaveAsPacket { Name = name }),
            tree => clientChannel?.SendPacket(new TalentEditorDeleteSavedTreePacket { TreeCode = tree }));
        clientTalentEditorDialog.TryOpen();
    }

    private void OpenLibraryDialog(ICoreClientAPI api, OpenLibraryPacket packet)
    {
        clientHubDialog?.TryClose();
        clientHubDialog?.Dispose();
        clientHubDialog = null;
        clientLibraryDialog?.TryClose();
        clientLibraryDialog?.Dispose();
        clientLibraryDialog = new GuiDialogVRPGLibrary(api, packet.Entries);
        clientLibraryDialog.TryOpen();
    }

    private void OpenResourceHudPacket(ICoreClientAPI api, RpgResourcePacket packet)
    {
        clientHubDialog?.UpdateResources(packet);

        if (clientResourceHud == null && config.Modules.Rpg.Enabled && config.Modules.Rpg.Hud.Enabled)
        {
            clientResourceHud = new HudElementVRPGResources(api, config.Modules.Rpg.Hud);
            clientResourceHud.TryOpen();
        }

        clientResourceHud?.SetSnapshot(packet);
        clientSkillHotbar?.SetResources(packet);
    }

    private void OpenSkillHotbarPacket(ICoreClientAPI api, SkillLoadoutPacket packet)
    {
        if (clientSkillHotbar == null && config.Modules.Rpg.Enabled && config.Modules.Rpg.SkillHotbar.Enabled)
        {
            clientSkillHotbar = new HudElementVRPGSkillHotbar(api, config.Modules.Rpg.SkillHotbar, () => PersistClientConfig(api));
            clientSkillHotbar.TryOpen();
        }

        clientSkillHotbar?.SetSnapshot(packet);
    }

    private void SetClientHotbarOptions(ICoreClientAPI api, bool locked, int slotCount)
    {
        int normalizedSlotCount = Math.Clamp(slotCount, 4, 8);
        clientSkillHotbar?.SetLocked(locked);
        clientSkillHotbar?.SetSlotCount(normalizedSlotCount);
        config.Modules.Rpg.SkillHotbar.Locked = locked;
        config.Modules.Rpg.SkillHotbar.SlotCount = normalizedSlotCount;
        PersistClientConfig(api);
    }

    private void PersistClientConfig(ICoreClientAPI api)
    {
        api.StoreModConfig(config, VRPGConfigStore.ConfigFileName);
    }
}
