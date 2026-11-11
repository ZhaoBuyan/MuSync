using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MuSync.Utils;
using SteamKit2;
using SteamKit2.Discovery;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// CM 可达性排序的守卫。
/// <para>
/// 起因：SteamKit 每次 Connect 只试一个服务器，挑到被墙/下线的就要等满连接超时（本机 12 秒）。
/// 日志里"单次尝试 15~25 秒、几次退避才连上"就是这个原因。排序把连得上的放前面。
/// </para>
/// </summary>
public class CmServerOrderingTests
{
    private static ServerRecord Record(string hostPort)
    {
        var i = hostPort.LastIndexOf(':');
        return ServerRecord.CreateServer(hostPort[..i], int.Parse(hostPort[(i + 1)..]), ProtocolTypes.Tcp);
    }

    private static string EndpointOf(ServerRecord r) => r.GetHost() + ":" + r.GetPort();

    [Fact]
    public void ReachableFirst_PutsReachableOnesAhead_AndKeepsRelativeOrder()
    {
        var items = new[] { "a", "b", "c", "d" };
        var reachable = new HashSet<string> { "c", "a" };

        var ordered = CmServerOrdering.ReachableFirst(items, x => reachable.Contains(x));
        Assert.Equal(new[] { "a", "c", "b", "d" }, ordered);
    }

    [Fact]
    public void ReachableFirst_AllReachable_OrderUnchanged()
    {
        var items = new[] { "x", "y", "z" };
        Assert.Equal(items, CmServerOrdering.ReachableFirst(items, _ => true));
    }

    [Fact]
    public void ReachableFirst_NoneReachable_OrderUnchanged()
    {
        var items = new[] { "x", "y", "z" };
        Assert.Equal(items, CmServerOrdering.ReachableFirst(items, _ => false));
    }

    [Fact]
    public void ReachableFirst_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(CmServerOrdering.ReachableFirst(Array.Empty<string>(), _ => true));
    }

    [Fact]
    public async Task Probe_LocalListeningPort_CountsAsReachable_ClosedPortDoesNot()
    {
        // 起一个本地监听端口作为"可达"样本，同时探测一个关闭的端口作为反例
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var probe = new CmReachabilityProbe();
            var servers = new List<ServerRecord>
            {
                Record("127.0.0.1:1"),                  // 不可达
                Record("127.0.0.1:" + port),            // 本机监听，可达
            };

            var ordered = await probe.OrderAsync(servers);
            Assert.Equal("127.0.0.1:" + port, EndpointOf(ordered[0]));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Probe_SingleItem_ShortCircuits()
    {
        var probe = new CmReachabilityProbe();
        var servers = new List<ServerRecord> { Record("127.0.0.1:1") };
        var ordered = await probe.OrderAsync(servers);
        Assert.Single(ordered);
    }
}
