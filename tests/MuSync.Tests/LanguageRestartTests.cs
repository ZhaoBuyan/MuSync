using System.Reflection;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class LanguageRestartTests
{
    [Fact]
    public void SavedLanguageDoesNotHalfTranslateAnAlreadyRunningProcess()
    {
        var settings = Configurations.Instance.Settings;
        var language = settings.Language;
        var startup = typeof(Loc).GetField("_startupLanguage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = startup.GetValue(null);
        try
        {
            settings.Language = 0;
            Loc.InitializeForCurrentProcess();
            settings.Language = 1; // 保存下次启动的语言，不影响当前控件及动态状态。
            Assert.False(Loc.IsEnglish);
            Assert.Equal("中文", Loc.L("中文", "English"));
            Loc.InitializeForCurrentProcess(); // 模拟下一次启动读取已保存语言。
            Assert.True(Loc.IsEnglish);
            Assert.Equal("English", Loc.L("中文", "English"));
        }
        finally
        {
            settings.Language = language;
            startup.SetValue(null, previous);
        }
    }
}
