using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MuSync.Models;
using MuSync.Players.Interfaces;
namespace MuSync.Players;

/// <summary>
/// 酷狗音乐支持：
/// - 歌名 / 歌手：主进程窗口标题（「歌手 - 歌名 - 酷狗音乐」，含隐藏窗口兜底）；
/// - 进度 / 总时长：主进程内存中的 "MM:SS/MM:SS" 文本
///   （字符串模式搜索 + 活跃副本识别（正在增长的才是当前歌）+ 失效自动重搜）；
/// - 播放 / 暂停：以"进度是否在增长"判定。
/// 说明：酷狗的 SMTC 会话不稳定（暂停一段时间后会被系统移除），因此不依赖 SMTC。
/// </summary>
internal sealed class KuGou : IMusicPlayer
{
    private const int ChunkSize = 4 * 1024 * 1024;
    private const int MinDurationSeconds = 30;
    private const int MaxDurationSeconds = 3 * 60 * 60;

    private readonly int _pid;
    private readonly List<nint> _candidateAddrs = [];
    private readonly HashSet<nint> _liveAddrs = [];
    private readonly Dictionary<nint, double> _lastValues = [];
    private int _candidatesPid = -1;

    private DateTime _lastGrowthUtc = DateTime.UtcNow;
    private DateTime _lastProgressScanUtc = DateTime.MinValue;
    private string _currentSongId = Guid.NewGuid().ToString();
    private string? _lastTitle;
    private string? _lastArtist;

    public KuGou(int pid) => _pid = pid;

    /// <summary>当前绑定的进程 PID（父级检测到进程更替后据此重建实例）。</summary>
    public int Pid => _pid;

    private static Image? _appIconImage;

