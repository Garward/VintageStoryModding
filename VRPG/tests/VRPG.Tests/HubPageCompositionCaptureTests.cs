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
    private sealed class RootBounds : ElementBounds { public override double absX => 0; public override double absY => 0; }
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
