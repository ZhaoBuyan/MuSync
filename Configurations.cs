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

/// <summary>配置数据模型：config.json 的全部设置字段（同步 / 外观 / 程序规则 / 语言等）。</summary>
internal class ConfigData
{
    /// <summary>进度同步频率档位（快速 0.25s / 标准 0.5s / 省流 1s）。</summary>
    public SyncSpeedLevel SyncSpeed { get; set; } = SyncSpeedLevel.Standard;
    public bool AutoStart { get; set; }

    /// <summary>界面语言：0 = 中文（默认），1 = English；切换后重启生效。</summary>
    public int Language { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }

    /// <summary>
    /// 配置结构版本。
    /// <para>
    /// 当前为 1，且**没有迁移代码**：新增字段一律靠"缺失即取默认值"兼容（配合宽容枚举解析），
    /// 因此这个数字目前只作记录，不参与任何判定。
    /// </para>
    /// <para>
    /// ⚠️ 两点务必注意：
    /// ① 用户机器上的实际值可能**高于**本常量（实机见过 `SchemaVersion: 3`，来自一个已放弃的预览版）。
    ///    读进来是什么就存回什么，不要"纠正"成 1 —— 那是无意义的改动，还会抹掉磁盘上的真实来源。
    /// ② 真正的迁移分支（`MigrateSettings`）从未进过 main。将来若要做配置迁移，
    ///    编号从这里重新定，不要沿用那个已放弃分支的 2/3 语义。
    /// </para>
    /// </summary>
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

    /// <summary>背景图裁剪区域（归一化 0~1 的 [x,y,w,h]；null = 使用整图）。</summary>
    public double[]? BackgroundCropRect { get; set; }
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
    /// <summary>
    /// 同步浏览器里正在播放的网页媒体（视频 / 音频），走 Windows 系统媒体控件读取。
    /// 默认**关闭**：浏览器经常挂着后台页面在放，不希望它自动顶掉当前状态。
    /// 注意：系统接口不区分网页里放的是视频还是音频（实测视频也被标成 Music），
    /// 因此开启后两者都会同步 —— 这也是它默认关闭的原因。
    /// </summary>
    public bool BrowserMediaSyncEnabled { get; set; }
    /// <summary>
    /// 浏览器媒体的读取间隔（秒，默认 1）。实际状态推送频率仍跟随同步档位，
    /// 这里只控制"多久去系统媒体控件问一次" —— 浏览器会话读得越密越容易踩到接口失效，
    /// 而视频标题本身也不需要 233ms 的精度。
    /// </summary>
    public int BrowserMediaRefreshSeconds { get; set; } = 1;
    /// <summary>
    /// 音乐播放器优先级顺序（NetEase/Tencent/LxMusic/KuGou/TTPlayer；正在播放的始终优先于暂停的）。
    /// 默认值必须与 <c>RpcManager.DefaultPlayerOrder</c>、<c>SettingsForm.PlayerOrderKeys</c> 一致：
    /// 三处口径不同时运行期靠 Concat(Distinct) 补齐不会出错，但读代码的人会被绊住。
    /// </summary>
    public List<string> PlayerPriority { get; set; } = ["NetEase", "Tencent", "LxMusic", "KuGou", "TTPlayer", "Browser"];

    // —— 程序同步 ——
    /// <summary>程序同步总开关。</summary>
    public bool AppSyncEnabled { get; set; } = true;
    /// <summary>同步非游戏应用（关闭时仅游戏类参与同步）。</summary>
    public bool SyncNonGameApps { get; set; }
    /// <summary>程序与音乐组合显示时的分隔符。</summary>
    public string CombinedSeparator { get; set; } = "‖";

    // —— 常驻状态（无音乐 / 程序来源时推送的自定义文案）——
    /// <summary>启用常驻状态：无活跃来源时推送 PersistentStatusText，而不是清除状态。</summary>
    public bool PersistentStatusEnabled { get; set; }
    /// <summary>常驻状态文案（空 = 未启用；超过状态长度上限时按 UTF-8 截断）。</summary>
    public string PersistentStatusText { get; set; } = "";

    // —— 显示格式模板（变量：{app} {song} {artist} {artistPart} {bar} {time} {progress} {sep} + 自定义数据源名）——
    public string MusicFormat { get; set; } = "{song}{artistPart}{progress}";
    public string ProgramFormat { get; set; } = "{app}";
    public string CombinedFormat { get; set; } = "{app} {sep} {song}{artistPart}";
    /// <summary>
    /// 自定义 HTTP 数据源：从任意返回 JSON 的 HTTP 地址取一个值，当作模板积木用。
    /// <para>
    /// 例如「心率」源 = 地址 <c>http://127.0.0.1:9000/heartrate</c> + 取值表达式 <c>heart_rate</c> +
    /// 显示格式 <c>❤{v}</c>，于是模板里写 <c>{心率}</c> 就会展开成 <c>❤92</c>。
    /// 取不到值时该积木展开为空，不影响任何既有同步。
    /// </para>
    /// </summary>
    public List<DataSourceConfig> DataSources { get; set; } = [];
    /// <summary>进度条填充/空白字符（支持 emoji，各取一个字符）。</summary>
    public string ProgressBarFillChar { get; set; } = "#";
    public string ProgressBarEmptyChar { get; set; } = "-";
    /// <summary>进度条长度（格数，1~50，默认 10）。</summary>
    public int ProgressBarLength { get; set; } = 10;

    /// <summary>程序同步规则列表。</summary>
    public List<AppRule> Apps { get; set; } = [];
}

