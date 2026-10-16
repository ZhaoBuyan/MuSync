using System;
using System.Text;
using MuSync.Players;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 酷狗支持的纯解析逻辑测试：窗口标题解析与内存里 UTF-16「MM:SS/MM:SS」的时间对解析。
/// <para>
/// 为什么值得单独测：这两段是酷狗支持里最容易出错、又最难靠实机复现的部分 ——
/// 内存扫描是一次"字符串模式搜索"，误匹配的代价是给 Steam 推出错误的进度/时长；
/// 而标题解析要处理歌手可缺省、歌名里本身含 " - "、未播放时的固定标题等情况。
/// 二者都是纯函数（不碰进程），可以脱离酷狗本体直接测，因此投入产出比很高。
/// </para>
/// </summary>
public class KuGouParsingTests
{
    /// <summary>把「MM:SS/MM:SS」按酷狗内存里的形态编码（UTF-16LE，每字符后跟一个 0x00）。</summary>
    private static byte[] Pair(string text) => Encoding.Unicode.GetBytes(text);

    /// <summary>
    /// 构造「前面有一小段垃圾、payload 从非零偏移开始」的缓冲区，返回 (缓冲区, payload 起始偏移)。
    /// 内存扫描就是在任意偏移处切片的，所以这里模拟的是**切片从 payload 起点开始**的情形。
    /// </summary>
    private static (byte[] Buffer, int Offset) PairAtGarbageOffset(string pairText, string garbage = "xxx")
    {
        var garbageBytes = Pair(garbage);
        var payload = Pair(pairText);
        var buffer = new byte[garbageBytes.Length + payload.Length];
        Array.Copy(garbageBytes, 0, buffer, 0, garbageBytes.Length);
        Array.Copy(payload, 0, buffer, garbageBytes.Length, payload.Length);
        return (buffer, garbageBytes.Length);
    }

    // ================= 窗口标题 =================

