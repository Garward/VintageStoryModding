using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using VRPG.Config;
using VRPG.Core;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Network;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.Players;
using VRPG.Modules.Rpg.Scaling;
using VRPG.Modules.Rpg.Skills;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.StatusEffects;
using VRPG.Modules.Rpg.Talents;
using VRPG.Patches;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg;

public sealed class RpgModule : IVrpgModule
{
    private readonly RpgModuleConfig config;
    private readonly VRPGDataRegistry data;
    private readonly IServerNetworkChannel channel;
    private readonly RpgPlayerStore playerStore = new RpgPlayerStore();
    private readonly TalentTreeCatalog talentTree = new TalentTreeCatalog();
    private readonly SavedTalentTreeLibrary savedTalentTrees = new SavedTalentTreeLibrary();
    private readonly StatusEffectTracker statusEffects;
    private RpgResourceService? resources;
    private CombatStateService? combatStates;
    private EvasiveStepService? evasiveStep;
    private CreatureProgressionService? creatures;
    private SkillCastingService? skills;
    private SkillDamageResolver? skillDamage;
    private CombatVisualBroadcaster? visualBroadcaster;
    private GroundAreaService? groundAreas;
    private TalentAllocationService? talentAllocation;
    private AbilityFailureNotificationService? failureNotifications;
    private long statusTickListenerId;
    private long resourceTickListenerId;
    private long loadoutTickListenerId;
    private long skillTickListenerId;
    private Vintagestory.API.Common.PlayerDelegate? groundAreaSnapshotHandler;
    private ICoreServerAPI? serverApi;
    private string savedTalentTreeHash = "";
    private Dictionary<string, int> savedTalentCosts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private TalentTreeSnapshotPacket activeTalentTree = new TalentTreeSnapshotPacket { TreeCode = "vrpg:default" };
    private readonly Dictionary<string, TalentEditorDraft> talentEditorDrafts = new Dictionary<string, TalentEditorDraft>(StringComparer.OrdinalIgnoreCase);

    public RpgModule(RpgModuleConfig config, VRPGDataRegistry data, IServerNetworkChannel channel)
    {
        this.config = config;
        this.data = data;
        this.channel = channel;
        statusEffects = new StatusEffectTracker(data);
    }

    public string Code => "rpg";

    public CombatVisualBroadcaster? VisualBroadcaster => visualBroadcaster;

    public GroundAreaService? GroundAreas => groundAreas;

