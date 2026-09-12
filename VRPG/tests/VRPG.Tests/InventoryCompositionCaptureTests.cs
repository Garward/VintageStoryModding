using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Runtime.InteropServices;
using Cairo;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Inventory;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Path = System.IO.Path;
using Xunit;
using VRPG.Client.PlayerMenu.Components;

namespace VRPG.Tests;

/// <summary>Opt-in capture of the production Cairo shell; does not emulate a joined game or GL models.</summary>
[Collection("Inventory Cairo")]
public sealed class InventoryCompositionCaptureTests
{
    [Fact]
    public void ProductionInventoryCapturesPlayerFacingNamesAndLimitsRawAssetCodesToRecoveryDetails()
    {
        using var scope = new InventoryCompositionTestScope();
        var store = new InMemoryPlayerItemStore();
        store.Insert(new(PlayerItemStorageKind.Aggregate, "game:pickaxe-iron", "materials", "pickaxe-iron", Array.Empty<byte>()), 1, 0);
        store.Insert(new(PlayerItemStorageKind.Aggregate, "addon:ancient_vault-key", "materials", "ancient_vault-key", Array.Empty<byte>()), 1, store.Revision);
        store.Insert(new(PlayerItemStorageKind.Unique, "game:sword-iron", "gear", "Emberwake", Array.Empty<byte>()), 1, store.Revision);
        store.ImportRecovery("missing", new(PlayerItemStorageKind.Aggregate, "missing:lost-relic", "materials", "missing:lost-relic", Array.Empty<byte>()),
            1, PlayerItemLocation.Unresolved, PlayerItemRecoveryReason.MissingAsset, "capture", store.Revision);
        using var page = InventoryPage.CreateReadOnlyTestStore(store);
        page.ChangeQuery(new(Category: ""), 0); page.Pump(200);
        var session = new PlayerMenuSessionState(); session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        var api = DispatchProxy.Create<ICoreClientAPI, CaptureProxy>();
        var textCalls = new List<string>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, 1120, 680),
            PlayerMenuLayout.Resolve(1120, 680), session, null, _ => { }, page,
            localizedCollectibleName: entry => entry.AssetCode == "game:pickaxe-iron" ? "Iron Pickaxe"
                : entry.AssetCode == "game:sword-iron" ? "Iron Sword" : null,
            textDrawn: textCalls.Add);
        using var surface = new ImageSurface((Format)0, 1120, 680);
        using var context = new Context(surface);

        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });

        Assert.Contains("Iron Pickaxe", textCalls);
        Assert.Contains("Ancient Vault Key", textCalls);
        Assert.Contains("Emberwake", textCalls);
        Assert.DoesNotContain(textCalls, text => text.Contains("game:pickaxe-iron", StringComparison.Ordinal)
            || text.Contains("addon:ancient_vault-key", StringComparison.Ordinal)
            || text.Contains("game:sword-iron", StringComparison.Ordinal));

        page.ChangeQuery(page.Query with { Location = PlayerItemLocation.Unresolved }, 300); page.Pump(500);
        page.Select(page.View.Entries.Single().EntryId);
        textCalls.Clear();
        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });

        Assert.Contains("Unknown item", textCalls);
        Assert.Contains("Recovery item: missing:lost-relic", textCalls);
        Assert.DoesNotContain("missing:lost-relic", textCalls);
    }

    [Theory]
    [InlineData(1120, 680)][InlineData(800, 520)][InlineData(620, 520)][InlineData(400, 350)]
    public void ProductionInventoryExposesSixCenteredPageControls(int width, int height)
    {
        using var scope = new InventoryCompositionTestScope();
        using var page = InventoryPage.CreateReadOnlyTestStore(new InMemoryPlayerItemStore());
        page.Open(0); page.Pump(200);
        var session = new PlayerMenuSessionState(); session.Enter(PlayerMenuEntryPoint.InventoryMenu);
        var api = DispatchProxy.Create<ICoreClientAPI, CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, width, height),
            PlayerMenuLayout.Resolve(width, height), session, null, _ => { }, page);
        using var surface = new ImageSurface((Format)0, width, height);
        using var context = new Context(surface);
        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
        var controls = (List<(string Id, MenuRect Bounds, FittedButton Button)>)typeof(PlayerMenuShell)
            .GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        var tabs = controls.Where(c => c.Id.StartsWith("page:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "page:Inventory", "page:Stats", "page:Skills", "page:Talents", "page:Library", "page:Options" }, tabs.Select(c => c.Id));
        surface.Flush();
        byte[] pixels = new byte[surface.Stride * height];
        Marshal.Copy(surface.DataPtr, pixels, 0, pixels.Length);
        foreach (var tab in tabs.Concat(controls.Where(c => c.Id == "category-next")))
        {
            // Find the actual light text ink; copper borders and selected plates are darker.
            int left = width, right = -1, top = height, bottom = -1;
            for (int y = (int)tab.Bounds.Y + 5; y < tab.Bounds.Bottom - 5; y++)
            for (int x = (int)tab.Bounds.X + 5; x < tab.Bounds.Right - 5; x++)
            {
                int at = y * surface.Stride + x * 4;
                if (pixels[at] < 165 || pixels[at + 1] < 190 || pixels[at + 2] < 200) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            Assert.True(right >= left, "Missing label ink for " + tab.Id);
            Assert.InRange((left + right + 1) / 2d - (tab.Bounds.X + tab.Bounds.Width / 2), -1.5, 1.5);
            Assert.InRange((top + bottom + 1) / 2d - (tab.Bounds.Y + tab.Bounds.Height / 2), -1.5, 1.5);
            context.SelectFontFace(PlayerMenuTheme.Font, FontSlant.Normal, FontWeight.Normal); context.SetFontSize(14);
            var metrics = context.TextExtents(PlayerMenuShell.T(tab.Id == "category-next" ? "all-types" : tab.Button.LabelKey));
            var origin = PlayerMenuDrawing.CenteredOrigin(tab.Bounds,
                new MenuTextMetrics(metrics.Width, metrics.Height, metrics.XBearing, metrics.YBearing), 0);
            Assert.True(tab.Bounds.Contains(origin.X, origin.Y));
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1013", Justification = "Explicit opt-in entry point for VRPG.MenuCapture, not an automated test.")]
    public void CaptureActualShellFromRealStoreWhenRequested()
    {
        string? output = Environment.GetEnvironmentVariable("VRPG_MENU_CAPTURE_PATH");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("VRPG_MENU_CAPTURE_PATH is required.");
        NativeLibrary.SetDllImportResolver(typeof(Context).Assembly, (name, _, _) => name == "libcairo-2" ? NativeLibrary.Load("libcairo.so.2") : IntPtr.Zero);
        string langPath = Environment.GetEnvironmentVariable("VRPG_MENU_LANG_PATH") ?? throw new InvalidOperationException("VRPG_MENU_LANG_PATH is required.");
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(langPath))!
            .ToDictionary(x => "vrpg:" + x.Key, x => x.Value);
        var language = DispatchProxy.Create<ITranslationService, CaptureProxy>();
        ((CaptureProxy)(object)language).Translations = translations;
        var previousLocale = Lang.CurrentLocale;
        var previousDefault = Lang.DefaultLocale; Lang.DefaultLocale = "menu-capture";
        Lang.AvailableLanguages.TryGetValue("menu-capture", out var previousLanguage);
        Lang.AvailableLanguages["menu-capture"] = language; Lang.ChangeLanguage("menu-capture");
        Directory.CreateDirectory(output);
        try
        {
            foreach (string scenario in new[] { "normal", "empty", "maximum", "gear-grid", "consumables", "protected", "currency", "long-name", "localized-names", "recovery", "loading", "rejected", "equipment", "effect-condition" })
            foreach (var (width, height) in new[] { (1120, 680), (800, 520), (620, 520), (400, 350) })
            {
                string? scenarioFilter = Environment.GetEnvironmentVariable("VRPG_MENU_CAPTURE_SCENARIO");
                string? sizeFilter = Environment.GetEnvironmentVariable("VRPG_MENU_CAPTURE_SIZE");
                if (scenarioFilter != null && scenario != scenarioFilter || sizeFilter != null && sizeFilter != $"{width}x{height}") continue;
                var store = new InMemoryPlayerItemStore();
                if (scenario == "localized-names")
                {
                    store.Insert(new(PlayerItemStorageKind.Aggregate, "game:pickaxe-iron", "materials", "pickaxe-iron", Array.Empty<byte>()), 1, 0);
                    store.Insert(new(PlayerItemStorageKind.Aggregate, "addon:ancient_vault-key", "materials", "ancient_vault-key", Array.Empty<byte>()), 1, store.Revision);
                    store.Insert(new(PlayerItemStorageKind.Unique, "game:sword-iron", "gear", "Emberwake", Array.Empty<byte>()), 1, store.Revision);
                    store.Insert(new(PlayerItemStorageKind.Aggregate, "missing:lost-relic", "materials", "missing:lost-relic", Array.Empty<byte>()), 1, store.Revision);
                }
                else if (scenario != "empty")
                {
                    int count = scenario is "equipment" or "effect-condition" ? 0 : scenario == "recovery" ? 10000 : scenario == "maximum" ? 512 : 12;
                    for (int i = 0; i < count; i++)
                    {
                        string name = scenario == "long-name" ? "Ancient weathered copper ingots recovered from the collapsed eastern foundry and carefully set aside for inspection " + i
                            : "Copper ingots " + i.ToString("D5");
                        var item = InventoryViewModelTests.Item(name, "materials");
                        if (scenario == "consumables") item = item with { Kind = PlayerItemStorageKind.Consumable, Category = "consumables", DisplayName = "Consumable " + i };
                        if (scenario == "protected") item = item with { Kind = PlayerItemStorageKind.Protected, Category = "keysquest", DisplayName = "Protected quest key " + i };
                        if (scenario == "currency") item = item with { Kind = PlayerItemStorageKind.Ledger, Category = "miscellaneous", DisplayName = "Currency balance " + i };
                        if (scenario == "recovery") store.ImportRecovery("r" + i, item, 10000000,
                            PlayerItemLocation.Unresolved, PlayerItemRecoveryReason.MissingAsset, "capture", store.Revision);
                        else store.Insert(item, i == 0 ? long.MaxValue : 10000000, store.Revision);
                    }
                    if (scenario is "maximum" or "gear-grid" or "equipment" or "effect-condition")
                    {
                        for (int i = 0; i < (scenario == "equipment" ? 2 : scenario == "effect-condition" ? 1 : 120); i++)
                        {
                            var gear = GearReadinessValidatorTests.Instance();
                            if (scenario == "effect-condition") gear = gear with { Affixes = new[] { new VRPG.Inventory.Gear.GearAffixRoll("vrpg:flat-power", 1,
                                new[] { new VRPG.Inventory.Gear.GearModifierRoll("vrpg:strength", "add", 6) }) } };
                            string id = store.Insert("g" + i, new(PlayerItemStorageKind.Unique, "game:test-sword", "gear", "Test sword " + i.ToString("D3"),
                                VRPG.Inventory.Gear.GearItemStateCodec.Encode(gear)), 1, store.Revision).EntryId;
                            if (scenario == "equipment" && i == 0) store.Equip("wear", id, PlayerEquipmentRoles.MainWeapon, store.Revision);
                        }
                    }
                }
                using var page = InventoryPage.CreateReadOnlyTestStore(store, TestGearDefinitions.Registry());
                page.ChangeQuery(new(Category: scenario is "gear-grid" or "equipment" or "effect-condition" ? "gear" : "", Location: scenario == "recovery" ? PlayerItemLocation.Unresolved : PlayerItemLocation.Stored), 0);
                page.View.Grid = scenario == "gear-grid";
                if (scenario != "loading") page.Pump(200);
                if (page.View.Entries.Count > 0) page.Select(page.View.Entries[0].EntryId);
                if (scenario == "rejected")
                {
                    page.Refresh(300);
                    long queryId = page.Items.QueueQuery(page.Query.ToRequest(), 300);
                    page.Items.Receive(new VRPG.Network.PlayerItemQueryResult { ConnectionGeneration = 1, QueryId = queryId, RejectionCode = "rate-limit" });
                }
                var session = new PlayerMenuSessionState(); session.Enter(scenario == "equipment" ? PlayerMenuEntryPoint.CharacterMenu : PlayerMenuEntryPoint.InventoryMenu);
                session.Update(session.Current with { Category = "", SelectedEntry = page.View.SelectedId });
                var api = DispatchProxy.Create<ICoreClientAPI, CaptureProxy>();
                using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, width, height), PlayerMenuLayout.Resolve(width, height), session, null, _ => { }, page,
                    localizedCollectibleName: scenario == "localized-names" ? entry => entry.AssetCode == "game:pickaxe-iron" ? "Iron Pickaxe"
                        : entry.AssetCode == "game:sword-iron" ? "Iron Sword" : null : null);
                using var surface = new ImageSurface((Format)0, width, height);
                using var context = new Context(surface);
                typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                surface.WriteToPng(Path.Combine(output, $"{scenario}-{width}x{height}.png"));
                if (scenario == "effect-condition")
                {
                    session.Update(session.Current with { InspectorOnly = true, InspectorScroll = 420 });
                    typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                    surface.WriteToPng(Path.Combine(output, $"{scenario}-{width}x{height}-condition.png"));
                }
                if (scenario == "gear-grid" && width >= 800)
                {
                    session.Update(session.Current with { InspectorScroll = 560 });
                    typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                    surface.WriteToPng(Path.Combine(output, $"{scenario}-{width}x{height}-comparison.png"));
                }
                if (width < 800 && page.View.Selected != null)
                {
                    session.Update(session.Current with { InspectorOnly = true });
                    typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                    surface.WriteToPng(Path.Combine(output, $"{scenario}-{width}x{height}-inspect.png"));
                    if (scenario == "long-name")
                    {
                        session.Update(session.Current with { InspectorScroll = 190 });
                        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
                        surface.WriteToPng(Path.Combine(output, $"{scenario}-{width}x{height}-inspect-scrolled.png"));
                    }
                }
            }
        }
        finally
        {
            if (previousLanguage == null) Lang.AvailableLanguages.Remove("menu-capture"); else Lang.AvailableLanguages["menu-capture"] = previousLanguage;
            Lang.ChangeLanguage(previousLocale);
            Lang.DefaultLocale = previousDefault;
        }
    }

    public class CaptureProxy : DispatchProxy
    {
        public Dictionary<string, string>? Translations;
        public List<string> TextCalls { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (Translations != null && args?.FirstOrDefault() is string key)
            {
                bool found = Translations.TryGetValue(key, out string? text);
                if (method!.ReturnType == typeof(bool)) return found;
                if (method.ReturnType == typeof(string))
                {
                    string result = found ? string.Format(text!, args.LastOrDefault() as object[] ?? Array.Empty<object>()) : key;
                    TextCalls.Add(result); return result;
                }
            }
            var type = method!.ReturnType;
            if (type.IsInterface) return typeof(DispatchProxy).GetMethod("Create", Type.EmptyTypes)!.MakeGenericMethod(type, typeof(CaptureProxy)).Invoke(null, null);
            return type.IsValueType && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
