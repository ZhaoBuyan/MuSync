using System.Collections.Generic;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 模板积木的解析 / 编译（<see cref="FormatBlockCodec"/>）测试。
/// <para>
/// 重点钉住"自定义数据源积木"：用户手打的 <c>{心率}</c> 必须在编辑器里作为一个**积木**出现，
/// 否则它会被当成一段普通文字，用户既看不见也排不了序 —— 那正是这块功能原本的体验缺口。
/// </para>
/// </summary>
public class FormatBlockCodecTests
{
    private static string RoundTrip(string format) =>
        FormatBlockCodec.Compile(FormatBlockCodec.Parse(format));

    [Fact]
    public void Parse_CustomDataSource_BecomesDataSourceBlock()
    {
        var blocks = FormatBlockCodec.Parse("{song} {心率}");

        Assert.Contains(blocks, b => b.Type == FormatBlockType.DataSource && b.Text == "心率");
        // 空格仍是独立的文字块
        Assert.Contains(blocks, b => b.Type == FormatBlockType.Custom && b.Text == " ");
    }

    [Fact]
    public void Compile_DataSourceBlock_ProducesBraceToken()
    {
        var blocks = new List<FormatBlock>
        {
            new() { Type = FormatBlockType.Song },
            new() { Type = FormatBlockType.Custom, Text = " " },
            new() { Type = FormatBlockType.DataSource, Text = "心率" }
        };

        Assert.Equal("{song} {心率}", FormatBlockCodec.Compile(blocks));
    }

    [Theory]
    [InlineData("{song}")]
    [InlineData("{song} {artistPart}")]
    [InlineData("{app} {sep} {song}{artistPart}")]
    [InlineData("{song} ❤{心率}")]
    [InlineData("{song} {心率} {步数}")]
    [InlineData("纯文字没有任何积木")]
    [InlineData("")]
    public void RoundTrip_PreservesFormat(string format)
    {
        // 编辑器往返不能改动用户的模板（否则"打开编辑器再点确定"会悄悄改坏格式）
        Assert.Equal(format, RoundTrip(format));
    }

    [Fact]
    public void Parse_UnclosedBrace_StaysAsLiteralText()
    {
        // 没有配对的 { 不是积木，必须原样保留成文字
        var blocks = FormatBlockCodec.Parse("{未闭合");

        Assert.DoesNotContain(blocks, b => b.Type == FormatBlockType.DataSource);
        Assert.Equal("{未闭合", FormatBlockCodec.Compile(blocks));
    }

    [Fact]
    public void Parse_TooLongName_StaysAsLiteralText()
    {
        // 名字超过上限时模板引擎本来也认不出，编辑器不该把它当积木（否则点确定后格式会变）
        var longName = new string('X', SteamStatusManager.MaxBlockNameLength + 1);
        var format = "{" + longName + "}";

        var blocks = FormatBlockCodec.Parse(format);

        Assert.DoesNotContain(blocks, b => b.Type == FormatBlockType.DataSource);
        Assert.Equal(format, FormatBlockCodec.Compile(blocks));
    }

    [Fact]
    public void Parse_BuiltinToken_WinsOverDataSource()
    {
        // {song} 是内建积木，不能被当成名字叫 song 的数据源
        var blocks = FormatBlockCodec.Parse("{song}");

        Assert.Single(blocks);
        Assert.Equal(FormatBlockType.Song, blocks[0].Type);
    }

    [Fact]
    public void Parse_ArtistBeforeArtistPart_IsNotConfused()
    {
        // {artistPart} 必须以 ArtistPart 解析，不能先命中 {artist} 再留下 "Part}"
        var blocks = FormatBlockCodec.Parse("{artistPart}");

        Assert.Single(blocks);
        Assert.Equal(FormatBlockType.ArtistPart, blocks[0].Type);
    }
    [Fact]
    public void Parse_ProgressToken_SplitsIntoBarAndTime()
    {
        // {progress} 是"条 + 时间"的老写法。编辑器里不保留"旧条目"，
        // 打开时直接拆成两块；渲染结果必须与原来一致（见下一条测试）。
        var blocks = FormatBlockCodec.Parse("{song}{progress}");

        Assert.Equal(FormatBlockType.Song, blocks[0].Type);
        Assert.Contains(blocks, b => b.Type == FormatBlockType.Bar);
        Assert.Contains(blocks, b => b.Type == FormatBlockType.Time);
        Assert.Equal("{song}{bar}{time}", FormatBlockCodec.Compile(blocks));
    }

    [Fact]
    public void BarAndTime_AreSeparateTokens()
    {
        Assert.Equal("{song} {bar} {time}", RoundTrip("{song} {bar} {time}"));
    }

    [Fact]
    public void Parse_BarAndTime_ProduceTheirOwnBlockTypes()
    {
        var blocks = FormatBlockCodec.Parse("{bar}{time}");

        Assert.Equal(2, blocks.Count);
        Assert.Equal(FormatBlockType.Bar, blocks[0].Type);
        Assert.Equal(FormatBlockType.Time, blocks[1].Type);
    }
}