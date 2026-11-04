using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class EnglishWorkflowTextTests
{
    [Fact]
    public async Task ValidationAndUninitializedLoginErrorsUseEnglishWithoutNetworkRequests()
    {
        var config = Configurations.Instance.Settings;
        var previous = config.Language;
        try
        {
            config.Language = 1;
            var empty = await HttpDataSource.ProbeAsync(new DataSourceConfig());
            var invalid = await HttpDataSource.ProbeAsync(new DataSourceConfig { Url = "not a URL" });
            Assert.Contains("URL", empty.Error!);
            Assert.Contains("http://", invalid.Error!);
            using var session = new SteamSessionManager();
            Assert.False(await session.LoginWithTokenAsync("unused", "unused"));
            Assert.Contains("connection", session.LoginError!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("未初始化", session.LoginError!);
        }
        finally { config.Language = previous; }
    }

    [Fact]
    public void UnsupportedRuleFileTellsEnglishUserToUpdate()
    {
        var config = Configurations.Instance.Settings;
        var previous = config.Language;
        var path = Path.Combine(Path.GetTempPath(), "musync-english-rules-" + Guid.NewGuid() + ".json");
        try
        {
            config.Language = 1;
            File.WriteAllText(path, "{\"version\":999,\"rules\":[]}");
            var result = AppRuleExchange.Import(path, []);
            Assert.False(result.Ok);
            Assert.Contains("Update MuSync", result.Error!);
            Assert.Contains("999", result.Error!);
        }
        finally
        {
            config.Language = previous;
            File.Delete(path);
        }
    }

    [Fact]
    public void EnglishHeartRateExampleCanActuallyExpandAsDocumented()
    {
        var config = new ConfigData { PersistentStatusEnabled = true, PersistentStatusText = "{Heart rate}" };
        Assert.True(FormatBlockCodec.IsValidBlockName("Heart rate"));
        var output = SteamStatusManager.ComposeStatus(null, null, config,
            new Dictionary<string, string> { ["Heart rate"] = "92 bpm" });
        Assert.Equal("92 bpm", output);
    }
}
