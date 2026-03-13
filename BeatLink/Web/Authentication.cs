using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using BeatLink.Models;

namespace BeatLink.Web;

public static class Authentication
{
    /// <summary>
    /// Starts a local HTTP server for receiving redirect requests then launches a browser for the user to login.
    /// </summary>
    public static async Task GetTokenAsync()
    {
        // OAuth2 provider details
        var provider_auth_url = "https://discord.com/oauth2/authorize";
        var provider_token_url = "https://discord.com/api/oauth2/token";
        var client_id = "";
        var scope = "";

        var (code_challenge, code_verifier) = Pkce.Generate();

        // Local server details
        var redirect_uri = "http://localhost:14000/";

        // Create new HTTP listener
        var http = new HttpListener();
        http.Prefixes.Add(redirect_uri);
        http.Start();

        // Build authorization URL
        var url = string.Format("{0}?client_id={1}&code_challenge={2}&scope={3}&redirect_uri={4}&code_challenge_method=S256&response_type=code",
            provider_auth_url,
            client_id,
            code_challenge,
            scope,
            redirect_uri);

        // Launch browser
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });

        // Wait for authorization response
        var context = await http.GetContextAsync();

        // Perform code exchange
        if (context.Request.QueryString.Get("code") is string code and not null)
        {
            HttpClient client = new();

            var requestUri = provider_token_url;
            var requestParams = new Dictionary<string, string>
            {
                { "client_id", client_id },
                { "grant_type", "authorization_code" },
                { "code", code },
                { "redirect_uri", redirect_uri },
                { "code_verifier", code_verifier }
            };

            var content = new FormUrlEncodedContent(requestParams);
            var response = client.PostAsync(requestUri, content).GetAwaiter().GetResult();
            var responseString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (response.IsSuccessStatusCode) WriteTokenFile(responseString);
        }

        // Send response to browser
        byte[] buffer = Encoding.UTF8.GetBytes("Please return to the app.");

        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        context.Response.StatusCode = (int)HttpStatusCode.OK;

        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ContinueWith((task) =>
        {
            context.Response.OutputStream.Close();
            http.Stop();
        });
    }

    /// <summary>
    /// Deserializes and adds token expiration time to a JSON response then writes it to a file.
    /// </summary>
    public static void WriteTokenFile(string jsonResponse)
    {
        // Deserialize JSON response
        var tokenData = JsonSerializer.Deserialize(jsonResponse, SourceGenerationContext.Default.TokenResponse);

        // Store session expiration time
        tokenData?.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.ExpiresIn);

        // Write JSON to file
        File.WriteAllText("token.json", JsonSerializer.Serialize(tokenData, SourceGenerationContext.Default.TokenResponse));
    }

    /// <summary>
    /// Reads token data from a file.
    /// </summary>
    public static TokenResponse ReadTokenFile()
    {
        try
        {
            var jsonData = File.ReadAllText("token.json");
            var tokenData = JsonSerializer.Deserialize(jsonData, SourceGenerationContext.Default.TokenResponse);

            if (tokenData is not null) return tokenData;
        }
        catch
        {
            return new TokenResponse { AccessToken = string.Empty, TokenType = string.Empty };
        }

        // If null then return empty token data
        return new TokenResponse { AccessToken = string.Empty, TokenType = string.Empty };
    }
}
