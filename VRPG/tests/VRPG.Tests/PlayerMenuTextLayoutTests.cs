using VRPG.Client.PlayerMenu.Components;
using Xunit;

namespace VRPG.Tests;

public sealed class PlayerMenuTextLayoutTests
{
    [Fact]
    public void SearchTruncationKeepsUnicodePairsAndRemovesControlCharacters()
    {
        var field = new SearchField();
        field.SetText(new string('a', 127) + "🙂");
        Assert.Equal(new string('a', 127), field.Text);
        field.SetText("co\npper"); Assert.Equal("copper", field.Text);
    }
    [Fact]
    public void LongUnbrokenLabelsWrapWithoutLosingText()
    {
        var lines = PlayerMenuTextLayout.Wrap("abcdefghij", 4, value => value.Length);
        Assert.Equal(new[] { "abcd", "efgh", "ij" }, lines);
    }
    [Fact]
    public void OrdinaryWordsKeepTheirBoundaries()
    {
        Assert.Equal(new[] { "Copper", "ingots" }, PlayerMenuTextLayout.Wrap("Copper ingots", 9, value => value.Length));
    }
}