/// <summary>
/// 一个自定义 HTTP 数据源的配置。
/// <para>
/// 语义：请求 <see cref="Url"/>，按 <see cref="ValuePath"/> 从返回的 JSON 里取一个值，
/// 套上 <see cref="Format"/> 后作为模板积木 <c>{名称}</c> 的展开结果。
/// </para>
/// <para>
/// ⚠️ <see cref="Name"/> 就是模板里的积木名，所以**不能包含大括号**（否则会嵌套出歧义）；
/// 名称重复时后一个生效（加载时会去重并提示，避免同一块积木有两个来源）。
/// </para>
/// </summary>
public class DataSourceConfig
{
    /// <summary>是否启用。默认关：不主动往任何地址发请求。</summary>
    public bool Enabled { get; set; }
    /// <summary>积木名（模板里写 <c>{名称}</c> 使用）。</summary>
    public string Name { get; set; } = "";
    /// <summary>完整请求地址（含协议与端口，例如 http://127.0.0.1:9000/heartrate）。</summary>
    public string Url { get; set; } = "";
    /// <summary>
    /// 取值表达式：从 JSON 里取哪个字段，支持 <c>a.b.c</c> 嵌套；留空则取根值
    /// （对端直接返回数字/字符串时用得上）。取到的值可以是数字、字符串或布尔。
    /// </summary>
    public string ValuePath { get; set; } = "";
    /// <summary>显示格式，<c>{v}</c> 会被替换成取到的值；留空则只显示值本身。</summary>
    public string Format { get; set; } = "{v}";
    /// <summary>刷新间隔（秒）。实际推送频率跟随同步档位，这里只控制"多久去问一次对端"。</summary>
    public int RefreshSeconds { get; set; } = 1;
}

/// <summary>配置管理器（单例）：读取 / 保存 config.json——宽容解析、写入加锁、坏文件自动备份。</summary>
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

    /// <summary>
    /// 本次启动"没能读成功、正在用默认值跑"（磁盘上的配置仍在，只是没读到）。
    /// <para>
    /// ⚠️ 为 true 时**禁止写盘**：内存里是默认值，写回去就等于把用户配置清空。
    /// 由 <see cref="Save"/> 检查，见 Load 里对应分支的说明。
    /// </para>
    /// </summary>
    private bool _loadFailedWithDefaults;
    /// <summary>"因未成功加载而跳过保存"的日志只提醒一次，避免每个自动发现的新程序都刷一条。</summary>
    private bool _saveBlockedLogged;

    public void Save()
    {
        if (_loadFailedWithDefaults)
        {
            // 只提醒一次，避免每个自动发现的新程序都刷一条
            if (!_saveBlockedLogged)
            {
                _saveBlockedLogged = true;
                Logger.Warn("本次启动未能读取配置（沿用默认值），已跳过保存以免覆盖磁盘上的原配置；" +
                            "重启程序即可恢复正常保存");
            }
            return;
        }
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
            // 记完整异常（类型 + 消息 + 堆栈）：历史上出过「Value cannot be null.」这类不带参数名的信息，只记 Message 无法定性
            Logger.Error($"保存配置失败: {e}");
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
                // 读取/权限/占用等临时故障：保留磁盘上的原文件，本次仅用内存默认值，下次启动重试。
                // ⚠️ 只置内存默认值是不够的：必须立"禁止保存"的护栏，否则之后任何一次 Save()
                // （例如自动发现新程序时会顺手保存）都会把这份默认值写回磁盘，
                // 用户的令牌、语言、外观、程序规则会被静默清空 —— 而这里连 .bak 都没来得及拷。
                Logger.Warn("配置文件暂时不可读（可能是占用或权限问题），本次不会覆盖磁盘文件");
                _loadFailedWithDefaults = true;
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
                // ⚠️ 必须连 DataSources 一起去掉：它是"用户手改过的自由结构"里最容易写错类型的一块
                // （例如把 RefreshSeconds 写成字符串 "1"）。只去 Apps/PlayerPriority 的话，
                // 降级解析会**再抛一次**，于是整份配置被判损坏 → 备份 → 用默认值覆盖写盘，
                // 用户的令牌、模板、程序规则全部丢失。少恢复一项，远好过整份重置。
                root.Remove(nameof(ConfigData.DataSources));
                var config = root.Deserialize<ConfigData>(ConfigJson.Options);
                if (config != null)
                {
                    DecryptSecrets(config);
                    Logger.Warn("配置已部分恢复：程序同步列表 / 播放器优先级 / 自定义数据源被重置，其余设置保留");
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
        // ⚠️ 这一步必须"永远不抛"。实测踩过：某个承载环境里
        // System.Security.Cryptography.ProtectedData 程序集加载失败，异常在
        // TokenProtector.Unprotect 的**调用点**抛出（方法内部的 try/catch 抓不到），
        // 一路冒到配置解析 → 整份配置被判为"不可解析" → 走降级、降级也抛 → 用默认值替换。
        // 也就是说：**一个令牌字段解密失败，会连带把用户的全部设置判死**。
        // 令牌读不出来最多让用户重新登录一次，绝不能因此丢掉配置。
        try
        {
            config.SteamRefreshToken = TokenProtector.Unprotect(config.SteamRefreshToken);
        }
        catch (Exception ex)
        {
            Logger.Warn($"刷新令牌解密失败（按无令牌处理，其余设置保留）: {ex.GetType().Name}");
            config.SteamRefreshToken = "";
        }
        try
        {
            config.SteamGuardData = TokenProtector.Unprotect(config.SteamGuardData);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Steam Guard 数据解密失败（按无数据处理，其余设置保留）: {ex.GetType().Name}");
            config.SteamGuardData = "";
        }
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
