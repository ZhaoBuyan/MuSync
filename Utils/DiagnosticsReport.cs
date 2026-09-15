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
        sb.AppendLine($"—— MuSync 诊断信息（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）——");
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
        var buildTime = TryGet(() => File.GetLastWriteTime(exePath!).ToString("yyyy-MM-dd HH:mm"), "未知");
        sb.AppendLine($"版本：{UpdateChecker.GetCurrentVersionText()}（{exeName}，构建于 {buildTime}）");
        var uptime = DateTime.Now - Program.StartedAt;
        var uptimeText = uptime.TotalHours >= 1
            ? $"{(int)uptime.TotalHours} 小时 {uptime.Minutes} 分"
            : $"{Math.Max(1, (int)uptime.TotalMinutes)} 分钟";
        sb.AppendLine($"运行：启动于 {Program.StartedAt:yyyy-MM-dd HH:mm}，已运行 {uptimeText}");
    }

    /// <summary>Steam 连接 / 同步 / 更新状态。</summary>
    private static void AppendSteamInfo(StringBuilder sb)
    {
        var session = Program.GetSessionManager();
        var status = session switch
        {
            null => "未初始化",
            { IsLoggedOn: true } => "已登录",
            { IsConnected: true } => "已连接（未登录）",
            _ => "连接中"
        };
        var protocol = session is null ? "--" : session.ConnectionProtocolText;
        var error = string.IsNullOrEmpty(session?.LoginError) ? "无" : session!.LoginError!;
        sb.AppendLine($"Steam：{status} ｜ 协议 {protocol} ｜ 错误：{error}");

        var manager = Program.GetSteamManager();
        var config = Configurations.Instance.Settings;
        var syncState = manager?.ManualPause == true
            ? "已手动暂停"
            : manager?.IsRealGameActive == true && config.PauseWhenPlayingGame
                ? "真实游戏中暂停"
                : "正常";
        var update = Program.PendingUpdate is { } pending ? $"已发现 {pending.Tag}" : "未发现";
        sb.AppendLine($"同步：{syncState} ｜ 更新：{update}");
    }

    /// <summary>四个播放器的运行 / 播放 / 错误状态。</summary>
    private static void AppendPlayerInfo(StringBuilder sb)
    {
        var rpc = Program.GetRpcManager();
        if (rpc is null)
        {
            sb.AppendLine("播放器：未初始化");
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
                RpcManager.ErrorCode.PermissionDenied => "⚠️ 需要管理员运行",
                RpcManager.ErrorCode.DllNotFound => "⚠️ 播放器组件未加载",
                RpcManager.ErrorCode.VersionNotSupported => "⚠️ 版本不支持 / 特征码失效",
                _ => "⚠️ 未知错误"
            };
        }
        if (!status.Running) return "未运行";
        if (string.IsNullOrEmpty(status.Title)) return $"运行中（未在播放）{ActiveMark(status)}";
        var song = string.IsNullOrEmpty(status.Artists) ? status.Title : $"{status.Title} - {status.Artists}";
        return $"{(status.Pause ? "已暂停" : "正在播放")}「{song}」{ActiveMark(status)}";
    }

    private static string ActiveMark(PlayerStatus status) => status.IsActive ? "（当前活跃）" : "";

    /// <summary>程序同步状态。</summary>
    private static void AppendAppSyncInfo(StringBuilder sb)
    {
        var config = Configurations.Instance.Settings;
        if (!config.AppSyncEnabled)
        {
            sb.AppendLine("程序同步：关闭");
            return;
        }
        var scope = config.SyncNonGameApps ? "含非游戏程序" : "仅游戏";
        var current = Program.GetRpcManager()?.GetActiveAppDisplay();
        sb.AppendLine($"程序同步：开启（{scope}）｜ 当前：{current ?? "（无）"}");
    }

    /// <summary>关键设置摘要。</summary>
    private static void AppendSettingsSummary(StringBuilder sb)
    {
        var config = Configurations.Instance.Settings;
        var speed = config.SyncSpeed switch
        {
            SyncSpeedLevel.Fast => "快速 0.25s",
            SyncSpeedLevel.Economic => "省流 1s",
            _ => "标准 0.5s"
        };
        sb.AppendLine(
            $"音乐同步：{(config.MusicSyncEnabled ? "开启" : "关闭")} ｜ 暂停时隐藏：{(config.HideMusicWhenPaused ? "开" : "关")} ｜ 频率：{speed}");
        sb.AppendLine(
            $"启动：开机自启 {(config.AutoStart ? "开" : "关")} ｜ 关闭最小化到托盘 {(config.CloseToTray ? "开" : "关")} ｜ 静默启动 {(config.StartInTray ? "开" : "关")}");
        var appearance = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.AppearanceBackgroundImage)) appearance.Add("背景图");
        if (!string.IsNullOrWhiteSpace(config.AppearanceFontFamily) || config.AppearanceFontSize > 0) appearance.Add("字体");
        if (config.AppearanceTitleColorArgb is not null || config.AppearanceSongColorArgb is not null) appearance.Add("颜色");
        sb.AppendLine($"外观自定义：{(appearance.Count > 0 ? string.Join(" / ", appearance) : "无")}");
    }

    /// <summary>系统 / 内存 / 缓存 / 路径。</summary>
    private static void AppendEnvironmentInfo(StringBuilder sb)
    {
        var monitors = TryGet(() => Screen.AllScreens.Length.ToString(), "--");
        sb.AppendLine(
            $"系统：{Environment.OSVersion.VersionString} ｜ {(Environment.Is64BitOperatingSystem ? "64 位" : "32 位")} ｜ 显示器 {monitors} 台");
        var memory = PerformanceMonitor.GetMemoryInfo();
        var cache = PerformanceMonitor.GetCacheStatistics();
        sb.AppendLine(
            $"内存：工作集 {memory.GetFormattedWorkingSet()} ｜ 私有 {memory.GetFormattedPrivateMemory()} ｜ GC {memory.GetFormattedGcMemory()} ｜ 缓存：图片 {cache.ImageCacheCount} / 模块 {cache.ModuleCacheCount + cache.ProcessModuleCacheCount}");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        sb.AppendLine($"配置：{Path.Combine(localAppData, "MuSync", "config.json")}");
        var logsDir = Path.Combine(localAppData, "MuSync", "logs");
        var latestLog = TryGet(
            () => new DirectoryInfo(logsDir).GetFiles("MuSync-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.Name ?? "无",
            "无");
        sb.AppendLine($"日志：{logsDir} ｜ 最新：{latestLog}");
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
