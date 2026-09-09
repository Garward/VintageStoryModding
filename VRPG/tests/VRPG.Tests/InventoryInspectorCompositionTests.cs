using System;
using System.Linq;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Xunit;

namespace VRPG.Tests;

[Collection("Inventory Cairo")]
public sealed class InventoryInspectorCompositionTests
{
    [Fact]
    public void NativeInspectorIncludesConditionFromRealAffixProducerBesideItsEffect()
    {
        var f = new GearReadinessValidatorTests.Fixture();
        var gear = GearReadinessValidatorTests.Instance() with { Affixes = new[] { new GearAffixRoll("vrpg:flat-power", 1,
            new[] { new GearModifierRoll("vrpg:strength", "add", 6) }) } };
        string id = f.Insert(GearItemStateCodec.Encode(gear));
        using var page = InventoryPage.CreateReadOnlyTestStore(f.Store, f.Data); page.Open(0); page.Pump(200); page.Select(id);
        Assert.Contains(page.View.Detail!.Effects, x => x.ConditionCode == "applicable-runtime-stat");
        using var scope = new InventoryCompositionTestScope();
        var session = new PlayerMenuSessionState(); session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        scope.Draw(page, session, 1120, 680);
        int effect = scope.Text.FindIndex(x => x.StartsWith("Strength: 6", StringComparison.Ordinal));
        Assert.True(effect >= 0); Assert.Equal("When this effect applies", scope.Text[effect + 1]);
    }

    [Fact]
    public void ShortestInspectorHasReachableExactServerCapacityAndRecoveryTotals()
    {
        var store = new InMemoryPlayerItemStore();
        store.Insert(InventoryViewModelTests.Item("Copper", "materials"), 10, 0);
        store.ImportRecovery("r", InventoryViewModelTests.Item("Missing", "materials"), 1, PlayerItemLocation.Unresolved,
            PlayerItemRecoveryReason.MissingAsset, "test", store.Revision);
        using var page = InventoryPage.CreateReadOnlyTestStore(store); page.Open(0); page.Pump(200);
        using var scope = new InventoryCompositionTestScope();
        var session = new PlayerMenuSessionState(); session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        var browse = scope.Draw(page, session, 400, 350);
        Assert.Contains(browse.Controls, x => x.Id == "inspect" && x.Button.CanActivate);
        session.Update(session.Current with { InspectorOnly = true });
        scope.Draw(page, session, 400, 350);
        Assert.Contains("Gear 0/120   Stacks 1/512   Recovery 1", scope.Text);
    }
}
