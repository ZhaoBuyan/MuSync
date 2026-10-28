using System;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Models;

namespace MuSync.Utils;

/// <summary>
/// 自定义 HTTP 数据源：从任意返回 JSON 的 HTTP 地址取一个值，作为状态模板里的一个"积木"。
/// <para>
/// 用途举例：心率服务（本地 HTTP 返回 <c>{"heart_rate":92}</c>）、观众数、下载速度、天气 ——
/// 只要对方能用 HTTP 返回 JSON，就能通过"地址 + 取值表达式"接进来，不需要为本程序改一行代码。
/// </para>
/// <para>
/// ⚠️ 三条硬约束（都是"不能拖累同步"这一条要求的具体化）：
/// ① 全程异步 + 超时：绝不允许因为对端卡住而阻塞 233ms 的主循环节拍；
/// ② 失败必须降级为空：连不上 / 解析失败 / 字段不存在 / 值不是数字或文本 ⇒ 该积木展开为空，
///    不报错、不影响音乐与程序的同步；
/// ③ 连续失败要退避：对端关掉时不能每拍都去撞同一个失败点。
/// </para>
/// </summary>
internal sealed class HttpDataSource
{
    /// <summary>单次请求超时。取不到就当作"本拍没有值"，下一拍再来。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1.5);

    /// <summary>连续失败后的退避时长（期间不再发请求）。</summary>
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(5);

    /// <summary>成功读到值之间的最大间隔；超过即视为"数据已过期"（对端停了）。</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    /// <summary>过期阈值的宽限：覆盖一次请求超时 + 一次失败退避，避免对端慢一拍就判过期。</summary>
    private const double StaleGraceSeconds = 10;

