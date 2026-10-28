using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
namespace MuSync.Utils;
/// <summary>内嵌资源访问：程序图标（读取失败时回退系统默认图标）。</summary>
internal static class AppResource
{
    private static Icon? _icon;

    /// <summary>
    /// 程序图标（内嵌资源；取不到时退回系统默认图标）。
    /// <para>
    /// ⚠️ 刻意用**惰性初始化**而不是静态构造函数：静态构造函数一旦抛异常，
    /// 整个类型就永久不可用（后续任何成员访问都抛 <c>TypeInitializationException</c>）。
    /// 实测踩过：取图标失败导致本类型初始化失败，进而让**同一个类里**的
    /// <see cref="TryGetProcessPath"/> 也抛异常，而调用方（浏览器媒体读取）把它 catch 成
    /// "读取失败" —— 结果是为了拿一个图标，把整个媒体来源搞挂了。
    /// 惰性初始化把失败限制在"这次取图标"这一件事上。
    /// </para>
    /// </summary>
    public static Icon Icon => _icon ??= LoadIconOrDefault();

    private static Icon LoadIconOrDefault()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "MuSync.Resources.icon.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            return stream != null ? new Icon(stream) : SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// 按进程名取第一个匹配进程的可执行文件路径；取不到返回 null。
    /// <para>
    /// ⚠️ 读 <c>MainModule</c> 会打开进程句柄，因此这里对每个 Process 都 <c>using</c> ——
    /// 该路径会被封面占位逻辑反复调用，不释放就是持续的句柄 churn。
    /// 受保护 / 提权进程读不到路径时跳过，不抛异常。
    /// </para>
    /// </summary>
    public static string? TryGetProcessPath(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppResource] 获取 {processName} 路径失败: {ex.Message}");
            return null;
        }

        // ⚠️ 枚举结果整体释放：以前只在循环体里 using 单个进程，
        // 一旦命中就 return（或中途抛异常），剩下的对象要等 GC 终结器才关句柄。
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
                catch
                {
                    // 受保护 / 提权进程：跳过
                }
            }
        }
        finally
        {
            MuSync.Utils.ProcessQuery.DisposeAll(processes);
        }
        return null;
    }

    /// <summary>
    /// 提取可执行文件的关联图标并转成位图。
    /// <para>
    /// 用途：某些播放器不提供专辑封面（酷狗、千千静听），主界面用播放器自己的图标当占位图，
    /// 比空白灰块可读得多。大尺寸优先（128×128），失败再退回系统关联图标。
    /// </para>
    /// <para>
    /// ⚠️ **每次都返回新对象**（调用方负责 Dispose）：主界面靠"来源路径是否变化"判断要不要换图，
    /// 并负责释放上一张；若这里返回被缓存的同一实例，旧图一被释放就会连界面上正在用的图一起废掉。
    /// 文件不存在或提取失败一律返回 null，不抛异常。
    /// </para>
    /// </summary>
    public static Image? LoadExecutableIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return null;
        if (!LooksLikeExecutable(exePath)) return null;
        try
        {
            Icon? icon = null;
            try { icon = new Icon(exePath, 128, 128); } catch { /* 没有 128×128 就退回系统图标 */ }
            icon ??= Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;
            using (icon)
            {
                return icon.ToBitmap();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppResource] 提取程序图标失败({exePath}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 粗校验是不是可执行文件（PE）：先看 "MZ"，再按 e_lfanew 找 "PE\0\0"。
    /// <para>
    /// 为什么需要它：<c>Icon.ExtractAssociatedIcon</c> 对**非可执行文件**也会返回一个图标且不抛异常，
    /// 于是"提取失败"与"提取到一个无意义的默认图标"分不出来。
    /// 校验之后，非可执行文件一律走"没有图标"分支，界面退回空白灰块，语义才正确。
    /// </para>
    /// </summary>
    private static bool LooksLikeExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> dos = stackalloc byte[64];
            if (stream.Read(dos) < 64) return false;
            if (dos[0] != (byte)'M' || dos[1] != (byte)'Z') return false;
            var peOffset = BitConverter.ToInt32(dos[0x3C..0x40]);
            if (peOffset <= 0 || peOffset > stream.Length - 4) return false;
            stream.Position = peOffset;
            Span<byte> signature = stackalloc byte[4];
            if (stream.Read(signature) < 4) return false;
            return signature[0] == (byte)'P' && signature[1] == (byte)'E'
                   && signature[2] == 0 && signature[3] == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppResource] 校验可执行文件失败({path}): {ex.Message}");
            return false;
        }
    }
}
