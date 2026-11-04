using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using System.Windows.Forms;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 设置窗保存时的规则合并测试。
/// <para>
/// 背景：设置窗里的规则列表是**打开对话框那一刻的克隆**，
/// 而主循环在对话框开着的时候会继续自动发现前台程序并写进活配置。
/// 原先保存走的是 <c>settings.Apps = _rules</c>（整体替换），
/// 于是一串很常见的操作 —— 「打开设置 → 切几个程序 → 点确定」—— 会把期间发现的新程序全部丢掉。
/// </para>
/// <para>
/// 合并口径：以对话框里的列表为准（用户可能删过 / 改过 / 导入过），
/// 再把"对话框没显示过、但活列表里已经有的"补在后面。
/// </para>
/// <para>
/// ⚠️ 合并函数**不修改入参**：它曾经直接在活列表上 Clear/AddRange，而这发生在"正在枚举该列表"的
/// foreach 内部；轮询线程还会在后台向同一个列表 Add，枚举期间被改写会抛 InvalidOperationException。
/// 现在由调用方先取快照、再用返回的合并结果整体替换，并在测试里钉住"入参不被改动"。
/// </para>
/// </summary>
[Collection(LanguageSensitiveCollection.Name)]
public class SettingsRulesMergeTests
{
    private static AppRule Rule(string exe, string display = "", bool confirmed = true) => new()
    {
        ExeName = exe,
        DisplayName = display.Length == 0 ? exe : display,
        IsUserConfirmed = confirmed
    };

    private static string[] Names(IEnumerable<AppRule> rules) => rules.Select(r => r.ExeName).ToArray();

