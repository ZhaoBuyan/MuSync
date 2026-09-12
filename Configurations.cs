using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MuSync.Models;
using MuSync.Utils;
namespace MuSync;
internal class ConfigData
{
    public bool AutoStart { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }
    public bool ShowArtistName { get; set; } = true;
    public bool ShowProgressBar { get; set; } = true;
    public bool PauseWhenPlayingGame { get; set; } = true;
    public bool EnableSteamSync { get; set; } = true;
    public string SteamUsername { get; set; } = "";
    public string SteamRefreshToken { get; set; } = "";
    public string SteamGuardData { get; set; } = "";
    public bool EnableCustomPrefix { get; set; }
    public string CustomPrefix { get; set; } = "";

    // —— 连接 ——
    /// <summary>TCP 连接失败时自动切换 WebSocket (443 端口) 重试。</summary>
    public bool AllowWebSocketFallback { get; set; } = true;

    // —— 音乐同步 ——
    /// <summary>音乐同步总开关（独立于程序同步）。</summary>
    public bool MusicSyncEnabled { get; set; } = true;
    /// <summary>音乐暂停时不在 Steam 状态中显示。</summary>
    public bool HideMusicWhenPaused { get; set; } = true;

    // —— 程序同步 ——
    /// <summary>程序同步总开关。</summary>
    public bool AppSyncEnabled { get; set; } = true;
    /// <summary>同步非游戏应用（关闭时仅游戏类参与同步）。</summary>
    public bool SyncNonGameApps { get; set; }
    /// <summary>程序与音乐组合显示时的分隔符。</summary>
    public string CombinedSeparator { get; set; } = "‖";

    // —— 显示格式模板（变量：{app} {song} {artist} {artistPart} {progress} {sep}）——
    public string MusicFormat { get; set; } = "{song}{artistPart}{progress}";
    public string ProgramFormat { get; set; } = "{app}";
    public string CombinedFormat { get; set; } = "{app} {sep} {song}{artistPart}";
    /// <summary>进度条填充/空白字符（支持 emoji，各取一个字符）。</summary>
    public string ProgressBarFillChar { get; set; } = "#";
    public string ProgressBarEmptyChar { get; set; } = "-";
    /// <summary>音乐播放器优先级顺序（NetEase/Tencent/LxMusic；正在播放的始终优先于暂停的）。</summary>
    public List<string> PlayerPriority { get; set; } = ["NetEase", "Tencent", "LxMusic"];
    /// <summary>程序同步规则列表。</summary>
    public List<AppRule> Apps { get; set; } = [];

    // —— AI 辅助分类（可选，用户自备 API）——
    public string AiApiEndpoint { get; set; } = "";
    public string AiApiKey { get; set; } = "";
    public string AiApiModel { get; set; } = "";
}
internal class Configurations
{
    public static readonly Configurations Instance = new();
    private static readonly JsonSerializerOptions SJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public ConfigData Settings { get; private set; }
    [JsonIgnore] public bool IsFirstLoad { get; }
    [JsonIgnore] private readonly string _path;
    private Configurations()
    {
        Settings = new ConfigData();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MuSync");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "config.json");
        if (File.Exists(_path))
        {
            IsFirstLoad = false;
            Load();
        }
        else
        {
            IsFirstLoad = true;
            Save();
        }
    }
    public void Save()
    {
        try
        {
            var node = JsonSerializer.SerializeToNode(Settings, SJsonOptions);
            if (node is JsonObject root)
            {
                ProtectField(root, nameof(ConfigData.SteamRefreshToken));
                ProtectField(root, nameof(ConfigData.SteamGuardData));
            }
            File.WriteAllText(_path, node?.ToJsonString(SJsonOptions) ?? "{}", Encoding.UTF8);
        }
        catch (Exception e)
        {
            Logger.Error($"保存配置失败: {e.Message}");
        }
    }
    private void Load()
    {
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            var node = JsonNode.Parse(json);
            if (node is JsonObject root)
            {
                UnprotectField(root, nameof(ConfigData.SteamRefreshToken));
                UnprotectField(root, nameof(ConfigData.SteamGuardData));
            }
            var loadedConfig = node.Deserialize<ConfigData>(SJsonOptions);
            if (loadedConfig == null) return;
            Settings = loadedConfig;
        }
        catch (Exception e)
        {
            Logger.Error($"加载配置失败，使用默认值: {e.Message}");
            try
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
                Logger.Warn($"已备份可能损坏的配置文件到 {_path}.bak");
            }
            catch (Exception ex)
            {
                Logger.Error($"备份损坏配置文件失败: {ex.Message}");
            }
            Save();
        }
    }
    private static void ProtectField(JsonObject root, string name)
    {
        if (root[name] is JsonValue value &&
            value.GetValue<string>() is { Length: > 0 } plain)
        {
            root[name] = TokenProtector.Protect(plain);
        }
    }
    private static void UnprotectField(JsonObject root, string name)
    {
        if (root[name] is JsonValue value &&
            value.GetValue<string>() is { Length: > 0 } stored)
        {
            root[name] = TokenProtector.Unprotect(stored);
        }
    }
}
