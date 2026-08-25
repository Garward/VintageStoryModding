namespace ResponsiveVS.Config;

public enum ResponsiveDiagnosticsLevel
{
    Off = 0,
    Basic = 1,
    Verbose = 2,
    Trace = 3
}

public sealed class ResponsiveVSConfig
{
    public ResponsiveDiagnosticsLevel DiagnosticsLevel { get; set; } = ResponsiveDiagnosticsLevel.Off;
    public bool ForceBasicDiagnosticsInDevelopment { get; set; } = true;
    public NetworkConfig Network { get; set; } = new();
    public TransactionConfig Transactions { get; set; } = new();
    public FastCraftingConfig FastCrafting { get; set; } = new();
    public RuntimeDataConfig RuntimeData { get; set; } = new();
    public TimingGuardConfig TimingGuards { get; set; } = new();
    public CompatibilityConfig Compatibility { get; set; } = new();
}

public sealed class NetworkConfig
{
    public bool RequireHandshakeForOwnership { get; set; } = true;
    public bool DisableOwnershipOnProtocolMismatch { get; set; } = true;
}

public sealed class TransactionConfig
{
    public bool EnableInventoryOwnership { get; set; } = false;
    public bool EnableClientPreviewOnlyClicks { get; set; } = true;
    public bool EnableCraftingGridPreviewOwnership { get; set; } = true;
    public bool EnableCraftingGridDragPreview { get; set; } = true;
    public bool BypassClickDragGestures { get; set; } = true;
    public bool HoldPreviewThroughStaleServerEchoes { get; set; } = true;
    public int ClientPreviewTimeoutMs { get; set; } = 1500;
    public int TransactionTimeoutMs { get; set; } = 1500;
    public int SnapshotTimeoutMs { get; set; } = 1500;
    public bool EnablePreviewChaining { get; set; } = false;
}

public sealed class FastCraftingConfig
{
    public bool EnableIntegratedFastCrafting { get; set; } = false;
    public bool EnableMatchesPrefilter { get; set; } = false;
    public bool EnableDiagnostics { get; set; } = false;
}

public sealed class RuntimeDataConfig
{
    public bool EnableRuntimeDataHotPathPatch { get; set; } = false;
    public bool EnableAsObjectResultCache { get; set; } = true;
    public bool EnableStackAttributePacketDiagnostics { get; set; } = true;
    public bool TraceCallerStacks { get; set; } = false;
    public int MaxCachedAsObjectResults { get; set; } = 4096;
}

public sealed class TimingGuardConfig
{
    public bool EnableDeltaTimeClamp { get; set; } = false;
    public float MaxDeltaTimeSeconds { get; set; } = 0.25f;
    public bool EnableRefocusDebounce { get; set; } = false;
}

public sealed class CompatibilityConfig
{
    public bool DisableOwnershipWhenPrototypeModsLoaded { get; set; } = true;
    public bool EnableVanillaDoubleUpdateFix { get; set; } = false;
    public bool BypassStorageControllerVirtualInventory { get; set; } = true;
    public bool BypassCreativeInventory { get; set; } = true;
}
