using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
namespace MuSync;

/// <summary>
/// 程序同步设置：全局开关、显示模板（含分隔符）、程序规则列表（分类/显示名/模式/策略）、AI 配置。
/// </summary>
internal sealed class AppSyncSettingsForm : Form
{
    private CheckBox _enableAppSyncCheckBox = null!;
    private CheckBox _syncNonGameCheckBox = null!;
    private CheckBox _musicSyncCheckBox = null!;
    private CheckBox _hidePausedMusicCheckBox = null!;
    private TextBox _musicFormatBox = null!;
    private TextBox _programFormatBox = null!;
    private TextBox _combinedFormatBox = null!;
    private ComboBox _separatorCombo = null!;
    private Label _previewLabel = null!;
    private DataGridView _rulesGrid = null!;
    private TextBox _aiEndpointBox = null!;
    private TextBox _aiKeyBox = null!;
    private TextBox _aiModelBox = null!;
    private readonly List<AppRule> _rules = [];

    private static readonly string[] SeparatorPresets = ["‖", " | ", " · ", " — ", " ~ ", " + "];

    public AppSyncSettingsForm()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "程序同步设置 - MuSync";
        Size = new Size(660, 780);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;

        // ---- 同步开关 ----
        var switchGroup = new GroupBox
        {
            Text = "同步开关",
            Location = new Point(12, 8),
            Size = new Size(620, 78),
            BackColor = Color.White
        };
        _enableAppSyncCheckBox = new CheckBox
        {
            Text = "启用程序同步",
            Location = new Point(15, 22),
            AutoSize = true,
            BackColor = Color.White
        };
        _syncNonGameCheckBox = new CheckBox
        {
            Text = "同步非游戏应用（关闭时仅游戏类同步）",
            Location = new Point(200, 22),
            AutoSize = true,
            BackColor = Color.White
        };
        _musicSyncCheckBox = new CheckBox
        {
            Text = "启用音乐同步",
            Location = new Point(15, 48),
            AutoSize = true,
            BackColor = Color.White
        };
        _hidePausedMusicCheckBox = new CheckBox
        {
            Text = "音乐暂停时不在状态中显示",
            Location = new Point(200, 48),
            AutoSize = true,
            BackColor = Color.White
        };
        switchGroup.Controls.AddRange([_enableAppSyncCheckBox, _syncNonGameCheckBox,
            _musicSyncCheckBox, _hidePausedMusicCheckBox]);

        // ---- 显示模板 ----
        var templateGroup = new GroupBox
        {
            Text = "显示模板（变量：{app} {song} {artist} {artistPart} {progress} {sep}）",
            Location = new Point(12, 92),
            Size = new Size(620, 168),
            BackColor = Color.White
        };
        var musicFormatLabel = new Label { Text = "音乐格式:", Location = new Point(15, 28), AutoSize = true };
        _musicFormatBox = new TextBox
        {
            Location = new Point(120, 24),
            Width = 485
        };
        var programFormatLabel = new Label { Text = "程序格式:", Location = new Point(15, 58), AutoSize = true };
        _programFormatBox = new TextBox
        {
            Location = new Point(120, 54),
            Width = 485
        };
        var combinedFormatLabel = new Label { Text = "组合格式:", Location = new Point(15, 88), AutoSize = true };
        _combinedFormatBox = new TextBox
        {
            Location = new Point(120, 84),
            Width = 360
        };
        var separatorLabel = new Label { Text = "分隔符:", Location = new Point(490, 88), AutoSize = true };
        _separatorCombo = new ComboBox
        {
            Location = new Point(545, 84),
            Width = 60,
            DropDownStyle = ComboBoxStyle.DropDown
        };
        _separatorCombo.Items.AddRange(SeparatorPresets);
        _previewLabel = new Label
        {
            Text = "预览：",
            Location = new Point(15, 122),
            Size = new Size(590, 36),
            BackColor = Color.FromArgb(245, 245, 245),
            ForeColor = Color.FromArgb(50, 50, 50),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(5, 2, 5, 2)
        };
        _musicFormatBox.TextChanged += (_, _) => UpdatePreview();
        _programFormatBox.TextChanged += (_, _) => UpdatePreview();
        _combinedFormatBox.TextChanged += (_, _) => UpdatePreview();
        _separatorCombo.TextChanged += (_, _) => UpdatePreview();
        templateGroup.Controls.AddRange([musicFormatLabel, _musicFormatBox, programFormatLabel, _programFormatBox,
            combinedFormatLabel, _combinedFormatBox, separatorLabel, _separatorCombo, _previewLabel]);

