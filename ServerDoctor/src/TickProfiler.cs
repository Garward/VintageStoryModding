using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ServerDoctor;

internal sealed class TickProfiler
{
    private const int TopN = 20;
    private const int OverlayTopN = 16;
    private const int OverlayGeneralTopN = 28;

    private readonly ICoreServerAPI api;
    private readonly Action<ServerDoctorOverlayPacket> publishOverlay;
    private readonly object gate = new object();
    private readonly Dictionary<string, TickCost> frameCosts = new Dictionary<string, TickCost>();
    private readonly Dictionary<string, TickCost> callbackCosts = new Dictionary<string, TickCost>();
    private readonly Dictionary<string, TickCost> databaseCosts = new Dictionary<string, TickCost>();
    private readonly Dictionary<BlockKey, TickCost> blockCosts = new Dictionary<BlockKey, TickCost>();

    private ProfileEntryRange lastRootEntry;
    private bool previousFrameProfilerEnabled;
    private bool frameProfilerApplied;
    private bool frameProfilerDesired;
    private long startedStamp;
    private long lastReportStamp;
    private long observedTicks;
    private long observedFrameTicks;
    private long observedActiveTicks;
    private long maxActiveTickTicks;

    public bool Enabled { get; private set; }

    public bool FrameProfilerStateSettled
    {
        get
        {
            lock (gate)
            {
                return !frameProfilerApplied;
            }
        }
    }

    public TickProfiler(ICoreServerAPI api, Action<ServerDoctorOverlayPacket> publishOverlay)
    {
        this.api = api;
        this.publishOverlay = publishOverlay;
    }

    public void Start()
    {
        lock (gate)
        {
            if (Enabled) return;

            ResetLocked();
            frameProfilerDesired = true;
            startedStamp = Stopwatch.GetTimestamp();
            lastReportStamp = startedStamp;
            Enabled = true;
        }

        publishOverlay?.Invoke(ServerDoctorOverlayPacketFactory.Empty(true));
    }

    public string Stop()
    {
        lock (gate)
        {
            if (!Enabled) return "ServerDoctor tick profiler is already stopped.";

            Enabled = false;
            frameProfilerDesired = false;
            string report = BuildReportLocked("final");
            publishOverlay?.Invoke(ServerDoctorOverlayPacketFactory.Empty(false));
            ResetLocked();
            return report;
        }
    }

    public string Status()
    {
        lock (gate)
        {
            if (!Enabled) return "ServerDoctor tick profiler: stopped.";

            double seconds = SecondsSince(startedStamp);
            return string.Format("ServerDoctor tick profiler: running for {0:0.0}s, observed {1} server ticks.", seconds, observedTicks);
        }
    }

    public string DumpAndReset(string label)
    {
        lock (gate)
        {
            string report = BuildReportLocked(label);
            publishOverlay?.Invoke(BuildOverlayPacketLocked());
            ResetLocked();
            startedStamp = Stopwatch.GetTimestamp();
            lastReportStamp = startedStamp;
            return report;
        }
    }

    public void CollectPreviousFrame()
    {
        if (!Enabled) return;

        ProfileEntryRange root = api.World.FrameProfiler.PrevRootEntry;
        if (root == null || ReferenceEquals(root, lastRootEntry)) return;

        lock (gate)
        {
            if (!Enabled || ReferenceEquals(root, lastRootEntry)) return;

            lastRootEntry = root;
            observedTicks++;

            long sleepTicks = SumMarkTicks(root, "sleep");
            long activeTicks = Math.Max(0, root.ElapsedTicks - sleepTicks);
            observedFrameTicks += root.ElapsedTicks;
            observedActiveTicks += activeTicks;
            if (activeTicks > maxActiveTickTicks) maxActiveTickTicks = activeTicks;

            RecordFrameEntries(root, "frame");
        }
    }

    public void MaybeAutoReport()
    {
        if (!Enabled) return;

        long now = Stopwatch.GetTimestamp();
        if (TicksToSeconds(now - lastReportStamp) < 10) return;

        string report;
        lock (gate)
        {
            if (!Enabled || TicksToSeconds(now - lastReportStamp) < 10) return;
            report = BuildReportLocked("10s");
            publishOverlay?.Invoke(BuildOverlayPacketLocked());
            ResetLocked();
            startedStamp = now;
            lastReportStamp = now;
        }

        LogMultiline(report);
    }

