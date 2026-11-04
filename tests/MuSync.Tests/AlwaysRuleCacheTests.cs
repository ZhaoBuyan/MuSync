using System;
using System.IO;
using System.Reflection;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class AlwaysRuleCacheTests
{
    [Theory]
    [InlineData("disabled")]
    [InlineData("foreground")]
    [InlineData("force-off")]
    [InlineData("sync-off")]
    [InlineData("category-off")]
    public void CachedRunningRuleRespectsEditsBeforeNextProcessScan(string change)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var lastScan = typeof(AppRuleManager).GetField("_lastAlwaysScan", flags)!;
        var cachedExe = typeof(AppRuleManager).GetField("_cachedAlwaysExe", flags)!;
        var oldScan = lastScan.GetValue(null);
        var oldExe = cachedExe.GetValue(null);
        try
        {
            lastScan.SetValue(null, DateTime.MinValue);
            cachedExe.SetValue(null, null);
            var rule = new AppRule
            {
                ExeName = Path.GetFileName(Environment.ProcessPath)!,
                Enabled = true, Mode = AppSyncMode.Always, Category = AppCategory.Game
            };
            var config = new ConfigData { AppSyncEnabled = true, Apps = [rule] };
            var now = DateTime.UtcNow;
            Assert.Same(rule, AppRuleManager.FindRunningAlwaysRule(config, now));
            switch (change)
            {
                case "disabled": rule.Enabled = false; break;
                case "foreground": rule.Mode = AppSyncMode.Foreground; break;
                case "force-off": rule.Override = AppSyncOverride.ForceOff; break;
                case "sync-off": config.AppSyncEnabled = false; break;
                case "category-off": rule.Category = AppCategory.Other; config.SyncNonGameApps = false; break;
            }
            Assert.Null(AppRuleManager.FindRunningAlwaysRule(config, now.AddMilliseconds(1)));
        }
        finally
        {
            lastScan.SetValue(null, oldScan);
            cachedExe.SetValue(null, oldExe);
        }
    }
}
