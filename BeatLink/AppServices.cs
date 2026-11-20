using BeatLink.Game;
using BeatLink.Web;

namespace BeatLink;

/// <summary>
/// Composition root: builds the options, token store and paths from the current
/// environment. Centralises configuration loading (I1) so the window stays thin.
/// </summary>
public sealed class AppServices
{
    public BeatLinkOptions Options { get; }
    public TokenStore TokenStore { get; }
    public string ExpectedBytesPath { get; }

    private AppServices(BeatLinkOptions options, TokenStore tokenStore, string expectedBytesPath)
    {
        Options = options;
        TokenStore = tokenStore;
        ExpectedBytesPath = expectedBytesPath;
    }

    public static AppServices Create(IReadOnlyList<string> args)
    {
        string configPath = Path.Combine(TokenStore.DefaultDirectory, "beatlink.json");
        BeatLinkOptions options = BeatLinkOptions.Load(configPath, args);
        var tokenStore = new TokenStore(TokenStore.DefaultPath, new DpapiTokenProtector());
        string expectedPath = Path.Combine(TokenStore.DefaultDirectory, "expected-bytes.json");
        return new AppServices(options, tokenStore, expectedPath);
    }

    public ExpectedBytes LoadExpectedBytes() => ExpectedBytes.LoadOrEmpty(ExpectedBytesPath);
}
