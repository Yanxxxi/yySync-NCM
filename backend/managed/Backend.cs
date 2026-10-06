using YySyncNcm.Models;
using System.Text.Json;

namespace YySyncNcm;

internal sealed class Backend
{
    private readonly object _gate = new();
    private SteamSessionManager? _session;
    private SteamStatusManager? _status;
    private PlayerInfo? _song;
    private volatile GuardChallenge? _challenge;
    private volatile string? _operationError;
    private int _busy;
    private int _lifetime;
    private bool _started;
    private bool _allowConnect = true;

    public object Dispatch(JsonElement request)
    {
        lock (_gate)
        {
            switch (request.GetProperty("type").GetString())
            {
                case "initialize":
                    _allowConnect = !request.TryGetProperty("connect", out var connect) || connect.GetBoolean();
                    EnsureSession();
                    if (_allowConnect && Configurations.Instance.Settings.EnableSteamSync)
                    {
                        StartSession();
                        if (!string.IsNullOrEmpty(Configurations.Instance.Settings.SteamRefreshToken))
                            BeginLogin(null, null);
                    }
                    break;
                case "state": break;
                case "login":
                    var username = request.GetProperty("username").GetString()?.Trim() ?? "";
                    var password = request.GetProperty("password").GetString() ?? "";
                    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                        throw new ArgumentException("请输入 Steam 用户名和密码");
                    BeginLogin(username, password);
                    break;
                case "retry": BeginLogin(null, null); break;
                case "guard":
                    _session?.SubmitSteamGuardCode(request.GetProperty("code").GetString()?.Trim() ?? "");
                    break;
                case "configure": Configure(request.GetProperty("settings")); break;
                case "playback": UpdatePlayback(request); break;
                case "logout":
                    Shutdown();
                    var saved = Configurations.Instance.Settings;
                    saved.SteamRefreshToken = "";
                    saved.SteamGuardData = "";
                    Configurations.Instance.Save();
                    EnsureSession();
                    break;
                case "shutdown": Shutdown(); break;
                default: throw new ArgumentException("未知插件命令");
            }
            return State();
        }
    }

    private void EnsureSession()
    {
        if (_session is not null) return;
        _session = new SteamSessionManager();
        var lifetime = _lifetime;
        _session.OnGuardChallenge += challenge => { if (lifetime == _lifetime) _challenge = challenge; };
        _status = new SteamStatusManager(_session);
    }

    private void StartSession()
    {
        EnsureSession();
        if (!_started && _allowConnect)
        {
            _session!.Start();
            _started = true;
        }
    }

    private void BeginLogin(string? username, string? password)
    {
        if (!_allowConnect) throw new InvalidOperationException("诊断模式未启用网络连接");
        var settings = Configurations.Instance.Settings;
        if (username is null && string.IsNullOrEmpty(settings.SteamRefreshToken))
            throw new InvalidOperationException("没有可复用的登录凭据，请先登录 Steam");
        if (Volatile.Read(ref _busy) != 0) return;
        if (_session?.IsLoggedOn == true)
        {
            if (username is null) return;
            Shutdown();
        }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        try { StartSession(); }
        catch { Interlocked.Exchange(ref _busy, 0); throw; }
        var session = _session!;
        var lifetime = _lifetime;
        var savedUsername = settings.SteamUsername;
        var savedToken = settings.SteamRefreshToken;
        _operationError = null;
        _ = Task.Run(async () =>
        {
            try
            {
                var success = username is not null
                    ? await session.LoginAsync(username, password!)
                    : await session.LoginWithTokenAsync(savedUsername, savedToken);
                if (lifetime == _lifetime)
                    _operationError = success ? null : session.LoginError;
            }
            catch (Exception ex)
            {
                if (lifetime == _lifetime) _operationError = ex.Message;
            }
            finally
            {
                if (lifetime == _lifetime) Interlocked.Exchange(ref _busy, 0);
            }
        });
    }