    /// <summary>提取酷狗应用图标（作为无封面歌曲的占位图），只提取一次。</summary>
    public static Image? GetAppIcon()
    {
        if (_appIconImage != null) return _appIconImage;
        try
        {
            foreach (var process in Process.GetProcessesByName("KuGou"))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                    Icon? icon = null;
                    try { icon = new Icon(path, 128, 128); } catch { }
                    icon ??= Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        _appIconImage = icon.ToBitmap();
                        icon.Dispose();
                        return _appIconImage;
                    }
                }
                catch
                {
                    // 跳过访问失败的进程
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[KuGou] 提取应用图标失败: {e.Message}");
        }
        return _appIconImage;
    }

    /// <summary>酷狗主进程：全部 KuGou.exe 中能读到「酷狗音乐」标题窗口的那个。</summary>
    public static Process? FindMainProcess()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("KuGou"))
            {
                try
                {
                    // 快路径：可见 / 最小化的主窗口
                    if (process.MainWindowTitle.Contains("酷狗音乐", StringComparison.Ordinal))
                    {
                        return process;
                    }
                    // 慢路径：窗口被隐藏（最小化到托盘）时枚举全部顶层窗口
                    if (FindWindowTitleForPid(process.Id) is not null)
                    {
                        return process;
                    }
                }
                catch
                {
                    // 个别进程访问失败时跳过
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[KuGou] 查找主进程失败: {e.Message}");
        }
        return null;
    }

    // 酷狗读取不长期持有进程句柄（每轮开/关），无需释放资源
    public void Dispose()
    {
    }

    public async Task<PlayerInfo?> GetPlayerInfoAsync()
    {
        // 1) 窗口标题 → 歌手 / 歌名
        var windowTitle = GetWindowTitle();
        if (windowTitle is null) return null;
        var track = ParseWindowTitle(windowTitle);
        if (track is not { } t || t.Title.Length == 0) return null;

        // 2) 切歌检测：清理旧歌的候选与活跃副本（新歌副本可能是新地址）
        if (t.Title != _lastTitle || t.Artist != _lastArtist)
        {
            _currentSongId = Guid.NewGuid().ToString();
            _lastTitle = t.Title;
            _lastArtist = t.Artist;
            _lastProgressScanUtc = DateTime.MinValue;   // 切歌后允许立即重新扫描
            _candidateAddrs.Clear();
            _liveAddrs.Clear();
            _lastValues.Clear();
            _lastGrowthUtc = DateTime.UtcNow;
        }

        // 3) 内存 → 进度 / 时长（增长时间戳由读取过程更新）
        var (progress, duration) = await Task.Run(ReadProgressFromMemory);

        // 暂停判定：进度文本是秒级的、轮询比它快，单轮“未增长”不能当作暂停；
        // 统一用“距最后一次观察到增长的时间”判断，超过 2 秒视为暂停；
        // 读不到进度（duration=0，如内存读取异常）时不判定为暂停，避免把播放中的歌误显示为已暂停
        var paused = duration > 0 && (DateTime.UtcNow - _lastGrowthUtc).TotalSeconds > 2.0;

        return new PlayerInfo
        {
            Identity = _currentSongId,
            Title = t.Title,
            Artists = t.Artist,
            Album = string.Empty,
            Cover = string.Empty,
            Schedule = progress,
            Duration = duration,
            Pause = paused,
            Url = string.Empty
        };
    }

    // ================= 窗口标题 =================

    private string? GetWindowTitle()
    {
        try
        {
            var title = Process.GetProcessById(_pid).MainWindowTitle;
            if (!string.IsNullOrEmpty(title) && title.Contains("酷狗音乐", StringComparison.Ordinal))
            {
                return title;
            }
        }
        catch
        {
            // 进程已退出等情况：落到慢路径（也会失败，返回 null）
        }
        return FindWindowTitleForPid(_pid);
    }

    private static string? FindWindowTitleForPid(int pid)
    {
        string? found = null;
        try
        {
            Native.EnumWindows((hWnd, _) =>
            {
                Native.GetWindowThreadProcessId(hWnd, out var windowPid);
                if (windowPid != (uint)pid) return true;
                var buffer = new StringBuilder(512);
                if (Native.GetWindowText(hWnd, buffer, buffer.Capacity) <= 0) return true;
                var text = buffer.ToString();
                if (text.Contains("酷狗音乐", StringComparison.Ordinal))
                {
                    found = text;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[KuGou] 枚举窗口失败: {e.Message}");
        }
        return found;
    }

    /// <summary>解析窗口标题：「歌手 - 歌名 - 酷狗音乐」（歌手可缺省；歌名中的 " - " 保留）。</summary>
    private static (string Artist, string Title)? ParseWindowTitle(string raw)
    {
        var title = raw.Trim();
        const string suffix = "- 酷狗音乐";
        if (title.EndsWith(suffix, StringComparison.Ordinal))
        {
            title = title[..^suffix.Length].TrimEnd();
        }
        if (title.Length == 0 || title == "酷狗音乐")
        {
            return null; // 未在播放（仅有固定标题）
        }
        var separator = title.IndexOf(" - ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return (string.Empty, title);
        }
        var artist = title[..separator].Trim();
        var song = title[(separator + 3)..].Trim();
        return song.Length == 0 ? null : (artist, song);
    }

    // ================= 内存（进度 / 总时长）=================

    private (double Progress, double Duration) ReadProgressFromMemory()
    {
        if (_pid <= 0) return (0, 0);
        try
        {
            // 候选未就绪（首次 / 切歌后）→ 全内存搜索
            if (_candidatesPid != _pid || _candidateAddrs.Count == 0)
            {
                // 扫描失败时做节流（异常情形下避免每轮全内存扫描占用 CPU）
                if ((DateTime.UtcNow - _lastProgressScanUtc).TotalSeconds < 5.0) return (0, 0);
                _lastProgressScanUtc = DateTime.UtcNow;
                var found = ScanForProgressStrings();
                if (found.Count == 0) return (0, 0);
                _candidatesPid = _pid;
                _candidateAddrs.Clear();
                foreach (var f in found) _candidateAddrs.Add(f.Addr);
            }

            // 1) 已知活跃副本（当前歌的；同一首歌内暂停时也可靠）
            if (_liveAddrs.Count > 0)
            {
                var known = ReadAll(_liveAddrs);
                if (known.Count > 0)
                {
                    var grewKnown = false;
                    foreach (var r in known)
                    {
                        if (_lastValues.TryGetValue(r.Addr, out var previous) && r.Progress > previous)
                        {
                            grewKnown = true;
                        }
                        _lastValues[r.Addr] = r.Progress;
                    }
                    if (grewKnown) _lastGrowthUtc = DateTime.UtcNow;
                    var bestKnown = known.MaxBy(r => r.Progress);
                    return (bestKnown.Progress, bestKnown.Duration);
                }
                _liveAddrs.Clear();
            }

            // 2) 读取全部候选：正在增长的 = 当前歌的副本（历史残留永远静止，以此区分）
            var readings = ReadAll(_candidateAddrs);
            if (readings.Count == 0) return (0, 0);
            var growing = new List<(nint Addr, double Progress, double Duration)>();
            foreach (var r in readings)
            {
                if (_lastValues.TryGetValue(r.Addr, out var previous) && r.Progress > previous)
                {
                    growing.Add(r);
                }
                _lastValues[r.Addr] = r.Progress;
            }

            if (growing.Count > 0)
            {
                _liveAddrs.Clear();
                foreach (var g in growing) _liveAddrs.Add(g.Addr);
                _lastGrowthUtc = DateTime.UtcNow;
                var best = growing.MaxBy(g => g.Progress);
                return (best.Progress, best.Duration);
            }

            // 3) 无增长（首次 / 启动时暂停）→ 兜底取进度最大（下一轮播放即会自愈）
            var fallback = readings.MaxBy(r => r.Progress);
            return (fallback.Progress, fallback.Duration);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[KuGou] 进度读取失败: {e.Message}");
            return (0, 0);
        }
    }

    private List<(nint Addr, double Progress, double Duration)> ReadAll(IEnumerable<nint> addrs)
    {
        var result = new List<(nint Addr, double Progress, double Duration)>();
        var handle = Native.OpenProcess(Native.ProcessVmRead, false, _pid);
        if (handle == IntPtr.Zero) return result;
        try
        {
            var buffer = new byte[48];
            foreach (var addr in addrs)
            {
                if (!Native.ReadProcessMemory(handle, addr, buffer, buffer.Length, out var read) || read < 16)
                {
                    continue;
                }
                if (TryParseProgressPair(buffer.AsSpan(0, (int)read), out var progress, out var duration))
                {
                    result.Add((addr, progress, duration));
                }
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }
        return result;
    }

    private List<(nint Addr, double Progress, double Duration)> ScanForProgressStrings()
    {
        var results = new List<(nint Addr, double Progress, double Duration)>();
        var handle = Native.OpenProcess(
            Native.ProcessVmRead | Native.ProcessQueryInformation, false, _pid);
        if (handle == IntPtr.Zero) return results;
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            try
            {
                var mbiSize = Marshal.SizeOf<Native.MemoryBasicInformation>();
                nint addr = 0x10000;
                while (true)
                {
                    if (Native.VirtualQueryEx(handle, addr, out var mbi, (nuint)mbiSize) == 0) break;
                    var regionSize = (long)mbi.RegionSize;
                    if (regionSize <= 0) break;
                    var readable = mbi.State == Native.MemCommit &&
                                   (mbi.Protect & Native.PageGuard) == 0 &&
                                   (mbi.Protect & Native.PageNoAccess) == 0 &&
                                   (mbi.Protect & 0xFF) != 0;
                    if (readable && regionSize < 2L * 1024 * 1024 * 1024)
                    {
                        var regionBase = mbi.BaseAddress;
                        for (long offset = 0; offset < regionSize; offset += ChunkSize)
                        {
                            var chunk = (int)Math.Min(ChunkSize, regionSize - offset);
                            if (!Native.ReadProcessMemory(handle, regionBase + (nint)offset, buffer, chunk,
                                    out var read) || read == 0)
                            {
                                continue;
                            }
                            ScanChunk(buffer.AsSpan(0, (int)read), regionBase + (nint)offset, results);
                            if (results.Count >= 200) break;
                        }
                    }
                    if (results.Count >= 200) break;
                    var next = mbi.BaseAddress + (nint)regionSize;
                    if ((ulong)next >= 0x7FFF_FFFF_0000UL) break;
                    addr = next;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            Native.CloseHandle(handle);
        }
        return results;
    }

    private static void ScanChunk(ReadOnlySpan<byte> span, nint baseAddr,
        List<(nint Addr, double Progress, double Duration)> results)
    {
        for (var i = 0; i + 16 <= span.Length; i += 2)
        {
            // 找 UTF-16 的 ':'（0x3A 0x00）
            if (span[i] != 0x3A || span[i + 1] != 0x00) continue;
            if (i < 2 || span[i - 1] != 0x00) continue;
            if (span[i - 2] < 0x30 || span[i - 2] > 0x39) continue;

            // 回溯分钟位：1-2 位；3 位以上（含日期/时间戳）排除
            var start = i - 2;
            if (start >= 2 && span[start - 1] == 0x00 && span[start - 2] >= 0x30 && span[start - 2] <= 0x39)
            {
                start -= 2;
                if (start >= 2 && span[start - 1] == 0x00 && span[start - 2] >= 0x30 && span[start - 2] <= 0x39)
                {
                    continue;
                }
            }

            var maxLen = Math.Min(span.Length - start, 40);
            if (TryParseProgressPair(span.Slice(start, maxLen), out var progress, out var duration))
            {
                results.Add((baseAddr + start, progress, duration));
            }
        }
    }

    private static bool TryParseProgressPair(ReadOnlySpan<byte> span, out double progress, out double duration)
    {
        progress = 0;
        duration = 0;
        if (!TryParseTime(span, 0, out var progressSeconds, out var endPos)) return false;
        // '/'
        if (endPos + 2 > span.Length || span[endPos] != 0x2F || span[endPos + 1] != 0x00) return false;
        if (!TryParseTime(span, endPos + 2, out var durationSeconds, out var endPos2)) return false;
        // 语义校验：时长合理区间、进度不超过时长
        if (durationSeconds < MinDurationSeconds || durationSeconds > MaxDurationSeconds) return false;
        if (progressSeconds > durationSeconds) return false;
        // 结尾不能紧跟数字（防止从更长的数字串中截断误匹配）
        if (endPos2 + 1 < span.Length && span[endPos2 + 1] == 0x00 &&
            span[endPos2] >= 0x30 && span[endPos2] <= 0x39)
        {
            return false;
        }
        progress = progressSeconds;
        duration = durationSeconds;
        return true;
    }

    private static bool TryParseTime(ReadOnlySpan<byte> span, int pos, out int totalSeconds, out int endPos)
    {
        totalSeconds = 0;
        endPos = pos;
        var minutes = 0;
        var digits = 0;
        while (digits < 2 && pos + 1 < span.Length && span[pos + 1] == 0x00 &&
               span[pos] >= 0x30 && span[pos] <= 0x39)
        {
            minutes = minutes * 10 + (span[pos] - 0x30);
            pos += 2;
            digits++;
        }
        if (digits < 1) return false;
        if (pos + 1 >= span.Length || span[pos] != 0x3A || span[pos + 1] != 0x00) return false;
        pos += 2;
        if (pos + 3 >= span.Length) return false;
        if (!(span[pos] >= 0x30 && span[pos] <= 0x39 && span[pos + 1] == 0x00)) return false;
        if (!(span[pos + 2] >= 0x30 && span[pos + 2] <= 0x39 && span[pos + 3] == 0x00)) return false;
        var seconds = (span[pos] - 0x30) * 10 + (span[pos + 2] - 0x30);
        if (seconds > 59) return false;
        pos += 4;
        totalSeconds = minutes * 60 + seconds;
        endPos = pos;
        return true;
    }

    // ================= Win32 =================

    /// <summary>酷狗读取所需的 Win32 P/Invoke 封装（进程内存读取 / 窗口枚举）。</summary>
    private static class Native
    {
        public const int ProcessVmRead = 0x0010;
        public const int ProcessQueryInformation = 0x0400;
        public const uint MemCommit = 0x1000;
        public const uint PageNoAccess = 0x01;
        public const uint PageGuard = 0x100;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>VirtualQueryEx 返回的内存区域信息（基址 / 大小 / 状态 / 保护属性）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct MemoryBasicInformation
        {
            public nint BaseAddress;
            public nint AllocationBase;
            public uint AllocationProtect;
            public uint Alignment1;
            public nuint RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
            public uint Alignment2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(int desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(IntPtr handle, nint baseAddress, [Out] byte[] buffer, int size,
            out int bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nuint VirtualQueryEx(IntPtr handle, nint address,
            out MemoryBasicInformation information, nuint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
