using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace FastCraftingGrid.Internal;

internal static class CraftingBurstDiagnostics
{
    private const int IdleFlushMs = 200;
    private static readonly ConditionalWeakTable<InventoryCraftingGrid, BurstState> States = new ConditionalWeakTable<InventoryCraftingGrid, BurstState>();

    public static void RecordActivate(InventoryCraftingGrid inventory, int slotId, ItemStackMoveOperation op)
    {
        if (!Enabled(inventory)) return;

        BurstState state = Touch(inventory);
        lock (state)
        {
            state.ActivateCalls++;
            if (slotId == inventory.Count - 1)
            {
                state.OutputActivates++;
                if (op.MouseButton == EnumMouseButton.Left) state.OutputLeftActivates++;
                if (op.MouseButton == EnumMouseButton.Right) state.OutputRightActivates++;
            }
            else
            {
                state.InputActivates++;
            }
        }
    }

    public static void RecordMove(InventoryCraftingGrid inventory, string[] invIds, int[] slotIds, ItemStackMoveOperation op)
    {
        if (!Enabled(inventory)) return;

        BurstState state = Touch(inventory);
        lock (state)
        {
            state.MoveCalls++;

            string inventoryId = inventory.InventoryID;
            if (invIds != null && slotIds != null && invIds.Length > 1 && slotIds.Length > 1)
            {
                if (invIds[0] == inventoryId && slotIds[0] == inventory.Count - 1) state.OutputMoves++;
                if (invIds[1] == inventoryId && slotIds[1] == inventory.Count - 1) state.OutputMoves++;
                if (invIds[0] == inventoryId && slotIds[0] >= 0 && slotIds[0] < inventory.Count - 1) state.InputMoves++;
                if (invIds[1] == inventoryId && slotIds[1] >= 0 && slotIds[1] < inventory.Count - 1) state.InputMoves++;
            }

            state.RequestedMoveQuantity += Math.Max(0, op.RequestedQuantity);
            state.MovedQuantity += Math.Max(0, op.MovedQuantity);
        }
    }

    public static void RecordFind(
        InventoryCraftingGrid inventory,
        long ticks,
        bool cacheHit,
        bool runVanilla,
        int candidateCount,
        int plausibleCandidateCount,
        long gatherTicks,
        long shapedTicks,
        long shapelessTicks,
        long outputTicks)
    {
        if (!Enabled(inventory)) return;

        BurstState state = Touch(inventory);
        lock (state)
        {
            state.FindCalls++;
            if (cacheHit) state.CacheHits++;
            if (runVanilla) state.VanillaFallbacks++;
            state.CandidateChecks += Math.Max(0, candidateCount);
            state.PlausibleChecks += Math.Max(0, plausibleCandidateCount);
            state.FindTicks += ticks;
            state.GatherTicks += gatherTicks;
            state.ShapedTicks += shapedTicks;
            state.ShapelessTicks += shapelessTicks;
            state.OutputTicks += outputTicks;
            if (ticks > state.MaxFindTicks) state.MaxFindTicks = ticks;
        }
    }

    private static bool Enabled(InventoryCraftingGrid inventory)
    {
        return FastCraftingGridConfigSystem.Config.EnableDiagnostics && inventory?.Api?.World != null;
    }

    private static BurstState Touch(InventoryCraftingGrid inventory)
    {
        BurstState state = States.GetValue(inventory, _ => new BurstState());
        long nowTicks = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;

        lock (state)
        {
            if (state.StartedMs == 0 || nowMs - state.LastEventMs > IdleFlushMs)
            {
                FlushLocked(inventory, state, nowTicks, nowMs, "next-burst");
                state.Reset(nowTicks, nowMs);
            }

            state.LastEventTicks = nowTicks;
            state.LastEventMs = nowMs;
            state.Generation++;

            if (!state.FlushScheduled)
            {
                state.FlushScheduled = true;
                long generation = state.Generation;
                inventory.Api.Event.RegisterCallback(_ => FlushIfIdle(inventory, state, generation), IdleFlushMs, true);
            }
        }

        return state;
    }

    private static void FlushIfIdle(InventoryCraftingGrid inventory, BurstState state, long scheduledGeneration)
    {
        if (!Enabled(inventory)) return;

        lock (state)
        {
            state.FlushScheduled = false;

            long nowMs = Environment.TickCount64;
            if (state.StartedMs == 0)
            {
                return;
            }

            if (scheduledGeneration != state.Generation || nowMs - state.LastEventMs < IdleFlushMs)
            {
                if (!state.FlushScheduled)
                {
                    state.FlushScheduled = true;
                    long generation = state.Generation;
                    inventory.Api.Event.RegisterCallback(_ => FlushIfIdle(inventory, state, generation), IdleFlushMs, true);
                }

                return;
            }

            FlushLocked(inventory, state, Stopwatch.GetTimestamp(), nowMs, "idle");
            state.Reset(0, 0);
        }
    }

