using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamKit2.Discovery;

namespace MuSync.Utils;

/// <summary>
/// 服务器列表提供者的包装：**报告"刚刷新过"，从而彻底绕开 Steam Directory（api.steampowered.com）**。
/// <para>
/// 为什么需要（2026-11-11 实测）：本机网络里 <c>api.steampowered.com:443</c> 不可达。
/// SteamKit 在每次 <c>Connect()</c> 前会检查 <see cref="IServerListProvider.LastServerListRefresh"/>，
/// 判定列表"过期"就去 Steam Directory 拉新列表 —— 那次 HTTP 请求在本机会一直等到超时，
/// 于是每次连接尝试都白白烧掉十几二十秒，还刷出一串 HttpRequestException 与"未观察任务异常"。
/// 日志表现就是"连接很慢"：单次尝试 15~25 秒、退避后重试，几分钟才连上。
/// </para>
/// <para>
/// 做法：缓存文件里的 CM 列表仍然照常读取（<see cref="FileStorageServerListProvider"/> 负责解析），
/// 但只要**读到非空列表**，就把刷新时间报告为"现在"，SteamKit 便不再去碰那个不可达的端点。
/// 列表需要更新时：删除 <c>%LocalAppData%\MuSync\cm-serverlist.json</c> 即可，
/// 下次启动会重新从目录服务拉取（网络可达时）。
/// </para>
/// </summary>
internal sealed class CachedServerListProvider : IServerListProvider
{
    private readonly IServerListProvider _inner;

    /// <summary>最近一次我们亲眼读到非空列表的时刻（仅在内存里，不写盘）。</summary>
    private DateTime _servedAt = DateTime.MinValue;

    public CachedServerListProvider(IServerListProvider inner) => _inner = inner;

    /// <summary>
    /// 列表非空时报告"刚刚刷新"，阻止 SteamKit 去连不可达的 Steam Directory；
    /// 列表为空（首次运行 / 缓存损坏）时如实返回，让它按原逻辑去拉取。
    /// </summary>
    public DateTime LastServerListRefresh =>
        _servedAt != DateTime.MinValue ? _servedAt : _inner.LastServerListRefresh;

    public async Task<IEnumerable<ServerRecord>> FetchServerListAsync()
    {
        var servers = await _inner.FetchServerListAsync().ConfigureAwait(false);
        var list = servers as IList<ServerRecord> ?? new List<ServerRecord>(servers);
        if (list.Count > 0) _servedAt = DateTime.UtcNow;
        return list;
    }

    public Task UpdateServerListAsync(IEnumerable<ServerRecord> endpoints) =>
        _inner.UpdateServerListAsync(endpoints);
}
