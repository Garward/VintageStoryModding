using VintageKinematics.Storage.Terminal;
using Xunit;

namespace VintageKinematics.Tests.Storage.Terminal
{
    public sealed class StorageTerminalPacketCodecTests
    {
        [Fact]
        public void Action_RoundTripsIntentAndRefreshQuery()
        {
            var original = new StorageTerminalActionRequest(
                StorageTerminalAction.WithdrawStackToInventory,
                42,
                new StorageTerminalQuery(
                    9,
                    "copper",
                    2,
                    StorageTerminalSort.QuantityDescending,
                    80));

            byte[] bytes = StorageTerminalPacketCodec.EncodeAction(123, original);
            bool decoded = StorageTerminalPacketCodec.TryDecodeAction(
                bytes,
                out long sessionId,
                out StorageTerminalActionRequest request);

            Assert.True(decoded);
            Assert.Equal(123, sessionId);
            Assert.Equal(StorageTerminalAction.WithdrawStackToInventory, request.Action);
            Assert.Equal(42, request.EntryId);
            Assert.Equal(9, request.RefreshQuery.RequestId);
            Assert.Equal("copper", request.RefreshQuery.Search);
            Assert.Equal(2, request.RefreshQuery.Page);
            Assert.Equal(StorageTerminalSort.QuantityDescending, request.RefreshQuery.Sort);
            Assert.Equal(80, request.RefreshQuery.RequestedPageSize);
        }

        [Fact]
        public void Action_RejectsWithdrawWithoutEntryIdentity()
        {
            var invalid = new StorageTerminalActionRequest(
                StorageTerminalAction.WithdrawOneToCursor,
                0,
                new StorageTerminalQuery(1, "", 0, StorageTerminalSort.Name));

            byte[] bytes = StorageTerminalPacketCodec.EncodeAction(123, invalid);

            Assert.False(StorageTerminalPacketCodec.TryDecodeAction(
                bytes,
                out _,
                out _));
        }

        [Fact]
        public void Action_RoundTripsInventorySlotDepositSource()
        {
            var original = new StorageTerminalActionRequest(
                StorageTerminalAction.DepositInventorySlot,
                0,
                new StorageTerminalQuery(4, "", 0, StorageTerminalSort.Name),
                "backpack-player",
                7);

            byte[] bytes = StorageTerminalPacketCodec.EncodeAction(321, original);

            Assert.True(StorageTerminalPacketCodec.TryDecodeAction(
                bytes,
                out _,
                out StorageTerminalActionRequest request));
            Assert.Equal("backpack-player", request.SourceInventoryId);
            Assert.Equal(7, request.SourceSlotId);
        }

        [Fact]
        public void Action_RejectsOversizedPayloadBeforeParsing()
        {
            Assert.False(StorageTerminalPacketCodec.TryDecodeAction(
                new byte[513],
                out _,
                out _));
        }
    }
}
