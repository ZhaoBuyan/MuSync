using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MuSync.Utils;

namespace MuSync;

/// <summary>发现新版本提示窗：展示版本对比与更新日志，可前往下载。</summary>
internal sealed class UpdateForm : Form
{
    public UpdateForm(string currentVersion, UpdateChecker.UpdateInfo info)
    {
        Text = "发现新版本";
        Size = new Size(560, 490);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei", 9);
        BackColor = Color.White;

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(20, 18),
            Font = new Font("Microsoft YaHei", 12, FontStyle.Bold),
            Text = $"MuSync {info.Tag} 已发布"
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(22, 54),
            ForeColor = Color.Gray,
            Text = $"当前版本 v{currentVersion} → 最新版本 {info.Version}"
        };
        var logsBox = new TextBox
        {
            Location = new Point(20, 84),
            Size = new Size(505, 310),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 248, 248),
            Text = string.IsNullOrWhiteSpace(info.Body) ? "(本次更新暂无说明)" : info.Body
        };
        var downloadButton = new Button
        {
            Text = "前往下载",
            Location = new Point(300, 410),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        downloadButton.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(info.Url) { UseShellExecute = true });
            }
            catch
            {
                // 打开浏览器失败：留给用户手动访问
            }
            Close();
        };
        var laterButton = new Button
        {
            Text = "稍后",
            Location = new Point(420, 410),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        laterButton.Click += (_, _) => Close();

        Controls.AddRange([title, subtitle, logsBox, downloadButton, laterButton]);
        AcceptButton = downloadButton;
        CancelButton = laterButton;
    }
}
