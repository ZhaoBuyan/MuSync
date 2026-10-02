using System.Drawing;
using MuSync.Models;

namespace MuSync.Utils;

/// <summary>
/// 程序同步面板的状态判定结果：状态枚举 + 展示文案 + 语义色。
/// 判定逻辑与界面分离，<see cref="AppSyncStatus.Evaluate"/> 是纯函数，便于参数化单测。
/// </summary>
internal enum AppSyncPanelState
{
    /// <summary>程序同步总开关关闭（面板整体隐藏，此态只在诊断里出现）。</summary>
    Disabled,
    /// <summary>检测到真实 Steam 游戏在运行，且设置里勾了「玩游戏时暂停」。</summary>
    PausedByGame,
    /// <summary>托盘「暂停同步」处手动暂停。</summary>
    PausedManual,
    /// <summary>没有前台/后台命中的程序。</summary>
    NotDetected,
    /// <summary>识别到了程序，但规则还没被用户确认（不会推送到 Steam）。</summary>
    PendingConfirmation,
    /// <summary>已作为当前 Steam 状态显示。</summary>
    Displayed
}

/// <summary>判定结果：状态 + 副行文案 + 语义色（<see cref="AppSyncStatus.DotColorOf"/>）。</summary>
internal readonly record struct AppSyncStatusResult(AppSyncPanelState State, string Text)
{
    /// <summary>该状态的颜色（界面用它给状态点与文字上色）。</summary>
    public Color Color => AppSyncStatus.DotColorOf(State);
}

/// <summary>
/// 程序同步面板「状态点」的状态判定。
/// 这里是本项目 2026-09-30 决定的补状态方案：原先面板只有「未检测到程序（灰）」「已显示（绿）」两种，
/// 而托盘早就在显示暂停态了，主界面掉队——本类把两边口径统一到一处判定上。
/// </summary>
internal static class AppSyncStatus
{
    /// <summary>
    /// 判定当前应显示的状态。优先级（先命中先返回）：
    /// 总开关关闭 → 游戏暂停 → 手动暂停 → 待确认 → 无程序 → 已显示。
    /// ⚠️ 两条顺序约束（v0.5.0 实测踩过）：
    /// ① 暂停类必须优先于「有无程序」：暂停时用户更需要知道"为什么没显示"，而不是"没检测到程序"。
    /// ② 「待确认」必须优先于「无程序」：待确认程序**不会**进入 activeAppDisplay（它被 Enabled=false 挡在推送之外），
    ///    若先判 activeAppDisplay 为空，待确认状态就永远不可达（蓝点出不来）。
    /// </summary>
    public static AppSyncStatusResult Evaluate(
        bool syncEnabled,
        bool gameRunning,
        bool pauseWhenPlayingGame,
        bool manualPause,
        string? activeAppDisplay,
        bool hasActiveAppRule,
        bool activeAppRuleConfirmed)
    {
        if (!syncEnabled)
        {
            return new AppSyncStatusResult(AppSyncPanelState.Disabled,
                Loc.L("程序同步未启用", "App sync is disabled"));
        }
        if (gameRunning && pauseWhenPlayingGame)
        {
            return new AppSyncStatusResult(AppSyncPanelState.PausedByGame,
                Loc.L("检测到游戏运行，已自动暂停", "Game detected — paused automatically"));
        }
        if (manualPause)
        {
            return new AppSyncStatusResult(AppSyncPanelState.PausedManual,
                Loc.L("同步已手动暂停", "Sync paused manually"));
        }
        if (hasActiveAppRule && !activeAppRuleConfirmed)
        {
            return new AppSyncStatusResult(AppSyncPanelState.PendingConfirmation,
                Loc.L("待确认：请在设置 → 程序中确认分类", "Pending — confirm it in Settings → Apps"));
        }
        if (string.IsNullOrEmpty(activeAppDisplay))
        {
            return new AppSyncStatusResult(AppSyncPanelState.NotDetected,
                Loc.L("切到已启用的程序后将在 Steam 中显示", "Will show on Steam when you switch to an enabled app"));
        }
        return new AppSyncStatusResult(AppSyncPanelState.Displayed,
            Loc.L("已作为当前 Steam 状态显示", "Shown as your current Steam status"));
    }

    /// <summary>
    /// 状态语义色：黄＝暂停（两类暂停同色，文案区分原因）、蓝＝待确认、绿＝已显示、灰＝待机/未启用。
    /// 不引入新配色，全部取自既有的语义色。
    /// </summary>
    public static Color DotColorOf(AppSyncPanelState state) => state switch
    {
        AppSyncPanelState.PausedByGame => Color.FromArgb(212, 148, 30),
        AppSyncPanelState.PausedManual => Color.FromArgb(212, 148, 30),
        AppSyncPanelState.PendingConfirmation => Color.FromArgb(30, 120, 215),
        AppSyncPanelState.Displayed => Color.FromArgb(32, 150, 60),
        _ => Color.Gray
    };

    /// <summary>分类显示名（与 RpcManager.GetActiveAppCategoryText 同一口径；待确认程序没有「生效规则」时用）。</summary>
    public static string CategoryTextOf(AppCategory category) => category switch
    {
        AppCategory.Game => Loc.L("游戏", "Game"),
        AppCategory.Work => Loc.L("工作", "Work"),
        AppCategory.Media => Loc.L("媒体", "Media"),
        AppCategory.Social => Loc.L("社交", "Social"),
        AppCategory.Other => Loc.L("其他", "Other"),
        AppCategory.Ignore => Loc.L("忽略", "Ignore"),
        _ => ""
    };
}
