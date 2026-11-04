using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class SettingsReloadPerformanceTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name,Flags)!.GetValue(target)!;
    private static T Row<T>(object row,string name) => (T)row.GetType().GetProperty(name)!.GetValue(row)!;

    [Fact]
    public void ReloadReusesSourcesAndUnchangedRulesButRestoresUnsavedInputs()
    {
        var settings=Configurations.Instance.Settings;
        var sources=settings.DataSources;var rules=settings.Apps;
        try
        {
            settings.DataSources=[new DataSourceConfig {Name="fixture",Url="http://127.0.0.1:9000",ValuePath="value",Format="{v}",RefreshSeconds=3}];
            settings.Apps=[new AppRule {ExeName="fixture.exe",DisplayName="Original",Enabled=true}];
            using var view=new SettingsView();
            var rows=Field<IList>(view,"_dataSourceRows");var original=rows[0]!;
            var container=Row<Panel>(original,"Container");
            var grid=Field<DataGridView>(view,"_rulesGrid");var gridRow=grid.Rows[0];
            view.ReloadSettings();
            Assert.Same(original,rows[0]);Assert.Same(gridRow,grid.Rows[0]);Assert.False(container.IsDisposed);
            Row<TextBox>(original,"Name").Text="{invalid draft}";
            Row<TextBox>(original,"Url").Text="unsaved";
            Row<NumericUpDown>(original,"Refresh").Value=50;
            view.ReloadSettings();
            Assert.Same(original,rows[0]);Assert.Equal("fixture",Row<TextBox>(original,"Name").Text);
            Assert.Equal("http://127.0.0.1:9000",Row<TextBox>(original,"Url").Text);
            Assert.Equal(3,Row<NumericUpDown>(original,"Refresh").Value);
            settings.Apps[0].DisplayName="Changed externally";
            view.ReloadSettings();Assert.NotSame(gridRow,grid.Rows[0]);
            Assert.Equal("Changed externally",grid.Rows[0].Cells[2].Value);
            var draft=Field<List<AppRule>>(view,"_rules");draft[0].Enabled=false;
            view.ReloadSettings();Assert.True(draft[0].Enabled);
            settings.DataSources=[];view.ReloadSettings();Assert.Empty(rows);Assert.True(container.IsDisposed);
        }
        finally {settings.DataSources=sources;settings.Apps=rules;}
    }

    [Fact]
    public void QueuedDeletionFromDiscardedDraftDoesNotRemoveReloadedSource()
    {
        var settings=Configurations.Instance.Settings;var previous=settings.DataSources;
        try
        {
            settings.DataSources=[new DataSourceConfig {Name="fixture"}];
            using var view=new SettingsView();_ = view.Handle;
            var rows=Field<IList>(view,"_dataSourceRows");
            var panel=Row<Panel>(rows[0]!,"Container");
            var remove=panel.Controls.OfType<Button>().Single();
            typeof(Button).GetMethod("OnClick",Flags)!.Invoke(remove,[EventArgs.Empty]);
            view.ReloadSettings();Application.DoEvents();
            Assert.Single(rows.Cast<object>());Assert.False(panel.IsDisposed);
        }
        finally {settings.DataSources=previous;}
    }

    [Fact]
    public async Task NavigationDiagnosticsDoNotWaitForTheDiskLoggerLock()
    {
        var logLock=typeof(Logger).GetField("SyncRoot",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        using var acquired=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var holder=Task.Run(()=> {lock(logLock){acquired.Set();release.Wait(TimeSpan.FromSeconds(10));}});
        try
        {
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(3)));
            var producer=Task.Run(()=>UiHealth.Note("[UI] nonblocking-navigation-test"));
            await producer.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally {release.Set();await holder.WaitAsync(TimeSpan.FromSeconds(3));}
    }
}
