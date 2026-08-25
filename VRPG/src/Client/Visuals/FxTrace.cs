using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VRPG.Client.Visuals;

/// <summary>Opt-in structured impact trace. Failures disable it and never escape into rendering.</summary>
public sealed class FxTrace : IDisposable
{
    private readonly ICoreClientAPI capi;
    private StreamWriter? writer;
    private bool failureLogged;

    public bool Enabled => writer != null;
    public string Path { get; }

    public FxTrace(ICoreClientAPI capi)
    {
        this.capi = capi;
        Path = System.IO.Path.Combine(capi.GetOrCreateDataPath("VRPG"), "fx-trace.ndjson");
    }

    public void RegisterCommand()
    {
        capi.ChatCommands
            .GetOrCreate("vrpg")
            .BeginSubCommand("fx")
                .WithDescription("VRPG spell FX diagnostics.")
                .BeginSubCommand("trace")
                    .WithDescription("Enable or disable the NDJSON impact trace.")
                    .WithArgs(capi.ChatCommands.Parsers.OptionalWord("state"))
                    .HandleWith(args => HandleTraceCommand((string?)args[0]))
                .EndSubCommand()
            .EndSubCommand();
    }

    public PendingFxTrace? Begin(
        string skillCode,
        string preset,
        float radius,
        Vec3d position,
        FxSyncMeasurement? sync,
        VisualBudgetSnapshot budget,
        int layerCount)
    {
        if (!Enabled) return null;

        var record = new FxTraceRecord
        {
            T = capi.ElapsedMilliseconds,
            Tick = capi.World.ElapsedMilliseconds / 50,
            Skill = skillCode ?? "",
            Preset = preset ?? "",
            Radius = radius,
            Position = new[] { position.X, position.Y, position.Z },
            Sync = sync == null ? null : FxTraceSync.From(sync),
            Budget = new FxTraceBudget
            {
                Spent = budget.Spent,
                PerSecond = budget.PerSecond,
                Load = budget.Load
            }
        };
        var pending = new PendingFxTrace(this, record, layerCount);
        if (layerCount == 0) pending.CompleteIfReady();
        return pending;
    }

    internal void Write(FxTraceRecord record)
    {
        if (writer == null) return;
        try
        {
            writer.WriteLine(JsonConvert.SerializeObject(record, Formatting.None));
        }
        catch (Exception ex)
        {
            DisableAfterFailure(ex);
        }
    }

    public void Dispose()
    {
        writer?.Dispose();
        writer = null;
    }

    private TextCommandResult HandleTraceCommand(string? state)
    {
        string normalized = (state ?? "status").ToLowerInvariant();
        if (normalized is "on" or "1" or "true" or "enable" or "enabled")
        {
            try
            {
                writer?.Dispose();
                writer = new StreamWriter(Path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
                return TextCommandResult.Success("VRPG FX trace enabled: " + Path);
            }
            catch (Exception ex)
            {
                DisableAfterFailure(ex);
                return TextCommandResult.Error("Could not enable VRPG FX trace; see client log.");
            }
        }

        if (normalized is "off" or "0" or "false" or "disable" or "disabled")
        {
            Dispose();
            return TextCommandResult.Success("VRPG FX trace disabled.");
        }

        return TextCommandResult.Success(Enabled
            ? "VRPG FX trace is enabled: " + Path
            : "VRPG FX trace is disabled.");
    }

    private void DisableAfterFailure(Exception ex)
    {
        writer?.Dispose();
        writer = null;
        if (failureLogged) return;
        failureLogged = true;
        capi.Logger.Error("[VRPG] FX trace disabled after write failure: {0}", ex);
    }
}

public sealed class PendingFxTrace
{
    private readonly FxTrace owner;
    private readonly FxTraceRecord record;
    private int remaining;
    private bool completed;

    internal PendingFxTrace(FxTrace owner, FxTraceRecord record, int remaining)
    {
        this.owner = owner;
        this.record = record;
        this.remaining = remaining;
    }

    public void AddLayer(FxTraceLayer layer)
    {
        if (completed) return;
        record.Layers.Add(layer);
        remaining--;
        CompleteIfReady();
    }

