using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Talents;

public sealed class TalentTreeCatalog
{
    private const string SaveFileName = "vrpg-active-talent-tree.json";
    private readonly List<TalentNodeDefinition> nodes = new List<TalentNodeDefinition>();
    private readonly Dictionary<string, TalentNodeDefinition> byCode = new Dictionary<string, TalentNodeDefinition>(StringComparer.OrdinalIgnoreCase);
    private ICoreServerAPI? api;

    public string TreeCode { get; private set; } = "vrpg:default";
    public string TreeName { get; private set; } = "Talent Tree";
    public IReadOnlyList<TalentNodeDefinition> All => nodes;

    public void Load(ICoreServerAPI serverApi, VRPGDataRegistry data)
    {
        api = serverApi;
        TalentTreeSaveDocument? saved = null;
        try
        {
            saved = api.LoadModConfig<TalentTreeSaveDocument>(SaveFileName);
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VRPG/RPG] Failed to load authored talent tree: {0}", ex.Message);
        }

        bool legacyFixture = saved?.Nodes?.Length > 0
            && string.Equals(saved.TreeCode, "vrpg:default", StringComparison.OrdinalIgnoreCase)
            && saved.Nodes.Length <= 9;
        if (saved?.Nodes?.Length > 0 && !legacyFixture)
        {
            bool isDefaultTree = string.Equals(saved.TreeCode, "vrpg:six_sector_426", StringComparison.OrdinalIgnoreCase);
            bool schemaMigration = saved.SchemaVersion < TalentTreeSaveDocument.CurrentSchemaVersion;
            bool defaultNameMigration = isDefaultTree
                && string.Equals(saved.TreeName, "Six-Sector 426 Scaffold", StringComparison.OrdinalIgnoreCase);
            if (schemaMigration && isDefaultTree)
            {
                MigrateScaffoldPresentation(saved, data.TalentTreeTemplates.Get("vrpg:six_sector_426"));
            }
            else if (defaultNameMigration)
            {
                saved.TreeName = data.TalentTreeTemplates.Get("vrpg:six_sector_426")?.Name ?? "VRPG";
            }
            Replace(saved.TreeCode, string.IsNullOrWhiteSpace(saved.TreeName) ? "Talent Tree" : saved.TreeName, saved.Nodes);
            if (schemaMigration || defaultNameMigration) Save();
            return;
        }

        TalentTreeTemplateDefinition? scaffold = data.TalentTreeTemplates.Get("vrpg:six_sector_426");
        if (scaffold != null && scaffold.Nodes.Length > 0)
        {
            Replace(scaffold.Code, scaffold.Name, FromTemplate(scaffold));
            Save();
            api.Logger.Notification(
                "[VRPG/RPG] Activated the statless {0}-node talent scaffold as the default tree{1}.",
                scaffold.Nodes.Length,
                legacyFixture ? " and replaced the legacy nine-node fixture" : "");
            return;
        }

        Replace("vrpg:default", "Legacy Talent Tree", data.Talents.All);
    }

    public TalentNodeDefinition? Get(string code)
    {
        return byCode.TryGetValue(Normalize(code), out TalentNodeDefinition? node) ? node : null;
    }

    public void Replace(string treeCode, string treeName, IEnumerable<TalentNodeDefinition> definitions)
    {
        nodes.Clear();
        byCode.Clear();
        TreeCode = string.IsNullOrWhiteSpace(treeCode) ? "vrpg:custom" : treeCode;
        TreeName = string.IsNullOrWhiteSpace(treeName) ? "Talent Tree" : treeName.Trim();
        foreach (TalentNodeDefinition source in definitions)
        {
            TalentNodeDefinition node = Clone(source);
            nodes.Add(node);
            byCode[Normalize(node.Code)] = node;
        }
        nodes.Sort((left, right) => string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase));
    }

    public void Save()
    {
        api?.StoreModConfig(new TalentTreeSaveDocument
        {
            SchemaVersion = TalentTreeSaveDocument.CurrentSchemaVersion,
            TreeCode = TreeCode,
            TreeName = TreeName,
            Nodes = nodes.ToArray()
        }, SaveFileName);
    }

    public static TalentNodeDefinition[] FromTemplate(TalentTreeTemplateDefinition template)
    {
        var result = new TalentNodeDefinition[template.Nodes.Length];
        for (int i = 0; i < template.Nodes.Length; i++)
        {
            TalentTreeTemplateNodeDefinition node = template.Nodes[i];
            result[i] = new TalentNodeDefinition
            {
                Code = node.Code,
                Name = node.Name,
                Description = node.Description,
                X = node.X,
                Y = node.Y,
                Links = (string[])node.Links.Clone(),
                Modifiers = CloneModifiers(node.Modifiers),
                Cost = Math.Max(1, node.Cost),
                Keystone = node.Keystone,
                Starter = node.Starter,
                Foundation = node.Foundation,
                VisualTier = node.VisualTier
            };
        }
        return result;
    }

    public static TalentNodeDefinition Clone(TalentNodeDefinition source)
    {
        return new TalentNodeDefinition
        {
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            X = source.X,
            Y = source.Y,
            Links = (string[])(source.Links ?? Array.Empty<string>()).Clone(),
            Modifiers = CloneModifiers(source.Modifiers),
            Cost = Math.Max(1, source.Cost),
            Keystone = source.Keystone,
            Starter = source.Starter,
            Foundation = source.Foundation,
            VisualTier = source.VisualTier
        };
    }

    private static StatModifierDefinition[] CloneModifiers(StatModifierDefinition[]? modifiers)
    {
        modifiers ??= Array.Empty<StatModifierDefinition>();
        var result = new StatModifierDefinition[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
        {
            result[i] = new StatModifierDefinition
            {
                Stat = modifiers[i].Stat,
                Min = modifiers[i].Min,
                Max = modifiers[i].Max,
                Operation = modifiers[i].Operation
            };
        }
        return result;
    }

    private static string Normalize(string code)
    {
        string value = (code ?? "").Trim();
        return value.Contains(':') ? value : "vrpg:" + value;
    }

    private static void MigrateScaffoldPresentation(TalentTreeSaveDocument saved, TalentTreeTemplateDefinition? template)
    {
        if (template == null) return;
        saved.TreeName = template.Name;
        var templateByCode = new Dictionary<string, TalentTreeTemplateNodeDefinition>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < template.Nodes.Length; i++) templateByCode[Normalize(template.Nodes[i].Code)] = template.Nodes[i];
        for (int i = 0; i < saved.Nodes.Length; i++)
        {
            TalentNodeDefinition node = saved.Nodes[i];
            if (!templateByCode.TryGetValue(Normalize(node.Code), out TalentTreeTemplateNodeDefinition? current)) continue;
            node.Name = current.Name;
            node.Description = current.Description;
            node.VisualTier = current.VisualTier;
        }
    }
}

public sealed class TalentTreeSaveDocument
{
    public const int CurrentSchemaVersion = 4;
    public int SchemaVersion { get; set; }
    public string TreeCode { get; set; } = "vrpg:custom";
    public string TreeName { get; set; } = "Talent Tree";
    public TalentNodeDefinition[] Nodes { get; set; } = Array.Empty<TalentNodeDefinition>();
}
