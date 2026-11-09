using System;
using System.Reflection;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 令牌登录等待时间的守卫。
/// <para>
/// 起因（2026-11-09 实测）：直连 CM 能连上、但认证包回不来时，<c>WaitForLogOnResultAsync</c>
/// 以 360 次 × 500ms = 180 秒为上限，于是"一次尝试干等 3 分钟、三轮 9 分钟毫无反应"。
/// 正常登录 1~2 秒就返回，等 3 分钟既没有信息量，也拖慢了换线路重试。
/// </para>
/// </summary>
public class SteamLoginTimingTests
{
    private static int TokenLogonWaitPolls() =>
        (int)typeof(SteamSessionManager)
            .GetField("TokenLogonWaitPolls", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;

    [Fact]
    public void TokenLogonWait_IsAtMostThirtySeconds()
    {
        var polls = TokenLogonWaitPolls();
        Assert.InRange(polls * 0.5, 10, 30);   // 轮询间隔 500ms
    }

    [Fact]
    public void TokenLogonWait_IsShorterThanTheOldThreeMinutes()
    {
        // 回归守卫：不许再回到 360 次（180 秒）
        Assert.True(TokenLogonWaitPolls() < 180,
            "令牌登录等待又变长了：超过 180 秒会让用户以为卡死");
    }
}
