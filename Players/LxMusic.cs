using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players.Interfaces;
using MuSync.Utils;
namespace MuSync.Players;
/// <summary>LX Music（洛雪）读取：调用其开放 API（HTTP）获取播放状态，进度由调度层插值推进。</summary>
internal sealed class LxMusic : IMusicPlayer
{
    // LX Music 走 HTTP API，无需高频轮询：每秒最多请求一次，
    // 期间的进度由调度层按时间插值推进，每次请求做一次校准。
    private const double PollIntervalMs = 1000;

    private readonly HttpClient _httpClient;
    private readonly string? _apiBaseUrl;
    private readonly bool _isEnabled;
    private string? _lastSongTitle;
    private string? _lastSongArtist;
    private string _currentSongId = Guid.NewGuid().ToString();
    private PlayerInfo? _lastInfoCache;
    private DateTime _lastRequestUtc = DateTime.MinValue;
    private DateTime _lastSuccessUtc = DateTime.MinValue;

    public LxMusic(int pid)
    {
        _httpClient = HttpClientManager.SharedClient;
        try
        {
            var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "lx-music-desktop", "LxDatas", "config_v2.json");
            if (!File.Exists(configPath))
            {
                Debug.WriteLine("[LX Music] Config file not found.");
                _isEnabled = false;
                return;
            }
            var configJson = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<LxMusicConfig>(configJson);
            if (config?.Setting is { Enable: true, Port: not null } settings)
            {
                _apiBaseUrl = $"http://localhost:{settings.Port}";
                _isEnabled = true;
                Debug.WriteLine($"[LX Music] API enabled on port: {settings.Port}");
            }
            else
            {
                _isEnabled = false;
                Debug.WriteLine("[LX Music] OpenAPI is disabled or could not be read from config file.");
                if (config?.Setting != null)
                {
                    Debug.WriteLine(
                        $"[LX Music] Debug Info: Found Enable={config.Setting.Enable}, Port={config.Setting.Port ?? "null"}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[ERROR] Failed to initialize LX Music player: {e.Message}");
            _isEnabled = false;
        }
    }

    // LX Music 没有需要手动释放的长期资源（HttpClient 为全局共享）
    public void Dispose()
    {
    }

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        if (!_isEnabled || string.IsNullOrEmpty(_apiBaseUrl)) return null;
        var nowUtc = DateTime.UtcNow;
        // 节流窗口内直接返回上次成功状态，避免每秒多次 HTTP 请求
        if (_lastInfoCache is { } cached && (nowUtc - _lastRequestUtc).TotalMilliseconds < PollIntervalMs)
        {
            return cached;
        }
        _lastRequestUtc = nowUtc;
        try
        {
            var requestUrl = $"{_apiBaseUrl}/status?filter=status,name,singer,albumName,duration,progress,picUrl";
            var responseJson = await _httpClient.GetStringAsync(requestUrl);
            var status = JsonSerializer.Deserialize<LxMusicStatus>(responseJson);
            if (status is null || string.IsNullOrEmpty(status.Name))
            {
                _lastInfoCache = null;
                return null;
            }
            var isPaused = status.Status switch
            {
                "playing" => false,
                "paused" => true,
                _ => (bool?)null
            };
            if (isPaused is null)
            {
                _lastInfoCache = null;
                return null;
            }
            if (status.Name != _lastSongTitle || status.Singer != _lastSongArtist)
            {
                _currentSongId = Guid.NewGuid().ToString();
                _lastSongTitle = status.Name;
                _lastSongArtist = status.Singer;
            }
            var info = new PlayerInfo
            {
                Identity = _currentSongId,
                Title = status.Name,
                Artists = status.Singer,
                Album = status.AlbumName,
                Cover = status.PicUrl,
                Schedule = status.Progress,
                Duration = status.Duration,
                Pause = isPaused.Value,
                Url = string.Empty
            };
            _lastInfoCache = info;
            _lastSuccessUtc = DateTime.UtcNow;
            return info;
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[Lx Music] API request failed: {e.Message}");
            // 请求失败时短暂沿用上次成功状态（避免网络抖动导致 Steam 状态被误清），
            // 超过 15 秒仍未恢复则视为不可用，交还仲裁层处理
            if (_lastInfoCache is { } fallback && (DateTime.UtcNow - _lastSuccessUtc).TotalSeconds <= 15.0)
            {
                return fallback;
            }
            _lastInfoCache = null;
            return null;
        }
    }
}
/// <summary>洛雪 config 文件的映射模型（读取 openAPI 开关与端口）。</summary>
file record LxMusicConfig
{
    [JsonPropertyName("setting")] public LxMusicSetting? Setting { get; init; }
}
/// <summary>洛雪设置节：开放 API 是否启用与监听端口。</summary>
file record LxMusicSetting
{
    [JsonPropertyName("openAPI.enable")] public bool Enable { get; init; }
    [JsonPropertyName("openAPI.port")] public string? Port { get; init; }
}
/// <summary>洛雪开放 API 返回的播放状态（歌名 / 歌手 / 专辑 / 时长 / 进度 / 封面）。</summary>
file record LxMusicStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("singer")] string Singer,
    [property: JsonPropertyName("albumName")]
    string AlbumName,
    [property: JsonPropertyName("duration")]
    double Duration,
    [property: JsonPropertyName("progress")]
    double Progress,
    [property: JsonPropertyName("picUrl")] string PicUrl
);
