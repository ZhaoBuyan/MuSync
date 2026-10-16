using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;

namespace MuSync;

/// <summary>
/// 统一设置窗口（单窗五页：常规 / 同步 / 显示 / 程序 / 诊断）。
/// 所有设置条目平铺在标签页中，不再有"设置里的设置"。
/// </summary>
internal sealed class SettingsForm : Form
{
    // —— 常规页 ——
    private CheckBox _autoStartCheckBox = null!;
    private CheckBox _closeToTrayCheckBox = null!;
    private CheckBox _startInTrayCheckBox = null!;
    private ComboBox _languageCombo = null!;
    private CheckBox _allowWebSocketFallbackCheckBox = null!;
    private Label _accountStatusLabel = null!;
    private Button _loginButton = null!;
    private Button _logoutButton = null!;

    // —— 同步页 ——
    private CheckBox _enableSteamSyncCheckBox = null!;
    private CheckBox _musicSyncCheckBox = null!;
    private CheckBox _hidePausedMusicCheckBox = null!;
    private CheckBox _browserMediaCheckBox = null!;
    private CheckBox _enableAppSyncCheckBox = null!;
    private CheckBox _syncNonGameCheckBox = null!;
    private CheckBox _pauseWhenPlayingGameCheckBox = null!;
    private ComboBox _syncSpeedCombo = null!;
    private ListBox _playerPriorityList = null!;
    private readonly List<Button> _playerPriorityMoveButtons = [];

    // —— 显示页 ——
    private TextBox _musicFormatBox = null!;
    private TextBox _programFormatBox = null!;
    private TextBox _combinedFormatBox = null!;
    private ComboBox _separatorCombo = null!;
    private ComboBox _progressBarStyleCombo = null!;
    private NumericUpDown _barLengthBox = null!;
    private ComboBox _templatePresetCombo = null!;
    private Label _previewLabel = null!;

    // 常驻状态控件（显示页）
    private CheckBox _persistentStatusCheck = null!;
    private TextBox _persistentStatusBox = null!;

    // 外观设置控件
    private CheckBox _titleFollowCheck = null!;
    private Button _titleColorButton = null!;
    private CheckBox _songFollowCheck = null!;
    private Button _songColorButton = null!;
    private Button _fontButton = null!;
    private Button _backgroundColorButton = null!;
    private Label _backgroundImageLabel = null!;
    private ComboBox _backgroundLayoutCombo = null!;
    private double[]? _backgroundCropRect;
    private string _backgroundImagePath = "";
    private string _appearanceFontFamily = "";
    private float _appearanceFontSize;
    private bool _updatingTemplatePreset;

    // —— 程序页 ——
    private DataGridView _rulesGrid = null!;
    private readonly List<AppRule> _rules = [];

    // —— 诊断页 ——
    private Label _memoryInfoLabel = null!;
    private Label _versionLabel = null!;

    // —— 关于页 ——
    private Label _aboutVersionLabel = null!;
    private Label _updateNoticeLabel = null!;

    // —— 底部按钮 ——
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    private Button _applyButton = null!;

    // —— 定时器 ——
    private readonly Timer _perfTimer = new() { Interval = 2000 };
    private readonly Timer _previewTimer = new() { Interval = 2000 };

    /// <summary>播放器键表。internal 供测试核对「与 RpcManager.DefaultPlayerOrder / ConfigData.PlayerPriority 三处一致」。</summary>
    internal static readonly string[] PlayerOrderKeys = ["NetEase", "Tencent", "LxMusic", "KuGou", "TTPlayer", "Browser"];

    public SettingsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = Loc.L("设置 - MuSync", "MuSync - Settings");
        Size = new Size(700, 660);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei", 9);
        BackColor = Color.White;

