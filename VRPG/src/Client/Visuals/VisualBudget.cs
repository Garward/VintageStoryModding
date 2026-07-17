using System;

namespace VRPG.Client.Visuals;

public enum VisualPriority
{
    Critical = 0,
    Own = 1,
    Others = 2,
    Cosmetic = 3
}

/// <summary>
/// Sliding one-second particle budget. Non-critical priorities degrade in
/// order (cosmetic, then others, then own) under the own-first policy, or all
/// together under the uniform policy. Critical (P0) always returns 1 — and P0
/// renderers do not consult the budget at all.
/// </summary>
public sealed class VisualBudget
{
    private readonly int particlesPerSecond;
    private long windowStartMs;
    private float spent;

    public bool OwnFirst { get; set; } = true;

    public VisualBudget(int particlesPerSecond = 900)
    {
        this.particlesPerSecond = Math.Max(1, particlesPerSecond);
    }

    public void Record(float particleCost, long nowMs)
    {
        RollWindow(nowMs);
        spent += Math.Max(0f, particleCost);
    }

    public float QuantityScale(VisualPriority priority, long nowMs)
    {
        if (priority == VisualPriority.Critical)
        {
            return 1f;
        }

        RollWindow(nowMs);
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

    private void RollWindow(long nowMs)
    {
        if (nowMs - windowStartMs >= 1000)
        {
            windowStartMs = nowMs;
            spent = 0f;
        }
    }
}
