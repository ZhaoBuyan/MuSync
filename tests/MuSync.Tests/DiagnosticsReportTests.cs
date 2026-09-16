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
    public void Build_WithoutInitializedManagers_ReportsNotInitialized()
    {
        // 测试进程中未初始化任何管理器：应输出「未初始化」而不是抛异常
        var text = DiagnosticsReport.Build();
        Assert.Contains("Steam：未初始化", text);
        Assert.Contains("播放器：未初始化", text);
    }
}
