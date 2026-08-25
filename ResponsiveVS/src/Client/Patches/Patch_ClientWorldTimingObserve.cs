using HarmonyLib;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace ResponsiveVS.Client.Patches;

public static class Patch_ClientWorldTimingObserve
{
    private static readonly System.Reflection.FieldInfo GameField =
        AccessTools.Field(typeof(ClientSystem), "game");

    public static void OnGameTickPrefix(object __instance, float dt, out TimingState __state)
    {
        ClientMain game = GameField?.GetValue(__instance) as ClientMain;
        __state = Capture(game, dt);
    }

    public static void OnGameTickPostfix(TimingState __state)
    {
        Flush("client-ongametick", __state, Capture(__state?.Game, __state?.Dt ?? 0));
    }

    public static void HandleHandInteractionPrefix(object __instance, float dt, out TimingState __state)
    {
        ClientMain game = GameField?.GetValue(__instance) as ClientMain;
        __state = Capture(game, dt);
    }

    public static void HandleHandInteractionPostfix(TimingState __state)
    {
        Flush("client-handlehand", __state, Capture(__state?.Game, __state?.Dt ?? 0));
    }

    private static TimingState Capture(ClientMain game, float dt)
    {
        return new TimingState
        {
            Game = game,
            Dt = dt,
            HandUse = game?.EntityPlayer?.Controls?.HandUse ?? EnumHandInteract.None,
            UsingCount = game?.EntityPlayer?.Controls?.UsingCount ?? -1,
            RightMouse = game?.InWorldMouseState?.Right ?? false,
            LeftMouse = game?.InWorldMouseState?.Left ?? false,
            MouseGrabbed = game?.MouseGrabbed ?? false,
            MouseWorldAnyway = game?.mouseWorldInteractAnyway ?? false,
            ActiveSlot = InventoryDiagFormat.Slot(game?.Player?.InventoryManager?.ActiveHotbarSlot)
        };
    }

    private static void Flush(string phase, TimingState before, TimingState after)
    {
        if (before == null || after == null || !ResponsiveDiagnostics.BasicEnabled)
        {
            return;
        }

        bool active = before.HandUse != EnumHandInteract.None || after.HandUse != EnumHandInteract.None;
        int delta = after.UsingCount - before.UsingCount;
        bool suspicious = before.Dt > 0.04f || delta > 1;

        if (!active || (!suspicious && !ResponsiveDiagnostics.TraceEnabled))
        {
            return;
        }

        string message =
            "WORLD TIMING CLIENT {0} dt={1:0.0000} hand={2}->{3} using={4}->{5} delta={6} mouse=R{7}/L{8} grabbed={9} anyway={10} slot={11}";

        if (suspicious)
        {
            ResponsiveDiagnostics.Basic(
                message,
                phase,
                before.Dt,
                before.HandUse,
                after.HandUse,
                before.UsingCount,
                after.UsingCount,
                delta,
                before.RightMouse,
                before.LeftMouse,
                before.MouseGrabbed,
                before.MouseWorldAnyway,
                before.ActiveSlot);
        }
        else
        {
            ResponsiveDiagnostics.Trace(
                message,
                phase,
                before.Dt,
                before.HandUse,
                after.HandUse,
                before.UsingCount,
                after.UsingCount,
                delta,
                before.RightMouse,
                before.LeftMouse,
                before.MouseGrabbed,
                before.MouseWorldAnyway,
                before.ActiveSlot);
        }
    }

    public sealed class TimingState
    {
        public ClientMain Game;
        public float Dt;
        public EnumHandInteract HandUse;
        public int UsingCount;
        public bool RightMouse;
        public bool LeftMouse;
        public bool MouseGrabbed;
        public bool MouseWorldAnyway;
        public string ActiveSlot;
    }
}
