using VRPG.Network;
using Xunit;

namespace VRPG.Tests;

public sealed class VisualDamageTypesTests
{
    [Theory]
    [InlineData("vrpg:physical", 0)]
    [InlineData("physical", 0)]
    [InlineData("vrpg:fire", 1)]
    [InlineData("vrpg:cold", 2)]
    [InlineData("vrpg:lightning", 3)]
    [InlineData("vrpg:rust", 4)]
    [InlineData("vrpg:bleed", 5)]
    [InlineData("", 0)]
    [InlineData("vrpg:someday-new-type", 0)]
    public void MapsDamageCodesWithPhysicalFallback(string code, byte expected)
    {
        Assert.Equal(expected, VisualDamageTypes.FromCode(code));
    }

    [Fact]
    public void EveryIdHasAnOpaqueColor()
    {
        for (byte id = 0; id <= VisualDamageTypes.Heal; id++)
        {
            int color = VisualDamageTypes.ColorRgba(id);
            Assert.NotEqual(0, color & unchecked((int)0xff000000));
        }
    }
}
