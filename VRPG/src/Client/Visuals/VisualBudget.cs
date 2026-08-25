using System;
using System.Collections.Generic;

namespace VRPG.Client.Visuals;

public enum VisualPriority
{
    Critical = 0,
    Own = 1,
    Others = 2,
    Cosmetic = 3
}

/// <summary>
/// True sliding one-second particle budget. Critical range communication is
/// never scaled and is deliberately not recorded as degradable load.
/// </summary>
public sealed class VisualBudget
{
    private const long WindowMs = 1000;
    private readonly int particlesPerSecond;
    private readonly Queue<Spend> spends = new Queue<Spend>();
    private float spent;

    public bool OwnFirst { get; set; } = true;
    public int ParticlesPerSecond => particlesPerSecond;

    public VisualBudget(int particlesPerSecond = 900)
    {
        this.particlesPerSecond = Math.Max(1, particlesPerSecond);
    }

    public void Record(float particleCost, long nowMs)
    {
        Prune(nowMs);
        float cost = Math.Max(0f, particleCost);
        if (cost <= 0f) return;

        spends.Enqueue(new Spend(nowMs, cost));
        spent += cost;
    }

    public float QuantityScale(VisualPriority priority, long nowMs)
    {
        if (priority == VisualPriority.Critical)
        {
            return 1f;
        }

        Prune(nowMs);
        float load = spent / particlesPerSecond;
        if (!OwnFirst)
        {
            return Math.Clamp(1f - load, 0f, 1f);
        }

        return priority switch
        {
            VisualPriority.Cosmetic => Math.Clamp(1f - 2f * load, 0f, 1f),
            VisualPriority.Others => Math.Clamp(1f - 2f * Math.Max(0f, load - 0.5f), 0f, 1f),
            _ => Math.Clamp(1f - 5f * Math.Max(0f, load - 0.8f), 0f, 1f)
        };
    }

    public VisualBudgetSnapshot Snapshot(long nowMs)
    {
        Prune(nowMs);
        return new VisualBudgetSnapshot(spent, particlesPerSecond, spent / particlesPerSecond);
    }

    private void Prune(long nowMs)
    {
        long cutoff = nowMs - WindowMs;
        while (spends.Count > 0 && spends.Peek().AtMs <= cutoff)
        {
            spent -= spends.Dequeue().Cost;
        }

        if (spent < 0.0001f) spent = 0f;
    }

    private readonly record struct Spend(long AtMs, float Cost);
}

public readonly record struct VisualBudgetSnapshot(float Spent, int PerSecond, float Load);
