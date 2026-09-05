using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

/// <summary>Neutralizes Vanilla class gameplay while retaining its compatibility sentinel.</summary>
public static class VanillaClassRuntimePatch
{
    public const string InertClassCode = "commoner";
    private const string HarmonyOwner = "vrpg.character-runtime";

    public static System.Func<Entity, bool>? OwnershipResolver { get; set; }
    public static System.Func<bool>? ClientAppearanceOnlyResolver { get; set; }

    public static void Patch(Harmony harmony, ILogger logger)
    {
        Type? characterSystem = AccessTools.TypeByName("Vintagestory.GameContent.CharacterSystem");
        PatchMethod(harmony, logger, characterSystem, "setCharacterClass", nameof(SetCharacterClassPrefix));
        PatchMethod(harmony, logger, characterSystem, "applyTraitAttributes", nameof(ApplyTraitAttributesPrefix));
        PatchMethod(harmony, logger, characterSystem, "HasTrait", nameof(HasTraitPrefix));
        PatchMethod(harmony, logger, characterSystem, "Event_MatchesGridRecipe", nameof(MatchesGridRecipePrefix));

        Type? creatorDialog = AccessTools.TypeByName("Vintagestory.GameContent.GuiDialogCreateCharacter");
        PatchMethod(
            harmony,
            logger,
            creatorDialog,
            "get_AllowClassSelection",
            nameof(AllowClassSelectionPrefix));
    }

    public static void SetCharacterClassPrefix(
        EntityPlayer eplayer,
        ref string classCode,
        ref bool initializeGear)
    {
        if (!IsOwned(eplayer))
        {
            return;
        }

        classCode = InertClassCode;
        initializeGear = false;
        NormalizePlayer(eplayer);
    }

    public static bool ApplyTraitAttributesPrefix(EntityPlayer eplr)
    {
        if (!IsOwned(eplr))
        {
            return true;
        }

        NormalizePlayer(eplr);
        return false;
    }

    public static bool HasTraitPrefix(IPlayer player, ref bool __result)
    {
        if (!IsOwned(player?.Entity))
        {
            return true;
        }

        __result = false;
        return false;
    }

    public static bool MatchesGridRecipePrefix(IPlayer player, ref bool __result)
    {
        if (!IsOwned(player?.Entity))
        {
            return true;
        }

        __result = true;
        return false;
    }

    public static bool AllowClassSelectionPrefix(ref bool __result)
    {
        if (ClientAppearanceOnlyResolver?.Invoke() != true)
        {
            return true;
        }

        __result = false;
        return false;
    }

    public static void NormalizePlayer(Entity entity)
    {
        if (!IsOwned(entity))
        {
            return;
        }

        var affectedCategories = new List<string>();
        foreach (KeyValuePair<string, EntityFloatStats> category in entity.Stats)
        {
            if (category.Value.ValuesByKey.ContainsKey("trait"))
            {
                affectedCategories.Add(category.Key);
            }
        }

        foreach (string category in affectedCategories)
        {
            entity.Stats.Remove(category, "trait");
        }

        entity.WatchedAttributes.SetString("characterClass", InertClassCode);
        entity.WatchedAttributes.RemoveAttribute("extraTraits");
        entity.WatchedAttributes.SetBool("allowcharselonce", false);
        entity.WatchedAttributes.MarkPathDirty("characterClass");
        entity.WatchedAttributes.MarkPathDirty("extraTraits");
        entity.WatchedAttributes.MarkPathDirty("allowcharselonce");

        EntityBehavior? health = entity.GetBehavior("health");
        AccessTools.Method(health?.GetType(), "MarkDirty")?.Invoke(health, null);
    }

    private static bool IsOwned(Entity? entity)
    {
        return entity != null && OwnershipResolver?.Invoke(entity) == true;
    }

    private static void PatchMethod(
        Harmony harmony,
        ILogger logger,
        Type? targetType,
        string methodName,
        string patchName)
    {
        if (targetType == null)
        {
            logger.Error("[VRPG/Character] CharacterSystem was not found while patching {0}.", methodName);
            return;
        }

        MethodInfo? original = AccessTools.DeclaredMethod(targetType, methodName);
        MethodInfo? patch = AccessTools.Method(typeof(VanillaClassRuntimePatch), patchName);
        if (original == null || patch == null)
        {
            logger.Error("[VRPG/Character] Could not install class authority patch for {0}.{1}.", targetType.FullName, methodName);
            return;
        }

        HarmonyLib.Patches? existing = Harmony.GetPatchInfo(original);
        if (existing?.Owners.Contains(HarmonyOwner) == true)
        {
            return;
        }

        harmony.Patch(original, prefix: new HarmonyMethod(patch));
    }
}
