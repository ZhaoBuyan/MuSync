using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
namespace MuSync.Utils;

/// <summary>
/// 分级日志：Info/Warn/Error 写入 %LocalAppData%\MuSync\logs\ 下的按天日志文件（Release 也生效），
/// 并同步输出到调试器；Debug/Diagnose/Memory 仅 DEBUG 构建生效，供高频细节排查。
/// </summary>
internal static class Logger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = ResolveLogDirectory();
    private static string _currentDate = "";
    private static string? _currentFilePath;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    [Conditional("DEBUG")]
    public static void Debug(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
    }

    [Conditional("DEBUG")]
    public static void Diagnose(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[DIAGNOSE] {message}");
    }

    [Conditional("DEBUG")]
    public static void Memory(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[MEMORY] {message}");
    }

    /// <summary>日志目录：支持 MUSYNC_LOG_DIR 环境变量覆盖（测试隔离用），默认 %LocalAppData%\MuSync\logs。</summary>
    private static string ResolveLogDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("MUSYNC_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuSync", "logs");
    }

    /// <summary>清理旧日志：保留最近 14 天且在 50 MB 总量内（从最旧的开始删）。</summary>
    public static void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var files = new DirectoryInfo(LogDirectory)
                .GetFiles("MuSync-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            const int keepDays = 14;
            const long maxTotalBytes = 50L * 1024 * 1024;
            long total = 0;
            var cutoff = DateTime.UtcNow.AddDays(-keepDays);
            foreach (var file in files)
            {
                total += file.Length;
                if (file.LastWriteTimeUtc < cutoff || total > maxTotalBytes)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // 单个文件删除失败时忽略
                    }
                }
            }
        }
        catch
        {
            // 清理失败绝不影响主流程
        }
    }

    /// <summary>把内存快照写入日志（低频调用，用于排查内存增长趋势）。</summary>
    public static void MemorySnapshot()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / 1024.0 / 1024.0;
            var privateBytes = process.PrivateMemorySize64 / 1024.0 / 1024.0;
            var gcHeap = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            Info($"[Memory] 工作集 {workingSet:F1} MB | 私有 {privateBytes:F1} MB | GC 堆 {gcHeap:F1} MB");
        }
        catch
        {
            // 忽略
        }
    }

    private static void Write(string level, string message)
    {
        // 调试器输出（DEBUG 构建可见，Release 下该调用被剥离）
        System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        try
        {
            lock (SyncRoot)
            {
                var now = DateTime.Now;
                var date = now.ToString("yyyyMMdd");
                if (date != _currentDate)
                {
                    _currentDate = date;
                    _currentFilePath = null;
                }
                _currentFilePath ??= Path.Combine(LogDirectory, $"MuSync-{date}.log");
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(_currentFilePath,
                    $"{now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败绝不影响主流程
        }
    }
}
