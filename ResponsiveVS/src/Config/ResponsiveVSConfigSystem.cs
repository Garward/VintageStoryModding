using System;
using Vintagestory.API.Common;

namespace ResponsiveVS.Config;

public static class ResponsiveVSConfigSystem
{
    public const string ConfigFileName = "responsivevs.json";

    public static ResponsiveVSConfig Config { get; private set; } = new();

    public static void Load(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<ResponsiveVSConfig>(ConfigFileName) ?? new ResponsiveVSConfig();
            Normalize(Config);
            api.StoreModConfig(Config, ConfigFileName);
        }
        catch (Exception exception)
        {
            Config = new ResponsiveVSConfig();
            api.Logger.Warning("[responsivevs] Failed to load config, using defaults: {0}", exception);
        }
    }

    private static void Normalize(ResponsiveVSConfig config)
    {
        config.Network ??= new NetworkConfig();
        config.Transactions ??= new TransactionConfig();
        config.FastCrafting ??= new FastCraftingConfig();
        config.RuntimeData ??= new RuntimeDataConfig();
        config.TimingGuards ??= new TimingGuardConfig();
        config.Compatibility ??= new CompatibilityConfig();

        if (config.Transactions.TransactionTimeoutMs < 100)
        {
            config.Transactions.TransactionTimeoutMs = 100;
        }

        if (config.Transactions.ClientPreviewTimeoutMs < 100)
        {
            config.Transactions.ClientPreviewTimeoutMs = 100;
        }

        if (config.Transactions.SnapshotTimeoutMs < 100)
        {
            config.Transactions.SnapshotTimeoutMs = 100;
        }

        if (config.TimingGuards.MaxDeltaTimeSeconds <= 0)
        {
            config.TimingGuards.MaxDeltaTimeSeconds = 0.25f;
        }

        if (config.RuntimeData.MaxCachedAsObjectResults < 0)
        {
            config.RuntimeData.MaxCachedAsObjectResults = 0;
        }
    }
}
