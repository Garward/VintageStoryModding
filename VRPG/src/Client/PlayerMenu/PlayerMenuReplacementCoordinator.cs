using System;
using System.Collections.Generic;
using System.Linq;

namespace VRPG.Client.PlayerMenu;

/// <summary>Owns an atomic pair of semantic dialog registrations, never item state.</summary>
public sealed class PlayerMenuReplacementCoordinator<T> where T : class
{
    private readonly IList<T> dialogs;
    private readonly T inventoryEntry, characterEntry;
    private readonly Func<T, string?> action;
    private readonly Func<T, bool> close;
    private readonly Action<T> bind;
    private (T Dialog, int Index)[]? originals;
    public PlayerMenuReplacementCoordinator(IList<T> dialogs, T inventoryEntry, T characterEntry,
        Func<T, string?> action, Func<T, bool> close, Action<T> bind)
    {
        this.dialogs = dialogs; this.inventoryEntry = inventoryEntry; this.characterEntry = characterEntry;
        this.action = action; this.close = close; this.bind = bind;
    }
    public bool IsInstalled { get; private set; }
    public Exception? LastFailure { get; private set; }
    public bool TryInstall(bool ready)
    {
        if (!ready) return false;
        if (IsInstalled) return true;
        LastFailure = null;
        try
        {
            if (originals != null) Restore();
            if (inventoryEntry == null || characterEntry == null || ReferenceEquals(inventoryEntry, characterEntry)
                || !Matches(inventoryEntry, VanillaMenuActions.Inventory) || !Matches(characterEntry, VanillaMenuActions.Character)
                || dialogs.Any(d => ReferenceEquals(d, inventoryEntry) || ReferenceEquals(d, characterEntry))) return false;
            var inventory = dialogs.Select((d, i) => (Dialog: d, Index: i)).Where(x => Matches(x.Dialog, VanillaMenuActions.Inventory)).ToArray();
            var character = dialogs.Select((d, i) => (Dialog: d, Index: i)).Where(x => Matches(x.Dialog, VanillaMenuActions.Character)).ToArray();
            if (inventory.Length != 1 || character.Length != 1 || ReferenceEquals(inventory[0].Dialog, character[0].Dialog)) return false;
            originals = new[] { inventory[0], character[0] };
            foreach (var original in originals)
                if (!close(original.Dialog)) throw new InvalidOperationException("Native dialog refused to close.");
            // Closing can register another GUI. Resolve identities again before using list indices.
            int inventoryIndex = IndexOf(inventory[0].Dialog), characterIndex = IndexOf(character[0].Dialog);
            if (inventoryIndex < 0 || characterIndex < 0)
                throw new InvalidOperationException("Native dialog ownership changed while closing.");
            dialogs[inventoryIndex] = inventoryEntry;
            dialogs[characterIndex] = characterEntry;
            bind(inventoryEntry); bind(characterEntry);
            IsInstalled = true;
            return true;
        }
        catch (Exception failure)
        {
            LastFailure = failure;
            try { Restore(); }
            catch (Exception rollback) { LastFailure = new AggregateException(failure, rollback); }
            return false;
        }
    }
    public void Restore()
    {
        if (originals == null) return;
        IsInstalled = false;
        // Keep ownership until membership and both handlers have been restored successfully.
        for (int i = dialogs.Count - 1; i >= 0; i--)
            if (ReferenceEquals(dialogs[i], inventoryEntry) || ReferenceEquals(dialogs[i], characterEntry)
                || originals.Any(x => ReferenceEquals(x.Dialog, dialogs[i]))) dialogs.RemoveAt(i);
        foreach (var original in originals.OrderBy(x => x.Index))
            dialogs.Insert(Math.Min(original.Index, dialogs.Count), original.Dialog);
        var failures = new List<Exception>();
        foreach (var original in originals)
            try { bind(original.Dialog); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count > 0) throw new AggregateException("Could not restore native menu handlers.", failures);
        originals = null;
    }
    private bool Matches(T dialog, string expected) => string.Equals(action(dialog), expected, StringComparison.OrdinalIgnoreCase);
    private int IndexOf(T dialog)
    {
        for (int i = 0; i < dialogs.Count; i++) if (ReferenceEquals(dialogs[i], dialog)) return i;
        return -1;
    }
}
