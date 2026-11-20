using System.Text;

namespace BeatLink.Game;

/// <summary>
/// The data-driven patch table (I2). Replaces the seven inline
/// <c>WriteProcessMemory</c> calls in the old <c>Memory.ApplyPatches</c> with a
/// single list that carries a name, an offset from the module base, a write
/// ordering class, the expected original bytes and the replacement bytes.
///
/// Offsets are relative to the module base (B4). Replacement bytes for the code
/// patches are the same values the original source wrote; the data patches
/// (token buffer, pointer, hostname) are built at runtime. Expected bytes come
/// from <see cref="ExpectedBytes"/>, captured per build.
/// </summary>
public static class Patches
{
    // Canonical patch names (also the keys in the captured expected-bytes file).
    public const string TokenBuffer = "Auth token buffer";
    public const string TokenPointer = "Auth token pointer";
    public const string RedirectorHostname = "Redirector hostname";
    public const string ProtoSslConnect = "ProtoSSL connect";
    public const string LsxAuthCheck = "LSX auth check";
    public const string AuthCodeReference = "Auth code reference";
    public const string EncryptionBypass = "Encryption bypass";

    // Offsets from the module base for the build the review covered (image base
    // 0x140000000). Expressed as RVAs so the absolute address is computed from
    // the live module base at runtime (B4).
    private const nint RvaTokenBuffer = 0x1C3B340;
    private const nint RvaTokenPointer = 0x1C3B300;
    private const nint RvaRedirectorHostname = 0x1D80890;
    private const nint RvaProtoSslConnect = 0x2DBE9B0;
    private const nint RvaLsxAuthCheck = 0x388BCE6;
    private const nint RvaAuthCodeReference = 0x388AED3;
    private const nint RvaEncryptionBypass = 0x39C0D81;

    // Replacement bytes carried over verbatim from the original source.
    private static readonly byte[] ReplTokenPointer = [0x40, 0xB3, 0xC3, 0x41, 0x01, 0x00, 0x00, 0x00]; // full 8 bytes (B9)
    private static readonly byte[] ReplProtoSslConnect = [0x31];
    private static readonly byte[] ReplLsxAuthCheck = [0x48, 0x90];
    private static readonly byte[] ReplAuthCodeReference = [0x15, 0x28, 0x04, 0x3B, 0xFE];
    private static readonly byte[] ReplEncryptionBypass = [0xE9, 0xC7, 0x00];

    /// <summary>The patch names that must have captured expected bytes for the current options.</summary>
    public static IEnumerable<string> RequiredNames(BeatLinkOptions options)
    {
        yield return TokenBuffer;
        yield return TokenPointer;
        yield return RedirectorHostname;
        yield return ProtoSslConnect;
        yield return LsxAuthCheck;
        yield return AuthCodeReference;
        if (options.DisableEncryption)
            yield return EncryptionBypass;
    }

    /// <summary>
    /// Builds the ordered patch set for the given token and configuration.
    /// </summary>
    /// <exception cref="PatchBuildException">
    /// The host does not fit the captured region, or a token is too large (B8, B20).
    /// </exception>
    public static IReadOnlyList<Patch> Build(BeatLinkOptions options, string token, ExpectedBytes expected)
    {
        var list = new List<Patch>();

        // --- Data (written first) ---

        // Token buffer: a zero-filled region exactly the captured size, with the
        // token copied in and the remainder (including a terminator) left zero
        // (B8). Reject a token that does not fit.
        byte[] bufExpected = expected.Get(TokenBuffer);
        int bufSize = bufExpected.Length;
        byte[] tokenBytes = Encoding.ASCII.GetBytes(token);
        if (bufSize > 0 && tokenBytes.Length >= bufSize)
            throw new PatchBuildException(
                $"Auth token is too long for the {bufSize}-byte buffer (needs a terminator).");
        byte[] bufReplacement = new byte[bufSize];
        if (bufSize > 0)
            Array.Copy(tokenBytes, bufReplacement, tokenBytes.Length);
        list.Add(new Patch(TokenBuffer, RvaTokenBuffer, PatchKind.Data, bufExpected, bufReplacement));

        // Redirector hostname: ASCII, must fit the captured region with a null
        // terminator; the rest is zero-padded (B20).
        byte[] hostExpected = expected.Get(RedirectorHostname);
        int hostSize = hostExpected.Length;
        byte[] hostAscii = Encoding.ASCII.GetBytes(options.ServerHost);
        if (hostSize > 0 && hostAscii.Length + 1 > hostSize)
            throw new PatchBuildException(
                $"Server host \"{options.ServerHost}\" does not fit the {hostSize}-byte " +
                "redirector field with a null terminator.");
        byte[] hostReplacement = new byte[hostSize];
        if (hostSize > 0)
            Array.Copy(hostAscii, hostReplacement, hostAscii.Length);
        list.Add(new Patch(RedirectorHostname, RvaRedirectorHostname, PatchKind.Data, hostExpected, hostReplacement));

        // --- Pointer (written second) ---
        list.Add(new Patch(TokenPointer, RvaTokenPointer, PatchKind.Pointer,
            expected.Get(TokenPointer), ReplTokenPointer));

        // --- Code (written last) ---
        list.Add(new Patch(ProtoSslConnect, RvaProtoSslConnect, PatchKind.Code,
            expected.Get(ProtoSslConnect), ReplProtoSslConnect));
        list.Add(new Patch(LsxAuthCheck, RvaLsxAuthCheck, PatchKind.Code,
            expected.Get(LsxAuthCheck), ReplLsxAuthCheck));
        list.Add(new Patch(AuthCodeReference, RvaAuthCodeReference, PatchKind.Code,
            expected.Get(AuthCodeReference), ReplAuthCodeReference));

        if (options.DisableEncryption)
            list.Add(new Patch(EncryptionBypass, RvaEncryptionBypass, PatchKind.Code,
                expected.Get(EncryptionBypass), ReplEncryptionBypass));

        return list;
    }

    /// <summary>The RVAs to sample when capturing expected bytes, with each region's length.</summary>
    public static IReadOnlyList<(string Name, nint Rva, int Length)> CaptureLayout(BeatLinkOptions options)
    {
        var layout = new List<(string, nint, int)>
        {
            (TokenBuffer, RvaTokenBuffer, 256),
            (RedirectorHostname, RvaRedirectorHostname, 30),
            (TokenPointer, RvaTokenPointer, 8),
            (ProtoSslConnect, RvaProtoSslConnect, ReplProtoSslConnect.Length),
            (LsxAuthCheck, RvaLsxAuthCheck, ReplLsxAuthCheck.Length),
            (AuthCodeReference, RvaAuthCodeReference, ReplAuthCodeReference.Length),
        };
        if (options.DisableEncryption)
            layout.Add((EncryptionBypass, RvaEncryptionBypass, ReplEncryptionBypass.Length));
        return layout;
    }
}

public sealed class PatchBuildException : Exception
{
    public PatchBuildException(string message) : base(message) { }
}
