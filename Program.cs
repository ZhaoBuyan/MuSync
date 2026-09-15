using System;
using System.Drawing;
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
    private static ToolStripMenuItem? TrayStatusItem { get; set; }
    private static ToolStripMenuItem? _trayUpdateItem;
    private static UpdateChecker.UpdateInfo? _pendingUpdate;
    public static RpcManager? GetRpcManager() => _rpcManager;
    public static SteamStatusManager? GetSteamManager() => _steamManager;
    public static SteamSessionManager? GetSessionManager() => _sessionManager;
    /// <summary>后台检查发现的可用更新（供主窗口红点与托盘菜单项使用）；无更新时为 null。</summary>
    public static UpdateChecker.UpdateInfo? PendingUpdate => _pendingUpdate;

    /// <summary>本次启动时间（诊断信息用）。</summary>
    public static DateTime StartedAt { get; } = DateTime.Now;

    /// <summary>全局异常兜底：写日志（同步、最小依赖）+ 用 MessageBox 提示（不用普通窗体，防二次崩溃）。</summary>
    private static void HandleFatalException(string source, Exception? exception)
    {
        try
        {
            Logger.Error($"[FATAL] {source}未处理异常: {exception}");
        }
        catch
        {
            // 日志失败也要继续尝试提示
        }
        try
        {
            MessageBox.Show(
                $"MuSync 遇到了一个未处理的错误，详情已记录到日志：\n\n{exception?.Message}",
                "MuSync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // 提示失败时静默
        }
    }

    [STAThread]
    private static void Main()
    {
        // 全局异常兜底（UI 线程 / 后台线程 / 未观察任务）
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatalException("UI 线程", e.Exception);
        AppDomain.CurrentDomain.UnhandledException +=
            (_, e) => HandleFatalException("后台线程", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Warn($"[FATAL] 未观察任务异常: {e.Exception.Message}");
            e.SetObserved();
        };

        // 启动时清理旧日志（保留最近 14 天 / 50 MB 内）
        Logger.CleanupOldLogs();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var mutex = new Mutex(true, "MuSyncMutex", out var isNewInstance);
        if (!isNewInstance)
        {
            // 已有实例在运行：请求它把主窗口唤起到前台，然后安静退出
            if (!TryActivateExistingInstance())
            {
                MessageBox.Show("MuSync 已在运行。可从任务栏右下角的托盘图标打开主窗口。",
                    "MuSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }
        // 首个实例：创建「唤起窗口」通知事件（托盘驻留时也能被第二次启动唤起）
        using var activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        Win32Api.AutoStart.MigrateLegacyRegistration();
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _sessionManager = new SteamSessionManager();
        // 登录 / 重连成功后立即重推当前状态（重连后 Steam 侧状态已清空，且本地去重缓存会拦截重推）
        _sessionManager.OnLoginSucceeded += RefreshStatusAfterRecovery;
        _sessionManager.Start();
        // 系统唤醒 / 解锁后同样主动重推一次（睡眠期间连接可能中断）
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Resume) RefreshStatusAfterRecovery();
            };
            Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
            {
                if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) RefreshStatusAfterRecovery();
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Program] 订阅系统电源事件失败: {ex.Message}");
        }
        // 无论登录与否，先让界面与轮询跑起来（未登录时状态同步自动跳过）
        _steamManager = new SteamStatusManager(_sessionManager);
        _rpcManager = new RpcManager(_steamManager);
        Task.Run(_rpcManager.Start, token);
        _mainForm = new MainForm();
        // 提前创建窗口句柄：托盘驻留（窗口从未显示过）时也能从后台线程安全唤醒
        _ = _mainForm.Handle;
        StartActivationListener(activationEvent);
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
        // 清理托盘图标（防偶发残留）
        try
        {
            TrayIcon.Visible = false;
            TrayIcon.Dispose();
        }
        catch
        {
            // 忽略
        }
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnApplicationIdle;
        _ = StartupSteamLoginAsync();
        _ = CheckUpdatesInBackgroundAsync();
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

    /// <summary>
    /// 启动后台更新检查：发现新版本时点亮设置按钮红点与托盘菜单项（不再弹气泡打扰）。
    /// </summary>
    private static async Task CheckUpdatesInBackgroundAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            while (true)
            {
                var info = await UpdateChecker.CheckAsync().ConfigureAwait(false);
                if (info != null)
                {
                    Logger.Info($"[Update] 发现新版本 {info.Tag}");
                    SetPendingUpdate(info);
                }
                // 常驻托盘时定期复查（6 小时一次），避免长期运行错过新版本
                await Task.Delay(TimeSpan.FromHours(6)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[Update] 检查更新异常: {ex.Message}");
        }
    }

    /// <summary>记录发现的更新并刷新托盘菜单项（可从任意线程调用）。</summary>
    public static void SetPendingUpdate(UpdateChecker.UpdateInfo info)
    {
        _pendingUpdate = info;
        void Apply()
        {
            if (_trayUpdateItem == null) return;
            _trayUpdateItem.Text = $"⬆ 有新版本 {info.Tag}";
            _trayUpdateItem.Visible = true;
        }
        try
        {
            if (_mainForm is { IsHandleCreated: true } form && form.InvokeRequired)
            {
                form.BeginInvoke(Apply);
            }
            else
            {
                Apply();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 刷新更新提示失败: {ex.Message}");
        }
    }

    private const string ActivateEventName = "MuSyncActivateEvent";

    /// <summary>显示并激活主窗口（托盘菜单与二次启动唤起共用）。</summary>
    private static void ShowMainWindow()
    {
        if (_mainForm == null) return;
        _mainForm.Show();
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    /// <summary>把主窗口显示并移动到鼠标所在屏幕的工作区中央。</summary>
    private static void CenterMainWindow()
    {
        ShowMainWindow();
        if (_mainForm == null) return;
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        _mainForm.Location = new Point(
            workArea.Left + (workArea.Width - _mainForm.Width) / 2,
            workArea.Top + (workArea.Height - _mainForm.Height) / 2);
    }

    /// <summary>通知已运行的实例唤起主窗口（第二次启动时调用）。</summary>
    private static bool TryActivateExistingInstance()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
                {
                    using (handle)
                    {
                        handle.Set();
                    }
                    return true;
                }
            }
            catch
            {
                // 忽略，稍后重试（兼容首个实例尚未创建事件的启动竞态）
            }
            Thread.Sleep(100);
        }
        return false;
    }

    /// <summary>监听「唤起窗口」通知（后台线程，收到后切回 UI 线程显示主窗口）。</summary>
    private static void StartActivationListener(EventWaitHandle activationEvent)
    {
        _ = Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    activationEvent.WaitOne();
                    ShowMainWindowFromBackground();
                }
                catch (ObjectDisposedException)
                {
                    return; // 程序退出中
                }
                catch
                {
                    // 单次失败不退出监听
                }
            }
        });
    }

    /// <summary>后台线程安全地显示主窗口（必要时切回 UI 线程）。</summary>
    private static void ShowMainWindowFromBackground()
    {
        var form = _mainForm;
        if (form == null) return;
        try
        {
            if (form.IsHandleCreated && form.InvokeRequired)
            {
                form.BeginInvoke((Action)ShowMainWindow);
                return;
            }
            ShowMainWindow();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Program] 唤起主窗口失败: {ex.Message}");
        }
    }

    /// <summary>登录 / 系统唤醒 / 解锁后：清理去重缓存并请求立即重推状态。</summary>
    private static void RefreshStatusAfterRecovery()
    {
        try
        {
            GetSteamManager()?.InvalidatePushedCache();
            GetRpcManager()?.RequestStateRefresh();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Program] 恢复后重推状态失败: {ex.Message}");
        }
    }

    private static NotifyIcon CreateTrayIcon()
    {
        // 菜单顶部状态行（禁用态，由 UpdateTrayStatus 节流刷新）
        TrayStatusItem = new ToolStripMenuItem("MuSync") { Enabled = false };
        var pauseSyncItem = new ToolStripMenuItem("暂停同步") { CheckOnClick = true };
        pauseSyncItem.Click += (_, _) =>
        {
            var steamManager = GetSteamManager();
            if (steamManager == null) return;
            steamManager.ManualPause = pauseSyncItem.Checked;
            Logger.Info($"[Program] 手动暂停同步: {pauseSyncItem.Checked}");
            if (!pauseSyncItem.Checked)
            {
                // 恢复后立即重新推送当前状态
                GetRpcManager()?.RequestStateRefresh();
            }
        };
        // 更新提示项：默认隐藏，后台发现新版本后点亮
        _trayUpdateItem = new ToolStripMenuItem("有新版本") { Visible = false };
        _trayUpdateItem.Click += (_, _) =>
        {
            if (_pendingUpdate is not { } info) return;
            using var updateForm = new UpdateForm(UpdateChecker.GetCurrentVersionText(), info);
            updateForm.ShowDialog();
        };
        var showMainWindowItem = new ToolStripMenuItem("主窗口");
        var centerWindowItem = new ToolStripMenuItem("窗口回中");
        var showSettingsItem = new ToolStripMenuItem("设置");
        var exitMenuItem = new ToolStripMenuItem("退出");
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(
            TrayStatusItem, _trayUpdateItem, new ToolStripSeparator(),
            showMainWindowItem, centerWindowItem, pauseSyncItem, showSettingsItem,
            new ToolStripSeparator(), exitMenuItem);
        showSettingsItem.Click += (_, _) =>
        {
            using var settingsForm = new SettingsForm();
            settingsForm.StartPosition = FormStartPosition.CenterScreen;
            settingsForm.ShowDialog();
            _mainForm?.ApplyAppearance();
        };
        showMainWindowItem.Click += (_, _) => ShowMainWindow();
        centerWindowItem.Click += (_, _) => CenterMainWindow();
        exitMenuItem.Click += (_, _) => Application.Exit();
        var notifyIcon = new NotifyIcon
        {
            Icon = AppResource.Icon,
            Text = "MuSync",
            ContextMenuStrip = contextMenu
        };
        notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        return notifyIcon;
    }

    /// <summary>刷新托盘悬停提示与右键菜单状态行（由主轮询循环节流调用，约每 5 秒）。</summary>
    public static void UpdateTrayStatus()
    {
        if (TrayIcon == null) return;
        try
        {
            string text;
            var config = Configurations.Instance.Settings;
            if (config.EnableSteamSync && config.PauseWhenPlayingGame &&
                GetSteamManager()?.IsRealGameActive == true)
            {
                text = "游戏中，音乐同步已暂停";
            }
            else if (GetSteamManager()?.ManualPause == true)
            {
                text = "同步已手动暂停";
            }
            else
            {
                var appDisplay = GetRpcManager()?.GetActiveAppDisplay();
                var current = GetRpcManager()?.GetCurrentPlayerInfo();
                if (appDisplay != null && current?.PlayerInfo is { } song)
                {
                    text = $"{appDisplay} + {song.Title}";
                }
                else if (appDisplay != null)
                {
                    text = appDisplay;
                }
                else if (current?.PlayerInfo is { } onlySong)
                {
                    text = $"正在播放 {current.Value.PlayerName}: {onlySong.Title}";
                }
                else
                {
                    text = "未在播放音乐";
                }
            }
            text = StringUtils.GetTruncatedStringByMaxByteLength(text, 60);
            TrayIcon.Text = text;
            if (TrayStatusItem != null && TrayStatusItem.Text != text)
            {
                TrayStatusItem.Text = text;
            }
        }
        catch (Exception ex)
        {
            // 托盘刷新失败不影响主流程
            Logger.Error($"[Program] 刷新托盘状态失败: {ex.Message}");
        }
    }

    public static void ShowMinimizeToTrayNotification()
    {
        // 静音版托盘气泡：保留文字提示，但不播放系统提示音
        if (TrayIcon is { } icon)
        {
            Win32Api.TrayBalloon.ShowSilent(icon, "应用仍在运行", "MuSync 已最小化到托盘区域。");
        }
    }
}