    private void Configure(JsonElement json)
    {
        var config = Configurations.Instance.Settings;
        if (json.TryGetProperty("enableSteamSync", out var enabled)) config.EnableSteamSync = enabled.GetBoolean();
        if (json.TryGetProperty("showArtistName", out var artist)) config.ShowArtistName = artist.GetBoolean();
        if (json.TryGetProperty("showProgressBar", out var progress)) config.ShowProgressBar = progress.GetBoolean();
        if (json.TryGetProperty("showPausedStatus", out var paused)) config.ShowPausedStatus = paused.GetBoolean();
        if (json.TryGetProperty("enableCustomPrefix", out var prefixEnabled)) config.EnableCustomPrefix = prefixEnabled.GetBoolean();
        if (json.TryGetProperty("customPrefix", out var prefix))
        {
            var text = prefix.GetString() ?? "";
            config.CustomPrefix = text.Length > 128 ? text[..128] : text;
        }
        if (json.TryGetProperty("statusPriority", out var priority))
            config.StatusPriority = priority.GetString() == "ProgressBar" ? SteamStatusPriority.ProgressBar : SteamStatusPriority.Artist;
        Configurations.Instance.Save();
        if (!config.EnableSteamSync) _status?.ClearStatus();
        else if (_allowConnect)
        {
            StartSession();
            if (!_session!.IsLoggedOn && Volatile.Read(ref _busy) == 0 && !string.IsNullOrEmpty(config.SteamRefreshToken))
                BeginLogin(null, null);
        }
    }

    private void UpdatePlayback(JsonElement request)
    {
        if (!request.TryGetProperty("song", out var song) || song.ValueKind == JsonValueKind.Null)
            _song = null;
        else
        {
            static string Text(JsonElement json, string name) =>
                json.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
            static double Seconds(JsonElement json, string name) =>
                json.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) &&
                double.IsFinite(number) ? Math.Clamp(number / 1000, 0, 31536000) : 0;
            var duration = Seconds(request, "durationMs");
            _song = new PlayerInfo
            {
                Title = Text(song, "title"), Artists = Text(song, "artists"),
                Duration = duration,
                Schedule = Math.Min(Seconds(request, "currentTimeMs"), duration > 0 ? duration : double.MaxValue),
                Pause = !request.TryGetProperty("paused", out var paused) || paused.GetBoolean()
            };
        }
        var settings = Configurations.Instance.Settings;
        if (_song is null || !settings.EnableSteamSync || (_song.Value.Pause && !settings.ShowPausedStatus))
            _status?.ClearStatus();
        else
            _status?.UpdateStatusAsync(_song.Value).GetAwaiter().GetResult();
    }

    private object State()
    {
        var config = Configurations.Instance.Settings;
        return new
        {
            ok = true,
            connected = _session?.IsConnected ?? false,
            loggedOn = _session?.IsLoggedOn ?? false,
            busy = Volatile.Read(ref _busy) != 0,
            username = config.SteamUsername,
            hasToken = !string.IsNullOrEmpty(config.SteamRefreshToken),
            guard = _challenge,
            error = Configurations.Instance.StorageError ??
                (_session?.IsLoggedOn == true ? null : _operationError ?? _session?.LoginError),
            preview = _song is null ? "等待播放歌曲" : SteamStatusManager.GetStatusPreview(_song.Value, config),
            settings = new
            {
                config.EnableSteamSync, config.ShowArtistName, config.ShowProgressBar, config.ShowPausedStatus,
                config.EnableCustomPrefix, config.CustomPrefix,
                statusPriority = config.StatusPriority.ToString()
            }
        };
    }

    private void Shutdown()
    {
        _lifetime++;
        _session?.Dispose();
        _session = null;
        _status = null;
        _started = false;
        _challenge = null;
        _operationError = null;
        Interlocked.Exchange(ref _busy, 0);
    }
}
