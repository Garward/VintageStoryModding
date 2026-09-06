using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRPG.Inventory;
using Vintagestory.API.Datastructures;

namespace VRPG.Inventory.Gear;

/// <summary>Serializes immutable VRPG gear state in the canonical item-state subtree.</summary>
public static class GearItemStateCodec
{
    private const string GearTreeKey = "vrpgGear";
    private const string AffixesKey = "affixes";
    private const string ModifiersKey = "modifiers";
    private const string ProvenanceKey = "provenance";

    public static byte[] Encode(GearInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        try
        {
            ValidateForWrite(instance);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException(exception.Message, nameof(instance), exception);
        }

        var root = new TreeAttribute();
        var gear = new TreeAttribute();
        gear.SetInt("schemaVersion", instance.SchemaVersion);
        gear.SetInt("generationRuleVersion", instance.GenerationRuleVersion);
        gear.SetString("baseCode", instance.BaseCode);
        gear.SetInt("requiredLevel", instance.RequiredLevel);
        gear.SetString("rarityCode", instance.RarityCode);
        gear.SetString("equipmentRole", instance.EquipmentRole);
        gear.SetString("visualChassis", instance.VisualChassisAssetCode);
        WriteResolved(gear, instance.Resolved);
        WriteAffixes(gear, instance.Affixes);
        WriteProvenance(gear, instance.Provenance);
        gear.SetString("uniqueCode", instance.UniqueCode);
        gear.SetBytes("uniqueRulePayload", instance.UniqueRulePayload);
        root[GearTreeKey] = gear;
        return ItemStackCodec.EncodeCanonicalState(root);
    }

    public static bool TryDecode(byte[] state, out GearInstance? instance, out string error)
    {
        instance = null;
        error = string.Empty;
        if (state == null)
        {
            error = "Gear state is missing.";
            return false;
        }

        try
        {
            TreeAttribute root = ItemStackCodec.DecodeCanonicalState((byte[])state.Clone());
            ITreeAttribute? gear = root.GetTreeAttribute(GearTreeKey);
            if (gear == null) throw new InvalidDataException("Gear state is missing the vrpgGear subtree.");

            int schemaVersion = RequiredInt(gear, "schemaVersion");
            if (schemaVersion != GearInstanceSchema.CurrentVersion)
                throw new InvalidDataException($"Unsupported gear schema version {schemaVersion}.");
            int generationRuleVersion = RequiredInt(gear, "generationRuleVersion");
            if (generationRuleVersion is <= 0 or > GearInstanceSchema.CurrentGenerationRules)
                throw new InvalidDataException($"Unsupported gear generation-rule version {generationRuleVersion}.");

            GearInstance decoded = new(
                schemaVersion,
                generationRuleVersion,
                RequiredNormalized(gear, "baseCode"),
                RequiredLevel(gear),
                RequiredNormalized(gear, "rarityCode"),
                RequiredNormalized(gear, "equipmentRole"),
                RequiredNormalized(gear, "visualChassis"),
                ReadAffixes(gear),
                ReadResolved(gear),
                ReadProvenance(gear),
                OptionalNormalized(gear, "uniqueCode"),
                RequiredBytes(gear, "uniqueRulePayload"));
            ValidateForWrite(decoded);
            instance = decoded;
            return true;
        }
        catch (Exception exception)
        {
            error = "Malformed VRPG gear state: " + exception.Message;
            return false;
        }
    }

    private static void WriteResolved(ITreeAttribute gear, GearResolvedStats resolved)
    {
        gear.SetFloat("weaponPower", resolved.WeaponPower);
        gear.SetFloat("armor", resolved.Armor);
        gear.SetFloat("maximumHealth", resolved.MaximumHealth);
        gear.SetFloat("maximumMagicShield", resolved.MaximumMagicShield);
        gear.SetFloat("evasion", resolved.Evasion);
        gear.SetFloat("fireResistance", resolved.FireResistance);
        gear.SetFloat("coldResistance", resolved.ColdResistance);
        gear.SetFloat("lightningResistance", resolved.LightningResistance);
        gear.SetFloat("rustResistance", resolved.RustResistance);
    }

    private static GearResolvedStats ReadResolved(ITreeAttribute gear) => new(
        RequiredNonNegativeFiniteFloat(gear, "weaponPower"),
        RequiredNonNegativeFiniteFloat(gear, "armor"),
        RequiredNonNegativeFiniteFloat(gear, "maximumHealth"),
        RequiredNonNegativeFiniteFloat(gear, "maximumMagicShield"),
        RequiredNonNegativeFiniteFloat(gear, "evasion"),
        RequiredNonNegativeFiniteFloat(gear, "fireResistance"),
        RequiredNonNegativeFiniteFloat(gear, "coldResistance"),
        RequiredNonNegativeFiniteFloat(gear, "lightningResistance"),
        RequiredNonNegativeFiniteFloat(gear, "rustResistance"));

