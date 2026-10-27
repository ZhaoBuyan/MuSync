using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 「浏览器扩展增强」的端口/令牌落库逻辑。
/// <para>
/// 这组用例刻意**不**走 SettingsForm.SaveSettings：那会把整份配置（含数据源列表）按界面回写，
/// 污染后续测试。被测的是纯函数 <see cref="SettingsForm.ApplyExtensionSettings"/>。
/// </para>
/// <para>
/// 曾经的 bug：令牌赋值被嵌在"端口合法才保存"的 if 里 —— 端口填错时令牌一起丢失，且无任何提示。
/// </para>
/// </summary>
public class ExtensionSettingsApplyTests
{
    private static ConfigData NewConfig(int port = 47564, string token = "") => new()
    {
        BrowserExtensionPort = port,
        BrowserExtensionToken = token,
    };

    [Fact]
    public void ValidPort_IsApplied_NoWarning()
    {
        var settings = NewConfig();

        var warning = SettingsForm.ApplyExtensionSettings(settings, " 47570 ", "abc");

        Assert.Null(warning);
        Assert.Equal(47570, settings.BrowserExtensionPort);
        Assert.Equal("abc", settings.BrowserExtensionToken);
    }

    [Theory]
    [InlineData("not-a-port")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidPort_KeepsPreviousValue_AndStillSavesToken(string? portText)
    {
        var settings = NewConfig(port: 47564, token: "");

        var warning = SettingsForm.ApplyExtensionSettings(settings, portText, "  token-要保存  ");

        Assert.NotNull(warning);
        Assert.Contains(Loc.L("端口需", "Port must be"), warning);
        Assert.Equal(47564, settings.BrowserExtensionPort);            // 端口保留原值
        Assert.Equal("token-要保存", settings.BrowserExtensionToken);   // 令牌照样保存（这是之前的 bug）
    }

    [Fact]
    public void EmptyToken_MeansNoAuth_AndClearsPreviousToken()
    {
        var settings = NewConfig(token: "旧令牌");

        var warning = SettingsForm.ApplyExtensionSettings(settings, "47564", "   ");

        Assert.Null(warning);
        Assert.Equal("", settings.BrowserExtensionToken);
        Assert.Equal(47564, settings.BrowserExtensionPort);
    }

    [Fact]
    public void WarningText_MentionsTheKeptValue()
    {
        var settings = NewConfig(port: 47600);

        var warning = SettingsForm.ApplyExtensionSettings(settings, "abc", "");

        Assert.NotNull(warning);
        Assert.Contains("47600", warning);
    }
}
