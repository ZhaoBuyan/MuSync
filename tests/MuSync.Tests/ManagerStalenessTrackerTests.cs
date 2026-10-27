using MuSync.Players;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 会话管理器"假死"判定：既不能漏判（漏了会重现"读不到浏览器媒体"），
/// 也不能乱判（乱了会每拍重建管理器，那正是 CPU 泄漏的根因）。
/// </summary>
public class ManagerStalenessTrackerTests
{
    [Fact]
    public void NeverSeenSession_IsNotSuspicious()
    {
        var tracker = new ManagerStalenessTracker();

        // 从没播过：浏览器开着但没放东西，本来就没有会话
        for (int i = 0; i < 10; i++)
            Assert.False(tracker.Note(hasBrowserSession: false, browserRunning: true));
    }

    [Fact]
    public void SawThenLost_RebuildsOnlyAfterThreshold()
    {
        var tracker = new ManagerStalenessTracker();
        Assert.False(tracker.Note(true, true));          // 正常读到

        // 第一轮丢失：还不够可疑，先不重建（避免偶发空读就重建）
        Assert.False(tracker.Note(false, true));
        // 第二轮仍丢失：判定假死
        Assert.True(tracker.Note(false, true));
    }

    [Fact]
    public void BrowserNotRunning_IsNeverSuspicious()
    {
        var tracker = new ManagerStalenessTracker();
        Assert.False(tracker.Note(true, true));

        // 浏览器都关了，没有会话完全正常
        for (int i = 0; i < 10; i++)
            Assert.False(tracker.Note(false, browserRunning: false));
    }

    [Fact]
    public void SessionComingBack_ResetsCounter()
    {
        var tracker = new ManagerStalenessTracker();
        Assert.False(tracker.Note(true, true));
        Assert.False(tracker.Note(false, true));         // 丢失 1 轮
        Assert.False(tracker.Note(true, true));          // 又读到了 → 计数清零
        Assert.Equal(0, tracker.LostPolls);
        Assert.False(tracker.Note(false, true));         // 重新从 1 开始
        Assert.True(tracker.Note(false, true));          // 到 2 才重建
    }

    [Fact]
    public void AfterRebuild_DoesNotRebuildAgain_UntilSessionReappears()
    {
        var tracker = new ManagerStalenessTracker();
        tracker.Note(true, true);
        tracker.Note(false, true);
        Assert.True(tracker.Note(false, true));      // 一次丢失事件 → 重建一次

        // 重建后若新管理器仍读不到（或视频本来就已经播完），**不能**继续重建：
        // 那会退回"每秒重建"的老问题（WinRT 对象堆积、线程爆炸）。
        // 真正的兜底是管理器自身的 10 分钟最大存活时间。
        for (int i = 0; i < 10; i++)
            Assert.False(tracker.Note(false, true));

        Assert.False(tracker.SawBrowserSession);
        Assert.Equal(0, tracker.LostPolls);

        // 一旦又读到会话，判定重新武装
        Assert.False(tracker.Note(true, true));
        Assert.False(tracker.Note(false, true));
        Assert.True(tracker.Note(false, true));
    }
}