    private static void FlushLocked(InventoryCraftingGrid inventory, BurstState state, long nowTicks, long nowMs, string reason)
    {
        if (state.StartedMs == 0 || !state.HasWork)
        {
            return;
        }

        double ticksToMs = 1000.0 / Stopwatch.Frequency;
        double wallMs = (nowTicks - state.StartedTicks) * ticksToMs;
        double activeMs = (state.LastEventTicks - state.StartedTicks) * ticksToMs;
        double findMs = state.FindTicks * ticksToMs;
        double maxFindMs = state.MaxFindTicks * ticksToMs;
        double gatherMs = state.GatherTicks * ticksToMs;
        double shapedMs = state.ShapedTicks * ticksToMs;
        double shapelessMs = state.ShapelessTicks * ticksToMs;
        double outputMs = state.OutputTicks * ticksToMs;

        inventory.Api.Logger.Notification(
            "[fastcraftinggrid] burst {0} {1} side={2} wall={3:F1}ms active={4:F1}ms " +
            "activate={5} inputActivate={6} outputActivate={7} leftOut={8} rightOut={9} " +
            "moves={10} inputMoves={11} outputMoves={12} qty={13}/{14} " +
            "finds={15} cache={16} fallback={17} findTotal={18:F2}ms maxFind={19:F2}ms " +
            "candidates={20} plausible={21} gather={22:F2}ms shaped={23:F2}ms shapeless={24:F2}ms output={25:F2}ms",
            inventory.InventoryID ?? "?",
            reason,
            inventory.Api.Side,
            Math.Max(0, wallMs),
            Math.Max(0, activeMs),
            state.ActivateCalls,
            state.InputActivates,
            state.OutputActivates,
            state.OutputLeftActivates,
            state.OutputRightActivates,
            state.MoveCalls,
            state.InputMoves,
            state.OutputMoves,
            state.MovedQuantity,
            state.RequestedMoveQuantity,
            state.FindCalls,
            state.CacheHits,
            state.VanillaFallbacks,
            findMs,
            maxFindMs,
            state.CandidateChecks,
            state.PlausibleChecks,
            gatherMs,
            shapedMs,
            shapelessMs,
            outputMs);
    }

    private sealed class BurstState
    {
        public long StartedTicks;
        public long LastEventTicks;
        public long StartedMs;
        public long LastEventMs;
        public long Generation;
        public bool FlushScheduled;

        public int ActivateCalls;
        public int InputActivates;
        public int OutputActivates;
        public int OutputLeftActivates;
        public int OutputRightActivates;
        public int MoveCalls;
        public int InputMoves;
        public int OutputMoves;
        public int FindCalls;
        public int CacheHits;
        public int VanillaFallbacks;
        public long RequestedMoveQuantity;
        public long MovedQuantity;
        public long CandidateChecks;
        public long PlausibleChecks;
        public long FindTicks;
        public long MaxFindTicks;
        public long GatherTicks;
        public long ShapedTicks;
        public long ShapelessTicks;
        public long OutputTicks;

        public bool HasWork => ActivateCalls + MoveCalls + FindCalls > 0;

        public void Reset(long ticks, long ms)
        {
            StartedTicks = ticks;
            LastEventTicks = ticks;
            StartedMs = ms;
            LastEventMs = ms;
            ActivateCalls = 0;
            InputActivates = 0;
            OutputActivates = 0;
            OutputLeftActivates = 0;
            OutputRightActivates = 0;
            MoveCalls = 0;
            InputMoves = 0;
            OutputMoves = 0;
            FindCalls = 0;
            CacheHits = 0;
            VanillaFallbacks = 0;
            RequestedMoveQuantity = 0;
            MovedQuantity = 0;
            CandidateChecks = 0;
            PlausibleChecks = 0;
            FindTicks = 0;
            MaxFindTicks = 0;
            GatherTicks = 0;
            ShapedTicks = 0;
            ShapelessTicks = 0;
            OutputTicks = 0;
        }
    }
}

[HarmonyPatch(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.ActivateSlot))]
internal static class InventoryCraftingGridActivateSlotBurstPatch
{
    private static void Prefix(InventoryCraftingGrid __instance, int slotId, ref ItemStackMoveOperation op)
    {
        CraftingBurstDiagnostics.RecordActivate(__instance, slotId, op);
    }
}

[HarmonyPatch(typeof(InventoryCraftingGrid), nameof(InventoryCraftingGrid.TryMoveItemStack))]
internal static class InventoryCraftingGridTryMoveItemStackBurstPatch
{
    private static void Postfix(InventoryCraftingGrid __instance, string[] invIds, int[] slotIds, ref ItemStackMoveOperation op)
    {
        CraftingBurstDiagnostics.RecordMove(__instance, invIds, slotIds, op);
    }
}
