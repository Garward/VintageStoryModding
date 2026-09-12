using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Config;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class LibraryOptionsPageTests
{
    [Fact]
    public void ChangedVisibleSlotsAreImmediatelySharedWithSkillsAndSnapshot()
    {
        using var hub = new HubPageClientService(_ => { }, new());
        hub.Receive(new OpenHubPacket());
        hub.Options.ChangeSkillSlots(1);
        Assert.Equal(5, hub.Skills.VisibleSlots);
        Assert.Equal(5, hub.Packet!.Options.SkillHotbarSlots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothReceivePathsNormalizeNestedLibraryArraysBeforeSearching(bool direct)
    {
        using var hub = new HubPageClientService(_ => { }, new());
        var entry = new LibraryEntryPacket { Code = "vrpg:ember", Name = "Ember", Tags = null!, Fields = null! };
        if (direct) hub.Receive(new OpenLibraryPacket { Entries = new[] { entry } });
        else hub.Receive(new OpenHubPacket { Entries = new[] { entry } });
        Assert.True(hub.LibraryReady);
        Assert.Empty(hub.LibraryEntries[0].Tags); Assert.Empty(hub.LibraryEntries[0].Fields);
        hub.Library.SetQuery("nothing"); Assert.Empty(hub.Library.VisibleEntries);
        Assert.Null(entry.Tags); Assert.Null(entry.Fields);
    }

    [Fact]
    public void SearchMatchesReadableNamesSummariesAndTagsAndReconcilesSelection()
    {
        using var hub = new HubPageClientService(_ => { }, new());
        hub.Receive(new OpenLibraryPacket { Entries = Entries() });
        var page = hub.Library;
        Assert.Equal(new[] { "", "Currency", "Skills" }, page.Categories);
        page.SelectEntry("vrpg:slash");
        page.SetQuery(" HEALING "); Assert.Equal("vrpg:orb", Assert.Single(page.VisibleEntries).Code);
        Assert.Equal("vrpg:orb", page.SelectedEntry!.Code);
        page.SetQuery("CLEAVE"); Assert.Equal("vrpg:slash", Assert.Single(page.VisibleEntries).Code);
        page.SetQuery("recover health"); Assert.Equal("vrpg:orb", Assert.Single(page.VisibleEntries).Code);
        page.SetQuery("missing"); Assert.Empty(page.VisibleEntries); Assert.Null(page.SelectedEntry);
        page.SetQuery(""); page.SelectCategory("Skills"); Assert.Equal("vrpg:slash", Assert.Single(page.VisibleEntries).Code);
        hub.Receive(new OpenLibraryPacket { Entries = Entries().Reverse().ToArray() });
        Assert.Equal("Skills", page.Category); Assert.Equal("vrpg:slash", page.SelectedEntry!.Code);
        hub.Receive(new OpenLibraryPacket()); Assert.Equal("", page.Category); Assert.Null(page.SelectedEntry);
    }

    [Fact]
    public void LibraryScrollIsBoundedAndResetOnFilterAndConnectionChanges()
    {
        using var hub = new HubPageClientService(_ => { }, new());
        hub.Receive(new OpenLibraryPacket { Entries = Enumerable.Range(0, 200).Select(i => new LibraryEntryPacket { Code = "id" + i, Name = "Entry " + i }).ToArray() });
        hub.Library.ScrollBy(999, 5); Assert.Equal(195, hub.Library.Scroll);
        hub.Library.ScrollBy(-999, 5); Assert.Equal(0, hub.Library.Scroll);
        hub.Library.ScrollBy(99, 5); hub.Library.SetQuery("absent"); Assert.Equal(0, hub.Library.Scroll);
        hub.ResetConnection(); Assert.False(hub.LibraryReady); Assert.Equal("", hub.Library.Query);
    }

    [Fact]
    public void OptionsRetainCategoryAndSendExactNotificationHotbarAndAutoThrowArguments()
    {
        var notifications = new List<(bool, bool)>(); var hotbar = new List<(bool, int)>(); var throws = new List<bool>();
        using var hub = new HubPageClientService(_ => { }, new() { UpdateOptions = (a, b) => notifications.Add((a, b)), UpdateHotbar = (a, b) => hotbar.Add((a, b)), UpdateAutoThrow = throws.Add });
        hub.Receive(new OpenHubPacket()); var page = hub.Options;
        page.SelectCategory(OptionsCategory.ActionBar);
        page.ToggleCooldownNotifications(); page.ToggleResourceNotifications();
        Assert.Equal(new[] { (true, false), (true, true) }, notifications);
        page.ToggleHotbarLock(); page.ChangeSkillSlots(-1); page.ChangeSkillSlots(1); page.ToggleAutoThrow();
        Assert.Equal(new[] { (false, 4), (false, 4), (false, 5) }, hotbar); Assert.Equal(new[] { false }, throws);
        hub.Receive(new OpenHubPacket { Options = new() { SkillHotbarSlots = 99 } });
        Assert.Equal(OptionsCategory.ActionBar, page.Category); page.ChangeSkillSlots(1); Assert.Equal((true, 8), hotbar[^1]);
        hub.Receive(new OpenHubPacket { Options = new() { SkillHotbarSlots = -20 } });
        page.ChangeSkillSlots(1); Assert.Equal((true, 5), hotbar[^1]);
        hub.ResetConnection(); Assert.Equal(OptionsCategory.Notifications, page.Category);
    }

    [Fact]
    public void ResourceHudActionsKeepExistingDeltasAndBoundedButtons()
    {
        var changes = new List<(int, int)>(); var locks = new List<bool>(); int resets = 0;
        var config = new RpgHudConfig();
        using var hub = new HubPageClientService(_ => { }, new() { ResourceHud = config, SetResourceHudLocked = locks.Add,
            ResizeResourceHud = (w, h) => { changes.Add((w, h)); config.Width = Math.Clamp(config.Width + w, 180, 700); config.BarHeight = Math.Clamp(config.BarHeight + h, 14, 42); }, ResetResourceHud = () => resets++ });
        hub.Receive(new OpenHubPacket()); var page = hub.Options;
        page.ToggleResourceHudLock(); page.ChangeResourceWidth(-1); page.ChangeResourceWidth(1); page.ChangeResourceHeight(-1); page.ChangeResourceHeight(1); page.ResetResourceHud();
        Assert.Equal(new[] { false }, locks); Assert.Equal(new[] { (-25, 0), (25, 0), (0, -2), (0, 2) }, changes); Assert.Equal(1, resets);
        config.Width = 180; config.BarHeight = 14;
        Assert.False(page.CanResizeWidth(-1)); Assert.False(page.CanResizeHeight(-1));
        page.ChangeResourceWidth(-1); page.ChangeResourceHeight(-1); Assert.Equal(4, changes.Count);
        config.Width = 700; config.BarHeight = 42;
        Assert.False(page.CanResizeWidth(1)); Assert.False(page.CanResizeHeight(1));
        page.ChangeResourceWidth(1); page.ChangeResourceHeight(1); Assert.Equal(4, changes.Count);
    }

    [Fact]
    public void CombatVisualActionsCycleExistingValuesAndPersistEveryChange()
    {
        int saves = 0; using var hub = new HubPageClientService(_ => { }, new() { PersistClientConfig = () => saves++ });
        hub.Receive(new OpenHubPacket()); var page = hub.Options; var v = hub.Actions.CombatVisuals;
        page.ToggleCombatText(); page.ToggleDamageNumbers(); page.ToggleEventWords(); page.ToggleMergeNumbers(); page.ToggleStatusAuras();
        Assert.False(v.CombatTextEnabled); Assert.False(v.DamageNumbers); Assert.False(v.EventWords); Assert.False(v.MergeNumbers); Assert.False(v.StatusAuras);
        page.ToggleDegradationPolicy(); Assert.Equal("uniform", v.DegradationPolicy); page.ToggleDegradationPolicy(); Assert.Equal("own-first", v.DegradationPolicy);
        page.CycleTelegraphOpacity(); Assert.Equal(.5f, v.TelegraphOpacity); page.CycleTelegraphOpacity(); Assert.Equal(.25f, v.TelegraphOpacity); page.CycleTelegraphOpacity(); Assert.Equal(1f, v.TelegraphOpacity);
        page.CycleIntensity(); Assert.Equal(.75f, v.Intensity); page.CycleIntensity(); Assert.Equal(.5f, v.Intensity); page.CycleIntensity(); Assert.Equal(1f, v.Intensity);
        Assert.Equal(13, saves);
    }

    internal static LibraryEntryPacket[] Entries() => new[] {
        new LibraryEntryPacket { Code = "vrpg:slash", Name = "Cleave", Category = "Skills", Summary = "Sweep nearby enemies.", Tags = new[] { "Melee" }, Fields = new[] { new LibraryFieldPacket { Label = "Damage", Value = "12" } } },
        new LibraryEntryPacket { Code = "vrpg:orb", Name = "Life orb", Category = "Currency", Summary = "Recover health.", Tags = new[] { "Healing" } } };
}
