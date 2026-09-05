using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerItemContractTests
{
    [Theory]
    [InlineData("gear", "gear")]
    [InlineData(" MATERIALS ", "materials")]
    [InlineData("unknown-addon-tab", "miscellaneous")]
    [InlineData("", "miscellaneous")]
    public void CategoriesNormalizeToRequiredOrFallback(string input, string expected)
    {
        Assert.Equal(expected, PlayerItemCategories.NormalizeOrFallback(input));
    }

    [Fact]
    public void ClassifierUsesDeterministicBaseline()
    {
        var classifier = new PlayerItemClassifier();

        Assert.Equal(
            new PlayerItemClassification(PlayerItemStorageKind.Unique, PlayerItemCategories.Gear),
            classifier.Classify(Context(PlayerItemAssetKind.Item, maxStack: 64, tool: true)));
        Assert.Equal(
            new PlayerItemClassification(PlayerItemStorageKind.Consumable, PlayerItemCategories.Consumables),
            classifier.Classify(Context(PlayerItemAssetKind.Item, maxStack: 64, consumable: true)));
        Assert.Equal(
            new PlayerItemClassification(PlayerItemStorageKind.Aggregate, PlayerItemCategories.Materials),
            classifier.Classify(Context(PlayerItemAssetKind.Block, maxStack: 64)));
        Assert.Equal(
            new PlayerItemClassification(PlayerItemStorageKind.Aggregate, PlayerItemCategories.Miscellaneous),
            classifier.Classify(Context(PlayerItemAssetKind.Item, maxStack: 64)));
    }

    [Fact]
    public void ExactCodeOverrideWinsAndNormalizesCategory()
    {
        var classifier = new PlayerItemClassifier();
        classifier.Register(
            "addon:quest-token",
            new PlayerItemClassification(PlayerItemStorageKind.Protected, "KEYSQUEST"));

        PlayerItemClassification actual = classifier.Classify(
            Context(PlayerItemAssetKind.Item, maxStack: 1, code: "addon:quest-token"));

        Assert.Equal(PlayerItemStorageKind.Protected, actual.StorageKind);
        Assert.Equal(PlayerItemCategories.KeysAndQuest, actual.Category);
    }

    [Fact]
    public void BuiltInEquipmentRolesAreStableAndDistinct()
    {
        Assert.Contains(PlayerEquipmentRoles.MainWeapon, PlayerEquipmentRoles.BuiltIn);
        Assert.Contains(PlayerEquipmentRoles.Head, PlayerEquipmentRoles.BuiltIn);
        Assert.Contains(PlayerEquipmentRoles.CosmeticUpperBody, PlayerEquipmentRoles.BuiltIn);
        Assert.Equal(23, PlayerEquipmentRoles.BuiltIn.Count);
    }

    [Fact]
    public void DefaultCapacitySeparatesUniqueAndAggregateLimits()
    {
        Assert.True(PlayerItemCapacity.Default.IsValid);
        Assert.NotEqual(
            PlayerItemCapacity.Default.UniqueEntryLimit,
            PlayerItemCapacity.Default.AggregateEntryLimit);
    }

    [Fact]
    public void EquipmentReferencesCanonicalEntryWithoutCopyingItemState()
    {
        var equipped = new EquippedPlayerItemRecord(PlayerEquipmentRoles.MainWeapon, "entry-7");

        Assert.Equal("entry-7", equipped.EntryId);
        Assert.Equal(PlayerEquipmentRoles.MainWeapon, equipped.RoleCode);
    }

    [Fact]
    public void MissingAddonRecordRetainsOpaqueCanonicalPayload()
    {
        var payload = new CanonicalItemPayload(
            PlayerItemSchema.CurrentVersion,
            PlayerItemAssetKind.Item,
            "missingmod:relic",
            new byte[] { 3, 1, 4 });
        var item = new CanonicalPlayerItemRecord(
            PlayerItemSchema.CurrentVersion,
            "recovery-1",
            PlayerItemStorageKind.Unique,
            PlayerItemCategories.Gear,
            payload,
            1,
            PlayerItemFlags.Protected);

        var recovery = new RecoverablePlayerItemRecord(
            item,
            PlayerItemRecoveryReason.MissingAsset,
            "migration");

        Assert.Equal("missingmod:relic", recovery.Item.Payload.AssetCode);
        Assert.Equal(new byte[] { 3, 1, 4 }, recovery.Item.Payload.ExactState);
        Assert.Equal(PlayerItemRecoveryReason.MissingAsset, recovery.Reason);
    }

    private static PlayerItemClassificationContext Context(
        PlayerItemAssetKind kind,
        int maxStack,
        bool tool = false,
        bool consumable = false,
        string code = "game:test") =>
        new(kind, code, maxStack, tool, consumable);
}
