using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace MuSync.Utils;

/// <summary>
/// 更新包下载器（手动替换模式）：
/// 下载到 %LocalAppData%\MuSync\updates\，先写 .part 临时文件，下载完成且大小校验通过后才转为正式包。
/// 安全约定：任何失败/取消只清理自己的临时文件，绝不触碰其他文件；旧版本包仅在本次下载成功后清理。
/// </summary>
internal static class UpdateDownloader
{
    public enum DownloadStatus
    {
        Completed,
        Canceled,
        Failed
    }

    public sealed class DownloadResult
    {
        public DownloadStatus Status { get; init; }
        public string FilePath { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public readonly record struct DownloadProgress(long Received, long Total);

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(30);

    /// <summary>复用的下载 HTTP 客户端（避免每次下载新建连接池）。</summary>
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // 让连接定期重建，避免长生命周期客户端缓存 DNS 的问题
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var client = new HttpClient(handler) { Timeout = TransferTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MuSync-UpdateCheck");
        return client;
    }

    /// <summary>更新包存放目录：%LocalAppData%\MuSync\updates。</summary>
    public static string GetUpdatesDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuSync",
            "updates");

    /// <summary>更新包本地路径，如 MuSync-v0.2.1.exe / MuSync-lite-v0.2.1.exe。</summary>
    public static string GetPackagePath(UpdateChecker.UpdateAsset asset, string version, string? directory = null)
    {
        var baseName = Path.GetFileNameWithoutExtension(asset.Name);
        if (baseName.Length == 0) baseName = "MuSync";
        return Path.Combine(directory ?? GetUpdatesDirectory(), $"{baseName}-v{version}.exe");
    }

    /// <summary>该更新包是否已下载且大小匹配（避免重复下载）。</summary>
    public static bool IsAlreadyDownloaded(
        UpdateChecker.UpdateAsset asset, string version, out string path, string? directory = null)
    {
        path = GetPackagePath(asset, version, directory);
        return File.Exists(path) && FileSizesMatch(path, asset.Size);
    }

    public static async Task<DownloadResult> DownloadAsync(
        UpdateChecker.UpdateAsset asset,
        string version,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        string? directory = null)
    {
        var finalPath = GetPackagePath(asset, version, directory);
        var partPath = finalPath + ".part";
        try
        {
            if (File.Exists(finalPath) && FileSizesMatch(finalPath, asset.Size))
            {
                Logger.Info($"[Update] 更新包已存在，跳过下载: {finalPath}");
                return new DownloadResult { Status = DownloadStatus.Completed, FilePath = finalPath };
            }
            var targetDir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            // 清掉自己上次中断留下的临时文件（只清这一个）
            TryDelete(partPath);

            using var response = await Http
                .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = asset.Size > 0 ? asset.Size : response.Content.Headers.ContentLength ?? 0;

            var sizeMismatch = false;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                             partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastReport = DateTime.UtcNow;
                progress?.Report(new DownloadProgress(0, total));
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    if (read <= 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    var now = DateTime.UtcNow;
                    if (now - lastReport >= ReportInterval)
                    {
                        lastReport = now;
                        progress?.Report(new DownloadProgress(received, total));
                    }
                }
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                progress?.Report(new DownloadProgress(received, total));
                sizeMismatch = total > 0 && received != total;
            }

            if (sizeMismatch)
            {
                TryDelete(partPath);
                Logger.Warn("[Update] 下载文件大小不匹配，已丢弃临时文件");
                return new DownloadResult
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = Loc.L("下载文件不完整，请重试", "The downloaded file is incomplete — please try again")
                };
            }

            // 完整性校验：优先用 GitHub 提供的 SHA256 digest（拿不到时仅依赖大小校验）
            if (!VerifySha256(partPath, asset.Digest))
            {
                TryDelete(partPath);
                Logger.Warn("[Update] 更新包 SHA256 校验不通过，已丢弃（传输损坏或被篡改）");
                return new DownloadResult
                {
                    Status = DownloadStatus.Failed,
                    ErrorMessage = Loc.L("文件校验不通过，请重试", "File verification failed — please try again")
                };
            }

            File.Move(partPath, finalPath, overwrite: true);
            Logger.Info($"[Update] 更新包下载完成: {finalPath}");
            var cleanupDir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(cleanupDir) && Version.TryParse(version, out var parsedVersion))
            {
                CleanupOldPackages(cleanupDir, finalPath, UpdateChecker.NormalizeVersion(parsedVersion));
            }
            return new DownloadResult { Status = DownloadStatus.Completed, FilePath = finalPath };
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            Logger.Info("[Update] 下载已取消，临时文件已清理");
            return new DownloadResult { Status = DownloadStatus.Canceled };
        }
        catch (Exception ex)
        {
            TryDelete(partPath);
            Logger.Error($"[Update] 下载失败: {ex.Message}");
            return new DownloadResult { Status = DownloadStatus.Failed, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// 清理目录中比指定版本更旧的已下载更新包（仅在本次下载成功后调用）。
    /// 只删除符合「名字-v版本号.exe」命名规则的文件，其余文件一律不动；删除失败仅记日志。
    /// </summary>
    internal static void CleanupOldPackages(string directory, string keepFilePath, Version currentVersion)
    {
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe"))
            {
                if (string.Equals(file, keepFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryParsePackageVersion(Path.GetFileName(file), out var version)) continue;
                if (UpdateChecker.NormalizeVersion(version) >= currentVersion) continue;
                try
                {
                    File.Delete(file);
                    Logger.Info($"[Update] 已清理旧版本更新包: {file}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[Update] 清理旧更新包失败（忽略）: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 扫描更新包目录失败（忽略）: {ex.Message}");
        }
    }

    /// <summary>从更新包文件名解析版本号，如 MuSync-v0.2.1.exe / MuSync-lite-v0.2.1.exe → 0.2.1。</summary>
    internal static bool TryParsePackageVersion(string fileName, out Version version)
    {
        version = new Version(0, 0);
        const string extension = ".exe";
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
        var body = fileName[..^extension.Length];
        var marker = body.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || marker + 2 >= body.Length) return false;
        if (!Version.TryParse(body[(marker + 2)..], out var parsed)) return false;
        version = parsed;
        return true;
    }

    /// <summary>
    /// 校验文件 SHA256 是否与 GitHub 提供的 digest 一致（格式 "sha256:..."）。
    /// digest 为空或无法识别时返回 true（跳过校验，由大小校验兜底）。
    /// </summary>
    private static bool VerifySha256(string path, string digest)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrEmpty(digest) || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        try
        {
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            var expected = digest[prefix.Length..].Trim();
            var match = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                Logger.Warn($"[Update] SHA256 不一致：期望 {expected}，实际 {actual}");
            }
            return match;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] SHA256 校验出错（忽略校验）: {ex.Message}");
            return true;
        }
    }

    private static bool FileSizesMatch(string path, long expected)
    {
        if (expected <= 0) return true;
        try
        {
            return new FileInfo(path).Length == expected;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 删除临时文件失败（忽略）: {ex.Message}");
        }
    }
}
