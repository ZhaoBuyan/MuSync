#nullable disable
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace MuSync;
internal sealed class SteamLoginForm : Form
{
    private TextBox _txtUser;
    private TextBox _txtPass;
    private CheckBox _chkRemember;
    private Button _btnLogin;
    private Button _btnCancel;
    private Label _lblStatus;
    private readonly SteamSessionManager _session;
    private bool _isLoginInProgress;
    private Action<bool> _guardHandler;
    public bool LoginSucceeded { get; private set; }

    public SteamLoginForm(SteamSessionManager session)
    {
        _session = session;
        InitializeComponent();
        Load += async (s, e) => await AttemptAutoLogin();
        FormClosed += (s, e) =>
        {
            // 窗体关闭时退订事件，避免向已销毁的窗口 Invoke（曾导致"句柄未创建"异常）
            if (_guardHandler != null)
            {
                _session.OnSteamGuardRequired -= _guardHandler;
                _guardHandler = null;
            }
        };
    }

    private void InitializeComponent()
    {
        Text = "Steam 登录";
        Size = new Size(350, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        var lblUser = new Label { Text = "Steam 账号:", Location = new Point(20, 20), AutoSize = true };
        _txtUser = new TextBox { Location = new Point(20, 45), Width = 290 };
        if (!string.IsNullOrEmpty(Configurations.Instance.Settings.SteamUsername))
        {
            _txtUser.Text = Configurations.Instance.Settings.SteamUsername;
        }
        var lblPass = new Label { Text = "密码:", Location = new Point(20, 80), AutoSize = true };
        _txtPass = new TextBox { Location = new Point(20, 105), Width = 290, UseSystemPasswordChar = true };
        _chkRemember = new CheckBox
        {
            Text = "记住我（下次自动登录）",
            Location = new Point(20, 140),
            AutoSize = true,
            Checked = true
        };
        _btnLogin = new Button
        {
            Text = "登录",
            Location = new Point(130, 180),
            Width = 80,
            Height = 30,
            DialogResult = DialogResult.None
        };
        _btnLogin.Click += async (s, e) => await PerformLogin();
        _btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(230, 180),
            Width = 80,
            Height = 30,
            DialogResult = DialogResult.Cancel
        };
        _lblStatus = new Label
        {
            Text = "",
            Location = new Point(20, 220),
            Width = 300,
            ForeColor = Color.Red
        };
        Controls.AddRange(new Control[] { lblUser, _txtUser, lblPass, _txtPass, _chkRemember, _btnLogin, _btnCancel, _lblStatus });
        AcceptButton = _btnLogin;
        CancelButton = _btnCancel;
    }

    private async Task AttemptAutoLogin()
    {
        var savedUser = Configurations.Instance.Settings.SteamUsername;
        var savedToken = Configurations.Instance.Settings.SteamRefreshToken;
        if (string.IsNullOrEmpty(savedUser) || string.IsNullOrEmpty(savedToken))
        {
            return;
        }
        _isLoginInProgress = true;
        UpdateUiState();
        _lblStatus.ForeColor = Color.Blue;
        _lblStatus.Text = "正在自动登录...";
        var success = await Task.Run(() => _session.LoginWithTokenAsync(savedUser, savedToken));
        if (IsDisposed || !IsHandleCreated) return;
        if (success)
        {
            LoginSucceeded = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            _lblStatus.ForeColor = Color.Red;
            _lblStatus.Text = "自动登录失败，请手动登录。";
            _isLoginInProgress = false;
            UpdateUiState();
        }
    }

    private async Task PerformLogin()
    {
        if (_isLoginInProgress) return;
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text.Trim();
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ShowStatus("请输入账号和密码", Color.Red);
            return;
        }
        _isLoginInProgress = true;
        UpdateUiState();
        ShowStatus("正在登录...", Color.Blue);
        // 勾选"记住我"才保存令牌用于下次自动登录
        _session.RememberSession = _chkRemember.Checked;
        if (_guardHandler == null)
        {
            _guardHandler = OnSteamGuardRequired;
            _session.OnSteamGuardRequired += _guardHandler;
        }
        var success = await Task.Run(() => _session.LoginAsync(user, pass));
        if (IsDisposed || !IsHandleCreated) return;
        if (success)
        {
            LoginSucceeded = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            ShowStatus(_session.LoginError ?? "登录失败", Color.Red);
            _isLoginInProgress = false;
            UpdateUiState();
        }
    }

    /// <summary>Steam Guard 验证回调（仅存活窗口响应；事件在窗体关闭时退订）。</summary>
    private void OnSteamGuardRequired(bool isMobile)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            Invoke(() =>
            {
                if (IsDisposed) return;
                if (isMobile)
                {
                    ShowStatus("请在手机 Steam App 批准登录（没收到推送：App → 确认 → 登录请求）", Color.Blue);
                }
                else
                {
                    var code = InputBox("Steam Guard 验证", "请输入邮箱验证码:", isMobile);
                    if (!string.IsNullOrEmpty(code))
                    {
                        _session.SubmitSteamGuardCode(code);
                        ShowStatus("正在验证...", Color.Blue);
                    }
                    else
                    {
                        _session.SubmitSteamGuardCode("");
                        ShowStatus("已取消。", Color.Red);
                    }
                }
            });
        }
        catch (InvalidOperationException)
        {
            // 窗体正在销毁：忽略本次提示
        }
    }

    private void ShowStatus(string msg, Color color)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            if (!IsHandleCreated) return;
            try
            {
                Invoke(() => ShowStatus(msg, color));
            }
            catch (InvalidOperationException)
            {
                // 窗体正在销毁
            }
            return;
        }
        _lblStatus.ForeColor = color;
        _lblStatus.Text = msg;
    }

    private void UpdateUiState()
    {
        _txtUser.Enabled = !_isLoginInProgress;
        _txtPass.Enabled = !_isLoginInProgress;
        _btnLogin.Enabled = !_isLoginInProgress;
        _chkRemember.Enabled = !_isLoginInProgress;
        _btnLogin.Text = _isLoginInProgress ? "..." : "登录";
        Cursor = _isLoginInProgress ? Cursors.WaitCursor : Cursors.Default;
    }

    private string InputBox(string title, string prompt, bool isMobile)
    {
        Form promptForm = new Form()
        {
            Width = 330,
            Height = 160,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false
        };
        Label textLabel = new Label() { Left = 20, Top = 20, Text = prompt, AutoSize = true };
        TextBox textBox = new TextBox() { Left = 20, Top = 50, Width = 270 };
        Button confirmation = new Button() { Text = "确定", Left = 210, Width = 80, Top = 85, DialogResult = DialogResult.OK };
        promptForm.Controls.AddRange(new Control[] { textLabel, textBox, confirmation });
        promptForm.AcceptButton = confirmation;
        return promptForm.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
    }
}
