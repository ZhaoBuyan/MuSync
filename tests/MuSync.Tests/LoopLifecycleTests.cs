using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class LoopLifecycleTests
{
    [Fact]
    public void ActivationListenerUsesDedicatedThreadAndExitsOnCancellation()
    {
        using var signal = new AutoResetEvent(false);
        using var stop = new CancellationTokenSource();
        var listener = Program.StartActivationListener(signal, stop.Token);
        try
        {
            Assert.False(listener.IsThreadPoolThread);
            stop.Cancel();
            Assert.True(listener.Join(3000));
        }
        finally { stop.Cancel(); listener.Join(3000); }
    }

    private sealed class ControlledManager() : RpcManager(null!)
    {
        public readonly ConcurrentQueue<(CancellationToken Token, TaskCompletionSource Done)> Runs = new();
        protected override Task RunLoopAsync(CancellationToken token, int generation)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Runs.Enqueue((token, done));
            return done.Task;
        }
        public void Complete() { foreach (var run in Runs) run.Done.TrySetResult(); }
    }

    [Fact]
    public void RepeatedStartKeepsOneLoopAndCancelCannotBeUndoneByRestart()
    {
        var manager = new ControlledManager();
        using var stop = new CancellationTokenSource();
        try
        {
            Parallel.For(0, 16, _ => manager.Start(stop.Token));
            Assert.True(SpinWait.SpinUntil(() => manager.Runs.Count == 1, 3000));
            stop.Cancel();
            manager.RestartLoop(TimeSpan.FromMinutes(1));
            manager.Start();
            Assert.Single(manager.Runs);
            Assert.True(manager.Runs.Single().Token.IsCancellationRequested);
        }
        finally { manager.Stop(); manager.Complete(); }
    }

    [Fact]
    public void StuckOldLoopLimitsRecoveryToOneReplacement()
    {
        var manager = new ControlledManager();
        try
        {
            manager.Start();
            Assert.True(SpinWait.SpinUntil(() => manager.Runs.Count == 1, 3000));
            manager.RestartLoop(TimeSpan.FromMinutes(1));
            Assert.True(SpinWait.SpinUntil(() => manager.Runs.Count == 2, 3000));
            for (var i = 0; i < 20; i++) manager.RestartLoop(TimeSpan.FromMinutes(1));
            Assert.Equal(2, manager.Runs.Count);
            Assert.True(manager.Runs.First().Token.IsCancellationRequested);
            Assert.False(manager.Runs.Last().Token.IsCancellationRequested);
            manager.Stop();
            Assert.True(manager.Runs.Last().Token.IsCancellationRequested);
        }
        finally { manager.Stop(); manager.Complete(); }
    }

    [Fact]
    public void AlreadyCancelledStartDoesNotLaunchAWorker()
    {
        var manager = new ControlledManager();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        manager.Start(stop.Token);
        Assert.Empty(manager.Runs);
        manager.Stop();
    }
}