    public void RecordCallback(string category, string name, long elapsedTicks)
    {
        if (!Enabled || elapsedTicks <= 0 || string.IsNullOrEmpty(name)) return;
        if (name.StartsWith("ServerDoctor.", StringComparison.Ordinal)) return;

        lock (gate)
        {
            if (!Enabled) return;
            Record(callbackCosts, category + " " + name, elapsedTicks, 1);
        }
    }

    public void RecordBlockCallback(int x, int y, int z, string name, long elapsedTicks)
    {
        if (!Enabled || elapsedTicks <= 0) return;
        if (!string.IsNullOrEmpty(name) && name.StartsWith("ServerDoctor.", StringComparison.Ordinal)) return;

        lock (gate)
        {
            if (!Enabled) return;

            BlockKey key = new BlockKey(x, y, z);
            if (!blockCosts.TryGetValue(key, out var cost))
            {
                cost = new TickCost();
                blockCosts[key] = cost;
            }

            cost.ElapsedTicks += elapsedTicks;
            cost.Calls++;
            cost.Label = name;
        }
    }

    public void RecordDatabaseOperation(string name, long elapsedTicks)
    {
        if (!Enabled || elapsedTicks <= 0 || string.IsNullOrEmpty(name)) return;

        lock (gate)
        {
            if (!Enabled) return;
            Record(databaseCosts, "database " + name, elapsedTicks, 1);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (Enabled)
            {
                frameProfilerDesired = false;
            }
            Enabled = false;
            publishOverlay?.Invoke(ServerDoctorOverlayPacketFactory.Empty(false));
            ResetLocked();
        }
    }

    public void ApplyFrameProfilerStateAtFrameStart()
    {
        lock (gate)
        {
            if (frameProfilerDesired)
            {
                if (!frameProfilerApplied)
                {
                    previousFrameProfilerEnabled = api.World.FrameProfiler.Enabled;
                    api.World.FrameProfiler.Enabled = true;
                    frameProfilerApplied = true;
                }
            }
            else if (frameProfilerApplied)
            {
                api.World.FrameProfiler.Enabled = previousFrameProfilerEnabled;
                frameProfilerApplied = false;
            }
        }
    }

    private string BuildReportLocked(string label)
    {
        double seconds = SecondsSince(startedStamp);
        if (observedTicks == 0)
        {
            return "[ServerDoctor/Tick] " + label + ": no server ticks observed yet.";
        }

        double activeMs = TicksToMilliseconds(observedActiveTicks);
        double frameMs = TicksToMilliseconds(observedFrameTicks);
        double avgActiveMs = activeMs / observedTicks;
        double avgFrameMs = frameMs / observedTicks;
        double maxActiveMs = TicksToMilliseconds(maxActiveTickTicks);
        double observedTickRate = ObservedTickRate(seconds);
        double targetTickRate = TargetTickRate();

        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("[ServerDoctor/Tick] === {0} profile: {1:0.0}s, ticks={2}, tps={3:0.0}/{4:0.0}, active avg={5:0.00}ms, wall avg={6:0.00}ms, active max={7:0.00}ms ===",
            label, seconds, observedTicks, observedTickRate, targetTickRate, avgActiveMs, avgFrameMs, maxActiveMs);
        sb.AppendLine();

        AppendTop(sb, "frame segments", frameCosts, observedTicks, observedActiveTicks);
        AppendTop(sb, "exact tick callbacks", callbackCosts, observedTicks, observedActiveTicks);
        AppendTop(sb, "database/save operations", databaseCosts, observedTicks, observedActiveTicks);
        AppendTopBlockOffenders(sb);

        return sb.ToString().TrimEnd();
    }

    private void AppendTopBlockOffenders(StringBuilder sb)
    {
        var top = blockCosts
            .OrderByDescending(kv => kv.Value.ElapsedTicks)
            .Take(OverlayTopN)
            .ToList();

        if (top.Count == 0)
        {
            sb.AppendLine("[ServerDoctor/Tick] Top block offenders: none");
            return;
        }

        sb.AppendLine("[ServerDoctor/Tick] Top block offenders:");
        foreach (var kv in top)
        {
            TickCost cost = kv.Value;
            double msPerTick = TicksToMilliseconds(cost.ElapsedTicks) / Math.Max(1, observedTicks);
            double percent = observedActiveTicks <= 0 ? 0 : cost.ElapsedTicks * 100.0 / observedActiveTicks;
            Vec3i local = new BlockPos(kv.Key.X, kv.Key.Y, kv.Key.Z).ToLocalPosition(api);
            sb.AppendFormat("[ServerDoctor/Tick]  {0,7:0.00} ms/tick {1,6:0.0}% {2,7} calls  /tp {3} {4} {5}  {6}",
                msPerTick, percent, cost.Calls, local.X, local.Y, local.Z, cost.Label ?? "?");
            sb.AppendLine();
        }
    }

