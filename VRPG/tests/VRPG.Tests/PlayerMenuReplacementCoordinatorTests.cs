using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VRPG.Client.PlayerMenu;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuReplacementCoordinatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RegistryMutationFailureRestoresTheCompleteOriginalPair(int failOnMutation)
    {
        var f = new Fixture(); f.Dialogs.FailOnSet = failOnMutation;
        Assert.False(f.Coordinator.TryInstall(true));
        Assert.Equal(new[] { f.Other, f.Inventory, f.Character }, f.Dialogs);
        Assert.Same(f.Inventory, f.Handlers[VanillaMenuActions.Inventory]);
        Assert.Same(f.Character, f.Handlers[VanillaMenuActions.Character]);
    }

    [Fact]
    public void RestorationTriesBothHandlersAndRetainsOwnershipForRetryIfOneThrows()
    {
        var f = new Fixture(); Assert.True(f.Coordinator.TryInstall(true));
        f.FailAt = "bind-inventorydialog";
        Assert.Throws<AggregateException>(() => f.Coordinator.Restore());
        Assert.Equal(new[] { f.Other, f.Inventory, f.Character }, f.Dialogs);
        Assert.Same(f.Character, f.Handlers[VanillaMenuActions.Character]);
        f.Coordinator.Restore();
        Assert.Same(f.Inventory, f.Handlers[VanillaMenuActions.Inventory]);
    }
    [Fact]
    public void ClosingAnOwnerMayRegisterAnotherDialogWithoutReplacingThatDialog()
    {
        var f = new Fixture();
        var late = new Dialog("late");
        f.OnClose = () => { f.Dialogs.Insert(0, late); f.OnClose = null; };
        Assert.True(f.Coordinator.TryInstall(true));
        Assert.Contains(late, f.Dialogs);
        Assert.Contains(f.Other, f.Dialogs);
        Assert.Contains(f.InventoryEntry, f.Dialogs);
        Assert.Contains(f.CharacterEntry, f.Dialogs);
        Assert.DoesNotContain(f.Inventory, f.Dialogs);
        Assert.DoesNotContain(f.Character, f.Dialogs);
        f.Coordinator.Restore();
        Assert.Contains(late, f.Dialogs);
        Assert.Contains(f.Other, f.Dialogs);
        Assert.Equal(4, f.Dialogs.Count);
    }
    [Fact]
    public void SwapPreservesIndicesClosesOwnersAndRebindsBothActions()
    {
        var f = new Fixture();
        Assert.True(f.Coordinator.TryInstall(true));
        Assert.Equal(new[] { f.Other, f.InventoryEntry, f.CharacterEntry }, f.Dialogs);
        Assert.Equal(new[] { "close-inventorydialog", "close-characterdialog", "bind-entry-inventorydialog", "bind-entry-characterdialog" }, f.Events);
        Assert.True(f.Coordinator.TryInstall(true));
        Assert.Equal(4, f.Events.Count);
        f.Coordinator.Restore();
        Assert.Equal(new[] { f.Other, f.Inventory, f.Character }, f.Dialogs);
        Assert.Same(f.Inventory, f.Handlers[VanillaMenuActions.Inventory]);
        Assert.Same(f.Character, f.Handlers[VanillaMenuActions.Character]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-adapter")]
    [InlineData("not-ready")]
    public void IncompletePairOrReadinessNeverMutatesRegistry(string defect)
    {
        var f = new Fixture();
        if (defect == "missing") f.Dialogs.Remove(f.Character);
        if (defect == "duplicate") f.Dialogs.Add(new(VanillaMenuActions.Inventory));
        if (defect == "wrong-adapter") f.CharacterEntry.Action = "handbook";
        var before = f.Dialogs.ToArray();
        Assert.False(f.Coordinator.TryInstall(defect != "not-ready"));
        Assert.Equal(before, f.Dialogs);
        Assert.Empty(f.Events);
    }

    [Theory]
    [InlineData("close-inventorydialog")]
    [InlineData("close-characterdialog")]
    [InlineData("bind-entry-inventorydialog")]
    [InlineData("bind-entry-characterdialog")]
    public void FailureRestoresBothOriginalOwnersAndHandlers(string failAt)
    {
        var f = new Fixture { FailAt = failAt };
        Assert.False(f.Coordinator.TryInstall(true));
        Assert.Equal(new[] { f.Other, f.Inventory, f.Character }, f.Dialogs);
        Assert.Same(f.Inventory, f.Handlers[VanillaMenuActions.Inventory]);
        Assert.Same(f.Character, f.Handlers[VanillaMenuActions.Character]);
        Assert.False(f.Coordinator.IsInstalled);
        Assert.True(f.Coordinator.TryInstall(true));
    }

    private sealed class Dialog(string action, bool entry = false)
    {
        public string Action = action;
        public bool Entry = entry;
    }
    private sealed class Fixture
    {
        public readonly Dialog Other = new("handbook"), Inventory = new(VanillaMenuActions.Inventory), Character = new(VanillaMenuActions.Character);
        public readonly Dialog InventoryEntry = new(VanillaMenuActions.Inventory, true), CharacterEntry = new(VanillaMenuActions.Character, true);
        public readonly DialogCollection Dialogs;
        public readonly List<string> Events = new();
        public readonly Dictionary<string, Dialog> Handlers = new();
        public string? FailAt;
        public Action? OnClose;
        public readonly PlayerMenuReplacementCoordinator<Dialog> Coordinator;
        public Fixture()
        {
            Dialogs = new() { Other, Inventory, Character };
            Handlers[Inventory.Action] = Inventory; Handlers[Character.Action] = Character;
            Coordinator = new(Dialogs, InventoryEntry, CharacterEntry, d => d.Action,
                d => { Record("close-" + d.Action); OnClose?.Invoke(); return true; },
                d => { Record("bind-" + (d.Entry ? "entry-" : "") + d.Action); Handlers[d.Action] = d; });
        }
        private void Record(string value)
        {
            Events.Add(value);
            if (FailAt == value) { FailAt = null; throw new InvalidOperationException(value); }
        }
    }
    private sealed class DialogCollection : Collection<Dialog>
    {
        public int FailOnSet;
        protected override void SetItem(int index, Dialog value)
        {
            if (FailOnSet > 0 && --FailOnSet == 0) throw new InvalidOperationException("Registry mutation failed.");
            base.SetItem(index, value);
        }
    }
}
