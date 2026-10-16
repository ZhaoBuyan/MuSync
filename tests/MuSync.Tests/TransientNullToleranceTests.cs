using System;
using System.Collections.Generic;
using System.Linq;
using MuSync;
using MuSync.Models;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 「单次读取失败」容忍逻辑的测试。
/// <para>
/// 背景：SMTC 一类来源会偶发读不到（元数据掉一拍）。若让 null 直接覆盖 LastPolledInfo，
/// 会连锁造成两种可见问题：活跃源在两三拍内来回跳、以及内容没变却被反复重推状态。
/// 这里钉住"短窗口内沿用上一次有效内容、超过窗口如实清空"的口径。
/// </para>
/// </summary>
public class TransientNullToleranceTests
{
    private static PlayerInfo Info(string title) => new()
    {
        Identity = "k",
        Title = title,
        Artists = "",
        Album = "",
        Cover = "",
        Schedule = 10,
        Duration = 100,
        Url = "",
        Pause = false
    };

    private static readonly DateTime T0 = new(2026, 10, 16, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SuccessfulRead_IsStoredAndReturned()
    {
        var state = new RpcManager.PlayerState();
        var info = Info("歌一");

        var result = RpcManager.HoldThroughTransientNull(state, info, T0);

        Assert.Equal("歌一", result!.Value.Title);
        Assert.Equal("歌一", state.LastGoodInfo!.Value.Title);
        Assert.Equal(T0, state.LastGoodInfoTime);
    }

    [Fact]
    public void TransientFailure_WithinGrace_KeepsPreviousInfo()
    {
        // 掉一拍（宽限窗口内）：仍返回上一次的内容，避免活跃源来回跳、状态被反复重推
        var state = new RpcManager.PlayerState();
        RpcManager.HoldThroughTransientNull(state, Info("歌一"), T0);

        var result = RpcManager.HoldThroughTransientNull(state, null, T0.AddMilliseconds(300));

        Assert.NotNull(result);
        Assert.Equal("歌一", result!.Value.Title);
    }

    [Fact]
    public void Failure_BeyondGrace_ClearsInfo()
    {
        // 超出窗口（例如真换歌间隙 / 播放器关了）必须如实清空，不能被容忍逻辑掩盖
        var state = new RpcManager.PlayerState();
        RpcManager.HoldThroughTransientNull(state, Info("歌一"), T0);

        var result = RpcManager.HoldThroughTransientNull(state, null, T0.AddSeconds(5));

        Assert.Null(result);
    }

    [Fact]
    public void Failure_WithNoPreviousSuccess_StaysNull()
    {
        var state = new RpcManager.PlayerState();

        var result = RpcManager.HoldThroughTransientNull(state, null, T0);

        Assert.Null(result);
    }

    [Fact]
    public void RecoveringRead_AfterTransientFailure_ReturnsNewInfo()
    {
        var state = new RpcManager.PlayerState();
        RpcManager.HoldThroughTransientNull(state, Info("歌一"), T0);
        RpcManager.HoldThroughTransientNull(state, null, T0.AddMilliseconds(200));

        var result = RpcManager.HoldThroughTransientNull(state, Info("歌二"), T0.AddMilliseconds(400));

        Assert.Equal("歌二", result!.Value.Title);
        Assert.Equal("歌二", state.LastGoodInfo!.Value.Title);
        Assert.Equal(T0.AddMilliseconds(400), state.LastGoodInfoTime);
    }

    [Fact]
    public void RepeatedFailures_DoNotExtendTheWindowForever()
    {
        // 关键：连续失败时窗口以"最后一次成功"为基准，不能靠反复失败把旧内容无限延长
        var state = new RpcManager.PlayerState();
        RpcManager.HoldThroughTransientNull(state, Info("歌一"), T0);
        RpcManager.HoldThroughTransientNull(state, null, T0.AddMilliseconds(500));
        RpcManager.HoldThroughTransientNull(state, null, T0.AddMilliseconds(900));

        var result = RpcManager.HoldThroughTransientNull(state, null, T0.AddSeconds(2));

        Assert.Null(result);
    }

    [Fact]
    public void CleanupPlayerState_ClearsTheHoldCache()
    {
        // 播放器消失后不能靠宽限窗口把旧内容复活（否则会有最长 1 秒的陈旧显示）
        var state = new RpcManager.PlayerState();
        RpcManager.HoldThroughTransientNull(state, Info("歌一"), T0);

        RpcManager.CleanupPlayerState(state, "测试");

        Assert.Null(state.LastGoodInfo);
        Assert.Equal(DateTime.MinValue, state.LastGoodInfoTime);
        Assert.Null(state.LastPolledInfo);
        // 清理之后紧接一次失败读取，不应再拿旧内容
        Assert.Null(RpcManager.HoldThroughTransientNull(state, null, T0.AddMilliseconds(100)));
    }

    // ---- 数据源实例集合的对齐（改名/删除后旧积木必须消失）----

    [Fact]
    public void PruneDataSourceInstances_RemovesNamesNoLongerConfigured()
    {
        // 用户把「心率」改名为「心跳」：旧名字若留在集合里，它会因为再也不被刷新而永远不被过期判定清空，
        // 于是模板里手写的旧积木 {心率} 会一直取到几分钟前的陈旧读数。
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["心率"] = "❤92",
            ["天气"] = "晴"
        };
        var configuredNames = new[] { "心跳", "天气" };

        var removed = RpcManager.PruneDataSourceInstancesForTest(installed, configuredNames);

        Assert.Equal(["心率"], removed);
        Assert.False(installed.ContainsKey("心率"));
        Assert.True(installed.ContainsKey("天气"));   // 仍然配置着的不许误伤
        Assert.True(installed.ContainsKey("心跳") == false);  // 只做清理，不负责新建
    }

    [Fact]
    public void PruneDataSourceInstances_KeepsEverythingWhenAllConfigured()
    {
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["心率"] = "❤92" };

        var removed = RpcManager.PruneDataSourceInstancesForTest(installed, ["心率"]);

        Assert.Empty(removed);
        Assert.True(installed.ContainsKey("心率"));
    }
}