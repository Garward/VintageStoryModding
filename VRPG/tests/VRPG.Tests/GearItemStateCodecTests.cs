using System;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Vintagestory.API.Datastructures;
using Xunit;

namespace VRPG.Tests;

public sealed class GearItemStateCodecTests
{
    [Fact]
    public void OptionalUniqueCodeRequiresStringWireTypeWhenPresent()
    {
        AssertWrongWireType(gear => gear.SetInt("uniqueCode", 0), "uniqueCode", "StringAttribute");
        AssertWrongWireType(gear => gear.SetBytes("uniqueCode", Array.Empty<byte>()), "uniqueCode", "StringAttribute");
        AssertWrongWireType(gear => gear["uniqueCode"] = new TreeAttribute(), "uniqueCode", "StringAttribute");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalUniqueCodeMayBeAbsentOrEmpty(bool absent)
    {
        TreeAttribute root = ItemStackCodec.DecodeCanonicalState(GearItemStateCodec.Encode(CreateFixture()));
        ITreeAttribute gear = root.GetTreeAttribute("vrpgGear");
        if (absent) gear.RemoveAttribute("uniqueCode");
        else gear.SetString("uniqueCode", "");
        Assert.True(GearItemStateCodec.TryDecode(ItemStackCodec.EncodeCanonicalState(root), out var decoded, out var error), error);
        Assert.Empty(decoded!.UniqueCode);
    }

    [Fact]
    public void ExactInstanceRoundTripsWithoutRecalculation()
    {
        GearInstance expected = CreateFixture();

        byte[] state = GearItemStateCodec.Encode(expected);
        bool decoded = GearItemStateCodec.TryDecode(state, out GearInstance? actual, out string error);

        Assert.True(decoded, error);
        Assert.NotNull(actual);
        Assert.Equal(expected.BaseCode, actual!.BaseCode);
        Assert.Equal(expected.RequiredLevel, actual.RequiredLevel);
        Assert.Equal(expected.Resolved, actual.Resolved);
        Assert.Single(actual.Affixes);
        Assert.Equal("vrpg:stalwart", actual.Affixes[0].AffixCode);
        Assert.Single(actual.Affixes[0].Modifiers);
        Assert.Equal(7.375f, actual.Affixes[0].Modifiers[0].Value);
        Assert.Equal(expected.Provenance.Tags, actual.Provenance.Tags);
        Assert.Equal(expected.UniqueRulePayload, actual.UniqueRulePayload);
    }

    [Fact]
    public void FutureSchemaAndNonFiniteStatsAreRejected()
    {
        var root = new TreeAttribute();
        var gear = new TreeAttribute();
        gear.SetInt("schemaVersion", GearInstanceSchema.CurrentVersion + 1);
        root["vrpgGear"] = gear;
        byte[] future = ItemStackCodec.EncodeCanonicalState(root);

        Assert.False(GearItemStateCodec.TryDecode(future, out _, out string futureError));
        Assert.Contains("Unsupported gear schema", futureError);

        GearInstance fixture = CreateFixture();
        GearInstance invalid = fixture with
        {
            Resolved = fixture.Resolved with { Armor = float.NaN }
        };

        Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(invalid));
    }

