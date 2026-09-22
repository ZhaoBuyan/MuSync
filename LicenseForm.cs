using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using MuSync.Utils;
namespace MuSync;
/// <summary>开源许可窗口：内嵌展示 MuSync 自身许可（MIT）、第三方组件说明与 LGPL-2.1 全文。</summary>
internal class LicenseForm : Form
{
    /// <summary>读取内嵌许可文本（逻辑名见 MuSync.csproj 的 EmbeddedResource 三项）。</summary>
    private static string ReadEmbedded(string logicalName)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);
            if (stream == null) return "";
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // Win32 编辑框按 \r\n 断行：仓库里的 .md / .txt 是 LF，统一成 CRLF 再显示
            return reader.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n").Trim();
        }
        catch
        {
            return "";
        }
    }

    public LicenseForm()
    {
        Text = Loc.L("开源许可", "Open source licenses");
        Size = new Size(740, 580);
        MinimumSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei", 9f);
        Icon = AppResource.Icon;

        var content = new StringBuilder();
        content.AppendLine(Loc.L("=== 一、MuSync 本体许可（MIT）===", "=== 1. MuSync license (MIT) ==="));
        content.AppendLine();
        content.AppendLine(ReadEmbedded("MuSync.Licenses.LICENSE"));
        content.AppendLine();
        content.AppendLine(Loc.L("=== 二、第三方组件与出处（THIRD-PARTY-NOTICES.md）===", "=== 2. Third-party notices (THIRD-PARTY-NOTICES.md) ==="));
        content.AppendLine();
        content.AppendLine(ReadEmbedded("MuSync.Licenses.THIRD-PARTY-NOTICES"));
        content.AppendLine();
        content.AppendLine(Loc.L("=== 三、GNU LGPL 2.1 全文（SteamKit2 使用）===", "=== 3. GNU LGPL 2.1 full text (used by SteamKit2) ==="));
        content.AppendLine();
        content.AppendLine(ReadEmbedded("MuSync.Licenses.LGPL-2.1"));

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            Font = new Font("Microsoft YaHei", 8.5f),
            Location = new Point(12, 12),
            Size = new Size(ClientSize.Width - 24, ClientSize.Height - 70),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = content.ToString()
        };
        textBox.SelectionStart = 0;
        textBox.SelectionLength = 0;

        var closeButton = new Button
        {
            Text = Loc.L("关闭", "Close"),
            Size = new Size(90, 28),
            BackColor = Color.White,
            DialogResult = DialogResult.Cancel,
            Location = new Point(ClientSize.Width - 102, ClientSize.Height - 46),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        Controls.AddRange([textBox, closeButton]);
        CancelButton = closeButton;
    }
}
