using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
namespace MuSync.Utils;

/// <summary>
/// 诊断信息汇总：一次粘贴即包含排查所需的版本 / 运行状态 / 关键设置 / 路径。
/// 供设置 → 诊断页「复制诊断信息」按钮使用。
/// </summary>
internal static class DiagnosticsReport
{
    public static string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.L($"—— MuSync 诊断信息（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）——", $"—— MuSync diagnostics ({DateTime.Now:yyyy-MM-dd HH:mm:ss}) ——"));
        AppendVersionInfo(sb);
        AppendSteamInfo(sb);
        AppendPlayerInfo(sb);
        AppendAppSyncInfo(sb);
        AppendSettingsSummary(sb);
        AppendEnvironmentInfo(sb);
        return sb.ToString().TrimEnd();
    }

    /// <summary>版本 + 构建时间 + 运行时长。</summary>
    private static void AppendVersionInfo(StringBuilder sb)
    {
        var exePath = Environment.ProcessPath;
        var exeName = exePath is null ? "MuSync.exe" : Path.GetFileName(exePath);
        var buildTime = TryGet(() => File.GetLastWriteTime(exePath!).ToString("yyyy-MM-dd HH:mm"), Loc.L("未知", "Unknown"));
        sb.AppendLine(
            Loc.L($"版本：{UpdateChecker.GetCurrentVersionText()}（{UpdateChecker.GetCurrentEditionText()}，{exeName}，构建于 {buildTime}）", $"Version: {UpdateChecker.GetCurrentVersionText()} ({UpdateChecker.GetCurrentEditionText()}, {exeName}, built {buildTime})"));
        var uptime = DateTime.Now - Program.StartedAt;
        var uptimeText = uptime.TotalHours >= 1
            ? Loc.L($"{(int)uptime.TotalHours} 小时 {uptime.Minutes} 分", $"{(int)uptime.TotalHours}h {uptime.Minutes}m")
            : Loc.L($"{Math.Max(1, (int)uptime.TotalMinutes)} 分钟", $"{Math.Max(1, (int)uptime.TotalMinutes)}m");
        sb.AppendLine(Loc.L($"运行：启动于 {Program.StartedAt:yyyy-MM-dd HH:mm}，已运行 {uptimeText}", $"Runtime: started {Program.StartedAt:yyyy-MM-dd HH:mm}, uptime {uptimeText}"));
    }

    /// <summary>Steam 连接 / 同步 / 更新状态。</summary>
    private static void AppendSteamInfo(StringBuilder sb)
    {
        var session = Program.GetSessionManager();
        var status = session switch
        {
            null => Loc.L("未初始化", "Not initialized"),
            { IsLoggedOn: true } => Loc.L("已登录", "Signed in"),
            { IsConnected: true } => Loc.L("已连接（未登录）", "Connected (not signed in)"),
            _ => Loc.L("连接中", "Connecting")
        };
        var protocol = session is null ? "--" : session.ConnectionProtocolText;
        var error = string.IsNullOrEmpty(session?.LoginError) ? Loc.L("无", "None") : session!.LoginError!;
        sb.AppendLine(Loc.L($"Steam：{status} ｜ 协议 {protocol} ｜ 错误：{error}", $"Steam: {status} | protocol {protocol} | error: {error}"));

        var manager = Program.GetSteamManager();
        var config = Configurations.Instance.Settings;
        var syncState = manager?.ManualPause == true
            ? Loc.L("已手动暂停", "Paused manually")
            : manager?.IsRealGameActive == true && config.PauseWhenPlayingGame
                ? Loc.L("真实游戏中暂停", "Paused (real game running)")
                : Loc.L("正常", "Normal");
        var update = Program.PendingUpdate is { } pending ? Loc.L($"已发现 {pending.Tag}", $"Found {pending.Tag}") : Loc.L("未发现", "Not found");
        sb.AppendLine(Loc.L($"同步：{syncState} ｜ 更新：{update}", $"Sync: {syncState} | update: {update}"));
    }

    /// <summary>四个播放器的运行 / 播放 / 错误状态。</summary>
    private static void AppendPlayerInfo(StringBuilder sb)
    {
        var rpc = Program.GetRpcManager();
        if (rpc is null)
        {
            sb.AppendLine(Loc.L("播放器：未初始化", "Players: not initialized"));
            return;
        }
        foreach (var status in rpc.GetPlayerStatusSnapshot())
        {
            sb.AppendLine($"{status.Name}：{DescribePlayer(status)}");
        }
    }

    private static string DescribePlayer(PlayerStatus status)
    {
        if (status.LastError != RpcManager.ErrorCode.None)
        {
            return status.LastError switch
            {
                RpcManager.ErrorCode.PermissionDenied => Loc.L("⚠️ 需要管理员运行", "⚠️ Admin rights required"),
                RpcManager.ErrorCode.DllNotFound => Loc.L("⚠️ 播放器组件未加载", "⚠️ Player component not loaded"),
                RpcManager.ErrorCode.VersionNotSupported => Loc.L("⚠️ 版本不支持 / 特征码失效", "⚠️ Unsupported version / pattern not found"),
                _ => Loc.L("⚠️ 未知错误", "⚠️ Unknown error")
            };
        }
        if (!status.Running) return Loc.L("未运行", "Not running");
        if (string.IsNullOrEmpty(status.Title)) return Loc.L($"运行中（未在播放）{ActiveMark(status)}", $"Running (not playing) {ActiveMark(status)}");
        var song = string.IsNullOrEmpty(status.Artists) ? status.Title : $"{status.Title} - {status.Artists}";
        return Loc.L($"{(status.Pause ? "已暂停" : "正在播放")}「{song}」{ActiveMark(status)}", $"{(status.Pause ? "Paused" : "Playing")} - {song} {ActiveMark(status)}");
    }

    private static string ActiveMark(PlayerStatus status) => status.IsActive ? Loc.L("（当前活跃）", " (active)") : "";

    /// <summary>程序同步状态。</summary>
    private static void AppendAppSyncInfo(StringBuilder sb)
    {
        var config = Configurations.Instance.Settings;
        if (!config.AppSyncEnabled)
        {
            sb.AppendLine(Loc.L("程序同步：关闭", "App sync: off"));
            return;
        }
        var scope = config.SyncNonGameApps ? Loc.L("含非游戏程序", "Includes non-game apps") : Loc.L("仅游戏", "Games only");
        var current = Program.GetRpcManager()?.GetActiveAppDisplay();
        sb.AppendLine(Loc.L($"程序同步：开启（{scope}）｜ 当前：{current ?? "（无）"}", $"App sync: on ({scope}) | current: {current ?? "(None)"}"));
    }

    /// <summary>关键设置摘要。</summary>
    private static void AppendSettingsSummary(StringBuilder sb)
    {
        var config = Configurations.Instance.Settings;
        var speed = config.SyncSpeed switch
        {
            SyncSpeedLevel.Fast => Loc.L("快速 0.25s", "Fast 0.25s"),
            SyncSpeedLevel.Economic => Loc.L("省流 1s", "Data saver 1s"),
            _ => Loc.L("标准 0.5s", "Standard 0.5s")
        };
        sb.AppendLine(
            Loc.L($"音乐同步：{(config.MusicSyncEnabled ? "开启" : "关闭")} ｜ 暂停时隐藏：{(config.HideMusicWhenPaused ? "开" : "关")} ｜ 频率：{speed}", $"Music sync: {(config.MusicSyncEnabled ? "on" : "off")} | hide when paused: {(config.HideMusicWhenPaused ? "on" : "off")} | rate: {speed}"));
        sb.AppendLine(
            Loc.L($"启动：开机自启 {(config.AutoStart ? "开" : "关")} ｜ 关闭最小化到托盘 {(config.CloseToTray ? "开" : "关")} ｜ 静默启动 {(config.StartInTray ? "开" : "关")}", $"Startup: auto-start {(config.AutoStart ? "on" : "off")} | minimize to tray on close {(config.CloseToTray ? "on" : "off")} | start minimized {(config.StartInTray ? "on" : "off")}"));
        var appearance = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.AppearanceBackgroundImage)) appearance.Add(Loc.L("背景图", "Background image"));
        if (!string.IsNullOrWhiteSpace(config.AppearanceFontFamily) || config.AppearanceFontSize > 0) appearance.Add(Loc.L("字体", "Font"));
        if (config.AppearanceTitleColorArgb is not null || config.AppearanceSongColorArgb is not null) appearance.Add(Loc.L("颜色", "Color"));
        sb.AppendLine(Loc.L($"外观自定义：{(appearance.Count > 0 ? string.Join(" / ", appearance) : "无")}", $"Appearance customizations: {(appearance.Count > 0 ? string.Join(" / ", appearance) : "None")}"));
    }

    /// <summary>系统 / 内存 / 缓存 / 路径。</summary>
    private static void AppendEnvironmentInfo(StringBuilder sb)
    {
        var monitors = TryGet(() => Screen.AllScreens.Length.ToString(), "--");
        sb.AppendLine(
            Loc.L($"系统：{Environment.OSVersion.VersionString} ｜ {(Environment.Is64BitOperatingSystem ? "64 位" : "32 位")} ｜ 显示器 {monitors} 台", $"System: {Environment.OSVersion.VersionString} | {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} | {monitors} display(s)"));
        var memory = PerformanceMonitor.GetMemoryInfo();
        var cache = PerformanceMonitor.GetCacheStatistics();
        sb.AppendLine(
            Loc.L($"内存：工作集 {memory.GetFormattedWorkingSet()} ｜ 私有 {memory.GetFormattedPrivateMemory()} ｜ GC {memory.GetFormattedGcMemory()} ｜ 缓存：图片 {cache.ImageCacheCount} / 模块 {cache.ModuleCacheCount + cache.ProcessModuleCacheCount}", $"Memory: working set {memory.GetFormattedWorkingSet()} | private {memory.GetFormattedPrivateMemory()} | GC {memory.GetFormattedGcMemory()} | caches: images {cache.ImageCacheCount} / modules {cache.ModuleCacheCount + cache.ProcessModuleCacheCount}"));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        sb.AppendLine(Loc.L($"配置：{Path.Combine(localAppData, "MuSync", "config.json")}", $"Config: {Path.Combine(localAppData, "MuSync", "config.json")}"));
        var logsDir = Path.Combine(localAppData, "MuSync", "logs");
        var latestLog = TryGet(
            () => new DirectoryInfo(logsDir).GetFiles("MuSync-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.Name ?? Loc.L("无", "None"),
            Loc.L("无", "None"));
        sb.AppendLine(Loc.L($"日志：{logsDir} ｜ 最新：{latestLog}", $"Logs: {logsDir} | latest: {latestLog}"));
    }

    private static string TryGet(Func<string> getter, string fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }
}