    [Fact]
    public void CurrentGenerationVersionRoundTripsWhileFutureAndZeroVersionsAreRejected()
    {
        Assert.Equal(1, GearInstanceSchema.CurrentGenerationRules);
        GearInstance current = CreateFixture();

        bool decoded = GearItemStateCodec.TryDecode(
            GearItemStateCodec.Encode(current), out GearInstance? actual, out string error);

        Assert.True(decoded, error);
        Assert.Equal(GearInstanceSchema.CurrentGenerationRules, actual!.GenerationRuleVersion);
        Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(
            CreateFixture() with { GenerationRuleVersion = 0 }));
        Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(
            CreateFixture() with { GenerationRuleVersion = GearInstanceSchema.CurrentGenerationRules + 1 }));
        AssertGenerationDecodeRejected(0);
        AssertGenerationDecodeRejected(GearInstanceSchema.CurrentGenerationRules + 1);
    }

    [Fact]
    public void ReturnedCollectionsAndPayloadCannotMutateInstance()
    {
        GearModifierRoll[] modifiers = { new("vrpg:armor", "increased", 7.375f) };
        GearAffixRoll[] affixes = { new("vrpg:stalwart", 4, modifiers) };
        string[] tags = { "roundtrip" };
        byte[] payload = { 9, 4, 1 };
        GearInstance instance = new(
            GearInstanceSchema.CurrentVersion,
            GearInstanceSchema.CurrentGenerationRules,
            "vrpg:warden-body",
            80,
            "vrpg:rare",
            PlayerEquipmentRoles.Body,
            "game:armor-body-chain-copper",
            affixes,
            new GearResolvedStats(0, 215f, 40f, 0, 0.05f, 0.25f, 0.25f, 0.25f, 0.25f),
            new GearProvenance("test", "codec", tags, 42UL),
            "vrpg:bulwark-rule",
            payload);

        modifiers[0] = new GearModifierRoll("vrpg:armor", "add", 1);
        affixes[0] = new GearAffixRoll("vrpg:other", 1, Array.Empty<GearModifierRoll>());
        tags[0] = "changed";
        payload[0] = 0;
        byte[] returnedPayload = instance.UniqueRulePayload;
        returnedPayload[1] = 0;

        Assert.Equal("vrpg:stalwart", instance.Affixes[0].AffixCode);
        Assert.Equal("increased", instance.Affixes[0].Modifiers[0].Operation);
        Assert.Equal("roundtrip", instance.Provenance.Tags[0]);
        Assert.Equal(new byte[] { 9, 4, 1 }, instance.UniqueRulePayload);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("increased")]
    [InlineData("more")]
    public void AcceptedModifierOperationsRoundTrip(string operation)
    {
        GearInstance fixture = CreateFixture(operation);

        bool decoded = GearItemStateCodec.TryDecode(
            GearItemStateCodec.Encode(fixture), out GearInstance? actual, out string error);

        Assert.True(decoded, error);
        Assert.Equal(operation, actual!.Affixes[0].Modifiers[0].Operation);
    }

    [Fact]
    public void UnsupportedModifierOperationAndDuplicateAffixesAreRejectedOnEncode()
    {
        Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(CreateFixture("multiply")));

        GearAffixRoll affix = new("vrpg:stalwart", 4, new[]
        {
            new GearModifierRoll("vrpg:armor", "increased", 7.375f)
        });
        GearInstance duplicate = CreateFixture() with { Affixes = new[] { affix, affix } };

        Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(duplicate));
    }

    [Fact]
    public void ModifierOperationVocabularyIsClosedAndCannotBeMutatedThroughPublicApi()
    {
        Assert.Null(typeof(GearModifierOperations).GetProperty("Supported"));
        Assert.True(GearModifierOperations.IsSupported("add"));
        Assert.True(GearModifierOperations.IsSupported("increased"));
        Assert.True(GearModifierOperations.IsSupported("more"));
        Assert.False(GearModifierOperations.IsSupported("multiply"));
    }

    [Fact]
    public void DecodeRejectsWrongWireTypesForRequiredNumericAndPayloadFields()
    {
        AssertWrongWireType(gear => gear.SetString("requiredLevel", "80"), "requiredLevel", "IntAttribute");
        AssertWrongWireType(gear => gear.SetInt("armor", 215), "armor", "FloatAttribute");
        AssertWrongWireType(gear =>
            ((TreeArrayAttribute)((TreeArrayAttribute)gear["affixes"]!).value[0]["modifiers"]!).value[0]
                .SetInt("value", 7),
            "value", "FloatAttribute");
        AssertWrongWireType(gear => gear.SetString("uniqueRulePayload", "payload"), "uniqueRulePayload", "ByteArrayAttribute");
        AssertWrongWireType(gear =>
            gear.GetTreeAttribute("provenance")!.SetInt("selectionSeed", 42),
            "selectionSeed", "LongAttribute");
    }

    private static void AssertWrongWireType(Action<TreeAttribute> replace, string field, string expectedType)
    {
        TreeAttribute root = ItemStackCodec.DecodeCanonicalState(GearItemStateCodec.Encode(CreateFixture()));
        var gear = (TreeAttribute)root.GetTreeAttribute("vrpgGear")!;
        replace(gear);

        bool decoded = GearItemStateCodec.TryDecode(
            ItemStackCodec.EncodeCanonicalState(root), out _, out string error);

        Assert.False(decoded);
        Assert.Contains(field, error);
        Assert.Contains(expectedType, error);
    }

    private static void AssertGenerationDecodeRejected(int generationRuleVersion)
    {
        TreeAttribute root = ItemStackCodec.DecodeCanonicalState(GearItemStateCodec.Encode(CreateFixture()));
        root.GetTreeAttribute("vrpgGear")!.SetInt("generationRuleVersion", generationRuleVersion);

        bool decoded = GearItemStateCodec.TryDecode(
            ItemStackCodec.EncodeCanonicalState(root), out _, out string error);

        Assert.False(decoded);
        Assert.Contains("Unsupported gear generation-rule version", error);
    }

    private static GearInstance CreateFixture(string operation = "increased") => new(
        GearInstanceSchema.CurrentVersion,
        GearInstanceSchema.CurrentGenerationRules,
        "vrpg:warden-body",
        80,
        "vrpg:rare",
        PlayerEquipmentRoles.Body,
        "game:armor-body-chain-copper",
        new[]
        {
            new GearAffixRoll("vrpg:stalwart", 4, new[]
            {
                new GearModifierRoll("vrpg:armor", operation, 7.375f)
            })
        },
        new GearResolvedStats(0, 215f, 40f, 0, 0.05f, 0.25f, 0.25f, 0.25f, 0.25f),
        new GearProvenance("test", "codec", new[] { "roundtrip" }, 42UL),
        "vrpg:bulwark-rule",
        new byte[] { 9, 4, 1 });
}
