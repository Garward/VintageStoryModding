using System.Collections.Generic;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;

namespace ResponsiveVS.Compatibility;

public sealed class IncompatibleModDetector
{
    private static readonly string[] PrototypeModIds =
    {
        "itemsyncfixes",
        "fastcraftinggrid"
    };

    public IReadOnlyList<string> LoadedPrototypeMods { get; private set; } = new List<string>();

    public bool HasHardIncompatibility(ICoreAPI api)
    {
        List<string> loaded = new List<string>();

        foreach (string modId in PrototypeModIds)
        {
            if (api.ModLoader.IsModEnabled(modId))
            {
                loaded.Add(modId);
            }
        }

        LoadedPrototypeMods = loaded;

        if (loaded.Count == 0)
        {
            return false;
        }

        ResponsiveDiagnostics.Warning("Prototype mod(s) loaded alongside ResponsiveVS: {0}", string.Join(", ", loaded));
        return ResponsiveVSConfigSystem.Config.Compatibility.DisableOwnershipWhenPrototypeModsLoaded;
    }
}
