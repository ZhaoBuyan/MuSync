using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class DataSourceProbeLifetimeTests
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    private static T Field<T>(object view,string name)=>(T)view.GetType().GetField(name,Flags)!.GetValue(view)!;
    private static Task Start(SettingsView view)=>(Task)view.GetType().GetMethod("TestDataSourcesAsync",Flags)!.Invoke(view,null)!;
    private static object FirstRow(SettingsView view)=>Field<IList>(view,"_dataSourceRows")[0]!;
    private static T Row<T>(object row,string name)=>(T)row.GetType().GetProperty(name)!.GetValue(row)!;
    private static HttpDataSource.ProbeResult Result(string value)=>new(true,"{}",value,value,null);

    [Fact]
    public void ReloadedProbeCannotOverwriteOrUnlockANewerRun()
    {
        var settings=Configurations.Instance.Settings;var previous=settings.DataSources;
        var old=new TaskCompletionSource<HttpDataSource.ProbeResult>();
        var current=new TaskCompletionSource<HttpDataSource.ProbeResult>();
        try
        {
            settings.DataSources=[new DataSourceConfig{Name="fixture",Url="http://old.local"}];
            using var view=new SettingsView();_ = view.Handle;
            var count=0;
            typeof(SettingsView).GetField("_probeDataSource",Flags)!.SetValue(view,
                (Func<DataSourceConfig,Task<HttpDataSource.ProbeResult>>)(_=>++count==1?old.Task:current.Task));
            var first=Start(view);
            settings.DataSources[0].Url="http://new.local";view.ReloadSettings();
            var button=Field<Button>(view,"_dataSourceTestButton");Assert.True(button.Enabled);
            var status=Row<Label>(FirstRow(view),"Status");status.Text="new-page-marker";
            var second=Start(view);Assert.False(button.Enabled);
            old.SetResult(Result("old-value"));Application.DoEvents();
            Assert.True(first.IsCompletedSuccessfully);Assert.False(button.Enabled);
            Assert.Equal("new-page-marker",status.Text);
            current.SetResult(Result("new-value"));Application.DoEvents();
            Assert.True(second.IsCompletedSuccessfully);Assert.True(button.Enabled);
            Assert.Contains("new-value",status.Text);Assert.DoesNotContain("old-value",status.Text);
        }
        finally {old.TrySetResult(Result("unused"));current.TrySetResult(Result("unused"));Application.DoEvents();settings.DataSources=previous;}
    }

    [Fact]
    public void EditingTheRequestFieldsDiscardsItsOldResult()
    {
        var settings=Configurations.Instance.Settings;var previous=settings.DataSources;
        var pending=new TaskCompletionSource<HttpDataSource.ProbeResult>();
        try
        {
            settings.DataSources=[new DataSourceConfig{Name="fixture",Url="http://old.local"}];
            using var view=new SettingsView();_ = view.Handle;
            typeof(SettingsView).GetField("_probeDataSource",Flags)!.SetValue(view,
                (Func<DataSourceConfig,Task<HttpDataSource.ProbeResult>>)(_=>pending.Task));
            var task=Start(view);var row=FirstRow(view);
            Row<TextBox>(row,"Url").Text="http://changed.local";
            var status=Row<Label>(row,"Status");status.Text="edited-marker";
            pending.SetResult(Result("old-value"));Application.DoEvents();
            Assert.True(task.IsCompletedSuccessfully);Assert.Equal("edited-marker",status.Text);
            Assert.True(Field<Button>(view,"_dataSourceTestButton").Enabled);
            Assert.Equal(Loc.L("配置已变化，请重新测试。","The configuration changed. Please test again."),Field<Label>(view,"_dataSourceHint").Text);
        }
        finally {pending.TrySetResult(Result("unused"));Application.DoEvents();settings.DataSources=previous;}
    }

    [Fact]
    public void CompletingAProbeAfterDisposalDoesNotTouchDeadControls()
    {
        var settings=Configurations.Instance.Settings;var previous=settings.DataSources;
        var pending=new TaskCompletionSource<HttpDataSource.ProbeResult>();
        try
        {
            settings.DataSources=[new DataSourceConfig{Name="fixture",Url="http://fixture.local"}];
            using var view=new SettingsView();_ = view.Handle;
            typeof(SettingsView).GetField("_probeDataSource",Flags)!.SetValue(view,
                (Func<DataSourceConfig,Task<HttpDataSource.ProbeResult>>)(_=>pending.Task));
            var task=Start(view);view.Dispose();pending.SetResult(Result("ignored"));Application.DoEvents();
            Assert.True(task.IsCompletedSuccessfully);
        }
        finally {pending.TrySetResult(Result("unused"));Application.DoEvents();settings.DataSources=previous;}
    }
}
