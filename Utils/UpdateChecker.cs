using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
namespace MuSync.Utils;

/// <summary>
/// 更新检查：请求 GitHub 最新 Release（附带更新日志）。
/// 离线/限流/解析失败时静默返回 null——绝不打扰用户。
/// </summary>
internal static class UpdateChecker
{
    public sealed class UpdateInfo
    {
        public string Tag { get; init; } = "";
        public string Version { get; init; } = "";
        public string Body { get; init; } = "";
        public string Url { get; init; } = "";
    }

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MuSync-UpdateCheck");
            var json = await http
                .GetStringAsync("https://api.github.com/repos/ZhaoBuyan/MuSync/releases/latest")
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
            var body = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
            var versionText = tag.TrimStart('v', 'V');
            if (!Version.TryParse(versionText, out var remote)) return null;
            var current = Normalize(typeof(UpdateChecker).Assembly.GetName().Version);
            if (Normalize(remote) <= current) return null;
            return new UpdateInfo { Tag = tag, Version = versionText, Body = body, Url = url };
        }
        catch
        {
            return null;
        }
    }

    private static Version Normalize(Version? version)
    {
        if (version == null) return new Version(0, 0, 0, 0);
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }
}