    [Fact]
    public void ActualDeleteActionSurvivesSaveAndReloadClearsDraftDeletions()
    {
        var settings = Configurations.Instance.Settings;
        var previous = settings.Apps;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            settings.Apps = [Rule("a.exe"), Rule("b.exe")];
            using var view = new SettingsView();
            var type = typeof(SettingsView);
            var grid = (DataGridView)type.GetField("_rulesGrid", flags)!.GetValue(view)!;
            grid.ClearSelection();
            grid.Rows[1].Selected = true;
            type.GetMethod("RemoveButton_Click", flags)!.Invoke(view, [null, EventArgs.Empty]);
            settings.Apps.Add(Rule("new.exe"));
            var merge = type.GetMethod("MergeBackgroundDiscoveredRules", flags)!;
            merge.Invoke(view, [settings]);
            Assert.Equal(["a.exe", "new.exe"], Names(settings.Apps));
            merge.Invoke(view, [settings]);
            Assert.Equal(["a.exe", "new.exe"], Names(settings.Apps));

            // Discard/reload starts a new draft, so an earlier removal must not leak into it.
            settings.Apps = [Rule("b.exe")];
            type.GetMethod("LoadSettings", flags)!.Invoke(view, null);
            merge.Invoke(view, [settings]);
            Assert.Equal(["b.exe"], Names(settings.Apps));
            var removed = (HashSet<string>)type.GetField("_removedRuleNames", flags)!.GetValue(view)!;
            Assert.Empty(removed);
        }
        finally { settings.Apps = previous; }
    }

    [Fact]
    public void BackgroundDiscoveredRules_AreKeptOnSave()
    {
        // 对话框里有 a.exe；对话框开着的时候后台发现了 b.exe
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Single(kept);
        Assert.Equal("b.exe", kept[0].ExeName);
        Assert.Equal(["a.exe", "b.exe"], Names(merged));
    }

    [Fact]
    public void MultipleBackgroundDiscoveries_AreAllKept()
    {
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe"), Rule("c.exe"), Rule("d.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Equal(3, kept.Count);
        Assert.Equal(["a.exe", "b.exe", "c.exe", "d.exe"], Names(merged));
    }

    [Fact]
    public void UserDeletion_IsNotResurrectedWhenNothingDiscovered()
    {
        // 用户在对话框里删掉了 b.exe，后台也没有新发现 → b.exe 必须真的没了
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live, ["B.EXE"]);

        Assert.Empty(kept);
        Assert.Equal(["a.exe"], Names(merged));
    }

    [Fact]
    public void DeletedRulesStayDeletedAcrossSavesWithoutLosingNewDiscoveries()
    {
        var dialog = new[] { Rule("a.exe") };
        var (first, discovered) = SettingsView.MergeDiscoveredRules(dialog,
            new[] { Rule("a.exe"), Rule("b.exe"), Rule("new.exe") }, ["b.exe"]);
        Assert.Equal(["new.exe"], Names(discovered));
        var (second, _) = SettingsView.MergeDiscoveredRules(dialog, first, ["b.exe"]);
        Assert.Equal(["a.exe", "new.exe"], Names(second));
    }

    [Fact]
    public void ExplicitlyAddingRemovedRuleAgainKeepsTheNewEdits()
    {
        var edited = Rule("b.exe", "Added again");
        var (merged, kept) = SettingsView.MergeDiscoveredRules(
            new[] { edited }, new[] { Rule("B.EXE") }, ["b.exe"]);
        Assert.Empty(kept);
        Assert.Same(edited, Assert.Single(merged));
    }

    [Fact]
    public void UserEditsInsideDialog_Win()
    {
        // 对话框里改过显示名/分类 → 以对话框为准，不被活列表里的旧值盖回去
        var edited = Rule("a.exe", "我改的名字");
        edited.Category = AppCategory.Game;
        var dialog = new List<AppRule> { edited };
        var live = new List<AppRule> { Rule("a.exe", "旧名字") };

        var (merged, _) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Equal("我改的名字", merged[0].DisplayName);
        Assert.Equal(AppCategory.Game, merged[0].Category);
    }

    [Fact]
    public void CaseInsensitiveMatching_DoesNotDuplicate()
    {
        // ExeName 大小写不同视为同一个程序（与导入/匹配口径一致），不得重复添加
        var dialog = new List<AppRule> { Rule("App.EXE") };
        var live = new List<AppRule> { Rule("app.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Empty(kept);
        Assert.Single(merged);
        Assert.Equal("App.EXE", merged[0].ExeName);   // 以对话框里的写法为准
    }

    [Fact]
    public void NoBackgroundDiscovery_ReplacesLiveWithDialogExactly()
    {
        var dialog = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };
        var live = new List<AppRule> { Rule("a.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Empty(kept);
        Assert.Equal(["a.exe", "b.exe"], Names(merged));
    }

    [Fact]
    public void EmptyDialogRules_KeepsEverythingDiscovered()
    {
        // 对话框里一条都没有（用户全删了）+ 后台发现了新程序 → 至少不丢新程序
        var dialog = new List<AppRule>();
        var live = new List<AppRule> { Rule("auto.exe") };

        var (merged, kept) = SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Single(kept);
        Assert.Equal(["auto.exe"], Names(merged));
    }

    [Fact]
    public void Merge_DoesNotModifyItsInputs()
    {
        // 关键回归：合并过程不能改动入参 —— 否则"枚举期间被改写"的异常会重现，
        // 而且调用方拿到的快照会被悄悄改写。
        var dialog = new List<AppRule> { Rule("a.exe") };
        var live = new List<AppRule> { Rule("a.exe"), Rule("b.exe") };
        var dialogBefore = Names(dialog);
        var liveBefore = Names(live);

        SettingsView.MergeDiscoveredRules(dialog, live);

        Assert.Equal(dialogBefore, Names(dialog));
        Assert.Equal(liveBefore, Names(live));
    }

    [Fact]
    public void Merge_AcceptsSnapshotArray()
    {
        // 调用方传的是 ToArray() 快照（只读接口），合并必须照样工作
        var (merged, kept) = SettingsView.MergeDiscoveredRules(
            new[] { Rule("a.exe") },
            new[] { Rule("a.exe"), Rule("b.exe") });

        Assert.Single(kept);
        Assert.Equal(["a.exe", "b.exe"], Names(merged));
    }
}
