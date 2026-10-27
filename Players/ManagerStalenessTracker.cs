namespace MuSync.Players;

/// <summary>
/// 判定 SMTC 会话管理器是否"假死"，决定要不要重建它。
///
/// <para>
/// 为什么需要这个判定：缓存的 <c>GlobalSystemMediaTransportControlsSessionManager</c> 被反复调用后会假死 ——
/// 先正常返回 N 个会话，之后**持续返回 0 个**，重新 <c>RequestAsync</c> 拿到的又正常。
/// 但"每次轮询都重建"的代价太大：每次都会新建一整套 WinRT/COM 包装，只能等终结器回收。
/// 实测一晚（10.9 小时）堆积 31,995 个 <c>WinRT.ObjectReference</c>，终结器线程满载、线程池饥饿，
/// 线程数涨到 1500+、峰值吃 3.8 个核。
/// </para>
///
/// <para>
/// 判据（只在"本来有、现在没有"时才怀疑）：上一轮看到过浏览器会话，这一轮连续
/// <see cref="LostPollsBeforeRebuild"/> 轮看不到，而且确实有浏览器进程在跑。
/// "从来没播过"和"浏览器都没开"都不算可疑 —— 这两种情况本来就没有会话。
/// 另外**一次丢失事件只重建一次**：重建后若仍读不到（常见于视频刚好播完），不会连环重建；
/// 真正的兜底是管理器自身的最大存活时间（见 <c>Smtc.ManagerMaxAge</c>）。
/// </para>
/// </summary>
internal sealed class ManagerStalenessTracker
{
    /// <summary>连续多少轮"本来有、现在没有"才判定假死。太小会频繁重建，太大会漏判。</summary>
    internal const int LostPollsBeforeRebuild = 2;

    private bool _sawBrowserSession;
    private int _lostPolls;

    /// <summary>当前连续丢失的轮数（诊断 / 测试用）。</summary>
    internal int LostPolls => _lostPolls;

    /// <summary>是否曾经看到过浏览器会话（诊断 / 测试用）。</summary>
    internal bool SawBrowserSession => _sawBrowserSession;

    /// <summary>
    /// 记录一轮观测结果。
    /// </summary>
    /// <param name="hasBrowserSession">这一轮的系统会话里有没有被认作浏览器的。</param>
    /// <param name="browserRunning">有没有已知浏览器进程在运行（没有的话，"没会话"完全正常）。</param>
    /// <returns>true = 应丢弃缓存的会话管理器，下一轮重建。</returns>
    internal bool Note(bool hasBrowserSession, bool browserRunning)
    {
        if (hasBrowserSession)
        {
            _sawBrowserSession = true;
            _lostPolls = 0;
            return false;
        }

        if (!_sawBrowserSession || !browserRunning) return false;

        if (++_lostPolls < LostPollsBeforeRebuild) return false;

        // 判定假死：重建一次，状态清零（重建后若还读不到，下一轮重新计数，不会连环重建）
        _lostPolls = 0;
        _sawBrowserSession = false;
        return true;
    }
}
