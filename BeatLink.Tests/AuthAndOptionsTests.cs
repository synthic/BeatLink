using BeatLink.Web;

namespace BeatLink.Tests;

public static class AuthorizeUrlTests
{
    [Test("Authorize URL encodes values and includes state + S256")]
    public static void BuildsEncodedUrl()
    {
        string url = AuthorizeUrl.Build(
            "https://discord.com/oauth2/authorize",
            clientId: "123",
            codeChallenge: "abc",
            scope: "identify email",
            redirectUri: "http://127.0.0.1:14000/callback/",
            state: "xy z");

        Assert.True(url.Contains("response_type=code"), "missing response_type");
        Assert.True(url.Contains("code_challenge_method=S256"), "missing method");
        Assert.True(url.Contains("scope=identify%20email"), "scope not encoded");
        Assert.True(url.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A14000%2Fcallback%2F"), "redirect not encoded");
        Assert.True(url.Contains("state=xy%20z"), "state not encoded");
    }
}

public static class OptionsTests
{
    [Test("Defaults are loginable only once a client id is set (B14)")]
    public static void EnsureLoginable()
    {
        Assert.Throws<InvalidOperationException>(() => BeatLinkOptions.Defaults.EnsureLoginable());
        (BeatLinkOptions.Defaults with { ClientId = "123" }).EnsureLoginable(); // no throw
    }

    [Test("--server overrides the host (I1)")]
    public static void ServerArgument()
    {
        BeatLinkOptions o = BeatLinkOptions.Defaults.ApplyArguments(new[] { "--server", "play.example.com" });
        Assert.Equal("play.example.com", o.ServerHost);
    }

    [Test("RedirectUri uses the loopback IP literal and /callback (S6)")]
    public static void RedirectUriShape()
    {
        Assert.Equal("http://127.0.0.1:14000/callback/", BeatLinkOptions.Defaults.RedirectUri);
    }

    [Test("Default scope is identify only (S7)")]
    public static void ScopeIsIdentify()
    {
        Assert.Equal("identify", BeatLinkOptions.Defaults.Scope);
    }

    [Test("A missing config file falls back to defaults")]
    public static void LoadMissingConfig()
    {
        BeatLinkOptions o = BeatLinkOptions.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.json"), Array.Empty<string>());
        Assert.Equal(BeatLinkOptions.Defaults.ServerHost, o.ServerHost);
    }
}
