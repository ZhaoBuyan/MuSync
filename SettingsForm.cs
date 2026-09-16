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
    private CheckBox _enableAppSyncCheckBox = null!;
    private CheckBox _syncNonGameCheckBox = null!;
    private CheckBox _pauseWhenPlayingGameCheckBox = null!;
    private ComboBox _syncSpeedCombo = null!;
    private readonly List<ComboBox> _playerPriorityCombos = [];
    private bool _updatingPriorityCombos;
    private List<int> _lastPrioritySelection = [0, 1, 2];

    // —— 显示页 ——
    private TextBox _musicFormatBox = null!;
    private TextBox _programFormatBox = null!;
    private TextBox _combinedFormatBox = null!;
    private ComboBox _separatorCombo = null!;
    private ComboBox _progressBarStyleCombo = null!;
    private NumericUpDown _barLengthBox = null!;
    private ComboBox _templatePresetCombo = null!;
    private Label _previewLabel = null!;

    // 外观设置控件
    private CheckBox _titleFollowCheck = null!;
    private Button _titleColorButton = null!;
    private CheckBox _songFollowCheck = null!;
    private Button _songColorButton = null!;
    private Button _fontButton = null!;
    private Button _backgroundColorButton = null!;
    private Label _backgroundImageLabel = null!;
    private ComboBox _backgroundLayoutCombo = null!;
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

    private static readonly string[] PlayerOrderKeys = ["NetEase", "Tencent", "LxMusic", "KuGou"];

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
        _enableAppSyncCheckBox = CreateCheckBox(Loc.L("启用程序同步", "Enable app sync"), 340, 32);
        _syncNonGameCheckBox = CreateCheckBox(Loc.L("同步非游戏应用", "Sync non-game apps"), 340, 66);
        _pauseWhenPlayingGameCheckBox = CreateCheckBox(Loc.L("玩真实 Steam 游戏时自动暂停", "Auto-pause while playing a real Steam game"), 340, 100);
        switchGroup.Controls.AddRange([
            _enableSteamSyncCheckBox, _musicSyncCheckBox, _hidePausedMusicCheckBox,
            _enableAppSyncCheckBox, _syncNonGameCheckBox, _pauseWhenPlayingGameCheckBox
        ]);

        var priorityGroup = CreateGroupBox(Loc.L("音乐播放器优先级", "Music player priority"), 10, 182, 650, 100);
        var priorityLabel = new Label
        {
            AutoSize = true,
            Location = new Point(20, 32),
            Text = Loc.L("优先级从左到右（正在播放的始终优先）：", "Priority goes left to right (now-playing always wins):")
        };
        priorityGroup.Controls.Add(priorityLabel);
        for (var i = 0; i < 4; i++)
        {
            var combo = new ComboBox
            {
                Location = new Point(20 + i * 155, 58),
                Width = 145,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            combo.Items.AddRange([Loc.PlayerName("网易云音乐"), Loc.PlayerName("QQ音乐"), Loc.PlayerName("洛雪音乐"), Loc.PlayerName("酷狗音乐")]);
            combo.SelectedIndexChanged += (_, _) => ApplyPriorityFromCombos();
            _playerPriorityCombos.Add(combo);
            priorityGroup.Controls.Add(combo);
        }

        var speedGroup = CreateGroupBox(Loc.L("同步频率", "Sync frequency"), 10, 294, 650, 90);
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
            Location = new Point(350, 166),
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _progressBarStyleCombo.Items.AddRange(["#-", "█░", "▰▱", "●○", "■□", "▮▯"]);
        var barLengthLabel = new Label { Text = Loc.L("长度:", "Length:"), Location = new Point(525, 170), AutoSize = true };
        _barLengthBox = new NumericUpDown
        {
            Location = new Point(568, 166),
            Width = 72,
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
            Text = "变量：{app} 程序名｜{song} 歌名｜{artist} 歌手｜{artistPart} 自动连接符的歌手｜{progress} 进度｜{sep} 分隔符"
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

        var appearanceGroup = CreateAppearanceGroup();
        page.AutoScroll = true;
        page.Controls.AddRange([templateGroup, appearanceGroup]);
        return page;
    }

    /// <summary>「外观」分组：自定义主界面的颜色 / 字体 / 背景（显示页内，可滚动查看）。</summary>
    private GroupBox CreateAppearanceGroup()
    {
        var group = CreateGroupBox(Loc.L("外观", "Appearance"), 10, 350, 650, 196);

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
            _backgroundImageLabel.Text = Loc.L("（无）", "(None)");
            _backgroundImageLabel.ForeColor = Color.Gray;
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
            Filter = Loc.L("图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*", "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*")
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _backgroundImagePath = dialog.FileName;
            _backgroundImageLabel.Text = Path.GetFileName(dialog.FileName);
            _backgroundImageLabel.ForeColor = Color.Black;
        }
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
        _backgroundImageLabel.Text = Loc.L("（无）", "(None)");
        _backgroundImageLabel.ForeColor = Color.Gray;
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

        page.Controls.AddRange([_rulesGrid, addCurrentButton, removeButton]);
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

        page.Controls.AddRange([title, subtitle, _aboutVersionLabel, repoButton, releaseButton, checkUpdateButton, _updateNoticeLabel, licenseLabel]);
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

    // ================= 控件工具 =================
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
            foreground.ExeName, foreground.WindowTitle, foreground.IsFullscreen);
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
                rule.IsUserConfirmed ? Loc.L("已确认", "Confirmed") : Loc.L("AI 建议", "AI suggestion"));
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
        row.Cells[6].Value = rule.IsUserConfirmed ? Loc.L("已确认", "Confirmed") : Loc.L("AI 建议", "AI suggestion");
    }

    // ================= 播放器优先级 =================
    private void LoadPriorityCombos()
    {
        _updatingPriorityCombos = true;
        var order = (Configurations.Instance.Settings.PlayerPriority ?? []).Concat(PlayerOrderKeys).Distinct().Take(4).ToList();
        for (var i = 0; i < _playerPriorityCombos.Count; i++)
        {
            var index = Array.IndexOf(PlayerOrderKeys, order[i]);
            _playerPriorityCombos[i].SelectedIndex = index >= 0 ? index : i;
        }
        _lastPrioritySelection = _playerPriorityCombos.Select(c => c.SelectedIndex).ToList();
        _updatingPriorityCombos = false;
    }

    private void ApplyPriorityFromCombos()
    {
        if (_updatingPriorityCombos || _playerPriorityCombos.Count == 0) return;
        _updatingPriorityCombos = true;
        for (var i = 0; i < _playerPriorityCombos.Count; i++)
        {
            var current = _playerPriorityCombos[i].SelectedIndex;
            if (current < 0) continue;
            for (var j = 0; j < i; j++)
            {
                if (_playerPriorityCombos[j].SelectedIndex == current)
                {
                    _playerPriorityCombos[j].SelectedIndex = _lastPrioritySelection[i];
                    break;
                }
            }
        }
        _lastPrioritySelection = _playerPriorityCombos.Select(c => c.SelectedIndex).ToList();
        _updatingPriorityCombos = false;
    }

    private List<string> CurrentPriorityOrder()
    {
        var order = new List<string>();
        foreach (var combo in _playerPriorityCombos)
        {
            var index = combo.SelectedIndex;
            if (index >= 0 && index < PlayerOrderKeys.Length && !order.Contains(PlayerOrderKeys[index]))
            {
                order.Add(PlayerOrderKeys[index]);
            }
        }
        foreach (var key in PlayerOrderKeys)
        {
            if (!order.Contains(key)) order.Add(key);
        }
        return order;
    }

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
        if (!string.IsNullOrWhiteSpace(_backgroundImagePath))
        {
            _backgroundImageLabel.Text = Path.GetFileName(_backgroundImagePath);
            _backgroundImageLabel.ForeColor = Color.Black;
        }
        _backgroundLayoutCombo.SelectedIndex = settings.AppearanceBackgroundLayout switch
        {
            "Zoom" => 1,
            "Tile" => 2,
            "Center" => 3,
            _ => 0
        };
        UpdateAppearanceEnabled();
        _barLengthBox.Value = settings.ProgressBarLength is >= 1 and <= 50 ? settings.ProgressBarLength : 10;
        _syncSpeedCombo.SelectedIndex = settings.SyncSpeed switch
        {
            SyncSpeedLevel.Fast => 0,
            SyncSpeedLevel.Economic => 2,
            _ => 1
        };

        foreach (var rule in settings.Apps)
        {
            _rules.Add(CloneRule(rule));
        }
        RefreshRulesGrid();
        UpdatePreview();
        SyncPresetFromFormats();
        LoadPriorityCombos();
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
        settings.AppearanceBackgroundLayout = _backgroundLayoutCombo.SelectedIndex switch
        {
            1 => "Zoom",
            2 => "Tile",
            3 => "Center",
            _ => "Stretch"
        };
        settings.SyncSpeed = _syncSpeedCombo.SelectedIndex switch
        {
            0 => SyncSpeedLevel.Fast,
            2 => SyncSpeedLevel.Economic,
            _ => SyncSpeedLevel.Standard
        };
        settings.PlayerPriority = CurrentPriorityOrder();
        settings.Apps = _rules;

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
