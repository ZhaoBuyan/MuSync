using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 第三轮代码检查（2026-10-28）里可单测的两项：
/// #8 总开关必须真正拦住推送、#9 数据源配置变化必须被识别出来。
/// （#10 封面重试与 #11 认证重试涉及 UI / Steam 连接，靠代码审查与真机观察。）
/// </summary>
public class CodeReviewRound3Tests
{
    // ---------- #8 总开关 ----------

    private static ConfigData Config(bool enableSteamSync = true, bool pauseWhenPlayingGame = false) => new()
    {
        EnableSteamSync = enableSteamSync,
        PauseWhenPlayingGame = pauseWhenPlayingGame,
    };

    [Fact]
    public void ShouldPush_RefusesEverything_WhenMasterSwitchOff()
    {
        // 用户明确关掉「启用 Steam 同步」：音乐、程序、常驻状态都不许再推
        Assert.False(SteamStatusManager.ShouldPush(Config(enableSteamSync: false), isLoggedOn: true,
            isRealGameActive: false, manualPause: false));
    }

    [Fact]
    public void ShouldPush_AllowsNormally_WhenEverythingIsOn()
    {
        Assert.True(SteamStatusManager.ShouldPush(Config(), isLoggedOn: true,
            isRealGameActive: false, manualPause: false));
    }

    [Fact]
    public void ShouldPush_RespectsOtherGates()
    {
        // 未登录 / 玩真实游戏且设置为暂停 / 手动暂停 —— 三条既有门禁都要保留
        Assert.False(SteamStatusManager.ShouldPush(Config(), isLoggedOn: false, isRealGameActive: false, manualPause: false));
        Assert.False(SteamStatusManager.ShouldPush(Config(pauseWhenPlayingGame: true), isLoggedOn: true, isRealGameActive: true, manualPause: false));
        Assert.False(SteamStatusManager.ShouldPush(Config(), isLoggedOn: true, isRealGameActive: false, manualPause: true));
    }

    [Fact]
    public void ShouldPush_DoesNotPause_WhenGamePauseSettingIsOff()
    {
        Assert.True(SteamStatusManager.ShouldPush(Config(pauseWhenPlayingGame: false), isLoggedOn: true,
            isRealGameActive: true, manualPause: false));
    }

    // ---------- #9 数据源配置变化 ----------

    private static DataSourceConfig Source(string url, string path = "bpm", string format = "{v}", int seconds = 60) => new()
    {
        Enabled = true,
        Name = "心率",
        Url = url,
        ValuePath = path,
        Format = format,
        RefreshSeconds = seconds,
    };

    [Fact]
    public void ConfigFingerprint_ChangesWhenUrlChanges()
    {
        var a = HttpDataSource.ConfigFingerprint(Source("http://127.0.0.1:9000/a"));
        var b = HttpDataSource.ConfigFingerprint(Source("http://127.0.0.1:9000/b"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ConfigFingerprint_ChangesWhenIntervalShrinks()
    {
        // 3600 秒改成 1 秒：必须识别为配置变化，否则旧的"下一次请求时间"会把新配置压住一小时
        var slow = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: 3600));
        var fast = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: 1));

        Assert.NotEqual(slow, fast);
    }

    [Fact]
    public void ConfigFingerprint_ChangesWhenPathOrFormatChanges()
    {
        var basePrint = HttpDataSource.ConfigFingerprint(Source("http://x/y"));
        Assert.NotEqual(basePrint, HttpDataSource.ConfigFingerprint(Source("http://x/y", path: "heart_rate")));
        Assert.NotEqual(basePrint, HttpDataSource.ConfigFingerprint(Source("http://x/y", format: "❤{v}")));
    }

    [Fact]
    public void ConfigFingerprint_IsStableForSameConfig()
    {
        var a = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: 30));
        var b = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: 30));

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(0, 1)]      // 未填 → 按 1 秒算
    [InlineData(-5, 1)]
    [InlineData(60, 60)]
    public void ConfigFingerprint_UsesEffectiveInterval(int configured, int expectedEffective)
    {
        var print = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: configured));
        var expected = HttpDataSource.ConfigFingerprint(Source("http://x/y", seconds: expectedEffective));

        Assert.Equal(expected, print);
    }
}
