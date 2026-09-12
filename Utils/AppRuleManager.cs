using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MuSync.Models;
namespace MuSync.Utils;

/// <summary>
/// 程序同步规则管理：前台匹配、显示策略判定、自动发现新程序、"运行即显示"扫描。
/// </summary>
internal static class AppRuleManager
{
    private static readonly object SyncRoot = new();
    private static DateTime _lastAlwaysScan = DateTime.MinValue;
    private static string? _cachedAlwaysExe;
    private static readonly TimeSpan AlwaysScanInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 匹配前台程序。
    /// Rule=命中的生效规则；IsIgnored=true 表示"忽略类"（内置忽略或用户设忽略），
    /// 调用方应保持现状不动（不清状态、不切换）。
    /// </summary>
    public static (AppRule? Rule, bool IsIgnored) Match(ForegroundAppInfo app, ConfigData config)
    {
        if (AppClassifier.IsBuiltInIgnored(app.ExeName)) return (null, true);
        var rule = config.Apps.FirstOrDefault(r =>
            r.ExeName.Equals(app.ExeName, StringComparison.OrdinalIgnoreCase));
        if (rule != null)
        {
            if (rule.Category == AppCategory.Ignore) return (null, true);
            return rule.Enabled ? (rule, false) : (null, false);
        }
        // 自动发现：本地分类器给初步分类与显示名
        var (category, suggestedName, fromDictionary) = AppClassifier.Suggest(
            app.ExeName, app.WindowTitle, app.IsFullscreen);
        var newRule = new AppRule
        {
            ExeName = app.ExeName,
            DisplayName = suggestedName,
            Category = category,
            // 字典命中（高置信）自动生效；其余进入"待确认"（默认禁用，等用户确认）
            Enabled = fromDictionary,
            IsUserConfirmed = false
        };
        config.Apps.Add(newRule);
        Configurations.Instance.Save();
        Logger.Info(fromDictionary
            ? $"[AppSync] 自动识别程序 {app.ExeName} -> {suggestedName}（{category}）"
            : $"[AppSync] 发现新程序 {app.ExeName}（建议 {category}/{suggestedName}），待用户确认");
        return newRule.Enabled && category != AppCategory.Ignore
            ? (newRule, false)
            : (null, false);
    }

    /// <summary>该规则当前是否允许显示（分类策略 + 单程序覆盖 + 总开关）。</summary>
    public static bool IsAllowedToDisplay(AppRule rule, ConfigData config)
    {
        if (!config.AppSyncEnabled) return false;
        switch (rule.Override)
        {
            case AppSyncOverride.ForceOn:
                return true;
            case AppSyncOverride.ForceOff:
                return false;
        }
        if (rule.Category == AppCategory.Ignore) return false;
        if (rule.Category == AppCategory.Game) return true;
        return config.SyncNonGameApps;
    }

    public static string DisplayNameOf(AppRule rule)
    {
        return string.IsNullOrWhiteSpace(rule.DisplayName)
            ? Path.GetFileNameWithoutExtension(rule.ExeName)
            : rule.DisplayName.Trim();
    }

    /// <summary>获取正在运行进程的可执行文件路径（用于提取图标）。</summary>
    public static string GetRunningProcessPath(string exeName)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(exeName);
            using var process = Process.GetProcessesByName(processName).FirstOrDefault();
            return process?.MainModule?.FileName ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>查找"运行即显示"模式下正在运行的规则（每 5 秒扫描一次进程，开销可控）。</summary>
    public static AppRule? FindRunningAlwaysRule(ConfigData config, DateTime nowUtc)
    {
        lock (SyncRoot)
        {
            if (nowUtc - _lastAlwaysScan >= AlwaysScanInterval)
            {
                _lastAlwaysScan = nowUtc;
                _cachedAlwaysExe = null;
                foreach (var rule in config.Apps)
                {
                    if (!rule.Enabled || rule.Mode != AppSyncMode.Always) continue;
                    if (!IsAllowedToDisplay(rule, config)) continue;
                    var processName = Path.GetFileNameWithoutExtension(rule.ExeName);
                    try
                    {
                        if (Process.GetProcessesByName(processName).Length > 0)
                        {
                            _cachedAlwaysExe = rule.ExeName;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AppSync] 进程检查失败 {processName}: {ex.Message}");
                    }
                }
            }
            if (_cachedAlwaysExe == null) return null;
            return config.Apps.FirstOrDefault(r =>
                r.ExeName.Equals(_cachedAlwaysExe, StringComparison.OrdinalIgnoreCase));
        }
    }
}
