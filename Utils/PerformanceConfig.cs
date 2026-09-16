using System;
namespace MuSync.Utils;
/// <summary>性能参数集中配置（各类缓存容量 / 清理间隔 / HTTP 超时）。</summary>
internal static class PerformanceConfig
{
    public static int ImageCacheMaxSize { get; } = 20;
    public static int ModuleCacheMaxSize { get; } = 10;
    public static TimeSpan ModuleCacheCleanupInterval { get; } = TimeSpan.FromMinutes(5);
    public static int ProcessModuleCacheMaxSize { get; } = 50;
    public static TimeSpan ProcessModuleCacheCleanupInterval { get; } = TimeSpan.FromMinutes(5);
    public static TimeSpan HttpClientTimeout { get; } = TimeSpan.FromSeconds(5);
}