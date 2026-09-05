using System;
using System.Diagnostics;
using System.IO;
namespace MuSync.Utils;

/// <summary>
/// 分级日志：Info/Warn/Error 写入 %LocalAppData%\MuSync\logs\ 下的按天日志文件（Release 也生效），
/// 并同步输出到调试器；Debug/Diagnose/Memory 仅 DEBUG 构建生效，供高频细节排查。
/// </summary>
internal static class Logger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MuSync", "logs");
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
