using System;
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
        var newName = ComposeStatus(musicInfo, appDisplay, config);
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

    /// <summary>合成最终状态文本；返回 null 表示"无内容可显示"（调用方应清除状态）。</summary>
    public static string? ComposeStatus(PlayerInfo? music, string? appDisplay, ConfigData config)
    {
        var appText = string.IsNullOrWhiteSpace(appDisplay) ? null : appDisplay.Trim();
        var musicText = BuildMusicText(music, config, includeProgress: true);
        if (appText == null && musicText == null)
        {
            // 无音乐也无程序：常驻状态开启且文案非空时推送常驻文案，否则返回 null（调用方清除状态）
            var persistent = config.PersistentStatusEnabled ? (config.PersistentStatusText ?? "").Trim() : "";
            return persistent.Length == 0 ? null : TruncateToUtf8ByteLength(persistent, MaxStatusBytes);
        }
        if (appText == null) return TruncateToUtf8ByteLength(musicText!, MaxStatusBytes);
        if (musicText == null)
        {
            var programText = ApplyTemplate(config.ProgramFormat, appText, null, "", config);
            return TruncateToUtf8ByteLength(string.IsNullOrWhiteSpace(programText) ? appText : programText,
                MaxStatusBytes);
        }
        // 程序 + 音乐：按组合模板生成，超长时逐级降级（去进度 → 仅歌名 → 仅程序名）
        var song = music!.Value;
        var progressPart = BuildProgressPart(song, config);
        var full = ApplyTemplate(config.CombinedFormat, appText, song, progressPart, config);
        var noProgress = ApplyTemplate(config.CombinedFormat, appText, song, "", config);
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

    public static string GetStatusPreview(PlayerInfo? music, string? appDisplay, ConfigData config)
    {
        return ComposeStatus(music, appDisplay, config) ?? "MuSync";
    }

    private static string? BuildMusicText(PlayerInfo? music, ConfigData config, bool includeProgress)
    {
        if (music is not { } m) return null;
        if (!config.MusicSyncEnabled) return null;
        // 暂停时按设置隐藏；HideMusicWhenPaused=false 时显示 "(Paused)"（旧行为）
        if (m.Pause && config.HideMusicWhenPaused) return null;
        var progress = includeProgress ? BuildProgressPart(m, config) : "";
        var text = ApplyTemplate(config.MusicFormat, "", m, progress, config);
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
    private static string ApplyTemplate(string template, string app, PlayerInfo? music, string progressPart,
        ConfigData config)
    {
        var artist = music?.Artists ?? "";
        var artistPart = string.IsNullOrEmpty(artist) ? "" : $" - {artist}";
        if (string.IsNullOrEmpty(template)) return "";
        return PlaceholderPattern.Replace(template, match => match.Value switch
        {
            "{app}" => app,
            "{song}" => music?.Title ?? "",
            // {artistPart} 必须先于 {artist} 判断：否则 "{artistPart}" 会被当成 "{artist}" + "Part}"
            "{artistPart}" => artistPart,
            "{artist}" => artist,
            "{progress}" => progressPart,
            "{sep}" => config.CombinedSeparator,
            _ => match.Value
        }).Trim();
    }

    /// <summary>模板占位符（单趟替换用，见 <see cref="ApplyTemplate"/>）。</summary>
    private static readonly System.Text.RegularExpressions.Regex PlaceholderPattern =
        new(@"\{(?:app|song|artist|artistPart|progress|sep)\}",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string BuildProgressPart(PlayerInfo music, ConfigData config)
    {
        if (music.Pause) return " (Paused)";
        if (music.Duration <= 0) return "";
        // ⚠️ 必须同时挡掉非有限值：本工程在 Debug 与 Release 都开了 CheckForOverflowUnderflow，
        // 而 Math.Clamp(NaN, 0, 1) 返回的是 **NaN**（不是 0），下一行 (int)(NaN * n) 会抛 OverflowException。
        // 异常会冒到主循环兜底 → 每拍重建全部播放器并清空状态，等于该媒体存在期间同步彻底失效。
        // 位置来自播放器的内存读取，读到错位数据时出现 NaN/Infinity 是现实可能的。
        if (!double.IsFinite(music.Schedule) || !double.IsFinite(music.Duration)) return "";
        var barLength = config.ProgressBarLength is >= 1 and <= 50 ? config.ProgressBarLength : 10;
        var sb = new StringBuilder(" [");
        var progress = Math.Clamp(music.Schedule / music.Duration, 0, 1);
        var filledCount = (int)Math.Clamp(progress * barLength, 0, barLength);
        var fill = BarStyleParser.Normalize(config.ProgressBarFillChar, "#");
        var empty = BarStyleParser.Normalize(config.ProgressBarEmptyChar, "-");
        for (var i = 0; i < filledCount; i++)
        {
            sb.Append(fill);
        }
        for (var i = 0; i < barLength - filledCount; i++)
        {
            sb.Append(empty);
        }
        sb.Append($"] {FormatTime(music.Schedule)}/{FormatTime(music.Duration)}");
        return sb.ToString();
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
