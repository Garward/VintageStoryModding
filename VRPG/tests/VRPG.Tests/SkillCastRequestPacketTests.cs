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
}
