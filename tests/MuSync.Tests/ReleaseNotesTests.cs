using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>更新日志纯文本化测试：去掉 markdown 标记，但不能动正文内容（v0.5.0 起更新窗按纯文本展示）。</summary>
public class ReleaseNotesTests
{
    [Fact]
    public void HeadingMarkers_AreRemoved()
    {
        var text = ReleaseNotes.ToPlainText("### 新增\n\n- 一条内容");
        Assert.DoesNotContain("#", text);
        Assert.Contains("新增", text);
        Assert.Contains("- 一条内容", text); // 列表符保留：纯文本下更易读
    }

    [Fact]
    public void BoldMarkers_AreRemoved()
    {
        var text = ReleaseNotes.ToPlainText("- **常驻状态**：没有音乐时推送自定义文案");
        Assert.DoesNotContain("*", text);
        Assert.Equal("- 常驻状态：没有音乐时推送自定义文案", text);
    }

    [Fact]
    public void SingleAsteriskInContent_IsKept()
    {
        // `*.png` 这类内容不能被误伤
        Assert.Equal("图片文件|*.png;*.jpg", ReleaseNotes.ToPlainText("图片文件|*.png;*.jpg"));
    }

    [Fact]
    public void CodeSpanMarkers_AreRemoved()
    {
        Assert.Equal("config.json 在 %LocalAppData%\\MuSync", ReleaseNotes.ToPlainText("`config.json` 在 %LocalAppData%\\MuSync"));
    }

    [Fact]
    public void QuoteMarkers_AreRemoved()
    {
        var text = ReleaseNotes.ToPlainText("> 引用一行\n>\n> 再来一行");
        Assert.DoesNotContain(">", text);
        Assert.Contains("引用一行", text);
        Assert.Contains("再来一行", text);
    }

    [Fact]
    public void BlankLineRuns_AreCollapsed()
    {
        Assert.Equal("第一行\n\n第二行", ReleaseNotes.ToPlainText("第一行\n\n\n\n第二行"));
    }

    [Fact]
    public void CrLf_IsNormalized()
    {
        Assert.Equal("第一行\n第二行", ReleaseNotes.ToPlainText("第一行\r\n第二行"));
    }

    [Fact]
    public void EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal("", ReleaseNotes.ToPlainText(null));
        Assert.Equal("", ReleaseNotes.ToPlainText("   \n  "));
    }

    [Fact]
    public void RealChangelogSection_KeepsContentAndDropsMarkers()
    {
        // 用本项目 CHANGELOG 的真实片段跑一遍：正文不许丢，标记不许留
        const string section = """
            ### 新增

            - **常驻状态**：没有音乐、也没有程序在同步时不再清空状态，改为推送你自定义的一段文字
              （设置 → 显示 →「常驻状态」，支持 emoji）

            ### 修复

            - **偶发的同步停止**：长时间运行后状态可能不再更新、需重启程序——新增看门狗自动恢复
            """;
        var text = ReleaseNotes.ToPlainText(section);
        Assert.DoesNotContain("###", text);
        Assert.DoesNotContain("**", text);
        Assert.Contains("常驻状态", text);
        Assert.Contains("支持 emoji", text);
        Assert.Contains("偶发的同步停止", text);
    }
}
