using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Utils;
namespace MuSync;

/// <summary>
/// 状态合成与推送。
/// 显示优先级：真实 Steam 游戏（暂停一切）> 程序与音乐组合 > 程序单项 > 音乐单项 > 清除。
/// 文本由格式模板生成，支持变量：{app} {song} {artist} {artistPart} {progress} {sep}。
/// </summary>
internal class SteamStatusManager
{
    private const int MaxStatusBytes = 128;
    private readonly SteamSessionManager _session;
    private string _lastSetName = string.Empty;
    private bool _manualPause;

    /// <summary>最近一次成功推送到 Steam 的状态文本（诊断用：核对"实际发出去了什么"）。</summary>
    public string LastPushedStatus => _lastSetName;

    public SteamStatusManager(SteamSessionManager session)
    {
        _session = session;
    }

    public bool IsReady => _session.IsLoggedOn;
    public bool IsRealGameActive => _session.IsRealGameActive;

    /// <summary>托盘“暂停同步”手动开关：开启时立即清状态并停止推送。</summary>
    public bool ManualPause
    {
        get => _manualPause;
        set
        {
            _manualPause = value;
            if (value) ClearStatus();
        }
    }

    public async Task UpdateStatusAsync(PlayerInfo? musicInfo, string playerName, string? appDisplay = null)
    {
        if (!_session.IsLoggedOn) return;
        var config = Configurations.Instance.Settings;
        if (config.PauseWhenPlayingGame && _session.IsRealGameActive) return;
        if (_manualPause) return;
        var newName = ComposeStatus(musicInfo, appDisplay, config, DynamicBlockResolver?.Invoke());
        if (newName == null)
        {
            ClearStatus();
            return;
        }
        if (newName == _lastSetName) return;
        try
        {
            // 推送加超时兜底：Steam 侧异常时不允许把主循环拖住（主循环每个 await 都必须能回来）
            await _session.SetGameNameAsync(newName).WaitAsync(StabilityConfig.StatusPushTimeout)
                .ConfigureAwait(false);
            _lastSetName = newName;
            Debug.WriteLine($"[SteamStatus] 状态已更新: {newName}");
        }
        catch (TimeoutException)
        {
            Logger.Warn($"[SteamStatus] 状态推送超时（{StabilityConfig.StatusPushTimeout.TotalSeconds:F0} 秒），本轮跳过: {newName}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[SteamStatus] 更新状态失败: {ex.Message}");
        }
    }

    /// <summary>失效「同内容不重发」缓存：重连 / 系统唤醒后让下一次推送必定发出。</summary>
    public void InvalidatePushedCache() => _lastSetName = string.Empty;

    public void ClearStatus()
    {
        if (!_session.IsLoggedOn) return;
        if (_lastSetName.Length == 0) return;
        _session.ClearGameName();
        _lastSetName = string.Empty;
        Logger.Info("[SteamStatus] 状态已清除");
    }

