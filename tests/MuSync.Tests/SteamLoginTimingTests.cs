using System.Reflection;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 令牌登录等待时间的守卫。
/// <para>
/// 起因（2026-11-09 实测）：直连 CM 能连上、但认证包回不来时，<c>WaitForLogOnResultAsync</c>
/// 以 360 次 × 500ms = 180 秒为上限，于是"一次尝试干等 3 分钟、三轮 9 分钟毫无反应"。
/// 历史 52 次登录的实测分布：中位 0.82 秒、96% 在 10 秒内、最长 15.8 秒。
/// 所以首次给 10 秒（快速反馈），重试给 30 秒（慢线路还有机会成功）。
/// </para>
/// </summary>
public class SteamLoginTimingTests
{
    private static int Polls(string name) =>
        (int)typeof(SteamSessionManager)
            .GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;

    [Fact]
    public void FirstAttempt_RespondsWithinTenSeconds()
    {
        var seconds = Polls("TokenLogonWaitPollsFirst") * 0.5;   // 轮询间隔 500ms
        Assert.InRange(seconds, 5, 10);
    }

    [Fact]
    public void RetryAttempt_GivesSlowLinesRoomButStaysBounded()
    {
        var seconds = Polls("TokenLogonWaitPollsRetry") * 0.5;
        // 必须覆盖实测最慢的成功案例（15.8 秒），但也不能回到 180 秒那种"干等"
        Assert.InRange(seconds, 20, 45);
    }

    [Fact]
    public void RetryWindow_IsLongerThanFirstAttempt()
    {
        Assert.True(Polls("TokenLogonWaitPollsRetry") > Polls("TokenLogonWaitPollsFirst"));
    }

    [Fact]
    public void NoWindow_ReturnsToTheOldThreeMinutes()
    {
        Assert.True(Polls("TokenLogonWaitPollsRetry") * 0.5 < 180,
            "等待又变长了：超过 180 秒会让用户以为卡死");
    }
}