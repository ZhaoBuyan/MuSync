using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;

namespace MuSync;

internal sealed partial class SettingsView
{
    private Panel _programLibraryPanel = null!;
    private TextBox _programLibrarySearch = null!;
    private ComboBox _programLibraryCategory = null!;
    private DataGridView _programLibraryGrid = null!;
    private Label _programLibraryStatus = null!;
    private Button _programLibraryAdd = null!;

    private void CreateProgramLibrary(TabPage page)
    {
        _programLibraryPanel = new Panel
        {
            Name = "ProgramLibrary", Location = new Point(16, 16), Size = new Size(650, 560), Visible = false
        };
        var back = new CueButton
        {
            Text = Loc.L("返回规则", "Back to rules"), Location = Point.Empty, Size = new Size(144, 32)
        };
        back.Click += (_, _) => ShowProgramLibrary(page, false);
        var heading = new Label
        {
            Text = Loc.L("程序库", "App library"), Font = Theme.Section,
            Location = new Point(0, 52), Size = new Size(650, 32)
        };
        var hint = new Label
        {
            Text = Loc.L("搜索内置程序，或选择本机 EXE。添加后保存设置才会生效。",
                "Search built-in apps or choose a local EXE. Save settings to apply your additions."),
            Location = new Point(0, 92), Size = new Size(650, 48), ForeColor = Theme.Muted
        };
        var searchLabel = new Label
        {
            Text = Loc.L("搜索", "Search"), Location = new Point(0, 156), Size = new Size(60, 28)
        };
        _programLibrarySearch = new TextBox
        {
            Name = "ProgramLibrarySearch", PlaceholderText = Loc.L("搜索名称或 EXE 文件名", "Search by name or EXE filename"),
            Location = new Point(64, 152), Size = new Size(366, 32),
            AccessibleName = Loc.L("搜索程序", "Search apps")
        };
        _programLibraryCategory = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(446, 152), Size = new Size(204, 32),
            AccessibleName = Loc.L("程序分类", "App category")
        };
        _programLibraryCategory.Items.Add(Loc.L("全部分类", "All categories"));
        foreach (var category in new[] { AppCategory.Game, AppCategory.Work, AppCategory.Media, AppCategory.Social, AppCategory.Other })
            _programLibraryCategory.Items.Add(CategoryToText(category));
        _programLibraryCategory.SelectedIndex = 0;
        _programLibraryGrid = new DataGridView
        {
            Name = "ProgramLibraryGrid", Location = new Point(0, 204), Size = new Size(650, 256),
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Theme.Surface,
            BorderStyle = BorderStyle.None, ColumnHeadersHeight = 32, RowTemplate = { Height = 28 }
        };
        _programLibraryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("名称", "Name"), FillWeight = 42 });
        _programLibraryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("程序", "App"), FillWeight = 40 });
        _programLibraryGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = Loc.L("分类", "Category"), FillWeight = 18 });
        foreach (DataGridViewColumn column in _programLibraryGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        _programLibraryAdd = new CueButton
        {
            Text = Loc.L("添加所选程序", "Add selected app"), Location = new Point(0, 476), Size = new Size(202, 36)
        };
        var browse = new CueButton
        {
            Text = Loc.L("选择本机 EXE…", "Choose local EXE…"), Location = new Point(218, 476), Size = new Size(202, 36)
        };
        _programLibraryStatus = new Label
        {
            Location = new Point(0, 528), Size = new Size(650, 32), ForeColor = Theme.Muted
        };
        _programLibraryAdd.Click += (_, _) => AddSelectedLibraryEntry();
        browse.Click += (_, _) => BrowseLibraryExecutable();
        _programLibrarySearch.TextChanged += (_, _) => RefreshProgramLibrary();
        _programLibraryCategory.SelectedIndexChanged += (_, _) => RefreshProgramLibrary();
        _programLibraryGrid.SelectionChanged += (_, _) => UpdateLibraryAddButton();
        _programLibraryPanel.Controls.AddRange([back, heading, hint, searchLabel, _programLibrarySearch, _programLibraryCategory,
            _programLibraryGrid, _programLibraryAdd, browse, _programLibraryStatus]);
        page.Controls.Add(_programLibraryPanel);
    }

    private void ShowProgramLibrary(TabPage page, bool show)
    {
        page.SuspendLayout();
        try
        {
            page.AutoScrollPosition = Point.Empty;
            foreach (Control control in page.Controls)
                control.Visible = control == _programLibraryPanel ? show : !show;
            if (show)
            {
                RefreshProgramLibrary();
                _programLibraryPanel.BringToFront();
                _programLibrarySearch.Focus();
            }
        }
        finally { page.ResumeLayout(true); }
    }

    private void RefreshProgramLibrary()
    {
        AppCategory? category = _programLibraryCategory.SelectedIndex > 0
            ? CategoryFromText(_programLibraryCategory.Text) : null;
        _programLibraryGrid.Rows.Clear();
        foreach (var entry in ProgramLibrary.Search(_programLibrarySearch.Text, category))
        {
            var index = _programLibraryGrid.Rows.Add(entry.Name, entry.ExeName, CategoryToText(entry.Category));
            _programLibraryGrid.Rows[index].Tag = entry;
        }
        var count = _programLibraryGrid.Rows.Count;
        if (count > 0) _programLibraryGrid.CurrentCell = _programLibraryGrid.Rows[0].Cells[0];
        _programLibraryStatus.Text = count == 0
            ? Loc.L("没有匹配结果。可以选择本机 EXE 添加自定义程序。", "No matches. Choose a local EXE to add a custom app.")
            : Loc.L($"找到 {count} 个程序。已有规则不会被覆盖。",
                count == 1 ? "1 app found. Existing rules will be kept." : $"{count} apps found. Existing rules will be kept.");
        UpdateLibraryAddButton();
    }

    private void UpdateLibraryAddButton()
    {
        if (_programLibraryAdd is null) return;
        var entry = _programLibraryGrid.CurrentRow?.Tag as ProgramLibrary.Entry;
        var exists = entry is not null && _rules.Any(rule => rule.ExeName.Equals(entry.ExeName, StringComparison.OrdinalIgnoreCase));
        _programLibraryAdd.Enabled = entry is not null && !exists;
        _programLibraryAdd.Text = exists ? Loc.L("已在规则中", "Already in rules") : Loc.L("添加所选程序", "Add selected app");
    }

    private void AddSelectedLibraryEntry()
    {
        if (_programLibraryGrid.CurrentRow?.Tag is ProgramLibrary.Entry entry) AddLibraryEntry(entry);
    }

    private void AddLibraryEntry(ProgramLibrary.Entry entry)
    {
        var added = ProgramLibrary.TryAdd(_rules, entry);
        if (added)
        {
            _removedRuleNames.Remove(entry.ExeName);
            RefreshRulesGrid();
        }
        _programLibraryStatus.Text = added
            ? Loc.L("已加入规则草稿。请保存设置以生效。", "Added to your draft rules. Save settings to apply.")
            : Loc.L("未添加：程序已在规则中，或由音乐同步及系统忽略名单处理。",
                "Not added: already in rules, or handled by music sync or the system ignore list.");
        UpdateLibraryAddButton();
    }

    private void BrowseLibraryExecutable()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Loc.L("选择程序", "Choose an app"), Filter = Loc.L("程序 (*.exe)|*.exe", "Applications (*.exe)|*.exe"),
            CheckFileExists = true, Multiselect = false, RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var exeName = Path.GetFileName(dialog.FileName);
        var (category, name, _) = AppClassifier.Suggest(exeName, "", false, dialog.FileName);
        AddLibraryEntry(new ProgramLibrary.Entry(exeName, name, category));
    }
}
