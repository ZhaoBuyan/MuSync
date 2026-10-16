using System;
using System.Collections.Generic;
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

    private static readonly HttpClient Client = new()
    {
        // 本地服务也要走代理是很常见的事故来源，显式绕过
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

    /// <summary>
    /// 按需刷新一次（到期才发请求）；返回当前可用的显示文本（没有值时为 null）。
    /// 调用方每个节拍调用一次即可，内部的到期判断与退避都在这里处理。
    /// </summary>
    internal async Task<string?> RefreshIfDueAsync(DataSourceConfig source, CancellationToken token)
    {
        if (!source.Enabled || string.IsNullOrWhiteSpace(source.Url) || string.IsNullOrWhiteSpace(source.Name))
        {
            Display = null;
            return null;
        }

        var now = DateTime.UtcNow;
        // 数据过期（对端停了）就不再显示，避免把一个陈旧数值一直挂在状态里
        if (Display != null && LastSuccessUtc != DateTime.MinValue && now - LastSuccessUtc > StaleAfter)
        {
            Display = null;
        }

        if (now < _nextAttemptUtc) return Display;

        var interval = source.RefreshSeconds > 0 ? source.RefreshSeconds : 1;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.Url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            LastRawBody = body;

            if (!response.IsSuccessStatusCode)
            {
                Fail($"HTTP {(int)response.StatusCode}", interval);
                return Display;
            }

            var value = ExtractValue(body, source.ValuePath);
            if (value == null)
            {
                Fail(string.IsNullOrWhiteSpace(source.ValuePath)
                        ? "响应里没有取到值"
                        : $"按表达式 \"{source.ValuePath}\" 没有取到值",
                    interval);
                return Display;
            }

            Display = ApplyDisplayFormat(source.Format, value);
            LastSuccessUtc = DateTime.UtcNow;
            LastError = null;
            _loggedFailure = false;
            _nextAttemptUtc = DateTime.UtcNow.AddSeconds(interval);
            return Display;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 程序正在退出：保留当前值，不算失败
            return Display;
        }
        catch (Exception ex)
        {
            Fail(ex is TaskCanceledException ? "请求超时" : ex.Message, interval);
            return Display;
        }
    }

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

    private void Fail(string reason, int intervalSeconds)
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
        _ = intervalSeconds;   // 节流/退避时长由 FailureBackoff 决定，与刷新间隔无关
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
