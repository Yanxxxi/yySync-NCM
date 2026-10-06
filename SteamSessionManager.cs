using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
namespace MusicRpc;

internal sealed record GuardChallenge(string Kind, bool PreviousCodeIncorrect = false);
internal sealed class SteamSessionManager : IDisposable
{
    private SteamClient? _steamClient;
    private CallbackManager? _callbackManager;
    private SteamUser? _steamUser;
    private SteamFriends? _steamFriends;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _loginGate = new(1);
    private Task? _callbackTask;
    private bool _isRunning;
    private bool _hasAuthenticated;
    private string _currentGameName = "";
    private TaskCompletionSource<bool> _connected = NewCompletion();
    private TaskCompletionSource<EResult>? _loggedOn;
    private TaskCompletionSource<string>? _guardCode;
    private int _generation;
    public bool IsConnected => _steamClient?.IsConnected ?? false;
    public bool IsLoggedOn { get; private set; }
    public int SessionGeneration => _generation;
    public string? Username { get; private set; }
    public string? LoginError { get; private set; }
    public event Action<bool>? OnSteamGuardRequired;
    public event Action<GuardChallenge?>? OnGuardChallenge;

    private static TaskCompletionSource<bool> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(cb =>
        {
            _connected.TrySetResult(true);
            if (_hasAuthenticated)
                _ = Task.Run(RestoreSessionAsync);
        });
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            IsLoggedOn = false;
            _currentGameName = "";
            _connected = NewCompletion();
            _loggedOn?.TrySetResult(EResult.NoConnection);
            if (!cb.UserInitiated && _isRunning)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000, _cts.Token);
                        if (_isRunning) _steamClient.Connect();
                    }
                    catch (OperationCanceledException) { }
                });
        });
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(cb =>
        {
            IsLoggedOn = cb.Result == EResult.OK;
            LoginError = IsLoggedOn ? null : $"Steam 登录失败：{cb.Result}";
            if (IsLoggedOn)
            {
                _hasAuthenticated = true;
                Interlocked.Increment(ref _generation);
                _currentGameName = "";
                _steamFriends?.SetPersonaState(EPersonaState.Online);
            }
            _loggedOn?.TrySetResult(cb.Result);
        });
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(cb =>
        {
            IsLoggedOn = false;
            _currentGameName = "";
            LoginError = $"Steam 已断开会话：{cb.Result}";
        });
        _callbackTask = Task.Run(() => CallbackLoop(_cts.Token));
        _steamClient.Connect();
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_steamClient is null) { LoginError = "Steam 客户端未初始化"; return false; }
        if (IsConnected) return true;
        try
        {
            await _connected.Task.WaitAsync(TimeSpan.FromSeconds(30), _cts.Token);
            return IsConnected;
        }
        catch (TimeoutException) { LoginError = "连接 Steam 超时，已保留登录凭据，请稍后重试"; return false; }
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        await _loginGate.WaitAsync(_cts.Token);
        try
        {
            if (!await EnsureConnectedAsync()) return false;
            var config = Configurations.Instance.Settings;
            var sameAccount = string.Equals(config.SteamUsername, username, StringComparison.OrdinalIgnoreCase);
            Username = username;
            LoginError = null;
            using var authTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            authTimeout.CancelAfter(TimeSpan.FromMinutes(3));
            var auth = await _steamClient!.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true,
                    Authenticator = new SteamGuardAuthenticator(this, authTimeout.Token),
                    GuardData = sameAccount ? config.SteamGuardData : null,
                    DeviceFriendlyName = "yySync-NCM",
                    ClientOSType = EOSType.Windows10
                }).WaitAsync(authTimeout.Token);
            var result = await auth.PollingWaitForResultAsync(authTimeout.Token);
            // Persist the reusable token as soon as authentication succeeds. CM connection
            // failures after this point must not cause another phone authorization.
            config.SteamUsername = result.AccountName;
            config.SteamRefreshToken = result.RefreshToken;
            config.SteamGuardData = result.NewGuardData ?? (sameAccount ? config.SteamGuardData : "");
            Configurations.Instance.Save();
            return await LogOnAsync(result.AccountName, result.RefreshToken);
        }
        catch (OperationCanceledException) { LoginError = "登录已取消或验证超时，已保留现有凭据"; return false; }
        catch (Exception ex) { LoginError = $"登录失败：{ex.Message}"; return false; }
        finally
        {
            OnGuardChallenge?.Invoke(null);
            _guardCode = null;
            _loginGate.Release();
        }
    }

    public async Task<bool> LoginWithTokenAsync(string username, string refreshToken)
    {
        await _loginGate.WaitAsync(_cts.Token);
        try
        {
            if (!await EnsureConnectedAsync()) return false;
            return await LogOnAsync(username, refreshToken);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { LoginError = $"自动登录失败：{ex.Message}"; return false; }
        finally { _loginGate.Release(); }
    }

    private async Task<bool> LogOnAsync(string username, string refreshToken)
    {
        Username = username;
        LoginError = null;
        _loggedOn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _steamUser!.LogOn(new SteamUser.LogOnDetails
        {
            Username = username, AccessToken = refreshToken, LoginID = 1243,
            ShouldRememberPassword = true, MachineName = "yySync-NCM", ClientOSType = EOSType.Windows10
        });
        EResult result;
        try { result = await _loggedOn.Task.WaitAsync(TimeSpan.FromSeconds(30), _cts.Token); }
        catch (TimeoutException) { LoginError = "自动登录超时，已保留登录凭据，请稍后重试"; return false; }
        if (result == EResult.OK) return true;
        // Only a definitive credential rejection invalidates a saved refresh token.
        if (InvalidatesToken(result))
        {
            Configurations.Instance.Settings.SteamRefreshToken = "";
            Configurations.Instance.Save();
            LoginError = $"Steam 令牌已失效（{result}），请重新登录";
        }
        return false;
    }

    internal static bool InvalidatesToken(EResult result) =>
        result is EResult.InvalidPassword or EResult.Expired or EResult.Revoked;

    private async Task RestoreSessionAsync()
    {
        var saved = Configurations.Instance.Settings;
        if (!_isRunning || string.IsNullOrEmpty(saved.SteamRefreshToken)) return;
        await LoginWithTokenAsync(saved.SteamUsername, saved.SteamRefreshToken);
    }

    public Task SetGameNameAsync(string gameName)
    {
        if (!IsLoggedOn || _steamClient is null || gameName == _currentGameName) return Task.CompletedTask;
        var request = NewGamesMessage();
        if (!string.IsNullOrEmpty(gameName))
            request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_extra_info = gameName,
                game_id = new GameID { AppType = GameID.GameType.Shortcut, ModID = uint.MaxValue }
            });
        _steamClient.Send(request);
        _currentGameName = gameName;
        return Task.CompletedTask;
    }

    private static ClientMsgProtobuf<CMsgClientGamesPlayed> NewGamesMessage() =>
        new(EMsg.ClientGamesPlayedWithDataBlob)
        {
            Body = { client_os_type = unchecked((uint)EOSType.Windows10) }
        };

    public void ClearGameName()
    {
        if (!IsLoggedOn || _steamClient is null) return;
        _steamClient.Send(NewGamesMessage());
        _currentGameName = "";
    }

    public void SubmitSteamGuardCode(string code) => _guardCode?.TrySetResult(code);
    private void CallbackLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try { _callbackManager?.RunWaitCallbacks(TimeSpan.FromMilliseconds(250)); }
            catch (Exception ex) { Debug.WriteLine($"Steam callback: {ex.Message}"); }
        }
    }

    private sealed class SteamGuardAuthenticator(SteamSessionManager manager, CancellationToken cancellation) : IAuthenticator
    {
        private Task<string> RequestCode(string kind, bool incorrect)
        {
            manager._guardCode = new(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.OnGuardChallenge?.Invoke(new GuardChallenge(kind, incorrect));
            manager.OnSteamGuardRequired?.Invoke(kind == "deviceCode");
            return manager._guardCode.Task.WaitAsync(cancellation);
        }
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
            RequestCode("deviceCode", previousCodeWasIncorrect);
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
            RequestCode("emailCode", previousCodeWasIncorrect);
        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            manager.OnGuardChallenge?.Invoke(new GuardChallenge("confirmation"));
            manager.OnSteamGuardRequired?.Invoke(true);
            return Task.FromResult(true);
        }
    }

    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        if (IsLoggedOn) { ClearGameName(); _steamUser?.LogOff(); }
        _steamClient?.Disconnect();
        _callbackTask?.Wait(3000);
        // Tasks may still be finishing cancellation; do not dispose their synchronization
        // primitives until process teardown.
    }
}
