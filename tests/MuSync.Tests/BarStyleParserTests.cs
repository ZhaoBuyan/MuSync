using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>进度条字符解析测试：预设 / 自定义字符与零宽字符过滤。</summary>
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
    public void Parse_ZWJFamilyEmoji_NotSplit()
    {
        // 👨‍👩‍👧‍👦（ZWJ 序列）+ 🇨🇳（旗帜）均为单个文本元素
        var (fill, empty) = BarStyleParser.Parse("👨‍👩‍👧‍👦🇨🇳");
        Assert.Equal("👨‍👩‍👧‍👦", fill);
        Assert.Equal("🇨🇳", empty);
    }

    [Fact]
    public void Parse_SkinToneModifier_NotSplit()
    {
        var (fill, empty) = BarStyleParser.Parse("👍🏽🇨🇳");
        Assert.Equal("👍🏽", fill);
        Assert.Equal("🇨🇳", empty);
    }

    [Fact]
    public void Parse_ZeroWidthCharacters_AreFiltered()
    {
        // 粘贴时混入的零宽字符（ZWSP / BOM）不应干扰解析
        var (fill, empty) = BarStyleParser.Parse("\u200B❤️\uFEFF💕\u200B");
        Assert.Equal("❤️", fill);
        Assert.Equal("💕", empty);
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
