using VRPG.Interactions;
using Xunit;

namespace VRPG.Tests;

public sealed class InteractionRejectionCodesTests
{
    [Theory]
    [InlineData(InteractionRejection.None, "")]
    [InlineData(InteractionRejection.TooFar, "too-far")]
    [InlineData(InteractionRejection.Obstructed, "obstructed")]
    [InlineData(InteractionRejection.PermissionDenied, "permission-denied")]
    [InlineData(InteractionRejection.CoolingDown, "cooling-down")]
    public void ProducesStableProtocolCodes(InteractionRejection rejection, string expected)
    {
        Assert.Equal(expected, InteractionRejectionCodes.From(rejection));
    }
}
