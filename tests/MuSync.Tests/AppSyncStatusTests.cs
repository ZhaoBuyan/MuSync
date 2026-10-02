using System;
using System.Collections.Generic;
using System.Drawing;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 程序同步面板状态判定的参数化测试：6 种状态各自命中，且优先级与配色口径固定。
/// 判定是纯函数（<see cref="AppSyncStatus.Evaluate"/>），界面只负责把结果画出来。
/// </summary>
public class AppSyncStatusTests
{
    private static AppSyncStatusResult Evaluate(
        bool syncEnabled = true,
        bool gameRunning = false,
        bool pauseWhenPlayingGame = true,
        bool manualPause = false,
        string? activeAppDisplay = null,
        bool hasActiveAppRule = false,
        bool activeAppRuleConfirmed = true) =>
        AppSyncStatus.Evaluate(syncEnabled, gameRunning, pauseWhenPlayingGame, manualPause,
            activeAppDisplay, hasActiveAppRule, activeAppRuleConfirmed);

    [Fact]
    public void SyncDisabled_ReturnsDisabledWithNeutralColor()
    {
        var result = Evaluate(syncEnabled: false, activeAppDisplay: "Steam 游戏");

        Assert.Equal(AppSyncPanelState.Disabled, result.State);
        Assert.Equal(Color.Gray, result.Color);
        Assert.Equal("程序同步未启用", result.Text);
    }

    [Fact]
    public void GameRunningWithPauseEnabled_ReturnsPausedByGame()
    {
        var result = Evaluate(gameRunning: true, activeAppDisplay: "Visual Studio Code");

        Assert.Equal(AppSyncPanelState.PausedByGame, result.State);
        Assert.Equal(Color.FromArgb(212, 148, 30), result.Color);
        Assert.Contains("游戏", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GameRunningButPauseDisabled_FallsThroughToDisplayed()
    {
        // 没勾「玩游戏时暂停」时，游戏在跑不影响程序同步
        var result = Evaluate(gameRunning: true, pauseWhenPlayingGame: false, activeAppDisplay: "Visual Studio Code");

        Assert.Equal(AppSyncPanelState.Displayed, result.State);
    }

    [Fact]
    public void ManualPause_ReturnsPausedManual()
    {
        var result = Evaluate(manualPause: true, activeAppDisplay: "Visual Studio Code");

        Assert.Equal(AppSyncPanelState.PausedManual, result.State);
        Assert.Equal(Color.FromArgb(212, 148, 30), result.Color);
        Assert.Contains("手动", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoActiveApp_ReturnsNotDetected()
    {
        var result = Evaluate(activeAppDisplay: null);

        Assert.Equal(AppSyncPanelState.NotDetected, result.State);
        Assert.Equal(Color.Gray, result.Color);
    }

    [Fact]
    public void PendingUnconfirmedRule_ReturnsPendingConfirmation()
    {
        var result = Evaluate(activeAppDisplay: null, hasActiveAppRule: true, activeAppRuleConfirmed: false);

        Assert.Equal(AppSyncPanelState.PendingConfirmation, result.State);
        Assert.Equal(Color.FromArgb(30, 120, 215), result.Color);
        Assert.Contains("待确认", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveAppWithConfirmedRule_ReturnsDisplayed()
    {
        var result = Evaluate(activeAppDisplay: "Visual Studio Code", hasActiveAppRule: true, activeAppRuleConfirmed: true);

        Assert.Equal(AppSyncPanelState.Displayed, result.State);
        Assert.Equal(Color.FromArgb(32, 150, 60), result.Color);
    }

    [Fact]
    public void ActiveAppWithoutRule_StillDisplayed()
    {
        // 运行即显示等路径可能没有规则对象，不能因此判成「待确认」
        var result = Evaluate(activeAppDisplay: "Some App");

        Assert.Equal(AppSyncPanelState.Displayed, result.State);
    }

    [Theory]
    [InlineData(true, true, true, true)]     // 总开关关 → 永远 Disabled（优先级最高）
    [InlineData(true, true, true, false)]    // 游戏暂停 + 手动暂停 → 游戏优先
    public void Precedence_IsFixed(
        bool expectDisabled, bool gameRunning, bool pauseWhenPlayingGame, bool manualPause)
    {
        var result = Evaluate(
            syncEnabled: !expectDisabled,
            gameRunning: gameRunning,
            pauseWhenPlayingGame: pauseWhenPlayingGame,
            manualPause: manualPause,
            activeAppDisplay: "Visual Studio Code");

        var expected = expectDisabled
            ? AppSyncPanelState.Disabled
            : AppSyncPanelState.PausedByGame;
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void PauseStatesWinOverNoAppDetected()
    {
        // 暂停时用户更需要知道「为什么没显示」，而不是「没检测到程序」
        var game = Evaluate(gameRunning: true, activeAppDisplay: null);
        var manual = Evaluate(manualPause: true, activeAppDisplay: null);

        Assert.Equal(AppSyncPanelState.PausedByGame, game.State);
        Assert.Equal(AppSyncPanelState.PausedManual, manual.State);
    }

    [Fact]
    public void EveryStateHasTextAndDistinctColor()
    {
        var results = new List<AppSyncStatusResult>
        {
            Evaluate(syncEnabled: false),
            Evaluate(gameRunning: true, activeAppDisplay: "App"),
            Evaluate(manualPause: true, activeAppDisplay: "App"),
            Evaluate(),
            Evaluate(hasActiveAppRule: true, activeAppRuleConfirmed: false),
            Evaluate(activeAppDisplay: "App")
        };

        foreach (var result in results)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Text), $"{result.State} 缺文案");
            Assert.Equal(AppSyncStatus.DotColorOf(result.State), result.Color);
        }
        // 四类视觉分组必须互不相同：灰（未启用/待机）、两黄合一、蓝（待确认）、绿（已显示）
        Assert.Equal(results[0].Color, results[3].Color);    // 未启用 与 未检测到 同为灰
        Assert.Equal(results[1].Color, results[2].Color);    // 两类暂停同色，文案区分原因
        Assert.NotEqual(results[1].Color, results[4].Color);
        Assert.NotEqual(results[4].Color, results[5].Color);
        Assert.NotEqual(results[3].Color, results[5].Color);
    }

    [Fact]
    public void CategoryText_CoversAllCategories()
    {
        foreach (var category in Enum.GetValues<MuSync.Models.AppCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AppSyncStatus.CategoryTextOf(category)),
                $"{category} 缺分类显示名");
        }
    }
}
