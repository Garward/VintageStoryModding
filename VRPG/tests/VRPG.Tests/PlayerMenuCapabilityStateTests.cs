using VRPG.Client.PlayerMenu;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuCapabilityStateTests
{
    [Fact]
    public void DialogReplacementRequiresMatchingProtocolAndReadyStore()
    {
        var packet = new PlayerMenuCapabilityPacket
        {
            ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion,
            InventoryReplacementEnabled = true,
            ItemStoreReady = false,
            DialogReplacementReady = true
        };

        PlayerMenuCapabilityState state = PlayerMenuCapabilityState.FromPacket(packet);

        Assert.True(state.InventoryReplacementEnabled);
        Assert.False(state.ItemStoreReady);
        Assert.False(state.DialogReplacementReady);
    }

    [Fact]
    public void ProtocolMismatchDisablesEveryCapability()
    {
        var packet = new PlayerMenuCapabilityPacket
        {
            ProtocolVersion = PlayerMenuCapabilityProtocol.CurrentVersion + 1,
            InventoryReplacementEnabled = true,
            ItemStoreReady = true,
            DialogReplacementReady = true
        };

        Assert.Equal(
            PlayerMenuCapabilityState.Unavailable,
            PlayerMenuCapabilityState.FromPacket(packet));
    }
}
