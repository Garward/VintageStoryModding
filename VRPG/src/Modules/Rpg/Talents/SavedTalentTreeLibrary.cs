using System;
using System.Collections.Generic;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Talents;

public sealed class SavedTalentTreeLibrary
{
    private const string SaveFileName = "vrpg-saved-talent-trees.json";
    private readonly List<TalentTreeSaveDocument> trees = new List<TalentTreeSaveDocument>();
    private ICoreServerAPI? api;

    public IReadOnlyList<TalentTreeSaveDocument> All => trees;

    public void Load(ICoreServerAPI serverApi, TalentTreeCatalog activeTree)
    {
        api = serverApi;
        SavedTalentTreeLibraryDocument? saved = null;
        try
        {
            saved = api.LoadModConfig<SavedTalentTreeLibraryDocument>(SaveFileName);
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VRPG/RPG] Failed to load the saved talent-tree library: {0}", ex.Message);
        }

        trees.Clear();
        if (saved?.Trees != null)
        {
            for (int i = 0; i < saved.Trees.Length; i++)
            {
                TalentTreeSaveDocument candidate = saved.Trees[i];
                if (candidate.Nodes == null || candidate.Nodes.Length == 0 || string.IsNullOrWhiteSpace(candidate.TreeCode)) continue;
                if (Get(candidate.TreeCode) != null) continue;
                trees.Add(Clone(candidate));
            }
        }

        Upsert(activeTree.TreeCode, activeTree.TreeName, activeTree.All, save: false);
        Save();
    }

    public TalentTreeSaveDocument? Get(string code)
    {
        for (int i = 0; i < trees.Count; i++)
        {
            if (string.Equals(trees[i].TreeCode, code, StringComparison.OrdinalIgnoreCase)) return trees[i];
        }
        return null;
    }

    public void Upsert(string code, string name, IReadOnlyList<Data.Definitions.TalentNodeDefinition> nodes, bool save = true)
    {
        var document = new TalentTreeSaveDocument
        {
            SchemaVersion = TalentTreeSaveDocument.CurrentSchemaVersion,
            TreeCode = code,
            TreeName = name,
            Nodes = CloneNodes(nodes)
        };
        int existing = trees.FindIndex(tree => string.Equals(tree.TreeCode, code, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) trees[existing] = document;
        else trees.Add(document);
        Sort();
        if (save) Save();
    }

    public bool Remove(string code)
    {
        int index = trees.FindIndex(tree => string.Equals(tree.TreeCode, code, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        trees.RemoveAt(index);
        Save();
        return true;
    }

    private void Save()
    {
        api?.StoreModConfig(new SavedTalentTreeLibraryDocument
        {
            SchemaVersion = SavedTalentTreeLibraryDocument.CurrentSchemaVersion,
            Trees = trees.ToArray()
        }, SaveFileName);
    }

    private void Sort()
    {
        trees.Sort((left, right) => string.Compare(left.TreeName, right.TreeName, StringComparison.OrdinalIgnoreCase));
    }

    private static TalentTreeSaveDocument Clone(TalentTreeSaveDocument source)
    {
        return new TalentTreeSaveDocument
        {
            SchemaVersion = TalentTreeSaveDocument.CurrentSchemaVersion,
            TreeCode = source.TreeCode,
            TreeName = source.TreeName,
            Nodes = CloneNodes(source.Nodes)
        };
    }

    private static Data.Definitions.TalentNodeDefinition[] CloneNodes(IReadOnlyList<Data.Definitions.TalentNodeDefinition> source)
    {
        var result = new Data.Definitions.TalentNodeDefinition[source.Count];
        for (int i = 0; i < source.Count; i++) result[i] = TalentTreeCatalog.Clone(source[i]);
        return result;
    }
}

public sealed class SavedTalentTreeLibraryDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public TalentTreeSaveDocument[] Trees { get; set; } = Array.Empty<TalentTreeSaveDocument>();
}
