using BeatLink.Web;

namespace BeatLink.Tests;

public static class PkceTests
{
    // RFC 7636 Appendix B test vector.
    private const string RfcVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string RfcChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Test("PKCE challenge matches RFC 7636 Appendix B vector")]
    public static void RfcVector()
    {
        Assert.Equal(RfcChallenge, Pkce.ChallengeFor(RfcVerifier));
    }

    [Test("PKCE Generate produces a self-consistent challenge/verifier pair")]
    public static void GenerateRoundTrip()
    {
        PkcePair pair = Pkce.Generate();
        Assert.Equal(pair.Challenge, Pkce.ChallengeFor(pair.Verifier));
    }

    [Test("PKCE output is base64url with no padding")]
    public static void Base64UrlShape()
    {
        PkcePair pair = Pkce.Generate();
        foreach (string s in new[] { pair.Challenge, pair.Verifier })
        {
            Assert.False(s.Contains('+'), "contains +");
            Assert.False(s.Contains('/'), "contains /");
            Assert.False(s.Contains('='), "contains =");
        }
    }

    [Test("PKCE verifier length is within RFC bounds (43-128 chars for 32 bytes)")]
    public static void VerifierLength()
    {
        PkcePair pair = Pkce.Generate();
        Assert.True(pair.Verifier.Length is >= 43 and <= 128, $"len={pair.Verifier.Length}");
    }
}
