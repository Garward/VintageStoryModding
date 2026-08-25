namespace VintageKinematics.Storage.Terminal
{
    public enum StorageTerminalAction : byte
    {
        DepositHeldStack = 0,
        WithdrawStackToInventory = 1,
        WithdrawOneToCursor = 2,
        DepositInventorySlot = 3
    }

    /// <summary>Intent-only transfer request; item identity remains server authoritative.</summary>
    public sealed class StorageTerminalActionRequest
    {
        public StorageTerminalAction Action { get; }
        public long EntryId { get; }
        public string SourceInventoryId { get; }
        public int SourceSlotId { get; }
        public StorageTerminalQuery RefreshQuery { get; }

        public StorageTerminalActionRequest(
            StorageTerminalAction action,
            long entryId,
            StorageTerminalQuery refreshQuery,
            string sourceInventoryId = "",
            int sourceSlotId = -1)
        {
            Action = action;
            EntryId = entryId;
            SourceInventoryId = sourceInventoryId ?? string.Empty;
            SourceSlotId = sourceSlotId;
            RefreshQuery = refreshQuery;
        }
    }
}
