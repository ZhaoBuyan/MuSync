using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2.Discovery;

namespace MuSync.Utils;

/// <summary>
/// CM 候选排序（纯函数，便于单测）：把"连得上"的排前面，其余保持原有相对顺序。
/// <para>
/// 为什么需要：SteamKit 每次 Connect 只挑一个服务器，挑到被墙/已下线的就要等满连接超时
/// （本机 12 秒）才轮到下一个。日志里"单次尝试 15~25 秒、几次退避才连上"就是这么来的。
/// 先在本地对候选做一个几百毫秒的 TCP 探测，把通得过的放前面，能把这段等待压掉。
/// </para>
/// </summary>
internal static class CmServerOrdering
{
    /// <summary>按"可达"分组重排：可达的在前，其余保持原顺序（稳定排序）。</summary>
    internal static List<T> ReachableFirst<T>(IEnumerable<T> items, Func<T, bool> isReachable)
    {
        var all = items.ToList();
        var yes = new List<T>(all.Count);
        var no = new List<T>(all.Count);
        foreach (var item in all)
        {
            if (isReachable(item)) yes.Add(item);
            else no.Add(item);
        }
        yes.AddRange(no);
        return yes;
    }
}

/// <summary>
/// 服务器可达性探测：对候选端点做短超时的 TCP 连接测试。
/// <para>
/// 约束（都是刻意的）：
/// <list type="bullet">
/// <item>只探前若干个，不扫全表 —— 列表里有几十个，全探等于拖慢启动；</item>
/// <item>并发上限 8，单次超时 600ms，总预算 1.5 秒 —— 宁可少探也不能把连接拖住；</item>
/// <item>结果按 10 分钟缓存 —— 网络状况不会一分钟变一次，没必要每轮都探。</item>
/// </list>
/// </para>
/// </summary>
internal sealed class CmReachabilityProbe
{
    private const int MaxCandidates = 10;
    private const int MaxConcurrency = 8;
    private static readonly TimeSpan PerProbeTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheAtUtc = DateTime.MinValue;

    /// <summary>对候选排序；探测失败/超时一律当作"不可达"，不影响原有顺序的兜底。</summary>
    internal async Task<List<ServerRecord>> OrderAsync(IEnumerable<ServerRecord> servers,
        CancellationToken token = default)
    {
        var list = servers.ToList();
        if (list.Count <= 1) return list;
        if (DateTime.UtcNow - _cacheAtUtc > CacheLifetime) _cache.Clear();

        var candidates = list.Take(MaxCandidates).ToArray();
        await ProbeAllAsync(candidates, token).ConfigureAwait(false);
        return CmServerOrdering.ReachableFirst(list, record => _cache.TryGetValue(Key(record), out var ok) && ok);
    }

    private async Task ProbeAllAsync(ServerRecord[] candidates, CancellationToken token)
    {
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var pending = new List<Task>(candidates.Length);
        foreach (var record in candidates)
        {
            var key = Key(record);
            if (_cache.ContainsKey(key)) continue;
            pending.Add(Task.Run(async () =>
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    _cache[key] = await IsReachableAsync(record, token).ConfigureAwait(false);
                }
                catch
                {
                    _cache[key] = false;
                }
                finally
                {
                    gate.Release();
                }
            }, token));
        }
        if (pending.Count == 0) return;
        // 总预算：整体最多等 1.5 秒，超时就不管没回来的（它们保持"不可达"，排后面）
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(TimeSpan.FromMilliseconds(1500), token))
            .ConfigureAwait(false);
        _cacheAtUtc = DateTime.UtcNow;
    }

    private static async Task<bool> IsReachableAsync(ServerRecord record, CancellationToken token)
    {
        var host = record.GetHost();
        var port = record.GetPort();
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535) return false;

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(PerProbeTimeout);
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string Key(ServerRecord record) => record.GetHost() + ":" + record.GetPort();
}
