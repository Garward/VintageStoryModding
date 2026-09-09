using System;
using System.Linq;
using System.Text;
using VRPG.Client.PlayerMenu.Shell;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Explicit developer-only memory store. Never reads engine inventory, persists, or attaches a live channel.</summary>
public sealed class InventoryTestStoreSystem : ModSystem
{
    private GuiDialogVrpgPlayerMenu? dialog;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client
        && Environment.GetEnvironmentVariable("VRPG_PLAYER_MENU_TEST_STORE") == "1";
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.ChatCommands.Create("vrpgmenuinspect").WithDescription("Open read-only developer inventory inspection.")
            .HandleWith(_ =>
            {
                if (dialog == null)
                {
                    var data = api.ModLoader.GetModSystem<VRPGModSystem>().DataRegistry;
                    var store = new InMemoryPlayerItemStore();
                    var author = new GearInstanceAuthor(data);
                    var factory = new GearPrototypeFactory(code => api.World.GetItem(new AssetLocation(code)) != null, "");
                    foreach (var gearBase in data.GearBases.All.Take(12))
                    {
                        var authored = author.Author(new(gearBase.Code, 1, "vrpg:common", Array.Empty<GearAffixSelection>(), "test-store", "inspection", Array.Empty<string>(), 1));
                        if (authored.Instance != null && data.Rarities.Get("vrpg:common") is { } rarity
                            && api.World.GetItem(new AssetLocation(authored.Instance.VisualChassisAssetCode)) != null)
                            store.Insert(Guid.NewGuid().ToString("N"), factory.Create(authored.Instance, gearBase, rarity), 1, store.Revision);
                    }
                    void Add(string name, string code, string category, PlayerItemStorageKind kind, long quantity)
                    {
                        var result = store.Insert(Guid.NewGuid().ToString("N"), new(kind, code, category, name, Encoding.UTF8.GetBytes(name)), quantity, store.Revision);
                        if (kind == PlayerItemStorageKind.Aggregate && result.Success)
                        {
                            store.SetFlags(Guid.NewGuid().ToString("N"), result.EntryId, PlayerItemFlags.Favorite, store.Revision);
                            store.Reserve(Guid.NewGuid().ToString("N"), new[] { new PlayerItemAmount(result.EntryId, 3) }, store.Revision);
                        }
                    }
                    Add(PlayerMenuShell.T("test-copper"), "game:ingot-copper", "materials", PlayerItemStorageKind.Aggregate, 10000000);
                    Add(PlayerMenuShell.T("test-consumable"), "game:stick", "consumables", PlayerItemStorageKind.Consumable, 24);
                    Add(PlayerMenuShell.T("test-key"), "", "keysquest", PlayerItemStorageKind.Protected, 1);
                    Add(PlayerMenuShell.T("test-balance"), "", "miscellaneous", PlayerItemStorageKind.Ledger, long.MaxValue);
                    for (int i = 0; i < 10000; i++) store.ImportRecovery("recover-" + i, new(PlayerItemStorageKind.Aggregate,
                        "game:ingot-copper", "materials", PlayerMenuShell.T("test-recovery", i + 1), BitConverter.GetBytes(i)), 10000000,
                        i % 2 == 0 ? PlayerItemLocation.Overflow : PlayerItemLocation.Unresolved,
                        i % 2 == 0 ? PlayerItemRecoveryReason.CapacityExceeded : PlayerItemRecoveryReason.MissingAsset, "test-store", store.Revision);
                    dialog = new(api, new PlayerMenuController(InventoryPage.CreateReadOnlyTestStore(store, data)));
                }
                dialog.TryOpen(); return TextCommandResult.Success(PlayerMenuShell.T("read-only"));
            });
    }
    public override void Dispose() { dialog?.Dispose(); dialog = null; base.Dispose(); }
}
