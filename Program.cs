using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Utils;
namespace MuSync;
internal static class Program
{
    private static RpcManager? _rpcManager;
    private static SteamStatusManager? _steamManager;
    private static SteamSessionManager? _sessionManager;
    private static MainForm? _mainForm;
    private static NotifyIcon? TrayIcon { get; set; }
    public static RpcManager? GetRpcManager() => _rpcManager;
    public static SteamStatusManager? GetSteamManager() => _steamManager;
    public static SteamSessionManager? GetSessionManager() => _sessionManager;

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, "MuSyncMutex", out var isNewInstance);
        if (!isNewInstance)
        {
            MessageBox.Show("MuSync is already running.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Win32Api.AutoStart.MigrateLegacyRegistration();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _sessionManager = new SteamSessionManager();
        _sessionManager.Start();
        // 无论登录与否，先让界面与轮询跑起来（未登录时状态同步自动跳过）
        _steamManager = new SteamStatusManager(_sessionManager);
        _rpcManager = new RpcManager(_steamManager);
        Task.Run(_rpcManager.Start, token);
        _mainForm = new MainForm();
        TrayIcon = CreateTrayIcon();
        TrayIcon.Visible = true;
        if (!Configurations.Instance.Settings.StartInTray)
            _mainForm.Show();
        else
            ShowMinimizeToTrayNotification();
        // 消息循环空闲后再启动登录流程，避免启动阶段阻塞 UI（最长 15 秒白屏的问题）
        Application.Idle += OnApplicationIdle;
        Application.Run();
        cts.Cancel();
        _steamManager.ClearStatus();
        _sessionManager.Dispose();
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnApplicationIdle;
        _ = StartupSteamLoginAsync();
    }

    /// <summary>后台尝试令牌自动登录；失败则在 UI 线程弹出登录窗口。</summary>
    private static async Task StartupSteamLoginAsync()
    {
        try
        {
            var config = Configurations.Instance.Settings;
            if (!config.EnableSteamSync || _sessionManager == null) return;
            var hasSavedToken = !string.IsNullOrEmpty(config.SteamUsername) &&
                                !string.IsNullOrEmpty(config.SteamRefreshToken);
            if (hasSavedToken)
            {
                var username = config.SteamUsername;
                var refreshToken = config.SteamRefreshToken;
                var ok = await Task.Run(() => _sessionManager.LoginWithTokenAsync(username, refreshToken));
                if (ok)
                {
                    Logger.Info("[Program] Token 自动登录成功");
                    return;
                }
                Logger.Warn("[Program] Token 自动登录失败，弹出登录窗口");
            }
            // 走到这里说明需要手动登录；Idle 回调运行在 UI 线程，await 后仍回 UI 线程，弹模态窗安全
            using var loginForm = new SteamLoginForm(_sessionManager);
            loginForm.ShowDialog();
            if (loginForm.LoginSucceeded)
            {
                Logger.Info("[Program] Steam 登录成功，开始同步音乐状态");
            }
            else
            {
                MessageBox.Show(
                    "未登录 Steam，音乐状态将不会同步到好友列表。\n\n" +
                    "你可以在设置中重新登录。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[Program] 启动登录流程异常: {ex}");
        }
    }

    private static NotifyIcon CreateTrayIcon()
    {
        var showSettingsItem = new ToolStripMenuItem("显示设置");
        var showMainWindowItem = new ToolStripMenuItem("显示主窗口");
        var exitMenuItem = new ToolStripMenuItem("退出");
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(
            showMainWindowItem, showSettingsItem, new ToolStripSeparator(),
            exitMenuItem);
        showSettingsItem.Click += (_, _) =>
        {
            using var settingsForm = new SettingsForm();
            settingsForm.StartPosition = FormStartPosition.CenterScreen;
            settingsForm.ShowDialog();
        };
        showMainWindowItem.Click += (_, _) =>
        {
            if (_mainForm == null) return;
            _mainForm.Show();
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        };
        exitMenuItem.Click += (_, _) => Application.Exit();
        var notifyIcon = new NotifyIcon
        {
            Icon = AppResource.Icon,
            Text = "MuSync",
            ContextMenuStrip = contextMenu
        };
        notifyIcon.DoubleClick += (_, _) =>
        {
            if (_mainForm == null) return;
            _mainForm.Show();
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        };
        return notifyIcon;
    }

    public static void ShowMinimizeToTrayNotification()
    {
        TrayIcon?.ShowBalloonTip(1000, "应用仍在运行", "MuSync 已最小化到托盘区域。", ToolTipIcon.Info);
    }
}
