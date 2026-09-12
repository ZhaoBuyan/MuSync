using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
namespace MuSync;

internal enum TemplateKind
{
    Music,
    Program,
    Combined
}

/// <summary>
/// 模板积木编辑器：自由添加 / 排序 / 删除块，每块可选择类型
/// （歌名、歌手、进度条、程序名、分隔符、自定义文字），实时预览好友可见效果。
/// </summary>
internal sealed class FormatBlockEditorForm : Form
{
    private static readonly string[] TypeNames =
    [
        "歌名", "歌手", "歌手（无连接符）", "进度条", "程序名", "分隔符", "自定义文字"
    ];

    private static readonly FormatBlockType[] TypeValues =
    [
        FormatBlockType.Song, FormatBlockType.ArtistPart, FormatBlockType.Artist,
        FormatBlockType.Progress, FormatBlockType.App, FormatBlockType.Sep, FormatBlockType.Custom
    ];

    private readonly TemplateKind _kind;
    private readonly string _separator;
    private readonly List<FormatBlock> _blocks;
    private Panel _rowsPanel = null!;
    private Label _previewLabel = null!;

    public string ResultFormat { get; private set; } = "";

    public FormatBlockEditorForm(TemplateKind kind, string initialFormat, string separator)
    {
        _kind = kind;
        _separator = separator;
        _blocks = FormatBlockCodec.Parse(initialFormat);
        InitializeComponent();
        RebuildRows();
        UpdatePreview();
    }

    private void InitializeComponent()
    {
        Text = _kind switch
        {
            TemplateKind.Music => "编辑「音乐」模板 — 积木编辑",
            TemplateKind.Program => "编辑「程序」模板 — 积木编辑",
            _ => "编辑「组合」模板 — 积木编辑"
        };
        Size = new Size(560, 448);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei", 9);

        _rowsPanel = new Panel
        {
            Location = new Point(12, 12),
            Size = new Size(524, 290),
            AutoScroll = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var addButton = new Button
        {
            Text = "＋ 添加积木",
            Location = new Point(12, 310),
            Size = new Size(110, 28),
            BackColor = Color.White
        };
        addButton.Click += (_, _) =>
        {
            _blocks.Add(new FormatBlock { Type = FormatBlockType.Custom });
            RebuildRows();
            UpdatePreview();
        };

        _previewLabel = new Label
        {
            Location = new Point(12, 346),
            Size = new Size(524, 40),
            ForeColor = Color.FromArgb(0, 102, 51)
        };

        var okButton = new Button
        {
            Text = "确定",
            Location = new Point(400, 390),
            Size = new Size(64, 28),
            BackColor = Color.White
        };
        okButton.Click += (_, _) =>
        {
            ResultFormat = FormatBlockCodec.Compile(_blocks);
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(472, 390),
            Size = new Size(64, 28),
            BackColor = Color.White
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange(_rowsPanel, addButton, _previewLabel, okButton, cancelButton);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }

    private void RebuildRows()
    {
        _rowsPanel.SuspendLayout();
        _rowsPanel.Controls.Clear();
        var y = 6;
        for (var i = 0; i < _blocks.Count; i++)
        {
            var index = i;
            var block = _blocks[i];

            var typeCombo = new ComboBox
            {
                Location = new Point(8, y),
                Width = 135,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            typeCombo.Items.AddRange(TypeNames);
            typeCombo.SelectedIndex = Array.IndexOf(TypeValues, block.Type);
            if (typeCombo.SelectedIndex < 0)
            {
                typeCombo.SelectedIndex = TypeValues.Length - 1;
            }
            typeCombo.SelectedIndexChanged += (_, _) =>
            {
                var newType = TypeValues[typeCombo.SelectedIndex];
                block.Type = newType;
                if (newType != FormatBlockType.Custom)
                {
                    block.Text = "";
                }
                RebuildRows();
                UpdatePreview();
            };

            var textBox = new TextBox
            {
                Location = new Point(148, y),
                Width = 232,
                Text = block.Text,
                Enabled = block.Type == FormatBlockType.Custom
            };
            textBox.TextChanged += (_, _) =>
            {
                block.Text = textBox.Text;
                UpdatePreview();
            };

            var upButton = new Button
            {
                Text = "↑",
                Location = new Point(386, y - 1),
                Size = new Size(30, 26),
                Enabled = i > 0
            };
            upButton.Click += (_, _) => MoveBlock(index, -1);

            var downButton = new Button
            {
                Text = "↓",
                Location = new Point(420, y - 1),
                Size = new Size(30, 26),
                Enabled = i < _blocks.Count - 1
            };
            downButton.Click += (_, _) => MoveBlock(index, +1);

            var delButton = new Button
            {
                Text = "✕",
                Location = new Point(454, y - 1),
                Size = new Size(30, 26)
            };
            delButton.Click += (_, _) =>
            {
                _blocks.RemoveAt(index);
                RebuildRows();
                UpdatePreview();
            };

            _rowsPanel.Controls.AddRange(typeCombo, textBox, upButton, downButton, delButton);
            y += 32;
        }
        _rowsPanel.ResumeLayout();
    }

    private void MoveBlock(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= _blocks.Count) return;
        (_blocks[index], _blocks[target]) = (_blocks[target], _blocks[index]);
        RebuildRows();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_previewLabel == null) return;
        var compiled = FormatBlockCodec.Compile(_blocks);
        var dummySong = new PlayerInfo
        {
            Identity = "preview",
            Title = "稻香",
            Artists = "周杰伦",
            Album = "魔杰座",
            Cover = "",
            Schedule = 150,
            Duration = 255,
            Url = "",
            Pause = false
        };
        var config = new ConfigData
        {
            CombinedSeparator = _separator,
            HideMusicWhenPaused = false
        };
        string? text;
        switch (_kind)
        {
            case TemplateKind.Music:
                config.MusicFormat = compiled;
                text = SteamStatusManager.ComposeStatus(dummySong, null, config);
                break;
            case TemplateKind.Program:
                config.ProgramFormat = compiled;
                text = SteamStatusManager.ComposeStatus(null, "卡拉彼丘", config);
                break;
            default:
                config.CombinedFormat = compiled;
                text = SteamStatusManager.ComposeStatus(dummySong, "卡拉彼丘", config);
                break;
        }
        _previewLabel.Text = $"好友看到：{text ?? "(无内容)"}";
    }
}
