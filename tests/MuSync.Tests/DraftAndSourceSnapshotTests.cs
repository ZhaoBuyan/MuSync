using System.Collections.Generic;
using System.Reflection;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class DraftAndSourceSnapshotTests
{
    [Fact]
    public void PipeCharactersCannotHideAChangedSourceConfiguration()
    {
        var a=new DataSourceConfig{Name="fixture",Url="http://localhost/value",ValuePath="a|b",Format="{v}"};
        var b=new DataSourceConfig{Name="fixture",Url="http://localhost/value",ValuePath="a",Format="b|{v}"};
        Assert.NotEqual(HttpDataSource.ConfigFingerprint(a),HttpDataSource.ConfigFingerprint(b));
    }

    [Fact]
    public void QueuedSourceSnapshotDoesNotFollowLaterEdits()
    {
        var source=new DataSourceConfig{Name="fixture",Url="http://old.local",ValuePath="old",Format="{v}",RefreshSeconds=30};
        var queued=HttpDataSource.ConfigFingerprint(source);
        source.Url="http://new.local";source.ValuePath="new";source.Format="new {v}";source.RefreshSeconds=1;
        Assert.Equal("http://old.local",queued.Url);Assert.Equal("old",queued.ValuePath);
        Assert.Equal("{v}",queued.Format);Assert.Equal(30,queued.RefreshSeconds);
        Assert.NotEqual(queued,HttpDataSource.ConfigFingerprint(source));
    }

    [Fact]
    public void ImportedRulesRemainIndependentDraftUntilSettingsAreSaved()
    {
        var config=Configurations.Instance.Settings;var old=config.Apps;
        try
        {
            config.Apps=[new AppRule{ExeName="existing.exe",DisplayName="Existing"}];
            using var view=new SettingsView();
            var imported=new AppRule{ExeName="imported.exe",DisplayName="Imported"};
            view.ApplyImportedRules([config.Apps[0],imported]);
            var field=typeof(SettingsView).GetField("_rules",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var draft=(List<AppRule>)field.GetValue(view)!;
            Assert.Equal(2,draft.Count);Assert.Single(config.Apps);
            imported.DisplayName="Changed outside";
            Assert.Equal("Imported",draft[1].DisplayName);
            view.ReloadSettings();Assert.Single(draft);Assert.Single(config.Apps);
            view.ApplyImportedRules([config.Apps[0],imported]);
            typeof(SettingsView).GetMethod("MergeBackgroundDiscoveredRules",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,[config]);
            Assert.NotNull(config.FindAppRule("imported.exe"));
        }
        finally{config.Apps=old;}
    }
}
