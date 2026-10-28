using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MuSync.Utils;

/// <summary>扩展上报的播放状态快照（已做过裁剪与范围校验）。</summary>
internal sealed record BrowserExtensionSnapshot(
    string Title,
    string Artist,
    string Album,
    double PositionSeconds,
    double DurationSeconds,
    bool IsPlaying,
    string Site,
    DateTime ReceivedUtc,
    /// <summary>页面是否在前台（老版本扩展不发这个字段时为 false）。</summary>
    bool IsVisible = false);

/// <summary>
/// 浏览器扩展增强的本机接收器：浏览器扩展把当前播放状态 POST 到 <c>127.0.0.1:端口</c>，
/// 供"系统媒体控件读不到"的站点兜底。
/// <para>
/// 为什么用本地端口而不是读浏览器内存：浏览器是多进程 + V8 堆，读内存既脆又会触发杀软误报；
/// 而扩展可以直接在页面里读 DOM / <c>navigator.mediaSession</c>，稳定得多。
/// </para>
/// <para>
/// 安全边界：**只绑定 <see cref="IPAddress.Loopback"/>**，外部网络无法访问；
/// 请求体大小、标题长度、时间取值范围都有上界；解析失败只回 400，不抛给主循环。
/// 内容只用于显示（不会执行、不会落盘），因此不做来源鉴权 —— 但这意味着本机上任何程序
/// 都能伪造"正在播放什么"。这是显示类数据的可接受取舍。
/// </para>
/// </summary>
internal static class BrowserExtensionBridge
{
    /// <summary>请求头上限（含请求行），超过直接断开。</summary>
    /// <summary>单个连接的读取超时：静默连接（连上却不发数据）不能永久占住处理任务。</summary>
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(10);

    private const int MaxHeaderBytes = 8 * 1024;

    /// <summary>请求体上限：一条播放状态远小于此。</summary>
    private const int MaxBodyBytes = 16 * 1024;

    /// <summary>标题类字段的最大字符数（防止异常上报把状态撑爆）。</summary>
    private const int MaxTextLength = 512;

    /// <summary>给 /status 用的序列化选项：不转义中文（用户会在浏览器里直接看这个地址）。</summary>
    private static readonly JsonSerializerOptions StatusJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object Gate = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static BrowserExtensionSnapshot? _latest;
    private static int _port;
    private static string _lastError = "";
    private static string _lastLoggedTitle = "";
    /// <summary>是否已经为某个端口尝试过监听（失败后不每拍重试，避免日志刷屏）。</summary>
    private static bool _attempted;
    private static int _attemptedPort;

    /// <summary>上次尝试监听的时刻（失败后按冷却期重试，而不是永久放弃）。</summary>
    private static DateTime _lastAttemptUtc = DateTime.MinValue;

    /// <summary>监听失败后的重试冷却期。</summary>
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 可选的共享令牌（空 = 不校验）。
    /// 非空时，<c>POST /media</c> 与 <c>GET /status</c> 都必须带 <c>X-MuSync-Token</c> 头。
    /// </summary>
    private static string _token = "";

    /// <summary>是否正在监听。</summary>
    public static bool IsRunning
    {
        get { lock (Gate) { return _listener is not null; } }
    }

    /// <summary>最近一次监听/解析错误（为空表示无错误）。</summary>
    public static string LastError
    {
        get { lock (Gate) { return _lastError; } }
    }

