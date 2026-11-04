using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MuSync.Utils;

/// <summary>雾白与石墨蓝主题。字体由应用共享，控件不得单独释放这些字体。</summary>
internal static class Theme
{
    private static ThemePalette? _palette;
    internal static ThemePalette Palette => _palette ??= ThemePalettes.Resolve(Configurations.Instance.Settings.UiTheme);
    internal const TextFormatFlags PaintText = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
    internal static readonly Size WindowSize = new(880, 640);
    internal static void BeginForm(Form form)
    {
        form.SuspendLayout();
        form.HandleCreated += (_, _) => ApplyWindowFrame(form);
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }
    internal static void EndForm(Form form)
    {
        form.ResumeLayout(true);
        form.PerformAutoScale();
    }

    internal static Size FitWindowClientSize(Form form)
    {
        var scale = form.DeviceDpi / 96f;
        var area = Screen.FromControl(form).WorkingArea;
        var frame = form.Size - form.ClientSize;
        return new Size(
            Math.Min((int)Math.Round(WindowSize.Width * scale), Math.Max(320, area.Width - frame.Width - 32)),
            Math.Min((int)Math.Round(WindowSize.Height * scale), Math.Max(320, area.Height - frame.Height - 32)));
    }
    internal static void PolishFields(Control root)
    {
        foreach (Control child in root.Controls.Cast<Control>().ToArray())
        {
            if (child is TextBox { Multiline: false } field && field.Parent is not CueFieldHost)
                _ = new CueFieldHost(field);
            // UpDownBase 自己管理内部编辑框和箭头的位置，不能再次包装其 TextBox。
            else if (child is not (CueFieldHost or UpDownBase)) PolishFields(child);
        }
    }

    internal static Color Paper => Palette.Paper;
    internal static Color Surface => Palette.Surface;
    internal static Color Inset => Palette.Inset;
    internal static Color Canvas => Palette.Canvas;
    internal static Color Ink => Palette.Ink;
    internal static Color Muted => Palette.Muted;
    internal static Color Rule => Palette.Rule;
    internal static Color Accent => Palette.Accent;
    internal static Color Sidebar => Palette.Sidebar;
    internal static readonly Color SidebarText = ColorTranslator.FromHtml("#E4ECF0");
    internal static Color SidebarMuted => Palette.SidebarMuted;
    internal static Color SidebarRule => Palette.SidebarRule;
    internal static Color SidebarSelected => Palette.SidebarSelected;
    internal static Color OnAir => Palette.Error;
    internal static Color Warn => Palette.Warning;
    // ---- 字体：一律用系统字体栈，不内置字体文件 ----
    //
    // 取舍（2026-11-04 定）：设计稿原本要 Archivo + IBM Plex Mono，但主界面的控件位置是按
    // 当前字体度量手工调过的（含 144 DPI 实测）。换字体族会让每处文本宽度变化，需要连带重调
    // 手工坐标与 AutoSize 边界；而且这两族都不含中日韩字形，中文界面会按字形回退到系统 CJK 字体，
    // 中英混排反而出现两种字重。收益只是"看起来更特别"，代价是布局回归 —— 按"先不出 bug"的优先级放弃。
    //
    // 若要更换字体族：先在 100/125/150/200% DPI 量一遍所有 AutoSize=false 控件与手工坐标的边界，
    // 并做一次全量布局复核；不要只换字体然后指望 PerformAutoScale 兜住。
    // 用户可在「设置 → 显示 → 外观字体」覆盖字体族与字号（见 MainForm.ApplyAppearanceFont）。
    internal static readonly Font Body = new("Segoe UI", 9.5f);
    internal static readonly Font Heading = new("Segoe UI", 16f, FontStyle.Bold);
    internal static readonly Font Section = new("Segoe UI", 12f, FontStyle.Bold);
    internal static readonly Font Data = new("Consolas", 9f);
    internal static readonly Font Output = new("Consolas", 11f);
    internal static readonly Font Caption = new("Segoe UI", 8.25f);

    static Theme()
    {
        // 低噪音启动记录：写明实际生效的字体栈，便于排查字形异常或自定义字体不生效时对照
        Logger.Info($"[UI] 字体：{Body.FontFamily.Name} / {Data.FontFamily.Name}（系统字体栈，可在设置里更换）");
    }

    internal static void SwitchPalette(string? id, Control root)
        => PreviewPalette(ThemePalettes.Resolve(id), root);

