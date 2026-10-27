using System;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>诊断报告测试：核心段落齐全。</summary>
public class DiagnosticsReportTests
{
    [Fact]
    public void Build_ContainsCoreSections()
    {
        var text = DiagnosticsReport.Build();
        Assert.Contains("MuSync 诊断信息", text);
        Assert.Contains("版本：", text);
        Assert.Contains("运行：", text);
        Assert.Contains("Steam：", text);
        Assert.Contains("同步：", text);
        Assert.Contains("播放器：", text);
        Assert.Contains("程序同步：", text);
        Assert.Contains("音乐同步：", text);
        Assert.Contains("外观自定义：", text);
        Assert.Contains("系统：", text);
        Assert.Contains("内存：", text);
        Assert.Contains("配置：", text);
        Assert.Contains("日志：", text);
    }

    [Fact]
    public void Build_ReportsAppLibrarySize_RegardlessOfAppSyncSwitch()
    {
        // 库规模随版本变化：放进诊断后，反馈问题时能直接看出用户用的是哪一版库。
        // 它与「程序同步是否开启」无关，必须无条件输出：用户配置里 AppSyncEnabled=false 时，
        // 该段其余内容会被早退分支跳过，这行不能跟着一起消失。
        // ⚠️ 断言只查数字、不查中文标签：本类没进 LanguageSensitiveCollection，
        // 而 EnglishUiGuardTests 会把全局语言切成 English（诊断按语言输出）。
        var text = DiagnosticsReport.Build();

        Assert.Contains(AppClassifier.LibrarySize.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithoutInitializedManagers_ReportsNotInitialized()
    {
        // 测试进程中未初始化任何管理器：应输出「未初始化」而不是抛异常
        var text = DiagnosticsReport.Build();
        Assert.Contains("Steam：未初始化", text);
        Assert.Contains("播放器：未初始化", text);
    }
    [Fact]
    public void Report_IncludesBrowserMediaAndExtensionState()
    {
        // "浏览器视频不同步"的三种原因（设置没开 / 没浏览器进程 / 读不到会话）
        // 必须能从诊断信息里一眼区分，不能让用户去翻日志。
        var settings = Configurations.Instance.Settings;
        var backupEnabled = settings.BrowserExtensionEnabled;
        var backupPort = settings.BrowserExtensionPort;
        try
        {
            settings.BrowserExtensionEnabled = true;
            settings.BrowserExtensionPort = 47564;

            var text = DiagnosticsReport.Build();

            Assert.Contains(Loc.L("浏览器媒体", "Browser media"), text);
            Assert.Contains("47564", text);                       // 端口要能看到
            Assert.Contains(Loc.L("系统会话", "System sessions"), text);
            Assert.Contains(Loc.L("扩展桥", "Bridge"), text);
        }
        finally
        {
            settings.BrowserExtensionEnabled = backupEnabled;
            settings.BrowserExtensionPort = backupPort;
        }
    }
}