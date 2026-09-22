using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>卡死兜底 · 节拍器测试：内核等待（到点返回 / 被唤醒提前返回 / 取消退出），全内 30 秒不拖长。</summary>
public class TickPacerTests
{
    [Fact]
    public void Wait_NotWoken_ReturnsFalseAfterDelay()
    {
        var pacer = new TickPacer();
        var sw = Stopwatch.StartNew();
        var woken = pacer.Wait(TimeSpan.FromMilliseconds(120), CancellationToken.None);
        sw.Stop();
        Assert.False(woken);
        Assert.True(sw.ElapsedMilliseconds >= 80, $"等待应至少到点，实际 {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 3000, $"等待不应被拖长，实际 {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Wait_WokenWhileWaiting_ReturnsTrueEarly()
    {
        var pacer = new TickPacer();
        var wakeTask = Task.Run(() =>
        {
            Thread.Sleep(80);
            pacer.Wake();
        });
        var sw = Stopwatch.StartNew();
        var woken = pacer.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
        sw.Stop();
        await wakeTask;
        Assert.True(woken);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"应被提前唤醒，实际 {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Wait_WakeBeforeWait_TakesEffectImmediately()
    {
        // 设置变化 / 重连正好发生在两拍之间：唤醒信号不许丢，下一拍立刻开始
        var pacer = new TickPacer();
        pacer.Wake();
        var sw = Stopwatch.StartNew();
        var woken = pacer.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
        sw.Stop();
        Assert.True(woken);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"唤醒信号应立刻生效，实际 {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Wait_AlreadyCanceled_Throws()
    {
        var pacer = new TickPacer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => pacer.Wait(TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task Wait_CanceledWhileWaiting_Throws()
    {
        var pacer = new TickPacer();
        using var cts = new CancellationTokenSource();
        var cancelTask = Task.Run(() =>
        {
            Thread.Sleep(80);
            cts.Cancel();
        });
        var sw = Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() => pacer.Wait(TimeSpan.FromSeconds(30), cts.Token));
        sw.Stop();
        await cancelTask;
        Assert.True(sw.ElapsedMilliseconds < 5000, $"取消应立即生效，实际 {sw.ElapsedMilliseconds} ms");
    }
}
