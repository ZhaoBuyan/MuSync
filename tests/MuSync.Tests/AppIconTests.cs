using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 可执行文件图标提取的测试（无封面播放器的占位图来源：酷狗 / 千千静听）。
/// <para>
/// 为什么加 PE 校验：<c>Icon.ExtractAssociatedIcon</c> 对**非可执行文件**也会返回一个图标，
/// 不抛异常 —— 于是"提取失败"与"提取到一个无意义的默认图标"分不出来。
/// 因此加了 PE 头校验。这里同时钉住两件事：真 exe 仍能取到图（校验没把功能挡死）、非 PE 文件返回 null。
/// </para>
/// </summary>
public class AppIconTests
{
    /// <summary>拷一份主程序集当"真 exe"（它有 PE 头和真实的图标资源）。</summary>
    private static string CopyAssemblyAs(string fileName)
    {
        var source = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "MuSync.dll");
        var dir = Directory.CreateTempSubdirectory("musync-icon-test-").FullName;
        var target = Path.Combine(dir, fileName);
        File.Copy(source, target);
        return target;
    }

    [Fact]
    public void RealExecutable_StillYieldsAnIcon()
    {
        // 关键：PE 校验不能把"真 exe 取图标"这条主路径挡掉
        var copy = CopyAssemblyAs("FakeApp.exe");
        try
        {
            var icon = AppResource.LoadExecutableIcon(copy);

            Assert.NotNull(icon);
            Assert.True(icon!.Width > 0 && icon.Height > 0);
            icon.Dispose();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
    }

    [Fact]
    public void NonExecutableFile_ReturnsNull()
    {
        var dir = Directory.CreateTempSubdirectory("musync-icon-test-").FullName;
        var text = Path.Combine(dir, "not-an-exe.exe");
        File.WriteAllText(text, "这不是 PE 文件，只是文本");
        try
        {
            Assert.Null(AppResource.LoadExecutableIcon(text));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MissingOrEmptyPath_ReturnsNullWithoutThrowing()
    {
        Assert.Null(AppResource.LoadExecutableIcon(null));
        Assert.Null(AppResource.LoadExecutableIcon(""));
        Assert.Null(AppResource.LoadExecutableIcon("   "));
        Assert.Null(AppResource.LoadExecutableIcon(
            Path.Combine(Path.GetTempPath(), $"definitely-missing-{Guid.NewGuid():N}.exe")));
    }

    [Fact]
    public void RepeatedCalls_ReturnDistinctInstances()
    {
        // 主界面按"来源路径是否变化"判断要不要换占位图，并负责 Dispose 旧图；
        // 若这里返回被缓存的同一实例，旧图一被释放就会连界面上的图一起废掉
        var copy = CopyAssemblyAs("FakeApp2.exe");
        try
        {
            var first = AppResource.LoadExecutableIcon(copy);
            var second = AppResource.LoadExecutableIcon(copy);

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.False(ReferenceEquals(first, second));
            first!.Dispose();
            second!.Dispose();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
    }
}
