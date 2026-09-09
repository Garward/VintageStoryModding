using System;
using System.Collections.Generic;
using System.Linq;
using VRPG.Inventory.Persistence;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VRPG.Inventory.Migration;

/// <summary>Audited server-side view of every Vanilla inventory owned by one player.</summary>
public sealed class VintageStoryLegacyInventorySource : ILegacyPlayerInventorySource
{
    private static readonly string[] ManagedClasses =
    {
        GlobalConstants.hotBarInvClassName,
        GlobalConstants.backpackInvClassName,
        GlobalConstants.characterInvClassName,
        GlobalConstants.mousecursorInvClassName,
        GlobalConstants.craftingInvClassName
    };

    private readonly IServerPlayer player;
    private readonly PlayerItemClassifier classifier;
    private readonly WorldCollectibleResolver resolver;

    public VintageStoryLegacyInventorySource(
        IServerPlayer player,
        PlayerItemClassifier? classifier = null)
    {
        this.player = player ?? throw new ArgumentNullException(nameof(player));
        this.classifier = classifier ?? new PlayerItemClassifier();
        resolver = new WorldCollectibleResolver(player.Entity.World);
    }

    public string PlayerUid => player.PlayerUID;

    public LegacyInventoryInspection Inspect()
    {
        try
        {
            return InspectAvailableInventories();
        }
        catch (Exception exception)
        {
            player.Entity?.World?.Logger?.Error("[VRPG] Legacy inventory inspection failed: {0}", exception);
            return new LegacyInventoryInspection(PlayerUid, Array.Empty<LegacyItemSlotState>(),
                new[] { "The original inventory could not be checked. Try again. If this continues, check the server log." });
        }
    }