    /// <summary>
    /// 合成最终状态文本；返回 null 表示"无内容可显示"（调用方应清除状态）。
    /// </summary>
    /// <param name="dynamicBlocks">
    /// 自定义数据源的值（积木名 → 已按显示格式展开好的文本）。
    /// 例如 <c>{"心率": "❤92"}</c>，模板里写 <c>{心率}</c> 即可展开；缺失的积木展开为空。
    /// </param>
    public static string? ComposeStatus(PlayerInfo? music, string? appDisplay, ConfigData config,
        IReadOnlyDictionary<string, string>? dynamicBlocks = null)
    {
        var blocks = dynamicBlocks ?? EmptyBlocks;
        var appText = string.IsNullOrWhiteSpace(appDisplay) ? null : appDisplay.Trim();
        var musicText = BuildMusicText(music, config, includeProgress: true, blocks);
        if (appText == null && musicText == null)
        {
            // 无音乐也无程序：常驻状态开启且文案非空时推送常驻文案，否则返回 null（调用方清除状态）
            var persistent = config.PersistentStatusEnabled ? (config.PersistentStatusText ?? "").Trim() : "";
            // 常驻文案同样支持积木（例如不播音乐时只想显示心率）
            persistent = ApplyTemplate(persistent, "", null, "", "", config, blocks);
            return persistent.Length == 0 ? null : TruncateToUtf8ByteLength(persistent, MaxStatusBytes);
        }
        if (appText == null) return TruncateToUtf8ByteLength(musicText!, MaxStatusBytes);
        if (musicText == null)
        {
            var programText = ApplyTemplate(config.ProgramFormat, appText, null, "", "", config, blocks);
            return TruncateToUtf8ByteLength(string.IsNullOrWhiteSpace(programText) ? appText : programText,
                MaxStatusBytes);
        }
        // 程序 + 音乐：按组合模板生成，超长时逐级降级（去进度 → 仅歌名 → 仅程序名）
        var song = music!.Value;
        var barPart = BuildBarPart(song, config);
        var timePart = BuildTimePart(song, config);
        var full = ApplyTemplate(config.CombinedFormat, appText, song, barPart, timePart, config, blocks);
        var noProgress = ApplyTemplate(config.CombinedFormat, appText, song, "", "", config, blocks);
        var songOnly = $"{appText} {config.CombinedSeparator} {song.Title}".Trim();
        string[] candidates = [full, noProgress, songOnly, appText];
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && GetUtf8ByteCount(candidate) <= MaxStatusBytes)
            {
                return candidate.Trim();
            }
        }
        return TruncateToUtf8ByteLength(candidates[0].Trim(), MaxStatusBytes);
    }

    private static readonly Dictionary<string, string> EmptyBlocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 自定义积木的取值回调：由 RpcManager 在启动时注册，返回"积木名 → 当前显示文本"。
    /// <para>
    /// 为什么用回调而不是直接引用：<see cref="ComposeStatus"/> 是静态的，而数据源的运行实例归 RpcManager 所有。
    /// </para>
    /// <para>
    /// ⚠️ 只有真正推送状态的那条路径（<see cref="UpdateStatusAsync"/>）会调用它；
    /// 设置页与格式编辑器的**预览不调用**（预览不该发网络请求），因此预览里自定义积木显示为空。
    /// 未注册（或取不到值）时所有自定义积木展开为空，不影响任何既有行为。
    /// </para>
    /// </summary>
    public static Func<IReadOnlyDictionary<string, string>>? DynamicBlockResolver { get; set; }

    public static string GetStatusPreview(PlayerInfo? music, string? appDisplay, ConfigData config,
        IReadOnlyDictionary<string, string>? dynamicBlocks = null)
    {
        return ComposeStatus(music, appDisplay, config, dynamicBlocks) ?? "MuSync";
    }

    private static string? BuildMusicText(PlayerInfo? music, ConfigData config, bool includeProgress,
        IReadOnlyDictionary<string, string> blocks)
    {
        if (music is not { } m) return null;
        if (!config.MusicSyncEnabled) return null;
        // 暂停时按设置隐藏；HideMusicWhenPaused=false 时显示 "(Paused)"（旧行为）
        if (m.Pause && config.HideMusicWhenPaused) return null;
        var barPart = includeProgress ? BuildBarPart(m, config) : "";
        var timePart = includeProgress ? BuildTimePart(m, config) : "";
        var text = ApplyTemplate(config.MusicFormat, "", m, barPart, timePart, config, blocks);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// 模板替换：**单趟扫描**，只替换模板原文里的占位符。
    /// <para>
    /// ⚠️ 不能用一串 <c>Replace</c> 顺序替换：那样替换**进去的内容**会被后续占位符再次命中。
    /// 例如曲目名恰好是 <c>{progress}</c>（或用户把程序显示名设成 <c>{sep}</c>），
    /// 经 <c>.Replace("{song}", ...)</c> 注入后又被后面的 <c>.Replace("{progress}", ...)</c> 展开 ——
    /// 外部文本被当成模板解释。单趟扫描从根上避免。
    /// </para>
    /// </summary>
    private static string ApplyTemplate(string template, string app, PlayerInfo? music, string barPart, string timePart,
        ConfigData config, IReadOnlyDictionary<string, string> blocks)
    {
        var artist = music?.Artists ?? "";
        var artistPart = string.IsNullOrEmpty(artist) ? "" : $" - {artist}";
        if (string.IsNullOrEmpty(template)) return "";
        return PlaceholderPattern.Replace(template, match =>
        {
            var token = match.Value;
            switch (token)
            {
                case "{app}": return app;
                case "{song}": return music?.Title ?? "";
                // {artistPart} 必须先于 {artist} 判断：否则 "{artistPart}" 会被当成 "{artist}" + "Part}"
                case "{artistPart}": return artistPart;
                case "{artist}": return artist;
                // {progress} 保持"进度条 + 时间"的组合语义（老模板零变化）；
                // 想把两者分开放，用 {bar} / {time}。
                case "{progress}": return barPart + timePart;
                case "{bar}": return barPart;
                case "{time}": return timePart;
                case "{sep}": return config.CombinedSeparator;
            }
            // 自定义数据源积木：{名称} → 取到的值；没在配（或当前取不到值）时展开为空
            var name = token.Length > 2 ? token[1..^1] : "";
            return blocks.TryGetValue(name, out var value) ? value : "";
        }).Trim();
    }

    /// <summary>
    /// 模板占位符：内建标记 + 任意自定义积木名（<c>{心率}</c> 这类）。
    /// <para>
    /// ⚠️ 匹配范围放宽到"任意无括号的名字"，但**仍然是单趟扫描**：认不出的名字展开为空，
    /// 且替换进去的文本不会被再次扫描 —— 这是"外部文本不被当成模板解释"的保证。
    /// </para>
    /// <para>
    /// ⚠️ 名字长度上界 <see cref="MaxBlockNameLength"/> 必须与设置页的校验一致：
    /// 正则认不下的名字会**永远静默展开为空**（用户看不出哪里错了）。
    /// </para>
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PlaceholderPattern =
        new($@"\{{[^{{}}\r\n]{{1,{MaxBlockNameLength}}}\}}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 内建模板标记的名字（不含大括号），**区分大小写**。
    /// <para>
    /// ⚠️ 大小写口径必须与 <see cref="ApplyTemplate"/> 的 switch 一致：switch 比对的是字面量，
    /// 所以只有**全小写**的 <c>{time}</c> 会被内建时间轴抢走；
    /// 数据源叫 <c>Time</c> 时，模板里写 <c>{Time}</c> / <c>{TIME}</c> 仍能命中它（数据源查表忽略大小写）。
    /// 若这里用忽略大小写，就会把本来能用的名字误判成冲突。
    /// </para>
    /// </summary>
    internal static readonly IReadOnlySet<string> BuiltInBlockNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "app", "song", "artist", "artistPart", "bar", "time", "progress", "sep"
        };
    /// <summary>自定义积木名的最大长度（模板正则与设置页校验共用同一口径）。</summary>
    internal const int MaxBlockNameLength = 32;

    /// <summary>
    /// 进度条部分（**只有条**，不含时间）：<c> [####------]</c>。
    /// <para>想同时显示时间请用 <c>{time}</c>；老模板里的 <c>{progress}</c> 仍是"条 + 时间"。</para>
    /// </summary>
    private static string BuildBarPart(PlayerInfo music, ConfigData config)
    {
        if (music.Pause) return " (Paused)";
        if (music.Duration <= 0) return "";
        // ⚠️ 必须同时挡掉非有限值：本工程在 Debug 与 Release 都开了 CheckForOverflowUnderflow，
        // 而 Math.Clamp(NaN, 0, 1) 返回的是 **NaN**（不是 0），下一行 (int)(NaN * n) 会抛 OverflowException。
        // 异常会冒到主循环兜底 → 每拍重建全部播放器并清空状态，等于该媒体存在期间同步彻底失效。
        // 位置来自播放器的内存读取，读到错位数据时出现 NaN/Infinity 是现实可能的。
        if (!double.IsFinite(music.Schedule) || !double.IsFinite(music.Duration)) return "";
        var barLength = config.ProgressBarLength is >= 1 and <= 50 ? config.ProgressBarLength : 10;
        var progress = Math.Clamp(music.Schedule / music.Duration, 0, 1);
        var filledCount = (int)Math.Clamp(progress * barLength, 0, barLength);
        var fill = BarStyleParser.Normalize(config.ProgressBarFillChar, "#");
        var empty = BarStyleParser.Normalize(config.ProgressBarEmptyChar, "-");
        var sb = new StringBuilder(" [");
        for (var i = 0; i < filledCount; i++)
        {
            sb.Append(fill);
        }
        for (var i = 0; i < barLength - filledCount; i++)
        {
            sb.Append(empty);
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// 时间轴部分（**只有时间**，不含条）：<c> 2:30/4:15</c>。
    /// <para>
    /// 开头的空格是刻意保留的：这样 <c>{bar}{time}</c> 拼出来的结果与旧的 <c>{progress}</c> 完全一致，
    /// 老模板改成拆分布局时不会多/少一个空格。
    /// </para>
    /// </summary>
    private static string BuildTimePart(PlayerInfo music, ConfigData config)
    {
        if (music.Pause) return "";
        if (music.Duration <= 0) return "";
        if (!double.IsFinite(music.Schedule) || !double.IsFinite(music.Duration)) return "";
        return $" {FormatTime(music.Schedule)}/{FormatTime(music.Duration)}";
    }
    private static int GetUtf8ByteCount(string str) => Encoding.UTF8.GetByteCount(str);

    private static string TruncateToUtf8ByteLength(string str, int maxBytes)
    {
        if (string.IsNullOrEmpty(str)) return str;
        if (GetUtf8ByteCount(str) <= maxBytes) return str;
        var byteCount = 0;
        var charIndex = 0;
        foreach (var rune in str.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (byteCount + runeBytes > maxBytes) break;
            byteCount += runeBytes;
            charIndex += rune.Utf16SequenceLength;
        }
        return charIndex >= str.Length ? str : str[..charIndex];
    }

    private static string FormatTime(double totalSeconds) => FormatClockTime(totalSeconds);

    /// <summary>可安全格式化的最大秒数（超出按"时长未知"处理）。见 <see cref="FormatClockTime"/> 的夹取说明。</summary>
    private const double MaxFormattableSeconds = 359_999;   // 99:59:59，足够覆盖任何歌曲/视频

    /// <summary>
    /// 把秒数格式化成 <c>分:秒</c>；**超过一小时必须带上小时位**（<c>1:08:20</c>）。
    /// <para>
    /// ⚠️ 不能直接用 <c>TimeSpan</c> 的 <c>mm\:ss</c> 自定义格式：那只输出"分"与"秒"两个分量，
    /// 小时会被丢掉 —— 1 小时 8 分 20 秒会显示成 <c>08:20</c>（实测踩过：视频总时长正好差一小时）。
    /// 界面与 Steam 状态两处都必须走这个函数，避免口径不一致。
    /// </para>
    /// <para>
    /// ⚠️ 还必须做夹取（实测过 TimeSpan.FromSeconds 的行为）：<c>NaN</c> → <c>ArgumentException</c>，
    /// <c>±Infinity</c> 与超大有限值（如 <c>1e18</c>、<c>double.MaxValue</c>）→ <c>OverflowException</c>；
    /// 另有 <c>TimeSpan.MaxValue.TotalSeconds</c> 这类值虽不抛、却会把状态显示成荒谬的时长。
    /// 时长来自外部（SMTC 的 EndTime 有拿哨兵值表示"未知"的源、LX 的 HTTP JSON），
    /// 一旦抛出去会一路冒到主循环兜底 → <b>每拍</b>重建全部播放器并清空状态，
    /// 等于该媒体存在期间同步彻底失效。超出合理范围时按"时长未知"处理。
    /// </para>
    /// </summary>
    internal static string FormatClockTime(double totalSeconds)
    {
        if (!double.IsFinite(totalSeconds) || totalSeconds <= 0) return "0:00";
        if (totalSeconds > MaxFormattableSeconds) return "0:00";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
