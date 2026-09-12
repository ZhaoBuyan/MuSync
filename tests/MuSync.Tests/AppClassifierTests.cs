using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

public class AppClassifierTests
{
    [Fact]
    public void Suggest_KnownProgram_ReturnsDictionaryHit()
    {
        var (category, name, fromDictionary) = AppClassifier.Suggest("yuanshen.exe", "", false);
        Assert.Equal(AppCategory.Game, category);
        Assert.Equal("原神", name);
        Assert.True(fromDictionary);
    }

    [Fact]
    public void Suggest_FullscreenUnknownProgram_ReturnsGameSuggestion()
    {
        var (category, _, fromDictionary) = AppClassifier.Suggest("weird.exe", "", true);
        Assert.Equal(AppCategory.Game, category);
        Assert.False(fromDictionary);
    }

    [Fact]
    public void Suggest_KeywordGame_ReturnsGame()
    {
        var (category, _, _) = AppClassifier.Suggest("awesomegame.exe", "", false);
        Assert.Equal(AppCategory.Game, category);
    }

    [Fact]
    public void IsBuiltInIgnored_IsCaseInsensitive()
    {
        Assert.True(AppClassifier.IsBuiltInIgnored("explorer.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("EXPLORER.EXE"));
        Assert.False(AppClassifier.IsBuiltInIgnored("game.exe"));
    }

    [Fact]
    public void IsBuiltInIgnored_MusicPlayers_AreExcluded()
    {
        // 音乐播放器走音乐同步逻辑，不参与程序同步
        Assert.True(AppClassifier.IsBuiltInIgnored("cloudmusic.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("QQMusic.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("lx-music-desktop.exe"));
    }
}

public class User32SmokeTests
{
    [Fact]
    public void GetWindowTitle_InvalidHandle_DoesNotThrow()
    {
        // 回归保护：GetWindowTextLength 的 P/Invoke 必须使用 GetWindowTextLengthW
        var title = MuSync.Win32Api.User32.GetWindowTitle(System.IntPtr.Zero);
        Assert.Equal("", title);
    }

    [Fact]
    public void TryGetWindowBounds_InvalidHandle_ReturnsFalse()
    {
        var ok = MuSync.Win32Api.User32.TryGetWindowBounds(System.IntPtr.Zero, out _);
        Assert.False(ok);
    }
}
