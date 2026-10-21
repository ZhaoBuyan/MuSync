using System;
using System.IO;
using System.Reflection;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 更新包签名校验（Authenticode / WinVerifyTrust）的守卫测试。
/// <para>
/// 这里刻意用**真实文件**验证，而不是构造假数据：签名校验的价值全在"系统怎么判"，
/// 一旦错误码映射写错（比如把"非 PE 文件"当成"被篡改"），测试用假数据根本发现不了 ——
/// 本次实现就踩过这个坑（把 0x800B0003 误判成篡改，导致下载测试里那个假 exe 被拒）。
/// </para>
/// </summary>
public class BinarySignatureTests
{
    /// <summary>本项目的测试程序集本身没有签名，是稳定的"未签名"样本。</summary>
    private static string UnsignedSample()
    {
        var path = Assembly.GetExecutingAssembly().Location;
        Assert.False(string.IsNullOrEmpty(path), "拿不到测试程序集路径");
        Assert.True(File.Exists(path), "测试程序集不存在");
        return path;
    }

    [Fact]
    public void UnsignedAssembly_IsReportedUnsigned()
    {
        var status = BinarySignature.Check(UnsignedSample(), out var hresult);

        Assert.Equal(BinarySignatureStatus.Unsigned, status);
        Assert.Equal(unchecked((int)0x800B0100), hresult);   // TRUST_E_NOSIGNATURE
    }

    [Fact]
    public void UnsignedPackage_IsNotRejected()
    {
        // 关键契约：没有签名不等于"被篡改"。本项目发行包目前没签名，
        // 若把 Unsigned 也当拒绝，更新流程会整个废掉。
        Assert.False(BinarySignature.MustReject(BinarySignatureStatus.Unsigned));
        Assert.NotEmpty(BinarySignature.WarningFor(BinarySignatureStatus.Unsigned));
    }

    [Fact]
    public void RandomBytesFile_IsTreatedAsUnsigned_NotTampered()
    {
        // 下载测试里用的是随机字节：不是带签名的 PE，系统返回 0x800B0003。
        // 这一档必须归为"未签名"，否则所有假文件的下载测试都会被拒。
        var path = Path.Combine(Path.GetTempPath(), $"musync-sig-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[512]);
        try
        {
            var status = BinarySignature.Check(path, out _);
            Assert.Equal(BinarySignatureStatus.Unsigned, status);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_IsUnknown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"musync-missing-{Guid.NewGuid():N}.exe");

        var status = BinarySignature.Check(path, out _);

        Assert.Equal(BinarySignatureStatus.Unknown, status);
    }

    [Fact]
    public void NullPath_DoesNotThrow()
    {
        var status = BinarySignature.Check(null, out _);
        Assert.Equal(BinarySignatureStatus.Unknown, status);
    }

    [Fact]
    public void TamperedSignedBinary_IsRejected()
    {
        // 需要一个**内嵌签名**的可执行文件作为样本。系统自带的 notepad/curl 是"目录签名"，
        // WinVerifyTrust 按文件验会报 NOSIGNATURE，不能用来测篡改。dotnet.exe 是内嵌签名。
        var signed = FindEmbeddedSignedSample();
        if (signed is null) return;   // 环境里找不到样本就不硬编断言（CI 上 dotnet 一定在）

        var copy = Path.Combine(Path.GetTempPath(), $"musync-tampered-{Guid.NewGuid():N}.exe");
        try
        {
            File.Copy(signed, copy, overwrite: true);
            var bytes = File.ReadAllBytes(copy);
            bytes[bytes.Length / 2] ^= 0xFF;          // 改一个字节：PE 结构完好，但摘要必然对不上
            File.WriteAllBytes(copy, bytes);

            var status = BinarySignature.Check(copy, out var hresult);

            Assert.Equal(BinarySignatureStatus.Invalid, status);
            Assert.True(BinarySignature.MustReject(status), "篡改过的包必须被拒绝");
            Assert.Equal(unchecked((int)0x80096010), hresult);   // TRUST_E_BAD_DIGEST
        }
        finally
        {
            if (File.Exists(copy)) File.Delete(copy);
        }
    }

    [Fact]
    public void GenuineSignedBinary_IsValid()
    {
        var signed = FindEmbeddedSignedSample();
        if (signed is null) return;

        var status = BinarySignature.Check(signed, out var hresult);

        Assert.Equal(BinarySignatureStatus.Valid, status);
        Assert.Equal(0, hresult);
        Assert.Empty(BinarySignature.WarningFor(status));
    }

    /// <summary>找一个带**内嵌** Authenticode 签名的样本（目录签名的系统程序不算）。</summary>
    private static string? FindEmbeddedSignedSample()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            // 只认内嵌签名：目录签名的文件在这里会是 Unsigned，不能当样本
            if (BinarySignature.Check(candidate, out _) == BinarySignatureStatus.Valid) return candidate;
        }
        return null;
    }
}
