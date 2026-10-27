using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

/// <summary>
/// 端口绑定失败后的恢复：重启时端口可能还在被旧进程占着 / 处于 TIME_WAIT。
/// 旧行为是"失败一次就永不重试"，扩展增强会一直显示"未监听"（用户看得见的故障）。
/// </summary>
public class BrowserExtensionBridgeBindTests
{
    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public void OccupiedPort_FailsGracefully_WithReason()
    {
        var port = FindFreePort();
        var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();
        try
        {
            BrowserExtensionBridge.Ensure(enabled: true, port: port);

            Assert.False(BrowserExtensionBridge.IsRunning);
            Assert.False(string.IsNullOrWhiteSpace(BrowserExtensionBridge.LastError));
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
            occupier.Stop();
        }
    }

    [Fact]
    public void AfterPortIsFreed_CanBindAgain()
    {
        var port = FindFreePort();
        var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();
        try
        {
            BrowserExtensionBridge.Ensure(enabled: true, port: port);
            Assert.False(BrowserExtensionBridge.IsRunning);
        }
        finally
        {
            occupier.Stop();
        }

        // 关掉再打开（等同用户在设置里重新勾选）：应能成功绑定
        BrowserExtensionBridge.Ensure(enabled: false, port: port);
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            Assert.True(BrowserExtensionBridge.IsRunning);
            Assert.Equal("", BrowserExtensionBridge.LastError);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }

    [Fact]
    public void RebindSamePort_ImmediatelyAfterStop_Succeeds()
    {
        // 模拟"重启后立刻重绑"：关掉马上再开，不能因为 TIME_WAIT 失败
        var port = FindFreePort();
        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            Assert.True(BrowserExtensionBridge.IsRunning);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }

        BrowserExtensionBridge.Ensure(enabled: true, port: port);
        try
        {
            Assert.True(BrowserExtensionBridge.IsRunning);
        }
        finally
        {
            BrowserExtensionBridge.Ensure(enabled: false, port: port);
        }
    }
}