        // ---- 程序列表 ----
        var rulesGroup = new GroupBox
        {
            Text = "程序列表（淡黄色 = AI 建议，改动任意项即视为确认）",
            Location = new Point(12, 266),
            Size = new Size(620, 300),
            BackColor = Color.White
        };
        BuildRulesGrid();
        var addCurrentButton = new Button
        {
            Text = "添加当前前台程序",
            Location = new Point(12, 266),
            Size = new Size(140, 26),
            BackColor = Color.White
        };
        addCurrentButton.Click += AddCurrentButton_Click;
        var removeButton = new Button
        {
            Text = "删除选中",
            Location = new Point(162, 266),
            Size = new Size(90, 26),
            BackColor = Color.White
        };
        removeButton.Click += RemoveButton_Click;
        var restoreFormatButton = new Button
        {
            Text = "恢复默认模板",
            Location = new Point(490, 266),
            Size = new Size(118, 26),
            BackColor = Color.White
        };
        restoreFormatButton.Click += (_, _) =>
        {
            _musicFormatBox.Text = "{song}{artistPart}{progress}";
            _programFormatBox.Text = "{app}";
            _combinedFormatBox.Text = "{app} {sep} {song}{artistPart}";
            _separatorCombo.Text = "‖";
        };
        rulesGroup.Controls.AddRange([_rulesGrid, addCurrentButton, removeButton, restoreFormatButton]);

        // ---- AI 配置 ----
        var aiGroup = new GroupBox
        {
            Text = "AI 辅助分类（可选：填写 API 后可获得更聪明的程序识别；不填则使用本地规则）",
            Location = new Point(12, 572),
            Size = new Size(620, 122),
            BackColor = Color.White
        };
        var endpointLabel = new Label { Text = "API 地址:", Location = new Point(15, 26), AutoSize = true };
        _aiEndpointBox = new TextBox
        {
            Location = new Point(100, 22),
            Width = 505,
            PlaceholderText = "例如 https://api.deepseek.com/v1（OpenAI 兼容接口）"
        };
        var keyLabel = new Label { Text = "API Key:", Location = new Point(15, 56), AutoSize = true };
        _aiKeyBox = new TextBox
        {
            Location = new Point(100, 52),
            Width = 505,
            UseSystemPasswordChar = true
        };
        var modelLabel = new Label { Text = "模型:", Location = new Point(15, 86), AutoSize = true };
        _aiModelBox = new TextBox
        {
            Location = new Point(100, 82),
            Width = 220,
            PlaceholderText = "例如 deepseek-chat"
        };
        aiGroup.Controls.AddRange([endpointLabel, _aiEndpointBox, keyLabel, _aiKeyBox, modelLabel, _aiModelBox]);