    private LegacyInventoryInspection InspectAvailableInventories()
    {
        var slots = new List<LegacyItemSlotState>();
        var blocking = new List<string>();
        HashSet<string> managedIds = ManagedClasses
            .Select(player.InventoryManager.GetInventoryName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (IInventory opened in player.InventoryManager.OpenedInventories)
        {
            if (!managedIds.Contains(opened.InventoryID))
                blocking.Add("Close the open container and try again.");
        }

        foreach (string inventoryClass in ManagedClasses)
        {
            IInventory? inventory = player.InventoryManager.GetOwnInventory(inventoryClass);
            if (inventory == null)
            {
                blocking.Add($"The {FriendlyInventoryName(inventoryClass)} inventory is unavailable.");
                continue;
            }
            if (inventory is InventoryBase { InvNetworkUtil.PauseInventoryUpdates: true })
                blocking.Add("Finish the current item move and try again.");

            if (inventoryClass == GlobalConstants.backpackInvClassName)
                InspectBackpacks(inventory, slots, blocking);
            else
                InspectOrdinary(inventoryClass, inventory, slots, blocking);
        }

        return new LegacyInventoryInspection(
            PlayerUid,
            slots.OrderBy(SortArea).ThenBy(slot => slot.ParentSlotIndex).ThenBy(slot => slot.SlotIndex).ToArray(),
            blocking.Distinct(StringComparer.Ordinal).ToArray());
    }

    public LegacyInventoryState CompareTo(LegacyItemSlotState[] snapshot)
    {
        LegacyInventoryInspection current = Inspect();
        if (current.BlockingReasons.Length != 0) return LegacyInventoryState.Unavailable;
        if (current.Slots.Length == 0) return LegacyInventoryState.Empty;
        return SlotsEqual(current.Slots, snapshot)
            ? LegacyInventoryState.MatchesSnapshot
            : LegacyInventoryState.Changed;
    }

    public LegacyInventoryMutationResult Clear(LegacyItemSlotState[] expected)
    {
        LegacyInventoryInspection current = Inspect();
        if (current.BlockingReasons.Length != 0)
            return LegacyInventoryMutationResult.Rejected(current.BlockingReasons[0]);
        if (!SlotsEqual(current.Slots, expected))
            return LegacyInventoryMutationResult.Rejected("The source inventory changed.");

        try
        {
            foreach (string inventoryClass in ManagedClasses)
            {
                IInventory? inventory = player.InventoryManager.GetOwnInventory(inventoryClass);
                if (inventory == null) throw new InvalidOperationException("A source inventory disappeared.");
                int count = inventoryClass == GlobalConstants.craftingInvClassName
                    ? Math.Max(0, inventory.Count - 1)
                    : inventoryClass == GlobalConstants.backpackInvClassName
                        ? Math.Min(4, inventory.Count)
                        : inventory.Count;
                for (int index = count - 1; index >= 0; index--)
                {
                    ItemSlot? slot = inventory[index];
                    if (slot?.Itemstack == null) continue;
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
            player.InventoryManager.BroadcastHotbarSlot();
            player.BroadcastPlayerData();
            return LegacyInventoryMutationResult.Accepted();
        }
        catch (Exception exception)
        {
            Restore(expected);
            return LegacyInventoryMutationResult.Rejected(exception.Message);
        }
    }

    public LegacyInventoryMutationResult Restore(LegacyItemSlotState[] snapshot)
    {
        try
        {
            LegacyInventoryInspection current = Inspect();
            if (current.BlockingReasons.Length != 0)
                return LegacyInventoryMutationResult.Rejected(current.BlockingReasons[0]);
            if (SlotsEqual(current.Slots, snapshot)) return LegacyInventoryMutationResult.Accepted();
            var currentKeys = current.Slots.ToDictionary(SlotKey, StringComparer.OrdinalIgnoreCase);
            foreach (LegacyItemSlotState expected in OrderedForRestore(snapshot))
            {
                string key = SlotKey(expected);
                if (currentKeys.TryGetValue(key, out LegacyItemSlotState? present))
                {
                    if (!SlotEqual(present, expected))
                        return LegacyInventoryMutationResult.Rejected("A recovery destination contains another item.");
                    continue;
                }

                ItemSlot? destination = ResolveSlot(expected);
                if (destination == null || destination.Itemstack != null)
                    return LegacyInventoryMutationResult.Rejected("A recovery destination is unavailable.");
                CanonicalItemPayload payload = new(
                    PlayerItemSchema.CurrentVersion,
                    expected.Prototype.AssetKind,
                    expected.Prototype.AssetCode,
                    (byte[])expected.Prototype.ExactState.Clone(),
                    expected.Prototype.EmbeddedReferences);
                ItemStackMaterialization materialized = ItemStackCodec.Materialize(
                    payload,
                    expected.Quantity,
                    resolver);
                if (!materialized.Resolved || materialized.Stack == null)
                    return LegacyInventoryMutationResult.Rejected("An item needed for recovery is unavailable.");
                destination.Itemstack = materialized.Stack;
                destination.MarkDirty();
            }
            player.InventoryManager.BroadcastHotbarSlot();
            player.BroadcastPlayerData();
            LegacyInventoryInspection restored = Inspect();
            if (restored.BlockingReasons.Length != 0)
                return LegacyInventoryMutationResult.Rejected(restored.BlockingReasons[0]);
            return SlotsEqual(restored.Slots, snapshot)
                ? LegacyInventoryMutationResult.Accepted()
                : LegacyInventoryMutationResult.Rejected("The restored inventory did not pass verification.");
        }
        catch (Exception exception)
        {
            return LegacyInventoryMutationResult.Rejected(exception.Message);
        }
    }

    private void InspectOrdinary(
        string inventoryClass,
        IInventory inventory,
        List<LegacyItemSlotState> output,
        List<string> blocking)
    {
        int count = inventoryClass == GlobalConstants.craftingInvClassName
            ? Math.Max(0, inventory.Count - 1)
            : inventory.Count;
        for (int index = 0; index < count; index++)
        {
            ItemStack? stack = inventory[index]?.Itemstack;
            if (stack == null) continue;
            TryCapture(
                stack,
                AreaFor(inventoryClass, index),
                inventoryClass,
                index,
                -1,
                EquipmentRoleFor(inventoryClass, index),
                output,
                blocking);
        }
    }

    private void InspectBackpacks(
        IInventory inventory,
        List<LegacyItemSlotState> output,
        List<string> blocking)
    {
        int bagCount = Math.Min(4, inventory.Count);
        for (int bagIndex = 0; bagIndex < bagCount; bagIndex++)
        {
            ItemStack? original = inventory[bagIndex]?.Itemstack;
            if (original == null) continue;
            ItemStack emptyBag = original.Clone();
            IHeldBag? heldBag = emptyBag.Collectible.GetCollectibleInterface<IHeldBag>();
            if (heldBag == null)
            {
                blocking.Add($"Backpack slot {bagIndex + 1} is not a readable bag.");
                continue;
            }
            ItemStack[] contents = heldBag.GetContents(original, player.Entity.World) ?? Array.Empty<ItemStack>();
            heldBag.Clear(emptyBag);
            TryCapture(
                emptyBag,
                LegacyInventoryArea.Backpack,
                GlobalConstants.backpackInvClassName,
                bagIndex,
                -1,
                "",
                output,
                blocking);
            for (int contentIndex = 0; contentIndex < contents.Length; contentIndex++)
            {
                ItemStack? content = contents[contentIndex];
                if (content == null) continue;
                TryCapture(
                    content,
                    LegacyInventoryArea.Backpack,
                    GlobalConstants.backpackInvClassName,
                    contentIndex,
                    bagIndex,
                    "",
                    output,
                    blocking);
            }
        }
    }

    private void TryCapture(
        ItemStack stack,
        LegacyInventoryArea area,
        string inventoryClass,
        int slotIndex,
        int parentSlotIndex,
        string equipmentRole,
        List<LegacyItemSlotState> output,
        List<string> blocking)
    {
        try
        {
            EncodedItemStack encoded = ItemStackCodec.Encode(stack.Clone());
            PlayerItemClassificationContext description = ItemStackCodec.Describe(stack);
            PlayerItemClassification classification = classifier.Classify(description);
            output.Add(new LegacyItemSlotState
            {
                Area = area,
                InventoryClass = inventoryClass,
                SlotIndex = slotIndex,
                ParentSlotIndex = parentSlotIndex,
                Prototype = new PlayerItemPrototype(
                    classification.StorageKind,
                    encoded.Payload.AssetCode,
                    classification.Category,
                    stack.Collectible.Code.Path,
                    encoded.Payload.ExactState,
                    encoded.Payload.AssetKind,
                    encoded.Payload.EmbeddedReferences,
                    description.StatePolicy),
                Quantity = encoded.Quantity,
                EquipmentRole = equipmentRole
            });
        }
        catch
        {
            blocking.Add($"An item in {FriendlyInventoryName(inventoryClass)} slot {slotIndex + 1} cannot be preserved.");
        }
    }

    private ItemSlot? ResolveSlot(LegacyItemSlotState state)
    {
        IInventory? inventory = player.InventoryManager.GetOwnInventory(state.InventoryClass);
        if (inventory == null) return null;
        if (state.ParentSlotIndex < 0) return inventory[state.SlotIndex];
        return inventory.FirstOrDefault(slot => slot is ItemSlotBagContent content
            && content.BagIndex == state.ParentSlotIndex
            && content.SlotIndex == state.SlotIndex);
    }

    private static IEnumerable<LegacyItemSlotState> OrderedForRestore(LegacyItemSlotState[] slots) => slots
        .OrderBy(slot => slot.Area == LegacyInventoryArea.Backpack && slot.ParentSlotIndex >= 0 ? 1 : 0)
        .ThenBy(SortArea)
        .ThenBy(slot => slot.ParentSlotIndex)
        .ThenBy(slot => slot.SlotIndex);

    private static LegacyInventoryArea AreaFor(string inventoryClass, int index)
    {
        if (inventoryClass == GlobalConstants.hotBarInvClassName)
            return index == 11 ? LegacyInventoryArea.Offhand : LegacyInventoryArea.Hotbar;
        if (inventoryClass == GlobalConstants.characterInvClassName) return LegacyInventoryArea.Character;
        if (inventoryClass == GlobalConstants.mousecursorInvClassName) return LegacyInventoryArea.Mouse;
        return LegacyInventoryArea.Crafting;
    }

    private string EquipmentRoleFor(string inventoryClass, int index)
    {
        if (inventoryClass == GlobalConstants.hotBarInvClassName)
        {
            if (index == 11) return PlayerEquipmentRoles.Offhand;
            return index == player.InventoryManager.ActiveHotbarSlotNumber
                ? PlayerEquipmentRoles.MainWeapon
                : "";
        }
        if (inventoryClass != GlobalConstants.characterInvClassName) return "";
        return index switch
        {
            0 => PlayerEquipmentRoles.CosmeticHead,
            1 => PlayerEquipmentRoles.CosmeticShoulder,
            2 => PlayerEquipmentRoles.CosmeticUpperBody,
            3 => PlayerEquipmentRoles.CosmeticLowerBody,
            4 => PlayerEquipmentRoles.CosmeticFeet,
            5 => PlayerEquipmentRoles.CosmeticHands,
            6 => PlayerEquipmentRoles.CosmeticNeck,
            7 => PlayerEquipmentRoles.CosmeticEmblem,
            8 => PlayerEquipmentRoles.CosmeticFace,
            9 => PlayerEquipmentRoles.CosmeticWaist,
            10 => PlayerEquipmentRoles.CosmeticArms,
            11 => PlayerEquipmentRoles.CosmeticUpperBodyOver,
            12 => PlayerEquipmentRoles.Head,
            13 => PlayerEquipmentRoles.Body,
            14 => PlayerEquipmentRoles.Legs,
            _ => ""
        };
    }

    private static int SortArea(LegacyItemSlotState slot) => (int)slot.Area;

    private static string SlotKey(LegacyItemSlotState slot) =>
        $"{slot.InventoryClass}:{slot.ParentSlotIndex}:{slot.SlotIndex}";

    private static bool SlotsEqual(LegacyItemSlotState[] left, LegacyItemSlotState[] right)
    {
        if (left.Length != right.Length) return false;
        Dictionary<string, LegacyItemSlotState> byKey;
        try
        {
            byKey = left.ToDictionary(SlotKey, StringComparer.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return right.All(expected =>
            byKey.TryGetValue(SlotKey(expected), out LegacyItemSlotState? actual)
            && SlotEqual(actual, expected));
    }

    private static bool SlotEqual(LegacyItemSlotState left, LegacyItemSlotState right) =>
        left.Quantity == right.Quantity
        && left.Prototype.Kind == right.Prototype.Kind
        && left.Prototype.AssetKind == right.Prototype.AssetKind
        && string.Equals(left.Prototype.AssetCode, right.Prototype.AssetCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Prototype.Category, right.Prototype.Category, StringComparison.OrdinalIgnoreCase)
        && left.Prototype.ExactState.AsSpan().SequenceEqual(right.Prototype.ExactState)
        && ReferencesEqual(left.Prototype.EmbeddedReferences, right.Prototype.EmbeddedReferences);

    private static bool ReferencesEqual(
        CanonicalCollectibleReference[]? left,
        CanonicalCollectibleReference[]? right)
    {
        left ??= Array.Empty<CanonicalCollectibleReference>();
        right ??= Array.Empty<CanonicalCollectibleReference>();
        return left.Length == right.Length
            && left.OrderBy(ReferenceKey, StringComparer.OrdinalIgnoreCase)
                .Zip(right.OrderBy(ReferenceKey, StringComparer.OrdinalIgnoreCase))
                .All(pair => string.Equals(ReferenceKey(pair.First), ReferenceKey(pair.Second), StringComparison.OrdinalIgnoreCase));
    }

    private static string ReferenceKey(CanonicalCollectibleReference reference) =>
        $"{reference.AssetKind}:{reference.SourceRegistryId}:{reference.AssetCode}";

    private static string FriendlyInventoryName(string inventoryClass) => inventoryClass switch
    {
        GlobalConstants.hotBarInvClassName => "hotbar",
        GlobalConstants.backpackInvClassName => "backpack",
        GlobalConstants.characterInvClassName => "equipment",
        GlobalConstants.mousecursorInvClassName => "held item",
        GlobalConstants.craftingInvClassName => "crafting grid",
        _ => "player"
    };
}
