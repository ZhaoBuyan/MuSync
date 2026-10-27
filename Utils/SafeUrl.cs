using System;

namespace MuSync.Utils;

/// <summary>
/// 只允许用"浏览器能打开的 http(s) 地址"去调用 <c>Process.Start(UseShellExecute = true)</c>。
///
/// <para>
/// 为什么需要它：<c>UseShellExecute = true</c> 交给的是 Windows Shell，行为按协议分派 ——
/// 传进去的字符串如果是 <c>C:\path\evil.exe</c>、<c>\\host\share\x.exe</c>、<c>cmd:/c ...</c>
/// 或某个已注册的自定义协议，Shell 就会**执行**它，而不是打开网页。
/// 而这些地址的来源并不总是可信：歌曲链接是从播放器进程内存里读出来的字符串，
/// 更新地址来自网络响应。只要是"显示出来给用户点"的链接，就必须先确认是 http(s)。
/// </para>
///
/// <para>
/// 判定用 <see cref="Uri.TryCreate(string?, UriKind, out Uri)"/>，要求绝对地址且 scheme 为
/// http/https；其余（file、ftp、自定义协议、相对路径、缺 scheme）一律拒绝。
/// </para>
/// </summary>
internal static class SafeUrl
{
    /// <summary>是否为可用于"在浏览器中打开"的 http(s) 绝对地址。</summary>
    internal static bool IsHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// 用默认浏览器打开 http(s) 地址；不是 http(s) 就什么都不做并返回 false。
    /// </summary>
    /// <param name="url">待打开的地址（可能来自内存读取或网络响应，因此必须校验）。</param>
    /// <param name="error">打开失败时的异常（调用方自行决定是否记日志）。</param>
    internal static bool TryOpenInBrowser(string? url, out Exception? error)
    {
        error = null;
        if (!IsHttpUrl(url)) return false;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url!)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
