using System;
using Vintagestory.API.Common;

namespace VRPG.Inventory;

public enum EngineItemIngressDisposition
{
    PassThrough,
    Accepted,
    Rejected
}

public sealed record EngineItemIngressResult(
    EngineItemIngressDisposition Disposition,
    long Revision,
    PlayerItemMutationRejection Rejection)
{
    public static EngineItemIngressResult PassThrough() =>
        new(EngineItemIngressDisposition.PassThrough, 0, PlayerItemMutationRejection.None);
}

/// <summary>Converts one engine-owned stack into one all-or-nothing canonical store insertion.</summary>
public sealed class EngineItemIngressService
{
    private readonly PlayerItemStoreDirectory directory;
    private readonly PlayerItemClassifier classifier;

    public EngineItemIngressService(
        PlayerItemStoreDirectory directory,
        PlayerItemClassifier? classifier = null)
    {
        this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
        this.classifier = classifier ?? new PlayerItemClassifier();
    }

    public EngineItemIngressResult Route(string playerUid, ItemStack? stack, string requestId)
    {
        if (string.IsNullOrWhiteSpace(playerUid)
            || stack?.Collectible?.Code == null
            || stack.StackSize <= 0
            || string.IsNullOrWhiteSpace(requestId))
        {
            return EngineItemIngressResult.PassThrough();
        }

        try
        {
            EncodedItemStack encoded = ItemStackCodec.Encode(stack.Clone());
            PlayerItemClassificationContext description = ItemStackCodec.Describe(stack);
            PlayerItemClassification classification = classifier.Classify(description);
            var prototype = new PlayerItemPrototype(
                classification.StorageKind,
                encoded.Payload.AssetCode,
                classification.Category,
                stack.Collectible.Code.Path,
                encoded.Payload.ExactState,
                encoded.Payload.AssetKind,
                encoded.Payload.EmbeddedReferences,
                description.StatePolicy);
            IPlayerItemStore store = directory.ForPlayer(playerUid);
            PlayerItemMutationResult result = store.Insert(
                requestId.Trim(),
                prototype,
                encoded.Quantity,
                store.Revision);
            return new EngineItemIngressResult(
                result.Success
                    ? EngineItemIngressDisposition.Accepted
                    : EngineItemIngressDisposition.Rejected,
                result.Revision,
                result.Rejection);
        }
        catch
        {
            // An engine caller must retain or drop its source stack when canonical encoding fails.
            return new EngineItemIngressResult(
                EngineItemIngressDisposition.Rejected,
                0,
                PlayerItemMutationRejection.InvalidRequest);
        }
    }
}
