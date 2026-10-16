using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace MuSync.Utils;
/// <summary>封面图片 LRU 缓存：容量上限 / 内存压力联动清理 / 活动键保护。</summary>
internal static class ImageCacheManager
{
    private static readonly Dictionary<string, LinkedListNode<CacheItem>> Cache = new();
    private static readonly LinkedList<CacheItem> LruList = [];
    private static readonly Lock LockObject = new();
    /// <summary>在途下载任务（键 → 任务）：同一张图的并发请求只下载一次，避免任务越堆越多。</summary>
    private static readonly Dictionary<string, Task<Image?>> InFlight = new();
    private static readonly Lock InFlightLock = new();
    private static readonly HashSet<string> ActiveKeys = [];
    internal static int CacheCount
    {
        get
        {
            lock (LockObject)
            {
                return Cache.Count;
            }
        }
    }
    /// <summary>
    /// 已经写过诊断日志的封面键（只用于「每个键只记一次」的日志去重，DEBUG 构建才有意义）。
    /// <para>
    /// ⚠️ 这里**必须**有上限：图片缓存本身有 LRU 上限，但这个集合原先没上限，
    /// 而唯一清理它的 <c>ClearAllCache</c> 在主项目里没有任何调用方 →
    /// 长时间使用（尤其单曲封面 URL 带歌曲 ID 时）它会随歌曲数无界增长。
    /// 清空只意味着某些键会再记一次日志，对功能零影响。
    /// </para>
    /// </summary>
    private static readonly HashSet<string> LoggedCacheKeys = [];
    private const int LoggedKeysMaxSize = 2000;
    /// <summary>缓存条目（键 / 图片 / 最近访问时间）。</summary>
    private class CacheItem(string key, Image image)
    {
        public string Key { get; } = key;
        public Image Image { get; } = image;
        public DateTime LastAccessed { get; set; } = DateTime.Now;
    }
    public static void SetActiveKeys(IEnumerable<string> keys)
    {
        lock (LockObject)
        {
            ActiveKeys.Clear();
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    ActiveKeys.Add(key);
                }
            }
        }
    }
    private static bool IsValidImageData(byte[] data)
    {
        if (data.Length < 8)
        {
            return false;
        }
        if (data is [0xFF, 0xD8, ..])
        {
            return true;
        }
        switch (data.Length)
        {
            case >= 8 when
                data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
                data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A:
            case >= 6 when
                (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) && 
                (data[4] == 0x37 || data[4] == 0x39) && 
                data[5] == 0x61:
            case >= 2 when data[0] == 0x42 && data[1] == 0x4D:
            case >= 12 when
                data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 && 
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50:
                return true;
            default:
                return false;
        }
    }
    public static Task<Image?> LoadImageAsync(string cacheKey, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(cacheKey)) return Task.FromResult<Image?>(null);
        lock (InFlightLock)
        {
            if (InFlight.TryGetValue(cacheKey, out var running)) return running;
        }
        var task = LoadImageWithTimeoutAsync(cacheKey, imageUrl);
        lock (InFlightLock)
        {
            InFlight[cacheKey] = task;
        }
        // 成功 / 失败 / 超时都要清掉在途记录，否则同一张图的下一次请求会一直拿到旧结果
        _ = task.ContinueWith(_ => ClearInFlight(cacheKey, task), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }
    /// <summary>下载总时长兜底：整张图（含重试等待）超过 ImageDownloadTimeout 就放弃本次，返回 null。</summary>
    private static async Task<Image?> LoadImageWithTimeoutAsync(string cacheKey, string imageUrl)
    {
        try
        {
            return await LoadImageCoreAsync(cacheKey, imageUrl)
                .WaitAsync(StabilityConfig.ImageDownloadTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Logger.Warn($"[Image] 封面下载超时（{StabilityConfig.ImageDownloadTimeout.TotalSeconds:F0} 秒），已放弃: {cacheKey}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Image] 封面下载异常: {ex.Message}");
            return null;
        }
    }
    /// <summary>清掉在途下载记录（仅当仍是同一个任务，避免误删后来的请求）。</summary>
    private static void ClearInFlight(string cacheKey, Task<Image?> task)
    {
        lock (InFlightLock)
        {
            if (InFlight.TryGetValue(cacheKey, out var current) && ReferenceEquals(current, task))
            {
                InFlight.Remove(cacheKey);
            }
        }
    }
    private static async Task<Image?> LoadImageCoreAsync(string cacheKey, string imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(cacheKey))
        {
            return null;
        }
        Image? cachedImage = null;
        lock (LockObject)
        {
            if (Cache.TryGetValue(cacheKey, out var node))
            {
                LruList.Remove(node);
                LruList.AddFirst(node);
                node.Value.LastAccessed = DateTime.Now;
                cachedImage = node.Value.Image;
            }
        }
        var shouldLog = false;
        lock (LoggedCacheKeys)
        {
            if (!LoggedCacheKeys.Contains(cacheKey))
            {
                shouldLog = true;
                // 到上限就整体清掉：这只是日志去重集合，清空最多让若干键重记一条诊断，不影响功能
                if (LoggedCacheKeys.Count >= LoggedKeysMaxSize)
                {
                    LoggedCacheKeys.Clear();
                }
                LoggedCacheKeys.Add(cacheKey);
            }
        }
        if (shouldLog)
        {
            Logger.Diagnose($"ImageCacheManager received request for key: {cacheKey}");
        }
        if (cachedImage != null)
        {
            if (shouldLog)
            {
                Logger.Diagnose($"CACHE HIT for key: {cacheKey}");
            }
            return cachedImage;
        }
        Logger.Diagnose($"CACHE MISS for key: {cacheKey}. Downloading from URL.");
        const int maxRetries = 3;
        byte[]? downloadedBytes = null;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            var retryDelay = 100 * (1 << retry);
            try
            {
                var httpClient = HttpClientManager.SharedClient;
                using var response = await httpClient.GetAsync(imageUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Diagnose(
                        $"Image download failed for key {cacheKey}: HTTP Status {response.StatusCode} (Retry {retry + 1}/{maxRetries})");
                    return null;
                }
                var contentType = response.Content.Headers.ContentType?.MediaType;
                if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/"))
                {
                    Logger.Diagnose(
                        $"Downloaded content is not an image for key {cacheKey}. Content-Type: {contentType ?? "N/A"} (Retry {retry + 1}/{maxRetries})");
                    return null; 
                }
                await using (var httpStream = await response.Content.ReadAsStreamAsync())
                {
                    var contentLength = response.Content.Headers.ContentLength ?? 1024 * 1024; 
                    var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(contentLength, 10 * 1024 * 1024)); 
                    try
                    {
                        var totalRead = 0;
                        int bytesRead;
                        while ((bytesRead =
                                   await httpStream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead))) >
                               0)
                        {
                            totalRead += bytesRead;
                            if (totalRead != buffer.Length) continue;
                            var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                            Array.Copy(buffer, 0, newBuffer, 0, totalRead);
                            ArrayPool<byte>.Shared.Return(buffer);
                            buffer = newBuffer;
                        }
                        downloadedBytes = new byte[totalRead];
                        Array.Copy(buffer, 0, downloadedBytes, 0, totalRead);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
                if (downloadedBytes is not { Length: > 0 })
                {
                    Logger.Diagnose(
                        $"Downloaded content is empty or invalid for key: {cacheKey}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                if (!IsValidImageData(downloadedBytes))
                {
                    var headerHex = downloadedBytes.Length >= 16
                        ? BitConverter.ToString(downloadedBytes, 0, 16).Replace("-", " ")
                        : BitConverter.ToString(downloadedBytes).Replace("-", " ");
                    Logger.Diagnose(
                        $"Downloaded content is not a valid image format for key: {cacheKey}. Data length: {downloadedBytes.Length}. Header: {headerHex}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                Image image;
                try
                {
                    var ms = new MemoryStream(downloadedBytes);
                    image = Image.FromStream(ms);
                    Logger.Diagnose($"Downloaded and created new image for key: {cacheKey}. Caching it.");
                }
                catch (ArgumentException ex)
                {
                    Logger.Diagnose(
                        $"ArgumentException during Image.FromStream for key {cacheKey}. Data length: {downloadedBytes.Length}. Error: {ex.Message}. (Retry {retry + 1}/{maxRetries})");
                    if (retry >= maxRetries - 1) return null;
                    await Task.Delay(retryDelay);
                    continue;
                }
                var imageSize = MemoryPressureMonitor.EstimateImageSize(image);
                lock (LockObject)
                {
                    if (MemoryPressureMonitor.ShouldProactivelyCleanup())
                    {
                        var cleanupCount = MemoryPressureMonitor.GetSuggestedCleanupCount();
                        var itemsToCleanup = Math.Min(cleanupCount, LruList.Count);
                        for (var i = 0; i < itemsToCleanup && LruList.Last != null; i++)
                        {
                            var evictedItem = LruList.Last.Value;
                            // ⚠️ 不要在这里再 UnregisterImageSize：RemoveFromCacheInternal 内部已经扣减过，
                            // 重复扣减会让追踪值越来越低于真实占用，最终内存压力阈值再也触发不了（实测过的记账缺陷）。
                            // 返回 false 表示该项被 ActiveKeys 挡住（正在显示）而未能移除 —— 必须跳出，否则死循环。
                            if (!RemoveFromCacheInternal(evictedItem.Key)) break;
                            Logger.Diagnose($"Proactive cleanup evicted item with key: {evictedItem.Key}");
                        }
                    }
                    while (MemoryPressureMonitor.ShouldEvictItems(imageSize) && LruList.Last != null)
                    {
                        var evictedItem = LruList.Last.Value;
                        // 同第一个循环：不重复扣减计数，且被 ActiveKeys 挡住时跳出
                        if (!RemoveFromCacheInternal(evictedItem.Key)) break;
                        Logger.Diagnose($"Memory pressure evicted item with key: {evictedItem.Key}");
                    }
                    // 硬上限：内存阈值只管"占了多少字节"，条数无上限时大量小图仍会堆积
                    // （PerformanceConfig.ImageCacheMaxSize 此前在主项目里没有任何引用，等于没有这道闸）
                    var maxItems = PerformanceConfig.ImageCacheMaxSize;
                    while (maxItems > 0 && LruList.Count > maxItems && LruList.Last != null)
                    {
                        if (!RemoveFromCacheInternal(LruList.Last.Value.Key)) break;
                    }
                    var newItem = new CacheItem(cacheKey, image);
                    var newNode = new LinkedListNode<CacheItem>(newItem);
                    LruList.AddFirst(newNode);
                    Cache[cacheKey] = newNode;
                    MemoryPressureMonitor.RegisterImageSize(imageSize);
                    Logger.Diagnose(
                        $"Cached new image with key: {cacheKey}, size: {imageSize} bytes, total cache memory: {MemoryPressureMonitor.GetCurrentMemoryUsage()} bytes");
                }
                return image;
            }
            catch (ArgumentException ex)
            {
                var dataLength = downloadedBytes?.Length.ToString() ?? "N/A";
                Logger.Diagnose(
                    $"ArgumentException during image creation for key {cacheKey}. Image data length: {dataLength}. Error: {ex.Message}. Attempt {retry + 1}/{maxRetries}.");
                if (retry < maxRetries - 1)
                {
                    await Task.Delay(retryDelay);
                }
            }
            catch (Exception ex)
            {
                var dataLength = downloadedBytes?.Length.ToString() ?? "N/A";
                Logger.Diagnose(
                    $"General Exception during image download/creation for key {cacheKey}. Image data length: {dataLength}. Error: {ex.Message}.");
                return null;
            }
        }
        Logger.Diagnose($"Failed to download and create image for key {cacheKey} after {maxRetries} attempts.");
        return null;
    }
    /// <summary>
    /// 手动清理缓存（主界面 Ctrl+R 调试快捷键调用）：只保留最近使用的若干项。
    /// 说明：原先还有 ClearAllCache / PerformCleanup / PerformAggressiveCleanup / RemoveCacheByPrefix
    /// 四个清理入口，但主项目里一个调用方都没有（只有已废弃的 reference-yySync 在用），
    /// 而其中 ClearAllCache 是 LoggedCacheKeys 唯一的清理点 —— 留着它们等于留着"看起来会清理、实际不会"的假象，故一并删除。
    /// </summary>
    public static void ForceCleanupCache(int keepCount = 10)
    {
        lock (LockObject)
        {
            if (Cache.Count <= keepCount) return;
            var itemsToRemove = Cache.Count - keepCount;
            var keysToRemove = new List<string>();
            var sortedByTime = Cache.OrderBy(kvp => kvp.Value.Value.LastAccessed).Take(itemsToRemove);
            keysToRemove.AddRange(sortedByTime.Select(kvp => kvp.Key));
            foreach (var key in keysToRemove)
            {
                RemoveFromCacheInternal(key);
            }
            Logger.Diagnose(
                $"Force cleanup removed {keysToRemove.Count} cache items, kept {keepCount} most recent items.");
        }
    }
    /// <summary>移除一个缓存项。返回 false 表示未移除（不存在，或正在显示被 ActiveKeys 挡住）。</summary>
    private static bool RemoveFromCacheInternal(string cacheKey)
    {
        if (!Cache.TryGetValue(cacheKey, out var node)) return false;
        if (ActiveKeys.Contains(cacheKey))
        {
            // 正在显示的封面不能移除，否则 PictureBox 会引用到已释放的位图
            return false;
        }
        LruList.Remove(node);
        Cache.Remove(cacheKey);
        lock (LoggedCacheKeys)
        {
            LoggedCacheKeys.Remove(cacheKey);
        }
        var imageSize = MemoryPressureMonitor.EstimateImageSize(node.Value.Image);
        MemoryPressureMonitor.UnregisterImageSize(imageSize);
        try
        {
            node.Value.Image.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Diagnose($"[WARNING] Failed to dispose image for key {cacheKey}: {ex.Message}");
        }
        return true;
    }
}