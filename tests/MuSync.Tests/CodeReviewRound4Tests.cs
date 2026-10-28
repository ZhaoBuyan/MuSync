using System;
using System.Reflection;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 第四轮代码检查（2026-10-28）里可单测的一项：
/// #12「不勾记住我」不该连带丢掉**本次运行期间**的自动重登能力。
/// （R3 封面重试、R4 排队代际都属于行为/时序问题，靠代码审查与真机观察。）
/// </summary>
public class CodeReviewRound4Tests
{
    private static SteamSessionManager NewSession() => new();

    private static string EffectiveToken(SteamSessionManager session) =>
        (string)typeof(SteamSessionManager)
            .GetMethod("EffectiveRefreshToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, null)!;

    private static void SetInMemoryToken(SteamSessionManager session, string token) =>
        typeof(SteamSessionManager)
            .GetField("_inMemoryRefreshToken", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, token);

    [Fact]
    public void EffectiveToken_PrefersSavedToken()
    {
        var settings = Configurations.Instance.Settings;
        var backup = settings.SteamRefreshToken;
        try
        {
            settings.SteamRefreshToken = "保存的令牌";
            using var session = NewSession();
            SetInMemoryToken(session, "内存令牌");

            Assert.Equal("保存的令牌", EffectiveToken(session));
        }
        finally { settings.SteamRefreshToken = backup; }
    }

    [Fact]
    public void EffectiveToken_FallsBackToInMemory_WhenNothingSaved()
    {
        var settings = Configurations.Instance.Settings;
        var backup = settings.SteamRefreshToken;
        try
        {
            // 「不勾记住我」：磁盘上为空，但本次运行内存里有令牌 → 断线后仍能自动重登
            settings.SteamRefreshToken = "";
            using var session = NewSession();
            SetInMemoryToken(session, "内存令牌");

            Assert.Equal("内存令牌", EffectiveToken(session));
        }
        finally { settings.SteamRefreshToken = backup; }
    }

    [Fact]
    public void EffectiveToken_IsEmpty_WhenNeitherExists()
    {
        var settings = Configurations.Instance.Settings;
        var backup = settings.SteamRefreshToken;
        try
        {
            settings.SteamRefreshToken = "";
            using var session = NewSession();
            SetInMemoryToken(session, "");

            Assert.Equal("", EffectiveToken(session));
        }
        finally { settings.SteamRefreshToken = backup; }
    }
}
