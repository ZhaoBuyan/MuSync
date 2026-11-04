using System.Reflection;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class ActiveRuleRefreshTests
{
    [Fact]
    public void RuleGettersUseReplacementAndRemovalWithoutWaitingForPolling()
    {
        var config=Configurations.Instance.Settings;var previous=config.Apps;
        var manager=new RpcManager(null!);
        try
        {
            var old=new AppRule{ExeName="fixture.exe",Category=AppCategory.Other};
            config.Apps=[old];
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(RpcManager).GetField("_activeAppRule",flags)!.SetValue(manager,old);
            typeof(RpcManager).GetField("_pendingAppRule",flags)!.SetValue(manager,old);
            var replacement=new AppRule{ExeName="fixture.exe",Category=AppCategory.Work,Enabled=false,IsUserConfirmed=true};
            lock(config.AppsSync){config.Apps.Clear();config.Apps.Add(replacement);}
            Assert.Same(replacement,manager.GetActiveAppRule());
            Assert.Same(replacement,manager.GetPendingAppRule());
            Assert.Equal(Loc.L("工作","Work"),manager.GetActiveAppCategoryText());
            lock(config.AppsSync)config.Apps.Clear();
            Assert.Null(manager.GetActiveAppRule());Assert.Null(manager.GetPendingAppRule());
            Assert.Equal("",manager.GetActiveAppCategoryText());
        }
        finally{manager.Stop();config.Apps=previous;}
    }
}