    /// <summary>
    /// 按配置启动或停止监听。重复调用是安全的（端口/开关变化时会重建）。
    /// </summary>
    /// <param name="enabled">设置里的开关。</param>
    /// <param name="port">监听端口（仅本机回环）。</param>
    /// <param name="sharedToken">
    /// 可选共享令牌。留空表示不校验（默认，保持向后兼容）；填了则扩展必须在
    /// <c>X-MuSync-Token</c> 头里带上同一个值，否则一律拒绝。
    /// 这是为了挡住"本机其它程序伪装成扩展、把你的 Steam 状态改成任意文本"。
    /// </param>
    public static void Ensure(bool enabled, int port, string? sharedToken = null)
    {
        if (!enabled)
        {
            Stop();
            // 关掉时清掉"尝试过"的记录：用户再勾上就是一次**明确的重新尝试**，
            // 不该被失败冷却期挡住（否则"关掉再打开"看起来毫无反应）。
            lock (Gate)
            {
                _attempted = false;
                _lastAttemptUtc = DateTime.MinValue;
            }
            return;
        }
        if (port is < 1 or > 65535)
        {
            Stop();
            lock (Gate) { _lastError = $"端口非法：{port}"; }
            Logger.Warn($"[浏览器扩展] 端口非法（{port}），已跳过监听");
            return;
        }

        // 令牌变更不需要重建监听：它是每个请求现算的，改完下一次请求就生效
        var normalizedToken = (sharedToken ?? "").Trim();
        lock (Gate)
        {
            if (_token != normalizedToken)
            {
                _token = normalizedToken;
                Logger.Info(normalizedToken.Length > 0
                    ? "[浏览器扩展] 已启用共享令牌校验（扩展需在设置页填入同一令牌）"
                    : "[浏览器扩展] 未设置共享令牌：本机任意程序都可向该端口上报（仅影响状态显示）");
            }
            if (_listener is not null && _port == port) return;
            // 同一端口尝试失败后不每拍重试（会把日志刷屏），但也不能"永不重试"：
            // 实测有"重启时端口恰好还在 TIME_WAIT / 被旧进程占着"的情况，若永久放弃，
            // 扩展增强会一直显示"未监听"，直到用户手动换端口 —— 那是能被用户看见的故障。
            // 现在改成冷却期重试：失败后 60 秒再试一次。
            if (_attempted && _attemptedPort == port && _listener is null &&
                DateTime.UtcNow - _lastAttemptUtc < RetryCooldown)
            {
                return;
            }
            _attempted = true;
            _attemptedPort = port;
            _lastAttemptUtc = DateTime.UtcNow;
        }
        Stop();
        _attempted = true;
        _attemptedPort = port;
        _lastAttemptUtc = DateTime.UtcNow;
        Start(port);
    }

