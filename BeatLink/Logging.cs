using System.Text.RegularExpressions;
using BeatLink.Web;

namespace BeatLink;

/// <summary>
/// Minimal append-only logger writing to %LOCALAPPDATA%\BeatLink\beatlink.log,
/// with access tokens redacted so support logs never carry a credential (I13).
/// </summary>
public static partial class Log
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(TokenStore.DefaultDirectory, "beatlink.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Exception(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(TokenStore.DefaultDirectory);
                string line = $"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fffZ} [{level}] {Redact(message)}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take down the app.
        }
    }

    /// <summary>Redacts anything that looks like a bearer token or a long opaque secret.</summary>
    internal static string Redact(string input)
    {
        string s = BearerRegex().Replace(input, "Bearer [redacted]");
        s = LongTokenRegex().Replace(s, "[redacted]");
        return s;
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    // Opaque token-ish runs (Discord tokens, base64url blobs) of 20+ chars.
    [GeneratedRegex(@"\b[A-Za-z0-9\-_]{20,}\.[A-Za-z0-9\-_]{5,}\.[A-Za-z0-9\-_]{20,}\b")]
    private static partial Regex LongTokenRegex();
}
