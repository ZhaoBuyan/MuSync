using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 状态的最后一道处理：控制字符必须被清掉，长度必须仍在 128 字节以内。
/// 进状态的文本来自播放器内存、窗口标题、设置里的分隔符、扩展上报 —— 都不是"干净"的输入。
/// </summary>
public class StatusSanitizationTests
{
    private static ConfigData Config() => new()
    {
        CombinedFormat = "{app}{sep}{song}",
        ProgramFormat = "{app}",
        CombinedSeparator = " | ",
    };

    private static PlayerInfo Song(string title, string artist = "") => new()
    {
        Identity = "test",
        Title = title,
        Artists = artist,
        Album = "",
        Cover = "",
        CoverImage = null,
        Schedule = 10,
        Duration = 100,
        Url = "",
        Pause = false,
    };

    [Theory]
    [InlineData("正常的歌名")]
    [InlineData("换行\r\n注入")]
    [InlineData("制表\t符")]
    [InlineData("控制\u0007字符")]
    public void ComposedStatus_NeverContainsControlCharacters(string title)
    {
        var status = SteamStatusManager.ComposeStatus(Song(title), null, Config());

        Assert.NotNull(status);
        foreach (var c in status!)
        {
            Assert.False(char.IsControl(c), $"状态里仍有控制字符 U+{(int)c:X4}");
        }
    }

    [Fact]
    public void ControlCharacters_AreReplacedNotDropped()
    {
        // 换行两边的内容都要保留，只是被空格替代（避免两段粘成一个词）
        var status = SteamStatusManager.ComposeStatus(Song("前半\r\n后半"), null, Config());

        Assert.NotNull(status);
        Assert.Contains("前半", status);
        Assert.Contains("后半", status);
    }

    [Fact]
    public void SeparatorWithNewline_DoesNotBreakStatus()
    {
        var config = Config();
        config.CombinedSeparator = " |\n";
        var status = SteamStatusManager.ComposeStatus(Song("歌名"), "程序名", config);

        Assert.NotNull(status);
        Assert.DoesNotContain('\n', status!);
    }

    [Fact]
    public void StripControlChars_KeepsEmojiAndCjk()
    {
        var cleaned = SteamStatusManager.StripControlChars("透明軌道 🎵\u0000 测试");

        Assert.Contains("透明軌道", cleaned);
        Assert.Contains("🎵", cleaned);
        Assert.Contains("测试", cleaned);
        Assert.DoesNotContain('\u0000', cleaned);
    }

    [Fact]
    public void LongCjkTitle_IsTruncatedWithin128Bytes()
    {
        var longTitle = new string('歌', 200);
        var status = SteamStatusManager.ComposeStatus(Song(longTitle), null, Config());

        Assert.NotNull(status);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(status!) <= 128);
        // 不能截出半个字符
        Assert.DoesNotContain('\uFFFD', status);
    }

    [Fact]
    public void NormalStatusIsUnchanged()
    {
        var status = SteamStatusManager.ComposeStatus(Song("狐雨", "某某"), "Chrome", Config());

        Assert.NotNull(status);
        Assert.Contains("狐雨", status);
        Assert.Contains("Chrome", status);
    }
}
