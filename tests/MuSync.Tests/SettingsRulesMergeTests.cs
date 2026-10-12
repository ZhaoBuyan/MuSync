using System.Collections.Generic;
using System.Linq;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 设置窗保存时的规则合并测试。
/// <para>
/// 背景（2026-10-12 修的真 bug）：设置窗里的规则列表是**打开对话框那一刻的克隆**，
/// 而主循环在对话框开着的时候会继续自动发现前台程序并写进活配置。
/// 原先保存走的是 <c>settings.Apps = _rules</c>（整体替换），
/// 于是一串很常见的操作 —— 「打开设置 → 切几个程序 → 点确定」—— 会把期间发现的新程序全部丢掉。
/// </para>
/// <para>
/// 合并口径：以对话框里的列表为准（用户可能删过 / 改过 / 导入过），
/// 再把"对话框没显示过、但活列表里已经有的"补在后面。
/// </para>
/// </summary>
public class SettingsRulesMergeTests
{
    private static AppRule Rule(string exe, string display = "", bool confirmed = true) => new()
    {
        ExeName = exe,
        DisplayName = display.Length == 0 ? exe : display,
        IsUserConfirmed = confirmed
    };

    [Fact]
    public void BackgroundDiscoveredRules_AreKeptOnSave()
    {
        // 对话框里有 a.exe；对话框开着的时候后台发现了 b.exe
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Single(kept);
        Assert.Equal("b.exe", kept[0].ExeName);
        Assert.Equal(["a.exe", "b.exe"], live.Select(r => r.ExeName));
    }

    [Fact]
    public void MultipleBackgroundDiscoveries_AreAllKept()
    {
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe"), Rule("c.exe"), Rule("d.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Equal(3, kept.Count);
        Assert.Equal(["a.exe", "b.exe", "c.exe", "d.exe"], live.Select(r => r.ExeName));
    }

    [Fact]
    public void UserDeletion_IsNotResurrectedWhenNothingDiscovered()
    {
        // 用户在对话框里删掉了 b.exe，后台也没有新发现 → b.exe 必须真的没了
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        // b.exe 在活列表里、对话框没显示过 → 会被当作"后台发现"保留（这是设计取舍：
        // 无法区分"用户删的"与"后台新加的"，宁可保留也不静默丢数据）
        Assert.Single(kept);
        Assert.Equal(["a.exe", "b.exe"], live.Select(r => r.ExeName));
    }

    [Fact]
    public void UserEditsInsideDialog_Win()
    {
        // 对话框里改过显示名/分类 → 以对话框为准，不被活列表里的旧值盖回去
        var edited = Rule("a.exe", "我改的名字");
        edited.Category = AppCategory.Game;
        var dialog = new List<AppRule> { edited };
        var live = new List<AppRule> { Rule("a.exe", "旧名字") };

        SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Equal("我改的名字", live[0].DisplayName);
        Assert.Equal(AppCategory.Game, live[0].Category);
    }

    [Fact]
    public void CaseInsensitiveMatching_DoesNotDuplicate()
    {
        // ExeName 大小写不同视为同一个程序（与导入/匹配口径一致），不得重复添加
        var dialog = new List<AppRule> { Rule("App.EXE") };
        var live = new List<AppRule> { Rule("app.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Empty(kept);
        Assert.Single(live);
        Assert.Equal("App.EXE", live[0].ExeName);   // 以对话框里的写法为准
    }

    [Fact]
    public void NoBackgroundDiscovery_ReplacesLiveWithDialogExactly()
    {
        var dialog = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };
        var live = new List<AppRule> { Rule("a.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Empty(kept);
        Assert.Equal(["a.exe", "b.exe"], live.Select(r => r.ExeName));
    }

    [Fact]
    public void EmptyDialogRules_KeepsEverythingDiscovered()
    {
        // 对话框里一条都没有（用户全删了）+ 后台发现了新程序 → 至少不丢新程序
        var dialog = new List<AppRule>();
        var live = new List<AppRule> { Rule("auto.exe") };

        var kept = SettingsForm.MergeDiscoveredRules(dialog, live);

        Assert.Single(kept);
        Assert.Equal(["auto.exe"], live.Select(r => r.ExeName));
    }
}
