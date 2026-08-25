using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Gui.Storage;
using VintageKinematics.Storage.Terminal;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public const int PacketIdOpenTerminal = 6700;
        public const int PacketIdQueryTerminal = 6701;
        public const int PacketIdTerminalPage = 6702;
        public const int PacketIdTerminalAction = 6703;

        private const long SessionLifetimeMs = 30_000;
        private const long QueryIntervalMs = 100;
        private const double UseRange = 8.0;

        private readonly StorageTerminalPageBuilder terminalPages = new();
        private readonly Dictionary<string, TerminalSession> terminalSessions = new();
        private IReadOnlyCollection<StoredEntry> terminalEntrySnapshot = Array.Empty<StoredEntry>();
        private long terminalEntrySnapshotRevision = -1;
        private GuiDialogStorageTerminal clientTerminalDialog;
        private long clientTerminalSessionId;

        public bool OnPlayerRightClick(IPlayer player)
        {
            if (Api?.Side == EnumAppSide.Server) OpenTerminal(player);
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == PacketIdTerminalAction)
            {
                HandleTerminalAction(player, data);
                return;
            }
            if (packetid != PacketIdQueryTerminal)
            {
                base.OnReceivedClientPacket(player, packetid, data);
                return;
            }
            if (!StorageTerminalPacketCodec.TryDecodeQuery(
                data,
                out long sessionId,
                out StorageTerminalQuery query)) return;
            if (!TryUseSession(player, sessionId)) return;

            TerminalSession session = terminalSessions[player.PlayerUID];
            long now = Api.World.ElapsedMilliseconds;
            if (now - session.LastQueryMs < QueryIntervalMs) return;
            session.LastQueryMs = now;
            session.ExpiresAtMs = now + SessionLifetimeMs;
            SendTerminalPage(player as IServerPlayer, sessionId, query, PacketIdTerminalPage);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid != PacketIdOpenTerminal && packetid != PacketIdTerminalPage)
            {
                base.OnReceivedServerPacket(packetid, data);
                return;
            }
            if (Api is not ICoreClientAPI capi) return;
            if (!StorageTerminalPacketCodec.TryDecodePage(
                data,
                capi.World,
                out string title,
                out long sessionId,
                out StorageTerminalPage page)) return;

            if (packetid == PacketIdTerminalPage)
            {
                if (clientTerminalDialog == null || sessionId != clientTerminalSessionId) return;
                clientTerminalDialog.UpdatePage(page);
                return;
            }

            GuiDialogUtil.SafeDispose(ref clientTerminalDialog);
            clientTerminalSessionId = sessionId;
            clientTerminalDialog = new GuiDialogStorageTerminal(
                title,
                Pos,
                page,
                SendTerminalQuery,
                SendTerminalAction,
                capi);
            clientTerminalDialog.OnClosed += OnTerminalClosed;
            clientTerminalDialog.TryOpen();
        }

        private void OpenTerminal(IPlayer player)
        {
            if (player is not IServerPlayer serverPlayer || !CanUseTerminal(player)) return;

            long sessionId = Random.Shared.NextInt64(1, long.MaxValue);
            terminalSessions[player.PlayerUID] = new TerminalSession
            {
                SessionId = sessionId,
                ExpiresAtMs = Api.World.ElapsedMilliseconds + SessionLifetimeMs,
                LastQueryMs = long.MinValue / 2
            };
            SendTerminalPage(
                serverPlayer,
                sessionId,
                new StorageTerminalQuery(0, string.Empty, 0, StorageTerminalSort.Name),
                PacketIdOpenTerminal);
        }

        private void SendTerminalPage(
            IServerPlayer player,
            long sessionId,
            StorageTerminalQuery query,
            int packetId)
        {
            if (player == null) return;
            StorageStats stats = TerminalStats();
            long revision = activeRecoveryRecord?.Revision ?? 0;
            IReadOnlyCollection<StoredEntry> entries = TerminalEntries(revision);
            StorageTerminalPage page = terminalPages.Build(entries, query, stats, revision);
            string title = Lang.Get("vintagekinematics:storage-terminal-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:storage-terminal-title")
            {
                title = "Kinetic Warehouse";
            }
            byte[] payload = StorageTerminalPacketCodec.EncodePage(title, sessionId, page);
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(player, Pos, packetId, payload);
        }

        private StorageStats TerminalStats()
        {
            return new StorageStats(
                itemIndex?.StoredItems ?? 0,
                VerifiedItemCapacity,
                itemIndex?.EntryCount ?? 0,
                EffectiveTypeCapacity,
                StructureState,
                0,
                0,
                PowerRequirementEnabled,
                IsOperationallyPowered);
        }

        private IReadOnlyCollection<StoredEntry> TerminalEntries(long revision)
        {
            if (terminalEntrySnapshotRevision == revision) return terminalEntrySnapshot;
            terminalEntrySnapshot = itemIndex?.GetEntries() ?? Array.Empty<StoredEntry>();
            terminalEntrySnapshotRevision = revision;
            return terminalEntrySnapshot;
        }

        private bool TryUseSession(IPlayer player, long sessionId)
        {
            if (player == null
                || !terminalSessions.TryGetValue(player.PlayerUID, out TerminalSession session)
                || session.SessionId != sessionId
                || session.ExpiresAtMs < Api.World.ElapsedMilliseconds)
            {
                if (player != null) terminalSessions.Remove(player.PlayerUID);
                return false;
            }
            return CanUseTerminal(player);
        }

        private void HandleTerminalAction(IPlayer player, byte[] data)
        {
            if (!StorageTerminalPacketCodec.TryDecodeAction(
                data,
                out long sessionId,
                out StorageTerminalActionRequest request)) return;
            if (!TryUseSession(player, sessionId)
                || player is not IServerPlayer serverPlayer) return;

            TerminalSession session = terminalSessions[player.PlayerUID];
            long now = Api.World.ElapsedMilliseconds;
            if (now - session.LastActionMs < QueryIntervalMs)
            {
                // A prediction must always receive an authoritative answer. Returning the
                // current page rolls back an action rejected by the server rate limit.
                ResyncPredictedInventory(serverPlayer, request);
                SendTerminalPage(
                    serverPlayer,
                    sessionId,
                    request.RefreshQuery,
                    PacketIdTerminalPage);
                return;
            }
            session.LastActionMs = now;
            session.ExpiresAtMs = now + SessionLifetimeMs;

            StorageTransferResult result = request.Action switch
            {
                StorageTerminalAction.DepositHeldStack => DepositHeldStack(serverPlayer),
                StorageTerminalAction.DepositInventorySlot => DepositInventorySlot(
                    serverPlayer,
                    request.SourceInventoryId,
                    request.SourceSlotId),
                StorageTerminalAction.WithdrawOneToCursor => WithdrawOneToCursor(
                    serverPlayer,
                    request.EntryId),
                StorageTerminalAction.WithdrawStackToInventory => WithdrawStackToInventory(
                    serverPlayer,
                    request.EntryId),
                _ => StorageTransferResult.Fail(StorageTransferStatus.InvalidQuantity)
            };
            ResyncPredictedInventory(serverPlayer, request);
            if (!result.Success) SendTransferError(serverPlayer, result);
            SendTerminalPage(
                serverPlayer,
                sessionId,
                request.RefreshQuery,
                PacketIdTerminalPage);
        }

        private void ResyncPredictedInventory(
            IServerPlayer player,
            StorageTerminalActionRequest request)
        {
            switch (request.Action)
            {
                case StorageTerminalAction.DepositHeldStack:
                    ResolveDepositSlot(player)?.MarkDirty();
                    break;
                case StorageTerminalAction.DepositInventorySlot:
                    player.InventoryManager
                        .GetInventory(request.SourceInventoryId)?[request.SourceSlotId]
                        ?.MarkDirty();
                    break;
                case StorageTerminalAction.WithdrawOneToCursor:
                    player.InventoryManager.MouseItemSlot?.MarkDirty();
                    break;
                case StorageTerminalAction.WithdrawStackToInventory:
                    foreach (InventoryBase inventory in player.InventoryManager.InventoriesOrdered)
                    {
                        if (inventory is not InventoryBasePlayer
                            || !inventory.HasOpened(player)) continue;
                        for (int slotId = 0; slotId < inventory.Count; slotId++)
                        {
                            inventory.MarkSlotDirty(slotId);
                        }
                    }
                    break;
            }
        }

        private static void SendTransferError(
            IServerPlayer player,
            StorageTransferResult result)
        {
            string langCode = !string.IsNullOrWhiteSpace(result.MessageLangCode)
                ? result.MessageLangCode
                : "vintagekinematics:storage-transfer-" + result.Status.ToString().ToLowerInvariant();
            string message = Lang.Get(langCode);
            if (string.IsNullOrWhiteSpace(message) || message == langCode)
            {
                message = Lang.Get("vintagekinematics:storage-transfer-failed");
            }
            player.SendIngameError("vk-storage-transfer", message);
        }

        private bool CanUseTerminal(IPlayer player)
        {
            if (player?.Entity == null || Api?.Side != EnumAppSide.Server) return false;
            if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return false;
            return player.Entity.Pos.DistanceTo(Pos.ToVec3d().Add(0.5, 0.5, 0.5)) <= UseRange;
        }

        private void SendTerminalQuery(StorageTerminalQuery query)
        {
            if (Api is not ICoreClientAPI capi || clientTerminalSessionId <= 0) return;
            byte[] payload = StorageTerminalPacketCodec.EncodeQuery(clientTerminalSessionId, query);
            capi.Network.SendBlockEntityPacket(Pos, PacketIdQueryTerminal, payload);
        }

        private void SendTerminalAction(StorageTerminalActionRequest request)
        {
            if (Api is not ICoreClientAPI capi || clientTerminalSessionId <= 0) return;
            byte[] payload = StorageTerminalPacketCodec.EncodeAction(
                clientTerminalSessionId,
                request);
            capi.Network.SendBlockEntityPacket(Pos, PacketIdTerminalAction, payload);
        }

        private void OnTerminalClosed()
        {
            clientTerminalDialog = null;
            clientTerminalSessionId = 0;
        }

        private void DisposeTerminalDialog()
        {
            GuiDialogUtil.SafeDispose(ref clientTerminalDialog);
            clientTerminalSessionId = 0;
            terminalSessions.Clear();
            terminalEntrySnapshot = Array.Empty<StoredEntry>();
            terminalEntrySnapshotRevision = -1;
        }

        private sealed class TerminalSession
        {
            public long SessionId;
            public long ExpiresAtMs;
            public long LastQueryMs;
            public long LastActionMs = long.MinValue / 2;
        }
    }
}
