using System;
using System.Threading;
namespace MuSync.Utils;
/// <summary>
/// 主循环的节拍器：内核等待（WaitHandle）＋提前唤醒，完全不经过 .NET 定时器队列。
/// 背景：2026-09-21 的转储显示主循环会卡在 `Task.Delay` 上（定时器回调整个丢失、循环再也不动），
/// 节拍改成内核等待后不再依赖那条通路；设置变化 / 重连 / 看门狗复位都能立刻唤醒，不必等满一拍。
/// </summary>
internal sealed class TickPacer
{
    /// <summary>内核事件：Wake() 之后正在等待的（或下一次）等待立刻返回（AutoReset 由系统自动复位）。</summary>
    private readonly AutoResetEvent _signal = new(false);
    /// <summary>提前唤醒：让正在等待（或下一次）的等待立刻返回。</summary>
    public void Wake()
    {
        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // 退出竞态：忽略
        }
    }
    /// <summary>
    /// 等待一拍（内核等待）：
    /// 到点返回 false；被 Wake() 唤醒返回 true；token 取消则抛 OperationCanceledException。
    /// </summary>
    public bool Wait(TimeSpan delay, CancellationToken token)
    {
        if (token.IsCancellationRequested) throw new OperationCanceledException(token);
        WaitHandle[] handles = token.CanBeCanceled ? [_signal, token.WaitHandle] : [_signal];
        var index = WaitHandle.WaitAny(handles, delay);
        if (index == 0) return true;
        if (index == WaitHandle.WaitTimeout) return false;
        throw new OperationCanceledException(token);
    }
}
