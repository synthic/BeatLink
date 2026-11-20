using System.Security.Cryptography;
using System.Text;

namespace BeatLink.Web;

/// <summary>
/// Generates a PKCE code verifier and its corresponding code challenge.
/// </summary>
public static class Pkce
{
    /// <summary>
    /// Generates a <c>code_verifier</c> and the matching <c>code_challenge</c>
    /// as specified in RFC 7636.
    /// </summary>
    /// <remarks>
    /// See https://datatracker.ietf.org/doc/html/rfc7636#section-4.1 and
    /// https://datatracker.ietf.org/doc/html/rfc7636#section-4.2
    /// </remarks>
    public static PkcePair Generate(int size = 32)
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(size);
        string verifier = Base64UrlEncode(randomBytes);

        // SHA256.HashData is a static one-shot; nothing to dispose (B17).
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        string challenge = Base64UrlEncode(hash);

        return new PkcePair(challenge, verifier);
    }

    /// <summary>
    /// Computes the S256 challenge for a known verifier. Used by the RFC 7636
    /// Appendix B test vector.
    /// </summary>
    public static string ChallengeFor(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>A PKCE challenge/verifier pair with unambiguous member names (I14).</summary>
public readonly record struct PkcePair(string Challenge, string Verifier);
