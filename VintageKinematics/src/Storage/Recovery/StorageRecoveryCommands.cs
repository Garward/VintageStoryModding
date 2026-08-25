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
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.Storage.Recovery
{
    internal static class StorageRecoveryCommands
    {
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
                if (!string.Equals(
                    expectedEmptyToken,
                    suppliedToken,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return TextCommandResult.Error(
                        "Empty-reset confirmation token is missing or stale. Run the command without a source to inspect current evidence.");
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
                return TextCommandResult.Success(
                    "Empty recovery state initialized. The warehouse remains unavailable until its topology rebuild completes.");
            }

            StorageSnapshotSource source = sourceWord == "controller"
                ? StorageSnapshotSource.BlockEntity
                : StorageSnapshotSource.RecoveryRegistry;
            string expectedToken = CreateConfirmationToken(controller.WarehouseId, result, source);
            if (!string.Equals(expectedToken, suppliedToken, StringComparison.OrdinalIgnoreCase))
            {
                return TextCommandResult.Error(
                    "Recovery confirmation token is missing or stale. Run the command without a source to inspect current copies.");
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
            text.AppendLine("Inspection only; no warehouse data was changed.");
            text.AppendLine($"Warehouse {controller.WarehouseId} at {controller.Pos}; outcome={result.Outcome}.");
            AppendCopy(text, "controller", result.BlockEntityCopy, controller.WarehouseId, result, radius);
            AppendCopy(text, "recovery", result.RecoveryCopy, controller.WarehouseId, result, radius);
            if (result.Outcome == StorageReconciliationOutcome.Identical)
            {
                text.Append("Copies are identical; no recovery selection is required.");
            }
            else if (result.Outcome
                == StorageReconciliationOutcome.IdenticalMirrorsWithStaleHeader)
            {
                text.Append("Full mirrors are identical; the stale controller header was repaired automatically.");
            }
            else if (controller.CanConfirmEmptyRecovery)
            {
                string token = CreateConfirmationToken(
                    controller.WarehouseId,
                    result,
                    StorageSnapshotSource.None);
                text.Append(
                    $"No valid item copy exists and the synchronized stored count is zero; "
                    + $"explicit empty reset: /vk storagerecover {radius} empty {token}");
            }
            else if (result.RequiresAdminRecovery)
            {
                text.Append(
                    "Recovery remains locked until one of the confirm commands shown above is run exactly.");
            }
            return text.ToString();
        }

        private static void AppendCopy(
            StringBuilder text,
            string label,
            StorageSnapshotCopy copy,
            string warehouseId,
            StorageReconciliationResult result,
            int radius)
        {
            text.Append($"{label}: {DescribeCopy(copy)}");
            if (copy.State == StorageSnapshotCopyState.Valid
                && copy.Record != null
                && !copy.Record.IsTombstone
                && result.RequiresAdminRecovery)
            {
                StorageSnapshotSource source = label == "controller"
                    ? StorageSnapshotSource.BlockEntity
                    : StorageSnapshotSource.RecoveryRegistry;
                string token = CreateConfirmationToken(warehouseId, result, source);
                text.Append($"; confirm: /vk storagerecover {radius} {label} {token}");
            }
            text.AppendLine();
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
