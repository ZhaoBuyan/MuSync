using System.Text;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 安全相关的边界：哪些字符串可以交给 Shell 打开、哪些必须挡掉；
/// 以及网页可控文本进入状态前必须清洗控制字符。
/// </summary>
public class SecurityGuardsTests
{
    [Theory]
    [InlineData("https://music.163.com/song?id=1", true)]
    [InlineData("http://127.0.0.1:9000/heartrate", true)]
    [InlineData("HTTPS://Example.COM/x", true)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]   // 本地文件
    [InlineData("C:\\Windows\\System32\\cmd.exe", false)]        // 直接可执行路径
    [InlineData("\\\\attacker\\share\\x.exe", false)]            // UNC 路径
    [InlineData("cmd:/c calc", false)]                           // 自定义协议
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHttpUrl_OnlyAllowsHttpAndHttps(string? url, bool expected)
    {
        Assert.Equal(expected, SafeUrl.IsHttpUrl(url));
    }

    [Fact]
    public void TryOpenInBrowser_RefusesNonHttpWithoutLaunching()
    {
        // 关键：不能因为"看起来像路径"就去调用 Shell
        Assert.False(SafeUrl.TryOpenInBrowser("C:\\Windows\\System32\\notepad.exe", out var error));
        Assert.Null(error);   // 被拒绝 ≠ 打开失败
    }

    [Fact]
    public void ExtensionText_ControlCharactersAreStrippedBeforeStatus()
    {
        // 网页标题是别人可控的：换行/制表/控制符不能带进 Steam 状态
        var payload = Encoding.UTF8.GetBytes("""
            {"title":"第一行\u0007\r\n第二行\t带制表","artist":"作\u0000者","playing":true}
            """);

        var snapshot = BrowserExtensionBridge.Parse(payload);

        Assert.NotNull(snapshot);
        Assert.DoesNotContain('\r', snapshot!.Title);
        Assert.DoesNotContain('\n', snapshot.Title);
        Assert.DoesNotContain('\t', snapshot.Title);
        Assert.DoesNotContain('\u0007', snapshot.Title);
        Assert.Contains("第一行", snapshot.Title);
        Assert.Contains("第二行", snapshot.Title);
        // 连续空白被压成一个
        Assert.DoesNotContain("  ", snapshot.Title);
        Assert.Equal("作 者", snapshot.Artist);
    }

    [Fact]
    public void ExtensionText_NormalTextUnchanged()
    {
        var payload = Encoding.UTF8.GetBytes("""
            {"title":"透明軌道 (Constellation)","artist":"HACHI","playing":true}
            """);

        var snapshot = BrowserExtensionBridge.Parse(payload);

        Assert.NotNull(snapshot);
        Assert.Equal("透明軌道 (Constellation)", snapshot!.Title);
        Assert.Equal("HACHI", snapshot.Artist);
    }
}
