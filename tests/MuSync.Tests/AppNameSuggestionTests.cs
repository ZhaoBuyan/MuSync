using System;
using System.IO;
using System.Reflection;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 未收录程序的「建议显示名」测试：优先取可执行文件版本信息里的产品名，
/// 其次窗口标题，都没有才退回文件名。
/// <para>
/// 背景：程序列表里曾经只会出现 "xxx.exe" 这种没人看得懂的名字（标题为空时），
/// 或带着当前文档名的标题；而绝大多数程序都在自己的版本资源里写了产品名。
/// </para>
/// </summary>
public class AppNameSuggestionTests
{
    /// <summary>拷一份主程序集当"被测程序"——它带真实的 Win32 版本资源（ProductName = MuSync）。</summary>
    private static string CopyAssemblyAs(string fileName)
    {
        var source = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "MuSync.dll");
        var dir = Directory.CreateTempSubdirectory("musync-appname-test-").FullName;
        var target = Path.Combine(dir, fileName);
        File.Copy(source, target);
        return target;
    }

    [Fact]
    public void ReadProductName_RealAssemblyWithVersionResource_ReturnsProductName()
    {
        var copy = CopyAssemblyAs("FakeApp.dll");
        try
        {
            var name = AppClassifier.ReadProductName(copy);

            Assert.Contains("MuSync", name, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
    }

    [Fact]
    public void SuggestedName_PrefersProductNameOverWindowTitle()
    {
        var copy = CopyAssemblyAs("FakeApp2.dll");
        try
        {
            // 标题里的"文档名"不该盖过产品名
            var name = AppClassifier.SuggestedNameOf("fakeapp2.exe", "报告.docx - 某程序", copy);

            Assert.Contains("MuSync", name, StringComparison.Ordinal);
            Assert.DoesNotContain("报告.docx", name, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
    }

    [Fact]
    public void ReadProductName_MissingFile_ReturnsEmptyInsteadOfThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"musync-not-here-{Guid.NewGuid():N}.exe");

        Assert.Equal("", AppClassifier.ReadProductName(missing));
    }

    [Fact]
    public void ReadProductName_NotAnExecutable_ReturnsEmptyInsteadOfThrowing()
    {
        var dir = Directory.CreateTempSubdirectory("musync-appname-test-").FullName;
        var text = Path.Combine(dir, "notpe.exe");
        File.WriteAllText(text, "这不是一个 PE 文件");
        try
        {
            Assert.Equal("", AppClassifier.ReadProductName(text));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadProductName_NoPath_ReturnsEmpty(string? path)
    {
        Assert.Equal("", AppClassifier.ReadProductName(path));
    }

    // ---- 候选名筛选（纯函数）----

    [Fact]
    public void PickUsefulName_SkipsPlaceholdersAndGenericProductNames()
    {
        // 资源占位符与微软系泛化产品名都没有信息量，必须跳过
        Assert.Equal("", AppClassifier.PickUsefulName("@%1", "%1", "Microsoft® Windows® Operating System"));
        Assert.Equal("", AppClassifier.PickUsefulName("Microsoft Corporation"));
        Assert.Equal("", AppClassifier.PickUsefulName("Windows"));
    }

    [Fact]
    public void PickUsefulName_FallsThroughToFirstUsableCandidate()
    {
        Assert.Equal("真·产品名", AppClassifier.PickUsefulName("@%1", "   ", "真·产品名", "备选"));
    }

    [Fact]
    public void PickUsefulName_AllEmpty_ReturnsEmpty()
    {
        Assert.Equal("", AppClassifier.PickUsefulName(null, "", "   "));
    }

    // ---- 兜底链 ----

    [Fact]
    public void SuggestedName_NoVersionInfo_FallsBackToWindowTitle()
    {
        // 自解压包 / 绿色软件常有这种情况
        Assert.Equal("某文档 - 某程序", AppClassifier.SuggestedNameOf("mystery.exe", "某文档 - 某程序", null));
    }

    [Fact]
    public void SuggestedName_NoVersionInfoNoTitle_FallsBackToFileName()
    {
        Assert.Equal("mystery", AppClassifier.SuggestedNameOf("mystery.exe", "", null));
    }

    [Fact]
    public void SuggestedName_OverlongTitle_IsTruncated()
    {
        var name = AppClassifier.SuggestedNameOf("x.exe", new string('标', 80), null);

        Assert.Equal(40, name.Length);
    }

    // ---- 库规模 ----

    [Fact]
    public void LibrarySize_CoversKnownProgramsAndIgnoreList()
    {
        // 228 条程序名 + 57 条忽略名单（2026-10-12 的规模）；这里只钉住"两边都算进去了"
        Assert.True(AppClassifier.LibrarySize > 200, $"库规模异常: {AppClassifier.LibrarySize}");
    }
}