    internal void CompleteIfReady()
    {
        if (completed || remaining > 0) return;
        completed = true;
        owner.Write(record);
    }
}

public sealed class FxTraceRecord
{
    [JsonProperty("t")] public long T { get; set; }
    [JsonProperty("tick")] public long Tick { get; set; }
    [JsonProperty("ev")] public string Event { get; set; } = "impact";
    [JsonProperty("skill")] public string Skill { get; set; } = "";
    [JsonProperty("preset")] public string Preset { get; set; } = "";
    [JsonProperty("radius")] public float Radius { get; set; }
    [JsonProperty("sync", NullValueHandling = NullValueHandling.Ignore)] public FxTraceSync? Sync { get; set; }
    [JsonProperty("budget")] public FxTraceBudget Budget { get; set; } = new FxTraceBudget();
    [JsonProperty("layers")] public List<FxTraceLayer> Layers { get; } = new List<FxTraceLayer>();
    [JsonProperty("pos")] public double[] Position { get; set; } = Array.Empty<double>();
}

public sealed class FxTraceSync
{
    [JsonProperty("serverEventMs")] public long ServerEventMs { get; set; }
    [JsonProperty("clientRecvMs")] public long ClientRecvMs { get; set; }
    [JsonProperty("baselineMs")] public long BaselineMs { get; set; }
    [JsonProperty("driftMs")] public long DriftMs { get; set; }
    [JsonProperty("carrierLandMs", NullValueHandling = NullValueHandling.Ignore)] public long? CarrierLandMs { get; set; }
    [JsonProperty("informativeSpawnMs")] public long InformativeSpawnMs { get; set; }
    [JsonProperty("clientSchedulingMs")] public long ClientSchedulingMs { get; set; }
    [JsonProperty("gameplayToVisualMs")] public long GameplayToVisualMs { get; set; }
    [JsonProperty("medianMs")] public long MedianMs { get; set; }
    [JsonProperty("p95Ms")] public long P95Ms { get; set; }
    [JsonProperty("maxMs")] public long MaximumMs { get; set; }
    [JsonProperty("ceilingMs")] public int CeilingMs { get; set; } = 200;

    public static FxTraceSync From(FxSyncMeasurement value)
    {
        return new FxTraceSync
        {
            ServerEventMs = value.Observation.ServerEventMs,
            ClientRecvMs = value.Observation.ClientRecvMs,
            BaselineMs = value.Observation.BaselineMs,
            DriftMs = value.Observation.DriftMs,
            CarrierLandMs = value.CarrierLandMs,
            InformativeSpawnMs = value.InformativeSpawnMs,
            ClientSchedulingMs = value.ClientSchedulingMs,
            GameplayToVisualMs = value.GameplayToVisualMs,
            MedianMs = value.MedianMs,
            P95Ms = value.P95Ms,
            MaximumMs = value.MaximumMs
        };
    }
}

public sealed class FxTraceBudget
{
    [JsonProperty("spent")] public float Spent { get; set; }
    [JsonProperty("perSec")] public int PerSecond { get; set; }
    [JsonProperty("load")] public float Load { get; set; }
}

public sealed class FxTraceLayer
{
    [JsonProperty("role")] public string Role { get; set; } = "";
    [JsonProperty("fired")] public bool Fired { get; set; }
    [JsonProperty("skipReason", NullValueHandling = NullValueHandling.Ignore)] public string? SkipReason { get; set; }
    [JsonProperty("priority")] public string Priority { get; set; } = "";
    [JsonProperty("scale")] public float QuantityScale { get; set; }
    [JsonProperty("reqQty")] public float RequestedQuantity { get; set; }
    [JsonProperty("outQty")] public float OutputQuantity { get; set; }
    [JsonProperty("color")] public string Color { get; set; } = "";
    [JsonProperty("coverage")] public float Coverage { get; set; }
    [JsonProperty("originCoverage")] public float OriginCoverage { get; set; }
    [JsonProperty("extent")] public float Extent { get; set; }
    [JsonProperty("extentClamped")] public bool ExtentClamped { get; set; }
    [JsonProperty("lifetime")] public float Lifetime { get; set; }
    [JsonProperty("delay")] public float Delay { get; set; }
    [JsonProperty("informative")] public bool Informative { get; set; }
    [JsonProperty("size")] public float[] Size { get; set; } = Array.Empty<float>();

    public static string FormatColor(int rgba)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "#{0:x2}{1:x2}{2:x2}{3:x2}",
            (rgba >> 16) & 0xff,
            (rgba >> 8) & 0xff,
            rgba & 0xff,
            (rgba >> 24) & 0xff);
    }
}
