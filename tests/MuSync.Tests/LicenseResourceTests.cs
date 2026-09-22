using System.IO;
using System.Text;
using MuSync;
using Xunit;

namespace MuSync.Tests;

/// <summary>内嵌许可测试：三份文本都在程序集里（单文件 exe 也随得走），且内容非空、含关键字。</summary>
public class LicenseResourceTests
{
    private static string Read(string logicalName)
    {
        using var stream = typeof(LicenseForm).Assembly.GetManifestResourceStream(logicalName);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [Fact]
    public void MuSyncLicense_Embedded()
    {
        var text = Read("MuSync.Licenses.LICENSE");
        Assert.Contains("MIT License", text);
    }

    [Fact]
    public void ThirdPartyNotices_Embedded()
    {
        var text = Read("MuSync.Licenses.THIRD-PARTY-NOTICES");
        Assert.Contains("SteamKit2", text);
        Assert.Contains("LGPL-2.1-only", text);
    }

    [Fact]
    public void LgplFullText_Embedded()
    {
        var text = Read("MuSync.Licenses.LGPL-2.1");
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", text);
        Assert.True(text.Length > 20000);
    }
}
