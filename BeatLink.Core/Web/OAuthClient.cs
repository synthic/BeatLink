using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BeatLink.Models;

namespace BeatLink.Web;

/// <summary>
/// Drives the Discord OAuth2 PKCE login over a loopback redirect, returning a
/// <see cref="LoginResult"/> instead of throwing (I5, B2).
///
/// Fixes: B1 (cancellation + timeout, no permanent hang), B2 (all failures map
/// to a result), B11 (loops until a request carries a matching state with code
/// or error; reports the real outcome), B12 (awaits the token exchange),
/// B13 (encoded authorize URL), B17 (listener and SHA in using/one-shot; one
/// shared HttpClient), S2 (state, constant-time compared), I9 (HTML result
/// page). Unrelated local requests are ignored rather than ending the flow.
/// </summary>
public sealed class OAuthClient
{
    // One HttpClient for the process lifetime (B17).
    private static readonly HttpClient Http = new();

    private readonly BeatLinkOptions _options;
    private readonly TokenStore _tokenStore;
    private readonly Action<string> _launchBrowser;

    public OAuthClient(BeatLinkOptions options, TokenStore tokenStore, Action<string>? launchBrowser = null)
    {
        _options = options;
        _tokenStore = tokenStore;
        _launchBrowser = launchBrowser ?? DefaultLaunchBrowser;
    }

