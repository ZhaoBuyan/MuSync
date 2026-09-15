using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
namespace MuSync.Utils;

/// <summary>
/// 更新检查：请求 GitHub 最新 Release（版本号、更新日志、下载资产列表）。
/// 离线/限流/解析失败时静默返回 null——绝不打扰用户。
/// </summary>
internal static class UpdateChecker
{
    /// <summary>Release 中的一个可下载资产。</summary>
    public sealed class UpdateAsset
    {
        public string Name { get; init; } = "";
        public long Size { get; init; }
        public string DownloadUrl { get; init; } = "";
        /// <summary>GitHub 提供的完整性摘要（如 "sha256:..."），可能为空。</summary>
        public string Digest { get; init; } = "";
    }

    public sealed class UpdateInfo
    {
        public string Tag { get; init; } = "";
        public string Version { get; init; } = "";
        public string Body { get; init; } = "";
        public string Url { get; init; } = "";
        public IReadOnlyList<UpdateAsset> Assets { get; init; } = [];
    }

    /// <summary>复用的 HTTP 客户端（避免每次检查新建连接池）。</summary>
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // 让连接定期重建，避免长生命周期客户端缓存 DNS 的问题
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MuSync-UpdateCheck");
        return client;
    }

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await Http
                .GetStringAsync("https://api.github.com/repos/ZhaoBuyan/MuSync/releases/latest")
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
            var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var remote)) return null;
            if (NormalizeVersion(remote) <= GetCurrentVersion()) return null;
            return new UpdateInfo
            {
                Tag = tag,
                Version = versionText,
                Body = body,
                Url = url,
                Assets = ParseAssets(root)
            };
        }
        catch (Exception ex)
        {
            // 失败原因写日志（如网络不可达 / GitHub 限流），每 6 小时最多一条，不打扰用户
            Logger.Warn($"[Update] 检查更新失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>解析 Release 资产列表中的可下载文件（名称/大小/下载直链）。</summary>
    private static List<UpdateAsset> ParseAssets(JsonElement root)
    {
        var assets = new List<UpdateAsset>();
        if (!root.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            return assets;
        }
        foreach (var item in assetsElement.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
            var url = item.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
            var size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsed)
                ? parsed
                : 0;
            var digest = item.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString() ?? ""
                : "";
            if (name.Length == 0 || url.Length == 0) continue;
            assets.Add(new UpdateAsset { Name = name, Size = size, DownloadUrl = url, Digest = digest });
        }
        return assets;
    }

    /// <summary>
    /// 挑选与当前程序形态一致的更新包：优先与当前 exe 同名；
    /// 否则 lite 版程序选 lite 包、其余选完整版（重命名过的 exe 也能拿到可用的包）。
    /// </summary>
    public static UpdateAsset? SelectAsset(IReadOnlyList<UpdateAsset> assets, string? currentExeName = null)
    {
        var exeName = currentExeName ?? Path.GetFileName(Environment.ProcessPath ?? "");
        var exeAssets = assets
            .Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exeAssets.Count == 0) return null;
        var sameName = exeAssets.FirstOrDefault(
            a => string.Equals(a.Name, exeName, StringComparison.OrdinalIgnoreCase));
        if (sameName != null) return sameName;
        var wantsLite = exeName.Contains("lite", StringComparison.OrdinalIgnoreCase);
        return exeAssets.FirstOrDefault(
                   a => a.Name.Contains("lite", StringComparison.OrdinalIgnoreCase) == wantsLite)
               ?? exeAssets[0];
    }

    public static Version GetCurrentVersion() =>
        NormalizeVersion(typeof(UpdateChecker).Assembly.GetName().Version);

    public static string GetCurrentVersionText() => GetCurrentVersion().ToString(3);

    /// <summary>把缺位的 Build/Revision 归零，保证版本比较语义一致。</summary>
    internal static Version NormalizeVersion(Version? version)
    {
        if (version == null) return new Version(0, 0, 0, 0);
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }
}
