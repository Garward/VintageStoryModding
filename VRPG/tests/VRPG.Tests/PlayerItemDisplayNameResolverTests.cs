using System;
using VRPG.Client.PlayerMenu.Inventory;
using VRPG.Inventory;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemDisplayNameResolverTests
{
    private readonly PlayerItemDisplayNameResolver resolver = new(() => "Unknown item");

    [Fact]
    public void NativeLocalizationReplacesPersistedDebugPath()
    {
        var entry = Entry(asset: "game:pickaxe-iron", display: "pickaxe-iron");

        Assert.Equal("Iron Pickaxe", resolver.Resolve(entry, _ => "Iron Pickaxe"));
    }

    [Fact]
    public void AuthoredGearNameWinsOverChassisLocalization()
    {
        var entry = Entry(asset: "game:sword-iron", display: "Emberwake", category: "gear",
            kind: PlayerItemStorageKind.Unique);

        Assert.Equal("Emberwake", resolver.Resolve(entry, _ => "Iron Sword"));
    }

    [Fact]
    public void OrdinaryStoredDefinitionNameFollowsNativeLocalization()
    {
        var entry = Entry(asset: "addon:ward-token", display: "Ward Token", category: "keysquest",
            kind: PlayerItemStorageKind.Protected);

        Assert.Equal("Localized Ward Token", resolver.Resolve(entry, _ => "Localized Ward Token"));
    }

    [Theory]
    [InlineData("addon:ancient_vault-key", "ancient_vault-key")]
    [InlineData("addon:ancient_vault-key", "addon:ancient_vault-key")]
    [InlineData("addon:ancient_vault-key", "Ancient Vault Key")]
    public void MissingLocalizationHumanizesTechnicalStoredNames(string asset, string display)
    {
        var entry = Entry(asset: asset, display: display);

        Assert.Equal("Ancient Vault Key", resolver.Resolve(entry, _ => null));
    }

    [Fact]
    public void MissingLocalizationUsesDistinctSemanticDefinitionName()
    {
        var entry = Entry(asset: "addon:ward-token", display: "Keeper's Ward");

        Assert.Equal("Keeper's Ward", resolver.Resolve(entry, _ => null));
    }

    [Fact]
    public void UnresolvedRecordUsesLocalizedUnknownNameWithoutConsultingRegistry()
    {
        var entry = Entry(asset: "missing:lost-relic", display: "missing:lost-relic",
            location: PlayerItemLocation.Unresolved);
        bool called = false;

        Assert.Equal("Unknown item", resolver.Resolve(entry, _ => { called = true; return "Lost Relic"; }));
        Assert.False(called);
    }

    [Fact]
    public void EmptyUnresolvedAssetUsesLocalizedUnknownName()
    {
        var entry = Entry(asset: "", display: "", location: PlayerItemLocation.Unresolved);

        Assert.Equal("Unknown item", resolver.Resolve(entry, _ => null));
    }

    [Fact]
    public void AuthoredNonAsciiGearNameIsPreservedExactly()
    {
        var entry = Entry(asset: "game:sword-iron", display: "Éclat d’Æther — 火", category: "gear",
            kind: PlayerItemStorageKind.Unique);

        Assert.Equal("Éclat d’Æther — 火", resolver.Resolve(entry, _ => "Iron Sword"));
    }

    [Fact]
    public void LongAuthoredGearNameIsNotTruncatedOrRewritten()
    {
        string name = "The Emberwake Reforged Beneath the Last Copper Moon of the Eastern Foundry";
        var entry = Entry(asset: "game:sword-iron", display: name, category: "gear",
            kind: PlayerItemStorageKind.Unique);

        Assert.Equal(name, resolver.Resolve(entry, _ => "Iron Sword"));
    }

    [Theory]
    [InlineData("addon:ancientVault_key-fragment", "Ancient Vault Key Fragment")]
    [InlineData("mod:ritual/doorSeal", "Ritual Door Seal")]
    [InlineData("  copper---ingot__stack  ", "Copper Ingot Stack")]
    public void HumanizeAssetPathRemovesDomainAndTechnicalSeparators(string asset, string expected)
    {
        Assert.Equal(expected, PlayerItemDisplayNameResolver.HumanizeAssetPath(asset));
    }

    [Fact]
    public void ResolvingANameDoesNotMutateCanonicalPacketIdentity()
    {
        var entry = Entry(asset: "game:pickaxe-iron", display: "pickaxe-iron");
        string id = entry.EntryId;
        string asset = entry.AssetCode;

        _ = resolver.Resolve(entry, _ => "Iron Pickaxe");

        Assert.Equal(id, entry.EntryId);
        Assert.Equal(asset, entry.AssetCode);
        Assert.Equal("pickaxe-iron", entry.DisplayName);
    }

    private static PlayerItemEntryPacket Entry(
        string asset,
        string display,
        string category = "materials",
        PlayerItemStorageKind kind = PlayerItemStorageKind.Aggregate,
        PlayerItemLocation location = PlayerItemLocation.Stored) => new()
    {
        EntryId = "entry-1",
        AssetCode = asset,
        DisplayName = display,
        Category = category,
        Kind = kind,
        Location = location,
        Quantity = 1
    };
}
