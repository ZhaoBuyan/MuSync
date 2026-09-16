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

/// <summary>同步频率档位。</summary>
internal enum SyncSpeedLevel
{
    Fast,
    Standard,
    Economic
}

internal class ConfigData
{
    /// <summary>进度同步频率档位（快速 0.25s / 标准 0.5s / 省流 1s）。</summary>
    public SyncSpeedLevel SyncSpeed { get; set; } = SyncSpeedLevel.Standard;
    public bool AutoStart { get; set; }

    /// <summary>界面语言：0 = 中文（默认），1 = English；切换后重启生效。</summary>
    public int Language { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }

    /// <summary>配置结构版本（用于未来迁移；当前为 1）。</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>外观：标题（播放器名）颜色是否跟随播放器品牌色。</summary>
    public bool AppearanceTitleFollowPlayer { get; set; } = true;

    /// <summary>外观：标题颜色（ARGB；仅在关闭跟随时生效）。</summary>
    public int? AppearanceTitleColorArgb { get; set; }

    /// <summary>外观：歌名颜色是否跟随标题色。</summary>
    public bool AppearanceSongFollowPlayer { get; set; } = true;

    /// <summary>外观：歌名颜色（ARGB；仅在关闭跟随时生效）。</summary>
    public int? AppearanceSongColorArgb { get; set; }

    /// <summary>外观：主界面字体族（空 = 默认）。</summary>
    public string AppearanceFontFamily { get; set; } = "";

    /// <summary>外观：主界面字号（0 = 默认）。</summary>
    public float AppearanceFontSize { get; set; }

    /// <summary>外观：主界面背景色（ARGB；null = 默认）。</summary>
    public int? AppearanceBackgroundColorArgb { get; set; }

    /// <summary>外观：主界面背景图路径（空 = 无）。</summary>
    public string AppearanceBackgroundImage { get; set; } = "";

    /// <summary>外观：背景图排版（Stretch / Zoom / Tile / Center）。</summary>
    public string AppearanceBackgroundLayout { get; set; } = "Stretch";
    /// <summary>旧配置兼容字段：显示内容由模板引擎控制，代码不再读取此值。</summary>
    public bool ShowArtistName { get; set; } = true;
    /// <summary>旧配置兼容字段：显示内容由模板引擎控制，代码不再读取此值。</summary>
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
    /// <summary>音乐播放器优先级顺序（NetEase/Tencent/LxMusic；正在播放的始终优先于暂停的）。</summary>
    public List<string> PlayerPriority { get; set; } = ["NetEase", "Tencent", "LxMusic"];

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
    /// <summary>进度条长度（格数，1~50，默认 10）。</summary>
    public int ProgressBarLength { get; set; } = 10;

    /// <summary>程序同步规则列表。</summary>
    public List<AppRule> Apps { get; set; } = [];

    // —— AI 辅助分类（可选，用户自备 API）——
    public string AiApiEndpoint { get; set; } = "";
    public string AiApiKey { get; set; } = "";
    public string AiApiModel { get; set; } = "";
}

internal class Configurations
{
    // ⚠️ 注意：Instance 的构造函数会立即读取/写入配置文件。
    // 不能在此类中放“依赖静态字段声明顺序”的配置项（如 JsonSerializerOptions），
    // 否则构造期间它们还是 null——JSON 选项统一走 ConfigJson.Options（惰性单例）。
    public static readonly Configurations Instance = new();
    private readonly string _path;

    public ConfigData Settings { get; private set; } = new();
    public bool IsFirstLoad { get; private set; }

    private Configurations()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuSync");
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
            var node = JsonSerializer.SerializeToNode(Settings, ConfigJson.Options);
            if (node is JsonObject root)
            {
                ProtectField(root, nameof(ConfigData.SteamRefreshToken));
                ProtectField(root, nameof(ConfigData.SteamGuardData));
            }
            var json = node?.ToJsonString(ConfigJson.Options) ?? "{}";
            WriteAtomic(_path, json);
        }
        catch (Exception e)
        {
            Logger.Error($"保存配置失败: {e.Message}");
        }
    }

    /// <summary>原子写盘：先写临时文件再整体替换，避免写到一半被中断产生损坏配置。</summary>
    private static readonly object WriteLock = new();

    private static void WriteAtomic(string path, string content)
    {
        lock (WriteLock)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
    }

    private void Load()
    {
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            var loaded = DeserializeWithFallback(json);
            if (loaded != null)
            {
                Settings = loaded;
                return;
            }
            throw new InvalidOperationException("配置内容无法解析");
        }
        catch (Exception e)
        {
            Logger.Error($"加载配置失败，使用默认值: {e.Message}");
            if (e is IOException or UnauthorizedAccessException)
            {
                // 读取/权限/占用等临时故障：保留磁盘上的原文件，本次仅用内存默认值，下次启动重试
                Logger.Warn("配置文件暂时不可读（可能是占用或权限问题），本次不会覆盖磁盘文件");
                Settings = new ConfigData();
                return;
            }
            try
            {
                File.Copy(_path, _path + ".failed.json", overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error($"归档损坏配置文件失败: {ex.Message}");
            }
            try
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
                Logger.Warn($"已备份可能损坏的配置文件到 {_path}.bak（原始样本: {_path}.failed.json）");
            }
            catch (Exception ex)
            {
                Logger.Error($"备份损坏配置文件失败: {ex.Message}");
            }
            Settings = new ConfigData();
            Save();
        }
    }

    /// <summary>
    /// 分级解析：正常路径失败时，去掉易损坏的列表字段（程序规则/播放器优先级）重试，
    /// 尽量保住其余配置（登录令牌、开关、模板等），避免"一处坏全盘重置"。
    /// </summary>
    private static ConfigData? DeserializeWithFallback(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<ConfigData>(json, ConfigJson.Options);
            if (config != null)
            {
                DecryptSecrets(config);
            }
            return config;
        }
        catch (Exception e)
        {
            Logger.Warn($"配置解析失败（{e.Message}），尝试保留基本设置…");
        }

        try
        {
            if (JsonNode.Parse(json) is JsonObject root)
            {
                root.Remove(nameof(ConfigData.Apps));
                root.Remove(nameof(ConfigData.PlayerPriority));
                var config = root.Deserialize<ConfigData>(ConfigJson.Options);
                if (config != null)
                {
                    DecryptSecrets(config);
                    Logger.Warn("配置已部分恢复：程序同步列表/播放器优先级被重置，其余设置保留");
                }
                return config;
            }
        }
        catch (Exception e)
        {
            Logger.Error($"配置降级解析失败: {e.Message}");
        }
        return null;
    }

    private static void DecryptSecrets(ConfigData config)
    {
        config.SteamRefreshToken = TokenProtector.Unprotect(config.SteamRefreshToken);
        config.SteamGuardData = TokenProtector.Unprotect(config.SteamGuardData);
    }

    private static void ProtectField(JsonObject root, string name)
    {
        if (root[name] is not JsonValue value || value.GetValue<string>() is not { Length: > 0 } plain)
        {
            return;
        }
        root[name] = TokenProtector.Protect(plain);
    }
}