    private ServerDoctorOverlayPacket BuildOverlayPacketLocked()
    {
        ServerDoctorOverlayPacket packet = new ServerDoctorOverlayPacket
        {
            Enabled = Enabled,
            CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ObservedTickRate = (float)ObservedTickRate(SecondsSince(startedStamp)),
            TargetTickRate = (float)TargetTickRate(),
            AverageActiveMilliseconds = observedTicks <= 0 ? 0 : (float)(TicksToMilliseconds(observedActiveTicks) / observedTicks),
            AverageFrameMilliseconds = observedTicks <= 0 ? 0 : (float)(TicksToMilliseconds(observedFrameTicks) / observedTicks),
            MaxActiveMilliseconds = (float)TicksToMilliseconds(maxActiveTickTicks)
        };

        if (!Enabled || observedTicks <= 0) return packet;

        AddOverlayCostEntries(packet, databaseCosts, OverlayGeneralTopN);
        AddOverlayCostEntries(packet, frameCosts, OverlayGeneralTopN);
        AddOverlayCostEntries(packet, callbackCosts, OverlayGeneralTopN);

        var top = blockCosts
            .OrderByDescending(kv => kv.Value.ElapsedTicks)
            .Take(OverlayTopN);

        foreach (var kv in top)
        {
            TickCost cost = kv.Value;
            packet.Entries.Add(new ServerDoctorOverlayEntry
            {
                X = kv.Key.X,
                Y = kv.Key.Y,
                Z = kv.Key.Z,
                MillisecondsPerTick = (float)(TicksToMilliseconds(cost.ElapsedTicks) / Math.Max(1, observedTicks)),
                PercentOfActiveTick = observedActiveTicks <= 0 ? 0 : (float)(cost.ElapsedTicks * 100.0 / observedActiveTicks),
                Calls = cost.Calls > int.MaxValue ? int.MaxValue : (int)cost.Calls,
                Label = cost.Label ?? "block tick listener",
                HasCoordinates = true
            });
        }

        return packet;
    }

    private void AddOverlayCostEntries(ServerDoctorOverlayPacket packet, Dictionary<string, TickCost> costs, int max)
    {
        foreach (var kv in costs
            .Where(kv => !ShouldHide(kv.Key))
            .OrderByDescending(kv => kv.Value.ElapsedTicks)
            .Take(max))
        {
            TickCost cost = kv.Value;
            packet.Entries.Add(new ServerDoctorOverlayEntry
            {
                MillisecondsPerTick = (float)(TicksToMilliseconds(cost.ElapsedTicks) / Math.Max(1, observedTicks)),
                PercentOfActiveTick = observedActiveTicks <= 0 ? 0 : (float)(cost.ElapsedTicks * 100.0 / observedActiveTicks),
                Calls = cost.Calls > int.MaxValue ? int.MaxValue : (int)cost.Calls,
                Label = kv.Key,
                HasCoordinates = false
            });
        }
    }

    private void AppendTop(StringBuilder sb, string title, Dictionary<string, TickCost> costs, long ticks, long activeTicks)
    {
        var top = costs
            .Where(kv => !ShouldHide(kv.Key))
            .OrderByDescending(kv => kv.Value.ElapsedTicks)
            .Take(TopN)
            .ToList();

        if (top.Count == 0)
        {
            sb.AppendFormat("[ServerDoctor/Tick] Top {0}: none", title);
            sb.AppendLine();
            return;
        }

        sb.AppendFormat("[ServerDoctor/Tick] Top {0}:", title);
        sb.AppendLine();

        foreach (var kv in top)
        {
            TickCost cost = kv.Value;
            double totalMs = TicksToMilliseconds(cost.ElapsedTicks);
            double msPerTick = totalMs / Math.Max(1, ticks);
            double usPerCall = TicksToMilliseconds(cost.ElapsedTicks) * 1000.0 / Math.Max(1, cost.Calls);
            double percent = activeTicks <= 0 ? 0 : cost.ElapsedTicks * 100.0 / activeTicks;

            sb.AppendFormat("[ServerDoctor/Tick]  {0,7:0.00} ms/tick {1,6:0.0}% {2,7} calls {3,8:0.0} us/call  {4}",
                msPerTick, percent, cost.Calls, usPerCall, kv.Key);
            sb.AppendLine();
        }
    }