    [Fact]
    public void ParseWindowTitle_ArtistAndSong()
    {
        var track = KuGou.ParseWindowTitle("周杰伦 - 稻香 - 酷狗音乐");

        Assert.NotNull(track);
        Assert.Equal("周杰伦", track!.Value.Artist);
        Assert.Equal("稻香", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_NoArtist_ReturnsEmptyArtist()
    {
        var track = KuGou.ParseWindowTitle("稻香 - 酷狗音乐");

        Assert.NotNull(track);
        Assert.Equal("", track!.Value.Artist);
        Assert.Equal("稻香", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_SongContainingSeparator_KeepsRemainder()
    {
        // 歌名里本身带 " - " 时，只按第一个分隔符切分，其余保留
        var track = KuGou.ParseWindowTitle("Aimer - Brave Shine - Fate/stay night - 酷狗音乐");

        Assert.NotNull(track);
        Assert.Equal("Aimer", track!.Value.Artist);
        Assert.Equal("Brave Shine - Fate/stay night", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_TrimsSurroundingWhitespace()
    {
        var track = KuGou.ParseWindowTitle("  周杰伦 - 稻香 - 酷狗音乐  ");

        Assert.NotNull(track);
        Assert.Equal("周杰伦", track!.Value.Artist);
        Assert.Equal("稻香", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_IdleTitle_ReturnsNull()
    {
        // 未在播放时窗口标题就是固定标题，不该被当成一首歌
        Assert.Null(KuGou.ParseWindowTitle("酷狗音乐"));
    }

    [Fact]
    public void ParseWindowTitle_TrailingDashFromEmptySong_TreatedAsWholeTitle()
    {
        // "周杰伦 -  - 酷狗音乐" 去掉固定后缀与尾部空白后剩 "周杰伦 -"，里面已无完整 " - "，
        // 于是走"无歌手、整串当歌名"分支（这种无意义的标题由调用方按 Title 为空/无效处理）。
        // 这里把既有行为钉住：它不会崩、也不会编造出一个歌手名。
        var track = KuGou.ParseWindowTitle("周杰伦 -  - 酷狗音乐");

        Assert.NotNull(track);
        Assert.Equal("", track!.Value.Artist);
        Assert.Equal("周杰伦 -", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_TwoSongsWithSeparator_UsesFirstAsArtist()
    {
        // 正常两段式标题不受上面那条影响
        var track = KuGou.ParseWindowTitle("周杰伦 - 稻香 - 酷狗音乐");

        Assert.NotNull(track);
        Assert.Equal("周杰伦", track!.Value.Artist);
        Assert.Equal("稻香", track.Value.Title);
    }

    [Fact]
    public void ParseWindowTitle_NonKuGouTitle_ReturnsWholeAsSong()
    {
        // 标题里没有「- 酷狗音乐」后缀时按"无歌手、整串是歌名"处理（调用方另有「必须含酷狗音乐」的前置判断）
        var track = KuGou.ParseWindowTitle("Some Song");

        Assert.NotNull(track);
        Assert.Equal("", track!.Value.Artist);
        Assert.Equal("Some Song", track.Value.Title);
    }

    // ================= 时间对（内存里的进度文本） =================

    [Fact]
    public void TryParseProgressPair_NormalValues_AreParsed()
    {
        var ok = KuGou.TryParseProgressPair(Pair("02:30/05:10"), out var progress, out var duration);

        Assert.True(ok);
        Assert.Equal(150, progress, 3);
        Assert.Equal(310, duration, 3);
    }

    [Fact]
    public void TryParseProgressPair_SingleDigitMinute_IsParsed()
    {
        var ok = KuGou.TryParseProgressPair(Pair("1:05/3:20"), out var progress, out var duration);

        Assert.True(ok);
        Assert.Equal(65, progress, 3);
        Assert.Equal(200, duration, 3);
    }

    [Fact]
    public void TryParseProgressPair_ZeroProgress_IsAccepted()
    {
        // 刚切歌 / 刚暂停时进度为 0，是合法读数
        var ok = KuGou.TryParseProgressPair(Pair("00:00/04:00"), out var progress, out var duration);

        Assert.True(ok);
        Assert.Equal(0, progress, 3);
        Assert.Equal(240, duration, 3);
    }

    [Fact]
    public void TryParseProgressPair_TooShortDuration_IsRejected()
    {
        // 时长低于 30 秒：几乎可以肯定是误匹配（酷狗不会放这么短的"歌"）
        Assert.False(KuGou.TryParseProgressPair(Pair("00:10/00:20"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_ProgressBeyondDuration_IsRejected()
    {
        Assert.False(KuGou.TryParseProgressPair(Pair("05:00/03:00"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_SecondComponentOver59_IsRejected()
    {
        Assert.False(KuGou.TryParseProgressPair(Pair("00:99/03:00"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_MissingSlash_IsRejected()
    {
        Assert.False(KuGou.TryParseProgressPair(Pair("02:30 05:10"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_TrailingDigit_IsRejected()
    {
        // 从更长的数字串里截断出来的"看起来像时间对"必须被拒绝
        Assert.False(KuGou.TryParseProgressPair(Pair("02:30/05:100"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_ThreeDigitMinutes_IsNotTreatedAsMinutes()
    {
        // 三位分钟（或日期/时间戳片段）不该被当成时长
        Assert.False(KuGou.TryParseProgressPair(Pair("120:30/05:10"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_EmptySpan_IsRejected()
    {
        Assert.False(KuGou.TryParseProgressPair(ReadOnlySpan<byte>.Empty, out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_AtNonZeroOffset_IsParsed()
    {
        // 内存扫描是在任意偏移处切片的，所以 span 不保证从缓冲区 0 位置开始
        var (buffer, offset) = PairAtGarbageOffset("02:30/05:10");

        var ok = KuGou.TryParseProgressPair(buffer.AsSpan(offset), out var progress, out var duration);

        Assert.True(ok);
        Assert.Equal(150, progress, 3);
        Assert.Equal(310, duration, 3);
    }

    [Fact]
    public void TryParseProgressPair_MisalignedUtf16_IsRejected()
    {
        // 反向保护（实测确认的行为）：payload 在缓冲区内整体平移、字符错位时，
        // 绝不能解析出"看似合理"的时间对 —— 否则会把垃圾数字当进度推给 Steam。
        // 注意切片起点保持在同一位置，只让 payload 相对它错开 2 字节（错位是"相对切片"而言的）。
        var garbage = Pair("xxx");
        var payload = Pair("02:30/05:10");
        var buffer = new byte[garbage.Length + payload.Length + 2];
        Array.Copy(garbage, 0, buffer, 0, garbage.Length);
        Array.Copy(payload, 0, buffer, garbage.Length + 2, payload.Length);

        Assert.False(KuGou.TryParseProgressPair(buffer.AsSpan(garbage.Length), out _, out _));
    }

    [Fact]
    public void TryParseTime_ReportsEndPositionAfterSeconds()
    {
        var ok = KuGou.TryParseTime(Pair("02:30/05:10"), 0, out var totalSeconds, out var endPos);

        Assert.True(ok);
        Assert.Equal(150, totalSeconds);
        Assert.Equal(10, endPos);   // "02:30" = 5 个 UTF-16 字符 = 10 字节
    }

    [Fact]
    public void TryParseTime_SecondsOver59_IsRejected()
    {
        Assert.False(KuGou.TryParseTime(Pair("02:60"), 0, out _, out _));
    }

    [Fact]
    public void TryParseTime_SingleDigitSeconds_IsRejected()
    {
        // 秒必须是两位（"02:3x" 这种只可能是误匹配）
        Assert.False(KuGou.TryParseTime(Pair("02:3/05:10"), 0, out _, out _));
    }
    // ---- 3 位分钟（超过 99 分钟的曲目/视频）----

    [Fact]
    public void TryParseProgressPair_ThreeDigitMinutes_AreParsed()
    {
        // 时长上限写的是 3 小时，原先解析器只收 1~2 位分钟（≤99:59）→
        // 超过 99 分钟的媒体永远解析不出进度与时长，两个常量不自洽
        var span = Pair("100:30/168:00");
        Assert.True(KuGou.TryParseProgressPair(span, out var progress, out var duration));
        Assert.Equal(6030, progress, 3);
        Assert.Equal(10080, duration, 3);
    }

    [Fact]
    public void TryParseProgressPair_FourDigitMinutes_AreRejected()
    {
        // 分钟位最多 3 位：4 位数字不该被当成时间
        Assert.False(KuGou.TryParseProgressPair(Pair("1234:56/168:00"), out _, out _));
    }

    [Fact]
    public void TryParseProgressPair_OverThreeHours_IsRejected()
    {
        // 仍受 MaxDurationSeconds = 3 小时约束
        Assert.False(KuGou.TryParseProgressPair(Pair("10:00/200:00"), out _, out _));
    }
}