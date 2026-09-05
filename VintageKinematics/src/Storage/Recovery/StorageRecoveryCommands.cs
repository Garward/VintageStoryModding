using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Storage.Persistence;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryCommands
    {
        private const long ConfirmationLifetimeMilliseconds = 120_000;
        private static readonly Dictionary<string, PendingConfirmation> PendingConfirmations =
            new(StringComparer.Ordinal);

        public static TextCommandResult HandleRecover(
            ICoreServerAPI api,
            TextCommandCallingArgs args)
        {
            int radius = GameMath.Clamp((int)args[0], 1, 128);
            string sourceWord = args[1] as string;
            string suppliedToken = args[2] as string;
            BEKineticWarehouseTerminal controller = FindNearestController(api, args, radius);
            if (controller == null)
            {
                return TextCommandResult.Error("No loaded kinetic warehouse terminal was found in range.");
            }
            StorageReconciliationResult result = controller.LastReconciliation;
            if (result == null)
            {
                return TextCommandResult.Error("The controller has not completed recovery inspection.");
            }
            if (string.IsNullOrEmpty(sourceWord))
            {
                RememberInspection(api, args, controller, result);
                return TextCommandResult.Success(Describe(controller, result, radius));
            }
            if (!result.RequiresAdminRecovery)
            {
                return TextCommandResult.Error("The retained copies are already identical.");
            }

            if (sourceWord == "empty")
            {
                string expectedEmptyToken = CreateConfirmationToken(
                    controller.WarehouseId,
                    result,
                    StorageSnapshotSource.None);
                if (!MatchesConfirmation(
                    api,
                    args,
                    controller.WarehouseId,
                    StorageSnapshotSource.None,
                    expectedEmptyToken,
                    suppliedToken))
                {
                    return TextCommandResult.Error(
                        "Confirmation is missing or expired. Inspect the terminal again, then use the displayed confirm command within two minutes.");
                }
                if (!controller.TryConfirmEmptyRecovery(out string emptyFailure))
                {
                    return TextCommandResult.Error("Empty recovery reset was rejected: " + emptyFailure);
                }
                api.Logger.Warning(
                    "[VintageKinematics] Admin {0} explicitly initialized empty recovery state for warehouse {1} at {2}.",
                    args.Caller.Player?.PlayerName ?? args.Caller.FromChatGroupId.ToString(),
                    controller.WarehouseId,
                    controller.Pos);
                ClearPending(args);
                return TextCommandResult.Success(
                    "Empty recovery state initialized. The warehouse remains unavailable until its topology rebuild completes.");
            }

            StorageSnapshotSource source = sourceWord == "controller"
                ? StorageSnapshotSource.BlockEntity
                : StorageSnapshotSource.RecoveryRegistry;
            string expectedToken = CreateConfirmationToken(controller.WarehouseId, result, source);
            if (!MatchesConfirmation(
                api,
                args,
                controller.WarehouseId,
                source,
                expectedToken,
                suppliedToken))
            {
                return TextCommandResult.Error(
                    "Confirmation is missing or expired. Inspect the terminal again, then use the displayed confirm command within two minutes.");
            }
            if (!controller.TryConfirmRecovery(source, out string failureCode))
            {
                return TextCommandResult.Error("Recovery was not committed: " + failureCode);
            }

            api.Logger.Warning(
                "[VintageKinematics] Admin {0} selected {1} for warehouse {2} at {3}; both mirrors converged to revision {4}.",
                args.Caller.Player?.PlayerName ?? args.Caller.FromChatGroupId.ToString(),
                sourceWord,
                controller.WarehouseId,
                controller.Pos,
                controller.LastReconciliation.BlockEntityCopy.Record?.Revision ?? 0);
            ClearPending(args);
            return TextCommandResult.Success(failureCode == null
                ? "Recovery committed. The warehouse remains unavailable until its topology rebuild completes."
                : "Recovery selection was retained, but this warehouse stays locked because other persistence issues remain.");
        }

        internal static string CreateConfirmationToken(
            string warehouseId,
            StorageReconciliationResult result,
            StorageSnapshotSource source)
        {
            string material = warehouseId
                + "|" + source
                + "|" + DescribeCopy(result.BlockEntityCopy)
                + "|" + DescribeCopy(result.RecoveryCopy);
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        }

        private static string Describe(
            BEKineticWarehouseTerminal controller,
            StorageReconciliationResult result,
            int radius)
        {
            StringBuilder text = new StringBuilder();
            if (result.Outcome == StorageReconciliationOutcome.Identical)
            {
                return $"Warehouse at {controller.Pos} is healthy. No recovery is needed.";
            }
            if (result.Outcome
                == StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader)
            {
                return $"Warehouse at {controller.Pos} is healthy. Outdated terminal metadata was repaired automatically.";
            }

            text.AppendLine($"Warehouse recovery: LOCKED terminal at {controller.Pos}.");
            if (controller.CanConfirmEmptyRecovery)
            {
                text.AppendLine("No recoverable item data remains.");
                if (controller.SyncedStoredItems > 0)
                {
                    text.AppendLine(
                        $"Resetting as empty will permanently discard the terminal's reported {controller.SyncedStoredItems} item(s).");
                }
                text.Append($"Unlock as empty: {ConfirmationCommand(radius, "empty")}");
            }
            else
            {
                AppendCopy(text, "Terminal copy", result.BlockEntityCopy);
                AppendCopy(text, "Saved recovery copy", result.RecoveryCopy);
                AppendRecoveryChoices(text, result, radius);
                AppendRevisionLossWarning(text, result);
            }
            return text.ToString().TrimEnd();
        }

        private static void AppendCopy(
            StringBuilder text,
            string label,
            StorageSnapshotCopy copy)
        {
            if (IsUsable(copy))
            {
                text.AppendLine(DescribeCopyForPlayer(label, copy));
            }
        }

        internal static string DescribeCopyForPlayer(
            string label,
            StorageSnapshotCopy copy)
        {
            StorageRecoveryRecord record = copy?.Record;
            if (IsUsable(copy)
                && TrySnapshotStats(record, out long items, out int types))
            {
                return $"{label}: revision {record.Revision}, {items} item(s) across {types} type(s).";
            }
            return $"{label}: unavailable.";
        }

        private static void AppendRecoveryChoices(
            StringBuilder text,
            StorageReconciliationResult result,
            int radius)
        {
            AppendRecoveryChoice(
                text,
                "terminal copy",
                "controller",
                result.BlockEntityCopy,
                radius);
            AppendRecoveryChoice(
                text,
                "saved recovery copy",
                "recovery",
                result.RecoveryCopy,
                radius);
        }

        private static void AppendRevisionLossWarning(
            StringBuilder text,
            StorageReconciliationResult result)
        {
            long metadataRevision = Math.Max(
                result.BlockEntityCopy.Header?.Revision ?? 0,
                result.RecoveryCopy.Header?.Revision ?? 0);
            long newestSnapshotRevision = Math.Max(
                IsUsable(result.BlockEntityCopy)
                    ? result.BlockEntityCopy.Record.Revision
                    : 0,
                IsUsable(result.RecoveryCopy)
                    ? result.RecoveryCopy.Record.Revision
                    : 0);
            if (metadataRevision > newestSnapshotRevision && newestSnapshotRevision > 0)
            {
                text.Append(
                    $"Warning: metadata reached revision {metadataRevision}; the newest snapshot is revision {newestSnapshotRevision}. Later item changes are unrecoverable.");
            }
        }

        private static void AppendRecoveryChoice(
            StringBuilder text,
            string label,
            string commandSource,
            StorageSnapshotCopy copy,
            int radius)
        {
            if (copy?.State != StorageSnapshotCopyState.Valid
                || copy.Record == null
                || copy.Record.IsTombstone)
            {
                return;
            }
            text.AppendLine(
                $"Restore the {label}: {ConfirmationCommand(radius, commandSource)}");
        }

        internal static string ConfirmationCommand(int radius, string source)
        {
            return $"/vk storagerecover {radius} {source} confirm";
        }

        private static void RememberInspection(
            ICoreServerAPI api,
            TextCommandCallingArgs args,
            BEKineticWarehouseTerminal controller,
            StorageReconciliationResult result)
        {
            var tokens = new Dictionary<StorageSnapshotSource, string>();
            if (IsUsable(result.BlockEntityCopy))
            {
                tokens[StorageSnapshotSource.BlockEntity] = CreateConfirmationToken(
                    controller.WarehouseId,
                    result,
                    StorageSnapshotSource.BlockEntity);
            }
            if (IsUsable(result.RecoveryCopy))
            {
                tokens[StorageSnapshotSource.RecoveryRegistry] = CreateConfirmationToken(
                    controller.WarehouseId,
                    result,
                    StorageSnapshotSource.RecoveryRegistry);
            }
            if (controller.CanConfirmEmptyRecovery)
            {
                tokens[StorageSnapshotSource.None] = CreateConfirmationToken(
                    controller.WarehouseId,
                    result,
                    StorageSnapshotSource.None);
            }

            PendingConfirmations[CallerKey(args)] = new PendingConfirmation(
                controller.WarehouseId,
                api.World.ElapsedMilliseconds + ConfirmationLifetimeMilliseconds,
                tokens);
        }

        private static bool MatchesConfirmation(
            ICoreServerAPI api,
            TextCommandCallingArgs args,
            string warehouseId,
            StorageSnapshotSource source,
            string expectedToken,
            string suppliedToken)
        {
            if (string.Equals(expectedToken, suppliedToken, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (!string.Equals(suppliedToken, "confirm", StringComparison.OrdinalIgnoreCase)
                || !PendingConfirmations.TryGetValue(
                    CallerKey(args),
                    out PendingConfirmation pending)
                || pending.ExpiresAtMilliseconds < api.World.ElapsedMilliseconds
                || pending.WarehouseId != warehouseId
                || !pending.Tokens.TryGetValue(source, out string inspectedToken))
            {
                return false;
            }
            return string.Equals(inspectedToken, expectedToken, StringComparison.Ordinal);
        }

        private static bool IsUsable(StorageSnapshotCopy copy)
        {
            return copy?.State == StorageSnapshotCopyState.Valid
                && copy.Record != null
                && !copy.Record.IsTombstone;
        }

        private static void ClearPending(TextCommandCallingArgs args)
        {
            PendingConfirmations.Remove(CallerKey(args));
        }

        private static string CallerKey(TextCommandCallingArgs args)
        {
            return args.Caller.Player?.PlayerUID
                ?? "chatgroup:" + args.Caller.FromChatGroupId;
        }

        private static bool TrySnapshotStats(
            StorageRecoveryRecord record,
            out long items,
            out int types)
        {
            items = 0;
            types = 0;
            if (record == null || record.IsTombstone) return false;
            StorageSnapshotDecodeResult decoded = StorageSnapshotCodec.Decode(record.SnapshotBytes);
            if (decoded.QuarantinedEntries.Count > 0) return false;
            try
            {
                foreach (PersistedStorageEntry entry in decoded.Entries)
                {
                    items = checked(items + entry.Quantity);
                }
            }
            catch (OverflowException)
            {
                items = 0;
                return false;
            }
            types = decoded.Entries.Count;
            return true;
        }

        private sealed class PendingConfirmation
        {
            public string WarehouseId { get; }
            public long ExpiresAtMilliseconds { get; }
            public IReadOnlyDictionary<StorageSnapshotSource, string> Tokens { get; }

            public PendingConfirmation(
                string warehouseId,
                long expiresAtMilliseconds,
                IReadOnlyDictionary<StorageSnapshotSource, string> tokens)
            {
                WarehouseId = warehouseId;
                ExpiresAtMilliseconds = expiresAtMilliseconds;
                Tokens = tokens;
            }
        }

        private static string DescribeCopy(StorageSnapshotCopy copy)
        {
            StorageRecoveryRecord record = copy?.Record;
            string description = record == null
                ? (copy?.State ?? StorageSnapshotCopyState.Missing).ToString()
                : $"{copy.State}, revision={record.Revision}, tombstone={record.IsTombstone}, checksum={record.ChecksumHex}";
            StorageRecoveryIndexEntry header = copy?.Header;
            return header == null
                ? description
                : description + $", compact-header-revision={header.Revision}, compact-header-tombstone={header.IsTombstone}, compact-header-checksum={header.ChecksumHex}";
        }

        private static BEKineticWarehouseTerminal FindNearestController(
            ICoreServerAPI api,
            TextCommandCallingArgs args,
            int radius)
        {
            BlockSelection selected = args.Caller.Player?.CurrentBlockSelection;
            if (selected?.Position != null
                && api.World.BlockAccessor.GetBlockEntity(selected.Position)
                    is BEKineticWarehouseTerminal selectedController)
            {
                return selectedController;
            }

            EntityPos caller = args.Caller.Entity.Pos;
            BEKineticWarehouseTerminal physical = FindNearestPhysicalController(
                api,
                caller,
                System.Math.Min(radius, 16));
            if (physical != null) return physical;

            KineticStorageRecoverySystem system = api.ModLoader.GetModSystem<KineticStorageRecoverySystem>();
            IEnumerable<StorageRecoveryRecord> candidates = system.Registry.GetRecords()
                .Concat(system.ControllerRegistry.GetRecords())
                .GroupBy(record => record.WarehouseId
                    + "@" + record.Controller.Dimension
                    + ":" + record.Controller.X
                    + "," + record.Controller.InternalY
                    + "," + record.Controller.Z)
                .Select(group => group.First());
            BEKineticWarehouseTerminal nearest = null;
            double nearestDistance = double.MaxValue;
            foreach (StorageRecoveryRecord record in candidates)
            {
                StorageControllerLocation location = record.Controller;
                if (location.Dimension != caller.Dimension) continue;
                double dx = location.X + 0.5 - caller.X;
                double dy = location.InternalY + 0.5 - caller.InternalY;
                double dz = location.Z + 0.5 - caller.Z;
                double distance = dx * dx + dy * dy + dz * dz;
                if (distance > radius * radius || distance >= nearestDistance) continue;
                BlockPos pos = WorldStorageTopologySource.ToBlockPos(new StorageTopologyPosition(
                    location.X,
                    location.InternalY,
                    location.Z,
                    location.Dimension));
                if (api.World.BlockAccessor.GetBlockEntity(pos)
                    is not BEKineticWarehouseTerminal controller)
                {
                    continue;
                }
                nearest = controller;
                nearestDistance = distance;
            }
            return nearest;
        }

        private static BEKineticWarehouseTerminal FindNearestPhysicalController(
            ICoreServerAPI api,
            EntityPos caller,
            int radius)
        {
            int centerX = (int)System.Math.Floor(caller.X);
            int centerY = (int)System.Math.Floor(caller.InternalY);
            int centerZ = (int)System.Math.Floor(caller.Z);
            int dimension = caller.Dimension;
            int radiusSquared = radius * radius;
            BEKineticWarehouseTerminal nearest = null;
            int nearestDistance = int.MaxValue;

            for (int y = centerY - radius; y <= centerY + radius; y++)
            for (int z = centerZ - radius; z <= centerZ + radius; z++)
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                int dz = z - centerZ;
                int distance = dx * dx + dy * dy + dz * dz;
                if (distance > radiusSquared || distance >= nearestDistance) continue;

                BlockPos position = WorldStorageTopologySource.ToBlockPos(
                    new StorageTopologyPosition(x, y, z, dimension));
                if (api.World.BlockAccessor.GetBlockEntity(position)
                    is not BEKineticWarehouseTerminal controller)
                {
                    continue;
                }
                nearest = controller;
                nearestDistance = distance;
            }
            return nearest;
        }
    }
}
