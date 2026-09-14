using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class BarStyleParserTests
{
    [Theory]
    [InlineData("#-", "#", "-")]
    [InlineData("█░", "█", "░")]
    [InlineData("▰▱", "▰", "▱")]
    [InlineData("█", "█", "-")]
    [InlineData("", "#", "-")]
    public void Parse_AsciiAndBlockStyles(string input, string fill, string empty)
    {
        var (f, e) = BarStyleParser.Parse(input);
        Assert.Equal(fill, f);
        Assert.Equal(empty, e);
    }

    [Fact]
    public void Parse_EmojiWithVariationSelector_NotSplit()
    {
        var (fill, empty) = BarStyleParser.Parse("❤️💕");
        Assert.Equal("❤️", fill);
        Assert.Equal("💕", empty);
    }

    [Fact]
    public void Parse_RepeatedEmojiString_DetectsTwoDistinctChars()
    {
        // 直接粘贴 ❤️❤️💕💕💕💕 → 填充 ❤️、空白 💕
        var (fill, empty) = BarStyleParser.Parse("❤️❤️💕💕💕💕");
        Assert.Equal("❤️", fill);
        Assert.Equal("💕", empty);
    }

    [Fact]
    public void Parse_SameCharTwice_KeepsSecondAsEmpty()
    {
        var (fill, empty) = BarStyleParser.Parse("██");
        Assert.Equal("█", fill);
        Assert.Equal("█", empty);
    }

    [Fact]
    public void Parse_WhitespaceInputs_FallBackToDefaults()
    {
        var (fill, empty) = BarStyleParser.Parse("  ");
        Assert.Equal("#", fill);
        Assert.Equal("-", empty);
    }

    [Fact]
    public void Parse_WhitespaceAroundChars_IsFiltered()
    {
        var (fill, empty) = BarStyleParser.Parse(" █░");
        Assert.Equal("█", fill);
        Assert.Equal("░", empty);
    }

    [Fact]
    public void Normalize_TakesFirstVisibleTextElement()
    {
        Assert.Equal("❤️", BarStyleParser.Normalize("❤️", "#"));
        Assert.Equal("█", BarStyleParser.Normalize("█░", "-"));
        Assert.Equal("#", BarStyleParser.Normalize("", "#"));
        Assert.Equal("#", BarStyleParser.Normalize(" ", "#"));
        Assert.Equal("-", BarStyleParser.Normalize(null, "-"));
    }
}
