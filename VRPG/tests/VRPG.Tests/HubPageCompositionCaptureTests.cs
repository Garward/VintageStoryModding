using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Cairo;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Hub;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;

namespace VRPG.Tests;

[Collection("Inventory Cairo")]
public sealed class HubPageCompositionCaptureTests
{
    [Fact]
    public void TalentEditorRemainsPrivilegedAndInspectorActionsExplainCurrentOperation()
    {
        using var scope = new InventoryCompositionTestScope(); int opened = 0;
        using var hub = new HubPageClientService(_ => { }, new() { OpenTalentEditor = () => opened++ });
        hub.Receive(TalentPageTests.Fixture.Tree()); var packet = TalentPageTests.Fixture.Packet(Array.Empty<string>(), 3, 2); hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents);
        using var shell = Shell(session, hub, 1120, 680); Draw(shell, 1120, 680);
        Assert.DoesNotContain(Controls(shell), c => c.Id == "talent:editor");
        packet.CanEditTalentTree = true; hub.Receive(packet); Draw(shell, 1120, 680);
        Activate(shell, "talent:editor"); Assert.Equal(1, opened);
        hub.Talents.QueueAllocation("root"); Draw(shell, 1120, 680);
        Activate(shell, "talent:allocate"); Assert.Empty(hub.Talents.Allocations);
    }
    [Fact]
    public void TalentDetailsCanBeReadWithMouseAtMinimumSize()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new TalentPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents); session.Update(session.Current with { InspectorOnly = true });
        using var shell = Shell(session, f.Hub, 400, 350); Draw(shell, 400, 350);
        Activate(shell, "talent:read"); Assert.True(session.Current.InspectorScroll > 0);
        Draw(shell, 400, 350); Assert.All(Controls(shell), c => AssertInside(c.Bounds, 400, 350));
    }
    [Fact]
    public void TalentErrorDoesNotScrollInvisibleInspector()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new TalentPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents); session.Update(session.Current with { InspectorOnly = true });
        using var shell = Shell(session, f.Hub, 400, 350); Draw(shell, 400, 350);
        f.Hub.Receive(new TalentTreeSnapshotPacket { SchemaVersion = -1 }); Draw(shell, 400, 350);
        var key = new KeyEvent { KeyCode = (int)GlKeys.PageDown }; var before = session.Current;
        shell.OnKeyDown(null!, key); Assert.False(key.Handled); Assert.Equal(before, session.Current);
    }
    [Fact]
    public void TalentCompositionAndInputPreserveLogicalCoordinatesAndAuthoritativeApply()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new TalentPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents);
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 1120, 680, text.Add);
        shell.Bounds.ParentBounds = new RootBounds(41, 29); Draw(shell, 1120, 680);
        Assert.Contains("Way of the Sentinel", text); Assert.Contains(Controls(shell), c => c.Id == "talent:apply");
        var root = Assert.Single(f.Hub.Talents.Graph.VisibleNodes.Where(n => n.Node.Code == "root"));
        float old = Vintagestory.API.Config.RuntimeEnv.GUIScale;
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                Vintagestory.API.Config.RuntimeEnv.GUIScale = scale;
                f.Hub.Talents.ResetDraft(); Draw(shell, 1120, 680);
                var now = f.Hub.Talents.Graph.VisibleNodes.Single(n => n.Node.Code == "root");
                Assert.Equal(root.X, now.X); Assert.Equal(root.Radius, now.Radius);
                var down = new MouseEvent(41 + (int)(root.X * scale), 29 + (int)(root.Y * scale), EnumMouseButton.Left, 0);
                shell.OnMouseDownOnElement(null!, down); shell.OnMouseUpOnElement(null!, down);
                Assert.Equal(new[] { "vrpg:root" }, f.Hub.Talents.Allocations);
            }
        }
        finally { Vintagestory.API.Config.RuntimeEnv.GUIScale = old; }
        Draw(shell, 1120, 680); Activate(shell, "talent:apply"); Assert.Single(f.Sent);
    }

    [Fact]
    public void TalentDragDoesNotClickAndWheelOnlyConsumesGraph()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new TalentPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents);
        using var shell = Shell(session, f.Hub, 1120, 680); shell.Bounds.ParentBounds = new RootBounds(); Draw(shell, 1120, 680);
        var root = Assert.Single(f.Hub.Talents.Graph.VisibleNodes.Where(n => n.Node.Code == "root"));
        var down = new MouseEvent((int)root.X, (int)root.Y, EnumMouseButton.Left, 0);
        shell.OnMouseDownOnElement(null!, down);
        shell.OnMouseMove(null!, new MouseEvent((int)root.X + 8, (int)root.Y + 8, EnumMouseButton.Left, 0));
        shell.OnMouseUpOnElement(null!, down); Assert.Empty(f.Hub.Talents.Allocations);
        Assert.False(shell.TalentWheel(1, shell.Layout.Inspector.X + 5, shell.Layout.Inspector.Y + 5));
        double before = f.Hub.Talents.Graph.Zoom;
        Assert.True(shell.TalentWheel(1, root.X, root.Y)); Assert.True(f.Hub.Talents.Graph.Zoom > before);
        shell.OnMouseDownOnElement(null!, new MouseEvent(0, 0, EnumMouseButton.Left, 0));
        shell.OnMouseUpOnElement(null!, down); Assert.Empty(f.Hub.Talents.Allocations);
    }

    [Fact]
    public void TalentCapturesCoverDraftStatesAndAnchoredActions()
    {
        using var scope = new InventoryCompositionTestScope();
        string directory = Environment.GetEnvironmentVariable("VRPG_TALENT_CAPTURE_PATH") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrpg-task5-captures");
        Directory.CreateDirectory(directory);
        foreach (var (width, height) in new[] { (1120, 680), (800, 520), (620, 520), (400, 350) })
        foreach (string state in new[] { "loading", "empty", "normal", "draft", "refund", "pending", "rejection", "error", "long", "no-points", "large" })
        {
            using var f = new TalentPageTests.Fixture(state == "refund" ? new[] { "root", "bridge", "leaf" } : null);
            if (state == "loading") { f.Hub.ResetConnection(); f.Hub.RequestSnapshot(); }
            if (state == "empty") { f.Hub.ResetConnection(); f.Hub.Receive(new OpenHubPacket()); f.Hub.Receive(new TalentTreeSnapshotPacket()); }
            if (state == "error") f.Hub.Receive(new TalentTreeSnapshotPacket { SchemaVersion = -1 });
            if (state is "draft" or "pending" or "rejection") { f.Hub.Talents.QueueAllocation("root"); f.Hub.Talents.QueueAllocation("bridge"); }
            if (state is "pending" or "rejection") f.Hub.Talents.Apply();
            if (state == "rejection") { var p = TalentPageTests.Fixture.Packet(Array.Empty<string>(), 3, 2); p.FeedbackError = true; p.FeedbackMessage = "Unknown talent: vrpg:private-node"; f.Hub.Receive(p); }
            if (state == "refund") { f.Hub.Talents.QueueRefund("leaf"); f.Hub.Talents.Graph.Select("leaf"); }
            if (state == "no-points") { f.Hub.Receive(TalentPageTests.Fixture.Packet(new[] { "root", "bridge" }, 0, 0)); f.Hub.Talents.Graph.Select("leaf"); }
            if (state == "large")
            {
                var tree = TalentPageTests.Fixture.Tree();
                tree.Nodes = Enumerable.Range(0, 2048).Select(i => new TalentTreeNodePacket { Code = "node-" + i, Name = "Sentinel " + i,
                    Starter = i == 0, X = (i % 64) * 64, Y = (i / 64) * 64, Links = i < 2047 ? new[] { "node-" + (i + 1) } : Array.Empty<string>() }).ToArray();
                f.Hub.Receive(tree);
            }
            if (state == "long")
            {
                var tree = TalentPageTests.Fixture.Tree(); tree.Nodes[0].Name = "Sentinels of the weathered northern mountain sanctuary";
                tree.Nodes[0].Description = string.Join(" ", Enumerable.Repeat("Protect your allies and hold your ground through the longest battles.", 20));
                f.Hub.Receive(tree);
            }
            var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Talents);
            var text = new List<string>(); using var shell = Shell(session, f.Hub, width, height, text.Add);
            string path = System.IO.Path.Combine(directory, $"talents-{state}-{width}x{height}");
            Draw(shell, width, height, path + ".png"); Assert.All(Controls(shell), c => AssertInside(c.Bounds, width, height));
            Assert.DoesNotContain(text, t => t.Contains("vrpg:", StringComparison.Ordinal));
            if (state is "loading" or "error") continue;
            Assert.Contains(Controls(shell), c => c.Id == "talent:apply");
            Assert.Contains(Controls(shell), c => c.Id == "talent:reset");
            if (shell.Layout.AlternatePanes)
            { Activate(shell, "talent:details"); Draw(shell, width, height, path + "-details.png"); Assert.All(Controls(shell), c => AssertInside(c.Bounds, width, height)); }
            if (state == "long") { session.Update(session.Current with { InspectorScroll = 400 }); Draw(shell, width, height, path + "-scrolled.png"); }
            if (state == "rejection") Assert.Contains("Could not apply talents. Review your plan and try again.", text);
        }
    }

    private sealed class RootBounds(double x = 0, double y = 0) : ElementBounds { public override double absX => x; public override double absY => y; }
    [Theory]
    [InlineData(PlayerMenuPage.Skills, "Unknown skill: vrpg:cleave", "That skill is unavailable. Choose another skill.")]
    [InlineData(PlayerMenuPage.Stats, "Unknown base stat: vrpg:strength", "That stat is unavailable. Try again later.")]
    [InlineData(PlayerMenuPage.Skills, "Unavailable while in combat. Try again after combat.", "Unavailable while in combat. Try again after combat.")]
    [InlineData(PlayerMenuPage.Stats, "No unspent stat points are available.", "No unspent stat points are available.")]
    [InlineData(PlayerMenuPage.Stats, "System.InvalidOperationException: internal failure", "Could not change stats. Try again.")]
    public void FeedbackPresentationKeepsHelpfulTextAndHidesDiagnostics(PlayerMenuPage page, string response, string expected)
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        if (page == PlayerMenuPage.Stats) f.Hub.Stats.Adjust("strength", 1); else f.Hub.Skills.EquipSelected(0);
        var packet = StatsSkillsPageTests.Fixture.Packet(); packet.FeedbackError = true; packet.FeedbackMessage = response; f.Hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(page); session.Update(session.Current with { InspectorOnly = true });
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 400, 350, text.Add); Draw(shell, 400, 350);
        string? captureDirectory = Environment.GetEnvironmentVariable("VRPG_HUB_CAPTURE_PATH");
        if (captureDirectory != null && response.StartsWith("Unknown", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(captureDirectory);
            Draw(shell, 400, 350, System.IO.Path.Combine(captureDirectory, $"{page.ToString().ToLowerInvariant()}-diagnostic-rejection-400x350.png"));
        }
        Assert.Contains(expected, text);
        Assert.DoesNotContain(text, t => t.Contains("vrpg:", StringComparison.Ordinal) || t.Contains("Exception", StringComparison.Ordinal));
        Assert.Equal(response, f.Hub.Packet!.FeedbackMessage);
    }

    [Fact]
    public void ExcessiveServerFeedbackHasBoundedPlayerCopy()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        f.Hub.Skills.EquipSelected(0); var packet = StatsSkillsPageTests.Fixture.Packet();
        packet.FeedbackMessage = new string('x', 5000); packet.FeedbackError = true; f.Hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Skills);
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 1120, 680, text.Add); Draw(shell, 1120, 680);
        Assert.Contains("Could not assign that skill. Try again.", text);
        Assert.DoesNotContain(text, t => t.Length > 500);
        Assert.Equal(5000, f.Hub.Packet!.FeedbackMessage.Length);
    }

    [Theory]
    [InlineData((int)GlKeys.Down)][InlineData((int)GlKeys.PageDown)][InlineData((int)GlKeys.End)]
    public void ErrorBodyDoesNotNavigateInvisibleSkills(int keyCode)
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Skills);
        using var shell = Shell(session, f.Hub, 400, 350); Draw(shell, 400, 350);
        f.Hub.Receive(new TalentTreeSnapshotPacket { SchemaVersion = -1 }); Draw(shell, 400, 350);
        Assert.DoesNotContain(Controls(shell), c => c.Id.StartsWith("skill:", StringComparison.Ordinal));
        var before = session.Current; var selection = f.Hub.State;
        var key = new KeyEvent { KeyCode = keyCode }; shell.OnKeyDown(null!, key);
        Assert.False(key.Handled); Assert.Equal(before, session.Current); Assert.Equal(selection, f.Hub.State);
        f.Hub.ResetConnection(); Draw(shell, 400, 350);
        var afterReset = session.Current;
        key = new KeyEvent { KeyCode = keyCode }; shell.OnKeyDown(null!, key);
        Assert.False(key.Handled); Assert.Equal(afterReset, session.Current);
    }

    [Theory]
    [InlineData(1120, 680)][InlineData(800, 520)][InlineData(620, 520)][InlineData(400, 350)]
    public void RealPagesKeepActionsVisibleAndInvokeAuthoritativeCallbacks(int width, int height)
    {
        using var scope = new InventoryCompositionTestScope(); using var fixture = new StatsSkillsPageTests.Fixture();
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Stats);
        using var shell = Shell(session, fixture.Hub, width, height);
        Draw(shell, width, height);
        var statControls = Controls(shell).Where(c => c.Id.StartsWith("stat:")).ToArray();
        Assert.Equal(6, statControls.Length);
        Assert.All(statControls, c => AssertInside(c.Bounds, width, height));
        Activate(shell, "stat:strength:1");
        Assert.Equal(new[] { ("strength", 1) }, fixture.Stats);
        fixture.Hub.Receive(StatsSkillsPageTests.Fixture.Packet());
        session.Navigate(PlayerMenuPage.Skills); session.Update(session.Current with { InspectorOnly = true });
        Draw(shell, width, height);
        var slots = Controls(shell).Where(c => c.Id.StartsWith("skill-slot:")).ToArray();
        Assert.Equal(4, slots.Length); Assert.All(slots, c => AssertInside(c.Bounds, width, height));
        Activate(shell, "skill-slot:2"); Assert.Equal(new[] { (3, "vrpg:cleave") }, fixture.Equips);
    }

    [Fact]
    public void LongBrowseNamesEllipsizeAndRemainCompleteInDetails()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        var packet = StatsSkillsPageTests.Fixture.Packet();
        packet.Classes[0].Name = "Guardians of the weathered northern mountain sanctuary";
        packet.Classes[0].Skills[0].Name = "Ancient sweeping strike of the northern guardians"; f.Hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Skills);
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 400, 350, text.Add); Draw(shell, 400, 350);
        Assert.Equal(2, text.Count(t => t.EndsWith("…", StringComparison.Ordinal)));
        text.Clear(); session.Update(session.Current with { InspectorOnly = true }); Draw(shell, 400, 350);
        Assert.Contains(packet.Classes[0].Name, text); Assert.Contains(packet.Classes[0].Skills[0].Name, text);
        packet.Classes[0].Skills = Array.Empty<HubSkillPacket>(); f.Hub.Receive(packet); text.Clear(); Draw(shell, 400, 350);
        Assert.Contains(packet.Classes[0].Name, text);
        session.Update(session.Current with { InspectorOnly = false }); Draw(shell, 400, 350);
        Assert.Contains(Controls(shell), control => control.Id == "hub:details");
    }

    [Fact]
    public void SkillDetailsPreserveDamageTypeDeliveryAndRepeatedHitTiming()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        var packet = StatsSkillsPageTests.Fixture.Packet(); var skill = packet.Classes[0].Skills[0];
        skill.DamageType = "vrpg:fire"; skill.Delivery = "melee_arc"; skill.MeleeArcDegrees = 90;
        skill.TimingMode = "sequence"; skill.HitCount = 3; skill.HitIntervalSeconds = .4f; f.Hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Skills);
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 1120, 680, text.Add); Draw(shell, 1120, 680);
        Assert.Contains("Damage type: Fire", text);
        Assert.Contains("Melee sweep · 90° arc", text);
        Assert.Contains("3 hits · 0.4s apart", text);
    }

    [Fact]
    public void KeyboardPagesThroughLargeSkillListAndAssignmentHasAnAnchoredLabel()
    {
        using var scope = new InventoryCompositionTestScope(); using var f = new StatsSkillsPageTests.Fixture();
        var packet = StatsSkillsPageTests.Fixture.Packet();
        packet.Classes[0].Skills = Enumerable.Range(0, 80).Select(i => new HubSkillPacket { Code = "vrpg:skill-" + i, Name = "Skill " + i, RequiredLevel = i, LearnedLevel = 1,
            ResourceCost = 4, ResourceType = "mana", ResourceCostMode = "per_second" }).ToArray();
        f.Hub.Receive(packet);
        var session = new PlayerMenuSessionState(); session.Navigate(PlayerMenuPage.Skills);
        var text = new List<string>(); using var shell = Shell(session, f.Hub, 400, 350, text.Add);
        Draw(shell, 400, 350);
        shell.OnKeyDown(null!, new KeyEvent { KeyCode = (int)GlKeys.PageDown });
        Assert.True(session.Current.ScrollAnchor > 0);
        session.Update(session.Current with { InspectorOnly = true }); Draw(shell, 400, 350);
        Assert.Contains("Assign to slot", text);
        Assert.Contains("Cost: 4 mana / second", text);
    }

    [Fact]
    public void CapturesStateMatrixWithBoundedControlsAndNoRawCodes()
    {
        using var scope = new InventoryCompositionTestScope();
        string directory = Environment.GetEnvironmentVariable("VRPG_HUB_CAPTURE_PATH") ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrpg-task4-captures");
        Directory.CreateDirectory(directory);
        foreach (var (width, height) in new[] { (1120, 680), (800, 520), (620, 520), (400, 350) })
        foreach (var page in new[] { PlayerMenuPage.Stats, PlayerMenuPage.Skills })
        foreach (string state in new[] { "loading", "empty", "normal", "pending", "rejection", "long" })
        {
            using var fixture = new StatsSkillsPageTests.Fixture(); var packet = StatsSkillsPageTests.Fixture.Packet();
            if (state == "loading") { fixture.Hub.ResetConnection(); fixture.Hub.RequestSnapshot(); }
            if (state == "empty") { fixture.Hub.ResetConnection(); fixture.Hub.Receive(new OpenHubPacket()); }
            if (state == "long")
            {
                packet.Classes[0].Name = "Guardians of the weathered northern mountain sanctuary";
                packet.Classes[0].Skills = Enumerable.Range(0, 200).Select(i => new HubSkillPacket {
                    Code = "vrpg:long-" + i, Name = "Ancient sweeping strike of the northern guardians " + i,
                    Description = string.Join(" ", Enumerable.Repeat("Sweep your weapon through nearby enemies, then recover your stance.", 24)),
                    LearnedLevel = 1, RequiredLevel = i, MaxLevel = 5 }).ToArray();
                packet.Options.SkillHotbarSlots = 8; fixture.Hub.Receive(packet);
            }
            if (state is "pending" or "rejection")
            {
                if (page == PlayerMenuPage.Stats) fixture.Hub.Stats.Adjust("strength", 1); else fixture.Hub.Skills.EquipSelected(0);
                if (state == "rejection") { packet.FeedbackError = true; packet.FeedbackMessage = "Unavailable while in combat. Try again after combat."; fixture.Hub.Receive(packet); }
            }
            var session = new PlayerMenuSessionState(); session.Navigate(page);
            var texts = new List<string>();
            using var shell = Shell(session, fixture.Hub, width, height, texts.Add);
            string path = System.IO.Path.Combine(directory, $"{page.ToString().ToLowerInvariant()}-{state}-{width}x{height}");
            Draw(shell, width, height, path + ".png");
            Assert.DoesNotContain(texts, t => t.Contains("vrpg:", StringComparison.Ordinal));
            Assert.All(Controls(shell), c => AssertInside(c.Bounds, width, height));
            Assert.True(Controls(shell).Count(c => c.Id.StartsWith("skill:")) <= 10);
            if (state is "normal" or "pending" or "rejection" or "long")
            {
                Assert.Contains(texts, t => t.Contains(page == PlayerMenuPage.Stats ? "Strength" : state == "long" ? "Ancient" : "Cleave", StringComparison.Ordinal));
                if (PlayerMenuLayout.Resolve(width, height).AlternatePanes)
                {
                    session.Update(session.Current with { InspectorOnly = true }); Draw(shell, width, height, path + "-details.png");
                    Assert.All(Controls(shell), c => AssertInside(c.Bounds, width, height));
                }
                if (state == "long")
                {
                    session.Update(session.Current with { InspectorScroll = 420 }); Draw(shell, width, height, path + "-scrolled.png");
                }
            }
        }
    }

    private static PlayerMenuShell Shell(PlayerMenuSessionState session, HubPageClientService hub, int width, int height, Action<string>? text = null)
        => new(DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>(), ElementBounds.Fixed(0, 0, width, height),
            PlayerMenuLayout.Resolve(width, height), session, null, _ => { }, hub: hub, textDrawn: text);
    private static void Draw(PlayerMenuShell shell, int width, int height, string? path = null)
    {
        ((List<(string, MenuRect, FittedButton)>)typeof(PlayerMenuShell).GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!).Clear();
        using var surface = new ImageSurface((Format)0, width, height); using var ctx = new Context(surface);
        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { ctx });
        if (path != null) surface.WriteToPng(path);
    }
    private static (string Id, MenuRect Bounds, FittedButton Button)[] Controls(PlayerMenuShell shell)
        => ((List<(string, MenuRect, FittedButton)>)typeof(PlayerMenuShell).GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!).ToArray();
    private static void Activate(PlayerMenuShell shell, string id)
    {
        var bounds = Controls(shell).Single(c => c.Id == id).Bounds;
        shell.Bounds.ParentBounds = new RootBounds();
        float scale = Vintagestory.API.Config.RuntimeEnv.GUIScale;
        Vintagestory.API.Config.RuntimeEnv.GUIScale = 1;
        try { shell.OnMouseUpOnElement(null!, new MouseEvent((int)bounds.X + 2, (int)bounds.Y + 2, EnumMouseButton.Left, 0)); }
        finally { Vintagestory.API.Config.RuntimeEnv.GUIScale = scale; }
    }
    private static void AssertInside(MenuRect rect, int width, int height)
    { Assert.InRange(rect.X, 0, width); Assert.InRange(rect.Y, 0, height); Assert.InRange(rect.Right, 0, width); Assert.InRange(rect.Bottom, 0, height); }
}