        // ---- 底部按钮 ----
        var okButton = new Button
        {
            Text = "确定",
            Location = new Point(436, 706),
            Size = new Size(80, 28),
            DialogResult = DialogResult.OK,
            BackColor = Color.White
        };
        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(526, 706),
            Size = new Size(80, 28),
            DialogResult = DialogResult.Cancel,
            BackColor = Color.White
        };
        okButton.Click += (_, _) => SaveSettings();
        Controls.AddRange([switchGroup, templateGroup, rulesGroup, aiGroup, okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void BuildRulesGrid()
    {
        _rulesGrid = new DataGridView
        {
            Location = new Point(12, 20),
            Size = new Size(596, 238),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };
        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "启用", FillWeight = 8 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "程序", ReadOnly = true, FillWeight = 19 });
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "显示名", FillWeight = 22 });
        var categoryColumn = new DataGridViewComboBoxColumn { HeaderText = "分类", FillWeight = 13, FlatStyle = FlatStyle.Flat };
        categoryColumn.Items.AddRange("忽略", "游戏", "工作", "媒体", "社交", "其他");
        _rulesGrid.Columns.Add(categoryColumn);
        var modeColumn = new DataGridViewComboBoxColumn { HeaderText = "模式", FillWeight = 15, FlatStyle = FlatStyle.Flat };
        modeColumn.Items.AddRange("前台时显示", "运行即显示");
        _rulesGrid.Columns.Add(modeColumn);
        var overrideColumn = new DataGridViewComboBoxColumn { HeaderText = "策略", FillWeight = 14, FlatStyle = FlatStyle.Flat };
        overrideColumn.Items.AddRange("跟随分类", "强制显示", "强制隐藏");
        _rulesGrid.Columns.Add(overrideColumn);
        _rulesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", ReadOnly = true, FillWeight = 9 });
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
                rule.Mode = row.Cells[4].Value?.ToString() == "运行即显示"
                    ? AppSyncMode.Always
                    : AppSyncMode.Foreground;
                break;
            case 5:
                rule.Override = OverrideFromText(row.Cells[5].Value?.ToString());
                break;
            default:
                return;
        }
        if (e.ColumnIndex is 0 or 2 or 3 or 4 or 5)
        {
            rule.IsUserConfirmed = true;
            ApplyRowStyle(row, rule);
        }
    }

    private void AddCurrentButton_Click(object? sender, EventArgs e)
    {
        var foreground = ForegroundWatcher.GetCurrent();
        if (foreground == null)
        {
            MessageBox.Show("未检测到前台程序。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_rules.Any(r => r.ExeName.Equals(foreground.ExeName, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"{foreground.ExeName} 已在列表中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private void LoadSettings()
    {
        var settings = Configurations.Instance.Settings;
        _enableAppSyncCheckBox.Checked = settings.AppSyncEnabled;
        _syncNonGameCheckBox.Checked = settings.SyncNonGameApps;
        _musicSyncCheckBox.Checked = settings.MusicSyncEnabled;
        _hidePausedMusicCheckBox.Checked = settings.HideMusicWhenPaused;
        _musicFormatBox.Text = settings.MusicFormat;
        _programFormatBox.Text = settings.ProgramFormat;
        _combinedFormatBox.Text = settings.CombinedFormat;
        _separatorCombo.Text = settings.CombinedSeparator;
        _aiEndpointBox.Text = settings.AiApiEndpoint;
        _aiKeyBox.Text = settings.AiApiKey;
        _aiModelBox.Text = settings.AiApiModel;
        foreach (var rule in settings.Apps)
        {
            _rules.Add(CloneRule(rule));
        }
        RefreshRulesGrid();
        UpdatePreview();
    }

    private void SaveSettings()
    {
        var settings = Configurations.Instance.Settings;
        settings.AppSyncEnabled = _enableAppSyncCheckBox.Checked;
        settings.SyncNonGameApps = _syncNonGameCheckBox.Checked;
        settings.MusicSyncEnabled = _musicSyncCheckBox.Checked;
        settings.HideMusicWhenPaused = _hidePausedMusicCheckBox.Checked;
        settings.MusicFormat = NonEmpty(_musicFormatBox.Text, "{song}{artistPart}{progress}");
        settings.ProgramFormat = NonEmpty(_programFormatBox.Text, "{app}");
        settings.CombinedFormat = NonEmpty(_combinedFormatBox.Text, "{app} {sep} {song}{artistPart}");
        settings.CombinedSeparator = NonEmpty(_separatorCombo.Text, "‖");
        settings.AiApiEndpoint = _aiEndpointBox.Text.Trim();
        settings.AiApiKey = _aiKeyBox.Text.Trim();
        settings.AiApiModel = _aiModelBox.Text.Trim();
        settings.Apps = _rules;
        Configurations.Instance.Save();
        Program.GetRpcManager()?.RequestStateRefresh();
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
                rule.Mode == AppSyncMode.Always ? "运行即显示" : "前台时显示",
                OverrideToText(rule.Override),
                rule.IsUserConfirmed ? "已确认" : "AI 建议");
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
        row.Cells[6].Value = rule.IsUserConfirmed ? "已确认" : "AI 建议";
    }

    private void UpdatePreview()
    {
        var dummySong = new PlayerInfo
        {
            Identity = "preview",
            Title = "稻香",
            Artists = "周杰伦",
            Album = "",
            Cover = "",
            Schedule = 150,
            Duration = 255,
            Url = "",
            Pause = false
        };
        var previewConfig = new ConfigData
        {
            MusicFormat = NonEmpty(_musicFormatBox.Text, "{song}{artistPart}{progress}"),
            ProgramFormat = NonEmpty(_programFormatBox.Text, "{app}"),
            CombinedFormat = NonEmpty(_combinedFormatBox.Text, "{app} {sep} {song}{artistPart}"),
            CombinedSeparator = NonEmpty(_separatorCombo.Text, "‖"),
            HideMusicWhenPaused = false
        };
        var combined = SteamStatusManager.ComposeStatus(dummySong, "卡拉彼丘", previewConfig) ?? "(无)";
        var musicOnly = SteamStatusManager.ComposeStatus(dummySong, null, previewConfig) ?? "(无)";
        _previewLabel.Text = $"程序+音乐：{combined}{Environment.NewLine}仅音乐：{musicOnly}";
    }

    private static string NonEmpty(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string CategoryToText(AppCategory category) => category switch
    {
        AppCategory.Ignore => "忽略",
        AppCategory.Game => "游戏",
        AppCategory.Work => "工作",
        AppCategory.Media => "媒体",
        AppCategory.Social => "社交",
        _ => "其他"
    };

    private static AppCategory CategoryFromText(string? text) => text switch
    {
        "忽略" => AppCategory.Ignore,
        "游戏" => AppCategory.Game,
        "工作" => AppCategory.Work,
        "媒体" => AppCategory.Media,
        "社交" => AppCategory.Social,
        _ => AppCategory.Other
    };

    private static string OverrideToText(AppSyncOverride value) => value switch
    {
        AppSyncOverride.ForceOn => "强制显示",
        AppSyncOverride.ForceOff => "强制隐藏",
        _ => "跟随分类"
    };

    private static AppSyncOverride OverrideFromText(string? text) => text switch
    {
        "强制显示" => AppSyncOverride.ForceOn,
        "强制隐藏" => AppSyncOverride.ForceOff,
        _ => AppSyncOverride.FollowCategory
    };

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
