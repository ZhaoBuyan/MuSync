using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MuSync.Utils;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
namespace MuSync;
internal class SteamSessionManager : IDisposable
{
    // 断线自动重连：初始退避 5 秒，翻倍至上限 60 秒
    private const int ReconnectInitialDelaySeconds = 5;
    private const int ReconnectMaxDelaySeconds = 60;
    // 服务器要求更换节点（TryAnotherCM）时的自动重试上限
    private const int MaxCmRetries = 2;
    private int _cmRetryCount;
    // 启动后若长时间未连上 Steam（如开机时网络未就绪），进入自动重连的等待时间
    private const int FirstConnectWatchdogDelaySeconds = 20;

    private SteamClient? _steamClient;
    private volatile CallbackManager? _callbackManager;
    private ProtocolTypes _currentProtocol = ProtocolTypes.Tcp;
    private SteamUser? _steamUser;
    private SteamFriends? _steamFriends;
    private readonly CancellationTokenSource _cts = new();
    private Task? _callbackTask;
    private bool _isRunning;
    private string _currentGameName = string.Empty;
    private SteamID? _selfSteamId;
    private readonly ManualResetEventSlim _connectedEvent = new(false);
    private volatile bool _reconnectLoopRunning;
    private volatile bool _reconnectPending;

