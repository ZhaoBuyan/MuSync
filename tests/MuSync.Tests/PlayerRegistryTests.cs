using System;
using System.Linq;
using MuSync;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 播放器接入的「角色完整性」守卫。
/// <para>
/// 为什么需要它：播放器信息散落在五处 —— <c>RpcManager.DefaultPlayerOrder</c>（数据层）、
/// <c>ConfigData.PlayerPriority</c>（默认配置）、<c>SettingsForm.PlayerOrderKeys</c>（设置页键表）、
/// <c>MainForm._playerKeys</c> + <c>MainForm._playerColors</c>（界面）、<c>Loc.PlayerName</c>（显示名映射）。
/// 本测试把它们钉成"必须同时存在"。
/// </para>
/// </summary>
public class PlayerRegistryTests
{
    [Fact]
    public void DefaultPlayerOrder_ContainsAllPlayers()
    {
        // 六个来源：五个音乐播放器 + 浏览器媒体（视频）。
        // 浏览器媒体从"固定排最后、不进优先级列表"改成了与其它播放器平等参与优先级。
        Assert.Equal(6, RpcManager.DefaultPlayerOrder.Length);
        Assert.Contains("TTPlayer", RpcManager.DefaultPlayerOrder);
        Assert.Contains("Browser", RpcManager.DefaultPlayerOrder);
    }

    [Fact]
    public void ConfigDefaultPriority_MatchesBuiltInOrder()
    {
        // 三处口径必须一致：默认配置的优先级列表 == 数据层内置顺序 == 设置页键表
        var configDefault = new ConfigData().PlayerPriority;
        Assert.Equal(RpcManager.DefaultPlayerOrder, configDefault);
        Assert.Equal(RpcManager.DefaultPlayerOrder, SettingsForm.PlayerOrderKeys);
    }

    [Fact]
    public void PlayerColors_AlignWithPlayerKeys()
    {
        // 颜色表与键表一一对应：少一个就会在取色时越界/取错色
        Assert.Equal(MainForm._playerKeys.Length, MainForm._playerColors.Length);
    }

    [Fact]
    public void AccentColor_KnownPlayer_ReturnsThatPlayersBrandColor()
    {
        for (var i = 0; i < MainForm._playerKeys.Length; i++)
        {
            Assert.Equal(MainForm._playerColors[i], MainForm.GetPlayerAccentColor(MainForm._playerKeys[i]));
        }
    }

    /// <summary>
    /// 关键回归：调用方传的是**中文名**（<c>RpcManager.ResolveActiveState</c> 的口径），
    /// 而颜色表按 **key** 排列 —— 只认 key 的实现会让酷狗/千千静听静默掉进兜底色。
    /// </summary>
    [Fact]
    public void AccentColor_ChineseNameForm_ReturnsSameColorAsKeyForm()
    {
        for (var i = 0; i < MainForm._playerKeys.Length; i++)
        {
            var key = MainForm._playerKeys[i];
            var chineseName = MainForm.PlayerNameFor(key);
            Assert.False(string.IsNullOrEmpty(chineseName), $"{key} 缺中文名映射");
            Assert.Equal(MainForm._playerColors[i], MainForm.GetPlayerAccentColor(chineseName));
            Assert.Equal(MainForm.GetPlayerAccentColor(key), MainForm.GetPlayerAccentColor(chineseName));
        }
    }

    [Fact]
    public void AccentColor_UnknownName_ReturnsNeutralFallback()
    {
        // 认不出来的名字退回中性色，不得抛异常
        Assert.Equal(System.Drawing.Color.FromArgb(122, 120, 220), MainForm.GetPlayerAccentColor("来个不存在的播放器"));
    }

    [Fact]
    public void EveryPlayer_HasChineseDisplayName_IncludingChineseNameKeys()
    {
        // 界面显示的键有两种到达方式：数据层 key（NetEase）与中文名（网易云音乐）。
        // 两种写法都必须能被翻译，否则 Loc.PlayerName 会原样返回、英文界面下直接露中文。
        foreach (var key in RpcManager.DefaultPlayerOrder)
        {
            var name = MainForm.PlayerNameFor(key);
            Assert.False(string.IsNullOrWhiteSpace(name), $"{key} 缺中文显示名");
            Assert.Equal(name, Loc.PlayerName(name));
            Assert.DoesNotContain("NetEase", Loc.PlayerName(name), StringComparison.Ordinal);
            Assert.DoesNotContain("Tencent", Loc.PlayerName(name), StringComparison.Ordinal);
            Assert.DoesNotContain("LxMusic", Loc.PlayerName(name), StringComparison.Ordinal);
            Assert.DoesNotContain("KuGou", Loc.PlayerName(name), StringComparison.Ordinal);
            Assert.DoesNotContain("TTPlayer", Loc.PlayerName(name), StringComparison.Ordinal);
        }
    }
}
