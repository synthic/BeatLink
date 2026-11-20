using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatLink;

/// <summary>
/// All externally configurable settings for BeatLink. Resolves I1 and B14:
/// the server host, Discord client id, OAuth scope and redirect port are no
/// longer compiled-in constants, so a source build can log in and a custom
/// server can be targeted without recompiling.
///
/// Resolution order (lowest to highest precedence):
///   1. The compiled-in <see cref="Defaults"/>.
///   2. A <c>beatlink.json</c> file placed beside the token store.
///   3. Command-line overrides (e.g. <c>--server</c>).
/// </summary>
public sealed record BeatLinkOptions
{
    /// <summary>Discord OAuth2 authorize endpoint.</summary>
    public string AuthorizeUrl { get; init; } = "https://discord.com/oauth2/authorize";

    /// <summary>Discord OAuth2 token endpoint.</summary>
    public string TokenUrl { get; init; } = "https://discord.com/api/oauth2/token";

    /// <summary>
    /// Discord application (public) client id. Empty by default; login fails
    /// with a clear message until it is set (B14).
    /// </summary>
    public string ClientId { get; init; } = "";

    /// <summary>OAuth scope to request. Only "identify" is needed (S7).</summary>
    public string Scope { get; init; } = "identify";

    /// <summary>
    /// Hostname the Beat Revival redirector is reached on. Previously hardcoded
    /// to "localhost" in the patcher (B14); the live service is
    /// redirector.ploxxy.dev. Because the token is sent to this host with SSL
    /// disabled, a non-loopback host makes S1 a real network exposure.
    /// </summary>
    public string ServerHost { get; init; } = "redirector.ploxxy.dev";

    /// <summary>Loopback port the OAuth redirect listener binds to.</summary>
    public int RedirectPort { get; init; } = 14000;

    /// <summary>
    /// Opt-in replacement for the old <c>NO_ENCRYPTION</c> compile-time switch:
    /// when true, the patcher also applies the request-encryption bypass. Off by
    /// default (matching the "No longer disable encryption by default" change).
    /// </summary>
    public bool DisableEncryption { get; init; } = false;

    /// <summary>
    /// Loopback redirect URI registered with Discord. RFC 8252 recommends the
    /// IP literal over "localhost" (S6). The fixed <c>/callback</c> path lets
    /// the listener reject unrelated local requests (S6, B11).
    /// </summary>
    [JsonIgnore]
    public string RedirectUri => $"http://127.0.0.1:{RedirectPort}/callback/";

    /// <summary>Compiled-in defaults.</summary>
    public static BeatLinkOptions Defaults { get; } = new();

    /// <summary>
    /// Loads options by layering <c>beatlink.json</c> (if present and valid)
    /// over the defaults, then applying command-line overrides. A missing or
    /// malformed file falls back to defaults rather than throwing, so a user
    /// can always start the app.
    /// </summary>
    public static BeatLinkOptions Load(string? configPath, IReadOnlyList<string> args)
    {
        BeatLinkOptions options = Defaults;

        if (configPath is not null && File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                BeatLinkOptions? fromFile = JsonSerializer.Deserialize(
                    json, OptionsJsonContext.Default.BeatLinkOptions);
                if (fromFile is not null)
                    options = fromFile;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Keep defaults on a bad or unreadable config file.
            }
        }

        return options.ApplyArguments(args);
    }

    /// <summary>
    /// Applies recognised command-line overrides. Currently <c>--server
    /// &lt;host&gt;</c>. Unknown arguments are ignored so other launch flags
    /// (e.g. <c>--token</c>) still pass through.
    /// </summary>
    public BeatLinkOptions ApplyArguments(IReadOnlyList<string> args)
    {
        BeatLinkOptions result = this;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase))
            {
                string host = args[i + 1];
                if (!string.IsNullOrWhiteSpace(host))
                    result = result with { ServerHost = host };
            }
        }
        return result;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with a user-facing
    /// message when the configuration cannot support a login (B14).
    /// </summary>
    public void EnsureLoginable()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException(
                "No Discord client id is configured. Set \"ClientId\" in beatlink.json " +
                "next to the app before logging in.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BeatLinkOptions))]
internal partial class OptionsJsonContext : JsonSerializerContext;
