using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VRPG.Inventory;

public interface ICollectibleResolver
{
    Item? GetItem(AssetLocation code);
    Block? GetBlock(AssetLocation code);
}

public sealed class WorldCollectibleResolver : ICollectibleResolver
{
    private readonly IWorldAccessor world;

    public WorldCollectibleResolver(IWorldAccessor world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public Item? GetItem(AssetLocation code) => world.GetItem(code);
    public Block? GetBlock(AssetLocation code) => world.GetBlock(code);
}

public sealed record EncodedItemStack(CanonicalItemPayload Payload, int Quantity);

public enum ItemStackMaterializationFailure
{
    None,
    MissingAsset,
    MissingEmbeddedAsset,
    QuantityExceedsEngineLimit
}

public sealed record ItemStackMaterialization(
    bool Resolved,
    ItemStack? Stack,
    CanonicalItemPayload RecoverablePayload,
    ItemStackMaterializationFailure Failure)
{
    public static ItemStackMaterialization Rejected(
        CanonicalItemPayload payload,
        ItemStackMaterializationFailure failure) =>
        new(false, null, payload, failure);

    public static ItemStackMaterialization Success(ItemStack stack, CanonicalItemPayload payload) =>
        new(true, stack, payload, ItemStackMaterializationFailure.None);
}

/// <summary>
/// Converts the top-level engine stack to stable asset-code identity. Synchronized Attributes are
/// exact state; TempAttributes and localized display text are intentionally non-canonical. Nested
/// ItemstackAttribute registry remapping is a separate required codec stage.
/// </summary>
public static class ItemStackCodec
{
    public static EncodedItemStack Encode(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.StackSize <= 0)
            throw new ArgumentException("Only positive item stacks can be encoded.", nameof(stack));
        if (stack.Collectible?.Code == null)
            throw new ArgumentException("The item stack must resolve to a coded collectible.", nameof(stack));

        PlayerItemAssetKind kind = stack.Class == EnumItemClass.Block
            ? PlayerItemAssetKind.Block
            : PlayerItemAssetKind.Item;
        ITreeAttribute canonicalAttributes = stack.Attributes.Clone();
        var references = new Dictionary<(PlayerItemAssetKind Kind, int Id), CanonicalCollectibleReference>();
        var canonicalIds = new Dictionary<(PlayerItemAssetKind Kind, string Code), int>();
        CanonicalizeEmbeddedStacks(canonicalAttributes, references, canonicalIds);
        byte[] exactState = EncodeCanonicalState(canonicalAttributes);
        var payload = new CanonicalItemPayload(
            PlayerItemSchema.CurrentVersion,
            kind,
            stack.Collectible.Code.ToString(),
            exactState,
            new List<CanonicalCollectibleReference>(references.Values).ToArray());
        return new EncodedItemStack(payload, stack.StackSize);
    }

    public static ItemStackMaterialization Materialize(
        CanonicalItemPayload payload,
        int quantity,
        ICollectibleResolver resolver)
    {
        Validate(payload, quantity);
        ArgumentNullException.ThrowIfNull(resolver);

        var code = new AssetLocation(payload.AssetCode);
        CollectibleObject? collectible = payload.AssetKind switch
        {
            PlayerItemAssetKind.Item => resolver.GetItem(code),
            PlayerItemAssetKind.Block => resolver.GetBlock(code),
            _ => null
        };
        if (collectible == null)
            return ItemStackMaterialization.Rejected(
                Clone(payload),
                ItemStackMaterializationFailure.MissingAsset);
        if (quantity > collectible.MaxStackSize)
            return ItemStackMaterialization.Rejected(
                Clone(payload),
                ItemStackMaterializationFailure.QuantityExceedsEngineLimit);

        TreeAttribute attributes = DecodeCanonicalState(payload.ExactState);
        if (!TryRemapEmbeddedStacks(attributes, payload.EmbeddedReferences, resolver))
            return ItemStackMaterialization.Rejected(
                Clone(payload),
                ItemStackMaterializationFailure.MissingEmbeddedAsset);

        var stack = new ItemStack(collectible, quantity)
        {
            Attributes = attributes
        };
        return ItemStackMaterialization.Success(stack, Clone(payload));
    }

    public static PlayerItemClassificationContext Describe(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (stack.Collectible?.Code == null)
            throw new ArgumentException("The item stack must resolve to a coded collectible.", nameof(stack));

        return new PlayerItemClassificationContext(
            stack.Class == EnumItemClass.Block ? PlayerItemAssetKind.Block : PlayerItemAssetKind.Item,
            stack.Collectible.Code.ToString(),
            stack.Collectible.MaxStackSize,
            stack.Collectible.Tool.HasValue,
            stack.Collectible.NutritionProps != null,
            HasEvolvingState(stack)
                ? PlayerItemStatePolicy.FrozenInRecovery : PlayerItemStatePolicy.ExactSnapshot);
    }

    private static bool HasEvolvingState(ItemStack stack) =>
        stack.Collectible == null
        || stack.Collectible.TransitionableProps?.Length > 0
        || stack.Collectible.GetType().GetMethod(nameof(CollectibleObject.GetTransitionableProperties))
            ?.DeclaringType != typeof(CollectibleObject)
        || HasEvolvingAttributes(stack.Attributes);

    private static bool HasEvolvingAttributes(ITreeAttribute attributes)
    {
        if (attributes.HasAttribute("temperature") || attributes.HasAttribute("transitionstate")) return true;
        foreach (var pair in attributes)
        {
            if (pair.Value is ItemstackAttribute { value: not null } nested && HasEvolvingState(nested.value)) return true;
            if (pair.Value is ITreeAttribute child && HasEvolvingAttributes(child)) return true;
            if (pair.Value is TreeArrayAttribute trees)
                foreach (var tree in trees.value)
                    if (HasEvolvingAttributes(tree)) return true;
        }
        return false;
    }

