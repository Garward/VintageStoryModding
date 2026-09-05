using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using HarmonyLib;
using VRPG.Modules.Rpg.Combat;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VRPG.Patches;

public static class NativeDamageOverTimePatch
{
    public static void Patch(Harmony harmony, MethodInfo queued, MethodInfo ticks)
    {
        harmony.Patch(queued,
            postfix: new HarmonyMethod(typeof(NativeDamageOverTimePatch), nameof(QueuedPostfix)));
        harmony.Patch(ticks,
            transpiler: new HarmonyMethod(typeof(NativeDamageOverTimePatch), nameof(TickSourceTranspiler)));
    }

    public static void QueuedPostfix(EntityBehaviorHealth __instance, DamageSource damageSource, bool __result)
    {
        if (__result && __instance.ActiveDoTEffects.Count > 0)
            PlayerDamageOverTimeContext.AttachQueuedEffect(__instance.entity, damageSource, __instance.ActiveDoTEffects[^1]);
    }

    // Replace only the tick source constructor; the native loop still owns timing, cancellation,
    // persistence, per-tick budgets and dispatch. The adjacent field load identifies the effect local.
    public static IEnumerable<CodeInstruction> TickSourceTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var constructor = AccessTools.Constructor(typeof(DamageSource));
        var sourceField = AccessTools.Field(typeof(DamageOverTimeEffect), nameof(DamageOverTimeEffect.DamageSource));
        int index = code.FindIndex(x => x.opcode == OpCodes.Newobj && Equals(x.operand, constructor));
        if (index < 0 || index + 3 >= code.Count || code[index + 1].opcode != OpCodes.Dup
            || !code[index + 2].IsLdloc() || !Equals(code[index + 3].operand, sourceField))
            throw new InvalidOperationException("Unsupported native DoT source construction; cannot preserve combat authority.");
        CodeInstruction load = new(code[index + 2].opcode, code[index + 2].operand);
        load.labels.AddRange(code[index].labels);
        load.blocks.AddRange(code[index].blocks);
        code[index] = load;
        code.Insert(index + 1, new CodeInstruction(OpCodes.Call,
            AccessTools.Method(typeof(PlayerDamageOverTimeContext), nameof(PlayerDamageOverTimeContext.CreateTickSource))));
        return code;
    }

}