    public void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        statusEffects.Changed += entityId =>
        {
            Vintagestory.API.Common.Entities.Entity? entity = serverApi?.World.GetEntityById(entityId);
            if (entity == null)
            {
                return;
            }

            StatusSync.Write(entity.WatchedAttributes, statusEffects.Get(entityId));
            entity.WatchedAttributes.MarkPathDirty(StatusSync.TreeKey);
        };
        playerStore.Load(api);
        talentTree.Load(api, data);
        savedTalentTrees.Load(api, talentTree);
        playerStore.ReconcileTalents(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), talentTree.All);
        combatStates = new CombatStateService(api, config.CombatStatLockSeconds);
        EntityDamageScalingPatch.HostileDamageObserved = combatStates.ObserveHostileDamage;
        resources = new RpgResourceService(api, config, data, talentTree, playerStore, channel, combatStates);
        evasiveStep = new EvasiveStepService(api, config, talentTree, playerStore, channel);
        resources.EvasiveStepCooldownRemaining = evasiveStep.CooldownRemainingSeconds;
        EntityDamageScalingPatch.DamagePrevented = evasiveStep.TryPreventDamage;
        skillDamage = new SkillDamageResolver(talentTree);
        visualBroadcaster = new CombatVisualBroadcaster(api, channel);
        groundAreas = new GroundAreaService(api, channel);
        skills = new SkillCastingService(api, data, playerStore, resources, skillDamage, visualBroadcaster, groundAreas, statusEffects);
        talentAllocation = new TalentAllocationService(talentTree, playerStore, resources, combatStates);
        failureNotifications = new AbilityFailureNotificationService(api, playerStore);
        activeTalentTree = CreateTalentTreeSnapshot();
        savedTalentTreeHash = activeTalentTree.ContentHash;
        savedTalentCosts = BuildTalentCostIndex();
        var scaling = new RpgScalingService(api, data, playerStore);
        creatures = new CreatureProgressionService(api, scaling, resources);
        new RpgCommandSystem(api, config, data, playerStore, statusEffects, resources, skills, talentAllocation).Register();
        api.Event.PlayerNowPlaying += resources.OnPlayerNowPlaying;
        api.Event.PlayerNowPlaying += SendLoadout;
        groundAreaSnapshotHandler = player => groundAreas?.SendSnapshot(player);
        api.Event.PlayerNowPlaying += groundAreaSnapshotHandler;
        creatures.Start();
        statusTickListenerId = api.Event.RegisterGameTickListener(dt => statusEffects.Tick(dt), 1000);
        resourceTickListenerId = api.Event.RegisterGameTickListener(resources.Tick, 1000);
        loadoutTickListenerId = api.Event.RegisterGameTickListener(_ => BroadcastLoadouts(api), 1000);
        skillTickListenerId = api.Event.RegisterGameTickListener(skills.Tick, 50);

        api.Logger.Notification(
            "[VRPG/RPG] Enabled. {0} stat points/level, {1} talent points/level. Data: {2}",
            config.BaseStatPointsPerLevel,
            config.BaseTalentPointsPerLevel,
            data.Summary());
    }

    public bool TryCastSkill(IServerPlayer player, int slot, out string error)
    {
        return TryCastSkill(player, slot, out error, out _);
    }

    public bool TryCastSkill(IServerPlayer player, int slot, out string error, out AbilityFailureKind failureKind)
    {
        return TryHandleSkillInput(player, slot, true, out error, out failureKind);
    }

    public bool TryHandleSkillInput(
        IServerPlayer player,
        int slot,
        bool pressed,
        out string error,
        out AbilityFailureKind failureKind)
    {
        if (skills == null)
        {
            error = "The VRPG skill runtime is not available.";
            failureKind = AbilityFailureKind.Other;
            return false;
        }

        bool cast = skills.TryHandleSlotInput(player, slot, pressed, out error, out failureKind);
        SendLoadout(player);
        return cast;
    }

    public void NotifyAbilityFailure(IServerPlayer player, AbilityFailureKind kind, string error)
    {
        failureNotifications?.Notify(player, kind, error);
    }

    public HubOptionsPacket BuildOptions(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        return new HubOptionsPacket
        {
            ShowCooldownNotifications = state.ShowCooldownNotifications,
            ShowResourceNotifications = state.ShowResourceNotifications
        };
    }

    public void UpdateOptions(IServerPlayer player, bool showCooldownNotifications, bool showResourceNotifications)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        state.ShowCooldownNotifications = showCooldownNotifications;
        state.ShowResourceNotifications = showResourceNotifications;
        playerStore.Save();
    }

    public bool HandleProjectileImpact(EntityVrpgSkillProjectile projectile)
    {
        return skills?.HandleProjectileImpact(projectile) == true;
    }

    public bool TryEquipSkill(IServerPlayer player, int slot, string skillCode, out string message)
    {
        if (skills == null)
        {
            message = "The VRPG skill runtime is not available.";
            return false;
        }

        bool equipped = skills.Equip(player, slot, string.IsNullOrWhiteSpace(skillCode) ? "clear" : skillCode, out message);
        SendLoadout(player);
        return equipped;
    }

    public SkillLoadoutPacket BuildSkillLoadout(IServerPlayer player)
    {
        return skills?.BuildLoadout(player) ?? new SkillLoadoutPacket();
    }

    public void SetSkillEmpowered(IServerPlayer player, string skillCode, bool on)
    {
        skills?.SetEmpowered(player.PlayerUID, skillCode, on);
        SendLoadout(player);
    }

    public bool ApplyStatus(long entityId, string code, int stacks, float magnitude, float seconds)
    {
        return statusEffects.Apply(entityId, code, 0, seconds, stacks, magnitude);
    }

    private void SendLoadout(IServerPlayer player)
    {
        if (skills != null)
        {
            channel.SendPacket(skills.BuildLoadout(player), player);
        }
    }

    private void BroadcastLoadouts(ICoreServerAPI api)
    {
        foreach (Vintagestory.API.Common.IPlayer onlinePlayer in api.World.AllOnlinePlayers)
        {
            if (onlinePlayer is IServerPlayer player)
            {
                SendLoadout(player);
            }
        }
    }

    public bool TryApplyTalentPlan(IServerPlayer player, string[] allocate, string[] refund, out string message)
    {
        if (talentAllocation == null)
        {
            message = "The VRPG talent runtime is not available.";
            return false;
        }
        return talentAllocation.TryApplyPlan(player, allocate, refund, out message);
    }

    public RpgResourcePacket? BuildResourceSnapshot(IServerPlayer player)
    {
        return resources?.BuildSnapshot(player);
    }

    public HubStatPacket[] BuildBaseStats(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        var stats = new HubStatPacket[state.BaseStats.Count];
        int index = 0;
        foreach (var entry in state.BaseStats)
        {
            stats[index++] = new HubStatPacket
            {
                Code = entry.Key,
                Value = entry.Value
            };
        }

        return stats;
    }

    public bool TryAdjustBaseStat(IServerPlayer player, string statCode, int delta, out string error)
    {
        error = "";
        if (combatStates?.IsInCombat(player, out float remainingSeconds) == true)
        {
            error = $"Stats are locked during combat. Try again in {Math.Ceiling(remainingSeconds):0} second(s).";
            return false;
        }

        if (delta != -1 && delta != 1)
        {
            error = "Stat adjustments must be exactly one point.";
            return false;
        }

        string shortCode = CoreAttributeResolver.Normalize(statCode);
        if (!CoreAttributeResolver.IsCoreAttribute(shortCode))
        {
            error = "Unknown base stat: " + statCode;
            return false;
        }

        RpgPlayerState state = playerStore.GetOrCreate(player);
        string canonicalCode = "vrpg:" + shortCode;
        string storedCode = state.BaseStats.ContainsKey(canonicalCode)
            ? canonicalCode
            : state.BaseStats.ContainsKey(shortCode) ? shortCode : canonicalCode;
        int current = state.BaseStats.TryGetValue(storedCode, out int value) ? Math.Max(0, value) : 0;

        if (delta > 0)
        {
            if (state.UnspentStatPoints <= 0)
            {
                error = "No unspent stat points are available.";
                return false;
            }

            if (!CoreAttributeResolver.IsCoreAttribute(state.StartingAttributeAffinity)
                && CoreAttributeResolver.TotalAllocated(state) == 0)
            {
                state.StartingAttributeAffinity = shortCode;
            }

            state.UnspentStatPoints--;
            state.BaseStats[storedCode] = current + 1;
        }
        else
        {
            if (current <= 0)
            {
                error = "That stat has no allocated points to refund.";
                return false;
            }

            state.BaseStats[storedCode] = current - 1;
            state.UnspentStatPoints++;
        }

        playerStore.Save();
        resources?.SendSnapshot(player);
        return true;
    }

    public string[] BuildTalents(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        return state.Talents.ToArray();
    }

    public TalentTreeSnapshotPacket BuildTalentTreeSnapshot()
    {
        return activeTalentTree;
    }

    private TalentTreeSnapshotPacket CreateTalentTreeSnapshot()
    {
        return CreateTalentTreeSnapshot(talentTree.TreeCode, talentTree.TreeName, talentTree.All);
    }

    private static TalentTreeSnapshotPacket CreateTalentTreeSnapshot(string treeCode, string treeName, IReadOnlyList<TalentNodeDefinition> definitions)
    {
        var nodes = new TalentTreeNodePacket[definitions.Count];
        int index = 0;
        foreach (TalentNodeDefinition talent in definitions)
        {
            var modifiers = new string[talent.Modifiers.Length];
            for (int i = 0; i < talent.Modifiers.Length; i++)
            {
                StatModifierDefinition modifier = talent.Modifiers[i];
                float value = modifier.Max != 0f || modifier.Min != 0f ? (modifier.Min + modifier.Max) / 2f : 0f;
                modifiers[i] = FormatTalentModifier(modifier, value);
            }

            nodes[index++] = new TalentTreeNodePacket
            {
                Code = talent.Code,
                Name = talent.Name,
                Description = talent.Description,
                X = talent.X,
                Y = talent.Y,
                Links = talent.Links,
                Cost = Math.Max(1, talent.Cost),
                Keystone = talent.Keystone,
                Modifiers = modifiers,
                VisualTier = TalentVisualTier(talent),
                Starter = talent.Starter,
                Foundation = talent.Foundation
            };
        }

        return new TalentTreeSnapshotPacket
        {
            SchemaVersion = TalentTreeSnapshotPacket.CurrentSchemaVersion,
            TreeCode = treeCode,
            TreeName = treeName,
            ContentHash = TalentTreeContentHash(treeCode, treeName, nodes),
            Nodes = nodes
        };
    }

    public TalentEditorSnapshotPacket OpenTalentEditor(IServerPlayer player, string feedback = "", bool error = false)
    {
        if (!talentEditorDrafts.TryGetValue(player.PlayerUID, out TalentEditorDraft? draft))
        {
            draft = new TalentEditorDraft
            {
                TreeCode = talentTree.TreeCode,
                TreeName = talentTree.TreeName,
                Nodes = CloneNodes(talentTree.All)
            };
            talentEditorDrafts[player.PlayerUID] = draft;
        }

        var templateCodes = new string[data.TalentTreeTemplates.Count];
        var templateNames = new string[templateCodes.Length];
        for (int i = 0; i < data.TalentTreeTemplates.Count; i++)
        {
            templateCodes[i] = data.TalentTreeTemplates.All[i].Code;
            templateNames[i] = data.TalentTreeTemplates.All[i].Name;
        }

        var savedTreeCodes = new string[savedTalentTrees.All.Count];
        var savedTreeNames = new string[savedTalentTrees.All.Count];
        for (int i = 0; i < savedTalentTrees.All.Count; i++)
        {
            TalentTreeSaveDocument savedTree = savedTalentTrees.All[i];
            savedTreeCodes[i] = savedTree.TreeCode;
            savedTreeNames[i] = savedTree.TreeName
                + (string.Equals(savedTree.TreeCode, talentTree.TreeCode, StringComparison.OrdinalIgnoreCase) ? "  (Active)" : "");
        }

        var stats = new TalentEditorStatPacket[data.Stats.Count];
        for (int i = 0; i < data.Stats.Count; i++)
        {
            StatDefinition stat = data.Stats.All[i];
            stats[i] = new TalentEditorStatPacket { Code = stat.Code, Name = stat.Name, Category = stat.Category };
        }

        return new TalentEditorSnapshotPacket
        {
            Tree = CreateTalentTreeSnapshot(draft.TreeCode, draft.TreeName, draft.Nodes),
            TemplateCodes = templateCodes,
            TemplateNames = templateNames,
            SavedTreeCodes = savedTreeCodes,
            SavedTreeNames = savedTreeNames,
            SelectedSavedTreeCode = draft.TreeCode,
            ActiveTreeCode = talentTree.TreeCode,
            GraphResetRevision = draft.GraphResetRevision,
            Stats = stats,
            SelectedTemplateCode = "",
            Dirty = draft.Dirty,
            Feedback = feedback,
            FeedbackError = error
        };
    }

    public TalentEditorSnapshotPacket SelectTalentEditorTemplate(IServerPlayer player, string templateCode)
    {
        OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        TalentTreeTemplateDefinition? template = data.TalentTreeTemplates.Get(templateCode);
        if (template == null)
        {
            return OpenTalentEditor(player, "Unknown talent-tree template: " + templateCode, true);
        }
        draft.Nodes = TalentTreeCatalog.FromTemplate(template);
        draft.Dirty = true;
        draft.GraphResetRevision++;
        return OpenTalentEditor(player, "Reset the private draft from " + template.Name + ". Save when ready.");
    }

    public TalentEditorSnapshotPacket OpenSavedTalentTree(IServerPlayer player, string treeCode)
    {
        TalentTreeSaveDocument? saved = savedTalentTrees.Get(treeCode);
        if (saved == null)
        {
            return OpenTalentEditor(player, "Unknown saved talent tree: " + treeCode, true);
        }

        talentEditorDrafts[player.PlayerUID] = new TalentEditorDraft
        {
            TreeCode = saved.TreeCode,
            TreeName = saved.TreeName,
            Nodes = CloneNodes(saved.Nodes),
            GraphResetRevision = talentEditorDrafts.TryGetValue(player.PlayerUID, out TalentEditorDraft? previous)
                ? previous.GraphResetRevision + 1
                : 1
        };
        return OpenTalentEditor(player, "Opened " + saved.TreeName + " as a private draft.");
    }

    public TalentEditorSnapshotPacket AddTalentEditorModifier(IServerPlayer player, TalentEditorAddModifierPacket request)
    {
        TalentEditorSnapshotPacket opened = OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        TalentNodeDefinition? node = Array.Find(draft.Nodes, entry => string.Equals(entry.Code, request.NodeCode, StringComparison.OrdinalIgnoreCase));
        StatDefinition? stat = data.Stats.Get(request.StatCode);
        if (node == null || stat == null || !float.IsFinite(request.Amount) || Math.Abs(request.Amount) > 100000f)
        {
            return OpenTalentEditor(player, "Select a valid node, stat, and finite amount.", true);
        }

        string operation = StatModifierOperations.Normalize(request.Operation);
        var modifiers = new List<StatModifierDefinition>(node.Modifiers);
        int existing = modifiers.FindIndex(item => string.Equals(item.Stat, stat.Code, StringComparison.OrdinalIgnoreCase)
            && string.Equals(StatModifierOperations.Normalize(item.Operation), operation, StringComparison.OrdinalIgnoreCase));
        var modifier = new StatModifierDefinition { Stat = stat.Code, Min = request.Amount, Max = request.Amount, Operation = operation };
        if (Math.Abs(request.Amount) < 0.0001f)
        {
            if (existing >= 0) modifiers.RemoveAt(existing);
        }
        else if (existing >= 0) modifiers[existing] = modifier;
        else modifiers.Add(modifier);
        node.Modifiers = modifiers.ToArray();
        draft.Dirty = true;
        return OpenTalentEditor(player, (Math.Abs(request.Amount) < 0.0001f ? "Removed modifier from " : "Updated ") + node.Name + " in the private draft.");
    }

    public TalentEditorSnapshotPacket RenameTalentEditorNode(IServerPlayer player, TalentEditorRenameNodePacket request)
    {
        OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        TalentNodeDefinition? node = Array.Find(draft.Nodes, entry => string.Equals(entry.Code, request.NodeCode, StringComparison.OrdinalIgnoreCase));
        string name = (request.Name ?? "").Trim();
        if (node == null)
        {
            return OpenTalentEditor(player, "Select a valid node before renaming it.", true);
        }
        if (name.Length > 64 || name.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
        {
            return OpenTalentEditor(player, "Node names must be a single line of at most 64 characters.", true);
        }

        node.Name = name;
        draft.Dirty = true;
        return OpenTalentEditor(player, name.Length == 0 ? "Cleared the node name in the private draft." : "Renamed the node to " + name + " in the private draft.");
    }

    public TalentEditorSnapshotPacket RenameTalentEditorTree(IServerPlayer player, TalentEditorRenameTreePacket request)
    {
        OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        string name = (request.Name ?? "").Trim();
        if (name.Length == 0 || name.Length > 64 || name.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
        {
            return OpenTalentEditor(player, "Tree names must be a single line of 1–64 characters.", true);
        }
        draft.TreeName = name;
        draft.Dirty = true;
        return OpenTalentEditor(player, "Renamed the draft tree to " + name + ".");
    }

    public TalentEditorSnapshotPacket SaveTalentEditor(IServerPlayer player)
    {
        OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        talentTree.Replace(draft.TreeCode, draft.TreeName, draft.Nodes);
        talentTree.Save();
        savedTalentTrees.Upsert(draft.TreeCode, draft.TreeName, draft.Nodes);
        bool changed = SynchronizeSavedTalentTree();
        draft.Nodes = CloneNodes(talentTree.All);
        draft.Dirty = false;
        return OpenTalentEditor(player, changed ? "Saved and synchronized the active talent tree." : "No saved changes were detected.");
    }

    public TalentEditorSnapshotPacket SaveTalentEditorAs(IServerPlayer player, string requestedName)
    {
        OpenTalentEditor(player);
        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        string name = (requestedName ?? "").Trim();
        if (name.Length == 0 || name.Length > 64 || name.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
        {
            return OpenTalentEditor(player, "New tree names must be a single line of 1–64 characters.", true);
        }

        draft.TreeCode = "vrpg:tree-" + Guid.NewGuid().ToString("N");
        draft.TreeName = name;
        talentTree.Replace(draft.TreeCode, draft.TreeName, draft.Nodes);
        talentTree.Save();
        savedTalentTrees.Upsert(draft.TreeCode, draft.TreeName, draft.Nodes);
        SynchronizeSavedTalentTree();
        draft.Nodes = CloneNodes(talentTree.All);
        draft.Dirty = false;
        return OpenTalentEditor(player, "Saved " + name + " as a new active talent tree.");
    }

    public TalentEditorSnapshotPacket DeleteSavedTalentTree(IServerPlayer player, string treeCode)
    {
        OpenTalentEditor(player);
        if (string.Equals(treeCode, talentTree.TreeCode, StringComparison.OrdinalIgnoreCase))
        {
            return OpenTalentEditor(player, "The active talent tree cannot be deleted. Activate another saved tree first.", true);
        }

        TalentTreeSaveDocument? saved = savedTalentTrees.Get(treeCode);
        if (saved == null || !savedTalentTrees.Remove(treeCode))
        {
            return OpenTalentEditor(player, "That saved talent tree no longer exists.", true);
        }

        TalentEditorDraft draft = talentEditorDrafts[player.PlayerUID];
        if (string.Equals(draft.TreeCode, treeCode, StringComparison.OrdinalIgnoreCase))
        {
            draft = new TalentEditorDraft
            {
                TreeCode = talentTree.TreeCode,
                TreeName = talentTree.TreeName,
                Nodes = CloneNodes(talentTree.All),
                GraphResetRevision = draft.GraphResetRevision + 1
            };
            talentEditorDrafts[player.PlayerUID] = draft;
        }
        return OpenTalentEditor(player, "Deleted saved tree " + saved.TreeName + ".");
    }

    private static TalentNodeDefinition[] CloneNodes(IReadOnlyList<TalentNodeDefinition> source)
    {
        var result = new TalentNodeDefinition[source.Count];
        for (int i = 0; i < source.Count; i++) result[i] = TalentTreeCatalog.Clone(source[i]);
        return result;
    }

    /// <summary>
    /// Commits the current authored tree as one revision boundary and refreshes all players once.
    /// Draft/editor mutations must never call this method until the admin presses Save.
    /// </summary>
    public bool SynchronizeSavedTalentTree()
    {
        TalentTreeSnapshotPacket snapshot = CreateTalentTreeSnapshot();
        if (string.Equals(savedTalentTreeHash, snapshot.ContentHash, StringComparison.Ordinal))
        {
            return false;
        }

        int reconciledPlayers = playerStore.ReconcileTalents(savedTalentCosts, talentTree.All);
        activeTalentTree = snapshot;
        savedTalentTreeHash = snapshot.ContentHash;
        savedTalentCosts = BuildTalentCostIndex();

        if (serverApi != null)
        {
            foreach (Vintagestory.API.Common.IPlayer online in serverApi.World.AllOnlinePlayers)
            {
                if (online is not IServerPlayer player)
                {
                    continue;
                }

                channel.SendPacket(snapshot, player);
                channel.SendPacket(new TalentProgressPacket { Talents = BuildTalents(player) }, player);
                resources?.RefreshAfterTalentTreeSave(player);
            }

            serverApi.Logger.Notification(
                "[VRPG/RPG] Saved talent tree {0} ({1} nodes, {2} reconciled player profiles); synchronized online players without restart.",
                snapshot.ContentHash,
                snapshot.Nodes.Length,
                reconciledPlayers);
        }

        return true;
    }

    private Dictionary<string, int> BuildTalentCostIndex()
    {
        var costs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (TalentNodeDefinition talent in talentTree.All)
        {
            costs[NormalizeTalentCode(talent.Code)] = talent.Starter ? 0 : Math.Max(1, talent.Cost);
        }
        return costs;
    }

    private static string NormalizeTalentCode(string code)
    {
        string value = (code ?? "").Trim();
        return value.Contains(':') ? value : "vrpg:" + value;
    }

    private static string TalentTreeContentHash(string treeCode, string treeName, TalentTreeNodePacket[] nodes)
    {
        var content = new StringBuilder(nodes.Length * 96);
        content.Append(treeCode).Append('|').Append(treeName).Append('\n');
        for (int i = 0; i < nodes.Length; i++)
        {
            TalentTreeNodePacket node = nodes[i];
            content.Append(node.Code).Append('|').Append(node.Name).Append('|').Append(node.Description)
                .Append('|').Append(node.X).Append('|').Append(node.Y).Append('|').Append(node.Cost)
                .Append('|').Append(node.Keystone).Append('|').Append(node.VisualTier)
                .Append('|').Append(node.Starter).Append('|').Append(node.Foundation).Append(';');
            for (int linkIndex = 0; linkIndex < node.Links.Length; linkIndex++)
            {
                content.Append(node.Links[linkIndex]).Append(',');
            }
            content.Append(';');
            for (int modifierIndex = 0; modifierIndex < node.Modifiers.Length; modifierIndex++)
            {
                content.Append(node.Modifiers[modifierIndex]).Append(',');
            }
            content.Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string TalentVisualTier(TalentNodeDefinition talent)
    {
        if (!string.IsNullOrWhiteSpace(talent.VisualTier) && !string.Equals(talent.VisualTier, "normal", StringComparison.OrdinalIgnoreCase))
        {
            return talent.VisualTier;
        }
        if (talent.Keystone)
        {
            return "gamechanger";
        }

        if (talent.Cost > 1 || talent.Modifiers.Length > 1)
        {
            return "major";
        }

        return "normal";
    }

    private static string FormatTalentModifier(StatModifierDefinition modifier, float value)
    {
        string operation = StatModifierOperations.Normalize(modifier.Operation);
        string stat = HumanizeStat(modifier.Stat);
        if (operation == StatModifierOperations.Increased)
        {
            return Math.Abs(value).ToString("0.##") + "% " + (value < 0f ? "reduced " : "additional ") + stat;
        }
        if (operation == StatModifierOperations.More)
        {
            return Math.Abs(value).ToString("0.##") + "% " + (value < 0f ? "less " : "more ") + stat;
        }

        string sign = value > 0f ? "+" : "";
        return sign + value.ToString("0.##") + " " + stat;
    }

    private static string HumanizeStat(string stat)
    {
        string value = stat ?? "";
        int separator = value.IndexOf(':');
        if (separator >= 0) value = value.Substring(separator + 1);
        string[] words = value.Replace('-', '_').Split('_', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
        }
        return words.Length == 0 ? "Unknown Stat" : string.Join(" ", words);
    }

    private sealed class TalentEditorDraft
    {
        public string TreeCode { get; set; } = "vrpg:custom";
        public string TreeName { get; set; } = "Talent Tree";
        public TalentNodeDefinition[] Nodes { get; set; } = Array.Empty<TalentNodeDefinition>();
        public bool Dirty { get; set; }
        public int GraphResetRevision { get; set; }
    }

    public HubClassPacket[] BuildClasses(IServerPlayer player)
    {
        RpgPlayerState state = playerStore.GetOrCreate(player);
        var classes = new System.Collections.Generic.List<ClassDefinition>(data.Classes.All);
        classes.Sort((left, right) =>
        {
            int order = left.SortOrder.CompareTo(right.SortOrder);
            return order != 0 ? order : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        var packets = new HubClassPacket[classes.Count];
        for (int i = 0; i < classes.Count; i++)
        {
            ClassDefinition definition = classes[i];
            packets[i] = new HubClassPacket
            {
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                Icon = definition.Icon,
                Color = definition.Color,
                Tags = definition.Tags,
                Skills = SkillsForClass(definition.Code, state)
            };
        }

        return packets;
    }

    private HubSkillPacket[] SkillsForClass(string classCode, RpgPlayerState state)
    {
        var matching = new System.Collections.Generic.List<HubSkillPacket>();
        foreach (SkillDefinition skill in data.Skills.All)
        {
            if (!string.Equals(skill.ClassCode, classCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matching.Add(new HubSkillPacket
            {
                Code = skill.Code,
                Name = skill.Name,
                Description = skill.Description,
                RequiredLevel = Math.Max(1, skill.RequiredLevel),
                Tags = skill.Tags,
                ClassCode = skill.ClassCode,
                Icon = skill.Icon,
                Color = skill.Color,
                LearnedLevel = state.GetSkillLevel(skill.Code),
                MaxLevel = Math.Max(1, skill.MaxLevel),
                EquippedSlot = EquippedSlot(state, skill.Code),
                Delivery = skill.Delivery,
                DamageType = skill.Damage.Type,
                Damage = skillDamage?.Resolve(state, skill, Math.Max(1, state.GetSkillLevel(skill.Code)))
                    ?? skill.DamageAtLevel(Math.Max(1, state.GetSkillLevel(skill.Code))),
                ResourceType = skill.Resource.Type,
                ResourceCost = skill.ResourceCostAtLevel(Math.Max(1, state.GetSkillLevel(skill.Code))),
                CooldownSeconds = skill.CooldownSeconds,
                Range = skill.Range,
                Radius = skill.Radius,
                TimingMode = skill.Timing.Mode,
                HitCount = skill.Timing.HitCount,
                HitIntervalSeconds = skill.Timing.HitIntervalSeconds,
                ResourceCostMode = skill.Resource.CostMode,
                MeleeArcDegrees = skill.Melee.ArcDegrees,
                MeleeWidth = skill.Melee.Width,
                ProjectileImpactMode = string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
                    ? skill.Projectile.ImpactMode
                    : ""
            });
        }

        matching.Sort((left, right) =>
        {
            int level = left.RequiredLevel.CompareTo(right.RequiredLevel);
            return level != 0 ? level : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        return matching.ToArray();
    }

    private static int EquippedSlot(RpgPlayerState state, string skillCode)
    {
        for (int i = 0; i < state.EquippedSkills.Length; i++)
        {
            if (string.Equals(state.EquippedSkills[i], skillCode, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }

        return 0;
    }

    public void Dispose()
    {
        if (serverApi != null)
        {
            if (resources != null)
            {
                serverApi.Event.PlayerNowPlaying -= resources.OnPlayerNowPlaying;
            }
            serverApi.Event.PlayerNowPlaying -= SendLoadout;
            if (groundAreaSnapshotHandler != null)
            {
                serverApi.Event.PlayerNowPlaying -= groundAreaSnapshotHandler;
                groundAreaSnapshotHandler = null;
            }
            if (statusTickListenerId != 0) serverApi.Event.UnregisterGameTickListener(statusTickListenerId);
            if (resourceTickListenerId != 0) serverApi.Event.UnregisterGameTickListener(resourceTickListenerId);
            if (loadoutTickListenerId != 0) serverApi.Event.UnregisterGameTickListener(loadoutTickListenerId);
            if (skillTickListenerId != 0) serverApi.Event.UnregisterGameTickListener(skillTickListenerId);
        }

        if (combatStates != null && EntityDamageScalingPatch.HostileDamageObserved == combatStates.ObserveHostileDamage)
        {
            EntityDamageScalingPatch.HostileDamageObserved = null;
        }

        if (evasiveStep != null && EntityDamageScalingPatch.DamagePrevented == evasiveStep.TryPreventDamage)
        {
            EntityDamageScalingPatch.DamagePrevented = null;
        }

        creatures?.Stop();
        groundAreas?.Dispose();
        groundAreas = null;
        serverApi = null;
    }
}
