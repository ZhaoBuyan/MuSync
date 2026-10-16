using System;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// <see cref="AppResource"/> 的初始化健壮性测试。
/// <para>
/// 背景（实测踩过的坑）：本类的图标原本用**静态构造函数**初始化，而静态构造函数一旦抛异常，
/// 整个类型就永久不可用 —— 后续任何成员访问都抛 <c>TypeInitializationException</c>。
/// 当时为了取浏览器图标调用了同一个类里的 <c>TryGetProcessPath</c>，
/// 它把异常抛给了浏览器媒体读取器，被那边的 catch 吞成"读取失败"，
/// 于是**为了拿一个图标，把整个媒体来源搞挂了**（实测 60 拍里 27 拍读不到，
/// 状态被其它播放器反复顶掉）。
/// </para>
/// <para>
/// 因此这里钉住两条：① 图标访问永远不抛；② 取进程路径与图标互不牵连。
/// </para>
/// </summary>
public class AppResourceTests
{
    [Fact]
    public void Icon_IsAlwaysAvailable_EvenIfResourceLoadFails()
    {
        // 不论内嵌资源能否读出，都必须拿到一个可用图标（最差退回系统默认图标），不得抛异常
        var icon = AppResource.Icon;
        Assert.NotNull(icon);
        Assert.True(icon.Width > 0);
    }

    [Fact]
    public void Icon_RepeatedAccess_ReturnsSameInstance()
    {
        // 惰性初始化：第二次访问走缓存，不应每次重建
        Assert.Same(AppResource.Icon, AppResource.Icon);
    }

    [Fact]
    public void TryGetProcessPath_WorksIndependentlyOfIconInitialization()
    {
        // 关键回归：取进程路径不得因为"图标初始化失败"而抛异常。
        // 用一个几乎肯定不存在的进程名验证：应当安静地返回 null，而不是抛 TypeInitializationException。
        var path = AppResource.TryGetProcessPath("musync-no-such-process-" + Guid.NewGuid().ToString("N"));
        Assert.Null(path);
    }

    [Fact]
    public void TryGetProcessPath_KnownProcess_ReturnsExistingFile()
    {
        // 用一个一定在运行的进程验证正向路径（当前测试进程自己）
        var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        var path = AppResource.TryGetProcessPath(name);
        Assert.False(string.IsNullOrEmpty(path));
        Assert.True(System.IO.File.Exists(path));
    }
}
