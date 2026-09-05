using System.IO;
using ProtoBuf;
using VRPG.Modules.Rpg.Movement;
using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class MovementEffectTests
{
    [Fact]
    public void HorizontalSetPreservesVerticalMotion()
    {
        MovementVector result = MovementEffectMath.Apply(
            new MovementVector(1, 0.4, 2),
            new MovementVector(5, 9, 6),
            MovementEffectMode.SetVelocity,
            MovementEffectAxes.Horizontal,
            12f,
            0f);

        Assert.Equal(new MovementVector(5, 0.4, 6), result);
    }

    [Fact]
    public void ImpulseAddsOnlyOwnedAxes()
    {
        MovementVector result = MovementEffectMath.Apply(
            new MovementVector(1, 2, 3),
            new MovementVector(0.5, 4, -1),
            MovementEffectMode.AddImpulse,
            MovementEffectAxes.X | MovementEffectAxes.Y,
            12f,
            0f);

        Assert.Equal(new MovementVector(1.5, 6, 3), result);
    }

    [Fact]
    public void BlendConvergesWithoutOvershoot()
    {
        MovementVector result = MovementEffectMath.Apply(
            new MovementVector(0, 0, 0),
            new MovementVector(10, 0, 0),
            MovementEffectMode.BlendVelocity,
            MovementEffectAxes.X,
            8f,
            0.1f);

        Assert.InRange(result.X, 0.01, 9.99);
        Assert.Equal(0, result.Y);
        Assert.Equal(0, result.Z);
    }

    [Fact]
    public void PacketContractRoundTrips()
    {
        var packet = new MovementEffectPacket
        {
            Sequence = 42,
            EffectCode = "vrpg:test-launch",
            Mode = (byte)MovementEffectMode.BlendVelocity,
            Axes = (byte)MovementEffectAxes.All,
            X = 1.25,
            Y = 0.7,
            Z = -2.5,
            DurationMilliseconds = 800,
            ResponsePerSecond = 9f,
            Priority = 20,
            CollisionGraceMilliseconds = 150
        };
        using var stream = new MemoryStream();

        Serializer.Serialize(stream, packet);
        stream.Position = 0;
        MovementEffectPacket decoded = Serializer.Deserialize<MovementEffectPacket>(stream);

        Assert.Equal(packet.Sequence, decoded.Sequence);
        Assert.Equal(packet.EffectCode, decoded.EffectCode);
        Assert.Equal(packet.Mode, decoded.Mode);
        Assert.Equal(packet.Axes, decoded.Axes);
        Assert.Equal(packet.X, decoded.X);
        Assert.Equal(packet.Y, decoded.Y);
        Assert.Equal(packet.Z, decoded.Z);
        Assert.Equal(packet.DurationMilliseconds, decoded.DurationMilliseconds);
        Assert.Equal(packet.ResponsePerSecond, decoded.ResponsePerSecond);
        Assert.Equal(packet.Priority, decoded.Priority);
        Assert.Equal(packet.CollisionGraceMilliseconds, decoded.CollisionGraceMilliseconds);
    }
}
