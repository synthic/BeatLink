using System.Text;
using BeatLink.Models;
using BeatLink.Web;

namespace BeatLink.Tests;

public static class TokenStoreTests
{
    private static (TokenStore store, string path) NewStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "beatlink-test-" + Guid.NewGuid().ToString("N"), "token.json");
        return (new TokenStore(path, new PassthroughTokenProtector()), path);
    }

    [Test("Token round-trips and expiry is stamped from expires_in")]
    public static void RoundTrip()
    {
        (TokenStore store, _) = NewStore();
        TokenResponse written = store.Write("""{"access_token":"abc","token_type":"Bearer","expires_in":3600}""");

        Assert.Equal("abc", written.AccessToken);
        Assert.True(written.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50), "expiry not stamped");

        (TokenStore.ReadStatus status, TokenResponse? read, _) = store.TryRead();
        Assert.Equal(TokenStore.ReadStatus.Ok, status);
        Assert.Equal("abc", read!.AccessToken);
    }

    [Test("A null/empty token response throws rather than persisting 'null' (B21)")]
    public static void NullResponseThrows()
    {
        (TokenStore store, string path) = NewStore();
        Assert.Throws<InvalidOperationException>(() => store.Write("null"));
        Assert.Throws<InvalidOperationException>(() => store.Write("""{"token_type":"Bearer"}"""));
        Assert.False(File.Exists(path), "no file should be written on failure");
    }

    [Test("A corrupt token file reads as Unreadable, not NotPresent (B21)")]
    public static void CorruptFileIsUnreadable()
    {
        (TokenStore store, string path) = NewStore();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("}{ not json"));

        (TokenStore.ReadStatus status, _, _) = store.TryRead();
        Assert.Equal(TokenStore.ReadStatus.Unreadable, status);
    }

    [Test("Missing token file reads as NotPresent")]
    public static void MissingFileIsNotPresent()
    {
        (TokenStore store, _) = NewStore();
        (TokenStore.ReadStatus status, _, _) = store.TryRead();
        Assert.Equal(TokenStore.ReadStatus.NotPresent, status);
    }

    [Test("Clear deletes the stored token (S7 Log Out)")]
    public static void ClearDeletes()
    {
        (TokenStore store, string path) = NewStore();
        store.Write("""{"access_token":"abc","token_type":"Bearer","expires_in":3600}""");
        Assert.True(File.Exists(path), "precondition: file exists");
        store.Clear();
        Assert.False(File.Exists(path), "file should be gone after Clear");
    }

    [Test("Protector is applied: stored bytes are not the raw JSON")]
    public static void ProtectorIsUsed()
    {
        string path = Path.Combine(Path.GetTempPath(), "beatlink-test-" + Guid.NewGuid().ToString("N"), "token.json");
        var store = new TokenStore(path, new XorProtector(0x5A));
        store.Write("""{"access_token":"secret","token_type":"Bearer","expires_in":3600}""");

        string onDisk = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.False(onDisk.Contains("secret"), "token leaked to disk in clear text");

        (TokenStore.ReadStatus status, TokenResponse? read, _) = store.TryRead();
        Assert.Equal(TokenStore.ReadStatus.Ok, status);
        Assert.Equal("secret", read!.AccessToken);
    }

    /// <summary>Stand-in for DPAPI in tests: reversible, proves the seam works.</summary>
    private sealed class XorProtector : ITokenProtector
    {
        private readonly byte _key;
        public XorProtector(byte key) => _key = key;
        public byte[] Protect(byte[] plaintext) => Xor(plaintext);
        public byte[] Unprotect(byte[] ciphertext) => Xor(ciphertext);
        private byte[] Xor(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) result[i] = (byte)(data[i] ^ _key);
            return result;
        }
    }
}
