using System;
using System.Linq;
using System.Windows.Forms;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 设置页保存逻辑的边界：端口填错时**不能连带把令牌也丢掉**，也不能静默忽略。
/// （这条曾经真的错了：令牌赋值被嵌在端口校验的 if 里。）
/// </summary>
public class SettingsExtensionSaveTests
{
    private static (TextBox port, TextBox token) FindExtensionBoxes(SettingsForm form)
    {
        var all = new System.Collections.Generic.List<Control>();
        var stack = new System.Collections.Generic.Stack<Control>();
        stack.Push(form);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            all.Add(c);
            foreach (Control child in c.Controls) stack.Push(child);
        }
        var boxes = all.OfType<TextBox>().ToList();
        // 端口框：默认文本就是 47564
        var port = boxes.First(b => b.Text == "47564");
        // 令牌框：靠占位提示认（比宽度可靠，宽度会随布局变）
        var token = boxes.First(b => (b.PlaceholderText ?? "").Contains("留空"));
        return (port, token);
    }

    [Fact]
    public void InvalidPort_KeepsPreviousValue_AndStillSavesToken()
    {
        var settings = Configurations.Instance.Settings;
        var backupToken = settings.BrowserExtensionToken;
        var backupPort = settings.BrowserExtensionPort;
        try
        {
            using var form = new SettingsForm();
            var (port, token) = FindExtensionBoxes(form);

            port.Text = "not-a-port";
            token.Text = "token-应该被保存";
            InvokeSave(form);

            Assert.Equal(backupPort, settings.BrowserExtensionPort);   // 端口保留原值
            Assert.Equal("token-应该被保存", settings.BrowserExtensionToken); // 令牌照样保存
        }
        finally
        {
            settings.BrowserExtensionToken = backupToken;
            settings.BrowserExtensionPort = backupPort;
        }
    }

    /// <summary>通过反射调用私有的 SaveSettings（点「确定/应用」时走的就是它）。</summary>
    private static void InvokeSave(SettingsForm form)
    {
        var method = typeof(SettingsForm).GetMethod("SaveSettings",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(form, null);
    }
    private static Label FindExtensionStatusLabel(SettingsForm form)
    {
        var all = new System.Collections.Generic.List<Control>();
        var stack = new System.Collections.Generic.Stack<Control>();
        stack.Push(form);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            all.Add(c);
            foreach (Control child in c.Controls) stack.Push(child);
        }
        // 端口/令牌同一行的状态标签：宽 300、字号 8
        return all.OfType<Label>().First(l => l.Width == 300 && Math.Abs(l.Font.Size - 8f) < 0.6f);
    }

    [Fact]
    public void InvalidPort_ShowsWarningOnStatusLabel_InsteadOfSilentlyIgnoring()
    {
        var settings = Configurations.Instance.Settings;
        var backupPort = settings.BrowserExtensionPort;
        try
        {
            using var form = new SettingsForm();
            var (port, _) = FindExtensionBoxes(form);
            port.Text = "70000";   // 超出范围
            InvokeSave(form);

            var status = FindExtensionStatusLabel(form);
            Assert.Contains(Loc.L("端口需", "Port must be"), status.Text);
            Assert.Equal(backupPort.ToString(), port.Text);   // 输入框回填成生效值
        }
        finally
        {
            settings.BrowserExtensionPort = backupPort;
        }
    }

    [Fact]
    public void ValidPort_IsApplied()
    {
        var settings = Configurations.Instance.Settings;
        var backupPort = settings.BrowserExtensionPort;
        var backupEnabled = settings.BrowserExtensionEnabled;
        try
        {
            using var form = new SettingsForm();
            var (port, _) = FindExtensionBoxes(form);
            port.Text = "47570";
            InvokeSave(form);

            Assert.Equal(47570, settings.BrowserExtensionPort);
        }
        finally
        {
            settings.BrowserExtensionPort = backupPort;
            settings.BrowserExtensionEnabled = backupEnabled;
            MuSync.Utils.BrowserExtensionBridge.Ensure(false, backupPort, settings.BrowserExtensionToken);
        }
    }
}