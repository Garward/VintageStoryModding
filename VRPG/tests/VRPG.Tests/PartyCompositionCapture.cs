using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Cairo;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VRPG.Tests;

/// <summary>Native Cairo composition from validated connection-service snapshots, not joined-game evidence.</summary>
public static class PartyCompositionCapture
{
    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("VRPG_MENU_CAPTURE_PATH") ?? throw new InvalidOperationException("VRPG_MENU_CAPTURE_PATH is required.");
        string lang = Environment.GetEnvironmentVariable("VRPG_MENU_LANG_PATH") ?? throw new InvalidOperationException("VRPG_MENU_LANG_PATH is required.");
        PartyMenuIntegrationTests.CairoReady();
        var language = DispatchProxy.Create<ITranslationService, InventoryCompositionCaptureTests.CaptureProxy>();
        ((InventoryCompositionCaptureTests.CaptureProxy)(object)language).Translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(lang))!
            .ToDictionary(kv => "vrpg:" + kv.Key, kv => kv.Value);
        Lang.DefaultLocale = "party-capture"; Lang.AvailableLanguages["party-capture"] = language; Lang.ChangeLanguage("party-capture");
        Directory.CreateDirectory(output);
        foreach (string scenario in new[] { "none", "solo", "invited", "many-invites", "expired", "member", "leader", "full", "offline", "loading", "unavailable", "pending", "error", "success", "long-name", "long-unbroken", "online", "online-many", "disband" })
        foreach (var (width, height) in new[] { (1120, 680), (800, 520), (400, 350) })
        {
            using var f = new PartyPanelFixture(scenario is "pending" or "error" or "success" ? "none" : scenario is "online" or "disband" ? "leader" : scenario);
            var controller = new PlayerMenuController(new PlayerItemClientService(), f.Panel); controller.OpenParty();
            if (scenario is "invited" or "expired") { f.Panel.Handle("section:invitations"); f.Panel.Select(PartyPanelFixture.InvitationId); }
            if (scenario == "many-invites") f.Panel.Handle("section:invitations");
            if (scenario is "member" or "leader" or "offline" or "long-name" or "long-unbroken" or "full") f.Panel.Select("b");
            if (scenario is "solo" or "disband") f.Panel.Select("a");
            if (scenario == "disband") f.Panel.Handle("disband");
            if (scenario is "online" or "online-many") f.Panel.Handle("section:online");
            if (scenario is "pending" or "error" or "success")
            {
                f.Panel.Handle("create");
                if (scenario == "error") f.Transport.Receive(new PartyResultPacket { SessionId = "session", RequestId = f.Client.Pending!.RequestId, Code = "rate-limit" });
                if (scenario == "success")
                {
                    string request = f.Client.Pending!.RequestId;
                    f.Transport.Receive(new PartyResultPacket { SessionId = "session", RequestId = request, Code = "ok", Success = true,
                        Snapshot = new PartySnapshotPacket { SessionId = "session", RequestId = request, Sequence = 2, Revision = 8,
                            Health = VRPG.Parties.PartyHealth.Healthy, SelfUid = "a", PartyId = "22222222222222222222222222222222", LeaderUid = "a",
                            Members = new[] { new PartyMemberPacket { Uid = "a", DisplayName = "Rowan", IsOnline = true } } } });
                    f.Panel.Select("a");
                }
            }
            var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
            int maximumScroll = 0;
            void Capture(string suffix)
            {
                using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, width, height), PlayerMenuLayout.Resolve(width, height), controller.Session, null, _ => { }, party: f.Panel);
                using var surface = new ImageSurface((Format)0, width, height); using var context = new Context(surface);
                typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                maximumScroll = (int)typeof(PlayerMenuShell).GetField("partyDetailMaximum", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                surface.WriteToPng(System.IO.Path.Combine(output, $"party-{scenario}-{width}x{height}{suffix}.png"));
            }
            Capture("");
            if (width == 400 && scenario is "long-name" or "long-unbroken")
            { f.Panel.Scroll(10000, maximumScroll, true); Capture("-scrolled"); f.Panel.Scroll(-10000, maximumScroll, true); }
            if (width == 400 && f.Panel.State.ShowDetails) { f.Panel.Back(); Capture("-list"); }
        }
    }
}
