using System.Text;

namespace BeatLink.Web;

/// <summary>
/// Builds the OAuth2 authorization request URL. Every value is percent-encoded
/// (B13), and a <c>state</c> parameter is always included (S2).
/// </summary>
public static class AuthorizeUrl
{
    public static string Build(
        string authorizeEndpoint,
        string clientId,
        string codeChallenge,
        string scope,
        string redirectUri,
        string state)
    {
        var query = new StringBuilder();
        Append(query, "client_id", clientId);
        Append(query, "response_type", "code");
        Append(query, "code_challenge", codeChallenge);
        Append(query, "code_challenge_method", "S256");
        Append(query, "scope", scope);
        Append(query, "redirect_uri", redirectUri);
        Append(query, "state", state);

        return $"{authorizeEndpoint}?{query}";
    }

    private static void Append(StringBuilder sb, string key, string value)
    {
        if (sb.Length > 0)
            sb.Append('&');
        sb.Append(Uri.EscapeDataString(key))
          .Append('=')
          .Append(Uri.EscapeDataString(value));
    }
}
