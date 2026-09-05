using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using VRPG.Inventory.Gear;
using Xunit;

namespace VRPG.Tests;

public sealed class StarterGearSetFactoryTests
{
    [Theory]
    [InlineData("vrpg:corroder")]
    [InlineData("vrpg:pilferer")]
    [InlineData("vrpg:scrapper")]
    [InlineData("vrpg:smith")]
    [InlineData("vrpg:warden")]
    public void StarterClassProducesDeterministicWeaponAndFiveArmorPieces(string classCode)
    {
        VRPGDataRegistry data = Registry();
        StarterGearSetResult first = Factory(data).Create(classCode);
        StarterGearSetResult second = Factory(data).Create(classCode);

        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.Equal(classCode, first.ClassCode);
        Assert.Equal(6, first.Items.Length);
        var armor = new Dictionary<string, float>
        {
            [PlayerEquipmentRoles.MainWeapon] = 0,
            [PlayerEquipmentRoles.Head] = 15,
            [PlayerEquipmentRoles.Body] = 35,
            [PlayerEquipmentRoles.Legs] = 25,
            [PlayerEquipmentRoles.Hands] = 10,
            [PlayerEquipmentRoles.Feet] = 15
        };
        Assert.Equal(armor.Keys.Order(), first.Items.Select(item => item.RoleCode).Order());
        Assert.All(first.Items, item =>
        {
            GearInstance gear = Decode(item.Prototype.ExactState);
            Assert.Equal(1, gear.RequiredLevel);
            Assert.Equal("vrpg:common", gear.RarityCode);
            Assert.Empty(gear.Affixes);
            Assert.Empty(gear.UniqueCode);
            Assert.Equal(item.RoleCode, gear.EquipmentRole);
            Assert.Equal(armor[item.RoleCode], gear.Resolved.Armor);
            Assert.Equal("starter", gear.Provenance.SourceKind);
            Assert.Equal(classCode, gear.Provenance.SourceCode);
            Assert.Equal(new[] { classCode }, gear.Provenance.Tags);
            Assert.Equal(PlayerItemStorageKind.Unique, item.Prototype.Kind);
            Assert.Equal(PlayerItemCategories.Gear, item.Prototype.Category);
            GearBaseDefinition gearBase = data.GearBases.Get(gear.BaseCode)!;
            Assert.Equal(gearBase.Name, item.Prototype.DisplayName);
            Assert.Equal(gearBase.VisualChassis, item.Prototype.AssetCode);
            Assert.StartsWith(classCode + "-starter-", gear.BaseCode);
            Assert.Empty(gearBase.Implicits);
        });
        Assert.Equal(100f, first.Items.Sum(item => Decode(item.Prototype.ExactState).Resolved.Armor));
        Assert.True(Decode(first.Items.Single(item => item.RoleCode == PlayerEquipmentRoles.MainWeapon).Prototype.ExactState).Resolved.WeaponPower > 0);
        Assert.Equal(first.Items.Select(item => Convert.ToHexString(item.Prototype.ExactState)),
            second.Items.Select(item => Convert.ToHexString(item.Prototype.ExactState)));
        Assert.Equal(6, first.Items.Select(item => Decode(item.Prototype.ExactState).Provenance.SelectionSeed).Distinct().Count());
    }

    [Fact]
    public void SeedUsesCanonicalClassAndBigEndianSha256()
    {
        StarterGearSetFactory factory = Factory(Registry());
        StarterGearSetResult result = factory.Create("SMITH");
        Assert.True(result.Success, result.Error);
        GearInstance weapon = Decode(result.Items.Single(item => item.RoleCode == PlayerEquipmentRoles.MainWeapon).Prototype.ExactState);
        Assert.Equal(0x2BC2E6DEC47E7C7DUL, weapon.Provenance.SelectionSeed);
        Assert.Equal(factory.Create("vrpg:smith").Items.Select(item => Convert.ToHexString(item.Prototype.ExactState)),
            result.Items.Select(item => Convert.ToHexString(item.Prototype.ExactState)));
    }

    [Theory]
    [InlineData("vrpg:missing")]
    [InlineData("vrpg:handler")]
    public void RejectsUnknownOrNonStarterClass(string classCode) => AssertRejected(Factory(Registry()).Create(classCode));

    [Fact]
    public void RejectsMissingCommonRarity()
    {
        VRPGDataRegistry data = Registry();
        data.Rarities.Load(Array.Empty<GearRarityDefinition>());
        AssertRejected(Factory(data).Create("vrpg:smith"));
    }