    private static void WriteAffixes(ITreeAttribute gear, IReadOnlyList<GearAffixRoll> affixes)
    {
        TreeAttribute[] encoded = affixes.Select(affix =>
        {
            var tree = new TreeAttribute();
            tree.SetString("affixCode", affix.AffixCode);
            tree.SetInt("tier", affix.Tier);
            tree[ModifiersKey] = new TreeArrayAttribute(affix.Modifiers.Select(modifier =>
            {
                var modifierTree = new TreeAttribute();
                modifierTree.SetString("stat", modifier.Stat);
                modifierTree.SetString("operation", modifier.Operation);
                modifierTree.SetFloat("value", modifier.Value);
                return modifierTree;
            }).ToArray());
            return tree;
        }).ToArray();
        gear[AffixesKey] = new TreeArrayAttribute(encoded);
    }

    private static GearAffixRoll[] ReadAffixes(ITreeAttribute gear)
    {
        TreeAttribute[] trees = RequiredTreeArray(gear, AffixesKey);
        var affixes = new GearAffixRoll[trees.Length];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < trees.Length; index++)
        {
            TreeAttribute affixTree = trees[index] ?? throw new InvalidDataException("Gear affix cannot be null.");
            string affixCode = RequiredNormalized(affixTree, "affixCode");
            if (!identities.Add(affixCode)) throw new InvalidDataException($"Duplicate gear affix '{affixCode}'.");
            int tier = RequiredInt(affixTree, "tier");
            if (tier <= 0) throw new InvalidDataException("Gear affix tier must be positive.");

            TreeAttribute[] modifierTrees = RequiredTreeArray(affixTree, ModifiersKey);
            var modifiers = new GearModifierRoll[modifierTrees.Length];
            for (int modifierIndex = 0; modifierIndex < modifierTrees.Length; modifierIndex++)
            {
                TreeAttribute modifierTree = modifierTrees[modifierIndex]
                    ?? throw new InvalidDataException("Gear modifier cannot be null.");
                string operation = RequiredNormalized(modifierTree, "operation");
                if (!GearModifierOperations.IsSupported(operation))
                    throw new InvalidDataException($"Unsupported gear modifier operation '{operation}'.");
                modifiers[modifierIndex] = new GearModifierRoll(
                    RequiredNormalized(modifierTree, "stat"),
                    operation,
                    RequiredFiniteFloat(modifierTree, "value"));
            }

            affixes[index] = new GearAffixRoll(affixCode, tier, modifiers);
        }

