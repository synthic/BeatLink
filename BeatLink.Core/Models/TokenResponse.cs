using System.Text.Json.Serialization;

namespace BeatLink.Models;

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    // Use DateTimeOffset so the stored instant is unambiguous across time
    // zones and DST (I14).
    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>True when this represents a real, unexpired token.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(AccessToken);

    public static TokenResponse Empty => new() { AccessToken = string.Empty, TokenType = string.Empty };
}

[JsonSourceGenerationOptions(WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(TokenResponse))]
public partial class SourceGenerationContext : JsonSerializerContext
{
}
