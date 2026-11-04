#nullable disable
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MuSync.Utils;
namespace MuSync;
/// <summary>Steam 登录窗口：账号密码 / 手机令牌输入，含「记住我」与异常登录指引。</summary>
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
        // 已经登录上就根本不需要登录窗口（例如后台自愈重连先成功了）。
        // 窗口一显示就自动关掉，避免出现「已经登录、甚至开始同步了，还杵着一个登录窗」。
        // 这一窗还会诱导用户再登一次，把已有会话顶掉（LogonSessionReplaced）→ 反复掉线。
        Shown += (s, e) =>
        {
            if (_session.IsLoggedOn) CompleteWithSuccess();
        };
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

    /// <summary>以「登录成功」收尾：记结果、按 OK 关闭窗口（已销毁 / 未建句柄时安全跳过）。</summary>
    private void CompleteWithSuccess()
    {
        if (IsDisposed || !IsHandleCreated) return;
        LoginSucceeded = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void InitializeComponent()
    {
        Theme.BeginForm(this);
        Text = Loc.L("Steam 登录", "Steam Sign In");
            // 用我们自己画的图标：否则标题栏会显示 WinForms 默认图标
            Icon = AppResource.Icon;
        ClientSize = new Size(400, 448);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        var lblUser = new Label { Text = Loc.L("Steam 账号:", "Steam account:"), Location = new Point(24, 72), AutoSize = true };
        _txtUser = new TextBox { Location = new Point(24, 96), Width = 352 };
        if (!string.IsNullOrEmpty(Configurations.Instance.Settings.SteamUsername))
        {
            _txtUser.Text = Configurations.Instance.Settings.SteamUsername;
        }
        var lblPass = new Label { Text = Loc.L("密码:", "Password:"), Location = new Point(24, 144), AutoSize = true };
        _txtPass = new TextBox { Location = new Point(24, 168), Width = 352, UseSystemPasswordChar = true };
        _chkRemember = new CheckBox
        {
            Text = Loc.L("记住我（下次自动登录）", "Remember me (auto sign-in next time)"),
            Location = new Point(24, 212),
            AutoSize = true,
            Checked = true
        };
        _btnLogin = new CueButton
        {
            Text = Loc.L("登录", "Sign in"),
            Location = new Point(264, 256),
            Width = 112,
            Height = 32,
            DialogResult = DialogResult.None
        };
        _btnLogin.Click += async (s, e) => await PerformLogin();
        _btnCancel = new CueButton
        {
            Text = Loc.L("取消", "Cancel"),
            Location = new Point(152, 256),
            Width = 104,
            Height = 32,
            DialogResult = DialogResult.Cancel
        };
        _lblStatus = new Label
        {
            Text = "",
            Location = new Point(24, 304),
            Width = 352,
            Height = 40,
            ForeColor = Theme.OnAir
        };
        var lblPhoneHint = new Label
        {
            Text = Loc.L("手机验证提示：在 Steam 手机应用中查看本次登录请求，核对设备和地点后批准。\n未收到推送时，请打开应用的 Steam 令牌页面查看请求。", "Approve this sign-in in the Steam mobile app after checking the device and location.\nIf no notification appears, open the Steam Guard tab to check for the request."),
            Location = new Point(24, 352),
            Width = 352,
            Height = 72,
            Font = new Font("Microsoft YaHei", 8),
            ForeColor = Color.FromArgb(130, 130, 130)
        };
        Controls.Add(new Label { Text = Loc.L("连接 Steam", "Connect to Steam"), Font = Theme.Heading, Location = new Point(24, 20), Size = new Size(352, 40) });
        Controls.AddRange(new Control[] { lblUser, _txtUser, lblPass, _txtPass, _chkRemember, _btnLogin, _btnCancel, _lblStatus, lblPhoneHint });
        AcceptButton = _btnLogin;
        CancelButton = _btnCancel;
        Font = Theme.Body;
        Theme.Apply(this);
        Theme.PolishFields(this);
        Theme.Primary(_btnLogin);
        lblPhoneHint.Font = Theme.Caption;
        lblPhoneHint.ForeColor = Theme.Muted;
        Theme.EndForm(this);
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
        _lblStatus.ForeColor = Theme.Muted;
        _lblStatus.Text = Loc.L("正在自动登录...", "Signing in automatically...");
        // 带重试：断线时 LoginWithTokenAsync 会立刻返回失败（NetworkLost），
        // 而自带的重连循环几秒后就会连上并重登成功 —— 这里多试几轮就不必让用户手填。
        var success = false;
        for (var attempt = 0; attempt < 3 && !success; attempt++)
        {
            success = await Task.Run(() => _session.LoginWithTokenAsync(
                Configurations.Instance.Settings.SteamUsername,
                Configurations.Instance.Settings.SteamRefreshToken));
            if (success || IsDisposed || !IsHandleCreated) break;
            // 令牌已被服务器拒绝：重试没有意义，直接进入手动登录
            if (_session.TokenLoginRejected) break;
            ShowStatus(Loc.L("网络未就绪，正在重试...", "Network not ready — retrying..."), Theme.Muted);
            await Task.Delay(1500);
            if (IsDisposed || !IsHandleCreated) break;
        }
        if (IsDisposed || !IsHandleCreated) return;
        if (success || _session.IsLoggedOn)
        {
            // 兜底：窗口存续期间后台（自愈重连）可能已经登录成功
            CompleteWithSuccess();
        }
        else
        {
            _lblStatus.ForeColor = Theme.OnAir;
            _lblStatus.Text = Loc.L("自动登录失败，请手动登录。", "Auto sign-in failed. Please sign in manually.");
            _isLoginInProgress = false;
            UpdateUiState();
        }
    }

    private async Task PerformLogin()
    {
        if (_isLoginInProgress) return;
        var user = _txtUser.Text.Trim();
        var pass = _txtPass.Text;
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ShowStatus(Loc.L("请输入账号和密码", "Please enter your account and password"), Theme.OnAir);
            return;
        }
        _isLoginInProgress = true;
        UpdateUiState();
        ShowStatus(Loc.L("正在登录...", "Signing in..."), Theme.Muted);
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
            CompleteWithSuccess();
        }
        else
        {
            ShowStatus(_session.LoginError ?? Loc.L("登录失败", "Sign-in failed"), Theme.OnAir);
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
                    ShowStatus(Loc.L("请在手机 Steam 端完成验证（步骤见下方提示）", "Complete verification in the Steam mobile app (steps below)"), Theme.Muted);
                }
                else
                {
                    var code = InputBox(Loc.L("Steam Guard 验证", "Steam Guard"), Loc.L("请输入邮箱验证码:", "Enter email code:"), isMobile);
                    if (!string.IsNullOrEmpty(code))
                    {
                        _session.SubmitSteamGuardCode(code);
                        ShowStatus(Loc.L("正在验证...", "Verifying..."), Theme.Muted);
                    }
                    else
                    {
                        _session.SubmitSteamGuardCode("");
                        ShowStatus(Loc.L("已取消。", "Canceled."), Theme.OnAir);
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
        _btnLogin.Text = _isLoginInProgress ? "..." : Loc.L("登录", "Sign in");
        Cursor = _isLoginInProgress ? Cursors.WaitCursor : Cursors.Default;
    }

    private string InputBox(string title, string prompt, bool isMobile)
    {
        // using：ShowDialog 只是模态显示，窗体与其中的控件（含 HWND）必须显式释放，
        // 否则每次输入验证码都泄漏一整个窗体，要等 GC 终结器才回收
        using var promptForm = CreateGuardPrompt(title, prompt, out var textBox);
        // 先把文本取出来：窗体释放后 textBox 不可再访问
        var confirmed = promptForm.ShowDialog(this) == DialogResult.OK;
        var text = textBox.Text;
        return confirmed ? text : null;
    }
    internal static Form CreateGuardPrompt(string title, string prompt, out TextBox textBox)
    {
        var form = new Form
        {
            ClientSize = new Size(368, 208), Text = title, Icon = AppResource.Icon,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = Theme.Body
        };
        Theme.BeginForm(form);
        var hint = new Label { Text = prompt, Location = new Point(24, 24), Size = new Size(320, 48) };
        textBox = new TextBox { Location = new Point(24, 88), Width = 320 };
        var ok = new CueButton { Text = Loc.L("确定", "OK"), Bounds = new Rectangle(232, 152, 112, 32), DialogResult = DialogResult.OK };
        var cancel = new CueButton { Text = Loc.L("取消", "Cancel"), Bounds = new Rectangle(112, 152, 112, 32), DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([hint, textBox, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        Theme.Apply(form);
        Theme.PolishFields(form);
        Theme.Primary(ok);
        Theme.EndForm(form);
        return form;
    }

}
