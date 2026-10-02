using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 英文界面漏中文守卫：把语言切到 English，真实构造窗体，遍历所有可见文案断言不含中日韩字符。
/// 这条守卫的由来：`SettingsForm.cs` 的「变量：{app}…」提示行曾是硬编码中文，英文界面直接露中文。
/// 与源码正则扫描相比，走真实控件树天然跨行感知——`new Label { Text = Loc.L(第一行, 第二行) }`
/// 这类跨行写法只要真的取了英文就不会误报（源码扫描踩过的坑）。
/// ⚠️ 本类会临时改全局语言，必须与其它语言敏感测试串行（见 <see cref="LanguageSensitiveCollection"/>）。
/// </summary>
[Collection(LanguageSensitiveCollection.Name)]
public class EnglishUiGuardTests : IDisposable
{
    private readonly int _originalLanguage;

    public EnglishUiGuardTests()
    {
        _originalLanguage = Configurations.Instance.Settings.Language;
        Configurations.Instance.Settings.Language = 1;   // English
    }

    public void Dispose()
    {
        Configurations.Instance.Settings.Language = _originalLanguage;
    }

    /// <summary>任一 CJK 统一表意文字 / 扩展 A / 兼容表意文字 = 英文界面漏中文。</summary>
    private static bool HasChinese(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // 路径类文案跳过：诊断行会显示配置目录，Windows 用户名本身可能是中文
        // （例如 C:\Users\赵不言\AppData\Local\MuSync），那是用户自己的名字，不是漏译。
        if (text.Contains('\\', StringComparison.Ordinal) || text.Contains('/', StringComparison.Ordinal)) return false;
        foreach (var ch in text)
        {
            if ((ch >= '\u4E00' && ch <= '\u9FFF') ||
                (ch >= '\u3400' && ch <= '\u4DBF') ||
                (ch >= '\uF900' && ch <= '\uFAFF'))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>收集一棵控件树的全部可见文案（含 MenuStrip / ContextMenuStrip 菜单项与 TabPage / GroupBox）。</summary>
    private static List<string> CollectVisibleText(Control root)
    {
        var found = new List<string>();
        var pending = new Stack<Control>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var control = pending.Pop();
            if (!string.IsNullOrWhiteSpace(control.Text)) found.Add(control.Text);
            if (control is DataGridView grid)
            {
                found.AddRange(grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
            }
            if (control.ContextMenuStrip is { } menu)
            {
                found.AddRange(menu.Items.Cast<ToolStripItem>().Select(i => i.Text ?? ""));
            }
            foreach (Control child in control.Controls) pending.Push(child);
        }
        return found.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    private static void AssertNoChinese(IEnumerable<string> texts, string source)
    {
        var leaked = texts.Where(HasChinese).Distinct().ToList();
        Assert.True(leaked.Count == 0,
            $"{source} 在英文界面露出中文 {leaked.Count} 处：" + string.Join(" | ", leaked));
    }

    [Fact]
    public void SettingsForm_English_NoChineseLeaked()
    {
        using var form = new SettingsForm();
        AssertNoChinese(CollectVisibleText(form), "设置窗");
    }

    [Fact]
    public void SettingsForm_English_VariablesHintIsLocalized()
    {
        // 定点守卫：正是漏译过的那一行（Loc.L 跨行写法，源码扫描会误报，控件树不会）
        using var form = new SettingsForm();
        var hint = CollectVisibleText(form).FirstOrDefault(t => t.Contains("{artistPart}", StringComparison.Ordinal));
        Assert.NotNull(hint);
        Assert.Contains("Variables:", hint!, StringComparison.Ordinal);
        Assert.DoesNotContain("变量", hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void MainForm_English_NoChineseLeaked()
    {
        using var form = new MainForm();
        AssertNoChinese(CollectVisibleText(form), "主界面");
    }

    [Fact]
    public void TrayMenu_English_NoChineseLeaked()
    {
        // 托盘菜单由 Program.CreateTrayIcon 私有构造（顺带产出 NotifyIcon），用反射拿它再遍历菜单项
        var method = typeof(Program).GetMethod("CreateTrayIcon", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        using var notifyIcon = (NotifyIcon)method!.Invoke(null, null)!;
        var menu = notifyIcon.ContextMenuStrip;
        Assert.NotNull(menu);
        AssertNoChinese(menu!.Items.Cast<ToolStripItem>().Select(i => i.Text ?? ""), "托盘菜单");
    }
}