        // ================= 底部按钮 =================
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.White
        };
        _okButton = CreateDialogButton(Loc.L("确定", "OK"), 400);
        _okButton.Click += (_, _) =>
        {
            SaveSettings();
            Close();
        };
        _cancelButton = CreateDialogButton(Loc.L("取消", "Cancel"), 488);
        _cancelButton.Click += (_, _) => Close();
        _applyButton = CreateDialogButton(Loc.L("应用", "Apply"), 576);
        _applyButton.Click += (_, _) => SaveSettings();
        buttonPanel.Controls.AddRange([_okButton, _cancelButton, _applyButton]);

        // ================= Tab 容器 =================
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 6)
        };
        tabs.TabPages.Add(CreateGeneralPage());
        tabs.TabPages.Add(CreateSyncPage());
        tabs.TabPages.Add(CreateDisplayPage());
        tabs.TabPages.Add(CreateAppsPage());
        tabs.TabPages.Add(CreateDataSourcesPage());
        tabs.TabPages.Add(CreateDiagnosticsPage());
        tabs.TabPages.Add(CreateAboutPage());

        Controls.AddRange([tabs, buttonPanel]);

        _perfTimer.Tick += (_, _) =>
        {
            RefreshMemoryInfo();
            UpdateAccountStatus();
        };
        _perfTimer.Start();
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();

        FormClosed += (_, _) =>
        {
            _perfTimer.Dispose();
            _previewTimer.Dispose();
        };
    }

    private Button CreateDialogButton(string text, int x)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, 12),
            Size = new Size(80, 28),
            BackColor = Color.White
        };
        return button;
    }

    // ================= 常规页 =================
    private TabPage CreateGeneralPage()
    {
        var page = new TabPage(Loc.L("常规", "General")) { BackColor = Color.White };

        var startupGroup = CreateGroupBox(Loc.L("启动与托盘", "Startup & Tray"), 10, 10, 650, 150);
        _autoStartCheckBox = CreateCheckBox(Loc.L("开机自启", "Start with Windows"), 20, 30);
        _closeToTrayCheckBox = CreateCheckBox(Loc.L("关闭窗口时隐藏到托盘", "Minimize to tray when the window is closed"), 20, 62);
        _startInTrayCheckBox = CreateCheckBox(Loc.L("启动时隐藏到托盘", "Start minimized to tray"), 20, 94);

        var languageLabel = new Label
        {
            AutoSize = true,
            Location = new Point(315, 34),
            Text = Loc.L("语言:", "Language:")
        };
        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(400, 30),
            Size = new Size(110, 28)
        };
        _languageCombo.Items.AddRange(["中文", "English"]);
        _languageCombo.SelectedIndex = Math.Clamp(Configurations.Instance.Settings.Language, 0, 1);
        var languageHint = new Label
        {
            AutoSize = true,
            Location = new Point(518, 34),
            ForeColor = Color.Gray,
            Text = Loc.L("（重启后生效）", "(applies after restart)")
        };
        startupGroup.Controls.AddRange([_autoStartCheckBox, _closeToTrayCheckBox, _startInTrayCheckBox, languageLabel, _languageCombo, languageHint]);

        var networkGroup = CreateGroupBox(Loc.L("网络", "Network"), 10, 172, 650, 72);
        _allowWebSocketFallbackCheckBox = CreateCheckBox(Loc.L("TCP 连接失败时自动用 WebSocket (443) 重试", "Retry over WebSocket (443) if TCP fails"), 20, 28);
        networkGroup.Controls.AddRange([_allowWebSocketFallbackCheckBox]);

        var accountGroup = CreateGroupBox(Loc.L("Steam 账户", "Steam Account"), 10, 256, 650, 125);
        _accountStatusLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 30),
            Text = Loc.L("状态检查中…", "Checking status..."),
            ForeColor = Color.Gray
        };
        _loginButton = new Button
        {
            Text = Loc.L("重新登录", "Sign in again"),
            Location = new Point(20, 66),
            Size = new Size(110, 30),
            BackColor = Color.White
        };
        _loginButton.Click += LoginButton_Click;
        _logoutButton = new Button
        {
            Text = Loc.L("退出登录", "Sign out"),
            Location = new Point(145, 66),
            Size = new Size(110, 30),
            BackColor = Color.White,
            ForeColor = Color.Red
        };
        _logoutButton.Click += LogoutButton_Click;
        accountGroup.Controls.AddRange([_accountStatusLabel, _loginButton, _logoutButton]);

        page.Controls.AddRange([startupGroup, networkGroup, accountGroup]);
        return page;
    }

    // ================= 同步页 =================
    private TabPage CreateSyncPage()
    {
        var page = new TabPage(Loc.L("同步", "Sync")) { BackColor = Color.White };

        var switchGroup = CreateGroupBox(Loc.L("同步开关", "Sync options"), 10, 10, 650, 160);
        _enableSteamSyncCheckBox = CreateCheckBox(Loc.L("启用 Steam 同步（总开关）", "Enable Steam sync (master switch)"), 20, 32);
        _musicSyncCheckBox = CreateCheckBox(Loc.L("启用音乐同步", "Enable music sync"), 20, 66);
        _hidePausedMusicCheckBox = CreateCheckBox(Loc.L("音乐暂停时不在状态中显示", "Hide music status when paused"), 20, 100);
        _browserMediaCheckBox = CreateCheckBox(
            Loc.L("同步浏览器里正在播放的媒体（视频 / 音频）", "Sync media playing in your browser (video / audio)"), 20, 134);
        _enableAppSyncCheckBox = CreateCheckBox(Loc.L("启用程序同步", "Enable app sync"), 340, 32);
        _syncNonGameCheckBox = CreateCheckBox(Loc.L("同步非游戏应用", "Sync non-game apps"), 340, 66);
        _pauseWhenPlayingGameCheckBox = CreateCheckBox(Loc.L("玩真实 Steam 游戏时自动暂停", "Auto-pause while playing a real Steam game"), 340, 100);
        switchGroup.Controls.AddRange([
            _enableSteamSyncCheckBox, _musicSyncCheckBox, _hidePausedMusicCheckBox, _browserMediaCheckBox,
            _enableAppSyncCheckBox, _syncNonGameCheckBox, _pauseWhenPlayingGameCheckBox
        ]);

        var priorityGroup = CreateGroupBox(Loc.L("音乐播放器优先级", "Music player priority"), 10, 182, 650, 182);
        var priorityLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 32),
            Text = Loc.L("从上到下依次优先（正在播放的始终优先）：", "Top to bottom, highest first (now-playing always wins):")
        };
        var priorityHint = new Label
        {
            AutoSize = true,
            Location = new Point(228, 60),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L("选中一项，用右侧按钮上下调整", "Select an item, then use the buttons on the right to move it")
        };
        _playerPriorityList = new ListBox
        {
            Location = new Point(20, 58),
            // 高度留足：5 项 × 约 17px 逻辑高度，再叠上 DPI 缩放余量，保证不出现内部滚动条
            // （实测 94 在 125% 缩放下只渲染出 72px，装不下 5 项）
            Size = new Size(196, 120),
            BackColor = Color.White
        };
        _playerPriorityList.SelectedIndexChanged += (_, _) => UpdatePriorityMoveButtons();
        var moveUpButton = new Button
        {
            Text = Loc.L("上移", "Move up"),
            Location = new Point(228, 80),
            Size = new Size(78, 26),
            BackColor = Color.White
        };
        moveUpButton.Click += (_, _) => MovePriorityItem(-1);
        var moveDownButton = new Button
        {
            Text = Loc.L("下移", "Move down"),
            Location = new Point(314, 80),
            Size = new Size(78, 26),
            BackColor = Color.White
        };
        moveDownButton.Click += (_, _) => MovePriorityItem(1);
        _playerPriorityMoveButtons.AddRange([moveUpButton, moveDownButton]);
        priorityGroup.Controls.AddRange([priorityLabel, priorityHint, _playerPriorityList, moveUpButton, moveDownButton]);

        var speedGroup = CreateGroupBox(Loc.L("同步频率", "Sync frequency"), 10, 376, 650, 90);
        var speedLabel = new Label { Text = Loc.L("进度刷新档位:", "Update rate:"), Location = new Point(20, 32), AutoSize = true };
        _syncSpeedCombo = new ComboBox
        {
            Location = new Point(130, 28),
            Width = 240,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _syncSpeedCombo.Items.AddRange([Loc.L("快速（0.25 秒）", "Fast (0.25 s)"), Loc.L("标准（0.5 秒，推荐）", "Standard (0.5 s, recommended)"), Loc.L("省流（1 秒）", "Data saver (1 s)")]);
        var speedHint = new Label
        {
            AutoSize = true,
            Location = new Point(20, 60),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L("更快的刷新让 Steam 端更顺滑；省流档可降低被服务器限流的概率", "Faster updates look smoother on Steam; data saver reduces the chance of server rate-limiting")
        };
        speedGroup.Controls.AddRange([speedLabel, _syncSpeedCombo, speedHint]);

        var lxHint = new Label
        {
            Location = new Point(10, 396),
            Size = new Size(650, 60),
            ForeColor = Color.FromArgb(150, 110, 40),
            Font = new Font("Microsoft YaHei", 8.5f),
            Text = Loc.L("洛雪音乐用户请注意：请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，并允许来自局域网的访问。", "LX Music users: please enable \"Open API service\" under LX Music → Settings → Open API, and allow LAN access.")
        };

        page.Controls.AddRange([switchGroup, priorityGroup, speedGroup, lxHint]);
        return page;
    }

    // ================= 显示页 =================
    private TabPage CreateDisplayPage()
    {
        var page = new TabPage(Loc.L("显示", "Display")) { BackColor = Color.White };

        var templateGroup = CreateGroupBox(Loc.L("状态文本模板", "Status text template"), 10, 10, 650, 330);

        var presetLabel = new Label { Text = Loc.L("模板预设:", "Presets:"), Location = new Point(20, 34), AutoSize = true };
        _templatePresetCombo = new ComboBox
        {
            Location = new Point(95, 30),
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _templatePresetCombo.Items.AddRange([Loc.L("自定义", "Custom"), Loc.L("简洁（默认）", "Simple (default)"), Loc.L("带前缀：正在玩 / 正在听", "With prefix (Playing / Listening)"), Loc.L("只要名字", "Name only")]);
        _templatePresetCombo.SelectedIndexChanged += (_, _) => ApplyTemplatePreset(_templatePresetCombo.SelectedIndex);

        var musicFormatLabel = new Label { Text = Loc.L("音乐格式:", "Music format:"), Location = new Point(20, 70), AutoSize = true };
        _musicFormatBox = new TextBox { Location = new Point(118, 66), Width = 417 };
        var musicBlocksButton = new Button
        {
            Text = Loc.L("积木", "Blocks"),
            Location = new Point(545, 65),
            Size = new Size(85, 26),
            BackColor = Color.White
        };
        musicBlocksButton.Click += (_, _) => OpenBlockEditor(TemplateKind.Music, _musicFormatBox);

        var programFormatLabel = new Label { Text = Loc.L("程序格式:", "App format:"), Location = new Point(20, 102), AutoSize = true };
        _programFormatBox = new TextBox { Location = new Point(118, 98), Width = 417 };
        var programBlocksButton = new Button
        {
            Text = Loc.L("积木", "Blocks"),
            Location = new Point(545, 97),
            Size = new Size(85, 26),
            BackColor = Color.White
        };
        programBlocksButton.Click += (_, _) => OpenBlockEditor(TemplateKind.Program, _programFormatBox);

        var combinedFormatLabel = new Label { Text = Loc.L("组合格式:", "Combined format:"), Location = new Point(20, 134), AutoSize = true };
        _combinedFormatBox = new TextBox { Location = new Point(118, 130), Width = 417 };
        var combinedBlocksButton = new Button
        {
            Text = Loc.L("积木", "Blocks"),
            Location = new Point(545, 129),
            Size = new Size(85, 26),
            BackColor = Color.White
        };
        combinedBlocksButton.Click += (_, _) => OpenBlockEditor(TemplateKind.Combined, _combinedFormatBox);

        var separatorLabel = new Label { Text = Loc.L("分隔符:", "Separator:"), Location = new Point(20, 170), AutoSize = true };
        _separatorCombo = new ComboBox
        {
            Location = new Point(95, 166),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _separatorCombo.Items.AddRange(["‖", " | ", " · ", " — ", " ~ ", " + "]);

        var barStyleLabel = new Label { Text = Loc.L("进度条样式:", "Progress bar style:"), Location = new Point(250, 170), AutoSize = true };
        _progressBarStyleCombo = new ComboBox
        {
            Location = new Point(385, 166),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _progressBarStyleCombo.Items.AddRange(["#-", "█░", "▰▱", "●○", "■□", "▮▯"]);
        var barLengthLabel = new Label { Text = Loc.L("长度:", "Length:"), Location = new Point(512, 170), AutoSize = true };
        _barLengthBox = new NumericUpDown
        {
            Location = new Point(576, 166),
            Width = 64,
            Minimum = 1,
            Maximum = 50,
            Value = 10
        };

        var variablesHint = new Label
        {
            AutoSize = true,
            Location = new Point(20, 200),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L(
                "变量：{app} 程序名｜{song} 歌名｜{artist} 歌手｜{artistPart} 自动连接符的歌手｜{progress} 进度｜{sep} 分隔符",
                "Variables: {app} app name | {song} song | {artist} artist | {artistPart} artist with auto separators | {progress} progress | {sep} separator")
        };

        _previewLabel = new Label
        {
            Location = new Point(20, 228),
            Size = new Size(610, 88),
            ForeColor = Color.FromArgb(0, 102, 51)
        };

        _musicFormatBox.TextChanged += (_, _) => { UpdatePreview(); MarkPresetCustom(); };
        _programFormatBox.TextChanged += (_, _) => { UpdatePreview(); MarkPresetCustom(); };
        _combinedFormatBox.TextChanged += (_, _) => { UpdatePreview(); MarkPresetCustom(); };
        _separatorCombo.TextChanged += (_, _) => { UpdatePreview(); MarkPresetCustom(); };
        _progressBarStyleCombo.TextChanged += (_, _) => UpdatePreview();
        _barLengthBox.ValueChanged += (_, _) => UpdatePreview();

        templateGroup.Controls.AddRange([
            presetLabel, _templatePresetCombo,
            musicFormatLabel, _musicFormatBox, musicBlocksButton,
            programFormatLabel, _programFormatBox, programBlocksButton,
            combinedFormatLabel, _combinedFormatBox, combinedBlocksButton,
            separatorLabel, _separatorCombo, barStyleLabel, _progressBarStyleCombo,
            barLengthLabel, _barLengthBox,
            variablesHint, _previewLabel
        ]);

        var persistentGroup = CreatePersistentStatusGroup();
        var appearanceGroup = CreateAppearanceGroup();
        page.AutoScroll = true;
        page.Controls.AddRange([templateGroup, persistentGroup, appearanceGroup]);
        return page;
    }

    /// <summary>「常驻状态」分组：无音乐 / 程序来源时推送的自定义文案（显示页内）。</summary>
    private GroupBox CreatePersistentStatusGroup()
    {
        var group = CreateGroupBox(Loc.L("常驻状态", "Persistent status"), 10, 350, 650, 112);

        _persistentStatusCheck = CreateCheckBox(
            Loc.L("没有音乐 / 程序时显示下面这句（原本会清除状态）", "Show the line below when no music or app is active (instead of clearing)"),
            20, 30);

        var textLabel = new Label { Text = Loc.L("文案:", "Text:"), Location = new Point(20, 66), AutoSize = true };
        _persistentStatusBox = new TextBox { Location = new Point(70, 62), Width = 560, MaxLength = 128 };
        var hint = new Label
        {
            Location = new Point(20, 90),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L("支持 emoji；超过 128 字节按 UTF-8 截断（约 42 个汉字）", "Emoji supported; longer text is truncated to 128 bytes")
        };

        _persistentStatusCheck.CheckedChanged += (_, _) => _persistentStatusBox.Enabled = _persistentStatusCheck.Checked;

        group.Controls.AddRange([_persistentStatusCheck, textLabel, _persistentStatusBox, hint]);
        return group;
    }

    /// <summary>「外观」分组：自定义主界面的颜色 / 字体 / 背景（显示页内，可滚动查看）。</summary>
    private GroupBox CreateAppearanceGroup()
    {
        var group = CreateGroupBox(Loc.L("外观", "Appearance"), 10, 472, 650, 196);

        // 行 1：标题颜色 / 歌名颜色
        var titleColorLabel = new Label { Text = Loc.L("标题颜色:", "Title color:"), Location = new Point(20, 36), AutoSize = true };
        _titleFollowCheck = CreateCheckBox(Loc.L("跟随播放器", "Follow player"), 110, 32);
        _titleColorButton = CreateColorButton(new Point(230, 30));
        var songColorLabel = new Label { Text = Loc.L("歌名颜色:", "Song title color:"), Location = new Point(310, 36), AutoSize = true };
        _songFollowCheck = CreateCheckBox(Loc.L("跟随标题", "Follow title"), 425, 32);
        _songColorButton = CreateColorButton(new Point(535, 30));

        // 行 2：字体 / 背景色
        var fontLabel = new Label { Text = Loc.L("字体:", "Font:"), Location = new Point(20, 72), AutoSize = true };
        _fontButton = new Button
        {
            Text = Loc.L("微软雅黑 9pt", "Microsoft YaHei 9pt"),
            Location = new Point(110, 68),
            Size = new Size(160, 26),
            BackColor = Color.White
        };
        _fontButton.Click += FontButton_Click;
        var backgroundColorLabel = new Label { Text = Loc.L("背景色:", "Background color:"), Location = new Point(350, 72), AutoSize = true };
        _backgroundColorButton = CreateColorButton(new Point(475, 66));

        // 行 3：背景图 / 排版
        var backgroundImageLabel = new Label { Text = Loc.L("背景图:", "Background image:"), Location = new Point(20, 108), AutoSize = true };
        _backgroundImageLabel = new Label
        {
            Text = Loc.L("（无）", "(None)"),
            Location = new Point(150, 110),
            Size = new Size(145, 20),
            ForeColor = Color.Gray,
            AutoEllipsis = true
        };
        var chooseImageButton = new Button
        {
            Text = Loc.L("选择…", "Choose..."),
            Location = new Point(300, 104),
            Size = new Size(70, 26),
            BackColor = Color.White
        };
        chooseImageButton.Click += ChooseBackgroundImage_Click;
        var clearImageButton = new Button
        {
            Text = Loc.L("清除", "Clear"),
            Location = new Point(375, 104),
            Size = new Size(60, 26),
            BackColor = Color.White
        };
        clearImageButton.Click += (_, _) =>
        {
            _backgroundImagePath = "";
            _backgroundCropRect = null;
            UpdateBackgroundImageLabel();
        };
        var layoutLabel = new Label { Text = Loc.L("排版:", "Layout:"), Location = new Point(465, 108), AutoSize = true };
        _backgroundLayoutCombo = new ComboBox
        {
            Location = new Point(510, 104),
            Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _backgroundLayoutCombo.Items.AddRange([Loc.L("拉伸", "Stretch"), Loc.L("适应", "Fit"), Loc.L("平铺", "Tile"), Loc.L("居中", "Center")]);

        // 行 4：恢复默认
        var resetButton = new Button
        {
            Text = Loc.L("恢复默认外观", "Reset appearance"),
            Location = new Point(20, 146),
            Size = new Size(130, 28),
            BackColor = Color.White
        };
        resetButton.Click += (_, _) => ResetAppearanceControls();

        _titleColorButton.Click += (_, _) => PickColor(_titleColorButton);
        _songColorButton.Click += (_, _) => PickColor(_songColorButton);
        _backgroundColorButton.Click += (_, _) => PickColor(_backgroundColorButton);
        _titleFollowCheck.CheckedChanged += (_, _) => UpdateAppearanceEnabled();
        _songFollowCheck.CheckedChanged += (_, _) => UpdateAppearanceEnabled();

        group.Controls.AddRange([
            titleColorLabel, _titleFollowCheck, _titleColorButton,
            songColorLabel, _songFollowCheck, _songColorButton,
            fontLabel, _fontButton, backgroundColorLabel, _backgroundColorButton,
            backgroundImageLabel, _backgroundImageLabel, chooseImageButton, clearImageButton,
            layoutLabel, _backgroundLayoutCombo,
            resetButton
        ]);
        return group;
    }

    private static Button CreateColorButton(Point location) => new()
    {
        Text = "",
        Location = location,
        Size = new Size(48, 26),
        BackColor = Color.WhiteSmoke,
        FlatStyle = FlatStyle.Flat
    };

    /// <summary>打开取色器并更新色块按钮。</summary>
    private static void PickColor(Button button)
    {
        using var dialog = new ColorDialog { Color = button.BackColor, FullOpen = true };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            button.BackColor = dialog.Color;
        }
    }

    private void FontButton_Click(object? sender, EventArgs e)
    {
        var initialFamily = string.IsNullOrWhiteSpace(_appearanceFontFamily) ? "Microsoft YaHei" : _appearanceFontFamily;
        var initialSize = _appearanceFontSize > 0 ? _appearanceFontSize : 9f;
        using var dialog = new FontDialog { Font = new Font(initialFamily, initialSize) };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _appearanceFontFamily = dialog.Font.FontFamily.Name;
            _appearanceFontSize = dialog.Font.Size;
            _fontButton.Text = $"{_appearanceFontFamily} {_appearanceFontSize:0.#}pt";
        }
    }

    private void ChooseBackgroundImage_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = Loc.L("图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*", "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*")
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        // 选好图后进裁剪窗口：拖框＝用框内区域，不拖＝用整张图
        // 打不开的图（格式不支持 / 文件损坏）当场拦下：不进裁剪窗口，也不改当前设置
        try
        {
            using var cropForm = new BackgroundCropForm(dialog.FileName, null);
            if (cropForm.ShowDialog(this) != DialogResult.OK) return;
            _backgroundImagePath = dialog.FileName;
            _backgroundCropRect = cropForm.ResultCrop;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Settings] 打开背景图失败: {ex.Message}");
            MessageBox.Show(this,
                Loc.L("这张图片打不开，请换一张（支持 PNG / JPG / BMP / GIF）。", "This image can't be opened — please pick another one (PNG / JPG / BMP / GIF)."),
                Loc.L("背景图", "Background image"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        UpdateBackgroundImageLabel();
    }

    /// <summary>刷新背景图标签：文件名 + 是否已裁剪。</summary>
    private void UpdateBackgroundImageLabel()
    {
        if (string.IsNullOrWhiteSpace(_backgroundImagePath))
        {
            _backgroundImageLabel.Text = Loc.L("（无）", "(None)");
            _backgroundImageLabel.ForeColor = Color.Gray;
            return;
        }
        _backgroundImageLabel.Text = Path.GetFileName(_backgroundImagePath) +
            (_backgroundCropRect is { Length: 4 } ? Loc.L("（已裁剪）", " (cropped)") : "");
        _backgroundImageLabel.ForeColor = Color.Black;
    }

    private void UpdateAppearanceEnabled()
    {
        _titleColorButton.Enabled = !_titleFollowCheck.Checked;
        _songColorButton.Enabled = !_songFollowCheck.Checked;
    }

    /// <summary>把外观控件恢复为默认（不立即保存，随设置窗的确定/应用生效）。</summary>
    private void ResetAppearanceControls()
    {
        _titleFollowCheck.Checked = true;
        _songFollowCheck.Checked = true;
        _titleColorButton.BackColor = Color.WhiteSmoke;
        _songColorButton.BackColor = Color.WhiteSmoke;
        _backgroundColorButton.BackColor = Color.WhiteSmoke;
        _appearanceFontFamily = "";
        _appearanceFontSize = 0;
        _fontButton.Text = Loc.L("微软雅黑 9pt", "Microsoft YaHei 9pt");
        _backgroundImagePath = "";
        _backgroundCropRect = null;
        UpdateBackgroundImageLabel();
        _backgroundLayoutCombo.SelectedIndex = 0;
        UpdateAppearanceEnabled();
    }

    // ================= 程序页 =================
    private TabPage CreateAppsPage()
    {
        var page = new TabPage(Loc.L("程序", "Apps")) { BackColor = Color.White };

        BuildRulesGrid();
        _rulesGrid.Location = new Point(10, 10);

        var addCurrentButton = new Button
        {
            Text = Loc.L("添加当前前台程序", "Add current foreground app"),
            Location = new Point(10, 288),
            Size = new Size(150, 28),
            BackColor = Color.White
        };
        addCurrentButton.Click += AddCurrentButton_Click;
        var removeButton = new Button
        {
            Text = Loc.L("删除选中", "Remove selected"),
            Location = new Point(170, 288),
            Size = new Size(110, 28),
            BackColor = Color.White
        };
        removeButton.Click += RemoveButton_Click;
        var exportButton = new Button
        {
            Text = Loc.L("导出规则…", "Export rules…"),
            Location = new Point(290, 288),
            Size = new Size(110, 28),
            BackColor = Color.White
        };
        exportButton.Click += ExportRulesButton_Click;
        var importButton = new Button
        {
            Text = Loc.L("导入规则…", "Import rules…"),
            Location = new Point(410, 288),
            Size = new Size(110, 28),
            BackColor = Color.White
        };
        importButton.Click += ImportRulesButton_Click;

        // 两行说明放在按钮下方：
        // ① 讲清分类建议的来源与读取范围（本地库、不联网），消除"是不是要联网/AI"的疑虑；
        // ② 顺带把库规模写出来（内置多少个程序），让"为什么它能认出这个程序"变得可解释。
        var sourceHint = new Label
        {
            AutoSize = true,
            Location = new Point(10, 324),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L(
                "分类建议来自本机内置程序名库（不联网，只读进程名与窗口标题）",
                "Suggestions come from the built-in local app library (offline; only process names and window titles are read)")
        };
        var librarySizeHint = new Label
        {
            AutoSize = true,
            Location = new Point(10, 344),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8),
            Text = Loc.L(
                $"内置 {AppClassifier.LibrarySize} 个程序（含忽略名单）",
                $"{AppClassifier.LibrarySize} apps built in (including the ignore list)")
        };

        page.Controls.AddRange([_rulesGrid, addCurrentButton, removeButton, exportButton, importButton,
            sourceHint, librarySizeHint]);
        return page;
    }

    // ================= 诊断页 =================
    private TabPage CreateDiagnosticsPage()
    {
        var page = new TabPage(Loc.L("诊断", "Diagnostics")) { BackColor = Color.White };

        var memoryGroup = CreateGroupBox(Loc.L("性能监控", "Performance"), 10, 10, 650, 180);
        _memoryInfoLabel = new Label
        {
            Location = new Point(20, 30),
            Size = new Size(610, 100),
            Text = Loc.L("读取中…", "Loading...")
        };
        var refreshButton = new Button
        {
            Text = Loc.L("刷新内存信息", "Refresh memory info"),
            Location = new Point(20, 132),
            Size = new Size(120, 28),
            BackColor = Color.White
        };
        refreshButton.Click += (_, _) => RefreshMemoryInfo();
        memoryGroup.Controls.AddRange([_memoryInfoLabel, refreshButton]);

        var toolGroup = CreateGroupBox(Loc.L("诊断工具", "Diagnostic tools"), 10, 202, 650, 180);
        var openLogsButton = new Button
        {
            Text = Loc.L("打开日志文件夹", "Open logs folder"),
            Location = new Point(20, 34),
            Size = new Size(140, 30),
            BackColor = Color.White
        };
        openLogsButton.Click += (_, _) => OpenLogsFolder();
        var logsHint = new Label
        {
            AutoSize = true,
            Location = new Point(175, 41),
            ForeColor = Color.Gray,
            Text = Loc.L("遇到问题时，把这里最新日期的日志文件发给开发者", "If you run into a problem, send the latest log file to the developer")
        };
        _versionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 90),
            Text = Loc.L("版本：-", "Version: -")
        };
        var copyDiagButton = new Button
        {
            Text = Loc.L("复制诊断信息", "Copy diagnostics"),
            Location = new Point(20, 128),
            Size = new Size(140, 30),
            BackColor = Color.White
        };
        var copyDiagResetTimer = new System.Windows.Forms.Timer { Interval = 1600 };
        copyDiagResetTimer.Tick += (_, _) =>
        {
            copyDiagResetTimer.Stop();
            copyDiagButton.Text = Loc.L("复制诊断信息", "Copy diagnostics");
        };
        copyDiagButton.Click += (_, _) =>
        {
            if (CopyDiagnosticsInfo())
            {
                copyDiagButton.Text = Loc.L("已复制 ✓", "Copied ✓");
                copyDiagResetTimer.Stop();
                copyDiagResetTimer.Start();
            }
        };
        copyDiagButton.Disposed += (_, _) => copyDiagResetTimer.Dispose();
        var copyDiagHint = new Label
        {
            AutoSize = true,
            Location = new Point(175, 135),
            ForeColor = Color.Gray,
            Text = Loc.L("版本 / 内存 / 配置与日志路径（反馈问题时方便粘贴）", "Version / memory / config/log paths (handy to paste when reporting issues)")
        };
        toolGroup.Controls.AddRange([openLogsButton, logsHint, _versionLabel, copyDiagButton, copyDiagHint]);

        page.Controls.AddRange([memoryGroup, toolGroup]);
        return page;
    }

    // ================= 关于页 =================
    private TabPage CreateAboutPage()
    {
        var page = new TabPage(Loc.L("关于", "About")) { BackColor = Color.White };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(20, 24),
            Font = new Font("Microsoft YaHei", 16, FontStyle.Bold),
            Text = "MuSync"
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(22, 70),
            ForeColor = Color.Gray,
            Text = Loc.L("把音乐软件与任意程序的当前状态同步到 Steam", "Sync your music player and any app's status to Steam")
        };
        _aboutVersionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(22, 102),
            Text = Loc.L("版本：-", "Version: -")
        };
        var repoButton = new Button
        {
            Text = Loc.L("项目主页（GitHub）", "Project page (GitHub)"),
            Location = new Point(20, 140),
            Size = new Size(170, 30),
            BackColor = Color.White
        };
        repoButton.Click += (_, _) => OpenUrl("https://github.com/ZhaoBuyan/MuSync");
        var releaseButton = new Button
        {
            Text = Loc.L("下载与更新日志", "Downloads && changelog"),
            Location = new Point(205, 140),
            Size = new Size(170, 30),
            BackColor = Color.White
        };
        releaseButton.Click += (_, _) => OpenUrl("https://github.com/ZhaoBuyan/MuSync/releases");
        var checkUpdateButton = new Button
        {
            Text = Loc.L("检查更新", "Check for updates"),
            Location = new Point(390, 140),
            Size = new Size(150, 30),
            BackColor = Color.White
        };
        checkUpdateButton.Click += async (_, _) => await CheckUpdateFromAboutAsync();
        var licenseButton = new Button
        {
            Text = Loc.L("开源许可", "Licenses"),
            Location = new Point(20, 172),
            Size = new Size(150, 30),
            BackColor = Color.White
        };
        licenseButton.Click += (_, _) =>
        {
            using var form = new LicenseForm();
            form.ShowDialog(this);
        };
        _updateNoticeLabel = new Label
        {
            AutoSize = true,
            Location = new Point(370, 176),
            ForeColor = Color.FromArgb(233, 74, 62),
            Visible = false
        };
        var licenseLabel = new Label
        {
            Location = new Point(22, 204),
            Size = new Size(620, 130),
            ForeColor = Color.Gray,
            Font = new Font("Microsoft YaHei", 8.5f),
            Text = Loc.L("作者：ZhaoBuyan ｜ 本项目以 MIT 协议开源发布。\n基于开源谱系「半新写」构建，感谢所有铺路者（详见仓库 THIRD-PARTY-NOTICES）。\n\n洛雪音乐用户请注意：请在 洛雪音乐 → 设置 → 开放API 中「启用开放API服务」，\n并允许来自局域网的访问。", "By ZhaoBuyan | Open source under the MIT license.\nBuilt on an open-source lineage, roughly half rewritten — thanks to everyone who paved the way (see THIRD-PARTY-NOTICES in the repo).\n\nLX Music users: enable \"Open API service\" in LX Music → Settings → Open API, and allow LAN access.")
        };

        page.Controls.AddRange([title, subtitle, _aboutVersionLabel, repoButton, releaseButton, checkUpdateButton, licenseButton, _updateNoticeLabel, licenseLabel]);
        RefreshUpdateNotice();
        return page;
    }

    private async Task CheckUpdateFromAboutAsync()
    {
        // 后台已发现更新时直接展示（不再重复联网，也不重复打扰）
        if (Program.PendingUpdate is { } pending)
        {
            ShowUpdateDialog(pending);
            return;
        }
        var info = await Task.Run(UpdateChecker.CheckAsync);
        if (info == null)
        {
            MessageBox.Show(Loc.L("当前已是最新版本（或网络暂不可用，稍后再试）。", "You're on the latest version (or the network is unavailable — try again later)."), Loc.L("检查更新", "Check for updates"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Program.SetPendingUpdate(info);
        RefreshUpdateNotice();
        ShowUpdateDialog(info);
    }

    private void ShowUpdateDialog(UpdateChecker.UpdateInfo info)
    {
        using var updateForm = new UpdateForm(UpdateChecker.GetCurrentVersionText(), info);
        updateForm.ShowDialog(this);
    }

    /// <summary>关于页的更新红点：后台发现有新版本时显示（与主窗口红点、托盘菜单项同步）。</summary>
    private void RefreshUpdateNotice()
    {
        var info = Program.PendingUpdate;
        if (info == null)
        {
            _updateNoticeLabel.Visible = false;
            return;
        }
        _updateNoticeLabel.Text = Loc.L($"● 有新版本 {info.Tag}", $"● New version {info.Tag} available");
        _updateNoticeLabel.Visible = true;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.L($"打开链接失败：{ex.Message}", $"Failed to open the link: {ex.Message}"), Loc.L("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 数据源页 =================

    /// <summary>数据源编辑行的控件集合（保存时逐行读回）。</summary>
    private sealed class DataSourceRowControls
    {
        public required CheckBox Enabled { get; init; }
        public required TextBox Name { get; init; }
        public required TextBox Url { get; init; }
        public required TextBox ValuePath { get; init; }
        public required TextBox Format { get; init; }
        public required NumericUpDown Refresh { get; init; }
        public required Label Status { get; init; }
        public required Panel Container { get; init; }
    }

    private readonly List<DataSourceRowControls> _dataSourceRows = [];
    private Panel _dataSourcesPanel = null!;
    private Label _dataSourceHint = null!;

    private TabPage CreateDataSourcesPage()
    {
        var page = new TabPage(Loc.L("数据源", "Data sources")) { BackColor = Color.White };

        var intro = new Label
        {
            AutoSize = true,
            Location = new Point(10, 8),
            MaximumSize = new Size(660, 0),
            ForeColor = Color.DimGray,
            Text = Loc.L(
                "从任意返回 JSON 的 HTTP 地址取一个值，当作状态模板里的积木用。" +
                "例如心率服务填 地址=http://127.0.0.1:9000/heartrate、取值=heart_rate、显示=❤{v}，" +
                "就能在格式模板里写 {心率}。取不到值时该积木展开为空，不影响其它同步。",
                "Fetch a value from any HTTP endpoint that returns JSON and use it as a template block. " +
                "Missing values expand to nothing and never affect other syncing.")
        };
        page.Controls.Add(intro);

        var addButton = new Button
        {
            Text = Loc.L("添加数据源", "Add source"),
            Location = new Point(10, 74),
            Size = new Size(110, 28)
        };
        addButton.Click += (_, _) => AddDataSourceRow(new DataSourceConfig
        {
            Enabled = true,
            Name = Loc.L("心率", "Heart rate"),
            Url = "http://127.0.0.1:9000/heartrate",
            ValuePath = "heart_rate",
            Format = "❤{v}",
            RefreshSeconds = 1
        });

        var testButton = new Button
        {
            Text = Loc.L("测试当前填写", "Test as filled"),
            Location = new Point(128, 74),
            Size = new Size(120, 28)
        };
        testButton.Click += async (_, _) => await TestDataSourcesAsync();

        _dataSourceHint = new Label
        {
            AutoSize = true,
            Location = new Point(258, 80),
            ForeColor = Color.DimGray,
            Text = Loc.L("提示：音乐类服务关闭时其积木会展开为空。", "Tip: blocks expand to nothing while the service is down.")
        };
        page.Controls.AddRange([addButton, testButton, _dataSourceHint]);

        _dataSourcesPanel = new Panel
        {
            Location = new Point(10, 108),
            Size = new Size(660, 300),
            AutoScroll = true,
            BackColor = Color.White
        };
        page.Controls.Add(_dataSourcesPanel);
        return page;
    }

    /// <summary>把一条数据源配置渲染成一行可编辑控件。</summary>
    private void AddDataSourceRow(DataSourceConfig source)
    {
        var row = new Panel
        {
            Location = new Point(0, _dataSourceRows.Count * 118),
            Size = new Size(640, 112),
            BackColor = Color.White
        };

        var enabled = new CheckBox
        {
            Text = Loc.L("启用", "Enabled"),
            Location = new Point(0, 4),
            AutoSize = true,
            Checked = source.Enabled
        };
        var nameLabel = new Label { Text = Loc.L("名称", "Name"), Location = new Point(70, 6), AutoSize = true };
        var name = new TextBox { Location = new Point(110, 3), Size = new Size(110, 23), Text = source.Name };
        var urlLabel = new Label { Text = Loc.L("地址", "URL"), Location = new Point(228, 6), AutoSize = true };
        var url = new TextBox { Location = new Point(268, 3), Size = new Size(360, 23), Text = source.Url };

        var pathLabel = new Label { Text = Loc.L("取值", "Value path"), Location = new Point(70, 36), AutoSize = true };
        var path = new TextBox { Location = new Point(110, 33), Size = new Size(150, 23), Text = source.ValuePath };
        var formatLabel = new Label { Text = Loc.L("显示", "Format"), Location = new Point(268, 36), AutoSize = true };
        var format = new TextBox { Location = new Point(308, 33), Size = new Size(120, 23), Text = source.Format };
        var refreshLabel = new Label { Text = Loc.L("间隔(秒)", "Every (s)"), Location = new Point(436, 36), AutoSize = true };
        var refresh = new NumericUpDown
        {
            Location = new Point(500, 33),
            Size = new Size(60, 23),
            Minimum = 1,
            Maximum = 3600,
            Value = Math.Clamp(source.RefreshSeconds, 1, 3600)
        };
        var removeButton = new Button
        {
            Text = Loc.L("删除", "Remove"),
            Location = new Point(570, 32),
            Size = new Size(58, 25)
        };
        var status = new Label
        {
            Location = new Point(0, 64),
            Size = new Size(630, 20),
            ForeColor = Color.DimGray,
            Text = Loc.L("状态：未测试", "Status: not tested")
        };

        var controls = new DataSourceRowControls
        {
            Enabled = enabled,
            Name = name,
            Url = url,
            ValuePath = path,
            Format = format,
            Refresh = refresh,
            Status = status,
            Container = row
        };
        removeButton.Click += (_, _) =>
        {
            _dataSourceRows.Remove(controls);
            _dataSourcesPanel.Controls.Remove(row);
            row.Dispose();
            RelayoutDataSourceRows();
        };

        row.Controls.AddRange([enabled, nameLabel, name, urlLabel, url, pathLabel, path,
            formatLabel, format, refreshLabel, refresh, removeButton, status]);
        _dataSourcesPanel.Controls.Add(row);
        _dataSourceRows.Add(controls);
    }

    private void RelayoutDataSourceRows()
    {
        for (var i = 0; i < _dataSourceRows.Count; i++)
        {
            _dataSourceRows[i].Container.Location = new Point(0, i * 118);
        }
    }

    /// <summary>按当前界面上的填写读回数据源列表（保存设置时用）。</summary>
    private List<DataSourceConfig> CollectDataSources()
    {
        var list = new List<DataSourceConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _dataSourceRows)
        {
            var name = row.Name.Text.Trim();
            if (name.Length == 0) continue;
            // ⚠️ 名称就是模板里的积木名：大括号会让积木边界出现歧义，直接拒收并提示
            if (name.Contains('{') || name.Contains('}'))
            {
                row.Status.ForeColor = Color.Firebrick;
                row.Status.Text = Loc.L("名称不能包含大括号 { }", "Name cannot contain { }");
                continue;
            }
            if (!seen.Add(name))
            {
                row.Status.ForeColor = Color.Firebrick;
                row.Status.Text = Loc.L("名称重复，已忽略这一条", "Duplicate name, ignored");
                continue;
            }
            list.Add(new DataSourceConfig
            {
                Enabled = row.Enabled.Checked,
                Name = name,
                Url = row.Url.Text.Trim(),
                ValuePath = row.ValuePath.Text.Trim(),
                Format = row.Format.Text,
                RefreshSeconds = (int)row.Refresh.Value
            });
        }
        return list;
    }

    /// <summary>
    /// 「测试」按钮：按界面上的填写立刻各读一次，把 HTTP 结果与取到的值显示出来。
    /// 这是通用数据源最容易配错的地方（地址、字段名、嵌套路径），没有它根本没法排查。
    /// </summary>
    private async Task TestDataSourcesAsync()
    {
        var rows = _dataSourceRows.ToArray();
        if (rows.Length == 0)
        {
            _dataSourceHint.ForeColor = Color.DimGray;
            _dataSourceHint.Text = Loc.L("还没有添加数据源。", "No data sources yet.");
            return;
        }
        _dataSourceHint.ForeColor = Color.DimGray;
        _dataSourceHint.Text = Loc.L("正在测试…", "Testing…");
        var okCount = 0;
        foreach (var row in rows)
        {
            var source = new DataSourceConfig
            {
                Enabled = true,          // 测试时忽略"启用"开关：没启用也想验证配置对不对
                Name = row.Name.Text.Trim(),
                Url = row.Url.Text.Trim(),
                ValuePath = row.ValuePath.Text.Trim(),
                Format = row.Format.Text,
                RefreshSeconds = 1
            };
            var result = await Utils.HttpDataSource.ProbeAsync(source);
            if (result.Ok) okCount++;
            row.Status.ForeColor = result.Ok ? Color.ForestGreen : Color.Firebrick;
            row.Status.Text = result.Ok
                ? Loc.L($"成功：{result.RawBody}  →  {result.Value}  →  积木展开为 \"{result.Display}\"",
                        $"OK: {result.RawBody}  →  {result.Value}  →  block renders \"{result.Display}\"")
                : Loc.L($"失败：{result.Error}", $"Failed: {result.Error}");
        }
        _dataSourceHint.ForeColor = okCount == rows.Length ? Color.ForestGreen : Color.Firebrick;
        _dataSourceHint.Text = Loc.L($"{okCount}/{rows.Length} 个可用", $"{okCount}/{rows.Length} reachable");
    }


    private static GroupBox CreateGroupBox(string text, int x, int y, int width, int height)
    {
        return new GroupBox
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.White
        };
    }

    private static CheckBox CreateCheckBox(string text, int x, int y)
    {
        return new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            BackColor = Color.White
        };
    }

    // ================= 程序规则网格 =================
    private void BuildRulesGrid()
    {
        _rulesGrid = new DataGridView
        {
            Size = new Size(630, 268),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = Loc.L("启用", "Enabled"), FillWeight = 8 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("程序", "App"), ReadOnly = true, FillWeight = 19 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("显示名", "Display name"), FillWeight = 22 });
        var categoryColumn = new DataGridViewComboBoxColumn { HeaderText = Loc.L("分类", "Category"), FillWeight = 13, FlatStyle = FlatStyle.Flat };
        categoryColumn.Items.AddRange(Loc.L("忽略", "Ignore"), Loc.L("游戏", "Game"), Loc.L("工作", "Work"), Loc.L("媒体", "Media"), Loc.L("社交", "Social"), Loc.L("其他", "Other"));
        _rulesGrid.Columns.Add(categoryColumn);
        var modeColumn = new DataGridViewComboBoxColumn { HeaderText = Loc.L("模式", "Mode"), FillWeight = 15, FlatStyle = FlatStyle.Flat };
        modeColumn.Items.AddRange(Loc.L("前台时显示", "When in foreground"), Loc.L("运行即显示", "Always when running"));
        _rulesGrid.Columns.Add(modeColumn);
        var overrideColumn = new DataGridViewComboBoxColumn { HeaderText = Loc.L("策略", "Policy"), FillWeight = 14, FlatStyle = FlatStyle.Flat };
        overrideColumn.Items.AddRange(Loc.L("跟随分类", "Follow category"), Loc.L("强制显示", "Always show"), Loc.L("强制隐藏", "Always hide"));
        _rulesGrid.Columns.Add(overrideColumn);
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("状态", "Status"), ReadOnly = true, FillWeight = 9 });
        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty)
            {
                _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _rulesGrid.CellValueChanged += RulesGrid_CellValueChanged;
        _rulesGrid.DataError += (_, e) => { e.ThrowException = false; };
    }

    private void RulesGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rulesGrid.Rows.Count) return;
        var row = _rulesGrid.Rows[e.RowIndex];
        if (row.Tag is not AppRule rule) return;
        switch (e.ColumnIndex)
        {
            case 0:
                rule.Enabled = Convert.ToBoolean(row.Cells[0].Value ?? false);
                break;
            case 2:
                rule.DisplayName = row.Cells[2].Value?.ToString() ?? "";
                break;
            case 3:
                rule.Category = CategoryFromText(row.Cells[3].Value?.ToString());
                break;
            case 4:
                rule.Mode = row.Cells[4].Value?.ToString() == Loc.L("运行即显示", "Always when running")
                    ? AppSyncMode.Always
                    : AppSyncMode.Foreground;
                break;
            case 5:
                rule.Override = OverrideFromText(row.Cells[5].Value?.ToString());
                break;
            default:
                return;
        }
        rule.IsUserConfirmed = true;
        ApplyRowStyle(row, rule);
    }

    private void AddCurrentButton_Click(object? sender, EventArgs e)
    {
        var foreground = ForegroundWatcher.GetCurrent();
        if (foreground == null)
        {
            MessageBox.Show(Loc.L("未检测到前台程序。", "No foreground app detected."), Loc.L("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rules.Any(r => r.ExeName.Equals(foreground.ExeName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(Loc.L($"{foreground.ExeName} 已在列表中。", $"{foreground.ExeName} is already in the list."), Loc.L("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (category, suggestedName, _) = AppClassifier.Suggest(
            foreground.ExeName, foreground.WindowTitle, foreground.IsFullscreen, foreground.ExePath);
        _rules.Add(new AppRule
        {
            ExeName = foreground.ExeName,
            DisplayName = suggestedName,
            Category = category,
            Enabled = true,
            IsUserConfirmed = true
        });
        RefreshRulesGrid();
    }

    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        if (_rulesGrid.SelectedRows.Count == 0) return;
        var selected = _rulesGrid.SelectedRows[0];
        if (selected.Tag is not AppRule rule) return;
        _rules.Remove(rule);
        RefreshRulesGrid();
    }

    private void RefreshRulesGrid()
    {
        _rulesGrid.Rows.Clear();
        foreach (var rule in _rules)
        {
            var index = _rulesGrid.Rows.Add(
                rule.Enabled,
                rule.ExeName,
                rule.DisplayName,
                CategoryToText(rule.Category),
                rule.Mode == AppSyncMode.Always ? Loc.L("运行即显示", "Always when running") : Loc.L("前台时显示", "When in foreground"),
                OverrideToText(rule.Override),
                rule.IsUserConfirmed ? Loc.L("已确认", "Confirmed") : Loc.L("程序名库", "App library"));
            _rulesGrid.Rows[index].Tag = rule;
            ApplyRowStyle(_rulesGrid.Rows[index], rule);
        }
    }

    private static void ApplyRowStyle(DataGridViewRow row, AppRule rule)
    {
        if (!rule.IsUserConfirmed)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 215);
        }
        else if (!rule.Enabled)
        {
            row.DefaultCellStyle.ForeColor = Color.Gray;
        }
        row.Cells[6].Value = rule.IsUserConfirmed ? Loc.L("已确认", "Confirmed") : Loc.L("程序名库", "App library");
    }

    // ================= 规则导入 / 导出 =================
    /// <summary>导出全部规则（含忽略类与未启用条目——导回去才是完整现场）。</summary>
    private void ExportRulesButton_Click(object? sender, EventArgs e)
    {
        if (_rules.Count == 0)
        {
            MessageBox.Show(this, Loc.L("当前没有任何规则可导出。", "There are no rules to export."),
                Loc.L("导出规则", "Export rules"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = Loc.L("MuSync 规则文件|*.json|所有文件|*.*", "MuSync rule file|*.json|All files|*.*"),
            FileName = $"musync-app-rules-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = "json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AppRuleExchange.Export(dialog.FileName, _rules, DateTime.Now);
            Logger.Info($"[AppSync] 已导出 {_rules.Count} 条规则 -> {dialog.FileName}");
            MessageBox.Show(this,
                Loc.L($"已导出 {_rules.Count} 条规则。", $"Exported {_rules.Count} rule(s)."),
                Loc.L("导出规则", "Export rules"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error($"[AppSync] 导出规则失败: {ex}");
            MessageBox.Show(this,
                Loc.L($"导出失败：{ex.Message}", $"Export failed: {ex.Message}"),
                Loc.L("导出规则", "Export rules"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>导入规则：只补新、不动已有；确认前不写盘。</summary>
    private void ImportRulesButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = Loc.L("MuSync 规则文件|*.json|所有文件|*.*", "MuSync rule file|*.json|All files|*.*")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var result = AppRuleExchange.Import(dialog.FileName, _rules);
        if (!result.Ok)
        {
            MessageBox.Show(this, result.Error, Loc.L("导入规则", "Import rules"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        foreach (var reason in result.IgnoredReasons)
        {
            Logger.Info($"[AppSync] 导入忽略：{reason}");
        }

        var summary = Loc.L(
            $"新增 {result.Added} 条，跳过已有 {result.Skipped} 条，忽略无效 {result.Ignored} 条。\n\n" +
            "跳过＝本机已有同程序，保留本机设置；忽略＝条目缺失必要信息。",
            $"Added {result.Added}, skipped {result.Skipped} existing, ignored {result.Ignored} invalid.\n\n" +
            "Skipped = the app already exists locally (local settings kept); ignored = the entry lacks required info.");
        if (MessageBox.Show(this, summary, Loc.L("导入规则", "Import rules"),
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;   // 用户取消：不改内存、不写盘
        }

        // 走与「确定」相同的提交路径：更新内存 + 刷新表格；落盘由 Configurations 统一负责
        _rules.Clear();
        _rules.AddRange(result.Merged);
        RefreshRulesGrid();
        Configurations.Instance.Save();
        Logger.Info($"[AppSync] 已导入规则：新增 {result.Added} / 跳过 {result.Skipped} / 忽略 {result.Ignored}");
    }

    // ================= 播放器优先级（列表 + 上移/下移，不再限定个数） =================
    /// <summary>优先级列表项：存 key，显示名按当前语言取（沿用 Loc.PlayerName 的「中文做 key」口径）。</summary>
    private sealed record PriorityItem(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private void LoadPriorityOrder()
    {
        var saved = Configurations.Instance.Settings.PlayerPriority ?? [];
        // 存过的顺序在前，没提到的按内置默认补在后面；新增播放器（如千千静听）无需迁移配置就能出现
        var order = saved.Concat(PlayerOrderKeys).Distinct()
            .Where(k => PlayerOrderKeys.Contains(k)).ToList();
        _playerPriorityList.BeginUpdate();
        _playerPriorityList.Items.Clear();
        foreach (var key in order)
        {
            _playerPriorityList.Items.Add(new PriorityItem(key, Loc.PlayerName(PlayerNameOf(key))));
        }
        _playerPriorityList.EndUpdate();
        if (_playerPriorityList.Items.Count > 0) _playerPriorityList.SelectedIndex = 0;
        UpdatePriorityMoveButtons();
    }

    /// <summary>把优先级项上移（offset = -1）或下移（offset = +1），移动后保持选中。</summary>
    private void MovePriorityItem(int offset)
    {
        var index = _playerPriorityList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _playerPriorityList.Items.Count) return;
        var item = _playerPriorityList.Items[index];
        _playerPriorityList.Items.RemoveAt(index);
        _playerPriorityList.Items.Insert(target, item);
        _playerPriorityList.SelectedIndex = target;
        UpdatePriorityMoveButtons();
    }

    /// <summary>首末项的按钮置灰（再往上/下没有位置了）。</summary>
    private void UpdatePriorityMoveButtons()
    {
        if (_playerPriorityMoveButtons.Count < 2) return;
        var index = _playerPriorityList.SelectedIndex;
        var count = _playerPriorityList.Items.Count;
        _playerPriorityMoveButtons[0].Enabled = index > 0;
        _playerPriorityMoveButtons[1].Enabled = index >= 0 && index < count - 1;
    }

    /// <summary>当前列表顺序（写配置用）。</summary>
    private List<string> CurrentPriorityOrder() =>
        _playerPriorityList.Items.Cast<PriorityItem>().Select(i => i.Key).ToList();

    /// <summary>key → 数据层中文名（Loc.PlayerName 以中文名为 key，与诊断/日志口径一致）。</summary>
    private static string PlayerNameOf(string key) => key switch
    {
        "NetEase" => "网易云音乐",
        "Tencent" => "QQ音乐",
        "LxMusic" => "洛雪音乐",
        "KuGou" => "酷狗音乐",
        "TTPlayer" => "千千静听",
        _ => key
    };

    // ================= 模板预设 / 积木 =================
    private void OpenBlockEditor(TemplateKind kind, TextBox formatBox)
    {
        using var editor = new FormatBlockEditorForm(kind, formatBox.Text, NonEmpty(_separatorCombo.Text, "‖"));
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        formatBox.Text = editor.ResultFormat;
    }

    private void MarkPresetCustom()
    {
        if (_updatingTemplatePreset) return;
        if (_templatePresetCombo.SelectedIndex == 0) return;
        _updatingTemplatePreset = true;
        _templatePresetCombo.SelectedIndex = 0;
        _updatingTemplatePreset = false;
    }

    private void ApplyTemplatePreset(int index)
    {
        if (_updatingTemplatePreset || index <= 0) return;
        _updatingTemplatePreset = true;
        switch (index)
        {
            case 1:
                _musicFormatBox.Text = "{song}{artistPart}{progress}";
                _programFormatBox.Text = "{app}";
                _combinedFormatBox.Text = "{app} {sep} {song}{artistPart}";
                break;
            case 2:
                _musicFormatBox.Text = Loc.L("正在听：{song}{artistPart}", "Listening to: {song}{artistPart}");
                _programFormatBox.Text = Loc.L("正在玩：{app}", "Playing: {app}");
                _combinedFormatBox.Text = Loc.L("正在玩：{app} {sep} 正在听：{song}{artistPart}", "Playing: {app} {sep} Listening to: {song}{artistPart}");
                break;
            case 3:
                _musicFormatBox.Text = "{song}";
                _programFormatBox.Text = "{app}";
                _combinedFormatBox.Text = "{app} {sep} {song}";
                break;
        }
        _updatingTemplatePreset = false;
        UpdatePreview();
    }

    private void SyncPresetFromFormats()
    {
        _updatingTemplatePreset = true;
        var index = 0;
        if (_musicFormatBox.Text == "{song}{artistPart}{progress}" &&
            _programFormatBox.Text == "{app}" &&
            _combinedFormatBox.Text == "{app} {sep} {song}{artistPart}")
        {
            index = 1;
        }
        else if (_musicFormatBox.Text == Loc.L("正在听：{song}{artistPart}", "Listening to: {song}{artistPart}") &&
                 _programFormatBox.Text == Loc.L("正在玩：{app}", "Playing: {app}") &&
                 _combinedFormatBox.Text == Loc.L("正在玩：{app} {sep} 正在听：{song}{artistPart}", "Playing: {app} {sep} Listening to: {song}{artistPart}"))
        {
            index = 2;
        }
        else if (_musicFormatBox.Text == "{song}" && _programFormatBox.Text == "{app}" &&
                 _combinedFormatBox.Text == "{app} {sep} {song}")
        {
            index = 3;
        }
        _templatePresetCombo.SelectedIndex = index;
        _updatingTemplatePreset = false;
    }

    /// <summary>预览：正在播放时用用户真实的歌；否则用默认示例（鳥の詩）。</summary>
    private void UpdatePreview()
    {
        if (_previewLabel == null) return;
        PlayerInfo dummySong;
        var isLive = false;
        if (Program.GetRpcManager()?.GetCurrentPlayerInfo() is { PlayerInfo: { } live })
        {
            dummySong = live;
            isLive = true;
        }
        else
        {
            dummySong = new PlayerInfo
            {
                Identity = "preview",
                Title = Loc.L("鳥の詩", "Sunflower"),
                Artists = Loc.L("Lia", "Post Malone"),
                Album = Loc.L("Air", "Spider-Man: Into the Spider-Verse"),
                Cover = "",
                Schedule = 151,
                Duration = 366,
                Url = "",
                Pause = false
            };
        }
        var (barFill, barEmpty) = BarStyleParser.Parse(_progressBarStyleCombo.Text);
        var previewConfig = new ConfigData
        {
            MusicFormat = NonEmpty(_musicFormatBox.Text, "{song}{artistPart}{progress}"),
            ProgramFormat = NonEmpty(_programFormatBox.Text, "{app}"),
            CombinedFormat = NonEmpty(_combinedFormatBox.Text, "{app} {sep} {song}{artistPart}"),
            CombinedSeparator = NonEmpty(_separatorCombo.Text, "‖"),
            HideMusicWhenPaused = false,
            ProgressBarFillChar = barFill,
            ProgressBarEmptyChar = barEmpty,
            ProgressBarLength = (int)_barLengthBox.Value
        };
        var combined = SteamStatusManager.ComposeStatus(dummySong, Loc.L("卡拉彼丘", "Strinova"), previewConfig) ?? Loc.L("(无)", "(None)");
        var musicOnly = SteamStatusManager.ComposeStatus(dummySong, null, previewConfig) ?? Loc.L("(无)", "(None)");
        var source = isLive ? Loc.L("（以下使用你当前正在播放的内容）", "(using what you're playing right now)") : Loc.L("（示例内容）", "(sample content)");
        _previewLabel.Text = Loc.L($"预览{source}{Environment.NewLine}程序+音乐：{combined}{Environment.NewLine}仅音乐：{musicOnly}", $"Preview{source}{Environment.NewLine}App + music: {combined}{Environment.NewLine}Music only: {musicOnly}");
    }

    // ================= 加载 / 保存 =================
    private void LoadSettings()
    {
        var settings = Configurations.Instance.Settings;

        var isAutoStartEnabled = Win32Api.AutoStart.Check();
        _autoStartCheckBox.Checked = isAutoStartEnabled;
        settings.AutoStart = isAutoStartEnabled;
        _closeToTrayCheckBox.Checked = settings.CloseToTray;
        _startInTrayCheckBox.Checked = settings.StartInTray;
        _allowWebSocketFallbackCheckBox.Checked = settings.AllowWebSocketFallback;
        _languageCombo.SelectedIndex = Math.Clamp(settings.Language, 0, 1);

        _enableSteamSyncCheckBox.Checked = settings.EnableSteamSync;
        _musicSyncCheckBox.Checked = settings.MusicSyncEnabled;
        _hidePausedMusicCheckBox.Checked = settings.HideMusicWhenPaused;
        _browserMediaCheckBox.Checked = settings.BrowserMediaSyncEnabled;
        // 数据源：清掉旧行再按配置重建（对话框可能被重复打开）
        foreach (var row in _dataSourceRows.ToArray())
        {
            _dataSourcesPanel.Controls.Remove(row.Container);
            row.Container.Dispose();
        }
        _dataSourceRows.Clear();
        foreach (var source in settings.DataSources ?? [])
        {
            AddDataSourceRow(source);
        }
        _enableAppSyncCheckBox.Checked = settings.AppSyncEnabled;
        _syncNonGameCheckBox.Checked = settings.SyncNonGameApps;
        _pauseWhenPlayingGameCheckBox.Checked = settings.PauseWhenPlayingGame;

        _musicFormatBox.Text = settings.MusicFormat;
        _programFormatBox.Text = settings.ProgramFormat;
        _combinedFormatBox.Text = settings.CombinedFormat;
        _separatorCombo.Text = settings.CombinedSeparator;
        _progressBarStyleCombo.Text = settings.ProgressBarFillChar + settings.ProgressBarEmptyChar;

        // 外观
        _titleFollowCheck.Checked = settings.AppearanceTitleFollowPlayer;
        _songFollowCheck.Checked = settings.AppearanceSongFollowPlayer;
        _titleColorButton.BackColor = settings.AppearanceTitleColorArgb is int titleArgb
            ? Color.FromArgb(titleArgb)
            : Color.WhiteSmoke;
        _songColorButton.BackColor = settings.AppearanceSongColorArgb is int songArgb
            ? Color.FromArgb(songArgb)
            : Color.WhiteSmoke;
        _backgroundColorButton.BackColor = settings.AppearanceBackgroundColorArgb is int backgroundArgb
            ? Color.FromArgb(backgroundArgb)
            : Color.WhiteSmoke;
        _appearanceFontFamily = settings.AppearanceFontFamily ?? "";
        _appearanceFontSize = settings.AppearanceFontSize;
        _fontButton.Text = string.IsNullOrWhiteSpace(_appearanceFontFamily)
            ? (_appearanceFontSize > 0 ? Loc.L($"（默认字体）{_appearanceFontSize:0.#}pt", $"(Default font) {_appearanceFontSize:0.#}pt") : Loc.L("微软雅黑 9pt", "Microsoft YaHei 9pt"))
            : $"{_appearanceFontFamily} {(_appearanceFontSize > 0 ? _appearanceFontSize : 9f):0.#}pt";
        _backgroundImagePath = settings.AppearanceBackgroundImage ?? "";
        _backgroundCropRect = settings.BackgroundCropRect;
        UpdateBackgroundImageLabel();
        _backgroundLayoutCombo.SelectedIndex = settings.AppearanceBackgroundLayout switch
        {
            "Zoom" => 1,
            "Tile" => 2,
            "Center" => 3,
            _ => 0
        };
        UpdateAppearanceEnabled();
        _persistentStatusCheck.Checked = settings.PersistentStatusEnabled;
        _persistentStatusBox.Text = settings.PersistentStatusText ?? "";
        _persistentStatusBox.Enabled = _persistentStatusCheck.Checked;
        _barLengthBox.Value = settings.ProgressBarLength is >= 1 and <= 50 ? settings.ProgressBarLength : 10;
        _syncSpeedCombo.SelectedIndex = settings.SyncSpeed switch
        {
            SyncSpeedLevel.Fast => 0,
            SyncSpeedLevel.Economic => 2,
            _ => 1
        };

        // ⚠️ 先快照再枚举：轮询线程在后台会自动发现新程序并 config.Apps.Add，
        // 而 List<T> 在枚举期间被改写会抛 InvalidOperationException（版本号变化）——
        // 这里没有 try/catch，异常会冒到全局兜底弹"未处理错误"框。
        foreach (var rule in settings.Apps.ToArray())
        {
            _rules.Add(CloneRule(rule));
        }
        RefreshRulesGrid();
        UpdatePreview();
        SyncPresetFromFormats();
        LoadPriorityOrder();
        UpdateAccountStatus();
        RefreshMemoryInfo();
        var versionText = $"v{UpdateChecker.GetCurrentVersionText()}";
        _versionLabel.Text = Loc.L($"MuSync {versionText} ｜ 配置文件：{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\MuSync", $"MuSync {versionText} | Config file: {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\MuSync");
        _aboutVersionLabel.Text = Loc.L($"版本：MuSync {versionText}", $"Version: MuSync {versionText}");
    }

    private void SaveSettings()
    {
        var settings = Configurations.Instance.Settings;
        var isAutoStartChecked = _autoStartCheckBox.Checked;
        settings.AutoStart = isAutoStartChecked;
        settings.CloseToTray = _closeToTrayCheckBox.Checked;
        settings.StartInTray = _startInTrayCheckBox.Checked;
        settings.Language = Math.Clamp(_languageCombo.SelectedIndex, 0, 1);
        settings.AllowWebSocketFallback = _allowWebSocketFallbackCheckBox.Checked;

        settings.EnableSteamSync = _enableSteamSyncCheckBox.Checked;
        settings.MusicSyncEnabled = _musicSyncCheckBox.Checked;
        settings.HideMusicWhenPaused = _hidePausedMusicCheckBox.Checked;
        settings.BrowserMediaSyncEnabled = _browserMediaCheckBox.Checked;
        // 数据源按界面上的填写回写（名称重复/含大括号的条目会被 CollectDataSources 过滤掉）
        settings.DataSources = CollectDataSources();
        settings.AppSyncEnabled = _enableAppSyncCheckBox.Checked;
        settings.SyncNonGameApps = _syncNonGameCheckBox.Checked;
        settings.PauseWhenPlayingGame = _pauseWhenPlayingGameCheckBox.Checked;

        settings.MusicFormat = NonEmpty(_musicFormatBox.Text, "{song}{artistPart}{progress}");
        settings.ProgramFormat = NonEmpty(_programFormatBox.Text, "{app}");
        settings.CombinedFormat = NonEmpty(_combinedFormatBox.Text, "{app} {sep} {song}{artistPart}");
        settings.CombinedSeparator = NonEmpty(_separatorCombo.Text, "‖");
        var (barFill, barEmpty) = BarStyleParser.Parse(_progressBarStyleCombo.Text);
        settings.ProgressBarFillChar = barFill;
        settings.ProgressBarEmptyChar = barEmpty;
        settings.ProgressBarLength = (int)_barLengthBox.Value;

        // 外观
        settings.AppearanceTitleFollowPlayer = _titleFollowCheck.Checked;
        settings.AppearanceTitleColorArgb = _titleFollowCheck.Checked ? null : _titleColorButton.BackColor.ToArgb();
        settings.AppearanceSongFollowPlayer = _songFollowCheck.Checked;
        settings.AppearanceSongColorArgb = _songFollowCheck.Checked ? null : _songColorButton.BackColor.ToArgb();
        settings.AppearanceBackgroundColorArgb = _backgroundColorButton.BackColor.ToArgb();
        settings.AppearanceFontFamily = _appearanceFontFamily;
        settings.AppearanceFontSize = _appearanceFontSize;
        settings.AppearanceBackgroundImage = _backgroundImagePath;
        settings.BackgroundCropRect = _backgroundCropRect;
        settings.AppearanceBackgroundLayout = _backgroundLayoutCombo.SelectedIndex switch
        {
            1 => "Zoom",
            2 => "Tile",
            3 => "Center",
            _ => "Stretch"
        };

        // 常驻状态
        settings.PersistentStatusEnabled = _persistentStatusCheck.Checked;
        settings.PersistentStatusText = _persistentStatusBox.Text.Trim();
        settings.SyncSpeed = _syncSpeedCombo.SelectedIndex switch
        {
            0 => SyncSpeedLevel.Fast,
            2 => SyncSpeedLevel.Economic,
            _ => SyncSpeedLevel.Standard
        };
        settings.PlayerPriority = CurrentPriorityOrder();
        MergeBackgroundDiscoveredRules(settings);

        Configurations.Instance.Save();
        Program.GetRpcManager()?.RequestStateRefresh();

        if (isAutoStartChecked != Win32Api.AutoStart.Check())
        {
            var success = Win32Api.AutoStart.Set(isAutoStartChecked);
            if (!success)
            {
                MessageBox.Show(
                    Loc.L($"无法 {(isAutoStartChecked ? "设置" : "取消")} 开机自启。\n请尝试以管理员权限运行本程序一次。", $"Couldn't {(isAutoStartChecked ? "enable" : "disable")} start with Windows.\nTry running the app as administrator once."),
                    Loc.L("操作失败", "Operation failed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    /// <summary>
    /// 把当前规则写回配置。
    /// <para>
    /// ⚠️ 不能直接 <c>settings.Apps = _rules</c>：
    /// <c>_rules</c> 是**对话框打开那一刻的克隆**，而主循环在对话框开着的时候会继续自动发现程序
    /// （前台切到未收录的程序就写进 <c>settings.Apps</c>）。整体替换等于把期间发现的新程序
    /// 无声丢掉 —— 用户操作是"打开设置 → 切几个程序 → 点确定"，回来就发现那几条不见了。
    /// </para>
    /// <para>
    /// 合并口径：以对话框里的列表为准（用户可能删过、改过、导入过），
    /// 再把"对话框没显示过、但活列表里已经有的"补进去。这样既不丢后台发现，也不复活用户删掉的条目。
    /// 仍然存在的已知不足：对话框打开期间后台新发现的行不会实时出现在表格里（需要关掉重开）。
    /// </para>
    /// </summary>
    private void MergeBackgroundDiscoveredRules(ConfigData settings)
    {
        // 先取快照：轮询线程可能正在向 settings.Apps 里 Add 新发现的程序
        var (merged, kept) = MergeDiscoveredRules(_rules, settings.Apps.ToArray());
        settings.Apps.Clear();
        settings.Apps.AddRange(merged);
        if (kept.Count > 0)
        {
            Logger.Info($"[AppSync] 保存设置时保留对话框打开期间自动发现的 {kept.Count} 个程序：" +
                        string.Join("、", kept.Select(r => r.ExeName)));
        }
    }

    /// <summary>
    /// 合并"对话框里的规则"与"活配置里的规则"，返回合并结果与被保留下来的后台新发现条目。
    /// 纯函数（不碰界面、不碰单例、**也不修改入参**），便于单测 ——
    /// 上面那个真 bug 正是缺了这层可测性才漏出去的。
    /// </summary>
    /// <param name="liveRulesSnapshot">
    /// 活配置规则的**快照**（调用方先 ToArray()）。
    /// ⚠️ 不要直接把 settings.Apps 传进来：轮询线程在后台会向它 Add，
    /// 枚举期间被改写会抛 InvalidOperationException；而这里若再对它 Clear/AddRange，
    /// 更是"一边枚举一边改写"，保存设置时会让用户刚改的东西直接丢掉。
    /// </param>
    internal static (List<AppRule> Merged, List<AppRule> Discovered) MergeDiscoveredRules(
        IReadOnlyList<AppRule> dialogRules, IReadOnlyList<AppRule> liveRulesSnapshot)
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in dialogRules)
        {
            shown.Add(rule.ExeName);
        }
        var discovered = new List<AppRule>();
        foreach (var live in liveRulesSnapshot)
        {
            if (shown.Contains(live.ExeName)) continue;
            discovered.Add(live);
        }
        // 以对话框里的列表为准（用户可能删过、改过、导入过），再把后台新发现的补在后面
        var merged = new List<AppRule>(dialogRules.Count + discovered.Count);
        merged.AddRange(dialogRules);
        merged.AddRange(discovered);
        return (merged, discovered);
    }

    // ================= 账户 =================
    private void UpdateAccountStatus()
    {
        if (_accountStatusLabel == null) return;
        var session = Program.GetSessionManager();
        if (session?.IsLoggedOn == true)
        {
            _accountStatusLabel.Text = Loc.L("已登录 Steam（音乐与程序状态同步中）", "Signed in to Steam (syncing music & app status)");
            _accountStatusLabel.ForeColor = Color.Green;
        }
        else
        {
            _accountStatusLabel.Text = Loc.L("未登录 Steam", "Not signed in to Steam");
            _accountStatusLabel.ForeColor = Color.OrangeRed;
        }
    }

    private void LogoutButton_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            Loc.L("确定要退出登录吗？\n退出后需要重新打开程序并输入 Steam 账号密码。", "Sign out?\nYou'll need to reopen the app and enter your Steam account and password again."),
            Loc.L("退出登录", "Sign out"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) return;
        var settings = Configurations.Instance.Settings;
        settings.SteamUsername = "";
        settings.SteamRefreshToken = "";
        settings.SteamGuardData = "";
        Configurations.Instance.Save();
        Application.Exit();
    }

    private void LoginButton_Click(object? sender, EventArgs e)
    {
        var session = Program.GetSessionManager();
        if (session == null) return;
        using var loginForm = new SteamLoginForm(session);
        loginForm.StartPosition = FormStartPosition.CenterParent;
        loginForm.ShowDialog(this);
        if (!loginForm.LoginSucceeded)
        {
            UpdateAccountStatus();
            return;
        }
        MessageBox.Show(Loc.L("Steam 登录成功，状态同步已恢复。", "Signed in to Steam — status sync has resumed."), Loc.L("提示", "Notice"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        Program.GetRpcManager()?.RequestStateRefresh();
        UpdateAccountStatus();
    }

    // ================= 诊断 =================
    private void RefreshMemoryInfo()
    {
        if (_memoryInfoLabel == null) return;
        try
        {
            var memoryInfo = PerformanceMonitor.GetMemoryInfo();
            var cacheStats = PerformanceMonitor.GetCacheStatistics();
            _memoryInfoLabel.Text = Loc.L(
                $"""
                工作集: {memoryInfo.GetFormattedWorkingSet()}, 私有: {memoryInfo.GetFormattedPrivateMemory()}, 虚拟: {memoryInfo.GetFormattedVirtualMemory()}
                GC托管: {memoryInfo.GetFormattedGcMemory()}
                图片缓存: {cacheStats.ImageCacheCount} 项 | 模块缓存: {cacheStats.ModuleCacheCount + cacheStats.ProcessModuleCacheCount} 项
                更新于: {memoryInfo.Timestamp:HH:mm:ss}
                """,
                $"""
                Working set: {memoryInfo.GetFormattedWorkingSet()}, Private: {memoryInfo.GetFormattedPrivateMemory()}, Virtual: {memoryInfo.GetFormattedVirtualMemory()}
                GC heap: {memoryInfo.GetFormattedGcMemory()}
                Image cache: {cacheStats.ImageCacheCount} | Module cache: {cacheStats.ModuleCacheCount + cacheStats.ProcessModuleCacheCount}
                Updated: {memoryInfo.Timestamp:HH:mm:ss}
                """);
            _memoryInfoLabel.ForeColor = Color.Black;
        }
        catch (Exception ex)
        {
            _memoryInfoLabel.Text = Loc.L($"获取内存信息失败: {ex.Message}", $"Failed to get memory info: {ex.Message}");
            _memoryInfoLabel.ForeColor = Color.Red;
        }
    }

    /// <summary>把诊断信息（版本 / 运行状态 / 关键设置 / 路径）复制到剪贴板。</summary>
    private static bool CopyDiagnosticsInfo()
    {
        try
        {
            Clipboard.SetText(DiagnosticsReport.Build());
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.L($"复制失败：{ex.Message}", $"Copy failed: {ex.Message}"), Loc.L("提示", "Notice"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static void OpenLogsFolder()
    {
        try
        {
            var logsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MuSync", "logs");
            System.IO.Directory.CreateDirectory(logsPath);
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.L($"打开日志文件夹失败：{ex.Message}", $"Failed to open the logs folder: {ex.Message}"), Loc.L("提示", "Notice"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 小工具 =================
    private static string NonEmpty(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CategoryToText(AppCategory category) => category switch
    {
        AppCategory.Ignore => Loc.L("忽略", "Ignore"),
        AppCategory.Game => Loc.L("游戏", "Game"),
        AppCategory.Work => Loc.L("工作", "Work"),
        AppCategory.Media => Loc.L("媒体", "Media"),
        AppCategory.Social => Loc.L("社交", "Social"),
        _ => Loc.L("其他", "Other")
    };

    private static AppCategory CategoryFromText(string? text)
    {
        if (text == Loc.L("忽略", "Ignore")) return AppCategory.Ignore;
        if (text == Loc.L("游戏", "Game")) return AppCategory.Game;
        if (text == Loc.L("工作", "Work")) return AppCategory.Work;
        if (text == Loc.L("媒体", "Media")) return AppCategory.Media;
        if (text == Loc.L("社交", "Social")) return AppCategory.Social;
        return AppCategory.Other;
    }

    private static string OverrideToText(AppSyncOverride value) => value switch
    {
        AppSyncOverride.ForceOn => Loc.L("强制显示", "Always show"),
        AppSyncOverride.ForceOff => Loc.L("强制隐藏", "Always hide"),
        _ => Loc.L("跟随分类", "Follow category")
    };

    private static AppSyncOverride OverrideFromText(string? text)
    {
        if (text == Loc.L("强制显示", "Always show")) return AppSyncOverride.ForceOn;
        if (text == Loc.L("强制隐藏", "Always hide")) return AppSyncOverride.ForceOff;
        return AppSyncOverride.FollowCategory;
    }

    private static AppRule CloneRule(AppRule source) => new()
    {
        ExeName = source.ExeName,
        DisplayName = source.DisplayName,
        Category = source.Category,
        Mode = source.Mode,
        Override = source.Override,
        Enabled = source.Enabled,
        IsUserConfirmed = source.IsUserConfirmed
    };
}
