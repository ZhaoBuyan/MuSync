using System;
using System.IO;
using System.Reflection;
using System.Text;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 测试程序集「不带 Win32 应用清单」的守卫。
/// <para>
/// 为什么要有这道守卫：SDK 的 <c>SetWin32ManifestProperties</c>（Microsoft.Common.CurrentVersion.targets）
/// 在 <c>ApplicationManifest</c> 为空、且 <c>NoWin32Manifest != true</c> 时，会把 .NET Framework 自带的
/// <c>default.win32manifest</c> 嵌进程序集。测试程序集因此会带上
/// <c>&lt;assemblyIdentity version="1.0.0.0" name="MyApplication.app"/&gt;</c> 与
/// <c>&lt;requestedExecutionLevel level="asInvoker" uiAccess="false"/&gt;</c>。
/// 一个跑 xunit 的测试 DLL 声称自己是可执行程序，本身就是错的：测试宿主按 DLL 加载它，
/// 从不需要入口点或执行级别声明。清掉它是为了消除这层"可执行包装"。
/// </para>
/// <para>
/// 与杀软误报的关系（未证实，别当结论用）：曾有杀软把 <c>MuSync.Tests.dll</c> 判为
/// <c>Backdoor/Steam</c> 并反复删除，导致构建报「无法复制 MuSync.Tests.dll，因为找不到该文件」。
/// 排查发现该程序集当时确实带着上述清单，但同时也有反证 —— 把同一份带清单的文件改名、
/// 换目录、甚至改哈希后复制，依然被删，说明判据不是单一特征。因此本守卫守住的只是
/// 「工程不该有的包装」，能否因此不再被误杀需要另行复验。
/// 完整背景与证据见 docs/incident-test-assembly-false-positive.md。
/// </para>
/// </summary>
public class TestAssemblyPackagingGuardTests
{
    /// <summary>当前测试程序集在磁盘上的完整路径。</summary>
    private static string AssemblyPath()
    {
        var path = Assembly.GetExecutingAssembly().Location;
        Assert.False(string.IsNullOrEmpty(path), "拿不到测试程序集路径（单文件/内存加载？）");
        Assert.True(File.Exists(path), $"测试程序集路径不存在: {path}");
        return path;
    }

    /// <summary>在字节流里找 ASCII 子串（用于扫描 PE 里的清单文本）。</summary>
    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        var pattern = Encoding.ASCII.GetBytes(needle);
        for (var i = 0; i + pattern.Length <= haystack.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (haystack[i + j] != pattern[j])
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }
        return false;
    }

    [Fact]
    public void TestAssembly_DoesNotEmbedDefaultWin32Manifest()
    {
        var bytes = File.ReadAllBytes(AssemblyPath());
        Assert.False(ContainsAscii(bytes, "MyApplication.app"),
            "测试程序集里出现了默认应用清单的 assemblyIdentity —— NoWin32Manifest 被去掉了？");
        Assert.False(ContainsAscii(bytes, "requestedExecutionLevel"),
            "测试程序集里出现了 requestedExecutionLevel —— 应用清单又被嵌进来了");
        Assert.False(ContainsAscii(bytes, "urn:schemas-microsoft-com:asm.v2"),
            "测试程序集里出现了 trustInfo 命名空间 —— 应用清单又被嵌进来了");
    }

    /// <summary>
    /// 直接读 PE 头：若资源目录里存在 RT_MANIFEST（类型 24）条目，说明清单又被嵌回来了。
    /// 与文本扫描互为佐证：文本扫描快但可能被偶然字节骗过，PE 结构检查不依赖字符串内容。
    /// <para>
    /// 反向验证过：把测试工程的 <c>NoWin32Manifest</c> 设为 false 重新构建后，
    /// 本测试与上面的文本扫描会同时失败（报「资源目录里存在 RT_MANIFEST(24)」）。
    /// </para>
    /// </summary>
    [Fact]
    public void TestAssembly_PeHeader_HasNoManifestResource()
    {
        var bytes = File.ReadAllBytes(AssemblyPath());

        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        Assert.True(peOffset > 0 && peOffset + 4 <= bytes.Length, $"PE 头偏移异常: {peOffset}");
        Assert.Equal(0x00004550, BitConverter.ToInt32(bytes, peOffset)); // "PE\0\0"

        var optionalHeaderOffset = peOffset + 24;
        var magic = BitConverter.ToUInt16(bytes, optionalHeaderOffset);
        var dataDirectoryOffset = magic switch
        {
            0x010B => optionalHeaderOffset + 96,  // PE32
            0x020B => optionalHeaderOffset + 112, // PE32+
            _ => throw new InvalidOperationException($"未知的可选头 magic: 0x{magic:X4}")
        };

        // 数据目录第 2 项（下标 2）= 资源表
        var resourceRva = BitConverter.ToInt32(bytes, dataDirectoryOffset + 2 * 8);
        var resourceSize = BitConverter.ToInt32(bytes, dataDirectoryOffset + 2 * 8 + 4);
        Assert.True(resourceSize > 0, "测试程序集没有资源目录，不该发生（版本信息也在资源里）");

        // RVA → 文件偏移：遍历节表换算
        var sectionCount = BitConverter.ToUInt16(bytes, peOffset + 6);
        var sizeOfOptionalHeader = BitConverter.ToUInt16(bytes, peOffset + 20);
        var sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
        var resourceFileOffset = -1;
        for (var i = 0; i < sectionCount; i++)
        {
            var section = sectionTableOffset + i * 40;
            var virtualSize = BitConverter.ToInt32(bytes, section + 8);
            var virtualAddress = BitConverter.ToInt32(bytes, section + 12);
            var rawSize = BitConverter.ToInt32(bytes, section + 16);
            var rawPointer = BitConverter.ToInt32(bytes, section + 20);
            var span = Math.Max(virtualSize, rawSize);
            if (resourceRva >= virtualAddress && resourceRva < virtualAddress + span)
            {
                resourceFileOffset = rawPointer + (resourceRva - virtualAddress);
                break;
            }
        }
        Assert.True(resourceFileOffset > 0, "资源目录不在任何节里，PE 结构异常");

        // 资源目录顶层：每个条目 8 字节（Name/Id + OffsetToData）
        var namedEntries = BitConverter.ToUInt16(bytes, resourceFileOffset + 12);
        var idEntries = BitConverter.ToUInt16(bytes, resourceFileOffset + 14);
        var hasManifestEntry = false;
        for (var i = 0; i < namedEntries + idEntries; i++)
        {
            var entry = resourceFileOffset + 16 + i * 8;
            var id = BitConverter.ToInt32(bytes, entry);
            // RT_MANIFEST = 24；高位为 1 表示按名字引用
            if (id == 24)
            {
                hasManifestEntry = true;
                break;
            }
        }
        Assert.False(hasManifestEntry, "资源目录里存在 RT_MANIFEST(24)，应用清单又被嵌进来了");
    }
}
