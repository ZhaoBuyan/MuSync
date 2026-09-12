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
    private const int ProgressBarLength = 10;
    private readonly SteamSessionManager _session;
    private string _lastSetName = string.Empty;
    private bool _manualPause;

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
            await _session.SetGameNameAsync(newName).ConfigureAwait(false);
            _lastSetName = newName;
            Debug.WriteLine($"[SteamStatus] 状态已更新: {newName}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[SteamStatus] 更新状态失败: {ex.Message}");
        }
    }

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
        if (appText == null && musicText == null) return null;
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

    private static string ApplyTemplate(string template, string app, PlayerInfo? music, string progressPart,
        ConfigData config)
    {
        var artist = music?.Artists ?? "";
        var artistPart = string.IsNullOrEmpty(artist) ? "" : $" - {artist}";
        return (template ?? "")
            .Replace("{app}", app)
            .Replace("{song}", music?.Title ?? "")
            .Replace("{artistPart}", artistPart)
            .Replace("{artist}", artist)
            .Replace("{progress}", progressPart)
            .Replace("{sep}", config.CombinedSeparator)
            .Trim();
    }

    private static string BuildProgressPart(PlayerInfo music, ConfigData config)
    {
        if (music.Pause) return " (Paused)";
        if (music.Duration <= 0) return "";
        var sb = new StringBuilder(" [");
        var progress = Math.Clamp(music.Schedule / music.Duration, 0, 1);
        var filledCount = (int)(progress * ProgressBarLength);
        sb.Append('#', filledCount);
        sb.Append('-', ProgressBarLength - filledCount);
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

    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
