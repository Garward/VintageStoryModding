using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRPG.Client.UI;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Retained page choices, viewport and draft; never owned by a shell composition.</summary>
public sealed record HubPageState(string SelectedClass = "", string SelectedSkill = "", string OptionsCategory = "",
    string LibraryQuery = "", string LibrarySelection = "", double TalentPanX = 0, double TalentPanY = 0,
    double TalentZoom = 1, string TalentSelection = "", ImmutableArray<string> TalentAllocations = default,
    ImmutableArray<string> TalentRefunds = default);

/// <summary>Connection-owned snapshots. Packet access returns defensive copies of mutable wire DTOs.</summary>
public sealed class HubPageClientService : IDisposable
{
    private static readonly JsonSerializerOptions copyOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly Action<OpenHubRequestPacket> requestHub;
    private OpenHubPacket? packet;
    private TalentTreeSnapshotPacket? talentTree;
    private RpgResourcePacket? resources;
    private LibraryEntryPacket[] libraryEntries = Array.Empty<LibraryEntryPacket>();
    private bool treeRequestPending, disposed;
    public HubPageActions Actions { get; }
    public StatsPage Stats { get; }
    public SkillsPage Skills { get; }
    public long SnapshotRevision { get; private set; }
    public HubPageState State { get; set; } = new();
    public OpenHubPacket? Packet => packet == null ? null : Copy(packet);
    public TalentTreeSnapshotPacket? TalentTree => talentTree == null ? null : Copy(talentTree);
    public LibraryEntryPacket[] LibraryEntries => Copy(libraryEntries);
    public bool HasSnapshot => packet != null;
    public bool Loading { get; private set; }
    public string ErrorKey { get; private set; } = "";
    public event Action? Changed;

    public HubPageClientService(Action<OpenHubRequestPacket> requestHub, HubPageActions actions)
    {
        this.requestHub = requestHub ?? throw new ArgumentNullException(nameof(requestHub)); Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        Stats = new(this); Skills = new(this);
    }

    // Include explicit zero/false values: wire serialization can omit them and reapply DTO initializers.
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, copyOptions), copyOptions)!;

    public void RequestSnapshot()
    {
        if (disposed || Loading) return;
        Loading = true; ErrorKey = "";
        try { requestHub(new() { KnownTalentTreeCode = talentTree?.TreeCode ?? "", KnownTalentTreeHash = talentTree?.ContentHash ?? "" }); }
        catch (Exception error) { Loading = false; ErrorKey = "hub-error"; Actions.ReportError(error); }
        Changed?.Invoke();
    }

    public void Receive(OpenHubPacket next)
    {
        if (disposed) return;
        packet = Copy(next);
        packet.Entries ??= Array.Empty<LibraryEntryPacket>(); packet.BaseStats ??= Array.Empty<HubStatPacket>();
        packet.Classes ??= Array.Empty<HubClassPacket>(); packet.Talents ??= Array.Empty<string>();
        packet.Options ??= new();
        foreach (var cls in packet.Classes)
        {
            cls.Skills ??= Array.Empty<HubSkillPacket>(); cls.Tags ??= Array.Empty<string>();
            foreach (var skill in cls.Skills) skill.Tags ??= Array.Empty<string>();
        }
        Actions.ApplyClientOptions(packet.Options);
        packet.Resources ??= resources;
        resources = packet.Resources;
        libraryEntries = packet.Entries;
        Loading = false; ErrorKey = "";
        SnapshotRevision++;
        Stats.Receive(packet); Skills.Receive(Copy(packet));
        RequestMissingTree();
        Changed?.Invoke();
    }

    private void RequestMissingTree()
    {
        if (packet == null || treeRequestPending || (string.IsNullOrEmpty(packet.TalentTreeCode) && string.IsNullOrEmpty(packet.TalentTreeHash))) return;
        if (string.Equals(talentTree?.TreeCode, packet.TalentTreeCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(talentTree?.ContentHash, packet.TalentTreeHash, StringComparison.Ordinal)) return;
        treeRequestPending = true;
        try { Actions.RequestTalentTree(new() { KnownTreeCode = talentTree?.TreeCode ?? "", KnownContentHash = talentTree?.ContentHash ?? "" }); }
        catch (Exception error) { treeRequestPending = false; ErrorKey = "hub-error"; Actions.ReportError(error); }
    }

    public void Receive(TalentTreeSnapshotPacket next)
    {
        if (disposed) return;
        treeRequestPending = false;
        if (next.SchemaVersion != TalentTreeSnapshotPacket.CurrentSchemaVersion || next.Nodes == null || next.Nodes.Length > TalentGraphComponent.MaximumNodeCount)
        {
            ErrorKey = "hub-error";
            Actions.ReportError(new InvalidOperationException($"Rejected talent snapshot {next.TreeCode}: schema={next.SchemaVersion}, nodes={next.Nodes?.Length ?? 0}."));
            Changed?.Invoke(); return;
        }
        talentTree = Copy(next);
        foreach (var node in talentTree.Nodes) { node.Links ??= Array.Empty<string>(); node.Modifiers ??= Array.Empty<string>(); }
        ErrorKey = "";
        // The server sends this before a refreshed Hub packet. Its previous identity is not a refresh target.
        Changed?.Invoke();
    }

    public void Receive(TalentProgressPacket next)
    {
        if (disposed) return;
        if (packet != null) packet.Talents = (string[])(next.Talents?.Clone() ?? Array.Empty<string>());
        Changed?.Invoke();
    }

    public void Receive(RpgResourcePacket next)
    {
        if (disposed) return;
        resources = Copy(next);
        if (packet != null) packet.Resources = resources;
        Stats.UpdateResources(resources);
        Changed?.Invoke();
    }

    public void Receive(OpenLibraryPacket next)
    {
        if (disposed) return;
        libraryEntries = Copy(next).Entries ?? Array.Empty<LibraryEntryPacket>();
        if (packet != null) packet.Entries = libraryEntries;
        Changed?.Invoke();
    }

    public void ResetConnection()
    {
        packet = null; talentTree = null; resources = null; libraryEntries = Array.Empty<LibraryEntryPacket>();
        State = new(); Loading = false; treeRequestPending = false; ErrorKey = "";
        Stats.Reset(); Skills.Reset();
        Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; ResetConnection(); Changed = null;
    }
}
