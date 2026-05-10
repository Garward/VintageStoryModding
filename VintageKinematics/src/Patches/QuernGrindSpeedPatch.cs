using System.Collections;
using System.Reflection;
using HarmonyLib;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Network;

namespace VintageKinematics.Patches
{
    /// <summary>
    /// Postfix on vanilla BEQuern.GrindSpeed.get for our BEKineticQuern. Vanilla returns
    /// 1f whenever quantityPlayersGrinding &gt; 0 — but our BE deliberately sets that to 1
    /// from its kinetic tick to drive vanilla's render/sync paths even with no real player
    /// at the grindstone. So the postfix overrides the result with (CurrentRPM / 32) whenever
    /// the qpg counter is synthetic (real playersGrinding collection is empty). Real players
    /// still win — when they're grinding, vanilla's 1f is preserved.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityQuern), "GrindSpeed", MethodType.Getter)]
    public static class QuernGrindSpeedPatch
    {
        // Network RPM at which the kinetic quern grinds at vanilla speed (1.0).
        private const float QuernRefRPM = 32f;

        private static readonly FieldInfo playersGrindingField =
            typeof(BlockEntityQuern).GetField(
                "playersGrinding",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Postfix(BlockEntityQuern __instance, ref float __result)
        {
            if (__instance is not BEKineticQuern kq) return;

            int realPlayers = (playersGrindingField?.GetValue(kq) as ICollection)?.Count ?? 0;
            if (realPlayers > 0) return;

            BEBehaviorKinetic beh = kq.GetBehavior<BEBehaviorKinetic>();
            if (beh == null || beh.NetworkConflicted)
            {
                __result = 0f;
                return;
            }

            float rpm = System.MathF.Abs(beh.CurrentRPM);
            if (rpm < KineticNetwork.MinAbsRPM)
            {
                __result = 0f;
                return;
            }

            float speedMult = 1f;
            var cfg = kq.Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            if (cfg != null) speedMult = cfg.ResolveConsumerSpeed(kq.Block?.Code?.FirstCodePart());

            __result = (rpm / QuernRefRPM) * speedMult;
        }
    }
}
