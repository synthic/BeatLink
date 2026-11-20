using System.Security.Cryptography;

namespace BeatLink.Web;

/// <summary>
/// Encrypts the token blob with Windows DPAPI under the current-user scope (S3),
/// so only the signed-in user on this machine can read it.
/// </summary>
public sealed class DpapiTokenProtector : ITokenProtector
{
    private static readonly byte[] Entropy = "BeatLink.token.v1"u8.ToArray();

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
