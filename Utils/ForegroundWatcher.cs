using System;
using System.Diagnostics;
using MuSync.Win32Api;
namespace MuSync.Utils;

/// <summary>当前前台程序快照。</summary>
internal sealed class ForegroundAppInfo
{
    public int ProcessId { get; init; }
    /// <summary>可执行文件名（含 .exe 后缀，如 "strinova.exe"）。</summary>
    public string ExeName { get; init; } = "";
    public string WindowTitle { get; init; } = "";
    /// <summary>可执行文件完整路径（可能因权限获取失败而为空）。</summary>
    public string ExePath { get; init; } = "";
    /// <summary>窗口是否覆盖所在显示器全屏（全屏无边框常为游戏）。</summary>
    public bool IsFullscreen { get; init; }
}

/// <summary>
/// 前台程序检测器：每秒级轮询开销极小（GetForegroundWindow 为微秒级），
/// 仅在窗口切换时查询进程名；同一窗口 2 秒内直接返回缓存。
/// 只读取进程名与窗口标题，不读内存、不注入。
/// </summary>
internal static class ForegroundWatcher
{
    private static readonly object SyncRoot = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);
    private static IntPtr _lastHwnd;
    private static ForegroundAppInfo? _lastInfo;
    private static DateTime _lastQueryUtc = DateTime.MinValue;

    public static ForegroundAppInfo? GetCurrent()
    {
        lock (SyncRoot)
        {
            var hwnd = User32.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var now = DateTime.UtcNow;
            if (hwnd == _lastHwnd && _lastInfo != null && now - _lastQueryUtc < CacheDuration)
            {
                return _lastInfo;
            }
            var info = Query(hwnd);
            _lastHwnd = hwnd;
            _lastInfo = info;
            _lastQueryUtc = now;
            return info;
        }
    }

    private static ForegroundAppInfo? Query(IntPtr hwnd)
    {
        if (User32.GetWindowThreadProcessId(hwnd, out var pid) == 0 || pid <= 0) return null;
        string exeName;
        var exePath = "";
        try
        {
            using var process = Process.GetProcessById(pid);
            exeName = process.ProcessName + ".exe";
            try
            {
                exePath = process.MainModule?.FileName ?? "";
            }
            catch
            {
                // 提权/受保护进程可能读不到路径，不影响进程名使用
                exePath = "";
            }
        }
        catch
        {
            // 受保护/系统进程无法打开：视为不可识别
            return null;
        }
        var title = User32.GetWindowTitle(hwnd);
        return new ForegroundAppInfo
        {
            ProcessId = pid,
            ExeName = exeName,
            WindowTitle = title,
            ExePath = exePath,
            IsFullscreen = IsFullscreen(hwnd)
        };
    }

    private static bool IsFullscreen(IntPtr hwnd)
    {
        if (!User32.TryGetWindowBounds(hwnd, out var bounds)) return false;
        try
        {
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
            if (screen.IsEmpty) return false;
            // 与"含任务栏的整屏"完全一致才判定全屏（最大化窗口通常只有工作区大小，不会误判）
            return bounds.Width >= screen.Width && bounds.Height >= screen.Height;
        }
        catch
        {
            return false;
        }
    }
}