    /// <summary>本地原型验证入口；应用的主题选择与配置只通过 SwitchPalette/Resolve。</summary>
    internal static void PreviewPalette(ThemePalette next, Control root)
    {
        var previous = Palette;
        if (previous.Id == next.Id) return;
        Color[] Colors(ThemePalette p) => [p.Paper, p.Inset, p.Canvas, p.Ink, p.Muted, p.Rule,
            p.Accent, p.Sidebar, p.SidebarMuted, p.SidebarRule, p.SidebarSelected, p.Surface, p.Error, p.Warning];
        var oldColors = Colors(previous);
        var newColors = Colors(next);
        Color Map(Color color)
        {
            for (var i = 0; i < oldColors.Length; i++)
                if (color.ToArgb() == oldColors[i].ToArgb()) return newColors[i];
            return color;
        }
        void Recolor(Control control)
        {
            if (control.Name is not ("TitleColorSwatch" or "SongColorSwatch" or "BackgroundColorSwatch"))
                control.BackColor = Map(control.BackColor);
            control.ForeColor = Map(control.ForeColor);
            if (control is TextBoxBase or UpDownBase or ComboBox)
                control.ForeColor = control.Enabled ? Ink : Muted;
            if (control is ButtonBase button)
            {
                button.FlatAppearance.BorderColor = Map(button.FlatAppearance.BorderColor);
                button.FlatAppearance.MouseOverBackColor = Map(button.FlatAppearance.MouseOverBackColor);
                button.FlatAppearance.MouseDownBackColor = Map(button.FlatAppearance.MouseDownBackColor);
                button.FlatAppearance.CheckedBackColor = Map(button.FlatAppearance.CheckedBackColor);
            }
            if (control is LinkLabel link)
            {
                link.LinkColor = Map(link.LinkColor);
                link.ActiveLinkColor = Map(link.ActiveLinkColor);
                link.VisitedLinkColor = Map(link.VisitedLinkColor);
            }
            if (control is DataGridView grid)
            {
                grid.BackgroundColor = Map(grid.BackgroundColor);
                grid.GridColor = Map(grid.GridColor);
                void RecolorStyle(DataGridViewCellStyle style)
                {
                    style.BackColor = Map(style.BackColor); style.ForeColor = Map(style.ForeColor);
                    style.SelectionBackColor = Map(style.SelectionBackColor); style.SelectionForeColor = Map(style.SelectionForeColor);
                }
                foreach (var style in new[] { grid.DefaultCellStyle, grid.ColumnHeadersDefaultCellStyle,
                    grid.AlternatingRowsDefaultCellStyle, grid.RowsDefaultCellStyle, grid.RowHeadersDefaultCellStyle }) RecolorStyle(style);
                foreach (DataGridViewColumn column in grid.Columns) RecolorStyle(column.DefaultCellStyle);
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.HasDefaultCellStyle) RecolorStyle(row.DefaultCellStyle);
                    foreach (DataGridViewCell cell in row.Cells) if (cell.HasStyle) RecolorStyle(cell.Style);
                }
            }
            foreach (Control child in control.Controls) Recolor(child);
            control.Invalidate();
        }
        using var layout = new WindowUpdateScope(root);
        _palette = next;
        Recolor(root);
        if (root is Form form) ApplyWindowFrame(form);
        Logger.Info($"[UI] theme={next.Id}");
    }

    internal static void ApplyWindowFrame(Form form)
    {
        if (!form.IsHandleCreated) return;
        UiHealth.TryRun("window-frame", () =>
        {
            var dark = Palette.IsDark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
        });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void Apply(Control root)
    {
        if (root is ScrollableControl { AutoScroll: true } scrollable) ThemeScrollbars.Attach(scrollable);
        root.BackColor = Paper;
        // 保留调用方指定的状态语义及用户颜色，基础主题不能把错误/说明文字全部染黑。
        if (root.ForeColor.IsSystemColor || root.ForeColor.ToArgb() == Color.Black.ToArgb()) root.ForeColor = Ink;
        if (root is TextBoxBase or UpDownBase or ComboBox) root.ForeColor = root.Enabled ? Ink : Muted;
        if (root is ScrollBar scrollbar) ThemeScrollbars.Attach(scrollbar);
        if (root is DataGridView scrollGrid) ThemeScrollbars.Attach(scrollGrid);
        foreach (Control child in root.Controls)
        {
            Apply(child);
            if (child is TextBoxBase or NumericUpDown or ComboBox)
                child.BackColor = Surface;
            if (child is TextBoxBase field)
            {
                field.Font = Data;
                field.BorderStyle = BorderStyle.FixedSingle;
            }
            if (child is ListBox list)
            {
                list.BorderStyle = BorderStyle.None;
                if (list.Name != "SettingsNavigation") list.BackColor = Surface;
            }
            if (child is NumericUpDown number) number.BorderStyle = BorderStyle.FixedSingle;
            if (child is LinkLabel link)
            {
                link.LinkColor = Accent;
                link.ActiveLinkColor = Sidebar;
                link.VisitedLinkColor = Accent;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
            }
            if (child is Button button)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = button.ForeColor == OnAir ? OnAir : Rule;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.MouseOverBackColor = Inset;
                button.FlatAppearance.MouseDownBackColor = Rule;
            }
            if (child is DataGridView grid)
            {
                grid.BackgroundColor = Paper;
                grid.GridColor = Rule;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Canvas;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Canvas;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
                grid.DefaultCellStyle.BackColor = Paper;
                grid.DefaultCellStyle.ForeColor = Ink;
                grid.DefaultCellStyle.SelectionBackColor = Inset;
                grid.DefaultCellStyle.SelectionForeColor = Ink;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Paper;
                grid.RowTemplate.Height = 28;
                grid.BorderStyle = BorderStyle.None;
                grid.ColumnHeadersDefaultCellStyle.Font = Data;
            }
        }
    }

    internal static void Primary(Button button)
    {
        void UpdateColors()
        {
            button.BackColor = button.Enabled ? Accent : Inset;
            button.ForeColor = button.Enabled ? Paper : Muted;
            button.FlatAppearance.BorderColor = button.Enabled ? Accent : Rule;
            button.FlatAppearance.MouseOverBackColor = SidebarSelected;
            button.FlatAppearance.MouseDownBackColor = Sidebar;
        }
        button.EnabledChanged += (_, _) => UpdateColors();
        UpdateColors();
    }
}
