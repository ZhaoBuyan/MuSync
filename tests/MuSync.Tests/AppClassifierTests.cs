using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>程序分类器测试：常见程序字典命中与内置忽略名单（音乐播放器等）。</summary>
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
        Assert.True(AppClassifier.IsBuiltInIgnored("KuGou.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("TTPlayerRebuild.exe"));
    }

    [Fact]
    public void IsBuiltInIgnored_LaunchersAndAntiCheat_AreExcluded()
    {
        // 反作弊 / 运行时辅助进程会随游戏一起出现，不该单独成为"正在玩的程序"
        Assert.True(AppClassifier.IsBuiltInIgnored("AntiCheatExpert.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("EasyAntiCheat.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("SGuardSvc64.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("mhyprot3.exe"));
        // 腾讯 ACE 的辅助进程与 BattlEye 的启动器：都在实机进程里见过抢前台
        Assert.True(AppClassifier.IsBuiltInIgnored("ACE-Helper.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("aces.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("aces_BE.exe"));
        // 安装 / 更新程序
        Assert.True(AppClassifier.IsBuiltInIgnored("unins000.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("msiexec.exe"));
    }

    [Theory]
    [InlineData("Calabiyau-Win64-Shipping.exe", (int)AppCategory.Game, "卡拉彼丘")]
    [InlineData("Plain Craft Launcher 2.exe", (int)AppCategory.Game, "Plain Craft Launcher")]
    [InlineData("uu.exe", (int)AppCategory.Other, "UU 加速器")]
    [InlineData("oopz.exe", (int)AppCategory.Social, "Oopz")]
    public void Suggest_RealMachineProcesses_FromUserConfig_AreRecognized(
        string exeName, int expectedCategory, string expectedName)
    {
        // 这些名字来自用户实机的 config.json 里"未被收录"的条目，按实际进程名精确补入
        var (category, name, fromDictionary) = AppClassifier.Suggest(exeName, "", false);

        Assert.Equal((AppCategory)expectedCategory, category);
        Assert.Equal(expectedName, name);
        Assert.True(fromDictionary, $"{exeName} 应命中内置程序名库");
    }

    [Fact]
    public void IsBuiltInIgnored_ShellAndBrowserChildProcesses_AreExcluded()
    {
        // 开始菜单 / WebView 子进程 / Steam 后台：常驻且容易抢前台，必须忽略
        Assert.True(AppClassifier.IsBuiltInIgnored("searchapp.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("msedgewebview2.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("steamwebhelper.exe"));
        Assert.True(AppClassifier.IsBuiltInIgnored("textinputhost.exe"));
    }

    [Theory]
    [InlineData("MiSideFull.exe", (int)AppCategory.Game, "米塔 MiSide")]
    [InlineData("CalabiYau.exe", (int)AppCategory.Game, "卡拉彼丘")]
    [InlineData("osu!.exe", (int)AppCategory.Game, "osu!")]
    [InlineData("LobotomyCorp.exe", (int)AppCategory.Game, "脑叶公司")]
    [InlineData("studio64.exe", (int)AppCategory.Work, "Android Studio")]
    [InlineData("unity.exe", (int)AppCategory.Work, "Unity")]
    [InlineData("kicad.exe", (int)AppCategory.Work, "KiCad")]
    [InlineData("fl64.exe", (int)AppCategory.Work, "FL Studio")]
    [InlineData("docker desktop.exe", (int)AppCategory.Work, "Docker Desktop")]
    [InlineData("JianyingPro.exe", (int)AppCategory.Work, "剪映专业版")]
    [InlineData("obs64.exe", (int)AppCategory.Media, "OBS Studio")]
    [InlineData("potplayer.exe", (int)AppCategory.Media, "PotPlayer")]
    [InlineData("KOOK.exe", (int)AppCategory.Social, "KOOK")]
    [InlineData("lark.exe", (int)AppCategory.Social, "飞书")]
    [InlineData("quark.exe", (int)AppCategory.Other, "夸克")]
    [InlineData("uu_launcher.exe", (int)AppCategory.Other, "UU 加速器")]
    [InlineData("Battle.net.exe", (int)AppCategory.Other, "战网")]
    public void Suggest_ExpandedLibrary_ReturnsExpectedCategoryAndName(
        string exeName, int expectedCategory, string expectedName)
    {
        var (category, name, fromDictionary) = AppClassifier.Suggest(exeName, "", false);
        Assert.Equal((AppCategory)expectedCategory, category);
        Assert.Equal(expectedName, name);
        Assert.True(fromDictionary, $"{exeName} 应命中内置程序名库");
    }

    [Fact]
    public void Suggest_ExpandedLibrary_IsCaseInsensitive()
    {
        var (category, name, _) = AppClassifier.Suggest("MISIDEFULL.EXE", "", false);
        Assert.Equal(AppCategory.Game, category);
        Assert.Equal("米塔 MiSide", name);
    }
}

/// <summary>User32 冒烟测试：无效句柄等边界下 P/Invoke 不得抛异常（回归保护）。</summary>
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
