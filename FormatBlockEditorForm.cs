using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
namespace MuSync;

/// <summary>积木编辑器编辑的模板种类：音乐 / 程序 / 组合。</summary>
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
    /// <summary>
    /// 积木类型下拉的显示文案。
    /// ⚠️ 必须每次现算，不能写成 static readonly 字段：那样会在类型初始化时就把当时的语言固化下来，
    /// 之后切语言（或测试里临时改语言）拿到的是过期文案。
    /// </summary>
    private static string[] TypeNames =>
    [
        Loc.L("歌名", "Song"), Loc.L("歌手", "Artist (auto separator)"),
        Loc.L("歌手（无连接符）", "Artist (no separator)"),
        Loc.L("进度条", "Progress bar"), Loc.L("时间轴", "Timeline"),
        Loc.L("程序名", "App name"),
        Loc.L("分隔符", "Separator"), Loc.L("自定义文字", "Custom text"),
        Loc.L("数据源", "Data source")
    ];

    private static readonly FormatBlockType[] TypeValues =
    [
        FormatBlockType.Song, FormatBlockType.ArtistPart, FormatBlockType.Artist,
        FormatBlockType.Bar, FormatBlockType.Time, FormatBlockType.App, FormatBlockType.Sep,
        FormatBlockType.Custom, FormatBlockType.DataSource
    ];

    private readonly TemplateKind _kind;
    private readonly string _separator;
    private readonly List<FormatBlock> _blocks;
    private Panel _rowsPanel = null!;
    private Label _previewLabel = null!;
    /// <summary>是否已排了一次延迟重建（合并连续请求）。</summary>
    private bool _rebuildScheduled;
    /// <summary>重建重入保护。</summary>
    private bool _rebuilding;

    /// <summary>
    /// 当前配置里的数据源名称（供"数据源"积木下拉选择）。
    /// 只用于**列出候选**：手打一个配置里没有的名字同样有效（渲染时展开为空），所以这不是白名单。
    /// </summary>
    private static List<string> DataSourceNames
    {
        get
        {
            var names = new List<string>();
            foreach (var source in Configurations.Instance.Settings.DataSources ?? [])
            {
                if (!string.IsNullOrWhiteSpace(source.Name) && !names.Contains(source.Name))
                {
                    names.Add(source.Name);
                }
            }
            return names;
        }
    }

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
            TemplateKind.Music => Loc.L("编辑「音乐」模板 — 积木编辑", "Edit \"Music\" template — Block editor"),
            TemplateKind.Program => Loc.L("编辑「程序」模板 — 积木编辑", "Edit \"App\" template — Block editor"),
            _ => Loc.L("编辑「组合」模板 — 积木编辑", "Edit \"Combined\" template — Block editor")
        };
        Size = new Size(560, 520);
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
            Text = Loc.L("＋ 添加积木", "+ Add block"),
            Location = new Point(12, 310),
            Size = new Size(110, 28),
            BackColor = Color.White
        };
        addButton.Click += (_, _) =>
        {
            _blocks.Add(new FormatBlock { Type = FormatBlockType.Custom });
            RequestRebuild();
        };

        _previewLabel = new Label
        {
            Location = new Point(12, 346),
            Size = new Size(524, 40),
            ForeColor = Color.FromArgb(0, 102, 51)
        };

        var okButton = new Button
        {
            Text = Loc.L("确定", "OK"),
            Location = new Point(400, 440),
            Size = new Size(64, 28),
            BackColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        okButton.Click += (_, _) =>
        {
            if (!ValidateDataSourceBlocks()) return;   // 名字不合格就不关窗，让用户改
            ResultFormat = FormatBlockCodec.Compile(_blocks);
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancelButton = new Button
        {
            Text = Loc.L("取消", "Cancel"),
            Location = new Point(472, 440),
            Size = new Size(64, 28),
            BackColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
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

    /// <summary>
    /// 请求重建积木行：**延迟到当前消息处理完之后**再执行。
    /// <para>
    /// ⚠️ 不能在这些控件自己的事件里同步重建：<see cref="RebuildRowsCore"/> 会 Dispose 掉所有行控件，
    /// 包括正在触发事件的那一个。销毁 HWND 时它的 WndProc 还在调用栈上，
    /// ComboBox 尤其容易卡住（症状就是"有时候卡死"）。
    /// </para>
    /// <para>
    /// 用 BeginInvoke 把重建推到消息循环的下一轮；再用 <see cref="_rebuildScheduled"/> 合并连续请求
    /// （连点上下移不会排出一长串重建）。
    /// </para>
    /// </summary>
    private void RequestRebuild()
    {
        if (_rebuildScheduled || IsDisposed || !IsHandleCreated) return;
        _rebuildScheduled = true;
        BeginInvoke(new Action(() =>
        {
            _rebuildScheduled = false;
            if (IsDisposed) return;
            RebuildRows();
            UpdatePreview();
        }));
    }

    private void RebuildRows()
    {
        if (_rebuilding) return;
        _rebuilding = true;
        try
        {
            RebuildRowsCore();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void RebuildRowsCore()
    {
        _rowsPanel.SuspendLayout();
        // ⚠️ Controls.Clear() 只解除父子关系、**不释放**控件：每行 5 个控件（含 HWND）会一直留到
        // GC 终结器回收。每次改类型 / 增删 / 上下移都会重建行，所以必须显式释放。
        var stale = new List<Control>();
        foreach (Control control in _rowsPanel.Controls)
        {
            stale.Add(control);
        }
        _rowsPanel.Controls.Clear();
        foreach (var control in stale)
        {
            control.Dispose();
        }
        var y = 6;
        for (var i = 0; i < _blocks.Count; i++)
        {
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
                if (newType == FormatBlockType.DataSource)
                {
                    // 切到"数据源"时给个默认项，避免留下空名字导致编译出 "{}"
                    if (string.IsNullOrWhiteSpace(block.Text) && DataSourceNames.Count > 0)
                    {
                        block.Text = DataSourceNames[0];
                    }
                }
                else if (newType != FormatBlockType.Custom)
                {
                    block.Text = "";
                }
                RequestRebuild();
            };

            // 数据源块用可编辑下拉：列出你已配好的数据源，也能手打（未被本机配置的名字同样有效）
            Control valueEditor;
            if (block.Type == FormatBlockType.DataSource)
            {
                var combo = new ComboBox
                {
                    Name = "BlockDataSourceBox",
                    Location = new Point(148, y),
                    Width = 232,
                    DropDownStyle = ComboBoxStyle.DropDown,
                    Text = block.Text
                };
                combo.Items.AddRange(DataSourceNames.ToArray());
                combo.TextChanged += (_, _) =>
                {
                    block.Text = combo.Text;
                    UpdatePreview();
                };
                valueEditor = combo;
            }
            else
            {
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
                valueEditor = textBox;
            }

            var upButton = new Button
            {
                Text = "↑",
                Location = new Point(386, y - 1),
                Size = new Size(30, 26),
                Enabled = i > 0
            };
            // ⚠️ 必须按"积木对象"定位，不能按 index：重建改成延迟执行后，
            // 连续点击时旧的 index 可能已经指到别的积木（会移错/删错行）。
            upButton.Click += (_, _) => MoveBlock(block, -1);

            var downButton = new Button
            {
                Text = "↓",
                Location = new Point(420, y - 1),
                Size = new Size(30, 26),
                Enabled = i < _blocks.Count - 1
            };
            downButton.Click += (_, _) => MoveBlock(block, +1);

            var delButton = new Button
            {
                Text = "✕",
                Location = new Point(454, y - 1),
                Size = new Size(30, 26)
            };
            delButton.Click += (_, _) =>
            {
                _blocks.Remove(block);
                RequestRebuild();
            };

            _rowsPanel.Controls.AddRange(typeCombo, valueEditor, upButton, downButton, delButton);
            y += 32;
        }
        _rowsPanel.ResumeLayout(true);
    }

    private void MoveBlock(FormatBlock block, int delta)
    {
        // 按对象找当前位置：延迟重建期间用户可能连点，index 会过期
        var index = _blocks.IndexOf(block);
        if (index < 0) return;
        var target = index + delta;
        if (target < 0 || target >= _blocks.Count) return;
        (_blocks[index], _blocks[target]) = (_blocks[target], _blocks[index]);
        RequestRebuild();
    }

    /// <summary>
    /// 确定前的校验：数据源积木的名字必须可用（非空、不含大括号、不超过长度上限）。
    /// <para>
    /// 名字空的积木编译出来是空串（不会污染状态），但用户显然是想加一块积木却没填名字，
    /// 直接关窗会让"点了确定但什么都没加上"变成一次无声失败，所以挡住并提示。
    /// </para>
    /// </summary>
    private bool ValidateDataSourceBlocks()
    {
        foreach (var block in _blocks)
        {
            if (block.Type != FormatBlockType.DataSource) continue;
            if (FormatBlockCodec.IsValidBlockName(block.Text)) continue;
            var message = Loc.L(
                $"数据源积木的名字不能用：不能为空、不能含大括号、不能超过 {SteamStatusManager.MaxBlockNameLength} 个字符。" + Environment.NewLine + "请改正后再点确定。",
                $"The data source block name is invalid: it must not be empty, contain braces, or exceed {SteamStatusManager.MaxBlockNameLength} characters." + Environment.NewLine + "Fix it and press OK again.");
            MessageBox.Show(this, message, Loc.L("积木名字不合法", "Invalid block name"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }


    private void UpdatePreview()
    {
        if (_previewLabel == null) return;
        var compiled = FormatBlockCodec.Compile(_blocks);
        var dummySong = new PlayerInfo
        {
            Identity = "preview",
            Title = Loc.L("稻香", "Sunflower"),
            Artists = Loc.L("周杰伦", "Post Malone"),
            Album = Loc.L("魔杰座", "Spider-Man: Into the Spider-Verse"),
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
        // 数据源积木在预览里给一个占位值：真实值要靠主循环去取，预览不该发网络请求。
        // 有实时值就用实时值（设置页预览时通常已经有了），没有就显示「‹名称›」让人看出位置。
        var previewBlocks = BuildPreviewBlocks();
        string? text;
        switch (_kind)
        {
            case TemplateKind.Music:
                config.MusicFormat = compiled;
                text = SteamStatusManager.ComposeStatus(dummySong, null, config, previewBlocks);
                break;
            case TemplateKind.Program:
                config.ProgramFormat = compiled;
                text = SteamStatusManager.ComposeStatus(null, Loc.L("卡拉彼丘", "Strinova"), config, previewBlocks);
                break;
            default:
                config.CombinedFormat = compiled;
                text = SteamStatusManager.ComposeStatus(dummySong, Loc.L("卡拉彼丘", "Strinova"), config, previewBlocks);
                break;
        }
        _previewLabel.Text = Loc.L($"好友看到：{text ?? "(无内容)"}", $"Friends see: {text ?? "(None)"}");
    }

    /// <summary>
    /// 预览用的积木取值：配置里有的数据源尽量用实时值，取不到就给「‹名称›」占位，
    /// 这样用户在编辑器里能看见这块积木在句子里的位置，而不是一片空白。
    /// </summary>
    private Dictionary<string, string> BuildPreviewBlocks()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var live = Program.GetRpcManager()?.TryGetDynamicBlocksSnapshot();
        foreach (var name in DataSourceNames)
        {
            var value = live != null && live.TryGetValue(name, out var v) ? v : null;
            result[name] = string.IsNullOrEmpty(value) ? $"‹{name}›" : value;
        }
        return result;
    }
}
