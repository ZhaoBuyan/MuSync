using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Players;
using MuSync.Utils;
namespace MuSync;
internal class MainForm : Form
{
    private readonly Timer _updateTimer;
    private readonly Panel[] _playerPanels = new Panel[3];
    private readonly PictureBox[] _coverPictureBoxes = new PictureBox[3];
    private readonly Label[] _songTitleLabels = new Label[3];
    private readonly Label[] _artistLabels = new Label[3];
    private readonly Label[] _albumLabels = new Label[3];
    private readonly Label[] _statusLabels = new Label[3];
    private readonly ProgressBar[] _progressBars = new ProgressBar[3];
    private readonly Label[] _progressLabels = new Label[3];
    private readonly Label[] _playerNameLabels = new Label[3];
    private Label _lastUpdateLabel = null!;
    private Label _steamStateLabel = null!;
    private Panel _appSyncPanel = null!;
    private Label _appNameLabel = null!;
    private Label _appCategoryLabel = null!;
    private Label _appStatusLabel = null!;
    private PictureBox _appIconBox = null!;
    private string _appIconPath = "";
    private FadingButton _settingsButton = null!;
    private bool _updateBadgeVisible;
    private GradientDivider? _appSyncDivider;
    private readonly string[] _playerNames =
    [
        Loc.L("网易云音乐", "NetEase Cloud Music"), Loc.L("QQ音乐", "QQ Music"),
        Loc.L("洛雪音乐", "LX Music"), Loc.L("酷狗音乐", "KuGou Music")
    ];
    // 播放器品牌色（主界面标题、歌名与面板图标共用）：网易云红 / QQ音乐绿 / 洛雪青绿 / 酷狗蓝
    private static readonly Color[] _playerColors =
    [
        Color.FromArgb(211, 58, 49),
        Color.FromArgb(49, 194, 124),
        Color.FromArgb(0, 179, 134),
        Color.FromArgb(47, 110, 224)
    ];
    private readonly string[] _currentSongIds = new string[3];
    private readonly string[] _currentCoverUrls = ["", "", ""];
    private readonly string[] _currentCacheKeys = ["", "", ""];
    private readonly PlayerInfo?[] _lastPlayerInfos = new PlayerInfo?[3];
    private readonly bool[] _lastActiveStates = new bool[3];
    private string _lastMusicSourceName = "";
    public MainForm()
    {
        InitializeComponent();
        SetupForm();
        _updateTimer = new Timer { Interval = 1000 }; 
        _updateTimer.Tick += UpdateTimer_Tick;
        KeyPreview = true;
        KeyDown += MainForm_KeyDown;
        // 启动时应用外观设置
        ApplyAppearance();
    }
    private void InitializeComponent()
    {
        CreatePlayerPanel(0);
        // 面板之间的渐变分隔线（半透明 → 透明）
        _appSyncDivider = new GradientDivider
        {
            Location = new Point(10, 155),
            Size = new Size(580, 5)
        };
        Controls.Add(_appSyncDivider);
        _appSyncDivider.BringToFront();
        CreateAppSyncPanel();
        _lastUpdateLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.Gray,
            Location = new Point(10, 345),
            Size = new Size(150, 13),
            Text = Loc.L("最后更新: --:--:--", "Last update: --:--:--")
        };
        _settingsButton = new FadingButton
        {
            Text = Loc.L("设置", "Settings"),
            Size = new Size(75, 30),
            Location = new Point(516, 340),
            ForeColor = Color.Black,
            Font = new Font("Microsoft YaHei", 9)
        };
        _settingsButton.Click += SettingsButton_Click;
        _steamStateLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.Gray,
            Location = new Point(170, 346)
        };
        Controls.AddRange([_lastUpdateLabel, _settingsButton, _steamStateLabel]);
    }
    /// <summary>第 4 面板：程序同步状态。</summary>
    private void CreateAppSyncPanel()
    {
        var panel = new FadingBottomPanel
        {
            Size = new Size(620, 155),
            Location = new Point(0, 160),
            BorderStyle = BorderStyle.None
        };
        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(122, 120, 220),
            Location = new Point(10, 15),
            Text = Loc.L("程序同步", "App Sync")
        };
        _appNameLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = new Font("Microsoft YaHei", 11, FontStyle.Bold),
            ForeColor = Color.Black,
            Location = new Point(100, 15),
            Text = Loc.L("未检测到程序", "No app detected")
        };
        _appCategoryLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.DarkGray,
            Location = new Point(100, 50),
            Text = ""
        };
        _appStatusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.Gray,
            Location = new Point(100, 80),
            Text = Loc.L("前台出现新程序会自动加入设置列表", "New foreground apps are added to the list automatically")
        };
        _appIconBox = new PictureBox
        {
            Location = new Point(12, 45),
            Size = new Size(52, 52),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        var hintLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.Gray,
            Location = new Point(10, 130),
            Text = Loc.L("在 设置 → 程序同步设置 中管理分类与显示名", "Manage categories and display names in Settings → Apps")
        };
        panel.Controls.AddRange(titleLabel, _appNameLabel, _appCategoryLabel, _appStatusLabel, _appIconBox, hintLabel);
        _appSyncPanel = panel;
        Controls.Add(panel);
    }

    /// <summary>程序图标按需更新（仅在路径变化时提取）。</summary>
    private void UpdateAppIcon()
    {
        if (_appIconBox == null) return;
        var path = Program.GetRpcManager()?.GetActiveAppIconPath() ?? "";
        if (path == _appIconPath) return;
        _appIconPath = path;
        _appIconBox.Image?.Dispose();
        _appIconBox.Image = LoadAppIcon(path);
    }

    private static Image? LoadAppIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch (Exception ex)
        {
            Logger.Diagnose($"提取程序图标失败: {ex.Message}");
            return null;
        }
    }

    private void UpdateAppSyncDisplay()
    {
        if (_appSyncPanel == null) return;
        var rpc = Program.GetRpcManager();
        var config = Configurations.Instance.Settings;
        if (rpc == null || !config.AppSyncEnabled)
        {
            // 未启用程序同步时隐藏整个面板（及分隔线），把主界面让给音乐面板
            _appSyncPanel.Visible = false;
            if (_appSyncDivider != null) _appSyncDivider.Visible = false;
            _appNameLabel.Text = Loc.L("程序同步未启用", "App sync is disabled");
            _appCategoryLabel.Text = "";
            _appStatusLabel.Text = "";
            _appStatusLabel.ForeColor = Color.Gray;
            return;
        }
        // 启用后恢复显示
        _appSyncPanel.Visible = true;
        if (_appSyncDivider != null) _appSyncDivider.Visible = true;
        var display = rpc.GetActiveAppDisplay();
        if (display == null)
        {
            _appNameLabel.Text = Loc.L("未检测到程序", "No app detected");
            _appCategoryLabel.Text = "";
            _appStatusLabel.Text = Loc.L("切到已启用的程序后将在 Steam 中显示", "Will show on Steam when you switch to an enabled app");
            _appStatusLabel.ForeColor = Color.Gray;
        }
        else
        {
            _appNameLabel.Text = display;
            _appCategoryLabel.Text = Loc.L($"分类：{rpc.GetActiveAppCategoryText()}", $"Category: {rpc.GetActiveAppCategoryText()}");
            _appStatusLabel.Text = Loc.L("已作为当前 Steam 状态显示", "Shown as your current Steam status");
            _appStatusLabel.ForeColor = Color.Green;
        }
    }
    private void CreatePlayerPanel(int index)
    {
        var yOffset = index * 160;
        var playerColor = _playerColors[index];
        var panel = new FadingBottomPanel
        {
            Size = new Size(620, 155),
            Location = new Point(0, yOffset),
            BorderStyle = BorderStyle.None
        };
        var coverPictureBox = new PictureBox
        {
            Size = new Size(80, 80),
            Location = new Point(10, 15),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.LightGray
        };
        var playerNameLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 10, FontStyle.Bold),
            ForeColor = playerColor,
            Location = new Point(100, 15),
            Size = new Size(100, 15),
            Text = _playerNames[index]
        };
        var textFlowPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Location = new Point(100, 35),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var songTitleLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 11, FontStyle.Bold),
            ForeColor = Color.Black,
            Text = Loc.L("暂无播放", "Nothing playing"),
            Padding = new Padding(0, 0, 0, 5),
            Margin = new Padding(0)
        };
        var artistLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.DarkGray,
            Text = "",
            Padding = new Padding(0, 0, 0, 5),
            Margin = new Padding(0)
        };
        var albumLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Font = new Font("Microsoft YaHei", 9),
            ForeColor = Color.DarkGray,
            Text = "",
            Margin = new Padding(0)
        };
        songTitleLabel.DoubleClick += (_, _) => OpenSongUrl(index);
        textFlowPanel.Controls.AddRange(songTitleLabel, artistLabel, albumLabel);
        var statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 9, FontStyle.Bold),
            ForeColor = Color.Gray,
            Location = new Point(350, 15),
            Size = new Size(80, 14),
            Text = Loc.L("未在播放", "Not playing")
        };
        var progressBar = new ProgressBar
        {
            Location = new Point(350, 40),
            Size = new Size(200, 18),
            Style = ProgressBarStyle.Continuous,
            Value = 0
        };
        var progressLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.DarkGray,
            Location = new Point(350, 65),
            Size = new Size(100, 12),
            Text = "00:00 / 00:00"
        };
        panel.Controls.AddRange(coverPictureBox, playerNameLabel, textFlowPanel, statusLabel, progressBar,
            progressLabel);
        _playerPanels[index] = panel;
        _coverPictureBoxes[index] = coverPictureBox;
        _playerNameLabels[index] = playerNameLabel;
        _songTitleLabels[index] = songTitleLabel;
        _artistLabels[index] = artistLabel;
        _albumLabels[index] = albumLabel;
        _statusLabels[index] = statusLabel;
        _progressBars[index] = progressBar;
        _progressLabels[index] = progressLabel;
        Controls.Add(panel);
    }
    private void SetupForm()
    {
        Text = Loc.L("MuSync - 音乐状态同步", "MuSync - Music status sync");
        Size = new Size(620, 430);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        BackColor = Color.WhiteSmoke;
        Icon = AppResource.Icon;
        FormClosing += MainForm_FormClosing;
        VisibleChanged += (_, _) =>
        {
            // 窗口隐藏（托盘驻留）时暂停 UI 刷新，节省无谓开销
            _updateTimer.Enabled = Visible;
        };
    }
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        var config = Configurations.Instance;
        if (config.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            Program.ShowMinimizeToTrayNotification();
        }
        else
        {
            Application.Exit();
        }
    }
    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var settingsForm = new SettingsForm();
        settingsForm.ShowDialog(this);
        // 设置关闭后重新应用外观（包含用户在「外观」分组里的修改）
        ApplyAppearance();
    }
    private void UpdateUpdateBadge()
    {
        var shouldShow = Program.PendingUpdate != null;
        if (shouldShow == _updateBadgeVisible) return;
        _updateBadgeVisible = shouldShow;
        // 红点由 FadingButton 自绘（原挂 Paint 事件的方式在自绘按钮上不会触发）
        _settingsButton.ShowUpdateBadge = shouldShow;
        _settingsButton.Invalidate();
    }

    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        UpdateDisplay();
    }
    private void UpdateDisplay(bool forceRefresh = false)
    {
        try
        {
            var rpcManager = Program.GetRpcManager();
            if (rpcManager == null) return;
            var (currentInfo, currentName) = rpcManager.GetCurrentPlayerInfo();
            UpdateMusicPanel(currentInfo, currentName, forceRefresh);
            ImageCacheManager.SetActiveKeys(_currentCacheKeys.Where(k => !string.IsNullOrEmpty(k)));
            _lastUpdateLabel.Text = Loc.L($"最后更新: {DateTime.Now:HH:mm:ss}", $"Last update: {DateTime.Now:HH:mm:ss}");
            UpdateSteamStateLabel();
            UpdateAppSyncDisplay();
            UpdateAppIcon();
            UpdateUpdateBadge();
        }
        catch (Exception ex)
        {
            _lastUpdateLabel.Text = Loc.L($"更新失败: {ex.Message}", $"Update failed: {ex.Message}");
        }
    }
    private void UpdateSteamStateLabel()
    {
        if (_steamStateLabel == null) return;
        var session = Program.GetSessionManager();
        var config = Configurations.Instance.Settings;
        string text;
        Color color;
        if (!config.EnableSteamSync)
        {
            text = Loc.L("Steam 同步未启用", "Steam sync is disabled");
            color = Color.Gray;
        }
        else if (session == null)
        {
            text = Loc.L("Steam 服务未初始化", "Steam service not initialized");
            color = Color.Red;
        }
        else if (!session.IsLoggedOn)
        {
            text = session.IsConnected ? Loc.L("Steam 未登录", "Not signed in to Steam") : Loc.L("正在连接 Steam...", "Connecting to Steam...");
            color = session.IsConnected ? Color.Orange : Color.Gray;
        }
        else if (config.PauseWhenPlayingGame && Program.GetSteamManager()?.IsRealGameActive == true)
        {
            text = Loc.L("正在玩真实游戏，音乐同步已暂停", "Playing a real game — music sync paused");
            color = Color.Orange;
        }
        else
        {
            text = Loc.L("Steam 已登录，同步中", "Signed in to Steam — syncing");
            color = Color.Green;
        }
        if (_steamStateLabel.Text != text)
        {
            _steamStateLabel.Text = text;
            _steamStateLabel.ForeColor = color;
        }
    }
    /// <summary>首页音乐面板：跟随当前活跃源（受播放器优先级与"正在播放优先"规则影响）。</summary>
    private void UpdateMusicPanel(PlayerInfo? playerInfo, string playerName, bool forceRefresh)
    {
        if (!string.IsNullOrEmpty(playerName) && _lastMusicSourceName != playerName)
        {
            _lastMusicSourceName = playerName;
            _playerNameLabels[0].Text = Loc.PlayerName(playerName);
            _playerNameLabels[0].ForeColor = playerName switch
            {
                "网易云音乐" => _playerColors[0],
                "QQ音乐" => _playerColors[1],
                "LX Music" => _playerColors[2],
                "酷狗音乐" => _playerColors[3],
                _ => Color.FromArgb(122, 120, 220)
            };
        }
        UpdatePlayerDisplay(0, playerInfo, playerName, playerInfo != null, forceRefresh, RpcManager.ErrorCode.None);
    }

    private void UpdatePlayerDisplay(int index, PlayerInfo? playerInfo, string playerName, bool isActive,
        bool forceRefresh, RpcManager.ErrorCode lastError)
    {
        var lastInfo = _lastPlayerInfos[index];
        var lastActive = _lastActiveStates[index];
        if (!forceRefresh && lastActive == isActive && lastError == RpcManager.ErrorCode.None)
        {
            if (!isActive) return; 
            if (lastInfo.HasValue && playerInfo.HasValue &&
                lastInfo.Value.Title == playerInfo.Value.Title &&
                lastInfo.Value.Artists == playerInfo.Value.Artists &&
                lastInfo.Value.Album == playerInfo.Value.Album &&
                lastInfo.Value.Cover == playerInfo.Value.Cover &&
                lastInfo.Value.Pause == playerInfo.Value.Pause &&
                Math.Abs(lastInfo.Value.Schedule - playerInfo.Value.Schedule) < 0.5 && 
                Math.Abs(lastInfo.Value.Duration - playerInfo.Value.Duration) < 0.5) 
            {
                return;
            }
        }
        _lastPlayerInfos[index] = playerInfo;
        _lastActiveStates[index] = isActive;
        if (isActive && playerInfo != null)
        {
            const string zeroWidthSpace = "\u200B";
            var title = string.IsNullOrEmpty(playerInfo.Value.Title)
                ? StringUtils.GetTruncatedStringByMaxByteLength(Loc.L("未知歌曲", "Unknown track"), 128)
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Title + zeroWidthSpace, 128);
            var artists = string.IsNullOrEmpty(playerInfo.Value.Artists)
                ? ""
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Artists + zeroWidthSpace, 128);
            var album = string.IsNullOrEmpty(playerInfo.Value.Album)
                ? ""
                : StringUtils.GetTruncatedStringByMaxByteLength(playerInfo.Value.Album + zeroWidthSpace, 128);
            var currentSongId = playerInfo.Value.Identity;
            var previousSongId = _currentSongIds[index];
            if (!string.IsNullOrEmpty(currentSongId) && currentSongId != previousSongId)
            {
                _currentSongIds[index] = currentSongId;
            }
            if (_songTitleLabels[index].Text != title) _songTitleLabels[index].Text = title;
            var artistText = string.IsNullOrEmpty(artists) ? "" : $"🎤 {artists}";
            if (_artistLabels[index].Text != artistText) _artistLabels[index].Text = artistText;
            var albumText = string.IsNullOrEmpty(album) ? "" : $"💿 {album}";
            if (_albumLabels[index].Text != albumText) _albumLabels[index].Text = albumText;
            var statusText = playerInfo.Value.Pause ? Loc.L("⏸️ 已暂停", "⏸️ Paused") : Loc.L("▶️ 正在播放", "▶️ Playing");
            var statusColor = playerInfo.Value.Pause ? Color.Orange : Color.Green;
            if (_statusLabels[index].Text != statusText)
            {
                _statusLabels[index].Text = statusText;
                _statusLabels[index].ForeColor = statusColor;
            }
            if (playerInfo.Value.Duration > 0)
            {
                var progressPercentage = (int)((playerInfo.Value.Schedule / playerInfo.Value.Duration) * 100);
                var newValue = Math.Max(0, Math.Min(100, progressPercentage));
                if (_progressBars[index].Value != newValue) _progressBars[index].Value = newValue;
                var currentTime = TimeSpan.FromSeconds(playerInfo.Value.Schedule);
                var totalTime = TimeSpan.FromSeconds(playerInfo.Value.Duration);
                var progressText = $@"{currentTime:mm\:ss} / {totalTime:mm\:ss}";
                if (_progressLabels[index].Text != progressText) _progressLabels[index].Text = progressText;
            }
            else
            {
                if (_progressBars[index].Value != 0) _progressBars[index].Value = 0;
                if (_progressLabels[index].Text != "00:00 / 00:00") _progressLabels[index].Text = "00:00 / 00:00";
            }
            if (!string.IsNullOrEmpty(playerInfo.Value.Cover))
            {
                var uniqueCacheKey = string.IsNullOrEmpty(currentSongId)
                    ? playerInfo.Value.Cover
                    : $"{playerInfo.Value.Cover}_{currentSongId}";
                // 缓存保护 key 延迟到新封面真正显示时再登记（见 LoadCoverAsyncWithUniqueKey），此间旧 key 保持保护状态
                if (_currentCoverUrls[index] != playerInfo.Value.Cover)
                {
                    _currentCoverUrls[index] = playerInfo.Value.Cover;
                    Logger.Diagnose(
                        $"Updating cover for '{playerInfo.Value.Title}' (ID: {currentSongId}).");
                    Logger.Diagnose($"  - Cover URL: {playerInfo.Value.Cover}");
                    Logger.Diagnose($"  - Cache Key: {uniqueCacheKey}");
                    LoadCoverAsyncWithUniqueKey(index, playerInfo.Value.Cover, uniqueCacheKey);
                }
            }
            else
            {
                _currentCacheKeys[index] = "";
                // 酷狗不提供封面：用它自己的应用图标作为占位图
                var kuGouIcon = playerName == "酷狗音乐" ? KuGou.GetAppIcon() : null;
                if (kuGouIcon != null)
                {
                    if (!ReferenceEquals(_coverPictureBoxes[index].Image, kuGouIcon))
                    {
                        _coverPictureBoxes[index].BackColor = Color.White;
                        _coverPictureBoxes[index].Image = kuGouIcon;
                        _currentCoverUrls[index] = "";
                    }
                }
                else if (_coverPictureBoxes[index].Image != null)
                {
                    _coverPictureBoxes[index].Image = null;
                    _coverPictureBoxes[index].BackColor = Color.LightGray;
                    _currentCoverUrls[index] = "";
                }
            }
            // 标题与歌名颜色：默认跟随播放器品牌色，可在设置→显示→外观中自定义
            var appearance = Configurations.Instance.Settings;
            var accentColor = !appearance.AppearanceTitleFollowPlayer && appearance.AppearanceTitleColorArgb is int customTitleArgb
                ? Color.FromArgb(customTitleArgb)
                : GetPlayerAccentColor(playerName);
            if (_playerNameLabels[index].ForeColor != accentColor)
                _playerNameLabels[index].ForeColor = accentColor;
            var titleColor = !appearance.AppearanceSongFollowPlayer && appearance.AppearanceSongColorArgb is int customSongArgb
                ? Color.FromArgb(customSongArgb)
                : ToReadableTitleColor(accentColor);
            if (_songTitleLabels[index].ForeColor != titleColor)
                _songTitleLabels[index].ForeColor = titleColor;
        }
        else
        {
            var defaultTitle = StringUtils.GetTruncatedStringByMaxByteLength(Loc.L("未在播放音乐", "Nothing playing"), 128);
            if (_songTitleLabels[index].Text != defaultTitle) _songTitleLabels[index].Text = defaultTitle;
            if (_artistLabels[index].Text != "") _artistLabels[index].Text = "";
            if (_albumLabels[index].Text != "") _albumLabels[index].Text = "";
            string statusText;
            switch (lastError)
            {
                case RpcManager.ErrorCode.PermissionDenied:
                    statusText = Loc.L("⚠️ 需要管理员运行", "⚠️ Admin rights required");
                    break;
                case RpcManager.ErrorCode.DllNotFound:
                    statusText = Loc.L("⚠️ 播放器组件未加载", "⚠️ Player component not loaded");
                    break;
                case RpcManager.ErrorCode.VersionNotSupported:
                    statusText = Loc.L("⚠️ 版本不支持/特征码失效", "⚠️ Unsupported version / pattern not found");
                    break;
                default:
                    statusText = Loc.L("未在播放", "Not playing");
                    break;
            }
            if (_statusLabels[index].Text != statusText)
            {
                _statusLabels[index].Text = statusText;
                _statusLabels[index].ForeColor = lastError != RpcManager.ErrorCode.None ? Color.Red : Color.Gray;
            }
            if (_progressBars[index].Value != 0) _progressBars[index].Value = 0;
            if (_progressLabels[index].Text != "00:00 / 00:00") _progressLabels[index].Text = "00:00 / 00:00";
            if (_coverPictureBoxes[index].Image != null)
            {
                _coverPictureBoxes[index].Image = null;
                _coverPictureBoxes[index].BackColor = Color.LightGray;
            }
            if (_playerNameLabels[index].ForeColor != Color.Gray) _playerNameLabels[index].ForeColor = Color.Gray;
            _currentSongIds[index] = string.Empty;
            _currentCoverUrls[index] = string.Empty;
            _currentCacheKeys[index] = string.Empty;
        }
    }
    /// <summary>播放器强调色（主界面标题与歌名共用，取品牌色）。</summary>
    private static Color GetPlayerAccentColor(string playerName) => playerName switch
    {
        "网易云音乐" => _playerColors[0],
        "QQ音乐" => _playerColors[1],
        "LX Music" => _playerColors[2],
        "酷狗音乐" => _playerColors[3],
        _ => Color.FromArgb(122, 120, 220)
    };

    /// <summary>应用外观设置（背景色 / 背景图 / 字体）；启动与设置关闭后调用。</summary>
    internal void ApplyAppearance()
    {
        var settings = Configurations.Instance.Settings;
        try
        {
            // 背景色
            BackColor = settings.AppearanceBackgroundColorArgb is int backgroundArgb
                ? Color.FromArgb(backgroundArgb)
                : Color.WhiteSmoke;

            // 背景图（替换前释放旧图，避免句柄泄漏）
            var oldBackground = BackgroundImage;
            BackgroundImage = null;
            oldBackground?.Dispose();
            if (!string.IsNullOrWhiteSpace(settings.AppearanceBackgroundImage) &&
                File.Exists(settings.AppearanceBackgroundImage))
            {
                try
                {
                    using var stream = File.OpenRead(settings.AppearanceBackgroundImage);
                    using var loaded = Image.FromStream(stream);
                    // 复制一份：Image.FromStream 要求源流保持打开，而 stream 会被释放
                    BackgroundImage = new Bitmap(loaded);
                    BackgroundImageLayout = settings.AppearanceBackgroundLayout switch
                    {
                        "Zoom" => ImageLayout.Zoom,
                        "Tile" => ImageLayout.Tile,
                        "Center" => ImageLayout.Center,
                        _ => ImageLayout.Stretch
                    };
                }
                catch (Exception e)
                {
                    Logger.Warn($"[Appearance] 背景图加载失败: {e.Message}");
                }
            }

            // 子控件透明化：让半透明面板透出背景（文本/图片不挡底）
            ApplyChildTransparency();

            // 字体（仅在用户显式设置过时应用）
            if (!string.IsNullOrWhiteSpace(settings.AppearanceFontFamily) || settings.AppearanceFontSize > 0)
            {
                var family = string.IsNullOrWhiteSpace(settings.AppearanceFontFamily)
                    ? (Font?.FontFamily.Name ?? "Microsoft YaHei")
                    : settings.AppearanceFontFamily;
                var size = settings.AppearanceFontSize > 0 ? settings.AppearanceFontSize : (Font?.Size ?? 9f);
                ApplyFontRecursive(this, new Font(family, size));
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"[Appearance] 应用外观失败: {e.Message}");
        }
    }

    /// <summary>把面板与状态栏内的文本/图片控件设为透明，透出半透明面板与背景。</summary>
    private void ApplyChildTransparency()
    {
        foreach (var panel in new Control?[] { _playerPanels[0], _appSyncPanel })
        {
            if (panel == null) continue;
            foreach (Control child in panel.Controls)
            {
                MakeTransparentRecursive(child);
            }
        }
        _lastUpdateLabel.BackColor = Color.Transparent;
        _steamStateLabel.BackColor = Color.Transparent;
    }

    private static void MakeTransparentRecursive(Control control)
    {
        if (control is Label or PictureBox or FlowLayoutPanel or Panel)
        {
            control.BackColor = Color.Transparent;
        }
        if (control.HasChildren)
        {
            foreach (Control child in control.Controls)
            {
                MakeTransparentRecursive(child);
            }
        }
    }

    private static void ApplyFontRecursive(Control parent, Font font)
    {
        foreach (Control child in parent.Controls)
        {
            child.Font = font;
            if (child.HasChildren) ApplyFontRecursive(child, font);
        }
    }

    private static Color ToReadableTitleColor(Color color)
    {
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        if (luminance <= 0.6) return color;
        var factor = 0.6 / Math.Max(luminance, 0.001);
        return Color.FromArgb(
            (int)(color.R * factor),
            (int)(color.G * factor),
            (int)(color.B * factor));
    }

    private async void LoadCoverAsyncWithUniqueKey(int index, string coverUrl, string forceCacheKey)
    {
        try
        {
            var image = await ImageCacheManager.LoadImageAsync(forceCacheKey, coverUrl);
            if (IsHandleCreated && image != null)
            {
                await InvokeAsync(() =>
                {
                    if (coverUrl == _currentCoverUrls[index])
                    {
                        var pictureBox = _coverPictureBoxes[index];
                        try
                        {
                            _ = image.RawFormat;
                            pictureBox.Image = image;
                            _currentCacheKeys[index] = forceCacheKey;
                            pictureBox.BackColor = Color.White;
                        }
                        catch (Exception ex)
                        {
                            pictureBox.Image = null;
                            pictureBox.BackColor = Color.LightGray;
                            Logger.Error($"Invalid image detected: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.Diagnose($"Stale cover update ignored for URL: {coverUrl}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load cover image with timestamp: {ex.Message}");
            if (IsHandleCreated)
            {
                await InvokeAsync(() =>
                {
                    var pictureBox = _coverPictureBoxes[index];
                    pictureBox.Image = null;
                    pictureBox.BackColor = Color.LightGray;
                });
            }
        }
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _updateTimer.Start();
        UpdateDisplay(true);
    }
    /// <summary>双击歌曲标题时用默认浏览器打开歌曲链接（仅网易云/QQ 音乐提供）。</summary>
    private void OpenSongUrl(int index)
    {
        if (index < 0 || index >= _lastPlayerInfos.Length) return;
        var info = _lastPlayerInfos[index];
        if (info is not { } playerInfo || string.IsNullOrEmpty(playerInfo.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(playerInfo.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error($"打开歌曲链接失败: {ex.Message}");
        }
    }
    private static void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e is not { Control: true, KeyCode: Keys.R }) return;
        var beforeMemory = MemoryPressureMonitor.GetCurrentMemoryUsage();
        ImageCacheManager.ForceCleanupCache();
        var afterMemory = MemoryPressureMonitor.GetCurrentMemoryUsage();
        var freedMemory = beforeMemory - afterMemory;
        MessageBox.Show(
            Loc.L(
                $"缓存清理完成！\n清理前: {beforeMemory / 1024 / 1024:F1} MB\n清理后: {afterMemory / 1024 / 1024:F1} MB\n释放: {freedMemory / 1024 / 1024:F1} MB\n\n提示: 按 Ctrl+R 可随时清理缓存",
                $"Cache cleared!\nBefore: {beforeMemory / 1024 / 1024:F1} MB\nAfter: {afterMemory / 1024 / 1024:F1} MB\nFreed: {freedMemory / 1024 / 1024:F1} MB\n\nTip: press Ctrl+R anytime to clear the cache"),
            Loc.L("缓存清理", "Clear cache"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
