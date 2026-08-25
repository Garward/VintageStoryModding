using System.IO;
using ProtoBuf;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class SkillCastRequestPacketTests
{
    [Fact]
    public void ReleasedStateSurvivesProtobufRoundTrip()
    {
        var packet = new SkillCastRequestPacket { Slot = 2, Pressed = false };
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, packet);
        stream.Position = 0;
        SkillCastRequestPacket decoded = Serializer.Deserialize<SkillCastRequestPacket>(stream);

        Assert.Equal(2, decoded.Slot);
        Assert.False(decoded.Pressed);
    }

    [Fact]
    public void CarrierSynchronizedVisualSurvivesProtobufRoundTrip()
    {
        var packet = new CombatVisualEventPacket
        {
            Kind = (byte)CombatVisualKind.Burst,
            SourceEntityId = 14,
            TargetEntityId = 27,
            Flags = (int)CombatVisualFlags.SynchronizeToCarrier,
            ServerEventMs = 123456
        };
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, packet);
        stream.Position = 0;
        CombatVisualEventPacket decoded = Serializer.Deserialize<CombatVisualEventPacket>(stream);

        Assert.Equal(14, decoded.SourceEntityId);
        Assert.Equal(27, decoded.TargetEntityId);
        Assert.True((decoded.Flags & (int)CombatVisualFlags.SynchronizeToCarrier) != 0);
        Assert.Equal(123456, decoded.ServerEventMs);
    }

    [Fact]
    public void SkillChargeCountsSurviveProtobufRoundTrip()
    {
        var packet = new SkillLoadoutPacket
        {
            Slots = new[]
            {
                new SkillLoadoutSlotPacket
                {
                    Slot = 2,
                    Code = "vrpg:junk_toss",
                    CurrentCharges = 3,
                    MaximumCharges = 5,
                    CooldownRemainingSeconds = 1.25f
                }
            }
        };
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, packet);
        stream.Position = 0;
        SkillLoadoutPacket decoded = Serializer.Deserialize<SkillLoadoutPacket>(stream);

        Assert.Equal(3, decoded.Slots[0].CurrentCharges);
        Assert.Equal(5, decoded.Slots[0].MaximumCharges);
        Assert.Equal(1.25f, decoded.Slots[0].CooldownRemainingSeconds);
    }

    [Fact]
    public void EmptySkillChargeCountSurvivesProtobufRoundTrip()
    {
        var packet = new SkillLoadoutPacket
        {
            Slots = new[]
            {
                new SkillLoadoutSlotPacket
                {
                    Slot = 1,
                    Code = "vrpg:junk_toss",
                    CurrentCharges = 0,
                    MaximumCharges = 5,
                    CooldownSeconds = 1.8f,
                    CooldownRemainingSeconds = 1.8f
                }
            }
        };
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, packet);
        stream.Position = 0;
        SkillLoadoutPacket decoded = Serializer.Deserialize<SkillLoadoutPacket>(stream);

        Assert.Equal(0, decoded.Slots[0].CurrentCharges);
        Assert.Equal(5, decoded.Slots[0].MaximumCharges);
    }
}
