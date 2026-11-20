using System.ComponentModel;
using BeatLink.Game;
using BeatLink.Models;
using BeatLink.Web;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace BeatLink;

public sealed partial class MainWindow : Window
{
    // Treat a token as expired this long before its real expiry so we never
    // connect with one that dies mid-handshake (I12).
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);

    private readonly AppServices _services;
    private readonly TokenResponse? _injectedToken;
    private readonly DispatcherQueueTimer _gameTimer;

    private CancellationTokenSource? _loginCts;
    private bool _dialogShowing;      // B18 re-entry guard
    private bool _connected;          // drives the connect tick
    private GameProcess? _connectedGame;

    public MainWindow()
    {
        InitializeComponent();

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/AppIcon.ico");
        AppWindow.SetIcon(iconPath);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindowTitleBar.IsCustomizationSupported())
            AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.Black;

        const int windowWidth = 640, windowHeight = 480;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = windowWidth;
            presenter.PreferredMinimumHeight = windowHeight;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(windowWidth, windowHeight));

        string[] args = Environment.GetCommandLineArgs();
        _services = AppServices.Create(args);
        _injectedToken = ResolveInjectedToken(args);
        ServerBox.Text = _services.Options.ServerHost;

        // Poll for the game so Connect enables itself when it is available (I8).
        _gameTimer = this.DispatcherQueue.CreateTimer();
        _gameTimer.Interval = TimeSpan.FromSeconds(2);
        _gameTimer.Tick += (s, e) => UpdateState();
        _gameTimer.Start();

        Closed += (s, e) => _loginCts?.Cancel();

        UpdateState();

        if (HasFlag(args, "--capture"))
            _ = CaptureExpectedBytesAsync();
    }

    /// <summary>
    /// Setup step: with an unpatched game running, record this machine's original
    /// bytes at every patch site so Connect can verify the build (B4). Invoked via
    /// the <c>--capture</c> launch flag.
    /// </summary>
    private async Task CaptureExpectedBytesAsync()
    {
        StatusText.Text = "Capturing expected bytes from the running game…";
        try
        {
            BeatLinkOptions options = CurrentOptions();
            string build = $"capture-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            ExpectedBytes captured = await Task.Run(() => GameConnection.Capture(options, build));
            captured.Save(_services.ExpectedBytesPath);
            StatusText.Text = $"Captured expected bytes ({captured.Hex.Count} sites). You can now Connect.";
            Log.Info($"Captured expected bytes: {captured.Hex.Count} sites, build {build}.");
        }
        catch (GameNotRunningException ex)
        {
            await ShowDialogAsync("Game not found", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Exception("CaptureExpectedBytesAsync", ex);
            await ShowDialogAsync("Capture failed", ex.Message);
        }
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    // ---- Login (B1, B2, I7) -------------------------------------------------

    private async void LogInButton_Click(object sender, RoutedEventArgs e)
    {
        // While a login is in flight the button acts as Cancel (I7, B1).
        if (_loginCts is not null)
        {
            _loginCts.Cancel();
            return;
        }

        var cts = new CancellationTokenSource(LoginTimeout);
        _loginCts = cts;
        LogInButton.Content = "CANCEL";
        StatusText.Text = "Waiting for browser…";

        try
        {
            BeatLinkOptions options = CurrentOptions();
            var client = new OAuthClient(options, _services.TokenStore);
            LoginResult result = await client.GetTokenAsync(cts.Token);

            StatusText.Text = result.Outcome switch
            {
                LoginOutcome.Success => "Logged in.",
                LoginOutcome.Denied => "Login cancelled: consent was denied.",
                LoginOutcome.Cancelled => "Login cancelled.",
                _ => result.Message ?? "Login failed.",
            };

            if (result.Outcome == LoginOutcome.Failed)
            {
                Log.Error($"Login failed: {result.Message}");
                await ShowDialogAsync("Login failed", result.Message ?? "Something went wrong.");
            }
        }
        catch (Exception ex)
        {
            // Safety net so a login failure can never crash the app (B2).
            Log.Exception("LogInButton_Click", ex);
            StatusText.Text = "Login failed.";
            await ShowDialogAsync("Login failed", ex.Message);
        }
        finally
        {
            _loginCts = null;
            cts.Dispose();
            LogInButton.Content = "LOG IN";
            UpdateState();
        }
    }

    // ---- Connect (B2, B5, B15, I6, I8) -------------------------------------

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        TokenResponse? token = EffectiveToken();
        if (token is null || !IsUsable(token))
        {
            await ShowDialogAsync("Session expired", "Your session has expired. Please log in again.");
            return;
        }

        ConnectButton.IsEnabled = false;
        StatusText.Text = "Connecting…";

        try
        {
            BeatLinkOptions options = CurrentOptions();
            ExpectedBytes expected = _services.LoadExpectedBytes();

            ConnectOutcome outcome = await Task.Run(
                () => GameConnection.Connect(options, token.AccessToken, expected));

            if (outcome.Result.Ok)
            {
                AttachGame(outcome.Game);
                _connected = true;
                StatusText.Text = outcome.Result.Status == ApplyStatus.AlreadyApplied
                    ? "Already connected."
                    : "Connected.";
            }
            else
            {
                outcome.Game.Dispose();
                string message = outcome.Result.Message ?? "Could not patch the game.";
                Log.Error($"Connect failed: {outcome.Result.Status}: {message}");
                StatusText.Text = "Connect failed.";
                await ShowDialogAsync("Connect failed", message);
            }
        }
        catch (GameNotRunningException ex)
        {
            StatusText.Text = "Game not running.";
            await ShowDialogAsync("Game not found", ex.Message);
        }
        catch (PatchBuildException ex)
        {
            Log.Exception("Connect patch build", ex);
            StatusText.Text = "Connect failed.";
            await ShowDialogAsync("Connect failed", ex.Message);
        }
        catch (Win32Exception ex)
        {
            Log.Exception("Connect Win32", ex);
            StatusText.Text = "Connect failed.";
            await ShowDialogAsync("Connect failed", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Exception("ConnectButton_Click", ex);
            StatusText.Text = "Connect failed.";
            await ShowDialogAsync("Connect failed", ex.Message);
        }
        finally
        {
            UpdateState();
        }
    }

    // ---- Log out (S7) -------------------------------------------------------

    private void LogOutButton_Click(object sender, RoutedEventArgs e)
    {
        _services.TokenStore.Clear();
        _connected = false;
        DetachGame();
        StatusText.Text = "Logged out.";
        // Note: Discord token revocation (the network call) is a small follow-up;
        // deleting the local token is the security-relevant part (S7).
        UpdateState();
    }

    // ---- Shared state (B16) -------------------------------------------------

    /// <summary>
    /// Single source of truth for the UI's ticks and enabled state (B16). Both
    /// ticks derive from here; the connect tick is cleared on game exit via
    /// <see cref="AttachGame"/>.
    /// </summary>
    private void UpdateState()
    {
        TokenResponse? token = EffectiveToken();
        bool loggedIn = token is not null && IsUsable(token);

        LogInIcon.Opacity = loggedIn ? 1.0 : 0.0;
        if (loggedIn)
        {
            string expiry = $"Logged in – session valid until {token!.ExpiresAt.ToLocalTime():g}";
            AutomationProperties.SetName(LogInIcon, expiry);
            ToolTipService.SetToolTip(LogInIcon, expiry);
        }
        else
        {
            AutomationProperties.SetName(LogInIcon, "Not logged in");
            ToolTipService.SetToolTip(LogInIcon, "Not logged in");
        }

        ConnectIcon.Opacity = _connected ? 1.0 : 0.0;
        AutomationProperties.SetName(ConnectIcon, _connected ? "Connected to game" : "Not connected");

        bool loginInFlight = _loginCts is not null;
        ConnectButton.IsEnabled = loggedIn && !loginInFlight && GameProcess.IsRunning();
        LogOutButton.IsEnabled = loggedIn && !loginInFlight;
    }

    private void AttachGame(GameProcess game)
    {
        DetachGame();
        _connectedGame = game;
        game.Exited += OnGameExited; // clear the connect tick when the game quits (B16)
    }

    private void DetachGame()
    {
        if (_connectedGame is null) return;
        _connectedGame.Exited -= OnGameExited;
        _connectedGame.Dispose();
        _connectedGame = null;
    }

    private void OnGameExited(object? sender, EventArgs e)
    {
        // Marshal back to the UI thread before touching XAML.
        this.DispatcherQueue.TryEnqueue(() =>
        {
            _connected = false;
            DetachGame();
            UpdateState();
        });
    }

    // ---- Helpers ------------------------------------------------------------

    private BeatLinkOptions CurrentOptions()
    {
        string host = string.IsNullOrWhiteSpace(ServerBox.Text)
            ? _services.Options.ServerHost
            : ServerBox.Text.Trim();
        return _services.Options with { ServerHost = host };
    }

    private TokenResponse? EffectiveToken()
    {
        if (_injectedToken is not null)
            return _injectedToken;
        (TokenStore.ReadStatus status, TokenResponse? token, _) = _services.TokenStore.TryRead();
        return status == TokenStore.ReadStatus.Ok ? token : null;
    }

    private static bool IsUsable(TokenResponse token) =>
        token.ExpiresAt > DateTimeOffset.UtcNow.Add(ExpirySkew);

    /// <summary>
    /// Resolves a token supplied for debugging. Reads BEATLINK_TOKEN from the
    /// environment (all builds); the <c>--token</c> command-line form is debug
    /// only, since a command line is readable by other processes (S4).
    /// </summary>
    private static TokenResponse? ResolveInjectedToken(string[] args)
    {
        string? value = Environment.GetEnvironmentVariable("BEATLINK_TOKEN");

#if DEBUG
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--token", StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                break;
            }
        }
#endif

        if (string.IsNullOrWhiteSpace(value))
            return null;

        return new TokenResponse
        {
            AccessToken = value,
            TokenType = "Bearer",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
    }

    /// <summary>
    /// Shows a dialog, awaitable, with a re-entry guard so a second dialog never
    /// throws while one is open (B18).
    /// </summary>
    private async Task ShowDialogAsync(string title, string content)
    {
        if (_dialogShowing)
            return;

        _dialogShowing = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Exception("ShowDialogAsync", ex);
        }
        finally
        {
            _dialogShowing = false;
        }
    }
}
