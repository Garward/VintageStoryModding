using System;
using System.Collections.Generic;

namespace VRPG.Client.Visuals;

/// <summary>
/// Estimates the per-session client/server clock-plus-transit floor and reports
/// visual drift relative to it. It never claims absolute one-way latency.
/// </summary>
public sealed class FxSyncTracker
{
    private const long BaselineWindowMs = 60_000;
    private readonly Queue<OffsetSample> offsets = new Queue<OffsetSample>();
    private readonly Dictionary<string, List<long>> deltasBySkill = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);

    public FxSyncObservation? Observe(string skillCode, long serverEventMs, long clientRecvMs)
    {
        if (serverEventMs <= 0) return null;

        Prune(clientRecvMs);
        long observedOffset = clientRecvMs - serverEventMs;
        offsets.Enqueue(new OffsetSample(clientRecvMs, observedOffset));
        long baseline = observedOffset;
        foreach (OffsetSample sample in offsets)
        {
            baseline = Math.Min(baseline, sample.OffsetMs);
        }

        return new FxSyncObservation(
            skillCode ?? "",
            serverEventMs,
            clientRecvMs,
            baseline,
            Math.Max(0, observedOffset - baseline));
    }

    public FxSyncMeasurement Complete(FxSyncObservation observation, long informativeSpawnMs, long? carrierLandMs)
    {
        long schedulingMs = Math.Max(0, informativeSpawnMs - observation.ClientRecvMs);
        long relativeDeltaMs = observation.DriftMs + schedulingMs;
        if (!deltasBySkill.TryGetValue(observation.SkillCode, out List<long>? values))
        {
            values = new List<long>();
            deltasBySkill[observation.SkillCode] = values;
        }

        values.Add(relativeDeltaMs);
        values.Sort();
        long median = Percentile(values, 0.5);
        long p95 = Percentile(values, 0.95);
        return new FxSyncMeasurement(
            observation,
            carrierLandMs,
            informativeSpawnMs,
            schedulingMs,
            relativeDeltaMs,
            median,
            p95,
            values[^1]);
    }

    private void Prune(long nowMs)
    {
        long cutoff = nowMs - BaselineWindowMs;
        while (offsets.Count > 0 && offsets.Peek().ClientRecvMs < cutoff)
        {
            offsets.Dequeue();
        }
    }

    private static long Percentile(List<long> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        int index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private readonly record struct OffsetSample(long ClientRecvMs, long OffsetMs);
}

public sealed record FxSyncObservation(
    string SkillCode,
    long ServerEventMs,
    long ClientRecvMs,
    long BaselineMs,
    long DriftMs);

public sealed record FxSyncMeasurement(
    FxSyncObservation Observation,
    long? CarrierLandMs,
    long InformativeSpawnMs,
    long ClientSchedulingMs,
    long GameplayToVisualMs,
    long MedianMs,
    long P95Ms,
    long MaximumMs);
