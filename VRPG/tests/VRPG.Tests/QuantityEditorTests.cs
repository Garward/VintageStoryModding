using System.Globalization;
using VRPG.Client.PlayerMenu.Components;
using Xunit;

namespace VRPG.Tests;

public sealed class QuantityEditorTests
{
    [Theory]
    [InlineData("0")][InlineData("-1")][InlineData("1.5")][InlineData("9223372036854775808")][InlineData("12,34")]
    public void RejectsInvalidWithoutChangingTypedInput(string text)
    {
        var editor = new QuantityEditor(CultureInfo.GetCultureInfo("en-US"));
        editor.SetLimits(long.MaxValue, 0, long.MaxValue); editor.Type(text);
        Assert.False(editor.CanSubmit); Assert.Equal(text, editor.Text);
    }

    [Theory]
    [InlineData("en-US", "9,223,372,036,854,775,807")]
    [InlineData("de-DE", "9.223.372.036.854.775.807")]
    public void PreservesExactInt64AndExplicitCulture(string culture, string text)
    {
        var editor = new QuantityEditor(CultureInfo.GetCultureInfo(culture));
        editor.SetLimits(long.MaxValue, 0, long.MaxValue); editor.Type(text);
        Assert.True(editor.CanSubmit); Assert.Equal(long.MaxValue, editor.Value);
    }

    [Fact]
    public void LimitsRevalidateWithoutClampingAndShortcutsRespectDestination()
    {
        var editor = new QuantityEditor(CultureInfo.InvariantCulture);
        editor.SetLimits(100, 10, 12); editor.Max(); Assert.Equal(12, editor.Value);
        editor.Type("20"); Assert.False(editor.CanSubmit);
        editor.SetLimits(100, 10, 30); Assert.True(editor.CanSubmit);
        editor.SetLimits(21, 2, 30); Assert.False(editor.CanSubmit); Assert.Equal("20", editor.Text);
        editor.SetLimits(1, 0, 10); editor.Half(); Assert.Equal(1, editor.Value);
        editor.SetLimits(1, 1, 10); Assert.False(editor.CanSubmit);
    }
}
