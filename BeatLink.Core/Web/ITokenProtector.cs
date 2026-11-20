namespace BeatLink.Web;

/// <summary>
/// Encrypts and decrypts the token blob at rest (S3). The Windows app supplies
/// a DPAPI-backed implementation; tests use a passthrough. Keeping this out of
/// <see cref="TokenStore"/> lets the store live in the platform-neutral core.
/// </summary>
public interface ITokenProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>
/// No-op protector. Used in tests and as a last-resort fallback; never the
/// default on Windows, where DPAPI is available.
/// </summary>
public sealed class PassthroughTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] plaintext) => plaintext;
    public byte[] Unprotect(byte[] ciphertext) => ciphertext;
}