    /// <summary>
    /// Runs the full login flow. Never throws for an expected failure; returns a
    /// <see cref="LoginResult"/> describing the outcome.
    /// </summary>
    public async Task<LoginResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            _options.EnsureLoginable();
        }
        catch (InvalidOperationException ex)
        {
            return LoginResult.Failed(ex.Message);
        }

        PkcePair pkce = Pkce.Generate();
        string state = GenerateState();

        using var http = new HttpListener();
        http.Prefixes.Add(_options.RedirectUri);

        try
        {
            http.Start();
        }
        catch (HttpListenerException ex)
        {
            return LoginResult.Failed(
                $"Could not start the local login listener on port {_options.RedirectPort}. " +
                $"It may already be in use. ({ex.Message})");
        }

        try
        {
            string url = AuthorizeUrl.Build(
                _options.AuthorizeUrl, _options.ClientId, pkce.Challenge,
                _options.Scope, _options.RedirectUri, state);

            try
            {
                _launchBrowser(url);
            }
            catch (Exception ex)
            {
                return LoginResult.Failed($"Could not open a web browser. ({ex.Message})");
            }

            // Loop until a callback carries our state with a code or an error.
            // Unrelated local requests get a 404 and are ignored (B11).
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await GetContextAsync(http, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return LoginResult.Cancelled("Login was cancelled.");
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
                {
                    return LoginResult.Cancelled("Login was cancelled.");
                }

                HttpListenerRequest request = context.Request;

                // The redirect lands on the registered URI's path (the Beat
                // Revival app registers the root, "http://localhost:14000/"), so
                // the OAuth callback is identified by its query, not its path.
                string? returnedState = request.QueryString.Get("state");
                string? error = request.QueryString.Get("error");
                string? code = request.QueryString.Get("code");

                if (request.HttpMethod != "GET" || (code is null && error is null))
                {
                    // Unrelated local request (e.g. a favicon fetch); ignore it.
                    await RespondAsync(context, HttpStatusCode.NotFound, NotFoundPage).ConfigureAwait(false);
                    continue;
                }

                if (!FixedTimeEquals(returnedState, state))
                {
                    await RespondAsync(context, HttpStatusCode.BadRequest, MismatchPage).ConfigureAwait(false);
                    continue;
                }

                if (error is not null)
                {
                    bool denied = error.Equals("access_denied", StringComparison.OrdinalIgnoreCase);
                    await RespondAsync(context, HttpStatusCode.OK,
                        denied ? DeniedPage : FailedPage).ConfigureAwait(false);
                    return denied
                        ? LoginResult.Denied("Consent was denied.")
                        : LoginResult.Failed($"Authorization failed: {error}");
                }

                if (string.IsNullOrEmpty(code))
                {
                    await RespondAsync(context, HttpStatusCode.BadRequest, FailedPage).ConfigureAwait(false);
                    return LoginResult.Failed("Authorization response contained no code.");
                }

                LoginResult result = await ExchangeAsync(code, pkce.Verifier, cancellationToken)
                    .ConfigureAwait(false);

                await RespondAsync(context, HttpStatusCode.OK,
                    result.IsSuccess ? SuccessPage : FailedPage).ConfigureAwait(false);
                return result;
            }
        }
        finally
        {
            if (http.IsListening)
                http.Stop();
        }
    }

    private async Task<LoginResult> ExchangeAsync(string code, string verifier, CancellationToken ct)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["code_verifier"] = verifier,
        };

        try
        {
            using var content = new FormUrlEncodedContent(parameters);
            using HttpResponseMessage response =
                await Http.PostAsync(_options.TokenUrl, content, ct).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return LoginResult.Failed($"Token exchange failed ({(int)response.StatusCode}).");

            TokenResponse token = _tokenStore.Write(body);
            return LoginResult.Success(token);
        }
        catch (OperationCanceledException)
        {
            return LoginResult.Cancelled("Login was cancelled.");
        }
        catch (HttpRequestException ex)
        {
            return LoginResult.Failed($"Network error during token exchange. ({ex.Message})");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return LoginResult.Failed($"The token response could not be read. ({ex.Message})");
        }
    }

    private static async Task<HttpListenerContext> GetContextAsync(
        HttpListener listener, CancellationToken ct)
    {
        Task<HttpListenerContext> contextTask = listener.GetContextAsync();
        // HttpListener.GetContextAsync has no cancellation overload; race it
        // against the token so a cancel or timeout actually ends the wait (B1).
        var tcs = new TaskCompletionSource();
        using (ct.Register(() => tcs.TrySetResult()))
        {
            Task completed = await Task.WhenAny(contextTask, tcs.Task).ConfigureAwait(false);
            if (completed != contextTask)
            {
                // Abandoning the listener; observe its eventual fault so it does
                // not surface as an unobserved task exception when http.Stop runs.
                _ = contextTask.ContinueWith(t => _ = t.Exception,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                ct.ThrowIfCancellationRequested();
            }
        }
        return await contextTask.ConfigureAwait(false);
    }

    private static async Task RespondAsync(HttpListenerContext context, HttpStatusCode status, string html)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The browser may have gone away; nothing more to do.
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { /* ignore */ }
        }
    }

    private static string GenerateState() =>
        Pkce.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
            return false;
        byte[] ba = Encoding.ASCII.GetBytes(a);
        byte[] bb = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static void DefaultLaunchBrowser(string url) =>
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

    private const string PageHead =
        "<!doctype html><meta charset=\"utf-8\"><meta name=\"color-scheme\" content=\"light dark\">" +
        "<title>BeatLink</title>" +
        "<style>body{font-family:system-ui,sans-serif;margin:4rem auto;max-width:28rem;" +
        "text-align:center;padding:0 1rem}h1{font-size:1.4rem}</style>";

    private const string SuccessPage = PageHead +
        "<h1>You're logged in.</h1><p>You can close this tab and return to BeatLink.</p>";
    private const string DeniedPage = PageHead +
        "<h1>Login cancelled.</h1><p>Consent was not granted. You can close this tab and try again in BeatLink.</p>";
    private const string FailedPage = PageHead +
        "<h1>Login failed.</h1><p>Something went wrong. You can close this tab and try again in BeatLink.</p>";
    private const string MismatchPage = PageHead +
        "<h1>Unexpected request.</h1><p>This request did not match the current login. You can close this tab.</p>";
    private const string NotFoundPage = PageHead +
        "<h1>Not found.</h1><p>This address is not part of the BeatLink login.</p>";
}