        return affixes;
    }

    private static void WriteProvenance(ITreeAttribute gear, GearProvenance provenance)
    {
        var tree = new TreeAttribute();
        tree.SetString("sourceKind", provenance.SourceKind);
        tree.SetString("sourceCode", provenance.SourceCode);
        tree.SetStringArray("tags", provenance.Tags.ToArray());
        tree.SetLong("selectionSeed", unchecked((long)provenance.SelectionSeed));
        gear[ProvenanceKey] = tree;
    }

    private static GearProvenance ReadProvenance(ITreeAttribute gear)
    {
        ITreeAttribute provenance = gear.GetTreeAttribute(ProvenanceKey)
            ?? throw new InvalidDataException("Gear provenance is missing.");
        string[] tags = RequiredStringArray(provenance, "tags");
        foreach (string tag in tags) ValidateNormalized(tag, "provenance tag");
        return new GearProvenance(
            RequiredNormalized(provenance, "sourceKind"),
            RequiredNormalized(provenance, "sourceCode"),
            (string[])tags.Clone(),
            unchecked((ulong)RequiredLong(provenance, "selectionSeed")));
    }

    private static void ValidateForWrite(GearInstance instance)
    {
        if (instance.SchemaVersion != GearInstanceSchema.CurrentVersion)
            throw new ArgumentException("Unsupported gear schema version.", nameof(instance));
        if (instance.GenerationRuleVersion is <= 0 or > GearInstanceSchema.CurrentGenerationRules)
            throw new ArgumentException("Unsupported gear generation-rule version.", nameof(instance));
        if (instance.RequiredLevel is < 1 or > 100)
            throw new ArgumentException("Gear required level must be between 1 and 100.", nameof(instance));
        ValidateNormalized(instance.BaseCode, "base code");
        ValidateNormalized(instance.RarityCode, "rarity code");
        ValidateNormalized(instance.EquipmentRole, "equipment role");
        ValidateNormalized(instance.VisualChassisAssetCode, "visual chassis");
        ValidateOptionalNormalized(instance.UniqueCode, "unique code");
        ValidateResolved(instance.Resolved);
        ValidateProvenance(instance.Provenance);

        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (GearAffixRoll affix in instance.Affixes)
        {
            ArgumentNullException.ThrowIfNull(affix);
            ValidateNormalized(affix.AffixCode, "affix code");
            if (!identities.Add(affix.AffixCode))
                throw new ArgumentException($"Duplicate gear affix '{affix.AffixCode}'.", nameof(instance));
            if (affix.Tier <= 0) throw new ArgumentException("Gear affix tier must be positive.", nameof(instance));
            foreach (GearModifierRoll modifier in affix.Modifiers)
            {
                ArgumentNullException.ThrowIfNull(modifier);
                ValidateNormalized(modifier.Stat, "modifier stat");
                if (!GearModifierOperations.IsSupported(modifier.Operation))
                    throw new ArgumentException($"Unsupported gear modifier operation '{modifier.Operation}'.", nameof(instance));
                ValidateFinite(modifier.Value, "modifier value");
            }
        }
    }

    private static void ValidateResolved(GearResolvedStats resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ValidateNonNegativeFinite(resolved.WeaponPower, "weapon power");
        ValidateNonNegativeFinite(resolved.Armor, "armor");
        ValidateNonNegativeFinite(resolved.MaximumHealth, "maximum health");
        ValidateNonNegativeFinite(resolved.MaximumMagicShield, "maximum magic shield");
        ValidateNonNegativeFinite(resolved.Evasion, "evasion");
        ValidateNonNegativeFinite(resolved.FireResistance, "fire resistance");
        ValidateNonNegativeFinite(resolved.ColdResistance, "cold resistance");
        ValidateNonNegativeFinite(resolved.LightningResistance, "lightning resistance");
        ValidateNonNegativeFinite(resolved.RustResistance, "rust resistance");
    }

    private static void ValidateProvenance(GearProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ValidateNormalized(provenance.SourceKind, "provenance source kind");
        ValidateNormalized(provenance.SourceCode, "provenance source code");
        foreach (string tag in provenance.Tags) ValidateNormalized(tag, "provenance tag");
    }

    private static int RequiredLevel(ITreeAttribute tree)
    {
        int level = RequiredInt(tree, "requiredLevel");
        if (level is < 1 or > 100) throw new InvalidDataException("Gear required level must be between 1 and 100.");
        return level;
    }

    private static int RequiredInt(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        if (tree[key] is not IntAttribute)
            throw new InvalidDataException($"Gear field '{key}' must use IntAttribute.");
        return tree.GetInt(key);
    }

    private static long RequiredLong(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        if (tree[key] is not LongAttribute)
            throw new InvalidDataException($"Gear field '{key}' must use LongAttribute.");
        return tree.GetLong(key);
    }

    private static float RequiredFiniteFloat(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        if (tree[key] is not FloatAttribute)
            throw new InvalidDataException($"Gear field '{key}' must use FloatAttribute.");
        float value = tree.GetFloat(key);
        ValidateFinite(value, key);
        return value;
    }

    private static float RequiredNonNegativeFiniteFloat(ITreeAttribute tree, string key)
    {
        float value = RequiredFiniteFloat(tree, key);
        ValidateNonNegativeFinite(value, key);
        return value;
    }

    private static string RequiredNormalized(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        string? value = tree.GetString(key);
        ValidateNormalized(value, key);
        return value!;
    }

    private static string OptionalNormalized(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) return string.Empty;
        if (tree[key] is not StringAttribute)
            throw new InvalidDataException($"Gear field '{key}' must use StringAttribute.");
        string? value = tree.GetString(key);
        ValidateOptionalNormalized(value, key);
        return value ?? string.Empty;
    }

    private static byte[] RequiredBytes(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        if (tree[key] is not ByteArrayAttribute attribute)
            throw new InvalidDataException($"Gear field '{key}' must use ByteArrayAttribute.");
        return (byte[])attribute.value.Clone();
    }

    private static string[] RequiredStringArray(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key)) throw new InvalidDataException($"Gear field '{key}' is missing.");
        string[]? values = (tree[key] as StringArrayAttribute)?.value;
        return values == null
            ? throw new InvalidDataException($"Gear field '{key}' is invalid.")
            : (string[])values.Clone();
    }

    private static TreeAttribute[] RequiredTreeArray(ITreeAttribute tree, string key)
    {
        if (!tree.HasAttribute(key) || tree[key] is not TreeArrayAttribute attributes)
            throw new InvalidDataException($"Gear field '{key}' is missing or invalid.");
        return (TreeAttribute[])attributes.value.Clone();
    }

    private static void ValidateNormalized(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException($"Gear {field} must be nonempty and normalized.");
    }

    private static void ValidateOptionalNormalized(string? value, string field)
    {
        if (!string.IsNullOrEmpty(value)) ValidateNormalized(value, field);
    }

    private static void ValidateFinite(float value, string field)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException($"Gear {field} must be finite.");
    }

    private static void ValidateNonNegativeFinite(float value, string field)
    {
        ValidateFinite(value, field);
        if (value < 0) throw new InvalidDataException($"Gear {field} must be non-negative.");
    }
}
