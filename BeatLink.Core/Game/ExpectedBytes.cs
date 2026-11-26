using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatLink.Game;

/// <summary>
/// The original ("expected") bytes at each patch location, optionally captured
/// once from an unpatched game (<c>--capture</c>, see
/// <see cref="GamePatcher.Capture"/>). When present, the planner uses them to
/// verify each site before writing (B4) and to tell "not patched" from
/// "already patched" (I3). When absent, patches are applied unverified, which is
/// fine for the single supported game build.
/// </summary>
public sealed class ExpectedBytes
{
    [JsonPropertyName("build")]
    public string Build { get; set; } = "";

    /// <summary>Patch name → original bytes (hex string).</summary>
    [JsonPropertyName("bytes")]
    public Dictionary<string, string> Hex { get; set; } = new();

    public bool TryGet(string name, out byte[] bytes)
    {
        if (Hex.TryGetValue(name, out string? hex) && !string.IsNullOrEmpty(hex))
        {
            bytes = Convert.FromHexString(hex);
            return true;
        }
        bytes = [];
        return false;
    }

    public byte[] Get(string name) => TryGet(name, out byte[] b) ? b : [];

    public static ExpectedBytes LoadOrEmpty(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(json, ExpectedBytesContext.Default.ExpectedBytes)
                       ?? new ExpectedBytes();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
        {
            // Fall through to empty; the planner will report an invalid/incomplete table.
        }
        return new ExpectedBytes();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(this, ExpectedBytesContext.Default.ExpectedBytes);
        File.WriteAllText(path, json);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ExpectedBytes))]
internal partial class ExpectedBytesContext : JsonSerializerContext;
