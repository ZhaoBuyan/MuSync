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
    DateTime ReceivedUtc);

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
    public static void Ensure(bool enabled, int port)
    {
        if (!enabled)
        {
            Stop();
            return;
        }
        if (port is < 1 or > 65535)
        {
            Stop();
            lock (Gate) { _lastError = $"端口非法：{port}"; }
            Logger.Warn($"[浏览器扩展] 端口非法（{port}），已跳过监听");
            return;
        }

        lock (Gate)
        {
            if (_listener is not null && _port == port) return;
            // 同一端口已经尝试过且失败：不再每拍重试（改端口或重新启用会重新尝试），否则日志会被刷屏
            if (_attempted && _attemptedPort == port && _listener is null) return;
            _attempted = true;
            _attemptedPort = port;
        }
        Stop();
        _attempted = true;
        _attemptedPort = port;
        Start(port);
    }

    private static void Start(int port)
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (Exception ex)
        {
            lock (Gate)
            {
                _listener = null;
                _port = port;
                _lastError = ex.Message;
            }
            Logger.Warn($"[浏览器扩展] 监听 127.0.0.1:{port} 失败：{ex.Message}（可在设置里换一个端口）");
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
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];
        return text.Trim();
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
        string titleForLog;
        lock (Gate)
        {
            _latest = snapshot;
            _lastError = "";
            titleForLog = snapshot.Title;
        }
        // 标题变化时记一条，便于确认扩展是否在工作（同标题不刷屏）
        if (titleForLog != _lastLoggedTitle)
        {
            _lastLoggedTitle = titleForLog;
            Logger.Info($"[浏览器扩展] 收到：{titleForLog}{(snapshot.IsPlaying ? "（播放中）" : "（暂停）")}");
        }
    }

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
