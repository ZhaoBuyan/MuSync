using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    public SteamStatusPriority StatusPriority { get; set; } = SteamStatusPriority.Artist;
    public bool EnableCustomPrefix { get; set; }
    public string CustomPrefix { get; set; } = "";
}
public enum SteamStatusPriority
{
    Artist,
    ProgressBar
}
internal class Configurations
{
    public static readonly Configurations Instance = new();
    private static readonly JsonSerializerOptions SJsonOptions = new() { WriteIndented = true };
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
