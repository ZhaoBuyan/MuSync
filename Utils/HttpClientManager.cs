using System;
using System.Net;
using System.Net.Http;
using System.Threading;
namespace MuSync.Utils;
/// <summary>全局共享 HttpClient（单例）：统一超时与自动解压，避免重复创建连接池。</summary>
internal static class HttpClientManager
{
    private static readonly Lazy<HttpClient> SharedClientLazy = new(() =>
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler)
        {
            Timeout = PerformanceConfig.HttpClientTimeout,
        };
        return client;
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    public static HttpClient SharedClient => SharedClientLazy.Value;
}