    public static byte[] EncodeCanonicalState(ITreeAttribute attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ITreeAttribute clone = attributes.Clone();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        clone.ToBytes(writer);
        writer.Flush();
        return stream.ToArray();
    }

    public static TreeAttribute DecodeCanonicalState(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var attributes = new TreeAttribute();
        using var stream = new MemoryStream((byte[])bytes.Clone(), writable: false);
        using var reader = new BinaryReader(stream);
        attributes.FromBytes(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("The item attribute payload contains trailing data.");
        return attributes;
    }

    private static void CanonicalizeEmbeddedStacks(
        ITreeAttribute tree,
        Dictionary<(PlayerItemAssetKind Kind, int Id), CanonicalCollectibleReference> references,
        Dictionary<(PlayerItemAssetKind Kind, string Code), int> canonicalIds)
    {
        foreach (KeyValuePair<string, IAttribute> pair in tree)
        {
            switch (pair.Value)
            {
                case ITreeAttribute child:
                    CanonicalizeEmbeddedStacks(child, references, canonicalIds);
                    break;
                case TreeArrayAttribute trees:
                    foreach (TreeAttribute nestedTree in trees.value)
                        CanonicalizeEmbeddedStacks(nestedTree, references, canonicalIds);
                    break;
                case ItemstackAttribute { value: not null } stackAttribute:
                    ItemStack nested = stackAttribute.value;
                    if (nested.Collectible?.Code == null)
                        throw new InvalidDataException("A nested item stack is unresolved and cannot be remapped safely.");
                    PlayerItemAssetKind kind = nested.Class == EnumItemClass.Block
                        ? PlayerItemAssetKind.Block
                        : PlayerItemAssetKind.Item;
                    string code = nested.Collectible.Code.ToString();
                    var assetKey = (kind, code.ToLowerInvariant());
                    if (!canonicalIds.TryGetValue(assetKey, out int canonicalId))
                    {
                        canonicalId = canonicalIds.Count + 1;
                        canonicalIds.Add(assetKey, canonicalId);
                    }
                    nested.Id = canonicalId;
                    references[(kind, canonicalId)] =
                        new CanonicalCollectibleReference(kind, canonicalId, code);
                    CanonicalizeEmbeddedStacks(nested.Attributes, references, canonicalIds);
                    break;
            }
        }
    }

    private static bool TryRemapEmbeddedStacks(
        ITreeAttribute tree,
        CanonicalCollectibleReference[]? references,
        ICollectibleResolver resolver)
    {
        var byRegistryId = new Dictionary<(PlayerItemAssetKind Kind, int Id), CanonicalCollectibleReference>();
        foreach (CanonicalCollectibleReference reference in references ?? Array.Empty<CanonicalCollectibleReference>())
        {
            if (string.IsNullOrWhiteSpace(reference.AssetCode)) return false;
            var key = (reference.AssetKind, reference.SourceRegistryId);
            if (byRegistryId.TryGetValue(key, out CanonicalCollectibleReference? existing)
                && !string.Equals(existing.AssetCode, reference.AssetCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            byRegistryId[key] = reference;
        }

        return TryRemapTree(tree, byRegistryId, resolver);
    }

    private static bool TryRemapTree(
        ITreeAttribute tree,
        IReadOnlyDictionary<(PlayerItemAssetKind Kind, int Id), CanonicalCollectibleReference> references,
        ICollectibleResolver resolver)
    {
        foreach (KeyValuePair<string, IAttribute> pair in tree)
        {
            switch (pair.Value)
            {
                case ITreeAttribute child when !TryRemapTree(child, references, resolver):
                    return false;
                case TreeArrayAttribute trees:
                    foreach (TreeAttribute nestedTree in trees.value)
                    {
                        if (!TryRemapTree(nestedTree, references, resolver)) return false;
                    }
                    break;
                case ItemstackAttribute { value: not null } stackAttribute:
                    ItemStack serialized = stackAttribute.value;
                    PlayerItemAssetKind kind = serialized.Class == EnumItemClass.Block
                        ? PlayerItemAssetKind.Block
                        : PlayerItemAssetKind.Item;
                    if (!references.TryGetValue((kind, serialized.Id), out CanonicalCollectibleReference? reference))
                        return false;
                    var code = new AssetLocation(reference.AssetCode);
                    CollectibleObject? collectible = kind == PlayerItemAssetKind.Item
                        ? resolver.GetItem(code)
                        : resolver.GetBlock(code);
                    if (collectible == null) return false;
                    if (!TryRemapTree(serialized.Attributes, references, resolver)) return false;
                    stackAttribute.value = new ItemStack(collectible, serialized.StackSize)
                    {
                        Attributes = serialized.Attributes
                    };
                    break;
            }
        }

        return true;
    }

    private static void Validate(CanonicalItemPayload payload, int quantity)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.SchemaVersion != PlayerItemSchema.CurrentVersion)
            throw new InvalidDataException("Unsupported canonical item schema version.");
        if (string.IsNullOrWhiteSpace(payload.AssetCode))
            throw new InvalidDataException("The canonical item asset code is missing.");
        if (payload.ExactState == null)
            throw new InvalidDataException("The canonical item state is missing.");
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
    }

    private static CanonicalItemPayload Clone(CanonicalItemPayload payload) =>
        payload with
        {
            ExactState = (byte[])payload.ExactState.Clone(),
            EmbeddedReferences = payload.EmbeddedReferences == null
                ? null
                : (CanonicalCollectibleReference[])payload.EmbeddedReferences.Clone()
        };
}
