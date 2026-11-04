using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class AppRulesConcurrencyTests
{
    [Fact]
    public void ActualMergeWaitsForDiscoveryTransactionBeforeTakingItsSnapshot()
    {
        var config=Configurations.Instance.Settings;var old=config.Apps;
        try
        {
            config.Apps=[new AppRule{ExeName="a.exe"}];
            using var view=new SettingsView();
            using var started=new ManualResetEventSlim();using var finished=new ManualResetEventSlim();
            Exception? error=null;
            var merge=new Thread(()=>
            {
                started.Set();
                try{typeof(SettingsView).GetMethod("MergeBackgroundDiscoveredRules",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[config]);}
                catch(Exception ex){error=ex;}finally{finished.Set();}
            });
            lock(config.AppsSync)
            {
                merge.Start();Assert.True(started.Wait(3000));Assert.False(finished.Wait(100));
                AppRuleManager.Match(new ForegroundAppInfo{ExeName="code.exe"},config,()=>{});
            }
            Assert.True(merge.Join(5000));Assert.Null(error);Assert.NotNull(config.FindAppRule("code.exe"));
        }
        finally{config.Apps=old;}
    }
    [Fact]
    public void DiscoveryWaitsForRulesTransactionAndPersistsOutsideItsLock()
    {
        var config=new ConfigData();
        var entered=new ManualResetEventSlim();var finished=new ManualResetEventSlim();
        Exception? error=null;var persisted=0;
        var thread=new Thread(()=>
        {
            entered.Set();
            try {AppRuleManager.Match(new ForegroundAppInfo{ExeName="code.exe"},config,()=>
                {Assert.False(Monitor.IsEntered(config.AppsSync));Interlocked.Increment(ref persisted);});}
            catch(Exception ex){error=ex;}finally{finished.Set();}
        });
        lock(config.AppsSync)
        {
            thread.Start();Assert.True(entered.Wait(3000));
            Assert.False(finished.Wait(100));
        }
        Assert.True(thread.Join(3000));Assert.Null(error);
        Assert.Single(config.SnapshotAppRules());Assert.Equal(1,persisted);
        entered.Dispose();finished.Dispose();
    }

    [Fact]
    public void ConcurrentDiscoveriesDoNotDuplicateTheSameExe()
    {
        var config=new ConfigData();var writes=0;
        Parallel.For(0,32,_=>AppRuleManager.Match(new ForegroundAppInfo{ExeName="code.exe"},config,
            ()=>Interlocked.Increment(ref writes)));
        Assert.Single(config.SnapshotAppRules());Assert.Equal(1,writes);
        Assert.DoesNotContain("AppsSync",JsonSerializer.Serialize(config,ConfigJson.Options));
    }

    [Fact]
    public void MergePreservesDiscoveryAndDoesNotShareTheEditableDraft()
    {
        var config=Configurations.Instance.Settings;var old=config.Apps;
        try
        {
            config.Apps=[new AppRule{ExeName="a.exe",DisplayName="Original"}];
            using var view=new SettingsView();
            AppRuleManager.Match(new ForegroundAppInfo{ExeName="code.exe"},config,()=>{});
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(SettingsView).GetMethod("MergeBackgroundDiscoveredRules",flags)!.Invoke(view,[config]);
            Assert.NotNull(config.FindAppRule("code.exe"));
            var draft=(List<AppRule>)typeof(SettingsView).GetField("_rules",flags)!.GetValue(view)!;
            draft[0].DisplayName="Unsaved change";
            Assert.Equal("Original",config.FindAppRule("a.exe")!.DisplayName);
        }
        finally{config.Apps=old;}
    }
}