    public bool IsConnected => _steamClient?.IsConnected ?? false;
    public bool IsLoggedOn { get; private set; }
    public bool IsRealGameActive { get; private set; }
    /// <summary>密码登录成功后是否保存 refresh token（用于下次自动登录）；不保存则每次启动需手动登录。</summary>
    public bool RememberSession { get; set; } = true;
    public string? Username { get; private set; }
    public string? LoginError { get; private set; }
    public event Action<bool>? OnSteamGuardRequired;
    private TaskCompletionSource<string>? _guardCodeTcs;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        InitializeClient(ProtocolTypes.Tcp);
        _callbackTask = Task.Run(() => CallbackLoop(_cts.Token));
        _steamClient?.Connect();
        Debug.WriteLine("[SteamSession] 正在连接到 Steam...");
        StartFirstConnectWatchdog();
    }

    /// <summary>初始化连接客户端（TCP 或 WebSocket 协议）。切换协议时重建客户端与回调订阅。</summary>
    private void InitializeClient(ProtocolTypes protocol)
    {
        try
        {
            _steamClient?.Disconnect();
        }
        catch
        {
            // 旧客户端可能处于任意状态，忽略清理异常
        }
        _currentProtocol = protocol;
        var configuration = SteamConfiguration.Create(builder => builder.WithProtocolTypes(protocol));
        _steamClient = new SteamClient(configuration);
        var callbackManager = new CallbackManager(_steamClient);
        _callbackManager = callbackManager;
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        callbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
        _connectedEvent.Reset();
        Logger.Info($"[SteamSession] 连接客户端已就绪（协议：{protocol}）");
    }

    /// <summary>TCP 连接持续失败且允许回退时，改用 WebSocket (443 端口) 重连。</summary>
    private bool TryFallbackToWebSocketIfNeeded()
    {
        if (_currentProtocol != ProtocolTypes.Tcp) return false;
        if (!Configurations.Instance.Settings.AllowWebSocketFallback) return false;
        Logger.Warn("[SteamSession] TCP 连接持续失败，自动切换 WebSocket (443 端口) 重试");
        _reconnectPending = true;
        InitializeClient(ProtocolTypes.WebSocket);
        return true;
    }

    private void OnConnected(SteamClient.ConnectedCallback cb)
    {
        Debug.WriteLine("[SteamSession] 已连接到 Steam");
        Logger.Info("[SteamSession] 已连接到 Steam");
        _connectedEvent.Set();
        // 仅自动重连场景需要在此补登录；首次启动由启动流程负责登录
        if (!_reconnectPending || IsLoggedOn) return;
        _reconnectPending = false;
        var settings = Configurations.Instance.Settings;
        if (string.IsNullOrEmpty(settings.SteamUsername) ||
            string.IsNullOrEmpty(settings.SteamRefreshToken))
        {
            return;
        }
        Debug.WriteLine("[SteamSession] 连接已恢复，尝试用保存的令牌自动重新登录...");
        Logger.Info("[SteamSession] 连接已恢复，尝试用保存的令牌自动重新登录...");
        _ = Task.Run(async () =>
        {
            try
            {
                await LoginWithTokenAsync(settings.SteamUsername, settings.SteamRefreshToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamSession] 自动重登异常: {ex.Message}");
                Logger.Error($"[SteamSession] 自动重登异常: {ex.Message}");
            }
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        IsLoggedOn = false;
        _connectedEvent.Reset();
        Debug.WriteLine($"[SteamSession] 已断开连接 (UserInitiated={cb.UserInitiated})");
        Logger.Info($"[SteamSession] 已断开连接 (UserInitiated={cb.UserInitiated})");
        if (cb.UserInitiated || !_isRunning || _reconnectLoopRunning) return;
        _reconnectLoopRunning = true;
        _ = Task.Run(async () =>
        {
            try
            {
                await AutoReconnectLoopAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 程序退出中，静默忽略
            }
            finally
            {
                _reconnectLoopRunning = false;
            }
        }, _cts.Token);
    }

    /// <summary>启动 watchdog：首次连接长时间不成功（网络未就绪/服务器不可达）时自动进入退避重连。</summary>
    private void StartFirstConnectWatchdog()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(FirstConnectWatchdogDelaySeconds), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_isRunning || _steamClient?.IsConnected == true || _reconnectLoopRunning) return;
            _reconnectLoopRunning = true;
            try
            {
                Logger.Warn("[SteamSession] 首次连接超时，转入自动重连");
                TryFallbackToWebSocketIfNeeded();
                await AutoReconnectLoopAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 程序退出中，静默忽略
            }
            finally
            {
                _reconnectLoopRunning = false;
            }
        }, _cts.Token);
    }

    /// <summary>指数退避重连：反复 Connect 直到连上 Steam（无论是否已登录）。</summary>
    private async Task AutoReconnectLoopAsync(CancellationToken token)
    {
        var delaySeconds = ReconnectInitialDelaySeconds;
        var failedAttempts = 0;
        while (_isRunning && !token.IsCancellationRequested && _steamClient?.IsConnected != true)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_isRunning || token.IsCancellationRequested || _steamClient?.IsConnected == true) return;
            try
            {
                _reconnectPending = true;
                Debug.WriteLine($"[SteamSession] 尝试自动重连 (下次失败退避 {delaySeconds}s)...");
                Logger.Info($"[SteamSession] 尝试自动重连 (下次失败退避 {delaySeconds}s)...");
                _steamClient?.Connect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamSession] 自动重连异常: {ex.Message}");
                Logger.Error($"[SteamSession] 自动重连异常: {ex.Message}");
            }
            // 等待连接结果（最多约 8 秒）；连上即退出循环，令牌重登由 ConnectedCallback 触发
            for (var i = 0; i < 16 && !token.IsCancellationRequested && _steamClient?.IsConnected != true; i++)
            {
                try
                {
                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            if (_steamClient?.IsConnected == true) return;
            failedAttempts++;
            // 连续多次失败：可能是当前网络对 TCP 27017 端口不友好，尝试切换到 WebSocket (443)
            if (failedAttempts >= 3 && TryFallbackToWebSocketIfNeeded())
            {
                failedAttempts = 0;
                delaySeconds = ReconnectInitialDelaySeconds;
                continue;
            }
            delaySeconds = Math.Min(delaySeconds * 2, ReconnectMaxDelaySeconds);
        }
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result == EResult.OK)
        {
            IsLoggedOn = true;
            _selfSteamId = cb.ClientSteamID;
            IsRealGameActive = false;
            LoginError = null;
            Debug.WriteLine($"[SteamSession] 登录成功! SteamID: {cb.ClientSteamID}");
            Logger.Info($"[SteamSession] 登录成功! SteamID: {cb.ClientSteamID}");
            _steamFriends?.SetPersonaState(EPersonaState.Online);
            Debug.WriteLine("[SteamSession] 已设置在线状态");
        }
        else
        {
            IsLoggedOn = false;
            LoginError = cb.Result.ToString();
            Debug.WriteLine($"[SteamSession] 登录失败: {cb.Result} / {cb.ExtendedResult}");
            Logger.Error($"[SteamSession] 登录失败: {cb.Result} / {cb.ExtendedResult}");
        }
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        IsLoggedOn = false;
        Debug.WriteLine($"[SteamSession] 已登出: {cb.Result}");
        Logger.Info($"[SteamSession] 已登出: {cb.Result}");
    }

    public bool WaitForConnection(int timeoutMs = 8000)
    {
        return _connectedEvent.Wait(timeoutMs);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (_steamClient == null)
        {
            LoginError = "Steam 客户端未初始化";
            return false;
        }
        if (!IsConnected)
        {
            if (!WaitForConnection())
            {
                LoginError = "无法连接到 Steam 服务器";
                return false;
            }
        }
        Username = username;
        LoginError = null;
        try
        {
            var authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true,
                    Authenticator = new SteamGuardAuthenticator(this),
                    GuardData = Configurations.Instance.Settings.SteamGuardData,
                    DeviceFriendlyName = "MuSync"
                }
            ).ConfigureAwait(false);
            var pollResult = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pollResult.NewGuardData) && RememberSession)
            {
                Configurations.Instance.Settings.SteamGuardData = pollResult.NewGuardData;
                Configurations.Instance.Save();
            }
            var logOnDetails = new SteamUser.LogOnDetails
            {
                Username = username,
                AccessToken = pollResult.RefreshToken,
                LoginID = 1243,
                ShouldRememberPassword = true,
                MachineName = "MuSync"
            };
            _cmRetryCount = 0;
            LoginError = null;
            _steamUser!.LogOn(logOnDetails);
            var waitResult = await WaitForLogOnResultAsync(logOnDetails, 30);
            if (waitResult == LogOnWaitResult.Success)
            {
                var settings = Configurations.Instance.Settings;
                settings.SteamUsername = username;
                if (RememberSession)
                {
                    settings.SteamRefreshToken = pollResult.RefreshToken;
                }
                else
                {
                    // 未勾选“记住我”：不保存令牌与 Guard 数据，下次启动需手动登录
                    settings.SteamRefreshToken = "";
                    settings.SteamGuardData = "";
                }
                Configurations.Instance.Save();
                return true;
            }
            if (waitResult == LogOnWaitResult.Timeout)
            {
                LoginError = "登录超时";
            }
            return false;
        }
        catch (AuthenticationException ex)
        {
            LoginError = $"认证失败: {ex.Result} - {ex.Message}";
            Debug.WriteLine($"[SteamSession] {LoginError}");
            Logger.Error($"[SteamSession] {LoginError}");
            return false;
        }
        catch (Exception ex)
        {
            LoginError = $"登录异常: {ex.Message}";
            Debug.WriteLine($"[SteamSession] {LoginError}");
            Logger.Error($"[SteamSession] {LoginError}");
            return false;
        }
    }

    public async Task<bool> LoginWithTokenAsync(string username, string refreshToken)
    {
        if (_steamClient == null)
        {
            LoginError = "Steam 客户端未初始化";
            return false;
        }
        if (!IsConnected)
        {
            if (!WaitForConnection())
            {
                LoginError = "无法连接到 Steam 服务器";
                return false;
            }
        }
        Username = username;
        var logOnDetails = new SteamUser.LogOnDetails
        {
            Username = username,
            AccessToken = refreshToken,
            LoginID = 1243,
            ShouldRememberPassword = true,
            MachineName = "MuSync"
        };
        _cmRetryCount = 0;
        LoginError = null;
        _steamUser!.LogOn(logOnDetails);
        var waitResult = await WaitForLogOnResultAsync(logOnDetails, 20);
        if (waitResult == LogOnWaitResult.Success) return true;
        if (waitResult == LogOnWaitResult.NetworkLost)
        {
            // 登录过程中网络断开：保留令牌不清除，等自动重连后再试
            return false;
        }
        Debug.WriteLine("[SteamSession] Token 登录失败，清除已保存令牌");
        Logger.Warn("[SteamSession] Token 登录失败，已清除保存的令牌，需要重新登录");
        Configurations.Instance.Settings.SteamRefreshToken = "";
        Configurations.Instance.Save();
        return false;
    }

    private enum LogOnWaitResult
    {
        Success,
        Rejected,
        NetworkLost,
        Timeout
    }

    /// <summary>等待登录结果；服务器返回 TryAnotherCM 时自动更换节点重试（最多 MaxCmRetries 次）。</summary>
    private async Task<LogOnWaitResult> WaitForLogOnResultAsync(SteamUser.LogOnDetails details, int maxPolls)
    {
        for (var i = 0; i < maxPolls; i++)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if (IsLoggedOn) return LogOnWaitResult.Success;
            if (LoginError != null)
            {
                if (LoginError == nameof(EResult.TryAnotherCM) && _cmRetryCount < MaxCmRetries)
                {
                    _cmRetryCount++;
                    Logger.Info($"[SteamSession] 服务器要求更换节点 (TryAnotherCM)，自动重试 {_cmRetryCount}/{MaxCmRetries}");
                    if (!await ReconnectToAnotherCmAsync())
                    {
                        return LogOnWaitResult.NetworkLost;
                    }
                    LoginError = null;
                    _steamUser!.LogOn(details);
                    i = -1; // 更换节点后重新计时
                    continue;
                }
                return LogOnWaitResult.Rejected;
            }
            if (!IsConnected) return LogOnWaitResult.NetworkLost;
        }
        return LogOnWaitResult.Timeout;
    }

    private async Task<bool> ReconnectToAnotherCmAsync()
    {
        try
        {
            _steamClient?.Disconnect();
            _connectedEvent.Reset();
            await Task.Delay(1500).ConfigureAwait(false);
            if (!_isRunning) return false;
            _steamClient?.Connect();
            return await Task.Run(() => _connectedEvent.Wait(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[SteamSession] 更换节点失败: {ex.Message}");
            return false;
        }
    }

    public Task SetGameNameAsync(string gameName)
    {
        if (!IsLoggedOn || _steamClient == null) return Task.CompletedTask;
        if (gameName == _currentGameName) return Task.CompletedTask;
        Debug.WriteLine($"[SteamSession] 正在设置游戏名称: '{gameName}'");
        if (!string.IsNullOrEmpty(gameName))
        {
            var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob)
            {
                Body =
                {
                    client_os_type = unchecked((uint)EOSType.Windows10)
                }
            };
            request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_extra_info = gameName,
                game_id = new GameID
                {
                    AppType = GameID.GameType.Shortcut,
                    ModID = uint.MaxValue
                }
            });
            _steamClient.Send(request);
            Debug.WriteLine($"[SteamSession] CMsgClientGamesPlayed 已发送: '{gameName}'");
        }
        _currentGameName = gameName;
        return Task.CompletedTask;
    }

    public void ClearGameName()
    {
        if (!IsLoggedOn || _steamClient == null) return;
        var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob)
        {
            Body =
            {
                client_os_type = unchecked((uint)EOSType.Windows10)
            }
        };
        _steamClient.Send(request);
        _currentGameName = string.Empty;
        Debug.WriteLine("[SteamSession] 游戏名称已清除");
    }

    public void SubmitSteamGuardCode(string code)
    {
        _guardCodeTcs?.TrySetResult(code);
    }

    private void OnPersonaState(SteamFriends.PersonaStateCallback cb)
    {
        if (_selfSteamId is not { } self || cb.FriendID != self) return;
        var realGame = IsRealGameState(cb);
        if (realGame == IsRealGameActive) return;
        IsRealGameActive = realGame;
        Debug.WriteLine(realGame
            ? "[SteamSession] 检测到本账号正在玩真实 Steam 游戏，将暂停音乐同步"
            : "[SteamSession] 真实游戏已结束，恢复音乐同步");
    }

    private static bool IsRealGameState(SteamFriends.PersonaStateCallback cb)
    {
        if (cb.GameID is not { } gameId || gameId.AppID == 0) return false;
        return gameId.AppType is GameID.GameType.App or GameID.GameType.GameMod;
    }

    private void CallbackLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            _callbackManager?.RunWaitCallbacks(TimeSpan.FromSeconds(1));
        }
    }

    private class SteamGuardAuthenticator(SteamSessionManager manager) : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            manager._guardCodeTcs = new TaskCompletionSource<string>();
            manager.OnSteamGuardRequired?.Invoke(true);
            return manager._guardCodeTcs.Task;
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            manager._guardCodeTcs = new TaskCompletionSource<string>();
            manager.OnSteamGuardRequired?.Invoke(false);
            return manager._guardCodeTcs.Task;
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            manager.OnSteamGuardRequired?.Invoke(true);
            return Task.FromResult(true);
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        if (IsLoggedOn)
        {
            ClearGameName();
            _steamUser?.LogOff();
        }
        _steamClient?.Disconnect();
        _callbackTask?.Wait(3000);
        _cts.Dispose();
        _connectedEvent.Dispose();
    }
}
