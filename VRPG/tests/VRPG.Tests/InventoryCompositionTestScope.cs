using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Linq;
using Cairo;
using VRPG.Client.PlayerMenu;
using VRPG.Client.PlayerMenu.Components;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Client.PlayerMenu.Shell;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Xunit;

namespace VRPG.Tests;

[CollectionDefinition("Inventory Cairo", DisableParallelization = true)]
public sealed class InventoryCairoCollection { }

/// <summary>Deterministic tests run the actual Cairo shell. Only engine interfaces/translation IO are substituted.</summary>
internal sealed class InventoryCompositionTestScope : IDisposable
{
    private static bool cairoResolved;
    private readonly string oldLocale = Lang.CurrentLocale, oldDefault = Lang.DefaultLocale;
    private readonly InventoryCompositionCaptureTests.CaptureProxy translation;
    public List<string> Text => translation.TextCalls;
    internal static void EnsureCairo()
    {
        if (!cairoResolved)
        {
            NativeLibrary.SetDllImportResolver(typeof(Context).Assembly, (name, _, _) => name == "libcairo-2" ? NativeLibrary.Load("libcairo.so.2") : IntPtr.Zero);
            cairoResolved = true;
        }
    }
    public InventoryCompositionTestScope()
    {
        EnsureCairo();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, "VRPG/assets/vrpg/lang/en.json"))) directory = directory.Parent;
        string path = System.IO.Path.Combine(directory!.FullName, "VRPG/assets/vrpg/lang/en.json");
        var language = DispatchProxy.Create<ITranslationService, InventoryCompositionCaptureTests.CaptureProxy>();
        translation = (InventoryCompositionCaptureTests.CaptureProxy)(object)language;
        translation.Translations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!
            .ToDictionary(x => "vrpg:" + x.Key, x => x.Value);
        Lang.AvailableLanguages["inventory-cairo-test"] = language;
        Lang.DefaultLocale = "inventory-cairo-test"; Lang.ChangeLanguage("inventory-cairo-test");
    }
    public (int MaximumScroll, (string Id, MenuRect Bounds, FittedButton Button)[] Controls) Draw(
        InventoryPage page, PlayerMenuSessionState session, int width, int height, string? pngPath = null)
    {
        Text.Clear();
        var api = DispatchProxy.Create<ICoreClientAPI, InventoryCompositionCaptureTests.CaptureProxy>();
        using var shell = new PlayerMenuShell(api, ElementBounds.Fixed(0, 0, width, height), PlayerMenuLayout.Resolve(width, height), session, null, _ => { }, page);
        using var surface = new ImageSurface((Format)0, width, height); using var context = new Context(surface);
        typeof(PlayerMenuShell).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, new object[] { context });
        if (pngPath != null) surface.WriteToPng(pngPath);
        var controls = (List<(string, MenuRect, FittedButton)>)typeof(PlayerMenuShell).GetField("controls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        return ((int)typeof(PlayerMenuShell).GetField("inspectorMaximumScroll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!, controls.ToArray());
    }
    public void Dispose()
    {
        Lang.AvailableLanguages.Remove("inventory-cairo-test"); Lang.DefaultLocale = oldDefault; Lang.ChangeLanguage(oldLocale);
    }
}
