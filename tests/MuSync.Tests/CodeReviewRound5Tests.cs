using System.Drawing;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 第五轮代码检查（2026-10-28）里可单测的一项：
/// #14 自动识别（已启用但未手动确认）的程序不能显示成"待确认 / 未检测到"。
/// （R5 数据源启停调度、#15 LX 配置重读属于运行期时序，靠代码审查与真机观察。）
/// </summary>
public class CodeReviewRound5Tests
{
    private static AppSyncStatusResult Evaluate(
        string? activeAppDisplay,
        bool hasActiveAppRule,
        bool confirmed,
        bool enabled) =>
        AppSyncStatus.Evaluate(
            syncEnabled: true,
            gameRunning: false,
            pauseWhenPlayingGame: false,
            manualPause: false,
            activeAppDisplay: activeAppDisplay,
            hasActiveAppRule: hasActiveAppRule,
            activeAppRuleConfirmed: confirmed,
            activeAppRuleEnabled: enabled);

    [Fact]
    public void AutoDetectedEnabledApp_ShowsAsDisplayed_NotPending()
    {
        // 内置字典高置信命中：规则 Enabled=true、IsUserConfirmed=false，实际已经在推送
        var result = Evaluate(activeAppDisplay: "卡拉彼丘", hasActiveAppRule: true, confirmed: false, enabled: true);

        Assert.Equal(AppSyncPanelState.Displayed, result.State);
        Assert.DoesNotContain("待确认", result.Text);
    }

    [Fact]
    public void RecognizedButNotEnabled_StillPending()
    {
        // 识别到候选但没启用（不会推送）→ 仍然要提示用户确认
        var result = Evaluate(activeAppDisplay: null, hasActiveAppRule: true, confirmed: false, enabled: false);

        Assert.Equal(AppSyncPanelState.PendingConfirmation, result.State);
    }

    [Fact]
    public void ConfirmedApp_IsDisplayed()
    {
        var result = Evaluate(activeAppDisplay: "卡拉彼丘", hasActiveAppRule: true, confirmed: true, enabled: true);

        Assert.Equal(AppSyncPanelState.Displayed, result.State);
    }

    [Fact]
    public void NoRuleAtAll_IsNotDetected()
    {
        var result = Evaluate(activeAppDisplay: null, hasActiveAppRule: false, confirmed: false, enabled: false);

        Assert.Equal(AppSyncPanelState.NotDetected, result.State);
    }

    [Fact]
    public void DotColorIsDefinedForEveryState()
    {
        Assert.NotEqual(Color.Empty, AppSyncStatus.DotColorOf(AppSyncPanelState.Displayed));
        Assert.NotEqual(Color.Empty, AppSyncStatus.DotColorOf(AppSyncPanelState.PendingConfirmation));
        Assert.NotEqual(Color.Empty, AppSyncStatus.DotColorOf(AppSyncPanelState.NotDetected));
    }
}
