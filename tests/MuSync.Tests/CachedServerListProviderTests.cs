using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MuSync.Utils;
using SteamKit2;
using SteamKit2.Discovery;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// CM 服务器列表包装的守卫。
/// <para>
/// 起因（2026-11-11 实测）：本机 api.steampowered.com 不可达，SteamKit 每次 Connect 前都判定列表过期、
/// 去拉新列表，HTTP 一直等到超时 —— 表现为"连接一次要 15~25 秒"，并刷出未观察任务异常。
/// 包装后：只要缓存里有服务器，就把刷新时间报告为"刚刚"，SteamKit 不再去碰那个端点。
/// </para>
/// </summary>
public class CachedServerListProviderTests
{
    private sealed class FakeInner : IServerListProvider
    {
        private readonly List<ServerRecord> _servers;
        public int FetchCount { get; private set; }
        public int UpdateCount { get; private set; }
        public DateTime RefreshedAt { get; }

        public FakeInner(List<ServerRecord> servers, DateTime refreshedAt)
        {
            _servers = servers;
            RefreshedAt = refreshedAt;
        }

        public DateTime LastServerListRefresh => RefreshedAt;

        public Task<IEnumerable<ServerRecord>> FetchServerListAsync()
        {
            FetchCount++;
            return Task.FromResult<IEnumerable<ServerRecord>>(_servers);
        }

        public Task UpdateServerListAsync(IEnumerable<ServerRecord> endpoints)
        {
            UpdateCount++;
            return Task.CompletedTask;
        }
    }

    private static ServerRecord Record(string endpoint)
    {
        var i = endpoint.LastIndexOf(':');
        return ServerRecord.CreateServer(endpoint[..i], int.Parse(endpoint[(i + 1)..]), ProtocolTypes.Tcp);
    }

    [Fact]
    public async Task NonEmptyCache_ReportsJustRefreshed_SoSteamKitSkipsTheDirectory()
    {
        var inner = new FakeInner([Record("1.2.3.4:27017")], new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var wrapper = new CachedServerListProvider(inner);

        var before = DateTime.UtcNow;
        _ = await wrapper.FetchServerListAsync();
        var reported = wrapper.LastServerListRefresh;

        Assert.True(reported >= before, "读到缓存后应报告为刚刚刷新，否则会去连不可达的 Steam Directory");
        Assert.NotEqual(inner.LastServerListRefresh, reported);
    }

    [Fact]
    public async Task EmptyCache_KeepsInnerTimestamp_SoRefreshStillHappens()
    {
        // 真正的首次运行（缓存为空）必须允许它去拉列表，否则永远连不上
        var inner = new FakeInner([], new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var wrapper = new CachedServerListProvider(inner);

        _ = await wrapper.FetchServerListAsync();
        Assert.Equal(inner.LastServerListRefresh, wrapper.LastServerListRefresh);
    }

    [Fact]
    public async Task BeforeFirstFetch_TimestampComesFromInner()
    {
        var inner = new FakeInner([Record("1.2.3.4:27017")], new DateTime(2021, 5, 5, 0, 0, 0, DateTimeKind.Utc));
        var wrapper = new CachedServerListProvider(inner);
        Assert.Equal(inner.LastServerListRefresh, wrapper.LastServerListRefresh);
    }

    [Fact]
    public async Task FetchReturnsTheCachedServers_AndUpdateDelegates()
    {
        var inner = new FakeInner([Record("1.2.3.4:27017"), Record("5.6.7.8:27017")],
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var wrapper = new CachedServerListProvider(inner);

        var servers = new List<ServerRecord>(await wrapper.FetchServerListAsync());
        Assert.Equal(2, servers.Count);

        await wrapper.UpdateServerListAsync(servers);
        Assert.Equal(1, inner.UpdateCount);
        Assert.Equal(1, inner.FetchCount);
    }
}
