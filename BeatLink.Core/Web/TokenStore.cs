using System.Text;
using System.Text.Json;
using BeatLink.Models;

namespace BeatLink.Web;

/// <summary>
/// Reads and writes the stored OAuth token.
///
/// Fixes:
///   B10 - stored under %LOCALAPPDATA%\BeatLink\ rather than the working
///         directory, so launching from a shortcut or a read-only install
///         folder no longer loses or fails the login.
///   B21 - a null deserialisation result fails the load instead of writing the
///         literal text "null"; only the expected exceptions are swallowed so a
///         corrupt file or a permissions error is distinguishable from
///         "not logged in".
///   S3  - the token bytes are encrypted via <see cref="ITokenProtector"/>
///         (DPAPI on Windows) before hitting disk.
///   S7  - <see cref="Clear"/> deletes the stored token for Log Out.
/// </summary>
public sealed class TokenStore
{
    private readonly string _path;
    private readonly ITokenProtector _protector;

    public TokenStore(string path, ITokenProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    /// <summary>The default token path: %LOCALAPPDATA%\BeatLink\token.json.</summary>
    public static string DefaultPath =>
        Path.Combine(DefaultDirectory, "token.json");

    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BeatLink");

    /// <summary>The outcome of reading the token file.</summary>
    public enum ReadStatus { Ok, NotPresent, Unreadable }

    /// <summary>
    /// Attempts to read and decrypt the stored token. Distinguishes "no token
    /// yet" from "a token exists but could not be read" (B21).
    /// </summary>
    public (ReadStatus Status, TokenResponse? Token, string? Error) TryRead()
    {
        if (!File.Exists(_path))
            return (ReadStatus.NotPresent, null, null);

        try
        {
            byte[] ciphertext = File.ReadAllBytes(_path);
            byte[] plaintext = _protector.Unprotect(ciphertext);
            string json = Encoding.UTF8.GetString(plaintext);

            TokenResponse? token = JsonSerializer.Deserialize(
                json, SourceGenerationContext.Default.TokenResponse);

            if (token is null || token.IsEmpty)
                return (ReadStatus.Unreadable, null, "Stored token was empty or invalid.");

            return (ReadStatus.Ok, token, null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or FormatException
                or System.Security.Cryptography.CryptographicException)
        {
            return (ReadStatus.Unreadable, null, ex.Message);
        }
    }

    /// <summary>
    /// Convenience read that returns an empty token for both "not present" and
    /// "unreadable". Prefer <see cref="TryRead"/> when the caller needs to tell
    /// the two apart.
    /// </summary>
    public TokenResponse Read() => TryRead().Token ?? TokenResponse.Empty;

    /// <summary>
    /// Parses a token exchange response, stamps its absolute expiry, and writes
    /// it encrypted. Returns the parsed token. Throws on a null/invalid payload
    /// so login fails loudly instead of persisting "null" (B21).
    /// </summary>
    public TokenResponse Write(string jsonResponse)
    {
        TokenResponse? token;
        try
        {
            token = JsonSerializer.Deserialize(
                jsonResponse, SourceGenerationContext.Default.TokenResponse);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Token response was not valid JSON.", ex);
        }

        if (token is null || token.IsEmpty)
            throw new InvalidOperationException("Token response did not contain an access token.");

        token.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        WriteToken(token);
        return token;
    }

    public void WriteToken(TokenResponse token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        string json = JsonSerializer.Serialize(token, SourceGenerationContext.Default.TokenResponse);
        byte[] plaintext = Encoding.UTF8.GetBytes(json);
        byte[] ciphertext = _protector.Protect(plaintext);

        File.WriteAllBytes(_path, ciphertext);
    }

    /// <summary>Deletes the stored token (Log Out, S7). Safe to call when absent.</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a token that cannot be deleted still expires.
        }
    }
}