    /// <summary>
    /// 共用的 HTTP 客户端。
    /// <para>
    /// ⚠️ 必须显式 <c>UseProxy = false</c>：数据源绝大多数是**本机**服务（心率、OBS 叠加等），
    /// 而 .NET 默认遵循系统代理 —— 用户开着 Clash / v2ray 这类工具时，对本机地址的请求会被送到代理，
    /// 结果要么 502 要么超时，用户只看到"积木永远是空的"，日志里也只有一句读取失败，根本查不到原因。
    /// </para>
    /// <para>
    /// <c>ConnectTimeout</c> 收紧到 1 秒：连不上就该尽快放弃，别把节拍拖住。
    /// <c>AutomaticDecompression</c> 与仓库里其它 HTTP 客户端保持一致 ——
    /// 对端返回 gzip 压缩的 JSON 时不自动解压会解不出内容（同样表现为积木静默为空）。
    /// </para>
    /// </summary>
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(1),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    })
    {
        Timeout = RequestTimeout
    };

    /// <summary>取到的值（已按 <see cref="DataSourceConfig.Format"/> 展开好的显示文本）。null = 当前没有值。</summary>
    internal string? Display { get; private set; }

    /// <summary>最近一次成功取值的时刻（供界面判断"数据是否已停止"）。</summary>
    internal DateTime LastSuccessUtc { get; private set; } = DateTime.MinValue;

    /// <summary>最近一次的错误描述（供界面与诊断显示）；null = 最近一次成功。</summary>
    internal string? LastError { get; private set; }

    /// <summary>最近一次拿到的原始响应体（供设置页的"测试"按钮展示，便于排查）。</summary>
    internal string? LastRawBody { get; private set; }

    private DateTime _nextAttemptUtc = DateTime.MinValue;
    private bool _loggedFailure;
    private int _refreshInFlight;

    /// <summary>上一次见到的配置指纹（URL / 取值 / 显示 / 间隔）。变了就立刻重置调度。</summary>
    private string _configFingerprint = "";

    /// <summary>配置代际：配置一变就 +1，用于丢弃"旧配置发出的请求"回来的结果。</summary>
    private int _generation;

    /// <summary>
    /// 到期就**在后台**发一次请求，**绝不阻塞调用方**；返回当前可用的显示文本（没有值时为 null）。
    /// <para>
    /// ⚠️ 为什么不能在这里 await：本方法由 233ms 的主循环节拍调用。若在这里等网络，
    /// 每个卡住的对端都会把整拍拖满客户端超时（实测单个 1.5 秒、三个串行 3 秒）——
    /// 音乐进度推送与程序检测都排在这之后，表现为"状态卡顿"，而且因为看门狗阈值是 30 秒，
    /// 这种退化**不会**被任何日志或告警暴露出来。所以：请求放后台，节拍只读缓存值。
    /// </para>
    /// </summary>
    internal string? RefreshIfDue(DataSourceConfig source, CancellationToken token)
    {
        // ① 配置指纹（**含启用状态**）：停用/启用同样是一次配置变化，必须让旧请求失效、调度归零。
        // 以前启用判断在这个块之前就直接 return 了，于是"停用 → 启用"不会重置 _nextAttemptUtc，
        // 长间隔的源重新启用后仍要等旧计划（最长一小时）。
        var fingerprint = ConfigFingerprint(source);
        if (fingerprint != _configFingerprint)
        {
            _configFingerprint = fingerprint;
            Interlocked.Increment(ref _generation);   // 停用前的在途请求回来也不会再写显示缓存
            Display = null;
            LastSuccessUtc = DateTime.MinValue;
            LastError = null;
            _nextAttemptUtc = DateTime.MinValue;
            _loggedFailure = false;
        }

        // ② 停用 / 没填地址 / 没填名字：没有可请求的东西
        if (!source.Enabled || string.IsNullOrWhiteSpace(source.Url) || string.IsNullOrWhiteSpace(source.Name))
        {
            Display = null;
            return null;
        }

        var now = DateTime.UtcNow;
        // 数据过期（对端停了）就不再显示，避免把陈旧数值一直挂在状态里。
        //
        // ⚠️ 阈值必须**跟着配置的刷新间隔走**：以前固定 10 秒，而设置页允许 1~3600 秒。
        // 配 60 秒时，第 11 秒数据就被判过期清空，直到下次请求回来才恢复 —— 表现为积木每
        // 分钟闪一下、大部分时间是空的。现在取 max(StaleAfter, 刷新间隔 + 宽限)，
        // 宽限覆盖一次请求超时 + 一次退避，够对端慢一拍也不会闪。
        var refreshSeconds = SourceIntervalSeconds(source);
        if (Display != null && IsStale(now, LastSuccessUtc, refreshSeconds))
        {
            Display = null;
        }
        // 未到期，或上一轮还没回来（对端慢）：直接给当前值，不排队、不叠加请求
        if (now < _nextAttemptUtc) return Display;
        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return Display;

        _nextAttemptUtc = now.AddSeconds(SourceIntervalSeconds(source));
          var snapshot = source;
          // ⚠️ 代际必须在**排队前**固定下来：如果等任务真正开跑时才读 _generation，
          // 期间用户改了配置（代际已 +1），旧任务会读到新代际 → 校验全部通过，
          // 于是"旧地址的响应"被写进新配置的显示缓存里。
          var queuedGeneration = Volatile.Read(ref _generation);
          _ = Task.Run(async () =>
          {
              try
              {
                  await FetchOnceAsync(snapshot, queuedGeneration, token).ConfigureAwait(false);
              }
              catch (Exception ex)
              {
                  // Task.Run 里的异常绝不能逃逸（会变成未观察任务异常）
                  Debug.WriteLine($"[数据源] 后台刷新异常: {ex.Message}");
              }
              finally
              {
                  Interlocked.Exchange(ref _refreshInFlight, 0);
              }
          }, CancellationToken.None);

        return Display;
    }

    /// <summary>
    /// 数据多久没更新算"过期"。阈值**跟着刷新间隔走**：
    /// <c>max(StaleAfter, 刷新间隔 + 宽限)</c>。
    /// <para>
    /// 以前固定 10 秒，而设置页允许 1~3600 秒：配 60 秒时，第 11 秒就被判过期清空，
    /// 直到下次请求回来才恢复 —— 表现为积木每分钟闪一下、大部分时间是空的。
    /// </para>
    /// </summary>
    internal static TimeSpan StaleWindowFor(int refreshSeconds) =>
        TimeSpan.FromSeconds(Math.Max(StaleAfter.TotalSeconds, refreshSeconds + StaleGraceSeconds));

    /// <summary>这份数据是否已经过期（纯函数，便于单测）。</summary>
    internal static bool IsStale(DateTime nowUtc, DateTime lastSuccessUtc, int refreshSeconds) =>
        lastSuccessUtc != DateTime.MinValue && nowUtc - lastSuccessUtc > StaleWindowFor(refreshSeconds);

    /// <summary>
    /// 影响"请求与显示"的配置指纹：URL / 取值表达式 / 显示格式 / 生效刷新间隔。
    /// <para>
    /// 变了就说明用户改过这条数据源，必须立刻重置调度（<c>_nextAttemptUtc</c>）并丢掉旧值 ——
    /// 否则把 3600 秒改成 1 秒后仍要等一小时，界面上却已经显示新配置。
    /// </para>
    /// </summary>
    internal static string ConfigFingerprint(DataSourceConfig source) =>
        string.Join("|", source.Enabled, source.Url, source.ValuePath, source.Format, SourceIntervalSeconds(source));

    private static int SourceIntervalSeconds(DataSourceConfig source) =>
        source.RefreshSeconds > 0 ? source.RefreshSeconds : 1;

    /// <summary>真正的网络请求（只在后台任务里跑）。</summary>
    private async Task FetchOnceAsync(DataSourceConfig source, int generation, CancellationToken token)
    {
        // 代际由调用方在**排队时**固定并传入（不能在这里重新读 _generation，否则旧任务会认领新代际）。
        // 否则"旧配置的请求"回来会把新配置的值覆盖掉，而且用户完全看不出发生了什么。
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation)) return;
            LastRawBody = body;

            if (!response.IsSuccessStatusCode)
            {
                Fail($"HTTP {(int)response.StatusCode}");
                return;
            }

            var value = ExtractValue(body, source.ValuePath);
            if (value == null)
            {
                Fail(string.IsNullOrWhiteSpace(source.ValuePath)
                    ? "响应里没有取到值"
                    : $"按表达式 \"{source.ValuePath}\" 没有取到值");
                return;
            }

            if (!IsCurrentGeneration(generation)) return;
            Display = ApplyDisplayFormat(source.Format, value);
            LastSuccessUtc = DateTime.UtcNow;
            LastError = null;
            _loggedFailure = false;
            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(SourceIntervalSeconds(source));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 程序正在退出：保留当前值，不算失败
        }
        catch (Exception ex)
        {
            if (!IsCurrentGeneration(generation)) return;
            Fail(ex is TaskCanceledException ? "请求超时" : ex.Message);
        }
    }

    /// <summary>发起请求时的配置代际是否仍然是当前配置（纯读取，无需加锁）。</summary>
    private bool IsCurrentGeneration(int generation) => Volatile.Read(ref _generation) == generation;

    /// <summary>「测试」按钮的结果：一次性的探测结果，用于界面显示与排查。</summary>
    internal readonly record struct ProbeResult(
        bool Ok, string? RawBody, string? Value, string? Display, string? Error);

    /// <summary>
    /// 一次性探测（给设置页的「测试」按钮用）：不做缓存、不参与退避，把每一步的中间结果都带回去。
    /// <para>
    /// 单独一个入口而不是复用 <see cref="RefreshIfDueAsync"/>：测试要看到"失败的原始响应"，
    /// 而正常路径只在失败时记一句错误 —— 配错地址/字段名时正是原始响应最有价值。
    /// </para>
    /// </summary>
    internal static async Task<ProbeResult> ProbeAsync(DataSourceConfig source)
    {
        if (string.IsNullOrWhiteSpace(source.Url))
        {
            return new ProbeResult(false, null, null, null, "地址为空");
        }
        // 先校验 URL：坏地址（"http://"、缺 scheme、含空格）会让 HttpRequestMessage 抛框架异常，
        // 那种错误文案对用户没有意义
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return new ProbeResult(false, null, null, null, "地址格式不正确（需要以 http:// 或 https:// 开头）");
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await Client.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ProbeResult(false, body, null, null, $"HTTP {(int)response.StatusCode}");
            }
            var value = ExtractValue(body, source.ValuePath);
            if (value == null)
            {
                return new ProbeResult(false, body, null, null,
                    string.IsNullOrWhiteSpace(source.ValuePath)
                        ? "响应里没有取到值"
                        : $"按表达式 \"{source.ValuePath}\" 没有取到值");
            }
            return new ProbeResult(true, body, value, ApplyDisplayFormat(source.Format, value), null);
        }
        catch (Exception ex)
        {
            return new ProbeResult(false, null, null, null,
                ex is TaskCanceledException ? "请求超时" : ex.Message);
        }
    }

    private void Fail(string reason)
    {
        LastError = reason;
        // 只在"从成功转为失败"时记一条日志，避免对端长期关着时刷屏
        if (!_loggedFailure)
        {
            _loggedFailure = true;
            Logger.Info($"[数据源] 读取失败：{reason}（将每 {FailureBackoff.TotalSeconds:F0} 秒重试一次）");
        }
        Display = null;
        _nextAttemptUtc = DateTime.UtcNow + FailureBackoff;
    }

    /// <summary>
    /// 按 <c>a.b.c</c> 形式的路径从 JSON 里取值（纯函数，便于单测）。
    /// <para>
    /// 返回字符串表示：JSON 数字用不变文化格式（避免在中文/德语等区域下出现千位分隔符或逗号小数点），
    /// 字符串原样返回，布尔返回 true/false。取不到、类型不支持（对象/数组/null）一律返回 null。
    /// </para>
    /// </summary>
    internal static string? ExtractValue(string? json, string? path)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var current = doc.RootElement;

            foreach (var segment in SplitPath(path))
            {
                if (current.ValueKind != JsonValueKind.Object) return null;
                if (!current.TryGetProperty(segment, out var next)) return null;
                current = next;
            }

            return current.ValueKind switch
            {
                JsonValueKind.Number => FormatNumber(current),
                JsonValueKind.String => current.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) yield break;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return segment;
        }
    }

    /// <summary>数字转字符串：整数不显示小数点（92 而不是 92.0），小数保留原始精度。</summary>
    private static string FormatNumber(JsonElement number)
    {
        if (number.TryGetInt64(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (number.TryGetDouble(out var d)) return d.ToString("0.###", CultureInfo.InvariantCulture);
        return number.GetRawText();
    }

    /// <summary>
    /// 把取到的值套进显示格式：<c>{v}</c> 替换为值本身；格式为空时直接显示值。
    /// 单趟替换，不重复扫描替换进来的内容。
    /// </summary>
    internal static string ApplyDisplayFormat(string? format, string value)
    {
        if (string.IsNullOrEmpty(format)) return value;
        // 用 Replace 是安全的：只替换 {v} 一个固定标记，且不会把 value 里的内容当模板（值里若含 {v} 也只替换一次）
        return format.Replace("{v}", value);
    }
}
