using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SteamKit2;

namespace Cato.Infrastructure.Steam.SteamKit;

/// <summary>
/// Long-running service that connects to Steam via SteamKit2,
/// maintains the session, and exposes async query methods.
/// </summary>
public sealed class SteamKitService : ISteamKitService, IHostedService, IDisposable
{
    private readonly SteamSettings _settings;
    private readonly ILogger<SteamKitService> _logger;

    private SteamClient _steamClient = null!;
    private CallbackManager _callbackManager = null!;
    private SteamUser _steamUser = null!;
    private SteamApps _steamApps = null!;
    private SteamUserStats _steamUserStats = null!;

    private Thread? _callbackThread;
    private CancellationTokenSource _cts = new();

    private bool _isRunning;
    private bool _isLoggedOn;

    // Completion sources for async bridging
    private TaskCompletionSource<bool>? _loginTcs;

    public bool IsConnected => _isLoggedOn;

    public SteamKitService(IOptions<SteamSettings> settings, ILogger<SteamKitService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();

        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamApps = _steamClient.GetHandler<SteamApps>()!;
        _steamUserStats = _steamClient.GetHandler<SteamUserStats>()!;

        // Subscribe to essential callbacks
        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

        _isRunning = true;

        // Spin up a dedicated thread for SteamKit2's synchronous pump
        _callbackThread = new Thread(RunCallbackPump)
        {
            Name = "SteamKit2-CallbackPump",
            IsBackground = true
        };
        _callbackThread.Start();

        _steamClient.Connect();
        _logger.LogInformation("SteamKit2: Connecting to Steam network...");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _isRunning = false;
        _cts.Cancel();

        if (_isLoggedOn)
            _steamUser.LogOff();

        _steamClient.Disconnect();
        _callbackThread?.Join(TimeSpan.FromSeconds(5));

        _logger.LogInformation("SteamKit2: Disconnected.");
        return Task.CompletedTask;
    }

    // ── Callback pump ─────────────────────────────────────────────────────────

    private void RunCallbackPump()
    {
        while (_isRunning)
        {
            try
            {
                _callbackManager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                _logger.LogError(ex, "SteamKit2: Error in callback pump");
            }
        }
    }

    // ── Steam callbacks ───────────────────────────────────────────────────────

    private void OnConnected(SteamClient.ConnectedCallback cb)
    {
        _logger.LogInformation("SteamKit2: Connected. Logging in as '{Username}'...", _settings.Username);

        _steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = _settings.Username,
            Password = _settings.Password
        });
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback cb)
    {
        _isLoggedOn = false;
        if (!_isRunning) return;

        _logger.LogWarning("SteamKit2: Disconnected. Reconnecting in 10 seconds...");
        Task.Delay(TimeSpan.FromSeconds(10), _cts.Token)
            .ContinueWith(_ => _steamClient.Connect(), TaskContinuationOptions.NotOnCanceled);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback cb)
    {
        if (cb.Result != EResult.OK)
        {
            _logger.LogError("SteamKit2: Login failed: {Result}", cb.Result);
            _loginTcs?.TrySetException(new InvalidOperationException($"Steam login failed: {cb.Result}"));
            return;
        }

        _isLoggedOn = true;
        _logger.LogInformation("SteamKit2: Logged in successfully as '{Username}'", _settings.Username);
        _loginTcs?.TrySetResult(true);
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback cb)
    {
        _isLoggedOn = false;
        _logger.LogWarning("SteamKit2: Logged off: {Result}", cb.Result);
    }

    // ── ISteamKitService ──────────────────────────────────────────────────────

    public async Task<int?> GetCurrentPlayerCountAsync(int appId, CancellationToken ct = default)
    {
        if (!_isLoggedOn)
        {
            _logger.LogWarning("SteamKit2: Cannot get player count — not logged in.");
            return null;
        }

        try
        {
            var result = await _steamUserStats.GetNumberOfCurrentPlayers((uint)appId)
                .ToTask().WaitAsync(TimeSpan.FromSeconds(15), ct);

            if (result.Result != EResult.OK)
            {
                _logger.LogWarning("SteamKit2: GetNumberOfCurrentPlayers failed for AppId {AppId}: {Result}", appId, result.Result);
                return null;
            }

            return (int)result.NumPlayers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamKit2: Exception getting player count for AppId {AppId}", appId);
            return null;
        }
    }

    public async Task<(IReadOnlyList<uint> AppIds, uint LatestChangeNumber)> GetChangedAppIdsSinceAsync(
        uint sinceChangeNumber,
        CancellationToken ct = default)
    {
        if (!_isLoggedOn)
        {
            _logger.LogWarning("SteamKit2: Cannot get PICS changes — not logged in.");
            return (Array.Empty<uint>(), sinceChangeNumber);
        }

        try
        {
            var job = await _steamApps.PICSGetChangesSince(sinceChangeNumber, sendAppChangelist: true, sendPackageChangelist: false)
                .ToTask().WaitAsync(TimeSpan.FromSeconds(30), ct);

            var appIds = job.AppChanges.Keys.ToList();
            var latestChange = job.CurrentChangeNumber;

            _logger.LogInformation("SteamKit2: PICS changes since {Since}: {Count} apps changed (latest change #{Latest})",
                sinceChangeNumber, appIds.Count, latestChange);

            return (appIds, latestChange);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamKit2: Exception fetching PICS changes since {ChangeNumber}", sinceChangeNumber);
            return (Array.Empty<uint>(), sinceChangeNumber);
        }
    }

    public async Task<SteamPicsAppInfo?> GetAppInfoAsync(uint appId, CancellationToken ct = default)
    {
        if (!_isLoggedOn)
        {
            _logger.LogWarning("SteamKit2: Cannot get app info — not logged in.");
            return null;
        }

        try
        {
            var request = new SteamApps.PICSRequest(appId);
            var result = await _steamApps.PICSGetProductInfo(new[] { request }, Enumerable.Empty<SteamApps.PICSRequest>())
                .ToTask().WaitAsync(TimeSpan.FromSeconds(20), ct);

            var appInfo = result.Results?.FirstOrDefault()?.Apps.GetValueOrDefault(appId);
            if (appInfo is null) return null;

            var kv = appInfo.KeyValues;
            var common = kv["common"];

            var name = common["name"].Value ?? string.Empty;
            var type = common["type"].Value?.ToLowerInvariant() ?? string.Empty;
            var releaseState = common["releasestate"].Value?.ToLowerInvariant() ?? string.Empty;

            DateOnly? releaseDate = null;
            var releaseDateRaw = common["steam_release_date"].Value;
            if (long.TryParse(releaseDateRaw, out var unixSeconds) && unixSeconds > 0)
                releaseDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime);

            return new SteamPicsAppInfo(appId, name, type, releaseState, releaseDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SteamKit2: Exception fetching app info for AppId {AppId}", appId);
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}
