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
    private readonly bool _canAutoUpdate;
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
        // 一键更新仅限「安装器版 + 选中的包确为安装器包」；
        // 包缺失兜底可能选到其他形态，此时退回手动替换流程，避免把绿色包当安装器跑
        _canAutoUpdate = _asset != null &&
                         UpdateChecker.GetAssetEdition(_asset.Name) == "setup" &&
                         UpdateChecker.NormalizeEdition(UpdateChecker.GetCurrentEdition()) == "setup";

        Text = Loc.L("发现新版本", "Update available");
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
            Text = Loc.L($"MuSync {info.Tag} 已发布", $"MuSync {info.Tag} has been released")
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(22, 54),
            ForeColor = Color.Gray,
            Text = Loc.L($"当前版本 v{currentVersion} → 最新版本 {info.Version}", $"Current version v{currentVersion} → Latest version {info.Version}")
        };
        var logsBox = new TextBox
        {
            Location = new Point(20, 84),
            Size = new Size(505, 246),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(248, 248, 248),
            Text = string.IsNullOrWhiteSpace(info.Body) ? Loc.L("(本次更新暂无说明)", "(No release notes for this version)") : info.Body
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
            Text = Loc.L("下载更新", "Download update"),
            Location = new Point(188, 402),
            Size = new Size(125, 32),
            BackColor = Color.White
        };
        _actionButton.Click += ActionButton_Click;
        _browserButton = new Button
        {
            Text = Loc.L("在浏览器中打开", "Open in browser"),
            Location = new Point(319, 402),
            Size = new Size(105, 32),
            BackColor = Color.White
        };
        _browserButton.Click += (_, _) => OpenInBrowser();
        var closeButton = new Button
        {
            Text = Loc.L("稍后", "Later"),
            Location = new Point(430, 402),
            Size = new Size(95, 32),
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
            SetStatus(Loc.L("该版本未提供可直接下载的文件，请点「在浏览器中打开」手动下载。", "No direct download is available for this release. Use \"Open in browser\" to download it manually."), Color.DarkOrange);
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
                if (_canAutoUpdate) RunInstaller();
                else ExitAndOpenFolders();
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
        _actionButton.Text = Loc.L("取消下载", "Cancel download");
        _browserButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        SetStatus(Loc.L("正在连接下载服务器…", "Connecting to the download server..."), Color.Gray);

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
                ResetToIdle(Loc.L("已取消下载。", "Download canceled."));
                break;
            default:
                ResetToIdle(
                    Loc.L($"下载失败：{result.ErrorMessage}\n可重试，或点「在浏览器中打开」手动下载。", $"Download failed: {result.ErrorMessage}\nYou can retry, or use \"Open in browser\" to download manually."),
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
            ? Loc.L($"正在下载… {percent}%（{receivedMb:F1} / {totalMb:F1} MB）", $"Downloading... {percent}% ({receivedMb:F1} / {totalMb:F1} MB)")
            : Loc.L($"正在下载… {receivedMb:F1} MB", $"Downloading... {receivedMb:F1} MB");
        _statusLabel.ForeColor = Color.Gray;
    }

    private void ResetToIdle(string status, Color? color = null)
    {
        _state = DownloadState.Idle;
        _actionButton.Text = Loc.L("下载更新", "Download update");
        _actionButton.Enabled = true;
        _browserButton.Enabled = true;
        _progressBar.Visible = false;
        SetStatus(status, color ?? Color.Gray);
    }

    private void SetDoneState(bool alreadyExisted)
    {
        _state = DownloadState.Done;
        _actionButton.Text = _canAutoUpdate ? Loc.L("立即更新", "Update now") : Loc.L("退出并打开文件夹", "Exit & open folder");
        _actionButton.Enabled = true;
        _browserButton.Enabled = true;
        _progressBar.Visible = false;
        var prefix = alreadyExisted ? Loc.L("更新包已在本地：", "Update package saved locally:") : Loc.L("已下载：", "Downloaded:");
        var actionHint = _canAutoUpdate
            ? Loc.L("点「立即更新」→ 自动安装新版本，完成后自动重启。", "Click \"Update now\" — the new version installs automatically and MuSync restarts when installation finishes.")
            : Loc.L("点「退出并打开文件夹」→ 拖过去替换旧程序即可。", "Click \"Exit & open folder\" — then drag the new file over the old one.");
        SetStatus($"{prefix}{Path.GetFileName(_packagePath)}\n{actionHint}", Color.Green);
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    /// <summary>安装器版：静默运行已下载的新版安装器；本程序退出后由安装器完成升级并自动重启。</summary>
    private void RunInstaller()
    {
        if (_packagePath.Length == 0) return;
        var result = MessageBox.Show(
            this,
            Loc.L("MuSync 将退出，并静默安装新版本。\n安装完成后会自动重新启动，无需其他操作。", "MuSync will exit and install the new version silently.\nIt will restart automatically when finished — no further action needed."),
            Loc.L("立即更新", "Update now"),
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result != DialogResult.OK) return;
        try
        {
            Process.Start(new ProcessStartInfo(
                // 不带 /SUPPRESSMSGBOXES：安装失败时弹出错误框，避免“程序退出后无任何提示”
                _packagePath, "/VERYSILENT /NORESTART /FORCECLOSEAPPLICATIONS")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(_packagePath) ?? ""
            });
            Logger.Info("[Update] 已启动安装器，程序即将退出");
            Application.Exit();
        }
        catch (Exception ex)
        {
            Logger.Error($"[Update] 启动安装器失败: {ex.Message}");
            MessageBox.Show(
                this,
                Loc.L($"启动安装器失败：{ex.Message}\n\n也可以点「在浏览器中打开」手动下载安装。", $"Failed to launch the installer: {ex.Message}\n\nYou can also use \"Open in browser\" to download it manually."),
                Loc.L("更新失败", "Update failed"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    /// <summary>二次确认后：打开新旧两个文件夹，然后退出程序，让用户直接做替换。</summary>
    private void ExitAndOpenFolders()
    {
        var result = MessageBox.Show(
            this,
            Loc.L("MuSync 将退出，并打开新包与程序所在的两个文件夹。\n替换完成后，双击新版程序即可继续使用。", "MuSync will exit and open two folders: the new package and the current app.\nAfter replacing the file, double-click the new version to continue."),
            Loc.L("退出并替换", "Exit & replace"),
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (result != DialogResult.OK) return;
        OpenContainingFolder();
        Logger.Info("[Update] 用户选择退出并打开替换文件夹");
        Application.Exit();
    }

    /// <summary>同时打开「新包所在文件夹」和「当前程序所在文件夹」（各自选中文件），方便拖拽替换。</summary>
    private void OpenContainingFolder()
    {
        var packageDir = Path.GetDirectoryName(_packagePath) ?? "";
        OpenAndSelect(_packagePath);
        var currentExe = Environment.ProcessPath ?? "";
        var currentDir = Path.GetDirectoryName(currentExe) ?? "";
        // 新包与当前程序同目录时只开一个窗口
        if (currentExe.Length > 0 &&
            !string.Equals(currentDir, packageDir, StringComparison.OrdinalIgnoreCase))
        {
            OpenAndSelect(currentExe);
        }
    }

    /// <summary>在资源管理器中打开并选中指定文件；文件不存在时退回打开其所在目录。</summary>
    private static void OpenAndSelect(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
                return;
            }
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] 打开文件夹失败: {ex.Message}");
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