    private static void Start(int port)
    {
        TcpListener? listener = null;
        Exception? lastError = null;

        // 最多试 3 次：上次退出的连接可能还在 TIME_WAIT，立即重绑会偶发 EADDRINUSE。
        // 加上 ReuseAddress 后通常一次就成，重试只是兜底。
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var candidate = new TcpListener(IPAddress.Loopback, port);
                // 允许绑定到仍有 TIME_WAIT 连接的端口（否则重启后可能几秒内都绑不上）
                candidate.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                candidate.Start();
                listener = candidate;
                lastError = null;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < 3) Thread.Sleep(200 * attempt);
            }
        }

        if (listener is null)
        {
            lock (Gate)
            {
                _listener = null;
                _port = port;
                _lastError = lastError?.Message ?? "未知错误";
            }
            Logger.Warn($"[浏览器扩展] 监听 127.0.0.1:{port} 失败（已重试 3 次）：{lastError?.Message}。" +
                        $"60 秒后会再试；也可以换一个端口。");
            return;
        }

        var cts = new CancellationTokenSource();
        lock (Gate)
        {
            _listener = listener;
            _cts = cts;
            _port = port;
            _lastError = "";
        }
        Logger.Info($"[浏览器扩展] 已监听 http://127.0.0.1:{port}/media （仅本机可访问）");

        _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
    }

    private static void Stop()
    {
        CancellationTokenSource? cts;
        TcpListener? listener;
        lock (Gate)
        {
            cts = _cts;
            listener = _listener;
            _cts = null;
            _listener = null;
            _latest = null;
        }
        try { cts?.Cancel(); } catch { /* 关闭阶段的取消异常无意义 */ }
        try { listener?.Stop(); } catch { /* 同上 */ }
        cts?.Dispose();
    }

    private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单个连接的接受失败不该终止整个监听
                Logger.Diagnose($"[浏览器扩展] 接受连接失败：{ex.Message}");
                continue;
            }

            // 每个连接独立处理，互不阻塞
            _ = HandleClientAsync(client, token);
        }
    }

    private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
            // 读取超时：静默连接会把这个处理任务连同 socket 一直挂着（以前没有超时）。
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(ClientReadTimeout);
            token = timeoutCts.Token;
        using (client)
        {
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();

                var (header, bodyLength) = await ReadHeaderAsync(stream, token).ConfigureAwait(false);
                if (header is null)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "bad request", token).ConfigureAwait(false);
                    return;
                }

                var firstLine = header.Split("\r\n", 2, StringSplitOptions.None)[0];
                var parts = firstLine.Split(' ');
                if (parts.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "bad request", token).ConfigureAwait(false);
                    return;
                }

                var method = parts[0];
                var path = parts[1].Split('?', 2)[0];

                if (method == "OPTIONS")
                {
                    await WriteResponseAsync(stream, 204, "text/plain", "", token).ConfigureAwait(false);
                    return;
                }

                // 共享令牌（可选）：设置里填了令牌，才要求带上正确的头。
                // 放在 OPTIONS 之后：预检请求不会带自定义头，挡掉它会让扩展连预检都过不去。
                if (!IsTokenAcceptable(header))
                {
                    // 先把请求体读掉再回 401：否则客户端还在发 body，收到的会是"连接被重置"
                    // 而不是清清楚楚的 401（实测有客户端如此）。
                    if (bodyLength is > 0 and <= MaxBodyBytes)
                    {
                        await ReadExactAsync(stream, bodyLength, token).ConfigureAwait(false);
                    }
                    await WriteResponseAsync(stream, 401, "text/plain", "unauthorized", token).ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path == "/status")
                {
                    var json = DescribeCurrent();
                    await WriteResponseAsync(stream, 200, "application/json; charset=utf-8", json, token)
                        .ConfigureAwait(false);
                    return;
                }

                if (method != "POST" || path != "/media")
                {
                    await WriteResponseAsync(stream, 404, "text/plain", "not found", token).ConfigureAwait(false);
                    return;
                }

                if (bodyLength <= 0 || bodyLength > MaxBodyBytes)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "bad length", token).ConfigureAwait(false);
                    return;
                }

                var body = await ReadExactAsync(stream, bodyLength, token).ConfigureAwait(false);
                if (body is null)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "short body", token).ConfigureAwait(false);
                    return;
                }

                var snapshot = Parse(body);
                if (snapshot is null)
                {
                    await WriteResponseAsync(stream, 400, "text/plain", "bad json", token).ConfigureAwait(false);
                    return;
                }

                Store(snapshot);
                await WriteResponseAsync(stream, 200, "application/json; charset=utf-8", "{\"ok\":true}", token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                // 连接级别的任何异常都不能冒到主循环
                Logger.Diagnose($"[浏览器扩展] 处理请求失败：{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 校验 <c>X-MuSync-Token</c> 请求头。
    /// <para>
    /// 未配置令牌（空字符串）时一律放行 —— 保持默认行为不变。
    /// 配置了就按常量时间比较，避免"逐字符比较 + 提前返回"泄露令牌长度/前缀。
    /// </para>
    /// </summary>
    private static bool IsTokenAcceptable(string header)
    {
        string expected;
        lock (Gate) { expected = _token; }
        if (expected.Length == 0) return true;

        foreach (var line in header.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (!line.AsSpan(0, colon).Trim().Equals("X-MuSync-Token", StringComparison.OrdinalIgnoreCase)) continue;

            var provided = line[(colon + 1)..].Trim();
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(provided));
        }
        return false;
    }

    /// <summary>读请求头（读到空行为止），返回头文本与 Content-Length。</summary>
    private static async Task<(string? Header, int ContentLength)> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxHeaderBytes);
        try
        {
            var used = 0;
            while (used < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(used, 1), token).ConfigureAwait(false);
                if (read <= 0) return (null, 0);
                used += read;
                // 检查是否以 \r\n\r\n 结尾
                if (used >= 4 &&
                    buffer[used - 4] == (byte)'\r' && buffer[used - 3] == (byte)'\n' &&
                    buffer[used - 2] == (byte)'\r' && buffer[used - 1] == (byte)'\n')
                {
                    var header = Encoding.ASCII.GetString(buffer, 0, used);
                    return (header, ParseContentLength(header));
                }
            }
            return (null, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int ParseContentLength(string header)
    {
        foreach (var line in header.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line["Content-Length:".Length..].Trim(), out var length))
            {
                return length;
            }
        }
        return 0;
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int length, CancellationToken token)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), token).ConfigureAwait(false);
            if (read <= 0) return null;
            offset += read;
        }
        return buffer;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream, int status, string contentType, string body, CancellationToken token)
    {
        var reason = status switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            _ => "OK"
        };
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status} {reason}\r\n"
                   + $"Content-Type: {contentType}\r\n"
                   + $"Content-Length: {bodyBytes.Length}\r\n"
                   // 扩展在后台页里 fetch 时可能带 Origin/预检，这里统一放开（只监听本机回环）
                   + "Access-Control-Allow-Origin: *\r\n"
                   + "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n"
                   + "Access-Control-Allow-Headers: Content-Type\r\n"
                   + "Cache-Control: no-store\r\n"
                   + "Connection: close\r\n\r\n";
        var headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes, token).ConfigureAwait(false);
        if (bodyBytes.Length > 0) await stream.WriteAsync(bodyBytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析扩展上报的 JSON。字段全部可选、类型不对就忽略；无法解析返回 null。
    /// 单独抽出来是为了能脱离网络单测。
    /// </summary>
    internal static BrowserExtensionSnapshot? Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            // 容忍 UTF-8 BOM：有些工具写文件会带 BOM，JsonDocument 遇到会直接判非法
            var payload = utf8Json;
            if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
            {
                payload = payload[3..];
            }
            using var doc = JsonDocument.Parse(payload.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var title = ReadString(root, "title");
            if (title.Length == 0) return null;   // 没有标题等于没有内容

            return new BrowserExtensionSnapshot(
                Title: title,
                Artist: ReadString(root, "artist"),
                Album: ReadString(root, "album"),
                PositionSeconds: ReadNumber(root, "position"),
                DurationSeconds: ReadNumber(root, "duration"),
                IsPlaying: ReadBool(root, "playing"),
                Site: ReadString(root, "site"),
                  // 页面是否在前台：前台新开的视频要能接过后台的直播，
                  // 但两个都在后台时仍保持在位者（避免每秒互相顶掉）。老版本扩展不发 → false
                  IsVisible: ReadBool(root, "visible"),
                ReceivedUtc: DateTime.UtcNow);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        text = SanitizeText(text);
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];
        return text.Trim();
    }

    /// <summary>
    /// 清洗上报文本：把控制字符（换行、制表、以及其它 C0/C1 控制符）替换成空格，并压掉连续空白。
    ///
    /// <para>
    /// 为什么必须做：这些文本最终会进 Steam 状态。网页标题是**别人可控**的内容 ——
    /// 一个带 <c>\r\n</c> 或零宽控制符的标题会让状态显示错乱，也可能被用来伪装成别的行。
    /// 只保留可打印字符是最省事也最安全的做法（emoji 等正常字符不受影响）。
    /// </para>
    /// </summary>
    private static string SanitizeText(string text)
    {
        if (text.Length == 0) return text;

        var needsWork = false;
        foreach (var c in text)
        {
            // C0（含 \r \n \t）、DEL、C1 都算控制字符
            if (char.IsControl(c))
            {
                needsWork = true;
                break;
            }
        }
        if (!needsWork) return text;

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = false;
        foreach (var c in text)
        {
            var printable = char.IsControl(c) ? ' ' : c;
            if (printable == ' ' && lastWasSpace) continue;
            builder.Append(printable);
            lastWasSpace = printable == ' ';
        }
        return builder.ToString().Trim();
    }

    private static double ReadNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number)) return 0;
        if (double.IsNaN(number) || double.IsInfinity(number) || number < 0) return 0;
        return number > 86_400 ? 86_400 : number;   // 一天上限，防异常值把进度条撑爆
    }

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void Store(BrowserExtensionSnapshot snapshot)
    {
        string? titleForLog = null;
        bool playingForLog = false;
        lock (Gate)
        {
            var incumbent = _latest;
            // 多标签防抖：两个页面都在播放时，扩展会每秒各报一次。
            // 直接覆盖会让 Steam 状态每秒来回跳，所以：
            //   · 同一个页面（site+title 相同）→ 正常更新进度
            //   · 新来的在播、在位者已暂停 → 让新来的接管（用户切页面 / 切集）
            //   · 在位者已过期（超过粘性窗口没上报）→ 让新来的接管
            //   · 其它情况（都在播）→ 保持在位者，避免每秒互相顶掉
            var samePage = incumbent is not null &&
                           string.Equals(incumbent.Site, snapshot.Site, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(incumbent.Title, snapshot.Title, StringComparison.Ordinal);
            var incumbentFresh = incumbent is not null &&
                                 DateTime.UtcNow - incumbent.ReceivedUtc <= IncumbentStickiness;
            var accept = incumbent is null || samePage || !incumbentFresh ||
                         (snapshot.IsPlaying && !incumbent!.IsPlaying) ||
                           // 前台页面接管后台页面：后台挂着直播、前台开新视频时，直接丢弃会让人以为坏了
                         (snapshot.IsVisible && !incumbent!.IsVisible);
            if (!accept) return;

            _latest = snapshot;
            _lastError = "";
            if (snapshot.Title != _lastLoggedTitle)
            {
                _lastLoggedTitle = snapshot.Title;
                titleForLog = snapshot.Title;
                playingForLog = snapshot.IsPlaying;
            }
        }

        // 标题变化时记一条，便于确认扩展是否在工作（同标题不刷屏）。写在锁外，日志 IO 不占锁。
        if (titleForLog is not null)
        {
            Logger.Info($"[浏览器扩展] 收到：{titleForLog}{(playingForLog ? "（播放中）" : "（暂停）")}");
        }
    }

    /// <summary>在位者粘性窗口：这段时间内在位者一直有新上报，就不让"同样在播的另一个页面"顶掉它。</summary>
    private static readonly TimeSpan IncumbentStickiness = TimeSpan.FromSeconds(5);

    /// <summary>取"足够新"的快照；超过 <paramref name="maxAge"/> 视为没有数据。</summary>
    public static BrowserExtensionSnapshot? TryGetFresh(TimeSpan maxAge)
    {
        lock (Gate)
        {
            if (_latest is null) return null;
            return DateTime.UtcNow - _latest.ReceivedUtc <= maxAge ? _latest : null;
        }
    }

    /// <summary>当前快照的 JSON（供 <c>GET /status</c> 与设置页诊断用）。</summary>
    private static string DescribeCurrent()
    {
        BrowserExtensionSnapshot? snapshot;
        int port;
        string error;
        lock (Gate)
        {
            snapshot = _latest;
            port = _port;
            error = _lastError;
        }
        if (snapshot is null)
        {
            return $"{{\"listening\":{(IsRunning ? "true" : "false")},\"port\":{port},\"error\":{JsonSerializer.Serialize(error, StatusJson)},\"data\":null}}";
        }
        var age = (DateTime.UtcNow - snapshot.ReceivedUtc).TotalSeconds;
        return $"{{\"listening\":{(IsRunning ? "true" : "false")},\"port\":{port},\"error\":{JsonSerializer.Serialize(error, StatusJson)},"
               + $"\"data\":{{\"title\":{JsonSerializer.Serialize(snapshot.Title, StatusJson)},\"playing\":{snapshot.IsPlaying.ToString().ToLowerInvariant()},"
               // site / visible 供排查多标签取舍：两个标签都在播时靠 visible 决定谁上台
               + $"\"site\":{JsonSerializer.Serialize(snapshot.Site, StatusJson)},"
               + $"\"visible\":{snapshot.IsVisible.ToString().ToLowerInvariant()},"
               + $"\"ageSeconds\":{age:F1}}}}}";
    }

    /// <summary>
    /// 诊断信息里用的详细状态：**带原因**（设置页那一格太窄，只能给结论）。
    /// </summary>
    public static string DiagnosticText()
    {
        lock (Gate)
        {
            var listening = _listener is not null
                ? Loc.L($"监听中 127.0.0.1:{_port}", $"listening on 127.0.0.1:{_port}")
                : Loc.L("未监听", "not listening");
            var error = _lastError.Length > 0 ? Loc.L($"（{_lastError}）", $" ({_lastError})") : "";
            var data = _latest is null
                ? Loc.L("尚无上报", "no data yet")
                : Loc.L($"最近上报「{_latest.Title}」{(DateTime.UtcNow - _latest.ReceivedUtc).TotalSeconds:F0} 秒前",
                         $"last: {_latest.Title} {(DateTime.UtcNow - _latest.ReceivedUtc).TotalSeconds:F0}s ago");
            return $"{listening}{error} ｜ {data}";
        }
    }


    /// <summary>
    /// 设置页那一小格（166×40）里显示的一句话状态。详细原因走日志，这里只给结论。
    /// </summary>
    public static string StatusText()
    {
        lock (Gate)
        {
            if (_listener is null)
            {
                // 不把原始错误文本拼进来：它可能是系统语言的中文（例如端口占用），
                // 会把英文界面弄出中文字符；具体原因看日志。
                return _lastError.Length > 0
                    ? Loc.L("未监听（详情见日志）", "Not listening (see log)")
                    : Loc.L("未启用", "Not enabled");
            }
            if (_latest is null) return Loc.L("等待扩展上报…", "Waiting for the extension…");
            var age = (DateTime.UtcNow - _latest.ReceivedUtc).TotalSeconds;
            var title = _latest.Title.Length > 12 ? _latest.Title[..12] + "…" : _latest.Title;
            return age <= 5
                ? Loc.L($"收到：{title}", $"Receiving: {title}")
                : Loc.L($"已停止（{age:F0} 秒前）", $"Stopped ({age:F0}s ago)");
        }
    }
}