    private void RecordFrameEntries(ProfileEntryRange entry, string path)
    {
        if (entry == null) return;

        string nextPath = path;
        if (!string.IsNullOrEmpty(entry.Code) && entry.Code != "all")
        {
            nextPath = path + "/" + NormalizeCode(entry.Code);
            Record(frameCosts, nextPath, entry.ElapsedTicks, Math.Max(1, entry.CallCount));
        }

        if (entry.Marks != null)
        {
            foreach (var mark in entry.Marks)
            {
                Record(frameCosts, nextPath + "/" + NormalizeCode(mark.Key),
                    mark.Value.ElapsedTicks, Math.Max(1, mark.Value.CallCount));
            }
        }

        if (entry.ChildRanges == null) return;

        foreach (var child in entry.ChildRanges.Values)
        {
            RecordFrameEntries(child, nextPath);
        }
    }

    private long SumMarkTicks(ProfileEntryRange entry, string code)
    {
        if (entry == null) return 0;

        long total = 0;
        if (entry.Marks != null)
        {
            foreach (var mark in entry.Marks)
            {
                if (mark.Key == code) total += mark.Value.ElapsedTicks;
            }
        }

        if (entry.ChildRanges != null)
        {
            foreach (var child in entry.ChildRanges.Values)
            {
                total += SumMarkTicks(child, code);
            }
        }

        return total;
    }

    private void Record(Dictionary<string, TickCost> costs, string key, long elapsedTicks, long calls)
    {
        key = key ?? "?";
        if (!costs.TryGetValue(key, out var cost))
        {
            cost = new TickCost();
            costs[key] = cost;
        }

        cost.ElapsedTicks += elapsedTicks;
        cost.Calls += calls;
    }

    private void ResetLocked()
    {
        frameCosts.Clear();
        callbackCosts.Clear();
        databaseCosts.Clear();
        blockCosts.Clear();
        lastRootEntry = null;
        observedTicks = 0;
        observedFrameTicks = 0;
        observedActiveTicks = 0;
        maxActiveTickTicks = 0;
    }

    private static bool ShouldHide(string key)
    {
        return key.EndsWith("/sleep", StringComparison.Ordinal)
            || key.EndsWith("/end", StringComparison.Ordinal)
            || key.Contains("ServerDoctor.", StringComparison.Ordinal);
    }

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "?";

        if (code.StartsWith("ss-tick-", StringComparison.Ordinal))
        {
            return "server-system " + code.Substring("ss-tick-".Length);
        }
        if (code.StartsWith("gmle", StringComparison.Ordinal))
        {
            return "game-listener " + code.Substring("gmle".Length);
        }
        if (code.StartsWith("gmlb", StringComparison.Ordinal))
        {
            return "block-listener " + code.Substring("gmlb".Length);
        }
        if (code.StartsWith("dce", StringComparison.Ordinal))
        {
            return "delayed-callback " + code.Substring("dce".Length);
        }
        if (code.StartsWith("dcb", StringComparison.Ordinal))
        {
            return "delayed-block-callback " + code.Substring("dcb".Length);
        }
        if (code.StartsWith("sdcb", StringComparison.Ordinal))
        {
            return "single-delayed-block-callback " + code.Substring("sdcb".Length);
        }

        return code;
    }

    private void LogMultiline(string report)
    {
        string[] lines = report.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            api.Logger.Notification(lines[i]);
        }
    }

    private double SecondsSince(long stamp)
    {
        return TicksToSeconds(Stopwatch.GetTimestamp() - stamp);
    }

    private double ObservedTickRate(double seconds)
    {
        return seconds <= 0 ? 0 : observedTicks / seconds;
    }

    private double TargetTickRate()
    {
        double tickTime = api.Server?.Config?.TickTime ?? 33.333332;
        return tickTime <= 0 ? 30.0 : 1000.0 / tickTime;
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / Stopwatch.Frequency;
    }

    private static double TicksToSeconds(long ticks)
    {
        return ticks / (double)Stopwatch.Frequency;
    }

    private sealed class TickCost
    {
        public long ElapsedTicks;
        public long Calls;
        public string Label;
    }

    private readonly struct BlockKey : IEquatable<BlockKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public BlockKey(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(BlockKey other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is BlockKey other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ (Y * 31) ^ Z;
    }
}

internal static class ServerDoctorOverlayPacketFactory
{
    public static ServerDoctorOverlayPacket Empty(bool enabled)
    {
        return new ServerDoctorOverlayPacket
        {
            Enabled = enabled,
            CreatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
