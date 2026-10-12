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
        // 它与「程序同步是否开启」无关，必须无条件输出
        //（2026-10-12 实测：用户配置里 AppSyncEnabled=false 时，原先整段早退、这行根本不出现）。
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
}
