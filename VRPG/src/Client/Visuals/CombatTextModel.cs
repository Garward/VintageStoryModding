using System.Collections.Generic;

namespace VRPG.Client.Visuals;

public sealed class CombatTextSettings
{
    public int MergeWindowMs { get; set; } = 500;
    public int MaxEntries { get; set; } = 20;
    public int NumberLifetimeMs { get; set; } = 1100;
    public int WordLifetimeMs { get; set; } = 1400;
    public bool MergeNumbers { get; set; } = true;
}

public enum CombatTextKind
{
    Number,
    Word
}

public sealed class CombatTextEntry
{
    public long TargetEntityId;
    public CombatTextKind Kind;
    public byte DamageType;
    public float Amount;
    public string Word = "";
    public bool Crit;
    public int MergeCount = 1;
    public long CreatedMs;
    public long LastMergeMs;
    public long ExpiresAtMs;
    public int Priority;
    public double AnchorX, AnchorY, AnchorZ;
}

/// <summary>
/// Merge, cap, and priority rules for floating combat text. Engine-free so the
/// spam behavior is unit-testable; the HUD renderer only reads Entries.
/// </summary>
public sealed class CombatTextModel
{
    private readonly CombatTextSettings settings;
    private readonly List<CombatTextEntry> entries = new List<CombatTextEntry>();

    public CombatTextModel(CombatTextSettings settings)
    {
        this.settings = settings;
    }

    public IReadOnlyList<CombatTextEntry> Entries => entries;

    public void AddNumber(long targetId, byte damageType, float amount, bool crit, double x, double y, double z, long nowMs)
    {
        if (settings.MergeNumbers)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                CombatTextEntry existing = entries[i];
                if (existing.Kind == CombatTextKind.Number
                    && existing.TargetEntityId == targetId
                    && existing.DamageType == damageType
                    && nowMs - existing.LastMergeMs <= settings.MergeWindowMs)
                {
                    existing.Amount += amount;
                    existing.MergeCount++;
                    existing.Crit |= crit;
                    existing.Priority = existing.Crit ? 10 : existing.Priority;
                    existing.LastMergeMs = nowMs;
                    existing.ExpiresAtMs = nowMs + settings.NumberLifetimeMs;
                    existing.AnchorX = x;
                    existing.AnchorY = y;
                    existing.AnchorZ = z;
                    return;
                }
            }
        }

        Insert(new CombatTextEntry
        {
            TargetEntityId = targetId,
            Kind = CombatTextKind.Number,
            DamageType = damageType,
            Amount = amount,
            Crit = crit,
            Priority = crit ? 10 : 1,
            CreatedMs = nowMs,
            LastMergeMs = nowMs,
            ExpiresAtMs = nowMs + settings.NumberLifetimeMs,
            AnchorX = x,
            AnchorY = y,
            AnchorZ = z
        }, nowMs);
    }

    public void AddWord(long targetId, string word, int priority, double x, double y, double z, long nowMs)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            CombatTextEntry existing = entries[i];
            if (existing.Kind == CombatTextKind.Word && existing.TargetEntityId == targetId)
            {
                if (priority < existing.Priority)
                {
                    return;
                }

                existing.Word = word;
                existing.Priority = priority;
                existing.CreatedMs = nowMs;
                existing.LastMergeMs = nowMs;
                existing.ExpiresAtMs = nowMs + settings.WordLifetimeMs;
                existing.AnchorX = x;
                existing.AnchorY = y;
                existing.AnchorZ = z;
                return;
            }
        }

        Insert(new CombatTextEntry
        {
            TargetEntityId = targetId,
            Kind = CombatTextKind.Word,
            Word = word,
            Priority = priority,
            CreatedMs = nowMs,
            LastMergeMs = nowMs,
            ExpiresAtMs = nowMs + settings.WordLifetimeMs,
            AnchorX = x,
            AnchorY = y,
            AnchorZ = z
        }, nowMs);
    }

    public void Tick(long nowMs)
    {
        entries.RemoveAll(entry => entry.ExpiresAtMs <= nowMs);
    }

    private void Insert(CombatTextEntry entry, long nowMs)
    {
        entries.Add(entry);
        while (entries.Count > settings.MaxEntries)
        {
            int victim = 0;
            for (int i = 1; i < entries.Count; i++)
            {
                CombatTextEntry candidate = entries[i];
                CombatTextEntry current = entries[victim];
                if (candidate.Priority < current.Priority
                    || (candidate.Priority == current.Priority && candidate.CreatedMs < current.CreatedMs))
                {
                    victim = i;
                }
            }

            entries.RemoveAt(victim);
        }
    }
}