    [Fact]
    public void AuthorRejectionReturnsNoPartialSet()
    {
        VRPGDataRegistry data = Registry();
        data.GearBases.Get("vrpg:smith-starter-feet")!.LevelOneArmor = float.NaN;
        AssertRejected(Factory(data).Create("vrpg:smith"));
    }

    [Fact]
    public void MissingChassisRejectsInsteadOfSubstitutingFallback()
    {
        VRPGDataRegistry data = Registry();
        data.GearBases.Get("vrpg:smith-starter-feet")!.VisualChassis = "missing:boots";
        AssertRejected(Factory(data).Create("vrpg:smith"));
    }

    [Fact]
    public void MissingBaseReturnsNoPartialSet()
    {
        VRPGDataRegistry data = Registry();
        data.GearBases.Load(data.GearBases.All.Where(item => item.Code != "vrpg:smith-starter-feet").ToArray());
        AssertRejected(Factory(data).Create("vrpg:smith"));
    }

    [Fact]
    public void InvalidRoleCannotProduceAnIncompleteSet()
    {
        VRPGDataRegistry data = Registry();
        data.Classes.Get("vrpg:smith")!.Starter!.Items[5].EquipmentRole = PlayerEquipmentRoles.Body;
        AssertRejected(Factory(data).Create("vrpg:smith"));
    }

    internal static VRPGDataRegistry Registry()
    {
        var data = new VRPGDataRegistry();
        data.Classes.Load(Load<ClassDefinition>("classes"));
        data.GearBases.Load(Load<GearBaseDefinition>("gear"));
        data.Rarities.Load(Load<GearRarityDefinition>("rarities"));
        data.Affixes.Load(Load<GearAffixDefinition>("affixes"));
        data.Skills.Load(Load<SkillDefinition>("skills"));
        GearDefinitionValidator.ValidateOrThrow(data.GearBases.All, data.Rarities.All, data.Affixes.All, ChassisExists);
        var errors = new List<string>();
        foreach (ClassDefinition definition in data.Classes.All.Where(definition => definition.Starter != null))
            StarterLoadoutDefinitionValidator.Validate(definition, data.Skills.All, data.GearBases.All, ChassisExists, errors);
        Assert.Empty(errors);
        return data;
    }

    private static IEnumerable<T> Load<T>(string folder) =>
        Directory.GetFiles(Path.Combine(AssetRoot(), folder), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonConvert.DeserializeObject<T>(File.ReadAllText(path))!);

    internal static bool ChassisExists(string code) => code is
        "game:hammer-iron" or "game:spear-generic-iron" or "game:knife-generic-iron" or
        "game:club-scrapmace-scrap" or "game:club-scrap-scrap" or "game:gear-rusty" or
        "game:armor-head-chain-copper" or "game:armor-body-chain-copper" or "game:armor-legs-chain-copper" or
        "game:armor-head-lamellar-wood" or "game:armor-body-lamellar-wood" or "game:armor-legs-lamellar-wood" or
        "game:armor-head-sewn-leather" or "game:armor-body-sewn-leather" or "game:armor-legs-sewn-leather" or
        "game:armor-head-tailored-orange-linen" or "game:armor-body-tailored-orange-linen" or "game:armor-legs-tailored-orange-linen" or
        "game:clothes-hand-heavy-leather-gloves" or "game:clothes-hand-forlorn-bracer" or
        "game:clothes-hand-malefactor-gloves" or "game:clothes-hand-cuffsorange" or
        "game:clothes-foot-metalcap-boots" or "game:clothes-foot-soldier-boots" or
        "game:clothes-foot-malefactor-boots" or "game:clothes-foot-alchemist" or "game:clothes-foot-worn-leather-boots";

    private static StarterGearSetFactory Factory(VRPGDataRegistry data) =>
        new(data, new GearInstanceAuthor(data), new GearPrototypeFactory(ChassisExists, "game:gear-rusty"));

    private static GearInstance Decode(byte[] state)
    {
        Assert.True(GearItemStateCodec.TryDecode(state, out GearInstance? gear, out string error), error);
        return gear!;
    }

    private static void AssertRejected(StarterGearSetResult result)
    {
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error);
        Assert.Empty(result.Items);
    }

    internal static string AssetRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "assets", "vrpg", "vrpg");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("VRPG content fixtures were not found.");
    }
}
