using System;
using System.Windows.Forms;
using MuSync.Win32Api;
namespace MuSync.Utils;
/// <summary>
/// 托盘图标守护：资源管理器（Explorer）重启后，系统会向所有顶层窗口广播 TaskbarCreated；
/// 收到后重新注册托盘图标（先删后加），避免图标失联、点不出来。
/// </summary>
internal sealed class TrayIconGuard : NativeWindow, IDisposable
{
    /// <summary>系统注册消息号：资源管理器（任务栏）重建完成。0 表示注册失败。</summary>
    private static readonly uint TaskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");
    private readonly NotifyIcon _icon;
    private bool _disposed;
    private TrayIconGuard(NotifyIcon icon)
    {
        _icon = icon;
        // 注意：这里不能用 HWND_MESSAGE 的消息窗口——消息窗口收不到广播消息，
        // 必须是一个普通顶层窗口（从不显示，所以用户看不见）。
        CreateHandle(new CreateParams
        {
            Caption = "MuSync.TrayIconGuard"
        });
    }
    /// <summary>挂上一个守护窗口（返回的实例需长期持有，按需 Dispose）。</summary>
    public static TrayIconGuard Attach(NotifyIcon icon) => new(icon);
    protected override void WndProc(ref Message m)
    {
        if (!_disposed && TaskbarCreatedMessage != 0 && (uint)m.Msg == TaskbarCreatedMessage)
        {
            ReRegisterTrayIcon();
        }
        base.WndProc(ref m);
    }
    /// <summary>重新注册托盘图标（Explorer 重启会清掉原来的图标，需删掉再添加一次）。</summary>
    private void ReRegisterTrayIcon()
    {
        try
        {
            Logger.Info("[Tray] 检测到资源管理器重启（TaskbarCreated），正在重新注册托盘图标");
            _icon.Visible = false;
            _icon.Visible = true;
            Program.UpdateTrayStatus();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Tray] 重新注册托盘图标失败: {ex.Message}");
        }
    }
    public void Dispose()
    {
        _disposed = true;
        try
        {
            DestroyHandle();
        }
        catch
        {
            // 忽略
        }
    }
}
