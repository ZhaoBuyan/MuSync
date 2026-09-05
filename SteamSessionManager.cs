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

    private SteamClient? _steamClient;
    private CallbackManager? _callbackManager;
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
    public string? Username { get; private set; }
    public string? LoginError { get; private set; }
    public event Action<bool>? OnSteamGuardRequired;
    private TaskCompletionSource<string>? _guardCodeTcs;

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        _callbackManager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
        _callbackTask = Task.Run(() => CallbackLoop(_cts.Token));
        _steamClient.Connect();
        Debug.WriteLine("[SteamSession] 正在连接到 Steam...");
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

    /// <summary>指数退避重连：反复 Connect 直到登录成功、令牌失效或程序退出。</summary>
    private async Task AutoReconnectLoopAsync(CancellationToken token)
    {
        var delaySeconds = ReconnectInitialDelaySeconds;
        while (_isRunning && !IsLoggedOn && !token.IsCancellationRequested)
        {
            // 令牌已失效（LoginWithTokenAsync 失败会清空），不再自动重试，交给用户重新登录
            var settings = Configurations.Instance.Settings;
            if (string.IsNullOrEmpty(settings.SteamUsername) ||
                string.IsNullOrEmpty(settings.SteamRefreshToken))
            {
                Debug.WriteLine("[SteamSession] 无有效登录令牌，停止自动重连");
                Logger.Warn("[SteamSession] 无有效登录令牌，停止自动重连，需要重新登录");
                return;
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_isRunning || IsLoggedOn || token.IsCancellationRequested) return;
            try
            {
                Debug.WriteLine($"[SteamSession] 尝试自动重连 (下次失败退避 {delaySeconds}s)...");
                Logger.Info($"[SteamSession] 尝试自动重连 (下次失败退避 {delaySeconds}s)...");
                _reconnectPending = true;
                _steamClient?.Connect();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SteamSession] 自动重连异常: {ex.Message}");
            }
            delaySeconds = Math.Min(delaySeconds * 2, ReconnectMaxDelaySeconds);
            // 等待连接建立
            for (var i = 0; i < 6 && !IsLoggedOn && _isRunning && _steamClient?.IsConnected == false; i++)
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
            // 连接已建立：等待令牌重登完成（最多约 12 秒），失败则进入下一轮退避
            for (var i = 0; i < 24 && !IsLoggedOn && _isRunning && _steamClient?.IsConnected == true; i++)
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
            if (!string.IsNullOrEmpty(pollResult.NewGuardData))
            {
                Configurations.Instance.Settings.SteamGuardData = pollResult.NewGuardData;
                Configurations.Instance.Save();
            }
            _steamUser!.LogOn(new SteamUser.LogOnDetails
            {
                Username = username,
                AccessToken = pollResult.RefreshToken,
                LoginID = 1243,
                ShouldRememberPassword = true,
                MachineName = "MuSync"
            });
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (IsLoggedOn)
                {
                    Configurations.Instance.Settings.SteamUsername = username;
                    Configurations.Instance.Settings.SteamRefreshToken = pollResult.RefreshToken;
                    Configurations.Instance.Save();
                    return true;
                }
                if (LoginError != null)
                {
                    return false;
                }
            }
            LoginError = "登录超时";
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
        LoginError = null;
        _steamUser!.LogOn(new SteamUser.LogOnDetails
        {
            Username = username,
            AccessToken = refreshToken,
            LoginID = 1243,
            ShouldRememberPassword = true,
            MachineName = "MuSync"
        });
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if (IsLoggedOn) return true;
            if (LoginError != null) break;
        }
        Debug.WriteLine("[SteamSession] Token 登录失败，清除已保存令牌");
        Logger.Warn("[SteamSession] Token 登录失败，已清除保存的令牌，需要重新登录");
        Configurations.Instance.Settings.SteamRefreshToken = "";
        Configurations.Instance.Save();
        return false;
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
