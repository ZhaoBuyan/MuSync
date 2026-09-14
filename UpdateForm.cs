using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Utils;

namespace MuSync;

/// <summary>
/// 发现新版本提示窗：展示版本对比与更新日志，可下载更新包（下载后手动替换）或前往发布页。
/// 下载采用「临时文件 + 校验转正」策略，任何失败都不会破坏已有文件。
/// </summary>
internal sealed class UpdateForm : Form
{
    private enum DownloadState
    {
        Idle,
        Downloading,
        Done
    }

    private readonly UpdateChecker.UpdateInfo _info;
    private readonly UpdateChecker.UpdateAsset? _asset;
    private readonly Button _actionButton;
    private readonly Button _browserButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _statusLabel;
    private string _packagePath = "";
    private CancellationTokenSource? _cts;
    private DownloadState _state = DownloadState.Idle;

    public UpdateForm(string currentVersion, UpdateChecker.UpdateInfo info)
    {
        _info = info;
        _asset = UpdateChecker.SelectAsset(info.Assets);

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
            Size = new Size(505, 246),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 248, 248),
            Text = string.IsNullOrWhiteSpace(info.Body) ? "(本次更新暂无说明)" : info.Body
        };
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 340),
            Size = new Size(505, 16),
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };
        _statusLabel = new Label
        {
            Location = new Point(20, 360),
            Size = new Size(505, 36),
            ForeColor = Color.Gray
        };
        _actionButton = new Button
        {
            Text = "下载更新",
            Location = new Point(190, 402),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        _actionButton.Click += ActionButton_Click;
        _browserButton = new Button
        {
            Text = "在浏览器中打开",
            Location = new Point(305, 402),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        _browserButton.Click += (_, _) => OpenInBrowser();
        var closeButton = new Button
        {
            Text = "稍后",
            Location = new Point(420, 402),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        closeButton.Click += (_, _) => Close();

        Controls.AddRange(
            [title, subtitle, logsBox, _progressBar, _statusLabel, _actionButton, _browserButton, closeButton]);
        AcceptButton = _actionButton;
        CancelButton = closeButton;
        FormClosing += (_, _) => _cts?.Cancel();

        if (_asset == null)
        {
            _actionButton.Enabled = false;
            SetStatus("该版本未提供可直接下载的文件，请点「在浏览器中打开」手动下载。", Color.DarkOrange);
        }
        else if (UpdateDownloader.IsAlreadyDownloaded(_asset, info.Version, out var existingPath))
        {
            _packagePath = existingPath;
            SetDoneState(alreadyExisted: true);
        }
    }

    private async void ActionButton_Click(object? sender, EventArgs e)
    {
        switch (_state)
        {
            case DownloadState.Idle:
                await StartDownloadAsync();
                break;
            case DownloadState.Downloading:
                _cts?.Cancel();
                break;
            case DownloadState.Done:
                OpenContainingFolder();
                break;
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_asset == null) return;
        if (UpdateDownloader.IsAlreadyDownloaded(_asset, _info.Version, out var existing))
        {
            _packagePath = existing;
            SetDoneState(alreadyExisted: true);
            return;
        }

        _cts = new CancellationTokenSource();
        _state = DownloadState.Downloading;
        _actionButton.Text = "取消下载";
        _browserButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        SetStatus("正在连接下载服务器…", Color.Gray);

        var progress = new Progress<UpdateDownloader.DownloadProgress>(OnDownloadProgress);
        var result = await UpdateDownloader.DownloadAsync(_asset, _info.Version, progress, _cts.Token);

        _cts.Dispose();
        _cts = null;
        if (IsDisposed) return;

        switch (result.Status)
        {
            case UpdateDownloader.DownloadStatus.Completed:
                _packagePath = result.FilePath;
                SetDoneState(alreadyExisted: false);
                break;
            case UpdateDownloader.DownloadStatus.Canceled:
                ResetToIdle("已取消下载。");
                break;
            default:
                ResetToIdle(
                    $"下载失败：{result.ErrorMessage}\n可重试，或点「在浏览器中打开」手动下载。",
                    Color.Firebrick);
                break;
        }
    }

    private void OnDownloadProgress(UpdateDownloader.DownloadProgress progress)
    {
        if (IsDisposed) return;
        var percent = progress.Total > 0
            ? (int)Math.Clamp(progress.Received * 100 / progress.Total, 0, 100)
            : 0;
        _progressBar.Value = percent;
        var receivedMb = progress.Received / 1048576.0;
        var totalMb = progress.Total / 1048576.0;
        _statusLabel.Text = progress.Total > 0
            ? $"正在下载… {percent}%（{receivedMb:F1} / {totalMb:F1} MB）"
            : $"正在下载… {receivedMb:F1} MB";
        _statusLabel.ForeColor = Color.Gray;
    }

    private void ResetToIdle(string status, Color? color = null)
    {
        _state = DownloadState.Idle;
        _actionButton.Text = "下载更新";
        _actionButton.Enabled = true;
        _browserButton.Enabled = true;
        _progressBar.Visible = false;
        SetStatus(status, color ?? Color.Gray);
    }

    private void SetDoneState(bool alreadyExisted)
    {
        _state = DownloadState.Done;
        _actionButton.Text = "打开文件夹";
        _actionButton.Enabled = true;
        _browserButton.Enabled = true;
        _progressBar.Visible = false;
        var prefix = alreadyExisted ? "更新包已在本地：" : "已下载：";
        SetStatus($"{prefix}{Path.GetFileName(_packagePath)}\n退出 MuSync 后，用它替换当前程序即可。", Color.Green);
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void OpenContainingFolder()
    {
        try
        {
            if (File.Exists(_packagePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_packagePath}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(_packagePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 打开更新包文件夹失败: {ex.Message}");
        }
    }

    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_info.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 打开发布页失败: {ex.Message}");
        }
    }
}
