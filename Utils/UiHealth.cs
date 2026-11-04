using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace MuSync.Utils;

/// <summary>低频界面诊断；不记录控件内容、令牌或媒体名称。</summary>
internal static class UiHealth
{
    private static readonly ConcurrentDictionary<string, long> LastErrors = new();
    private static int _failures;
    private static readonly ConcurrentQueue<string> PendingNotes = new();
    private static int _drainingNotes;
    internal static int Failures => Volatile.Read(ref _failures);

    internal static bool TryRun(string operation, Action action)
    {
        try { action(); return true; }
        catch (Exception error)
        {
            Report(operation, error);
            return false;
        }
    }

    internal static void Report(string operation, Exception error)
    {
        Interlocked.Increment(ref _failures);
        var now = Environment.TickCount64;
        var last = LastErrors.GetOrAdd(operation, long.MinValue / 2);
        if (now - last >= 30000 && LastErrors.TryUpdate(operation, now, last))
            Logger.Error($"[UI] {operation} failed: {error.GetType().Name}: {error.Message}\n{error.StackTrace}");
    }

    internal static void Transition(string target, Stopwatch watch, int dpi, int width, int height)
        => Note($"[UI] view={target} elapsed={watch.Elapsed.TotalMilliseconds:F1}ms dpi={dpi} client={width}x{height} theme={Theme.Palette.Id}");

    internal static void Note(string message)
    {
        PendingNotes.Enqueue(message);
        while (PendingNotes.Count > 64) PendingNotes.TryDequeue(out _);
        DrainNotes();
    }

    private static void DrainNotes()
    {
        if (Interlocked.CompareExchange(ref _drainingNotes, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(static _ =>
        {
            try { while (PendingNotes.TryDequeue(out var message)) Logger.Info(message); }
            finally
            {
                Volatile.Write(ref _drainingNotes, 0);
                if (!PendingNotes.IsEmpty) DrainNotes();
            }
        });
    }
}
