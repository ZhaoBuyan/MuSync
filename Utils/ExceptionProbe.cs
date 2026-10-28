using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace MuSync.Utils;

/// <summary>
/// 异常计数探针：**第一机会异常**（包括被 catch 住的）按「类型 + 抛出程序集 + 抛出方法」汇总，
/// 每分钟写一条日志。
/// <para>
/// 为什么需要：被 catch 住的异常不会出现在日志里，但每次抛都要抓栈（真实 CPU 开销），
/// 而且往往说明代码在用异常做正常流程控制。实测线上稳定 5 次/秒的异常率、日志里却一条都没有，
/// 没有这个探针就查不到源头。
/// </para>
/// <para>
/// 实测结论（2026-10-28）：主要来源是 SteamKit2 内部（<c>System.Threading.Tasks.Dataflow</c> 的超时，
/// 被 SK2 自己吃掉，属第三方行为）与「数据源端口没监听」时的 HttpClient 超时 —— 都不是本项目的逻辑缺陷。
/// </para>
/// <para>
/// 开销：每次异常一次字典自增；没有异常时每分钟连日志都不写。
/// </para>
/// </summary>
internal static class ExceptionProbe
{
    private static readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);
    private static int _total;
    private static bool _stackCaptured;
    private static Timer? _timer;

    /// <summary>安装探针。重复调用无副作用。</summary>
    public static void Install()
    {
        if (_timer != null) return;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            Interlocked.Increment(ref _total);
            var key = Describe(e.Exception);
            Counts.AddOrUpdate(key, 1, static (_, current) => current + 1);

            // 每个汇总周期抓一条超时的完整栈：异常类型+程序集有时还不够定位，
            // 完整栈能看出是哪一层在等（例如 SK2 的哪条内部通道）。
            if (e.Exception is TimeoutException && !_stackCaptured)
            {
                _stackCaptured = true;
                Logger.Info("[异常统计] 超时完整栈：" + e.Exception.StackTrace);
            }
        };
        // 60 秒汇总一次；Timer 必须被静态字段持有，否则会被 GC 回收
        _timer = new Timer(static _ => Dump(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    private static string Describe(Exception ex)
    {
        // Source = 抛出异常的程序集（SteamKit2 / System.Net.Http / …），TargetSite = 抛出方法
        var source = string.IsNullOrEmpty(ex.Source) ? "?" : ex.Source;
        var declaring = ex.TargetSite?.DeclaringType?.FullName ?? "?";
        var method = ex.TargetSite?.Name ?? "?";
        return ex.GetType().Name + " | " + source + " | " + declaring + "." + method;
    }

    private static void Dump()
    {
        var total = Interlocked.Exchange(ref _total, 0);
        _stackCaptured = false;
        if (total == 0)
        {
            Counts.Clear();
            return;
        }

        var top = new List<KeyValuePair<string, int>>(Counts);
        top.Sort(static (a, b) => b.Value.CompareTo(a.Value));
        var builder = new StringBuilder();
        foreach (var pair in top)
        {
            if (builder.Length > 0) builder.Append("；");
            builder.Append(pair.Key).Append(" ×").Append(pair.Value);
            if (builder.Length > 500) break;
        }
        Counts.Clear();
        Logger.Info($"[异常统计] 最近 60 秒第一机会异常共 {total} 次：{builder}");
    }
}
