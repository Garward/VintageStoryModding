using System;
using System.Reflection;
using HarmonyLib;
using IME;
using Vintagestory.API.Common;

namespace InterestingMeFixClient
{
    public class InterestingMeFixClientModSystem : ModSystem
    {
        private const string HarmonyId = "interestingmefixclient";
        private Harmony harmony;

        public override double ExecuteOrder() => 1.1;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                api.Logger.Notification("[InterestingMeFixClient] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[InterestingMeFixClient] Failed to apply Harmony patches: {0}", ex);
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            base.Dispose();
        }
    }

    // Vintage Story client only forwards an interact packet to the server when its local
    // OnBlockInteractStart returns true. BlockMuckPile returns false when the block entity
    // is missing, so bugged piles silently swallow right-clicks. This postfix flips the
    // result to true on the client only, which makes the engine send the interact to the
    // server; the server's InterestingMeFix on-interact heal then attaches the BE and the
    // original IME interact runs.
    [HarmonyPatch(typeof(BlockMuckPile), nameof(BlockMuckPile.OnBlockInteractStart))]
    internal static class ForwardRightClickOnBuggedPiles
    {
        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
        {
            if (world == null) return;
            string side = world.Side == EnumAppSide.Client ? "client" : "server";

            world.Logger.Notification(
                "[InterestingMeFixClient] OnBlockInteractStart hit side={0} origResult={1} pos={2} sneak={3} beNull={4}",
                side,
                __result,
                blockSel?.Position,
                byPlayer?.Entity?.Controls?.Sneak,
                blockSel?.Position == null ? "?" : (world.BlockAccessor.GetBlockEntity(blockSel.Position) == null).ToString());

            if (__result) return;
            if (world.Side != EnumAppSide.Client) return;
            if (byPlayer == null || blockSel?.Position == null) return;
            if (byPlayer.Entity?.Controls?.Sneak == true) return;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityMuckPile) return;

            __result = true;
            world.Logger.Notification("[InterestingMeFixClient] Flipped result=true at {0}", blockSel.Position);
        }
    }
}
