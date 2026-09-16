using System;
using System.Diagnostics;
using MuSync.Win32Api;
namespace MuSync.Utils;

/// <summary>性能信息采集（供设置面板的"性能监控"区域展示）。</summary>
internal static class PerformanceMonitor
{
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();

    public static ProcessMemoryInfo GetMemoryInfo()
    {
        try
        {
            var workingSet = CurrentProcess.WorkingSet64;
            var privateMemory = CurrentProcess.PrivateMemorySize64;
            var virtualMemory = CurrentProcess.VirtualMemorySize64;
            var gcMemory = GC.GetTotalMemory(false);
            return new ProcessMemoryInfo
            {
                WorkingSetSize = workingSet,
                PrivateMemorySize = privateMemory,
                VirtualMemorySize = virtualMemory,
                GcMemorySize = gcMemory,
                Timestamp = DateTime.Now
            };
        }
        catch
        {
            return new ProcessMemoryInfo();
        }
    }

    public static CacheStatistics GetCacheStatistics()
    {
        return new CacheStatistics
        {
            ImageCacheCount = ImageCacheManager.CacheCount,
            ModuleCacheCount = Memory.ModuleCacheCount,
            ProcessModuleCacheCount = ProcessUtils.ModuleAddressCacheCount
        };
    }
}

/// <summary>进程内存快照（工作集 / 私有内存 / 虚拟内存 / GC 内存，带格式化显示）。</summary>
internal record ProcessMemoryInfo
{
    public long WorkingSetSize { get; init; }
    public long PrivateMemorySize { get; init; }
    public long VirtualMemorySize { get; init; }
    public long GcMemorySize { get; init; }
    public DateTime Timestamp { get; init; }

    public string GetFormattedWorkingSet() => FormatBytes(WorkingSetSize);
    public string GetFormattedPrivateMemory() => FormatBytes(PrivateMemorySize);
    public string GetFormattedVirtualMemory() => FormatBytes(VirtualMemorySize);
    public string GetFormattedGcMemory() => FormatBytes(GcMemorySize);

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

/// <summary>各缓存数量统计（图片 / 模块 / 进程模块）。</summary>
internal record CacheStatistics
{
    public int ImageCacheCount { get; init; }
    public int ModuleCacheCount { get; init; }
    public int ProcessModuleCacheCount { get; init; }
    public int TotalCacheCount => ImageCacheCount + ModuleCacheCount + ProcessModuleCacheCount;
}
