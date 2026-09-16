using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MuSync.Win32Api;

/// <summary>
/// 静音托盘气泡：使用 Shell_NotifyIcon(NIF_INFO | NIIF_NOSOUND) 显示与
/// NotifyIcon.ShowBalloonTip 相同的气泡，但不播放系统提示音。
/// 需要与已注册的 NotifyIcon 相同的窗口句柄与 ID（通过反射获取，取不到时退回原方法）。
/// </summary>
internal static class TrayBalloon
{
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_NOSOUND = 0x00000010;

    /// <summary>NOTIFYICONDATA 互操作结构（托盘图标与气泡信息）。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NotifyIconData lpData);

    /// <summary>显示静音气泡；句柄获取失败时退回 ShowBalloonTip（可能带声音）。</summary>
    public static void ShowSilent(NotifyIcon trayIcon, string title, string text, int timeoutMs = 1000)
    {
        if (trayIcon is null) return;
        try
        {
            var handle = TryGetNotifyIconHandle(trayIcon);
            if (handle == IntPtr.Zero)
            {
                trayIcon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
                return;
            }

            var data = new NotifyIconData
            {
                hWnd = handle,
                uID = 1,
                uFlags = NIF_INFO,
                szInfoTitle = title ?? "",
                szInfo = text ?? "",
                uTimeoutOrVersion = (uint)timeoutMs,
                dwInfoFlags = NIIF_INFO | NIIF_NOSOUND
            };
            data.cbSize = Marshal.SizeOf<NotifyIconData>();
            Shell_NotifyIconW(NIM_MODIFY, ref data);
        }
        catch
        {
            // 反射失败等异常情况下退回原方法，保证提示不丢失
            trayIcon.ShowBalloonTip(timeoutMs, title, text, ToolTipIcon.Info);
        }
    }

    private static IntPtr TryGetNotifyIconHandle(NotifyIcon trayIcon)
    {
        // NotifyIcon 内部用私有字段 _window（NativeWindow）承载消息窗口
        var field = typeof(NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
        var window = field?.GetValue(trayIcon);
        if (window is NativeWindow native && native.Handle != IntPtr.Zero)
        {
            return native.Handle;
        }
        return IntPtr.Zero;
    }
}
