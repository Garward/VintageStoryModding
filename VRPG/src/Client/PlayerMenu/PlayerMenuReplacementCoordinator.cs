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
    public bool RestorationPending => originals != null && !IsInstalled;
    public Exception? LastFailure { get; private set; }
    public bool TryInstall(bool ready)
    {
        LastFailure = null;
        try
        {
            if (RestorationPending) Restore();
            if (!ready) return false;
            if (IsInstalled) return true;
            if (inventoryEntry == null || characterEntry == null || ReferenceEquals(inventoryEntry, characterEntry)
                || !Matches(inventoryEntry, VanillaMenuActions.Inventory) || !Matches(characterEntry, VanillaMenuActions.Character)
                || dialogs.Any(d => ReferenceEquals(d, inventoryEntry) || ReferenceEquals(d, characterEntry))) return false;
            var inventory = Owners(VanillaMenuActions.Inventory);
            var character = Owners(VanillaMenuActions.Character);
            if (inventory.Length != 1 || character.Length != 1 || ReferenceEquals(inventory[0].Dialog, character[0].Dialog)) return false;
            originals = new[] { inventory[0], character[0] };
            foreach (var original in originals)
                if (!close(original.Dialog)) throw new InvalidOperationException("Native dialog refused to close.");
            // Closing can change registry membership or semantic identity. Revalidate the entire pair.
            var closedInventory = Owners(VanillaMenuActions.Inventory);
            var closedCharacter = Owners(VanillaMenuActions.Character);
            if (closedInventory.Length != 1 || closedCharacter.Length != 1
                || !ReferenceEquals(closedInventory[0].Dialog, inventory[0].Dialog)
                || !ReferenceEquals(closedCharacter[0].Dialog, character[0].Dialog)
                || ReferenceEquals(closedInventory[0].Dialog, closedCharacter[0].Dialog))
                throw new InvalidOperationException("Native dialog ownership changed while closing.");
            originals = new[] { closedInventory[0], closedCharacter[0] };
            dialogs[closedInventory[0].Index] = inventoryEntry;
            dialogs[closedCharacter[0].Index] = characterEntry;
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
        // Replace in situ: other dialogs may have registered or moved while our entries were installed.
        RestoreAtEntry(originals[0].Dialog, inventoryEntry);
        RestoreAtEntry(originals[1].Dialog, characterEntry);
        foreach (var original in originals.OrderBy(x => x.Index))
        {
            if (IndexOf(original.Dialog) >= 0) continue;
            int index = Math.Min(original.Index, dialogs.Count);
            // A missing entry uses its post-close position, bounded by the surviving paired owner.
            foreach (var other in originals)
            {
                int otherIndex = IndexOf(other.Dialog);
                if (otherIndex < 0) continue;
                if (other.Index < original.Index) index = Math.Max(index, otherIndex + 1);
                else if (other.Index > original.Index) index = Math.Min(index, otherIndex);
            }
            dialogs.Insert(index, original.Dialog);
        }
        var failures = new List<Exception>();
        foreach (var original in originals)
            try { bind(original.Dialog); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count > 0) throw new AggregateException("Could not restore native menu handlers.", failures);
        originals = null;
    }
    private void RestoreAtEntry(T original, T entry)
    {
        int index = IndexOf(entry);
        if (index < 0) return;
        if (IndexOf(original) >= 0) dialogs.RemoveAt(index);
        else dialogs[index] = original;
    }
    private (T Dialog, int Index)[] Owners(string semanticAction) => dialogs.Select((d, i) => (Dialog: d, Index: i))
        .Where(x => Matches(x.Dialog, semanticAction)).ToArray();
    private bool Matches(T dialog, string expected) => string.Equals(action(dialog), expected, StringComparison.OrdinalIgnoreCase);
    private int IndexOf(T dialog)
    {
        for (int i = 0; i < dialogs.Count; i++) if (ReferenceEquals(dialogs[i], dialog)) return i;
        return -1;
    }
}
